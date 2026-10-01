using UnityEngine;

/// <summary>
/// 方块交互：准星射线选中 → 左键挖、右键放，并管理鼠标锁定与选中框。
/// </summary>
public class VoxelInteraction : MonoBehaviour
{
    public float reach = 6f;

    /// <summary>可用方块（对应按 1-4）</summary>
    public static readonly byte[] Palette = { VoxelWorld.Grass, VoxelWorld.Dirt, VoxelWorld.Stone, VoxelWorld.Sand };
    static readonly string[] PaletteNames = { "草", "土", "石", "沙" };

    VoxelWorld _world;
    Transform _camera;
    Transform _highlight;
    CharacterController _controller;
    int _selected;

    public VoxelWorld World => _world;
    public string SelectedName => PaletteNames[_selected];
    public int SelectedIndex => _selected;

    public void Init(VoxelWorld world, Transform cameraTransform, Transform highlight)
    {
        _world = world;
        _camera = cameraTransform;
        _highlight = highlight;
        _controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (_world == null || _camera == null) return;

        HandleCursor();

        for (int i = 0; i < Palette.Length; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) _selected = i;
        }

        bool aiming = TryPick(out Vector3Int block, out Vector3Int normal);
        UpdateHighlight(aiming, block);

        if (Cursor.lockState != CursorLockMode.Locked) return;

        if (aiming)
        {
            if (Input.GetMouseButtonDown(0)) Break(block);
            else if (Input.GetMouseButtonDown(1)) Place(block, normal);
        }
    }

    void HandleCursor()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            // 第一次点击只负责锁定鼠标，这一帧不挖方块
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                Cursor.lockState = CursorLockMode.Locked;
        }
        else if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
        }

        Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
    }

    /// <summary>从相机沿视线打一条射线，命中则回推 0.5 格得到方块坐标</summary>
    bool TryPick(out Vector3Int block, out Vector3Int normal)
    {
        block = default;
        normal = default;

        Ray ray = new Ray(_camera.position, _camera.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, reach)) return false;

        Vector3 p = hit.point - hit.normal * 0.5f;
        block = new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
        normal = new Vector3Int(Mathf.RoundToInt(hit.normal.x), Mathf.RoundToInt(hit.normal.y), Mathf.RoundToInt(hit.normal.z));
        return true;
    }

    void UpdateHighlight(bool aiming, Vector3Int block)
    {
        if (_highlight == null) return;
        _highlight.gameObject.SetActive(aiming);
        if (aiming) _highlight.position = new Vector3(block.x + 0.5f, block.y + 0.5f, block.z + 0.5f);
    }

    void Break(Vector3Int block)
    {
        if (_world.GetBlock(block.x, block.y, block.z) == VoxelWorld.Air) return;
        if (block.y <= 0) return;                       // 最底层当基岩，不许挖穿
        _world.SetBlock(block.x, block.y, block.z, VoxelWorld.Air);
        _world.Rebuild();
    }

    void Place(Vector3Int block, Vector3Int normal)
    {
        Vector3Int target = block + normal;

        if (_world.GetBlock(target.x, target.y, target.z) != VoxelWorld.Air) return;
        if (target.y < 0 || target.y >= VoxelWorld.SizeY) return;
        if (target.x < 0 || target.z < 0 || target.x >= VoxelWorld.SizeX || target.z >= VoxelWorld.SizeZ) return;
        if (OverlapsPlayer(target)) return;             // 别把自己封进方块里

        _world.SetBlock(target.x, target.y, target.z, Palette[_selected]);
        _world.Rebuild();
    }

    bool OverlapsPlayer(Vector3Int b)
    {
        if (_controller == null) return false;
        Bounds blockBounds = new Bounds(new Vector3(b.x + 0.5f, b.y + 0.5f, b.z + 0.5f), Vector3.one);
        return _controller.bounds.Intersects(blockBounds);
    }
}
