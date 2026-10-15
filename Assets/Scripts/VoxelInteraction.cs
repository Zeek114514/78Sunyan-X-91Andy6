using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家交互（开放世界版）：只管鼠标锁定、手上的道具和 Q 键丢道具。
/// 方块世界那套挖 / 放已经去掉——现在的地形是平滑网格，没有格子可挖。
/// 另外这里也存着不占手上格子的收获：Andy的币（宝箱开出来的通用货币）和各种素材。
/// </summary>
public class VoxelInteraction : MonoBehaviour
{
    /// <summary>原石：不是方块，只存在于物品栏，按 Q 丢出来（值故意远超方块 id 范围）</summary>
    public const byte Primogem = 200;

    /// <summary>纠缠之缘：抽卡用的道具，同样不是方块（值故意远超方块 id 范围）</summary>
    public const byte IntertwinedFate = 201;

    /// <summary>物品栏里的原石数量</summary>
    public int PrimogemCount = 16;

    /// <summary>物品栏里的纠缠之缘数量（抽一次卡花 1 个）</summary>
    public int FateCount = 10;

    /// <summary>Andy的币：战利品宝箱开出来的通用货币，不占手上的格子，只在 HUD 右上角看得到</summary>
    public int AndyCoinCount;

    /// <summary>素材：名字 → 数量（宝箱开出来的材料，同样不占手上的格子）</summary>
    readonly Dictionary<string, int> _materials = new Dictionary<string, int>();

    /// <summary>素材的获得顺序（HUD 照着列，顺序稳定）</summary>
    readonly List<string> _materialOrder = new List<string>();

    /// <summary>物品栏：原石 / 纠缠之缘（Air 表示空手）</summary>
    public static readonly byte[] Palette = { Primogem, IntertwinedFate };

    static readonly string[] PaletteNames = { "原石", "纠缠之缘" };

    OpenWorld _world;
    Transform _camera;
    CharacterController _controller;
    int _selected;

    public string SelectedName => _selected < PaletteNames.Length ? PaletteNames[_selected] : "空手";
    public int SelectedIndex => _selected;

    public void Init(OpenWorld world, Transform cameraTransform)
    {
        _world = world;
        _camera = cameraTransform;
        _controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (_camera == null) return;
        // 对话框 / 祈愿页面 / 大地图 / 背包 / 角色界面 / 开幕动画打开时不响应按键和鼠标（大地图自己管鼠标锁）
        if (VoxelChat.IsOpen || VoxelGacha.IsOpen || WorldMap.IsOpen || VoxelInventory.IsOpen ||
            VoxelCharacterScreen.IsOpen || TitleScreen.IsOpen || VoxelMenu.IsOpen) return;

        HandleCursor();

        // 1-9 选道具
        for (int i = 0; i < Palette.Length; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) _selected = i;
        }

        // 滚轮切格：向上滚 = 左移一格，向下滚 = 右移一格（循环）
        float wheel = Input.GetAxisRaw("Mouse ScrollWheel");
        if (wheel > 0f)      _selected = (_selected - 1 + Palette.Length) % Palette.Length;
        else if (wheel < 0f) _selected = (_selected + 1) % Palette.Length;

        // Q 丢出当前选中的道具
        if (Input.GetKeyDown(KeyCode.Q)) DropSelected();
    }

    void HandleCursor()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            // 第一次点击只负责锁定鼠标
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                Cursor.lockState = CursorLockMode.Locked;
        }
        else if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
        }

        Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
    }

    /// <summary>把选中格里的道具丢到面前（Q 键）：原石和纠缠之缘都能丢</summary>
    void DropSelected()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        byte item = Palette[_selected];
        Vector3 from = _camera.position + _camera.forward * 1.3f;

        if (item == Primogem)
        {
            if (PrimogemCount <= 0) return;

            PrimogemCount--;
            VoxelGem.Spawn(_world, transform, this, from, VoxelGem.Kind.Primogem);
        }
        else if (item == IntertwinedFate)
        {
            if (FateCount <= 0) return;

            FateCount--;
            VoxelGem.Spawn(_world, transform, this, from, VoxelGem.Kind.Fate);
        }
    }

    /// <summary>玩家捡回原石时加数量（VoxelGem 调用）</summary>
    public void AddPrimogem(int amount) => PrimogemCount += amount;

    /// <summary>玩家捡回纠缠之缘时加数量（VoxelGem 调用）</summary>
    public void AddFate(int amount) => FateCount += amount;

    // ---------- Andy的币 / 素材（战利品宝箱的奖励） ----------

    /// <summary>开宝箱得到的 Andy的币</summary>
    public void AddAndyCoin(int amount)
    {
        if (amount > 0) AndyCoinCount += amount;
    }

    /// <summary>开宝箱得到的素材</summary>
    public void AddMaterial(string name, int amount)
    {
        if (string.IsNullOrEmpty(name) || amount <= 0) return;

        if (!_materials.ContainsKey(name))
        {
            _materials[name] = 0;
            _materialOrder.Add(name);          // 第一次拿到，记一笔顺序
        }
        _materials[name] += amount;
    }

    /// <summary>某种素材有多少</summary>
    public int MaterialCount(string name) => _materials.TryGetValue(name, out int n) ? n : 0;

    /// <summary>拿到过的素材名字，按获得顺序（HUD 照着列）</summary>
    public IReadOnlyList<string> MaterialNames => _materialOrder;
}
