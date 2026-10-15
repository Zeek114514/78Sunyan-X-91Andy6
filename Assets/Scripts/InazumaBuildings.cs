using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 稻妻的建筑种类（和蒙德 <see cref="WildKind"/>、璃月 <see cref="LiyueKind"/> 一一对应，风格完全不同）。
/// 顺序就是选点、地图标记、日志统计里的顺序，不要随便插队。
/// </summary>
public enum InazumaKind : byte
{
    Machiya = 0,      // 町屋：两层木屋商铺（一楼幌子 + 二楼纸窗 + 黑瓦双坡顶）
    Tenshu,           // 将军府（天守）：石垣 + 三层收分白墙黑瓦 + 金鯱，全岛最高
    Torii,            // 鸟居：朱红大门
    Shrine,           // 神社：鸟居 + 拜殿（千鸟破风）+ 注连绳 + 石灯笼
    Pagoda,           // 五重塔：五层塔檐 + 相轮
    Watchtower,       // 望楼：木构箭楼 + 太鼓
    Teahouse,         // 茶屋：木屋 + 缘侧 + 暖帘
    StoneLantern,     // 石灯笼：春日灯笼（石柱 + 火袋 + 笠）
    Bridge,           // 木桥：太鼓桥（拱形木桥 + 高栏）
    Dock,             // 码头：木栈桥 + 系船柱 + 货箱 + 小船
    Sakura,           // 樱花林：一丛樱花（岛上的固定景观）
}

/// <summary>
/// 稻妻的几何生成：日式木构 + 黑瓦 + 石垣 + 朱红鸟居 + 樱花。
///
/// 和 <see cref="LiyueBuildings"/> / <see cref="WildBuildings"/> 结构完全同构：
///   · 三个子网格（0 石墙贴图 / 1 屋顶贴图 / 2 顶点色）。★ 稻妻只用 0 和 2 ——
///     黑瓦和蒙德那套红瓦贴图不搭，所以瓦面走**顶点色平涂**（和璃月的黛瓦同一个理由）
///   · 全部低模 + 顶点色，和地形 / 其它建筑共用同一套材质
///   · 固定哈希，每次进游戏一样
///
/// 撑起"一眼看出是日式"的是四个零件：<see cref="KawaraRoof"/>（深出檐的黑瓦双坡顶）、
/// <see cref="ShojiWall"/>（白灰泥墙 + 木框纸窗）、<see cref="Ishigaki"/>（收分的石垣）、
/// <see cref="ToriiGate"/>（朱红鸟居）。其余都是常规低模。
/// </summary>
public static class InazumaBuildings
{
    // ---------- 稻妻配色 ----------

    static readonly Color WoodDark  = new Color32(0x46, 0x35, 0x2b, 255);   // 深木（柱、梁）
    static readonly Color Wood      = new Color32(0x6b, 0x4e, 0x39, 255);   // 木
    static readonly Color WoodLight = new Color32(0x8d, 0x6b, 0x4d, 255);   // 浅木（缘侧、地板）
    static readonly Color Plaster   = new Color32(0xef, 0xea, 0xdd, 255);   // 白灰泥墙
    static readonly Color PlasterSh = new Color32(0xd3, 0xcc, 0xbd, 255);   // 灰泥阴面
    static readonly Color Kawara    = new Color32(0x3f, 0x45, 0x4f, 255);   // 黑瓦
    static readonly Color KawaraDk  = new Color32(0x2b, 0x30, 0x38, 255);   // 黑瓦（阴面 / 屋脊）
    static readonly Color Vermilion = new Color32(0xc0, 0x45, 0x33, 255);   // 朱红（鸟居、栏杆）
    static readonly Color VermDark  = new Color32(0x93, 0x33, 0x26, 255);
    static readonly Color Stone     = new Color32(0xa9, 0xa3, 0x96, 255);   // 石垣 / 石灯笼
    static readonly Color StoneDark = new Color32(0x85, 0x7f, 0x74, 255);
    static readonly Color StoneMoss = new Color32(0x8f, 0x93, 0x7c, 255);   // 长了苔的石垣
    static readonly Color Gold      = new Color32(0xd9, 0xb4, 0x5c, 255);
    static readonly Color Paper     = new Color32(0xe9, 0xdf, 0xbe, 255);   // 纸窗 / 暖帘
    static readonly Color Cloth     = new Color32(0x39, 0x44, 0x6b, 255);   // 深蓝暖帘
    static readonly Color Leaf      = new Color32(0x54, 0x82, 0x4e, 255);   // 松
    static readonly Color SakuraPink = new Color32(0xf2, 0xa8, 0xc4, 255);  // 樱
    static readonly Color Electro   = new Color32(0x9b, 0x82, 0xd6, 255);   // 雷元素（紫）
    static readonly Color Rice      = new Color32(0xd8, 0xc4, 0x86, 255);

    // ---------- 生成状态 ----------

    static readonly List<Vector3> _v = new List<Vector3>();
    static readonly List<Color>   _c = new List<Color>();

    /// <summary>三个子网格：0 石垣（贴图）/ 1 屋顶瓦片（稻妻不用）/ 2 其余（顶点色）</summary>
    public static readonly List<int>[] SubTris = { new List<int>(), new List<int>(), new List<int>() };

    /// <summary>需要放"火苗"的位置（石灯笼、茶屋的炉子、码头的火盆）</summary>
    public static readonly List<Vector3> Flames = new List<Vector3>();

    /// <summary>民居位置（地图上画色块用）：xy = 位置，z = 半径</summary>
    public static readonly List<Vector4> Houses = new List<Vector4>();

    // ================= 对外接口 =================

    public static void Begin()
    {
        _v.Clear();
        _c.Clear();
        foreach (var list in SubTris) list.Clear();
        Flames.Clear();
        Houses.Clear();
    }

    /// <summary>占地半径（选点、地台、其它生成器避让都用它）</summary>
    public static float Footprint(InazumaKind kind)
    {
        switch (kind)
        {
            case InazumaKind.Machiya:      return 7f;
            case InazumaKind.Tenshu:       return 26f;
            case InazumaKind.Torii:        return 8f;
            case InazumaKind.Shrine:       return 16f;
            case InazumaKind.Pagoda:       return 13f;
            case InazumaKind.Watchtower:   return 7f;
            case InazumaKind.Teahouse:     return 7f;
            case InazumaKind.StoneLantern: return 2f;
            case InazumaKind.Bridge:       return 18f;
            case InazumaKind.Dock:         return 12f;
            case InazumaKind.Sakura:       return 9f;
            default:                       return 8f;
        }
    }

    /// <summary>地图标记种类（50 起，避开蒙德那套 14~24 和璃月那套 30~40）</summary>
    public static byte MarkerKind(InazumaKind kind) => (byte)(50 + (int)kind);

    public static string DisplayName(InazumaKind kind)
    {
        switch (kind)
        {
            case InazumaKind.Machiya:      return "町屋";
            case InazumaKind.Tenshu:       return "将军府";
            case InazumaKind.Torii:        return "鸟居";
            case InazumaKind.Shrine:       return "神社";
            case InazumaKind.Pagoda:       return "五重塔";
            case InazumaKind.Watchtower:   return "望楼";
            case InazumaKind.Teahouse:     return "茶屋";
            case InazumaKind.StoneLantern: return "石灯笼";
            case InazumaKind.Bridge:       return "木桥";
            case InazumaKind.Dock:         return "码头";
            case InazumaKind.Sakura:       return "樱花林";
            default:                       return "稻妻建筑";
        }
    }

    /// <summary>地图色块颜色：1 石板浅色 / 2 木质深色（稻妻的黑瓦 / 木构都算 2）</summary>
    public static byte BlobColor(InazumaKind kind) => kind == InazumaKind.Tenshu ? (byte)1 : (byte)2;

    /// <summary>把一栋（一组）建筑的几何写进列表</summary>
    public static void Append(InazumaKind kind, Vector3 center, float yaw, float variant, float param = 0f)
    {
        switch (kind)
        {
            case InazumaKind.Machiya:      BuildMachiya(center, yaw, variant); break;
            case InazumaKind.Tenshu:       BuildTenshu(center, yaw, variant); break;
            case InazumaKind.Torii:        BuildTorii(center, yaw, variant); break;
            case InazumaKind.Shrine:       BuildShrine(center, yaw, variant); break;
            case InazumaKind.Pagoda:       BuildPagoda(center, yaw, variant); break;
            case InazumaKind.Watchtower:   BuildWatchtower(center, yaw, variant); break;
            case InazumaKind.Teahouse:     BuildTeahouse(center, yaw, variant); break;
            case InazumaKind.StoneLantern: StoneLanternAt(center, yaw); break;
            case InazumaKind.Bridge:       BuildBridge(center, yaw, variant, param); break;
            case InazumaKind.Dock:         BuildDock(center, yaw, variant); break;
            case InazumaKind.Sakura:       BuildSakuraGrove(center, variant); break;
        }
    }

    public static Mesh BuildMesh(string name)
    {
        var mesh = new Mesh { name = name };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(_v);
        mesh.SetColors(_c);
        mesh.subMeshCount = 3;
        mesh.SetTriangles(SubTris[0], 0);
        mesh.SetTriangles(SubTris[1], 1);
        mesh.SetTriangles(SubTris[2], 2);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ================= 子网格路由 =================

    /// <summary>只有石垣 / 石灯笼走石墙贴图，瓦面和木构都走顶点色</summary>
    static List<int> T(Color color)
    {
        if (Same(color, Stone) || Same(color, StoneDark) || Same(color, StoneMoss)) return SubTris[0];
        return SubTris[2];
    }

    static bool Same(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f;

    // ================= 零件快捷方式 =================

    static void Box(Vector3 center, Vector3 size, Quaternion rot, Color top, Color side) =>
        LowPoly.Box(_v, _c, T(top), center, size, rot, top, side);

    static void Box(Vector3 center, Vector3 size, Color top, Color side) =>
        LowPoly.Box(_v, _c, T(top), center, size, Quaternion.identity, top, side);

    static void Cyl(Vector3 basePos, float height, float bottomR, float topR, int sides, Color color, bool capTop = false) =>
        LowPoly.Cylinder(_v, _c, T(color), basePos, height, bottomR, topR, sides, color, capTop);

    static void Prism(Vector3 basePos, float height, float bottomR, float topR, int sides, Color color) =>
        LowPoly.Prism(_v, _c, T(color), basePos, height, bottomR, topR, sides, color);

    static void Ball(Vector3 center, float radius, Color color, int segments, float squash = 1f, float seed = 0f) =>
        LowPoly.Sphere(_v, _c, T(color), center, radius, color, segments, squash, seed > 0f ? 1f : 0f, seed);

    static void Disc(Vector3 center, float innerR, float outerR, int sides, Quaternion rot, Color color) =>
        LowPoly.Ring(_v, _c, T(color), center, innerR, outerR, sides, rot, color);

    static float H(int a, int b, int salt) => LowPoly.Hash01(a, b, salt);

    // ================= 日式零件 =================

    /// <summary>
    /// 黑瓦双坡顶（日式的"入母屋"简化版）：**两整块**带厚度的坡面 + 栋木 + 两端的鬼瓦 + 四角翘角。
    ///
    /// ★ 坡面不能切成几段去拼"起翘"：段与段之间会露出台阶，从底下看就是一堆锯齿状的黑板
    ///   （日式屋顶本来就是**直檐 + 翘角**，和璃月那种整条檐线弯起来的飞檐不是一回事）。
    ///   要翘的是四个角，所以四角各加一块斜向外挑的小楔块。
    /// 屋脊沿**局部 Z**，两坡朝 ±X 落；c 是檐口高度。
    /// </summary>
    static void KawaraRoof(Vector3 c, float w, float d, float h, Quaternion rot,
                           Color tile, Color ridgeCol)
    {
        // 瓦面要够厚：日式屋顶出檐又深又厚，薄薄一片从底下看就是"几块黑板飘着"
        const float thick = 0.45f;
        float hw = w * 0.5f;

        float slope = Mathf.Sqrt(hw * hw + h * h);                  // 檐口到屋脊的坡长
        float ang = Mathf.Atan2(h, hw) * Mathf.Rad2Deg;             // 坡度角（25° 上下最像天守）

        for (int sx = -1; sx <= 1; sx += 2)
        {
            var slabRot = rot * Quaternion.Euler(0f, 0f, -sx * ang);
            Vector3 mid = c + rot * new Vector3(sx * hw * 0.5f, h * 0.5f - thick * 0.45f, 0f);
            Box(mid, new Vector3(slope, thick, d), slabRot, tile, tile * 0.78f);
        }

        // 栋木（屋脊）+ 两端的鬼瓦
        Box(c + rot * new Vector3(0f, h + 0.2f, 0f), new Vector3(0.72f, 0.46f, d + 0.3f), rot, ridgeCol, ridgeCol);
        for (int s = -1; s <= 1; s += 2)
            Box(c + rot * new Vector3(0f, h + 0.56f, s * (d * 0.5f + 0.12f)),
                new Vector3(0.4f, 0.5f, 0.4f), rot, ridgeCol, ridgeCol);

        // 四角翘角：斜向上外挑的小楔块（日式屋顶最认得出来的那一笔）
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var cornerRot = rot
                              * Quaternion.Euler(0f, Mathf.Atan2(sx, sz) * Mathf.Rad2Deg, 0f)
                              * Quaternion.Euler(-28f, 0f, 0f);
                Box(c + rot * new Vector3(sx * (hw - 0.2f), 0.14f, sz * (d * 0.5f - 0.2f)),
                    new Vector3(0.3f, 0.3f, 1.5f), cornerRot, ridgeCol, ridgeCol);
            }
    }

    /// <summary>白灰泥墙 + 木框 + 纸窗（日式墙面最典型的一段）。size 是墙面尺寸，沿局部 X 展开</summary>
    static void ShojiWall(Vector3 center, Vector3 size, Quaternion rot, int salt, bool paper = true)
    {
        Box(center, size, rot, Plaster, PlasterSh);

        // 上下木框 + 两根立柱
        Box(center + rot * new Vector3(0f, size.y * 0.5f - 0.12f, 0f),
            new Vector3(size.x, 0.24f, size.z + 0.06f), rot, WoodDark, WoodDark);
        Box(center + rot * new Vector3(0f, -size.y * 0.5f + 0.12f, 0f),
            new Vector3(size.x, 0.24f, size.z + 0.06f), rot, WoodDark, WoodDark);
        for (int s = -1; s <= 1; s += 2)
            Box(center + rot * new Vector3(s * (size.x * 0.5f - 0.14f), 0f, 0f),
                new Vector3(0.26f, size.y, size.z + 0.06f), rot, WoodDark, WoodDark);

        if (!paper) return;

        // 纸窗：从墙里凸出来一点，免得和墙面 z-fighting
        float facing = size.z * 0.5f * Mathf.Sign(H(salt, 1, 7) - 0.5f);
        Box(center + rot * new Vector3(0.15f, 0.1f, facing * 0.55f),
            new Vector3(size.x * 0.62f, size.y * 0.5f, 0.08f), rot, Paper, Paper);
    }

    /// <summary>石垣（城郭的石砌基座）：三层收分的石坡 + 顶上一圈白灰泥腰线</summary>
    static void Ishigaki(Vector3 basePos, float bottomW, float topW, float height, Quaternion rot)
    {
        const int steps = 3;
        for (int i = 0; i < steps; i++)
        {
            float t0 = (float)i / steps, t1 = (float)(i + 1) / steps;
            float w0 = Mathf.Lerp(bottomW, topW, t0), w1 = Mathf.Lerp(bottomW, topW, t1);
            float h = height / steps;

            Prism(basePos + Vector3.up * (height * t0), h, w0 * 0.5f * 1.414f, w1 * 0.5f * 1.414f, 4,
                  i % 2 == 0 ? Stone : StoneMoss);
        }

        Box(basePos + Vector3.up * (height + 0.16f), new Vector3(topW * 0.96f, 0.32f, topW * 0.96f), rot,
            StoneDark, StoneDark);
    }

    /// <summary>朱红鸟居：两根略内倾的柱 + 笠木（顶上那道弧）+ 岛木 + 贯 + 额束</summary>
    static void ToriiGate(Vector3 basePos, float yaw, float scale)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float hw = 2.6f * scale;                 // 柱距之半
        float h = 5.4f * scale;                  // 柱高

        for (int s = -1; s <= 1; s += 2)
        {
            var p = basePos + rot * new Vector3(s * hw, 0f, 0f);
            Cyl(p, 0.5f * scale, 0.34f * scale, 0.36f * scale, 8, StoneDark, true);          // 柱础
            // 柱：往内倾一点（鸟居的柱是斜的）
            Cyl(p + Vector3.up * (0.5f * scale), h, 0.26f * scale, 0.23f * scale, 8, Vermilion);
        }

        // 贯（下横木）+ 额束
        Box(basePos + Vector3.up * (h * 0.72f), new Vector3(hw * 2.1f, 0.3f * scale, 0.34f * scale), rot,
            VermDark, VermDark);
        Box(basePos + Vector3.up * (h * 0.94f), new Vector3(0.34f * scale, 0.7f * scale, 0.3f * scale), rot,
            Vermilion, VermDark);

        // 岛木 + 笠木（两道，上面那道两端微微翘起）
        Box(basePos + Vector3.up * (h + 0.62f * scale), new Vector3(hw * 2.5f, 0.26f * scale, 0.5f * scale), rot,
            Vermilion, VermDark);
        for (int s = -1; s <= 1; s += 2)                                                   // 翘起的端头
            Box(basePos + rot * new Vector3(s * hw * 1.22f, h + 0.86f * scale, 0f),
                new Vector3(1.1f * scale, 0.26f * scale, 0.5f * scale),
                rot * Quaternion.Euler(0f, 0f, -s * 9f), Vermilion, VermDark);
        Box(basePos + Vector3.up * (h + 0.95f * scale), new Vector3(hw * 2.7f, 0.3f * scale, 0.62f * scale), rot,
            Vermilion, VermDark);
    }

    /// <summary>石灯笼（春日灯笼）：基础 + 柱 + 火袋 + 笠 + 宝珠；带火苗的话登记一处火焰</summary>
    static void StoneLanternAt(Vector3 basePos, float yaw, bool flame = false)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        Cyl(basePos, 0.35f, 0.62f, 0.52f, 8, StoneDark, true);                            // 基础
        Cyl(basePos + Vector3.up * 0.35f, 1.55f, 0.22f, 0.19f, 8, Stone);                  // 柱
        Cyl(basePos + Vector3.up * 1.9f, 0.22f, 0.52f, 0.48f, 8, Stone, true);             // 中台
        Box(basePos + Vector3.up * 2.55f, new Vector3(0.74f, 0.66f, 0.74f), rot, Stone, StoneDark);   // 火袋
        for (int i = 0; i < 4; i++)                                                        // 火袋四面的口
            Box(basePos + rot * Quaternion.Euler(0f, i * 90f, 0f) * new Vector3(0f, 2.55f, 0.39f),
                new Vector3(0.42f, 0.34f, 0.06f), rot * Quaternion.Euler(0f, i * 90f, 0f), Gold, Gold);
        Prism(basePos + Vector3.up * 2.92f, 0.5f, 0.86f, 0.3f, 6, StoneDark);              // 笠
        Ball(basePos + Vector3.up * 3.5f, 0.16f, StoneDark, 6, 1f, 0f);                    // 宝珠

        if (flame) Flames.Add(basePos + Vector3.up * 2.55f);
    }

    /// <summary>暖帘（店门口的布帘）：深蓝布 + 白字条</summary>
    static void Noren(Vector3 center, Vector3 size, Quaternion rot)
    {
        Box(center, size, rot, Cloth, Cloth * 0.85f);
        Box(center + rot * new Vector3(0f, 0.02f, size.z * 0.6f), new Vector3(size.x * 0.16f, size.y * 0.78f, 0.06f),
            rot, Paper, Paper);
    }

    /// <summary>樱花的树冠零件（岛上的樱花树都用它，选点和建筑两边一致）</summary>
    static void SakuraTree(Vector3 basePos, float seed)
    {
        float height = 3.4f + seed * 1.8f;
        Color pink = Color.Lerp(SakuraPink, SakuraPink * 0.86f, seed);

        Prism(basePos, height, 0.26f, 0.19f, 6, Wood);
        Vector3 top = basePos + new Vector3(0.45f + seed * 0.4f, height, 0.2f);
        Prism(top - new Vector3(0f, height * 0.5f, 0f), height * 0.5f, 0.16f, 0.1f, 5, Wood);

        Ball(top + Vector3.up * 0.8f, 2.4f, pink, 8, 0.5f, seed);
        Ball(top + new Vector3(1.2f, 0.2f, 0.4f), 1.5f, pink * 0.94f, 6, 0.5f, seed + 0.3f);
        Ball(top + new Vector3(-1.0f, 0.1f, -0.5f), 1.4f, pink * 1.04f, 6, 0.55f, seed + 0.7f);
    }

    // ================= 1. 町屋 =================

    /// <summary>町屋：石砌基础 + 一楼店门（暖帘 + 灯笼）+ 二楼白墙纸窗 + 黑瓦双坡顶（屋脊沿街）</summary>
    static void BuildMachiya(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float w = 5.6f + variant * 1.6f;
        float d = 4.6f + variant * 0.8f;
        float h1 = 2.7f, h2 = 2.5f;

        Box(c + Vector3.up * 0.22f, new Vector3(w + 0.5f, 0.44f, d + 0.5f), rot, StoneDark, StoneMoss);

        // 一楼：木板墙 + 店门（暖帘）
        Box(c + Vector3.up * (0.44f + h1 * 0.5f), new Vector3(w, h1, d), rot, Wood, WoodDark);
        Noren(c + rot * new Vector3(0f, 1.55f, d * 0.5f + 0.06f), new Vector3(w * 0.52f, 1.5f, 0.1f), rot);
        Box(c + rot * new Vector3(w * 0.3f, 2.2f, d * 0.5f + 0.3f), new Vector3(0.42f, 0.62f, 0.42f), rot,
            Paper, Gold);                                                                     // 门前纸灯笼

        // 一楼檐（庇）
        KawaraRoof(c + rot * new Vector3(0f, 0.44f + h1, d * 0.5f - 0.4f), w + 1.6f, 2.2f, 0.7f,
                   rot, Kawara, KawaraDk);

        // 二楼：白灰泥墙 + 纸窗
        ShojiWall(c + rot * new Vector3(0f, 0.44f + h1 + h2 * 0.5f, 0f),
                  new Vector3(w * 0.94f, h2, d * 0.94f), rot, (int)(c.x + c.z) & 1023);

        // 屋顶：屋脊沿 Z（也就是"顺着街"）
        KawaraRoof(c + rot * new Vector3(0f, 0.44f + h1 + h2, 0f), w + 2.0f, d + 1.7f, w * 0.32f,
                   rot, Kawara, KawaraDk);

        Houses.Add(new Vector4(c.x, c.z, Mathf.Max(w, d) * 0.5f + 1.2f, 0f));
    }

    // ================= 2. 将军府（天守） =================

    /// <summary>
    /// 将军府：稻妻的主城 —— 石垣台基 + 三层逐层收分的白墙黑瓦天守 + 最上层的望楼与金鯱。
    /// 参考原神里稻妻城那座天守阁：白壁、黑瓦、金饰，靠"收分"和"深出檐"立住气势。
    /// </summary>
    static void BuildTenshu(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float s = 0.9f + variant * 0.25f;                // 整体缩放

        // ---------- 石垣台基 ----------
        Ishigaki(c, 30f * s, 22f * s, 9f * s, rot);

        // 高台前的广场铺地（七天神像 · 雷 就立在这块石板上）
        // ★ 用**方板**不用 Disc：Disc 是一圈扇形三角，套上石墙贴图会拉出一片歪斜的砖纹
        //   （实机截图里那块铺地就是一片奇怪的网格）。方板的 UV 是正的，石纹才是石纹
        Box(c + Vector3.up * 0.16f, new Vector3(62f * s, 0.32f, 62f * s), rot, Stone, StoneDark);
        Box(c + Vector3.up * 0.34f, new Vector3(46f * s, 0.2f, 46f * s), rot, StoneDark, StoneDark);

        Vector3 top = c + Vector3.up * (9f * s);

        // 台基上的外郭（白墙 + 木柱 + 一圈深出檐）
        // ★ 每层是"矮墙 + 陡瓦顶"：天守的瓦顶坡度 30° 上下（rise ≈ 0.5 × 半宽），
        //   墙高只占三分之一 —— 原来墙 5 米多、瓦顶只有 2.6 米（约 13°），从下面看就是几块黑板飘着
        float w1 = 19f * s, h1 = 4.6f * s;
        Box(top + Vector3.up * (h1 * 0.5f), new Vector3(w1, h1, w1), rot, Plaster, PlasterSh);
        for (int i = 0; i < 4; i++)                       // 四角的木柱
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var corner = rot * Quaternion.Euler(0f, i * 90f, 0f) * new Vector3(sx * (w1 * 0.47f), 0f, w1 * 0.47f);
                Box(top + corner + Vector3.up * (h1 * 0.5f), new Vector3(0.5f, h1, 0.5f), rot, WoodDark, WoodDark);
            }

        Box(top + Vector3.up * (h1 - 0.3f), new Vector3(w1 + 0.5f, 0.6f, w1 + 0.5f), rot, WoodDark, WoodDark);   // 檐下木带
        KawaraRoof(top + Vector3.up * h1, w1 + 3.4f * s, w1 + 3.4f * s, 4.8f * s, rot, Kawara, KawaraDk);

        // ---------- 二层 ----------
        Vector3 l2 = top + Vector3.up * (h1 + 4.6f * s);
        float w2 = 13f * s, h2 = 4.0f * s;
        Box(l2 + Vector3.up * (h2 * 0.5f), new Vector3(w2, h2, w2), rot, Plaster, PlasterSh);
        for (int i = 0; i < 4; i++)
            ShojiWall(l2 + rot * Quaternion.Euler(0f, i * 90f, 0f) * new Vector3(0f, h2 * 0.5f, w2 * 0.5f + 0.02f),
                      new Vector3(w2 * 0.62f, h2 * 0.62f, 0.12f),
                      rot * Quaternion.Euler(0f, i * 90f, 0f), i * 7 + 3);

        Box(l2 + Vector3.up * (h2 - 0.25f), new Vector3(w2 + 0.4f, 0.5f, w2 + 0.4f), rot, WoodDark, WoodDark);
        KawaraRoof(l2 + Vector3.up * h2, w2 + 3.0f * s, w2 + 3.0f * s, 4.0f * s, rot, Kawara, KawaraDk);

        // ---------- 三层（望楼）：四面开窗 + 高栏 ----------
        Vector3 l3 = l2 + Vector3.up * (h2 + 3.8f * s);
        float w3 = 8.4f * s, h3 = 3.4f * s;
        Box(l3 + Vector3.up * (h3 * 0.5f), new Vector3(w3, h3, w3), rot, Plaster, PlasterSh);
        for (int i = 0; i < 4; i++)
        {
            var side = rot * Quaternion.Euler(0f, i * 90f, 0f);
            Box(l3 + side * new Vector3(0f, h3 * 0.55f, w3 * 0.5f + 0.06f), new Vector3(w3 * 0.56f, h3 * 0.5f, 0.12f),
                side, Paper, Paper);
        }

        // 高栏（三层的回廊栏杆）
        Box(l3 + Vector3.up * 0.42f, new Vector3(w3 + 1.7f * s, 0.24f, w3 + 1.7f * s), rot, Vermilion, VermDark);
        for (int i = 0; i < 4; i++)
        {
            var side = rot * Quaternion.Euler(0f, i * 90f, 0f);
            Box(l3 + side * new Vector3(0f, 0.86f, (w3 + 1.7f * s) * 0.5f),
                new Vector3(w3 + 1.7f * s, 0.18f, 0.16f), side, Vermilion, VermDark);
        }

        // ---------- 顶层大屋顶 + 金鯱 ----------
        Vector3 roof = l3 + Vector3.up * h3;
        KawaraRoof(roof, w3 + 3.6f * s, w3 + 4.0f * s, 4.4f * s, rot, Kawara, KawaraDk);

        // 金鯱（脊上的两条金鱼，天守的招牌）
        for (int s2 = -1; s2 <= 1; s2 += 2)
        {
            Vector3 f = roof + rot * new Vector3(0f, 4.8f * s, s2 * (w3 * 0.5f + 1.8f * s));
            Box(f, new Vector3(0.6f * s, 2.0f * s, 1.1f * s), rot, Gold, Gold);
            Ball(f + Vector3.up * (1.2f * s), 0.5f * s, Gold, 6, 1f, 0f);
        }

        Flames.Add(roof + Vector3.up * (6.0f * s));      // 檐角的雷元素灯（夜里能看见）
    }

    // ================= 3. 鸟居 =================

    static void BuildTorii(Vector3 c, float yaw, float variant) =>
        ToriiGate(c, yaw, 0.9f + variant * 0.6f);

    // ================= 4. 神社 =================

    /// <summary>神社：正面的鸟居 + 拜殿（千鸟破风 + 注连绳）+ 两侧石灯笼 + 后方的本殿</summary>
    static void BuildShrine(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        ToriiGate(c + rot * new Vector3(0f, 0f, 10f), yaw, 0.85f);

        // 参道（石板）
        Box(c + rot * new Vector3(0f, 0.06f, 4f), new Vector3(3.0f, 0.14f, 13f), rot, Stone, StoneDark);

        // 拜殿：台基 + 木身 + 白墙 + 千鸟破风
        Vector3 hall = c + rot * new Vector3(0f, 0f, -1.5f);
        Box(hall + Vector3.up * 0.25f, new Vector3(8.4f, 0.5f, 6.6f), rot, StoneDark, StoneMoss);
        Box(hall + Vector3.up * (0.5f + 1.7f), new Vector3(7.6f, 3.4f, 5.8f), rot, Wood, WoodDark);
        ShojiWall(hall + rot * new Vector3(0f, 2.4f, 2.9f), new Vector3(5.2f, 1.9f, 0.16f), rot, 11);

        // 屋顶（千鸟破风：正面那道三角山花抬高一点）
        KawaraRoof(hall + Vector3.up * 3.9f, 10.6f, 8.4f, 3.2f, rot, Kawara, KawaraDk);
        Box(hall + rot * new Vector3(0f, 5.4f, 3.4f), new Vector3(0.4f, 2.0f, 6.2f), rot, Plaster, PlasterSh);

        // 注连绳（挂在拜殿前檐）
        Box(hall + rot * new Vector3(0f, 3.5f, 3.3f), new Vector3(5.6f, 0.26f, 0.26f), rot, Rice, Rice);
        for (int i = -2; i <= 2; i++)
            Box(hall + rot * new Vector3(i * 1.2f, 3.1f, 3.35f), new Vector3(0.24f, 0.7f, 0.1f), rot, Paper, Paper);

        // 两侧石灯笼
        for (int s = -1; s <= 1; s += 2)
            StoneLanternAt(c + rot * new Vector3(s * 5.0f, 0f, 3.2f), yaw, flame: s > 0);

        Flames.Add(hall + rot * new Vector3(0f, 2.6f, 3.0f));
    }

    // ================= 5. 五重塔 =================

    /// <summary>五重塔：逐层收分的塔身 + 五层深出檐 + 顶上的相轮（九轮）</summary>
    static void BuildPagoda(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float s = 0.85f + variant * 0.3f;

        Box(c + Vector3.up * 0.4f, new Vector3(11f * s, 0.8f, 11f * s), rot, StoneDark, StoneMoss);   // 基座

        float y = 0.8f;
        float w = 9.2f * s;
        for (int t = 0; t < 5; t++)
        {
            float h = (3.4f - t * 0.28f) * s;
            Box(c + Vector3.up * (y + h * 0.5f), new Vector3(w * 0.86f, h, w * 0.86f), rot, Plaster, PlasterSh);
            for (int i = 0; i < 4; i++)                    // 每层的木柱 + 栏杆
            {
                var side = rot * Quaternion.Euler(0f, i * 90f, 0f);
                Box(c + side * new Vector3(0f, y + h * 0.5f, w * 0.43f), new Vector3(w * 0.5f, h * 0.9f, 0.3f),
                    side, Vermilion, VermDark);
            }

            KawaraRoof(c + Vector3.up * (y + h), w + 2.6f * s, w + 2.6f * s, 1.9f * s, rot, Kawara, KawaraDk);

            y += h + 0.5f * s;
            w *= 0.86f;
        }

        // 相轮（塔顶的九轮塔刹）
        float top = y + 1.0f * s;
        Cyl(c + Vector3.up * top, 3.6f * s, 0.26f, 0.2f, 8, Gold);
        for (int i = 0; i < 9; i++)
            Disc(c + Vector3.up * (top + 0.4f * s + i * 0.34f * s), 0.28f, 0.62f, 8, rot, Gold);
        Ball(c + Vector3.up * (top + 3.9f * s), 0.34f * s, Gold, 6, 1f, 0f);

        Flames.Add(c + Vector3.up * (top + 3.0f * s));
    }

    // ================= 6. 望楼 =================

    /// <summary>望楼：石垣小台 + 木构箭楼 + 黑瓦顶 + 太鼓 + 高栏（村口 / 山头的小地标）</summary>
    static void BuildWatchtower(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        Ishigaki(c, 7.4f, 6.0f, 2.4f, rot);
        Vector3 t = c + Vector3.up * 2.4f;

        Box(t + Vector3.up * 1.5f, new Vector3(4.6f, 3.0f, 4.6f), rot, Wood, WoodDark);
        ShojiWall(t + rot * new Vector3(0f, 1.6f, 2.35f), new Vector3(3.0f, 1.5f, 0.16f), rot, 5);

        KawaraRoof(t + Vector3.up * 3.0f, 7.0f, 6.4f, 2.0f, rot, Kawara, KawaraDk);

        Cyl(t + rot * new Vector3(1.5f, 0.2f, 0f), 1.4f, 0.9f, 0.9f, 8, Wood);                 // 太鼓
        Box(t + rot * new Vector3(-1.6f, 3.2f, 0f), new Vector3(2.2f, 1.6f, 0.1f), rot, Cloth, Cloth);   // 旗
        Cyl(t + rot * new Vector3(-1.6f, 0.2f, 0f), 4.6f, 0.12f, 0.1f, 6, Wood);
    }

    // ================= 7. 茶屋 =================

    /// <summary>茶屋：木屋 + 缘侧（外廊）+ 暖帘 + 纸灯笼 + 长凳</summary>
    static void BuildTeahouse(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float w = 5.0f + variant * 1.0f, d = 4.2f, h = 2.8f;

        Box(c + Vector3.up * 0.2f, new Vector3(w + 0.6f, 0.4f, d + 0.6f), rot, StoneDark, StoneMoss);
        Box(c + Vector3.up * (0.4f + h * 0.5f), new Vector3(w, h, d), rot, Wood, WoodDark);
        ShojiWall(c + rot * new Vector3(0f, 0.4f + h * 0.55f, d * 0.5f), new Vector3(w * 0.7f, h * 0.5f, 0.14f), rot, 9);

        // 缘侧（外廊）+ 高栏
        Box(c + rot * new Vector3(0f, 0.9f, d * 0.5f + 0.9f), new Vector3(w * 0.9f, 0.18f, 1.8f), rot, WoodLight, Wood);
        for (int s = -1; s <= 1; s += 2)
            Cyl(c + rot * new Vector3(s * w * 0.4f, 0f, d * 0.5f + 1.6f), 0.9f, 0.1f, 0.09f, 6, Wood);

        KawaraRoof(c + Vector3.up * (0.4f + h), w + 2.2f, d + 2.0f, w * 0.34f, rot, Kawara, KawaraDk);

        Noren(c + rot * new Vector3(0f, 1.6f, d * 0.5f + 0.06f), new Vector3(w * 0.5f, 1.3f, 0.1f), rot);
        Box(c + rot * new Vector3(w * 0.42f, 2.1f, d * 0.5f + 0.3f), new Vector3(0.4f, 0.6f, 0.4f), rot, Paper, Gold);

        Flames.Add(c + rot * new Vector3(0f, 1.1f, 0f));       // 炉子
        Houses.Add(new Vector4(c.x, c.z, Mathf.Max(w, d) * 0.5f + 1.0f, 0f));
    }

    // ================= 8. 木桥（太鼓桥） =================

    /// <summary>太鼓桥：拱形木桥面 + 两侧高栏 + 桥墩。span = 半跨（从桥心到岸）</summary>
    static void BuildBridge(Vector3 c, float yaw, float variant, float span)
    {
        if (span < 6f) span = 14f;
        var rot = Quaternion.Euler(0f, yaw, 0f);
        const float halfW = 2.2f;

        float rise = span * 0.30f;
        int n = Mathf.Max(4, Mathf.RoundToInt(span * 0.9f));

        // 桥面：一段段木板沿拱线排（每段都朝切线方向）
        for (int i = 0; i < n; i++)
        {
            float a0 = Mathf.PI * (1f - (float)i / n);
            float a1 = Mathf.PI * (1f - (float)(i + 1) / n);
            Vector3 p0 = new Vector3(Mathf.Cos(a0) * span, Mathf.Sin(a0) * rise, 0f);
            Vector3 p1 = new Vector3(Mathf.Cos(a1) * span, Mathf.Sin(a1) * rise, 0f);
            Vector3 mid = (p0 + p1) * 0.5f;
            float len = Vector3.Distance(p0, p1) * 1.15f;
            float tang = Mathf.Atan2(p1.y - p0.y, p1.x - p0.x) * Mathf.Rad2Deg;

            var deckRot = rot * Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(0f, 0f, tang);
            Box(c + rot * new Vector3(0f, mid.y, -mid.x), new Vector3(len, 0.34f, halfW * 2f), deckRot,
                WoodLight, Wood);
        }

        // 高栏：立柱 + 两道横杆（沿拱线）
        for (int s = -1; s <= 1; s += 2)
        {
            for (int i = 0; i <= n; i += 2)
            {
                float a = Mathf.PI * (1f - (float)i / n);
                Vector3 p = new Vector3(Mathf.Cos(a) * span, Mathf.Sin(a) * rise, s * halfW);
                Box(c + rot * new Vector3(0f, p.y + 0.55f, -p.x), new Vector3(0.2f, 1.1f, 0.2f), rot,
                    Vermilion, VermDark);
            }
        }

        // 桥台
        for (int s = -1; s <= 1; s += 2)
            Box(c + rot * new Vector3(0f, 0.35f, -s * (span + 0.6f)), new Vector3(halfW * 2f + 0.8f, 1.6f, 2.4f),
                rot, StoneDark, StoneMoss);
    }

    // ================= 9. 码头 =================

    /// <summary>码头：木栈桥 + 系船柱 + 货箱 + 一条小船（建在港湾的水面上）</summary>
    static void BuildDock(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float len = 22f, wide = 5.0f, deck = OpenWorld.WaterLevel + 1.4f;

        // 桥面
        Box(c + Vector3.up * deck, new Vector3(wide, 0.4f, len), rot, WoodLight, Wood);

        // 桥桩（扎进水里）
        for (int i = -2; i <= 2; i++)
            for (int s = -1; s <= 1; s += 2)
                Box(c + rot * new Vector3(s * (wide * 0.5f - 0.4f), deck * 0.5f - 0.2f, i * (len * 0.22f)),
                    new Vector3(0.42f, deck, 0.42f), rot, Wood, WoodDark);

        for (int i = -2; i <= 2; i++)                          // 系船柱
            Cyl(c + rot * new Vector3(wide * 0.5f - 0.5f, deck + 0.2f, i * (len * 0.24f)),
                1.1f, 0.28f, 0.24f, 6, Wood, true);

        // 货箱 + 鱼篓
        Box(c + rot * new Vector3(-1.0f, deck + 0.7f, 5f), new Vector3(1.2f, 1.0f, 1.2f), rot, WoodLight, Wood);
        Box(c + rot * new Vector3(-1.0f, deck + 1.6f, 5f), new Vector3(0.9f, 0.8f, 0.9f), rot, Wood, WoodDark);
        for (int i = 0; i < 3; i++)
            Cyl(c + rot * new Vector3(1.2f, deck + 0.2f, 3f + i * 1.1f), 0.7f, 0.46f, 0.52f, 8, Wood, true);

        // 小船（停在栈桥旁）
        Vector3 b = c + rot * new Vector3(wide * 0.5f + 2.6f, OpenWorld.WaterLevel + 0.55f, -3f);
        Box(b, new Vector3(1.9f, 0.8f, 8.5f), rot, Wood, WoodDark);
        Box(b + Vector3.up * 0.55f, new Vector3(1.3f, 0.3f, 7.0f), rot, WoodLight, Wood);
        Prism(b + Vector3.up * 0.7f, 1.2f, 0.9f, 0.7f, 6, Cloth);         // 乌篷
        Cyl(b + rot * new Vector3(0f, 0.7f, 2.4f), 3.2f, 0.09f, 0.08f, 5, Wood);   // 桅杆

        Flames.Add(c + rot * new Vector3(-1.6f, deck + 0.5f, -6f));
        Cyl(c + rot * new Vector3(-1.6f, deck + 0.2f, -6f), 0.6f, 0.5f, 0.6f, 8, StoneDark, true);   // 火盆
    }

    // ================= 10. 樱花林 =================

    /// <summary>一丛樱花：3~5 棵，按固定哈希散开（岛上的固定景观 / 城镇绿化都用它）</summary>
    static void BuildSakuraGrove(Vector3 c, float variant)
    {
        int n = 3 + Mathf.RoundToInt(variant * 2f);

        for (int i = 0; i < n; i++)
        {
            float a = H(i, (int)(c.x + c.z), 41) * Mathf.PI * 2f;
            float r = 2.2f + H(i, (int)(c.x + c.z), 42) * 5.0f;
            var p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            SakuraTree(p, H(i, (int)(c.x + c.z), 43));
        }

        Houses.Add(new Vector4(c.x, c.z, 7f, 0f));
    }
}
