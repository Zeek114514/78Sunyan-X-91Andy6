using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 传送锚点（原神那套：走近自动点亮，之后在大地图上点它传送过来）。
///
/// 【两种形态】
///   · 石柱锚点（默认）：石台 + 石柱 + 石环 + 悬浮水晶；
///   · 七天神像（<see cref="AnchorKind.StatueWind"/> / <see cref="AnchorKind.StatueGeo"/>）：
///     每个国家一尊，外形完全不同（蒙德是风神立像 + 双翼，璃月是岩神坐像 + 悬浮岩块）。
///     神像**同时**是传送点（点亮、图钉、Tab、落地都走同一套）**和**回复点：
///     走到神像脚边（10 米内）生命与体力一起回满 —— 就是原神里在神像前恢复那套。
///     之所以做成同一个组件：传送这一头（大地图图钉 / 点击 / 黑场过渡）全都挂在
///     <see cref="TeleportAnchor.All"/> 上，神像另起一套等于把那一整套再抄一遍。
///
/// 【点亮】
///   玩家走到 ActivateRadius 米以内自动点亮 —— 不需要按键，也不会重复触发；
///   点亮会换一套网格（暗 → 亮）、往聊天栏播报一句，并把它加进"已解锁"名单，
///   大地图就是照这份名单画可点的锚点图钉。
///
/// 【外观】
///   石台 + 石柱 + 柱顶石环 + 悬浮水晶（上下两个锥拼的八面体）。
///   暗的时候水晶是深青灰、环是石灰岩色；点亮后水晶和环都转成青绿（和石灯 / 方碑"点亮"同一套颜色，
///   整个游戏里"已激活"就是这个颜色），水晶还会慢慢自转 + 上下浮动。
///   网格是"单子网格 + 顶点色"（和方碑、石灯同一路），配世界的顶点色材质，不碰贴图。
///
/// 【和大地图的关系】
///   大地图在 WorldMap.DrawBigMap 里画锚点图钉（已解锁的金色、未解锁的暗色），
///   点已解锁的图钉调 WorldMap.TeleportTo → VoxelPlayer.Teleport。
///   所以这个类只负责"在世界里长什么样、什么时候点亮"，传送那一头在地图里。
/// </summary>
public class TeleportAnchor : MonoBehaviour
{
    /// <summary>形态：石柱锚点 / 七天神像（风 · 蒙德 / 岩 · 璃月 / 雷 · 稻妻）</summary>
    public enum AnchorKind : byte { Pillar = 0, StatueWind, StatueGeo, StatueThunder }

    /// <summary>场上所有锚点（大地图画图钉、自检统计都用它）</summary>
    public static readonly List<TeleportAnchor> All = new List<TeleportAnchor>();

    const float ActivateRadius = 6f;      // 走到这么近自动点亮
    const float BlessRadius    = 10f;     // 七天神像：走到这么近回满生命与体力
    const float BlessInterval  = 3f;      // 神像祝福的检查间隔（没缺就不用刷）
    const float ShowRadius     = 24f;     // 多远开始飘名字
    const float BobAmplitude   = 0.12f;   // 水晶上下浮动幅度
    const float SpinsPerSecond = 0.35f;   // 水晶自转速度

    public string  SiteName { get; private set; }
    public bool    Unlocked { get; private set; }
    public Vector3 LandPos  { get; private set; }     // 传送落点（脚底，比锚点稍微靠外一点，免得卡在柱子里）
    public bool    Major    { get; private set; }     // 关键锚点（城镇 / 地标 / 村口）：大地图上图钉更大、名字一定会写出来
    public float   FaceYaw  { get; private set; }     // 传送到这儿以后面朝哪（度）；NaN = 别动玩家朝向
    public AnchorKind Kind  { get; private set; }     // 石柱锚点还是七天神像

    OpenWorld _world;
    Transform _player;
    Transform _crystal;
    MeshFilter _crystalFilter;
    GUIStyle _nameStyle, _hintStyle;
    float _age;
    float _nextBless;

    /// <summary>是不是七天神像（大地图 / 自检要用不同画法）</summary>
    public bool IsStatue => Kind != AnchorKind.Pillar;

    /// <summary>已解锁的锚点数（大地图的提示文字用）</summary>
    public static int UnlockedCount
    {
        get
        {
            int n = 0;
            foreach (var a in All) if (a != null && a.Unlocked) n++;
            return n;
        }
    }

    public void Init(string siteName, Vector3 anchorPos, Vector3 landPos, float faceYaw, bool major,
                     OpenWorld world, Transform player, Material material, bool unlockedAtStart = false,
                     AnchorKind kind = AnchorKind.Pillar)
    {
        SiteName = siteName;
        FaceYaw = faceYaw;
        Major = major;
        Kind = kind;
        _world = world;
        _player = player;

        // ★ 落点由调用方给，不等于锚点位置：锚点自己那根石柱是有碰撞的，
        //   直接把玩家放到锚点坐标上会传进柱子里 —— 所以落在旁边两三米处
        LandPos = landPos;

        transform.position = anchorPos;

        // 石台 + 石柱 + 石环（或整座神像）：这些不随点亮变化，一起建在"本体"网格里
        var body = new GameObject("Body");
        body.transform.SetParent(transform, false);
        var bodyFilter = body.AddComponent<MeshFilter>();
        bodyFilter.sharedMesh = IsStatue ? StatueMesh(Kind) : AnchorMesh(Alignment.Pillar);
        body.AddComponent<MeshRenderer>().sharedMaterial = material;
        body.AddComponent<MeshCollider>().sharedMesh = bodyFilter.sharedMesh;   // 锚点 / 神像要能挡人

        // 悬浮水晶：单独一个物体，方便自转 / 浮动（神像头上那颗元素宝珠也是它）
        var crystalGo = new GameObject("Crystal");
        crystalGo.transform.SetParent(transform, false);
        crystalGo.transform.localPosition = new Vector3(0f, CrystalHeight, 0f);
        crystalGo.transform.localScale = Vector3.one * (IsStatue ? 1.7f : 1f);
        _crystal = crystalGo.transform;

        _crystalFilter = crystalGo.AddComponent<MeshFilter>();
        _crystalFilter.sharedMesh = AnchorMesh(Alignment.CrystalDim);
        crystalGo.AddComponent<MeshRenderer>().sharedMaterial = material;

        SetUnlocked(unlockedAtStart, announce: false);
    }

    // ================= 点亮 / 神像祝福 =================

    void Update()
    {
        _age += Time.deltaTime;

        // 水晶自转 + 上下浮动（点亮后转得更"活"一点，暗的时候也慢慢转，远处能看见这里有东西）
        if (_crystal != null)
        {
            _crystal.localRotation = Quaternion.Euler(0f, _age * SpinsPerSecond * 360f, 0f);
            float bob = Mathf.Sin(_age * 1.6f) * BobAmplitude * (Unlocked ? 1.4f : 1f);
            _crystal.localPosition = new Vector3(0f, CrystalHeight + bob, 0f);
        }

        if (_player == null) return;

        // 七天神像：站在脚边就回满生命与体力（原神那套"在神像前恢复"）
        if (IsStatue && Time.time >= _nextBless)
        {
            _nextBless = Time.time + BlessInterval;

            Vector3 toStatue = _player.position - transform.position;
            toStatue.y = 0f;
            if (toStatue.sqrMagnitude <= BlessRadius * BlessRadius &&
                VoxelStats.Instance != null && VoxelStats.Instance.Bless())
            {
                VoxelChat.Announce($"{SiteName} 已为你恢复生命与体力");
                VoxelAudio.PlaySfxAt("heal", transform.position + Vector3.up * 3f, 0.8f);

                // 在神像前恢复满三次 → 解锁"七天神像"（成就卡片由 VoxelHud 弹）
                if (Achievements.MarkBlessing()) VoxelHud.UnlockAchievement("blessing");
            }
        }

        if (Unlocked) return;

        Vector3 to = _player.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude <= ActivateRadius * ActivateRadius) SetUnlocked(true, announce: true);
    }

    void SetUnlocked(bool on, bool announce)
    {
        Unlocked = on;

        // 只换水晶那一段网格：石台 / 石柱 / 石环不随点亮变化，全场共用同一张（见 AnchorMesh）
        if (_crystalFilter != null)
            _crystalFilter.sharedMesh = AnchorMesh(on ? Alignment.CrystalLit : Alignment.CrystalDim);

        if (on && announce)
        {
            VoxelChat.Announce(IsStatue
                ? $"解锁七天神像 · {SiteName}（大地图上点它传送，走到神像脚边还能回满生命与体力）"
                : $"解锁传送锚点 · {SiteName}（大地图上点它就能传送）");

            VoxelAudio.PlaySfxAt("heal", transform.position + Vector3.up * 3f, 0.7f);   // 点亮的一记清响
        }
    }

    // ================= 名牌 =================

    void OnGUI()
    {
        if (_player == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        float dist = Vector3.Distance(transform.position, cam.transform.position);
        if (dist > ShowRadius) return;

        if (_nameStyle == null)
        {
            _nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
        }

        if (IsStatue) _nameStyle.fontSize = 16;              // 神像的名牌大一号：它是个地标

        Vector3 head = transform.position + Vector3.up * (IsStatue ? 16.6f : 4.6f);
        Vector3 sp = cam.WorldToScreenPoint(head);
        if (sp.z <= 0f) return;

        float alpha = Mathf.Clamp01((ShowRadius - dist) / 5f);
        _nameStyle.normal.textColor = IsStatue
            ? new Color(1f, 0.97f, 0.86f, alpha)             // 神像：暖白（和金色图钉一套）
            : Unlocked
                ? new Color(0.72f, 0.96f, 0.88f, alpha)      // 已解锁：青绿（全局"已激活"色）
                : new Color(0.86f, 0.88f, 0.92f, alpha);

        VoxelHud.LabelShadow(new Rect(sp.x - 140f, Screen.height - sp.y - 12f, 280f, 22f), SiteName, _nameStyle);

        _hintStyle.normal.textColor = new Color(1f, 0.94f, 0.78f, alpha * 0.9f);
        VoxelHud.LabelShadow(new Rect(sp.x - 140f, Screen.height - sp.y + 8f, 280f, 20f),
                             IsStatue
                                 ? "走近回满生命与体力 · 大地图点它传送"
                                 : Unlocked ? "已解锁 · 大地图点它传送" : "走近即点亮", _hintStyle);
    }

    void OnEnable()  => All.Add(this);
    void OnDisable() => All.Remove(this);

    // ================= 网格（单子网格 + 顶点色，两种状态各建一次） =================

    /// <summary>水晶要单独换网格，石柱 / 石环是另一段。合成三个网格各建一次，全场共用</summary>
    public enum Alignment { Pillar = 0, CrystalDim, CrystalLit }

    const float CrystalY = 3.0f;

    /// <summary>水晶（神像那颗元素宝珠）离地多高：石柱锚点在柱顶，神像在头顶上方</summary>
    float CrystalHeight => IsStatue ? 14.2f : CrystalY;

    static readonly Mesh[] _cache = new Mesh[3];
    static readonly List<Vector3> _v = new List<Vector3>();
    static readonly List<Color>   _c = new List<Color>();
    static readonly List<int>     _t = new List<int>();

    // 配色：石件用石灰岩（和璃月那套一致），激活色用青绿（和方碑 / 石灯点亮的刻纹同色）
    static readonly Color Stone    = new Color32(0xd2, 0xcb, 0xb8, 255);
    static readonly Color StoneDim = new Color32(0xa8, 0xa2, 0x92, 255);
    static readonly Color GemLit   = new Color32(0x8c, 0xe6, 0xd2, 255);
    static readonly Color GemDim   = new Color32(0x5c, 0x6e, 0x74, 255);
    static readonly Color Gold     = new Color32(0xd9, 0xb4, 0x5c, 255);

    public static Mesh AnchorMesh(Alignment part)
    {
        if (_cache[(int)part] != null) return _cache[(int)part];

        _v.Clear(); _c.Clear(); _t.Clear();

        switch (part)
        {
            case Alignment.Pillar:
                BuildPillar();
                break;
            default:
                BuildCrystal(part == Alignment.CrystalLit);
                break;
        }

        _cache[(int)part] = LowPoly.BuildMesh("TeleportAnchor" + part, _v, _c, _t);
        return _cache[(int)part];
    }

    /// <summary>石台 + 石柱 + 柱顶石环（石环固定在柱顶，不随点亮变色 —— 变色的是水晶）</summary>
    static void BuildPillar()
    {
        LowPoly.Prism(_v, _c, _t, Vector3.zero, 0.34f, 1.5f, 1.3f, 8, StoneDim);            // 八角石台
        LowPoly.Cylinder(_v, _c, _t, Vector3.up * 0.34f, 2.1f, 0.36f, 0.3f, 8, Stone);      // 石柱
        LowPoly.Box(_v, _c, _t, new Vector3(0f, 2.46f, 0f), new Vector3(0.86f, 0.16f, 0.86f),
                    Quaternion.identity, Gold, Gold);                                       // 柱顶金箍
        LowPoly.Ring(_v, _c, _t, Vector3.up * 2.72f, 0.44f, 0.6f, 12, Quaternion.identity, StoneDim);   // 石环
        for (int i = 0; i < 4; i++)                                                          // 四角小石墩
        {
            float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            LowPoly.Box(_v, _c, _t, new Vector3(Mathf.Cos(a) * 1.05f, 0.22f, Mathf.Sin(a) * 1.05f),
                        new Vector3(0.34f, 0.44f, 0.34f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f),
                        Stone, StoneDim);
        }
    }

    /// <summary>悬浮水晶：上下两个锥拼成的八面体（局部原点在柱顶上方 CrystalY 处，物体自己负责摆位）</summary>
    static void BuildCrystal(bool lit)
    {
        Color gem  = lit ? GemLit : GemDim;
        Color trim = lit ? Gold : StoneDim;

        LowPoly.Prism(_v, _c, _t, Vector3.up * -0.5f, 0.5f, 0.02f, 0.34f, 6, gem);          // 下半（向上张开）
        LowPoly.Prism(_v, _c, _t, Vector3.zero, 0.5f, 0.34f, 0.02f, 6, gem);                // 上半（向上收尖）
        LowPoly.Cylinder(_v, _c, _t, Vector3.up * 0.46f, 0.14f, 0.1f, 0.04f, 6, trim);      // 顶上一小段金尖
    }

    // ================= 七天神像（两个国家各一尊，外形完全不同） =================

    static readonly Mesh[] _statueCache = new Mesh[4];

    // 蒙德（风）：浅米色石料（和城里那尊风神像同色）+ 青绿风元素
    static readonly Color WindStone     = new Color32(0xd8, 0xd0, 0xbc, 255);
    static readonly Color WindStoneDark = new Color32(0xb3, 0xa9, 0x95, 255);
    // 璃月（岩）：偏黄的岩色 + 琥珀岩元素
    static readonly Color GeoStone      = new Color32(0xc6, 0xbc, 0xa6, 255);
    static readonly Color GeoStoneDark  = new Color32(0x9e, 0x93, 0x7d, 255);
    static readonly Color GeoAmber      = new Color32(0xe2, 0xa9, 0x3e, 255);
    // 稻妻（雷）：青灰色石料 + 紫电
    static readonly Color ThStone       = new Color32(0xb9, 0xb6, 0xc2, 255);
    static readonly Color ThStoneDark   = new Color32(0x8d, 0x88, 0x98, 255);
    static readonly Color ThViolet      = new Color32(0x8f, 0x6f, 0xd8, 255);
    static readonly Color ThVioletLight = new Color32(0xc0, 0xa8, 0xf2, 255);

    /// <summary>七天神像的网格（和石柱锚点同一路：单子网格 + 顶点色，每种各建一次全场共用）</summary>
    public static Mesh StatueMesh(AnchorKind kind)
    {
        if (_statueCache[(int)kind] != null) return _statueCache[(int)kind];

        _v.Clear(); _c.Clear(); _t.Clear();

        switch (kind)
        {
            case AnchorKind.StatueWind:    BuildWindStatue(); break;
            case AnchorKind.StatueThunder: BuildThunderStatue(); break;
            default:                       BuildGeoStatue(); break;
        }

        _statueCache[(int)kind] = LowPoly.BuildMesh("SevenStatue" + kind, _v, _c, _t);
        return _statueCache[(int)kind];
    }

    /// <summary>风神像：两级石台 + 石柱 + 立像（袍身 + 肩 + 头）+ 抱在身前的竖琴 + 一对张开的羽翼。
    /// 和蒙德广场上那尊是同一套比例，只是放大一档、翅膀分两层（大羽 + 小羽）</summary>
    static void BuildWindStatue()
    {
        LowPoly.Prism(_v, _c, _t, Vector3.zero, 0.9f, 3.4f, 3.0f, 12, WindStoneDark);              // 台座
        LowPoly.Cylinder(_v, _c, _t, Vector3.up * 0.9f, 1.0f, 2.6f, 2.2f, 12, WindStone, true);    // 第二级
        LowPoly.Cylinder(_v, _c, _t, Vector3.up * 1.9f, 6.4f, 1.5f, 1.05f, 10, WindStone, true);   // 柱身

        Vector3 top = Vector3.up * 8.3f;
        LowPoly.Prism(_v, _c, _t, top, 3.4f, 1.5f, 0.95f, 8, WindStone);                           // 袍身（下宽上收）
        LowPoly.Box(_v, _c, _t, top + Vector3.up * 2.1f, new Vector3(2.4f, 0.5f, 1.2f),
                    Quaternion.identity, WindStone, WindStoneDark);                                // 肩
        LowPoly.Box(_v, _c, _t, top + new Vector3(0f, 1.35f, 0.85f), new Vector3(1.5f, 0.22f, 0.3f),
                    Quaternion.identity, Gold, Gold);                                              // 竖琴
        LowPoly.Sphere(_v, _c, _t, top + Vector3.up * 3.4f, 0.62f, WindStone, 8);                  // 头

        for (int w = -1; w <= 1; w += 2)                                                           // 双翼：外大羽 + 内小羽
        {
            var big = Quaternion.Euler(0f, 0f, w * 34f);
            LowPoly.Box(_v, _c, _t, top + Vector3.up * 2.5f + big * new Vector3(w * 2.3f, 0f, 0f),
                        new Vector3(5.0f, 0.4f, 1.15f), big, WindStone, WindStoneDark);

            var small = Quaternion.Euler(0f, 0f, w * 60f);
            LowPoly.Box(_v, _c, _t, top + Vector3.up * 3.6f + small * new Vector3(w * 1.9f, 0f, 0f),
                        new Vector3(3.4f, 0.34f, 0.9f), small, WindStone, WindStoneDark);
        }

        LowPoly.Sphere(_v, _c, _t, top + Vector3.up * 4.7f, 0.42f, Gold, 8);                       // 头顶金珠
    }

    /// <summary>稻妻（雷）神像：青灰石台 + 台基上的立像（和服身姿 + 冠）+ 身前合捧的紫电宝珠
    /// + 头顶的雷元素八面体 + 背后两道雷光弧（金饰）。形制和另外两尊完全不同</summary>
    static void BuildThunderStatue()
    {
        LowPoly.Prism(_v, _c, _t, Vector3.zero, 0.9f, 3.6f, 3.2f, 10, ThStoneDark);
        LowPoly.Prism(_v, _c, _t, Vector3.up * 0.9f, 0.8f, 3.0f, 2.7f, 10, ThStone);
        LowPoly.Cylinder(_v, _c, _t, Vector3.up * 1.7f, 5.2f, 1.5f, 1.1f, 8, ThStone, true);

        Vector3 top = Vector3.up * 6.9f;
        // 和服身姿：下身宽、上身收
        LowPoly.Prism(_v, _c, _t, top, 3.6f, 1.45f, 1.05f, 8, ThStone);
        LowPoly.Box(_v, _c, _t, top + Vector3.up * 2.3f, new Vector3(2.5f, 0.5f, 1.3f),
                    Quaternion.identity, ThStone, ThStoneDark);                       // 肩（袖）
        LowPoly.Box(_v, _c, _t, top + new Vector3(0f, 1.5f, 0.75f), new Vector3(1.5f, 0.5f, 0.5f),
                    Quaternion.identity, ThStoneDark, ThStoneDark);                   // 合捧的手
        LowPoly.Sphere(_v, _c, _t, top + Vector3.up * 3.5f, 0.64f, ThStone, 8);       // 头
        LowPoly.Box(_v, _c, _t, top + Vector3.up * 4.2f, new Vector3(1.7f, 0.26f, 1.7f),
                    Quaternion.identity, Gold, Gold);                                 // 冠

        // 手心的雷元素宝珠（上下两锥）
        LowPoly.Prism(_v, _c, _t, top + new Vector3(0f, 1.5f, 1.05f), 0.55f, 0.02f, 0.44f, 6, ThViolet);
        LowPoly.Prism(_v, _c, _t, top + new Vector3(0f, 2.05f, 1.05f), 0.55f, 0.44f, 0.02f, 6, ThVioletLight);

        // 背后两道雷光弧（斜插的细长金饰，稻妻神像那种"雷纹"轮廓）
        for (int s = -1; s <= 1; s += 2)
        {
            var arc = Quaternion.Euler(0f, 0f, s * 26f);
            LowPoly.Box(_v, _c, _t, top + Vector3.up * 2.9f + arc * new Vector3(s * 2.1f, 0f, 0f),
                        new Vector3(3.6f, 0.22f, 0.4f), arc, Gold, ThViolet);
        }

        // 四角悬浮的雷晶（小八面体）
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            Vector3 p = new Vector3(Mathf.Cos(a) * 3.9f, 4.2f + (i % 2) * 0.8f, Mathf.Sin(a) * 3.9f);
            LowPoly.Prism(_v, _c, _t, p, 0.5f, 0.02f, 0.4f, 6, ThViolet);
            LowPoly.Prism(_v, _c, _t, p + Vector3.up * 0.5f, 0.5f, 0.4f, 0.02f, 6, ThVioletLight);
        }
    }

    /// <summary>岩神像：三层岩台 + 岩柱 + 坐像（盘坐 + 上身 + 双臂 + 头 + 冠）+ 胸前的琥珀岩元素
    /// + 四块悬浮的岩块（岩元素最认得出来的那四块方岩）</summary>
    static void BuildGeoStatue()
    {
        LowPoly.Prism(_v, _c, _t, Vector3.zero, 0.8f, 3.8f, 3.4f, 10, GeoStoneDark);               // 岩台（下）
        LowPoly.Prism(_v, _c, _t, Vector3.up * 0.8f, 0.7f, 3.2f, 2.9f, 10, GeoStone);              // 岩台（上）
        LowPoly.Cylinder(_v, _c, _t, Vector3.up * 1.5f, 6.0f, 1.6f, 1.15f, 8, GeoStone, true);     // 岩柱

        Vector3 top = Vector3.up * 7.5f;
        LowPoly.Prism(_v, _c, _t, top, 1.5f, 2.0f, 1.7f, 8, GeoStone);                             // 盘坐
        LowPoly.Box(_v, _c, _t, top + Vector3.up * 2.4f, new Vector3(2.2f, 2.6f, 1.5f),
                    Quaternion.identity, GeoStone, GeoStoneDark);                                  // 上身

        for (int s = -1; s <= 1; s += 2)                                                           // 双臂搭在膝上
            LowPoly.Box(_v, _c, _t, top + new Vector3(s * 1.35f, 1.5f, 0.55f), new Vector3(0.55f, 1.7f, 0.7f),
                        Quaternion.identity, GeoStoneDark, GeoStoneDark);

        LowPoly.Sphere(_v, _c, _t, top + Vector3.up * 4.3f, 0.66f, GeoStone, 8);                   // 头
        LowPoly.Box(_v, _c, _t, top + Vector3.up * 5.05f, new Vector3(1.4f, 0.3f, 1.4f),
                    Quaternion.identity, Gold, Gold);                                              // 冠

        // 胸前的岩元素：上下两个锥拼的八面体
        LowPoly.Prism(_v, _c, _t, top + new Vector3(0f, 1.5f, 0.95f), 0.5f, 0.02f, 0.42f, 6, GeoAmber);
        LowPoly.Prism(_v, _c, _t, top + new Vector3(0f, 2.0f, 0.95f), 0.5f, 0.42f, 0.02f, 6, GeoAmber);

        // 四块悬浮岩：绕着雕像排一圈、高低错开
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            LowPoly.Box(_v, _c, _t,
                        new Vector3(Mathf.Cos(a) * 4.2f, 3.6f + (i % 2) * 1.0f, Mathf.Sin(a) * 4.2f),
                        new Vector3(0.9f, 0.9f, 0.9f), Quaternion.Euler(20f, a * Mathf.Rad2Deg, 25f),
                        GeoAmber, GeoAmber * 0.85f);
        }
    }
}
