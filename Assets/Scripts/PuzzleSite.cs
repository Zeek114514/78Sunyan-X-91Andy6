using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一处解密场地的运行时（几何在 WildPuzzles 里，选点在 PuzzleSpawner 里）。
///
/// 三种模式（PuzzleKind）：
///   AllLit   全部点亮：场地上一圈元素方碑，走到跟前按 E 逐根点亮，全亮即解。
///   Ordered  按顺序点亮：每根方碑顶端有一颗不同颜色的宝石；入口的提示石碑从上到下刻着要求的顺序。
///                        必须按这个顺序点亮，"点错"会让所有方碑一起熄灭、从头再来。
///   Guarded  解密 + 守护战：点亮所有方碑之后，中央机关台上会开出宝箱，同时冒出守卫怪；
///                        守卫是被"封印"的（复用 LootChest 那套），得把怪清掉才能开箱。
///
/// 【点亮怎么做的】方碑"暗"和"亮"是两个网格，直接换 MeshFilter.sharedMesh + 切换宝石子物体，
/// 不碰材质 —— 世界的着色器（Voxel/Demo）是自定义顶点色的，往里塞发光会牵动整套渲染。
/// 交互判定（距离 + 准星朝向 + E 键 + 屏幕提示）和 LootChest 保持一致，玩家不用学第二套操作。
/// </summary>
public class PuzzleSite : MonoBehaviour
{
    /// <summary>场上所有解密场地</summary>
    public static readonly List<PuzzleSite> All = new List<PuzzleSite>();

    const float UseRadius  = 3.2f;      // 走到这么近才能按 E
    const float FocusDot   = 0.42f;     // 准星大致朝着方碑才算"看着它"
    const float ShowRadius = 30f;       // 多远开始飘场地名字

    /// <summary>一根方碑</summary>
    sealed class Node
    {
        public Transform  root;
        public MeshFilter body;          // 柱身（暗 / 亮两个网格互切）
        public GameObject gemDim;        // 顶端宝石：暗
        public GameObject gemLit;        // 顶端宝石：亮
        public int   colorIndex;         // 宝石颜色（Ordered 模式靠它对应提示石碑）
        public bool  on;
    }

    OpenWorld _world;
    Transform _player;
    VoxelInteraction _inventory;
    Material _material;

    PuzzleKind _kind;
    PuzzleStyle _style;              // 外形风格：蒙德方碑 / 璃月石灯阵 / 璃月石鼓（玩法完全一样）
    LootChest.Tier _tier;
    Vector3 _center;
    float _yaw;
    int _seed;
    int[] _order;                    // Ordered：要求的点亮顺序（存的是 colorIndex）
    int _progress;                   // Ordered：已经按顺序点亮了几根
    bool _solved;

    readonly List<Node> _nodes = new List<Node>();
    readonly List<VoxelMonster> _guards = new List<VoxelMonster>();
    LootChest _reward;

    string _flash;                   // 顺序点错之类的一次性提示
    float _flashUntil;
    GUIStyle _nameStyle, _promptStyle;

    public PuzzleKind Kind => _kind;
    public bool Solved => _solved;

    // ---------- 初始化 ----------

    public void Init(OpenWorld world, Transform player, VoxelInteraction inventory, Material material,
                     PuzzleKind kind, LootChest.Tier tier, Vector3 center, float yaw, int seed,
                     PuzzleStyle style = PuzzleStyle.Monolith)
    {
        _world = world;
        _player = player;
        _inventory = inventory;
        _material = material;
        _kind = kind;
        _style = style;
        _tier = tier;
        _center = center;
        _yaw = yaw;
        _seed = seed;

        _order = WildPuzzles.RequiredOrder(kind, 0f, seed);

        BuildNodes();
        BuildRewardChest();
    }

    /// <summary>
    /// 把奖励宝箱**一开始就摆在中央机关台上**，但用机关锁锁住 —— 玩家走过来能看见箱子、
    /// 知道这里有目标，准星对上去显示"机关未启动 · 先点亮场地里的元素方碑"；
    /// 解开之后 SetLocked(false) 就能开了。
    ///
    /// ★ 上一版是"解开之后才生成宝箱"，玩家在场地里看不到任何奖励目标，不知道在解什么。
    /// ★ 位置用 WildPuzzles.RewardPos（机关台顶面）。LootChest.Init 现在是"只往上抬、不往下压"，
    ///   所以不会被吸到地面上、埋进机关台里。
    /// </summary>
    void BuildRewardChest()
    {
        Vector3 at = WildPuzzles.RewardPos(_center);

        var go = new GameObject("PuzzleReward_" + _tier);
        go.transform.SetParent(transform, false);
        go.transform.position = at;

        _reward = go.AddComponent<LootChest>();
        _reward.Init(_world, _player, _inventory, _material, _tier, _yaw + 90f);
        _reward.SetLocked(true);
    }

    void OnEnable()  => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// <summary>在石台上立起一圈节点（方碑 / 石灯 / 石鼓）</summary>
    void BuildNodes()
    {
        int n = WildPuzzles.NodeCount(_kind);
        float yaw = _yaw;
        float ring = WildPuzzles.RingRadius(_kind, _style);

        for (int i = 0; i < n; i++)
        {
            Vector3 p = WildPuzzles.NodePos(_center, yaw, n, ring, i);
            var root = new GameObject($"Monolith{i}").transform;
            root.SetParent(transform, false);
            root.position = new Vector3(p.x, _world.SurfaceY(p.x, p.z), p.z);
            root.rotation = Quaternion.Euler(0f, WildPuzzles.NodeYaw(_center, p), 0f);

            var body = new GameObject("Body");
            body.transform.SetParent(root, false);
            var filter = body.AddComponent<MeshFilter>();
            filter.sharedMesh = WildPuzzles.NodeMesh(_style, false);
            body.AddComponent<MeshRenderer>().sharedMaterial = _material;
            body.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;   // 节点要能挡人

            // 顶端那点"身份色"：暗 / 亮两个，互相切换。暗的也有颜色，不然对不上提示
            var node = new Node { root = root, body = filter, colorIndex = i, on = false };

            node.gemDim = MakeGem(root, WildPuzzles.GlowMesh(_style, i, false));
            node.gemLit = MakeGem(root, WildPuzzles.GlowMesh(_style, i, true));
            node.gemLit.SetActive(false);

            _nodes.Add(node);
        }
    }

    GameObject MakeGem(Transform parent, Mesh mesh)
    {
        var go = new GameObject("Gem");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = _material;
        return go;
    }

    // ---------- 主循环 ----------

    void Update()
    {
        if (_world == null) return;

        // 守护模式：守卫清光了，宝箱上的封印由 LootChest 自己解，这里不用管

        if (_solved || !CanUseInput()) return;
        if (!FocusedNode(out var node)) return;

        if (Input.GetKeyDown(KeyCode.E)) Light(node);
    }

    static bool CanUseInput() => VoxelPanels.CanUseInput;

    /// <summary>玩家是不是站在某根方碑跟前盯着它</summary>
    bool FocusedNode(out Node found)
    {
        found = null;
        if (_player == null) return false;

        var cam = Camera.main;
        if (cam == null) return false;

        float best = float.MaxValue;

        foreach (var node in _nodes)
        {
            if (node.on && _kind != PuzzleKind.Ordered) continue;   // 已经亮了的不用再点（顺序模式还要看提示）

            Vector3 to = node.root.position - _player.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > UseRadius) continue;

            Vector3 dir = (node.root.position + Vector3.up * 1.6f) - cam.transform.position;
            bool looking = dir.magnitude < 1.2f ||
                           Vector3.Dot(cam.transform.forward, dir.normalized) > FocusDot;
            if (!looking) continue;

            if (dist < best) { best = dist; found = node; }
        }

        return found != null;
    }

    // ---------- 点亮 ----------

    void Light(Node node)
    {
        if (node.on) return;

        if (_kind == PuzzleKind.Ordered)
        {
            // 要求的下一根是不是它
            if (_order[_progress] != node.colorIndex)
            {
                Flash($"顺序不对 —— {Noun()}全部熄灭，重新来过");
                ResetAll();
                return;
            }
            _progress++;
        }

        SetNode(node, true);

        if (_kind == PuzzleKind.Ordered && _progress >= _nodes.Count)
            Solve();
        else if (_kind != PuzzleKind.Ordered && AllOn())
            Solve();
    }

    void SetNode(Node node, bool on)
    {
        node.on = on;
        node.body.sharedMesh = WildPuzzles.NodeMesh(_style, on);
        node.gemDim.SetActive(!on);
        node.gemLit.SetActive(on);
    }

    void ResetAll()
    {
        foreach (var node in _nodes) SetNode(node, false);
        _progress = 0;
    }

    bool AllOn()
    {
        foreach (var node in _nodes) if (!node.on) return false;
        return true;
    }

    void Flash(string text, float seconds = 2.5f)
    {
        _flash = text;
        _flashUntil = Time.time + seconds;
    }

    // ---------- 解开 ----------

    void Solve()
    {
        _solved = true;

        // 宝箱从一开场就摆在机关台上了，这里只是把机关锁打开
        if (_reward != null)
        {
            _reward.SetLocked(false);
            VoxelChat.Announce(SiteName(_kind, _style) + " 机关启动，中央的宝箱可以打开了");
        }

        // 守护模式：机关启动的同时冒出守卫，宝箱被封印，得连怪一起清掉
        int guards = _kind == PuzzleKind.Guarded ? SpawnGuards() : 0;
        if (guards > 0 && _reward != null) _reward.Seal();

        Debug.Log($"[PuzzleSite] {SiteName(_kind, _style)} ({_center.x:0},{_center.z:0}) 已解开：" +
                  $"奖励 {LootChest.TierName(_tier)}，守卫 {guards} 只");
    }

    /// <summary>围着场地刷一圈守卫（位置算法和 LootChestSpawner 那套一致）</summary>
    int SpawnGuards()
    {
        int wanted = 3 + (_tier >= LootChest.Tier.Advanced ? 1 : 0);
        float ring = WildPuzzles.PlatformRadius(_kind, _style) + 1.5f;
        int spawned = 0;

        for (int i = 0; i < wanted; i++)
        {
            float a = (i * 360f / wanted + _yaw) * Mathf.Deg2Rad;
            float gx = _center.x + Mathf.Cos(a) * ring;
            float gz = _center.z + Mathf.Sin(a) * ring;

            if (!OpenWorld.InBounds(gx, gz)) continue;
            if (OpenWorld.Height(gx, gz) < OpenWorld.WaterLevel + 0.6f) continue;

            var go = new GameObject("PuzzleGuard");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(gx, _world.SurfaceY(gx, gz) + 0.3f, gz);

            var monster = go.AddComponent<VoxelMonster>();
            monster.Init(_material, _player, _world, _inventory,
                         i == 0 ? VoxelMonster.Kind.HilichurlBrute : VoxelMonster.Kind.Hilichurl);

            if (_reward != null) _reward.AddGuard(monster);
            _guards.Add(monster);
            spawned++;
        }

        return spawned;
    }

    // ---------- 提示 UI ----------

    void OnGUI()
    {
        if (_player == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        float dist = Vector3.Distance(_center + Vector3.up * 2f, cam.transform.position);
        bool near = dist <= ShowRadius;
        bool flash = Time.time < _flashUntil;

        if (!near && !flash) return;

        EnsureStyles();

        if (near)
        {
            // 场地名字（飘在场地正上方）
            Vector3 head = _center + Vector3.up * 5.2f;
            Vector3 sp = cam.WorldToScreenPoint(head);
            if (sp.z > 0f)
            {
                float alpha = Mathf.Clamp01((ShowRadius - dist) / 6f);
                _nameStyle.normal.textColor = new Color(1f, 0.94f, 0.78f, alpha);
                VoxelHud.LabelShadow(new Rect(sp.x - 140f, Screen.height - sp.y - 12f, 280f, 22f),
                                     SiteName(_kind, _style), _nameStyle);
            }

            // 操作提示
            float y = Screen.height * 0.5f + 54f;
            if (flash)
            {
                _promptStyle.normal.textColor = new Color(1f, 0.62f, 0.55f, 0.98f);
                VoxelHud.LabelShadow(new Rect(Screen.width * 0.5f - 240f, y - 28f, 480f, 26f), _flash, _promptStyle);
            }

            if (!_solved && CanUseInput() && FocusedNode(out _))
            {
                _promptStyle.normal.textColor = new Color(1f, 1f, 1f, 0.96f);
                VoxelHud.LabelShadow(new Rect(Screen.width * 0.5f - 240f, y, 480f, 26f), Hint(), _promptStyle);
            }
        }
        else if (flash)
        {
            _promptStyle.normal.textColor = new Color(1f, 0.62f, 0.55f, 0.98f);
            VoxelHud.LabelShadow(new Rect(Screen.width * 0.5f - 240f, Screen.height * 0.5f + 26f, 480f, 26f),
                                 _flash, _promptStyle);
        }
    }

    string Hint()
    {
        switch (_kind)
        {
            case PuzzleKind.Ordered:
                return $"[E] 点亮{Noun()} —— 按入口石碑的顺序（已点亮 {_progress}/{_nodes.Count}）";
            case PuzzleKind.Guarded:
                return $"[E] 点亮{Noun()} —— 全部点亮后会有守卫出现（{LitCount()}/{_nodes.Count}）";
            default:
                return $"[E] 点亮{Noun()}（{LitCount()}/{_nodes.Count}）";
        }
    }

    /// <summary>节点的称呼（蒙德"方碑"，璃月"石灯 / 石鼓"）</summary>
    string Noun()
    {
        switch (_style)
        {
            case PuzzleStyle.LiyueLamp: return "石灯";
            case PuzzleStyle.LiyueDrum: return "石鼓";
            default:                    return "方碑";
        }
    }

    int LitCount()
    {
        int n = 0;
        foreach (var node in _nodes) if (node.on) n++;
        return n;
    }

    void EnsureStyles()
    {
        if (_nameStyle != null) return;
        _nameStyle   = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        _promptStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
    }

    public static string SiteName(PuzzleKind kind)
    {
        switch (kind)
        {
            case PuzzleKind.Ordered: return "元素方碑 · 顺序";
            case PuzzleKind.Guarded: return "元素方碑 · 守护";
            default:                 return "元素方碑";
        }
    }

    /// <summary>场地名字（按风格取：璃月那两套的名字在 LiyuePuzzles 里）</summary>
    public static string SiteName(PuzzleKind kind, PuzzleStyle style) =>
        style == PuzzleStyle.Monolith ? SiteName(kind) : LiyueBuildings.PuzzleSiteName(kind, style);
}
