using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家交互（开放世界版）：只管鼠标锁定、手上的道具、Q 键丢道具和 R 键吃兽肉。
/// 方块世界那套挖 / 放已经去掉——现在的地形是平滑网格，没有格子可挖。
/// 另外这里也存着不占手上格子的收获：Andy的币（宝箱开出来的通用货币）和各种素材 ——
/// 狩猎野生动物掉下来的兽肉也走这一栏（按 R 吃掉回饱食度，见 EatMeat）。
/// </summary>
public class VoxelInteraction : MonoBehaviour
{
    /// <summary>原石：不是方块，只存在于物品栏，按 Q 丢出来（值故意远超方块 id 范围）</summary>
    public const byte Primogem = 200;

    /// <summary>纠缠之缘：抽卡用的道具，同样不是方块（值故意远超方块 id 范围）</summary>
    public const byte IntertwinedFate = 201;

    /// <summary>兽肉：猎野生动物掉下来的食材。它按"素材"记（不占手上的格子），按 R 吃一块</summary>
    public const string MeatName = "兽肉";

    /// <summary>一块兽肉回多少饱食度（三块回满，别让狩猎把 /eat 完全替掉）</summary>
    public const float MeatHunger = 35f;

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
        // 有面板占着输入（对话框 / 祈愿 / 大地图 / 背包 / 角色 / 菜单 / 开幕动画）就不响应按键和鼠标
        if (VoxelPanels.InputBlocked) return;

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

        // R 吃一块兽肉（狩猎的收益：回饱食度）
        if (Input.GetKeyDown(KeyCode.R)) EatMeat();
    }

    void HandleCursor()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            // 第一次点击只负责锁定鼠标
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                Cursor.lockState = CursorLockMode.Locked;
        }
        // ★ 这里原来还有一条"按 Esc 就松开鼠标"。去掉它：Esc 现在归面板（VoxelMenu 开菜单、各面板关自己），
        //   面板在同一帧里刚把自己关掉并把鼠标锁回去之后，这里会因为"同一个 Esc 还在按下"再把鼠标松开 ——
        //   结果就是鼠标永远是松的：玩家不能移动／不能交互，而 HUD 又要求"鼠标锁定"才画，看起来就是整个卡死。
        //   测试：打开大地图（M）后按 Esc 关掉，就会复现。

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

    /// <summary>
    /// R 键：吃一块兽肉回饱食度。兽肉只有猎野生动物才有（见 VoxelAnimal / AnimalSpawner），
    /// 所以这条把"野外打猎"和"饱食度 / 疾跑 / 回血"串成了一条闭环。
    /// </summary>
    void EatMeat()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        if (MaterialCount(MeatName) <= 0)
        {
            VoxelChat.Announce("没有" + MeatName + "（野外的小动物会掉：兔子 1 块、鹿 3 块、野猪 4 块）");
            return;
        }

        var stats = VoxelStats.Instance;
        if (stats == null) return;

        if (!stats.EatFood(MeatHunger, MeatName)) return;      // 已经吃饱了，别浪费一块肉

        SpendMaterial(MeatName, 1);
        VoxelAudio.PlaySfx("heal", 0.5f, 1.1f);
    }

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

    /// <summary>消耗素材（吃肉时扣掉一块）；不够扣就返回 false，什么都不改。
    /// 注意这里**不**把名字从获得顺序里删掉：数量归零之后 HUD / 背包照样列着"兽肉 ×0"，
    /// 免得玩家"手里那块肉吃完了，列表也跟着消失"看不出自己有过多少</summary>
    public bool SpendMaterial(string name, int amount)
    {
        if (string.IsNullOrEmpty(name) || amount <= 0) return false;

        int have = MaterialCount(name);
        if (have < amount) return false;

        _materials[name] = have - amount;
        return true;
    }

    /// <summary>拿到过的素材名字，按获得顺序（HUD 照着列）</summary>
    public IReadOnlyList<string> MaterialNames => _materialOrder;
}
