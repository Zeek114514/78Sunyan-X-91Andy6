using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 璃月的建筑种类（和蒙德的 <see cref="WildKind"/> 一一对应，但风格完全不同）。
/// 顺序就是选点、地图标记、日志统计里的顺序，不要随便插队。
/// </summary>
public enum LiyueKind : byte
{
    Village = 0,     // 璃月村落：白墙黛瓦 + 马头墙 + 祠堂 + 晒谷场
    Pagoda,          // 楼阁：三层塔，逐层收小的中式屋顶
    Paifang,         // 牌坊：四柱三间 + 层叠额枋 + 顶部小屋顶
    StiltHouse,      // 吊脚楼：木柱扎到水下，悬挑木屋（建在水边）
    ArchBridge,      // 石拱桥：半圆拱圈 + 桥面 + 石栏板 + 抱鼓石
    TeaStall,        // 茶棚：四柱 + 中式屋顶 + 桌椅 + 幌子
    WaterMill,       // 水车：大水车 + 木槽 + 中式小屋
    Shrine,          // 岩神像：圆形石台 + 石像 + 四角石灯 + 香炉
    StoneForest,     // 石林遗迹：残破石柱 / 石阶 + 摩崖石刻
    TerraceHut,      // 梯田农舍：小民居 + 农具 + 稻草堆
    Dock,            // 码头：木栈桥 + 系船柱 + 小船 + 鱼篓
}

/// <summary>
/// 璃月建筑的几何生成：红柱黛瓦、飞檐翘角、牌坊楼阁、吊脚楼、石拱桥、红灯笼。
///
/// 和蒙德那套（<see cref="WildBuildings"/>）结构完全同构：
///   · 三路子网格（0 石墙贴图 / 1 屋顶贴图 / 2 顶点色）—— 璃月只用得上 0 和 2，
///     中式黛瓦走**顶点色平涂**（蒙德那套红瓦贴图配黛瓦会变成脏红棕色，不如直接平涂干净）
///   · 全部低模 + 顶点色，和地形、蒙德建筑共用同一套材质
///   · 固定哈希，每次进游戏一样
///
/// 撑起"一眼看出是中式"的是四个零件：<see cref="ChineseRoof"/>（飞檐翘角）、
/// <see cref="RedColumn"/>（朱红柱 + 斗拱）、<see cref="Lantern"/>（红灯笼）、
/// <see cref="HorseHeadWall"/>（马头墙）。其余都是常规低模。
/// </summary>
public static partial class LiyueBuildings
{
    // ---------- 璃月配色（和蒙德那套刻意拉开） ----------

    static readonly Color Vermilion     = new Color32(0xb2, 0x3a, 0x2e, 255);   // 朱红（柱、门、栏）
    static readonly Color VermilionDark = new Color32(0x8e, 0x2c, 0x24, 255);
    static readonly Color Tile          = new Color32(0x4a, 0x4f, 0x5a, 255);   // 黛瓦
    static readonly Color TileDark      = new Color32(0x35, 0x39, 0x43, 255);
    static readonly Color Ridge         = new Color32(0x2a, 0x2d, 0x35, 255);   // 屋脊 / 脊头
    static readonly Color WallWhite     = new Color32(0xf2, 0xed, 0xe3, 255);   // 白墙
    static readonly Color WallShade     = new Color32(0xdc, 0xd5, 0xc7, 255);
    static readonly Color Wood          = new Color32(0x5a, 0x3f, 0x2b, 255);   // 深木
    static readonly Color WoodLight     = new Color32(0x7d, 0x5c, 0x3d, 255);
    static readonly Color Bamboo        = new Color32(0x7f, 0xa6, 0x5a, 255);
    static readonly Color Limestone     = new Color32(0xb9, 0xb2, 0xa4, 255);   // 石灰岩
    static readonly Color LimestoneDark = new Color32(0x96, 0x90, 0x84, 255);
    static readonly Color LanternRed    = new Color32(0xd8, 0x41, 0x2f, 255);
    static readonly Color Gold          = new Color32(0xd9, 0xb4, 0x5c, 255);
    static readonly Color Cloth         = new Color32(0xc8, 0xa8, 0x72, 255);
    static readonly Color Rice          = new Color32(0xd8, 0xc4, 0x86, 255);

    // ---------- 生成状态 ----------

    static readonly List<Vector3> _v = new List<Vector3>();
    static readonly List<Color>   _c = new List<Color>();

    /// <summary>三个子网格：0 石墙（贴图）/ 1 屋顶瓦片（璃月不用）/ 2 其余（顶点色）</summary>
    public static readonly List<int>[] SubTris = { new List<int>(), new List<int>(), new List<int>() };

    /// <summary>需要放"火苗"的位置（茶棚的炉子、码头的火盆）</summary>
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
    public static float Footprint(LiyueKind kind)
    {
        switch (kind)
        {
            case LiyueKind.Village:     return 30f;
            case LiyueKind.Pagoda:      return 13f;
            case LiyueKind.Paifang:     return 9f;
            case LiyueKind.StiltHouse:  return 11f;
            case LiyueKind.ArchBridge:  return 18f;
            case LiyueKind.TeaStall:    return 7f;
            case LiyueKind.WaterMill:   return 10f;
            case LiyueKind.Shrine:      return 12f;
            case LiyueKind.StoneForest: return 14f;
            case LiyueKind.TerraceHut:  return 8f;
            case LiyueKind.Dock:        return 12f;
            default:                    return 10f;
        }
    }

    /// <summary>地图标记种类（30 起，避开蒙德那套 14~24）</summary>
    public static byte MarkerKind(LiyueKind kind) => (byte)(30 + (int)kind);

    public static string DisplayName(LiyueKind kind)
    {
        switch (kind)
        {
            case LiyueKind.Village:     return "璃月村落";
            case LiyueKind.Pagoda:      return "楼阁";
            case LiyueKind.Paifang:     return "牌坊";
            case LiyueKind.StiltHouse:  return "吊脚楼";
            case LiyueKind.ArchBridge:  return "石拱桥";
            case LiyueKind.TeaStall:    return "茶棚";
            case LiyueKind.WaterMill:   return "水车";
            case LiyueKind.Shrine:      return "岩神像";
            case LiyueKind.StoneForest: return "石林遗迹";
            case LiyueKind.TerraceHut:  return "梯田农舍";
            case LiyueKind.Dock:        return "码头";
            default:                    return "璃月建筑";
        }
    }

    /// <summary>地图色块颜色：0 红顶 / 1 石板浅色 / 2 木质深色（璃月的黛瓦走 2）</summary>
    public static byte BlobColor(LiyueKind kind) => kind == LiyueKind.Village ? (byte)1 : (byte)2;

    /// <summary>把一栋（一组）建筑的几何写进列表。param 是少数几种需要的额外尺寸（石拱桥用它传半跨长）</summary>
    public static void Append(LiyueKind kind, Vector3 center, float yaw, float variant, float param = 0f)
    {
        switch (kind)
        {
            case LiyueKind.Village:     BuildVillage(center, yaw, variant); break;
            case LiyueKind.Pagoda:      BuildPagoda(center, yaw, variant); break;
            case LiyueKind.Paifang:     BuildPaifang(center, yaw, variant); break;
            case LiyueKind.StiltHouse:  BuildStiltHouse(center, yaw, variant); break;
            case LiyueKind.ArchBridge:  BuildArchBridge(center, yaw, variant, param); break;
            case LiyueKind.TeaStall:    BuildTeaStall(center, yaw, variant); break;
            case LiyueKind.WaterMill:   BuildWaterMill(center, yaw, variant); break;
            case LiyueKind.Shrine:      BuildShrine(center, yaw, variant); break;
            case LiyueKind.StoneForest: BuildStoneForest(center, yaw, variant); break;
            case LiyueKind.TerraceHut:  BuildTerraceHut(center, yaw, variant); break;
            case LiyueKind.Dock:        BuildDock(center, yaw, variant); break;
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

    /// <summary>苏州/徽派那套：只有石灰岩走石墙贴图，黛瓦走顶点色平涂
    /// （蒙德的红瓦贴图配黛瓦会变成脏红棕色，不如纯平涂干净）</summary>
    static List<int> T(Color color)
    {
        if (Same(color, Limestone) || Same(color, LimestoneDark)) return SubTris[0];
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

    // ================= 中式零件（撑起风格的四个核心） =================

    /// <summary>
    /// 一块"带厚度的四边形板"：给四个角（按顺序绕一圈），自动补出顶面 / 底面 / 四个侧面。
    /// 中式屋顶的每个坡面都是这么一块板拼的。
    /// ★ 必须带厚度：单面片会被背面剔除，从屋檐底下往上看就是透明的。
    /// </summary>
    static void ThickQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float thickness, Color top, Color side)
    {
        Vector3 n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
        Vector3 off = -n * thickness;

        var t = T(top);

        // 顶面
        int b = _v.Count;
        Add(p0, top); Add(p1, top); Add(p2, top); Add(p3, top);
        t.Add(b); t.Add(b + 1); t.Add(b + 2);
        t.Add(b); t.Add(b + 2); t.Add(b + 3);

        // 底面（反向绕序）
        int b2 = _v.Count;
        Add(p0 + off, side); Add(p3 + off, side); Add(p2 + off, side); Add(p1 + off, side);
        t.Add(b2); t.Add(b2 + 1); t.Add(b2 + 2);
        t.Add(b2); t.Add(b2 + 2); t.Add(b2 + 3);

        // 四个侧面（每条边一块，绕序保证法线朝外）
        Vector3[] q = { p0, p1, p2, p3 };
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = q[i], bb = q[(i + 1) % 4];
            int b3 = _v.Count;
            Add(a, side); Add(a + off, side); Add(bb + off, side); Add(bb, side);
            t.Add(b3); t.Add(b3 + 1); t.Add(b3 + 2);
            t.Add(b3); t.Add(b3 + 2); t.Add(b3 + 3);
        }

        void Add(Vector3 p, Color col)
        {
            _v.Add(p);
            _c.Add(col.linear);
        }
    }

    /// <summary>
    /// 中式坡屋顶（硬山 / 歇山）：屋脊 + 两坡 + **两端起翘的飞檐** + 脊头小兽 + 四角翘角。
    ///
    /// 飞檐的做法：把每个坡面沿进深切成 3 段 —— 中间那段的檐口在低位，两端那两段檐口往上抬 lift，
    /// 于是整条檐线从中间往两头翘起来。再用四块斜向上外挑的小楔形块点出翘角。
    /// 低模里这样最省，又能一眼认出是中式屋顶（而不是西式的直檐双坡）。
    ///
    /// 屋脊沿局部 Z 轴，两坡朝 ±X 方向落。
    /// </summary>
    static void ChineseRoof(Vector3 c, float w, float d, float h, float lift, Quaternion rot,
                            Color tile, Color ridge)
    {
        // ★ 每一坡就是一整块板（ThickQuad）：曾经把它切成三段去拼"檐口起翘"，
        //   段与段之间必然露出台阶 —— 从屋檐底下往上看，就是一堆错开的黑板（实机里那种"飘着的黑屋顶"）。
        //   现在：直檐 + 四角单独翘（lift 用来定四个翘角挑多高，檐线本身是直的）
        const float thick = 0.34f;
        float hw = w * 0.5f, hd = d * 0.5f;

        for (int sx = -1; sx <= 1; sx += 2)
        {
            if (sx > 0)
            {
                // 右坡：从 +n 方向看是逆时针（法线朝上偏外）
                ThickQuad(c + rot * new Vector3(0f, h, -hd),
                          c + rot * new Vector3(0f, h, hd),
                          c + rot * new Vector3(hw, lift, hd),
                          c + rot * new Vector3(hw, lift, -hd),
                          thick, tile, tile * 0.80f);
            }
            else
            {
                // 左坡：顶点顺序反过来，法线才朝外
                ThickQuad(c + rot * new Vector3(-hw, lift, -hd),
                          c + rot * new Vector3(-hw, lift, hd),
                          c + rot * new Vector3(0f, h, hd),
                          c + rot * new Vector3(0f, h, -hd),
                          thick, tile, tile * 0.80f);
            }
        }

        // 屋脊 + 两端的脊头小兽
        Box(c + rot * new Vector3(0f, h + 0.12f, 0f), new Vector3(0.4f, 0.28f, d + 0.24f), rot, ridge, ridge);
        for (int s = -1; s <= 1; s += 2)
            Box(c + rot * new Vector3(0f, h + 0.4f, s * (hd + 0.12f)), new Vector3(0.28f, 0.5f, 0.28f),
                rot * Quaternion.Euler(-16f * s, 0f, 0f), ridge, ridge);

        // 四角翘角：斜向上外挑的小楔块（中式屋顶最有辨识度的一笔）
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var cornerRot = rot
                              * Quaternion.Euler(0f, Mathf.Atan2(sx, sz) * Mathf.Rad2Deg, 0f)
                              * Quaternion.Euler(-34f, 0f, 0f);
                Box(c + rot * new Vector3(sx * (hw - 0.25f), lift + 0.12f, sz * (hd - 0.25f)),
                    new Vector3(0.24f, 0.24f, 1.05f), cornerRot, ridge, ridge);
            }
        }
    }

    /// <summary>朱红柱：石础 + 朱红柱身 + 柱顶斗拱（三层依次外挑的小方块）。
    /// 斗拱是中式木构最醒目的特征，三层方块就够示意</summary>
    static void RedColumn(Vector3 basePos, float height, float radius, Quaternion rot,
                          Color column, Color stone, Color bracket)
    {
        Cyl(basePos, 0.3f, radius * 1.4f, radius * 1.15f, 8, stone, true);
        Cyl(basePos + Vector3.up * 0.3f, height, radius, radius * 0.92f, 8, column);
        Cyl(basePos + Vector3.up * (0.3f + height), 0.12f, radius * 1.14f, radius * 1.0f, 8, bracket, true);

        for (int i = 0; i < 3; i++)
        {
            float s = 1.5f + i * 0.5f;
            Box(basePos + Vector3.up * (0.44f + height + i * 0.16f), new Vector3(s, 0.14f, s), rot, bracket, bracket);
        }
    }

    /// <summary>红灯笼：吊线 + 上下箍 + 椭球灯身 + 下面一撮流苏</summary>
    static void Lantern(Vector3 top, float size, Color body, Color cap)
    {
        Box(top + Vector3.up * -0.32f, new Vector3(0.05f, 0.64f, 0.05f), cap, cap);
        Cyl(top + Vector3.up * -0.7f, 0.12f, size * 0.4f, size * 0.4f, 6, cap, true);
        Ball(top + Vector3.up * -(0.76f + size * 0.5f), size * 0.6f, body, 7, 1.2f, 0f);
        Cyl(top + Vector3.up * -(0.76f + size * 1.1f), 0.1f, size * 0.38f, size * 0.38f, 6, cap, true);
        Box(top + Vector3.up * -(0.9f + size * 1.1f), new Vector3(0.07f, 0.32f, 0.07f), cap, cap);
    }

    /// <summary>马头墙：山墙做成三级台阶状（江南/徽派民居最醒目的特征）。
    /// 沿着 ±Z 两个山墙面各起一排阶梯墙，比屋顶还高一点</summary>
    static void HorseHeadWall(Vector3 c, Quaternion rot, float w, float d, float wallH, Color wall, Color cap)
    {
        for (int sz = -1; sz <= 1; sz += 2)
        {
            for (int k = 0; k < 3; k++)
            {
                float stepW = w * (0.46f - k * 0.13f);
                float stepY = wallH + 0.5f + k * 0.62f;
                Vector3 p = c + rot * new Vector3(0f, stepY, sz * (d * 0.5f + 0.13f));

                Box(p, new Vector3(stepW, 0.9f, 0.26f), rot, wall, wall * 0.92f);
                // 每级顶上一道压顶瓦
                Box(p + Vector3.up * 0.5f, new Vector3(stepW + 0.16f, 0.14f, 0.34f), rot, cap, cap);
            }
        }
    }

    /// <summary>石拱桥：半圆拱圈（楔形块拼）+ 桥面 + 石栏板 + 两端抱鼓石 + 两头引桥。
    /// span = 半跨（从桥心到岸），rise = 起拱高度</summary>
    static void StoneArchBridge(Vector3 c, float yaw, float span, float rise)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float rx = span, ry = rise;
        const int N = 11;
        const float halfW = 2.4f;

        // ★ 拱圈的方向必须和桥面一致：桥面 / 栏杆 / 引桥都是沿本地 Z 铺开的，
        //   所以拱圈也要从"本地 XY 平面"绕 Y 转 90° 摆进"本地 ZY 平面" ——
        //   否则拱会横着架在桥面正下方（侧看像两座桥叠在一起，桥洞还能看穿）
        var archRot = rot * Quaternion.Euler(0f, 90f, 0f);

        // 拱圈：楔形块沿椭圆排一圈
        for (int i = 0; i < N; i++)
        {
            float a0 = Mathf.PI * (1f - (float)i / N);
            float a1 = Mathf.PI * (1f - (float)(i + 1) / N);

            Vector3 p0 = new Vector3(Mathf.Cos(a0) * rx, Mathf.Sin(a0) * ry, 0f);
            Vector3 p1 = new Vector3(Mathf.Cos(a1) * rx, Mathf.Sin(a1) * ry, 0f);
            Vector3 pm = (p0 + p1) * 0.5f;

            float len = Vector3.Distance(p0, p1) * 1.18f;
            float tang = Mathf.Atan2(p1.y - p0.y, p1.x - p0.x) * Mathf.Rad2Deg;

            Box(c + rot * Zy(pm), new Vector3(len, 0.66f, halfW * 2f),
                archRot * Quaternion.Euler(0f, 0f, tang), Limestone, LimestoneDark);

            // 拱腹的券石线脚（下缘一道深色，拱形更清楚）
            Box(c + rot * Zy(pm - new Vector3(0f, 0.42f, 0f)), new Vector3(len, 0.14f, halfW * 2f + 0.14f),
                archRot * Quaternion.Euler(0f, 0f, tang), LimestoneDark, LimestoneDark);
        }

        // 桥面：整块直板（不分成拱形，免得接缝折角）
        float deckY = rise + 0.36f;
        float deckLen = span * 2f + 7f;
        Box(c + rot * new Vector3(0f, deckY, 0f), new Vector3(halfW * 2f, 0.4f, deckLen), rot,
            Limestone, LimestoneDark);

        // 引桥：两头各一块斜板落到地面
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 a = c + rot * new Vector3(0f, deckY, s * (deckLen * 0.5f - 0.3f));
            Vector3 b = c + rot * new Vector3(0f, 0.2f, s * (deckLen * 0.5f + 3.2f));
            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = b - a;
            Box(mid, new Vector3(halfW * 2f, 0.34f, dir.magnitude),
                Quaternion.LookRotation(dir.normalized, Vector3.up), Limestone, LimestoneDark);
        }

        // 石栏板：立柱 + 两道横杆 + 中间的栏板
        int posts = Mathf.Max(6, Mathf.RoundToInt(deckLen / 2.6f));
        for (int s = -1; s <= 1; s += 2)
        {
            for (int i = 0; i <= posts; i++)
            {
                float z = -deckLen * 0.5f + i * deckLen / posts;
                Box(c + rot * new Vector3(s * halfW, deckY + 0.5f, z), new Vector3(0.2f, 1.0f, 0.2f), rot,
                    Limestone, LimestoneDark);
            }
            Box(c + rot * new Vector3(s * halfW, deckY + 0.95f, 0f),
                new Vector3(0.24f, 0.18f, deckLen - 0.6f), rot, Limestone, LimestoneDark);
            Box(c + rot * new Vector3(s * halfW, deckY + 0.55f, 0f),
                new Vector3(0.16f, 0.5f, deckLen - 1.2f), rot, LimestoneDark, LimestoneDark);
        }

        // 抱鼓石：四角各一块圆鼓
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 p = c + rot * new Vector3(sx * halfW, deckY + 0.1f, sz * (deckLen * 0.5f - 0.2f));
                Cyl(p, 0.5f, 0.42f, 0.36f, 8, Limestone, true);
                Ball(p + Vector3.up * 0.78f, 0.4f, LimestoneDark, 6, 1.1f, 0f);
            }
        }
    }

    /// <summary>把"本地 XY 平面"上的点搬到"本地 ZY 平面"（绕 Y 转 90°）：
    /// 拱券按椭圆在 XY 平面上算最顺手，但桥面是沿 Z 铺的，最后要摆回 ZY 平面里去</summary>
    static Vector3 Zy(Vector3 p) => new Vector3(0f, p.y, -p.x);

    // ================= 1. 璃月村落 =================

    /// <summary>
    /// 璃月村落：一圈篱笆 + 6~8 栋白墙黛瓦民居（带马头墙）+ 祠堂（朱柱高台）+ 晒谷场 + 水井 + 石灯 + 灯笼串。
    /// 布局和蒙德村落一样按半径分层，互不重叠：村心 r&lt;7 ｜ 内圈 r=10.5 ｜ 外圈 r=19.5 ｜ 篱笆 r=25.5
    /// </summary>
    static void BuildVillage(Vector3 c, float yaw, float variant)
    {
        int seed = (int)(c.x * 7f + c.z * 13f);
        float yawRad = yaw * Mathf.Deg2Rad;

        // 地面：夯土晒场 + 村心的石板地
        Disc(c + Vector3.up * 0.05f, 0f, 26f, 24, Quaternion.identity, new Color32(0xc4, 0xb0, 0x86, 255));
        Disc(c + Vector3.up * 0.09f, 0f, 5.2f, 16, Quaternion.identity, LimestoneDark);

        const float ringA = 10.5f, ringB = 19.5f;
        int inner = 3, outer = 5;

        for (int i = 0; i < inner; i++)
        {
            float a = yawRad + i * Mathf.PI * 2f / inner + (H(i, seed, 31) - 0.5f) * 0.22f;
            var p = c + new Vector3(Mathf.Cos(a) * ringA, 0f, Mathf.Sin(a) * ringA);
            LiyueHouse(p, -a * Mathf.Rad2Deg + 90f + (H(i, seed, 33) - 0.5f) * 16f,
                       5.0f + H(i, seed, 34) * 0.8f, 4.2f + H(i, seed, 35) * 0.6f, 3.0f,
                       i);
        }

        for (int i = 0; i < outer; i++)
        {
            float a = yawRad + i * Mathf.PI * 2f / outer + 0.35f + (H(i, seed, 41) - 0.5f) * 0.18f;
            var p = c + new Vector3(Mathf.Cos(a) * ringB, 0f, Mathf.Sin(a) * ringB);

            if (i == 0)
            {
                // 外圈第一栋做成祠堂：石台基 + 朱柱 + 大屋顶 + 匾额
                AncestralHall(p, -a * Mathf.Rad2Deg + 90f);
                continue;
            }

            LiyueHouse(p, -a * Mathf.Rad2Deg + 90f + (H(i, seed, 43) - 0.5f) * 22f,
                       5.0f + H(i, seed, 44) * 0.9f, 4.2f + H(i, seed, 45) * 0.7f, 2.9f,
                       i + inner);
        }

        // 村心：水井（石圈 + 木架 + 中式小顶）
        Vector3 well = c + new Vector3(Mathf.Cos(yawRad + 1.0f) * 4.6f, 0f, Mathf.Sin(yawRad + 1.0f) * 4.6f);
        Cyl(well + Vector3.up * 0.08f, 0.8f, 1.1f, 1.0f, 12, LimestoneDark, true);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + 0.4f;
            Box(well + new Vector3(Mathf.Cos(a) * 1.2f, 1.0f, Mathf.Sin(a) * 1.2f),
                new Vector3(0.2f, 1.6f, 0.2f), Wood, Wood);
        }
        Box(well + Vector3.up * 1.8f, new Vector3(2.8f, 0.16f, 0.16f), Wood, Wood);
        ChineseRoof(well + Vector3.up * 1.95f, 3.6f, 2.8f, 0.85f, 0.35f, Quaternion.identity, Tile, Ridge);

        // 村心：石灯 + 晒谷的大竹筐
        Vector3 stone = c + new Vector3(Mathf.Cos(yawRad + 2.5f) * 5.4f, 0f, Mathf.Sin(yawRad + 2.5f) * 5.4f);
        StoneLamp(stone);

        Vector3 basket = c + new Vector3(Mathf.Cos(yawRad - 2.4f) * 5.6f, 0f, Mathf.Sin(yawRad - 2.4f) * 5.6f);
        Cyl(basket, 0.75f, 0.62f, 0.72f, 10, Bamboo, true);
        Cyl(basket + new Vector3(1.3f, 0f, 0.3f), 0.7f, 0.58f, 0.68f, 10, Bamboo, true);

        // 晒谷场上的谷堆
        for (int i = 0; i < 3; i++)
        {
            float a = yawRad + 0.6f + i * 1.6f;
            var p = c + new Vector3(Mathf.Cos(a) * 8.4f, 0f, Mathf.Sin(a) * 8.4f);
            Ball(p + Vector3.up * 0.5f, 0.9f + H(i, seed, 55) * 0.3f, Rice, 6, 0.7f, H(i, seed, 56));
        }

        // 篱笆一圈（两个出入口）：柱 + 逐段连线的横杆
        BuildFence(c, yawRad, 25.5f, seed);

        // 正门牌坊（对着主入口，一眼能认出这是个村子）
        float gateA = yawRad + 1.0f;
        Vector3 gate = c + new Vector3(Mathf.Cos(gateA) * 25.5f, 0f, Mathf.Sin(gateA) * 25.5f);
        PaifangAt(gate, -gateA * Mathf.Rad2Deg + 90f, 4.6f, 4.4f);
    }

    /// <summary>一栋璃月民居：夯土台基 + 白墙 + 深木框 + 朱红柱 + 黛瓦中式屋顶 + 马头墙 + 檐下灯笼</summary>
    static void LiyueHouse(Vector3 center, float yaw, float w, float d, float wallH, int salt)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        // 台基
        Box(center + Vector3.up * 0.2f, new Vector3(w + 0.5f, 0.4f, d + 0.5f), rot, LimestoneDark, LimestoneDark);
        // 白墙
        Box(center + Vector3.up * (0.4f + wallH * 0.5f), new Vector3(w, wallH, d), rot, WallWhite, WallShade);

        // 四角深木柱（比墙略高出一点，接屋顶）
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Box(center + rot * new Vector3(sx * (w * 0.5f - 0.05f), 0.4f + wallH * 0.5f, sz * (d * 0.5f - 0.05f)),
                    new Vector3(0.18f, wallH, 0.18f), rot, Wood, Wood);
            }
        }

        // 腰线
        Box(center + rot * new Vector3(0f, 0.4f + wallH * 0.62f, 0f), new Vector3(w + 0.12f, 0.14f, d + 0.12f),
            rot, Wood, Wood);

        // 中式屋顶（出檐 + 飞檐翘角）
        ChineseRoof(center + Vector3.up * (0.4f + wallH), w + 1.3f, d + 1.3f, w * 0.36f, 0.5f, rot, Tile, Ridge);

        // 马头墙
        HorseHeadWall(center, rot, w + 0.4f, d + 0.4f, 0.4f + wallH, WallWhite, TileDark);

        // 门（正面 +Z）：朱红门扇 + 门框
        Vector3 door = center + rot * new Vector3(0f, 0.4f + 0.9f, d * 0.5f + 0.04f);
        Box(door, new Vector3(1.1f, 1.8f, 0.12f), rot, Vermilion, VermilionDark);
        Box(door + rot * new Vector3(0f, 0.95f, -0.03f), new Vector3(1.4f, 0.16f, 0.22f), rot, Wood, Wood);
        // 门簪
        Box(door + rot * new Vector3(-0.62f, 0.35f, 0.04f), new Vector3(0.5f, 0.5f, 0.08f), rot, Gold, Gold);

        // 窗（木格栅）：前面两扇
        float wy = 0.4f + wallH * 0.58f;
        for (int s = -1; s <= 1; s += 2)
        {
            Box(center + rot * new Vector3(s * w * 0.28f, wy, d * 0.5f + 0.04f),
                new Vector3(0.7f, 0.75f, 0.1f), rot, Wood, WoodLight);
        }

        // 檐下两盏灯笼
        for (int s = -1; s <= 1; s += 2)
        {
            Lantern(center + rot * new Vector3(s * (w * 0.5f - 0.3f), 0.4f + wallH + 0.55f, d * 0.5f + 0.5f),
                    0.42f, LanternRed, Gold);
        }

        Houses.Add(new Vector4(center.x, center.z, Mathf.Max(w, d) * 0.5f + 1.4f, salt));
    }

    /// <summary>祠堂：石台基 + 一圈朱红柱 + 大中式屋顶 + 匾额（村里最气派的那栋）</summary>
    static void AncestralHall(Vector3 center, float yaw)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float w = 7.4f, d = 5.6f, wallH = 3.6f;

        // 台基（两级）
        Box(center + Vector3.up * 0.22f, new Vector3(w + 1.6f, 0.44f, d + 1.6f), rot, Limestone, LimestoneDark);
        Box(center + Vector3.up * 0.62f, new Vector3(w + 0.9f, 0.36f, d + 0.9f), rot, LimestoneDark, LimestoneDark);
        // 台阶
        Box(center + rot * new Vector3(0f, 0.3f, d * 0.5f + 1.1f), new Vector3(3.2f, 0.6f, 0.7f), rot,
            Limestone, LimestoneDark);

        float baseY = 0.8f;

        // 白墙 + 木框
        Box(center + Vector3.up * (baseY + wallH * 0.5f), new Vector3(w, wallH, d), rot, WallWhite, WallShade);

        // 一圈朱红柱 + 斗拱
        int cols = 4;
        for (int i = 0; i < cols; i++)
        {
            float x = -w * 0.5f + 0.55f + i * (w - 1.1f) / (cols - 1);
            for (int sz = -1; sz <= 1; sz += 2)
                RedColumn(center + rot * new Vector3(x, baseY, sz * (d * 0.5f + 0.28f)), wallH, 0.19f, rot,
                          Vermilion, Limestone, VermilionDark);
        }

        // 大屋顶
        ChineseRoof(center + Vector3.up * (baseY + wallH), w + 2.0f, d + 2.0f, w * 0.34f, 0.62f, rot, Tile, Ridge);

        // 匾额 + 大门
        Box(center + rot * new Vector3(0f, baseY + wallH * 0.82f, d * 0.5f + 0.08f),
            new Vector3(2.0f, 0.7f, 0.14f), rot, Wood, Wood);
        Box(center + rot * new Vector3(0f, baseY + wallH * 0.82f, d * 0.5f + 0.16f),
            new Vector3(1.7f, 0.5f, 0.06f), rot, Gold, Gold);
        Box(center + rot * new Vector3(0f, baseY + 1.15f, d * 0.5f + 0.06f),
            new Vector3(2.4f, 2.3f, 0.14f), rot, Vermilion, VermilionDark);

        // 门口一对石狮（用低模球+方块示意）
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 p = center + rot * new Vector3(s * 2.6f, 0.3f, d * 0.5f + 1.5f);
            Box(p + Vector3.up * 0.25f, new Vector3(0.5f, 0.5f, 0.5f), rot, Limestone, LimestoneDark);
            Box(p + Vector3.up * 0.85f, new Vector3(0.72f, 0.75f, 0.62f), rot, LimestoneDark, LimestoneDark);
            Ball(p + Vector3.up * 1.35f, 0.34f, Limestone, 6, 1.0f, 0f);
        }
    }

    /// <summary>石灯：柱 + 灯室 + 顶盖（中式庭院的标配小件）</summary>
    static void StoneLamp(Vector3 basePos)
    {
        Cyl(basePos, 0.25f, 0.4f, 0.34f, 6, LimestoneDark, true);
        Cyl(basePos + Vector3.up * 0.25f, 1.0f, 0.16f, 0.14f, 6, Limestone);
        Box(basePos + Vector3.up * 1.45f, new Vector3(0.44f, 0.42f, 0.44f), Limestone, Limestone);
        ChineseRoof(basePos + Vector3.up * 1.68f, 1.2f, 1.2f, 0.3f, 0.16f, Quaternion.identity, TileDark, Ridge);
    }

    /// <summary>木篱笆：先把柱子位置全算出来，再逐段连接相邻两根柱子架横杆
    /// （横杆必须按实际连线摆 —— 按切线方向 + 固定长度摆会和柱子对不上，看着就是"栅栏错位"）</summary>
    static void BuildFence(Vector3 c, float yawRad, float radius, int seed)
    {
        const int postCount = 44;
        float gateA = yawRad + 1.0f, gateB = yawRad - 1.9f;

        var posts = new List<Vector3>();
        var heights = new List<float>();

        for (int i = 0; i < postCount; i++)
        {
            float a = yawRad + i * Mathf.PI * 2f / postCount;
            if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, gateA * Mathf.Rad2Deg)) < 12f) { posts.Add(Vector3.zero); heights.Add(0f); continue; }
            if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, gateB * Mathf.Rad2Deg)) < 12f) { posts.Add(Vector3.zero); heights.Add(0f); continue; }

            float r = radius + (H(i, seed, 51) - 0.5f) * 0.35f;
            float hgt = 1.18f + H(i, seed, 52) * 0.26f;
            posts.Add(c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            heights.Add(hgt);
        }

        for (int i = 0; i < posts.Count; i++)
        {
            Vector3 p0 = posts[i], p1 = posts[(i + 1) % posts.Count];
            if (p0 == Vector3.zero || p1 == Vector3.zero) continue;

            Vector3 mid = (p0 + p1) * 0.5f;
            Vector3 dir = p1 - p0;
            float len = dir.magnitude;
            if (len < 0.05f) continue;

            float yawDeg = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            var railRot = Quaternion.Euler(0f, yawDeg, 0f);
            float top = Mathf.Min(heights[i], heights[(i + 1) % posts.Count]);

            Box(mid + Vector3.up * (top * 0.74f), new Vector3(0.16f, 0.12f, len * 1.03f), railRot, Bamboo, Bamboo * 0.85f);
            Box(mid + Vector3.up * (top * 0.40f), new Vector3(0.13f, 0.1f, len * 1.03f), railRot, Bamboo, Bamboo * 0.85f);
        }

        for (int i = 0; i < posts.Count; i++)
        {
            if (posts[i] == Vector3.zero) continue;
            float a = yawRad + i * Mathf.PI * 2f / postCount;
            Box(posts[i] + Vector3.up * (heights[i] * 0.5f), new Vector3(0.17f, heights[i], 0.17f),
                Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), Bamboo, Bamboo * 0.8f);
        }
    }

    // ================= 2. 楼阁（三层塔） =================

    static void BuildPagoda(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        // 台基
        Cyl(c + Vector3.up * 0.05f, 0.5f, 5.4f, 5.0f, 12, Limestone, true);
        Disc(c + Vector3.up * 0.56f, 0f, 4.6f, 12, Quaternion.identity, LimestoneDark);
        // 四角石灯
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            StoneLamp(c + new Vector3(Mathf.Cos(a) * 4.2f, 0.55f, Mathf.Sin(a) * 4.2f));
        }

        // 三层：逐层收小
        float[] tierW = { 6.4f, 5.2f, 4.0f };
        float[] tierH = { 3.4f, 3.0f, 2.6f };
        float y = 0.56f;

        for (int t = 0; t < 3; t++)
        {
            float w = tierW[t], h = tierH[t];

            // 一层楼身：白墙 + 一圈朱柱
            Box(c + rot * new Vector3(0f, y + h * 0.5f, 0f), new Vector3(w * 0.86f, h, w * 0.86f), rot,
                WallWhite, WallShade);
            int cols = 4;
            for (int i = 0; i < cols; i++)
            {
                float k = -w * 0.43f + 0.4f + i * (w * 0.86f - 0.8f) / (cols - 1);
                for (int sz = -1; sz <= 1; sz += 2)
                    RedColumn(c + rot * new Vector3(k, y, sz * (w * 0.43f + 0.22f)), h, 0.16f, rot,
                              Vermilion, Limestone, VermilionDark);
            }

            // 每层的中式腰檐（比楼身大一圈，出檐）
            ChineseRoof(c + rot * new Vector3(0f, y + h, 0f), w + 1.6f, w + 1.6f, w * 0.22f, 0.55f, rot, Tile, Ridge);

            // 檐角挂灯笼
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Lantern(c + rot * new Vector3(sx * (w * 0.5f + 0.5f), y + h + 0.5f, sz * (w * 0.5f + 0.5f)),
                            0.4f, LanternRed, Gold);
                }
            }

            y += h + 0.55f;
        }

        // 顶上的宝顶
        Cyl(c + rot * new Vector3(0f, y, 0f), 1.5f, 0.75f, 0.3f, 8, Gold, true);
        Ball(c + rot * new Vector3(0f, y + 1.75f, 0f), 0.42f, Gold, 6, 1.1f, 0f);
    }

    // ================= 3. 牌坊 =================

    static void BuildPaifang(Vector3 c, float yaw, float variant)
    {
        PaifangAt(c, yaw, 5.2f, 5.2f);
    }

    /// <summary>牌坊：四柱三间（中间高、两侧低）+ 层叠额枋 + 顶部小屋顶 + 匾额 + 抱鼓石</summary>
    static void PaifangAt(Vector3 c, float yaw, float width, float height)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float halfD = 0.65f;

        // 四根朱红柱
        float[] xs = { -width * 0.5f, -width * 0.17f, width * 0.17f, width * 0.5f };
        for (int i = 0; i < xs.Length; i++)
        {
            Vector3 p = c + rot * new Vector3(xs[i], 0f, 0f);
            Cyl(p, 0.3f, 0.42f, 0.36f, 8, LimestoneDark, true);
            Box(p + Vector3.up * (0.3f + height * 0.5f), new Vector3(0.36f, height, 0.36f), rot, Vermilion, VermilionDark);
            // 抱鼓石
            Box(p + rot * new Vector3(0f, 0.42f, halfD), new Vector3(0.26f, 0.74f, 0.26f), rot, LimestoneDark, LimestoneDark);
        }

        // 额枋：中间一间高、两侧低（层叠出两跳）
        float midY = height + 0.3f;
        Box(c + rot * new Vector3(0f, midY, 0f), new Vector3(width * 0.42f, 0.34f, halfD * 1.7f), rot, Wood, VermilionDark);
        Box(c + rot * new Vector3(0f, midY + 0.5f, 0f), new Vector3(width * 0.5f, 0.3f, halfD * 1.9f), rot, Vermilion, VermilionDark);
        for (int s = -1; s <= 1; s += 2)
        {
            Box(c + rot * new Vector3(s * width * 0.335f, midY - 0.9f, 0f),
                new Vector3(width * 0.33f, 0.3f, halfD * 1.6f), rot, Wood, VermilionDark);
        }

        // 顶部小屋顶（中间一间）
        ChineseRoof(c + rot * new Vector3(0f, midY + 0.75f, 0f), width * 0.72f, halfD * 2.6f, 0.7f, 0.5f, rot, Tile, Ridge);
        // 两侧小顶
        for (int s = -1; s <= 1; s += 2)
            ChineseRoof(c + rot * new Vector3(s * width * 0.335f, midY - 0.6f, 0f), width * 0.42f, halfD * 2.2f, 0.45f, 0.32f, rot, TileDark, Ridge);

        // 匾额
        Box(c + rot * new Vector3(0f, midY + 0.28f, halfD * 0.9f), new Vector3(width * 0.3f, 0.52f, 0.1f), rot, Gold, Gold);

        // 柱顶灯笼
        for (int s = -1; s <= 1; s += 2)
            Lantern(c + rot * new Vector3(s * width * 0.5f, height + 0.45f, 0f), 0.42f, LanternRed, Gold);
    }

    // ================= 4. 吊脚楼 =================

    /// <summary>吊脚楼：一排木柱扎到水下，上面挑出木屋 + 小阳台 + 灯笼（建在内湾/河岸边）</summary>
    static void BuildStiltHouse(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float w = 6.2f, d = 5.0f, deckY = 3.6f;      // 楼面离地 3.6 米（下面是水面）

        // 木柱（前排扎到水下，后排站在岸上）
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float x = sx * (w * 0.5f - 0.4f);
                float z = sz * (d * 0.5f - 0.35f);
                float len = sz > 0 ? deckY + 3.0f : deckY;      // 临水那排更长
                Box(c + rot * new Vector3(x, deckY - len * 0.5f, z), new Vector3(0.3f, len, 0.3f), rot, Wood, Wood);
            }
        }
        // 斜撑
        for (int sx = -1; sx <= 1; sx += 2)
        {
            Box(c + rot * new Vector3(sx * (w * 0.5f - 0.4f), deckY - 1.4f, 0f),
                new Vector3(0.16f, 0.16f, d), rot * Quaternion.Euler(30f, 0f, 0f), Wood, Wood);
        }

        // 楼板 + 挑出的阳台
        Box(c + rot * new Vector3(0f, deckY, 0f), new Vector3(w, 0.32f, d), rot, Wood, WoodLight);
        Box(c + rot * new Vector3(0f, deckY, d * 0.5f + 0.9f), new Vector3(w * 0.86f, 0.28f, 2.2f), rot,
            WoodLight, Wood);
        // 阳台栏杆
        Box(c + rot * new Vector3(0f, deckY + 0.55f, d * 0.5f + 1.95f), new Vector3(w * 0.86f, 0.7f, 0.14f), rot,
            Wood, WoodLight);
        for (int s = -1; s <= 1; s += 2)
            Box(c + rot * new Vector3(s * w * 0.42f, deckY + 0.55f, d * 0.5f + 0.9f), new Vector3(0.14f, 0.7f, 2.2f), rot,
                Wood, WoodLight);

        // 屋身：白墙 + 木框
        float wallH = 3.0f;
        Box(c + rot * new Vector3(0f, deckY + 0.32f + wallH * 0.5f, 0f), new Vector3(w * 0.92f, wallH, d * 0.9f), rot,
            WallWhite, WallShade);
        Box(c + rot * new Vector3(0f, deckY + 0.32f + wallH * 0.6f, d * 0.9f * 0.5f + 0.03f),
            new Vector3(w * 0.6f, 0.7f, 0.1f), rot, Wood, WoodLight);

        // 中式屋顶
        ChineseRoof(c + rot * new Vector3(0f, deckY + 0.32f + wallH, 0f), w + 1.2f, d + 1.2f, w * 0.3f, 0.5f, rot,
            Tile, Ridge);

        // 檐下与阳台灯笼
        for (int s = -1; s <= 1; s += 2)
            Lantern(c + rot * new Vector3(s * (w * 0.5f - 0.4f), deckY + 0.32f + wallH + 0.5f, d * 0.5f + 0.4f),
                    0.4f, LanternRed, Gold);
        Lantern(c + rot * new Vector3(0f, deckY + 0.9f, d * 0.5f + 2.0f), 0.38f, LanternRed, Gold);
    }

    // ================= 5. 石拱桥 =================

    static void BuildArchBridge(Vector3 c, float yaw, float variant, float span)
    {
        if (span < 8f) span = 16f;          // 兜底（正常由选点阶段按实际河宽给）
        StoneArchBridge(c, yaw, span, span * 0.42f);
    }

    // ================= 6. 茶棚 =================

    /// <summary>茶棚：四根竹柱 + 中式顶 + 桌椅 + 幌子 + 炉子（路边的小歇脚处）</summary>
    static void BuildTeaStall(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float w = 4.6f, d = 3.8f, h = 2.7f;

        // 台基
        Box(c + Vector3.up * 0.12f, new Vector3(w + 0.6f, 0.24f, d + 0.6f), rot, LimestoneDark, LimestoneDark);

        // 四根竹柱
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Prism(c + rot * new Vector3(sx * (w * 0.5f - 0.2f), 0.24f, sz * (d * 0.5f - 0.2f)), h, 0.12f, 0.1f, 6, Bamboo);
            }
        }

        // 中式顶（比柱距大一圈）
        ChineseRoof(c + rot * new Vector3(0f, 0.24f + h, 0f), w + 1.4f, d + 1.4f, 0.85f, 0.45f, rot, Tile, Ridge);

        // 幌子（挂在前面的一幅布）
        Box(c + rot * new Vector3(-w * 0.5f - 0.05f, 1.5f, 0f), new Vector3(0.08f, 0.6f, 1.4f), rot, Cloth, Cloth);

        // 桌椅：一张方桌 + 四条凳
        Box(c + rot * new Vector3(0f, 0.72f, 0f), new Vector3(1.4f, 0.14f, 1.4f), rot, WoodLight, Wood);
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Box(c + rot * new Vector3(sx * 0.55f, 0.35f, sz * 0.55f), new Vector3(0.12f, 0.72f, 0.12f), rot, Wood, Wood);
                Box(c + rot * new Vector3(sx * 1.3f, 0.45f, sz * 0.9f), new Vector3(0.8f, 0.1f, 0.8f), rot, Bamboo, Bamboo * 0.85f);
                Box(c + rot * new Vector3(sx * 1.3f, 0.22f, sz * 0.9f), new Vector3(0.7f, 0.46f, 0.7f), rot, Bamboo * 0.9f, Bamboo * 0.8f);
            }
        }

        // 茶壶 + 杯
        Ball(c + rot * new Vector3(0f, 0.9f, 0f), 0.2f, LimestoneDark, 6, 0.9f, 0f);
        for (int i = 0; i < 3; i++)
            Cyl(c + rot * new Vector3(-0.4f + i * 0.4f, 0.8f, 0.55f), 0.12f, 0.07f, 0.06f, 6, WallWhite, true);

        // 炉子 + 水壶（火苗登记出去，落地时补）
        Vector3 stove = c + rot * new Vector3(w * 0.5f + 0.7f, 0f, -0.2f);
        Cyl(stove, 0.7f, 0.55f, 0.48f, 8, LimestoneDark, true);
        Cyl(stove + Vector3.up * 0.72f, 0.4f, 0.3f, 0.34f, 8, Limestone, true);
        Box(stove + Vector3.up * 1.05f, new Vector3(0.5f, 0.06f, 0.06f), rot, Wood, Wood);
        Flames.Add(stove + Vector3.up * 0.45f);

        // 檐下灯笼
        for (int sx = -1; sx <= 1; sx += 2)
            Lantern(c + rot * new Vector3(sx * (w * 0.5f + 0.35f), 0.24f + h + 0.35f, d * 0.5f + 0.35f),
                    0.38f, LanternRed, Gold);
    }

    // ================= 7. 水车 =================

    /// <summary>水车：大水轮 + 引水木槽 + 一间中式小屋</summary>
    static void BuildWaterMill(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        // 中式小屋
        LiyueHouse(c, yaw, 6.0f, 4.8f, 3.2f, 5);

        // 水轮：立在一侧，半径 3.2
        Vector3 wheel = c + rot * new Vector3(4.4f, 2.1f, 0f);
        var wheelRot = rot * Quaternion.Euler(0f, 0f, 90f);
        Disc(wheel, 2.55f, 3.2f, 20, wheelRot, Wood);
        Disc(wheel, 0.85f, 1.25f, 12, wheelRot, Wood);
        Cyl(wheel + wheelRot * new Vector3(0f, 0f, -0.5f), 1.0f, 0.35f, 0.35f, 8, Wood, true);

        for (int i = 0; i < 8; i++)
        {
            var spokeRot = wheelRot * Quaternion.Euler(0f, 0f, i * 45f);
            Box(wheel + spokeRot * new Vector3(0f, 1.9f, 0f), new Vector3(0.22f, 3.8f, 0.22f), spokeRot,
                WoodLight, Wood);
        }
        for (int i = 0; i < 16; i++)
        {
            var armRot = wheelRot * Quaternion.Euler(0f, 0f, i * 22.5f);
            Box(wheel + armRot * new Vector3(0f, 2.9f, 0.35f), new Vector3(1.1f, 0.5f, 0.16f), armRot, Wood, Wood);
        }

        // 引水木槽
        Vector3 flumeA = c + rot * new Vector3(1.5f, 3.2f, 0f);
        Vector3 flumeB = wheel + Vector3.up * 3.6f;
        Vector3 mid = (flumeA + flumeB) * 0.5f;
        Vector3 dir = flumeB - flumeA;
        Box(mid, new Vector3(0.95f, dir.magnitude, 0.5f),
            Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(90f, 0f, 0f),
            WoodLight, Wood);

        // 门口的磨盘 + 麻袋
        Box(c + rot * new Vector3(-3.2f, 0.5f, 2.0f), new Vector3(0.3f, 1.0f, 0.3f), rot, Wood, Wood);
        Cyl(c + rot * new Vector3(-3.2f, 1.0f, 2.0f), 0.28f, 1.0f, 0.95f, 14, Limestone, true);
        Box(c + rot * new Vector3(-4.2f, 0.4f, 1.1f), new Vector3(0.8f, 0.8f, 0.7f), rot, Cloth, Cloth);
        Box(c + rot * new Vector3(-4.3f, 1.05f, 1.15f), new Vector3(0.62f, 0.55f, 0.55f), rot, Cloth, Cloth);
    }

    // ================= 8. 岩神像 =================

    /// <summary>岩神像：圆形石台 + 神像（低模）+ 四角石灯 + 香炉 + 供桌</summary>
    static void BuildShrine(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float R = 8.6f;

        // 圆形石台（两级 + 台阶）
        Cyl(c + Vector3.up * 0.02f, 0.36f, R + 0.8f, R + 0.5f, 20, LimestoneDark, true);
        Cyl(c + Vector3.up * 0.38f, 0.34f, R, R - 0.12f, 20, Limestone, true);
        Disc(c + Vector3.up * 0.72f, 0f, R - 1.0f, 20, Quaternion.identity, LimestoneDark);
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = c + rot * new Vector3(0f, 0f, R + 1.1f + i * 0.9f);
            Box(p + Vector3.up * (0.12f - i * 0.03f), new Vector3(4.4f - i * 0.5f, 0.24f, 1.0f), rot,
                Limestone, LimestoneDark);
        }

        // 中央神像：石座 + 身躯 + 头 + 手里的岩晶
        Vector3 p0 = c + rot * new Vector3(0f, 0.7f, 0f);
        Box(p0 + Vector3.up * 0.5f, new Vector3(3.0f, 1.0f, 3.0f), rot, Limestone, LimestoneDark);
        Box(p0 + Vector3.up * 1.6f, new Vector3(2.2f, 1.4f, 2.2f), rot, LimestoneDark, LimestoneDark);
        Box(p0 + rot * new Vector3(0f, 2.9f, 0f), new Vector3(1.9f, 2.8f, 1.5f), rot, Limestone, LimestoneDark);
        // 双臂
        for (int s = -1; s <= 1; s += 2)
            Box(p0 + rot * new Vector3(s * 1.35f, 3.1f, 0f), new Vector3(0.6f, 2.2f, 0.7f), rot, LimestoneDark, LimestoneDark);
        // 头 + 冠
        Ball(p0 + rot * new Vector3(0f, 4.7f, 0f), 0.72f, Limestone, 7, 1.1f, 0f);
        Box(p0 + rot * new Vector3(0f, 5.4f, 0f), new Vector3(1.5f, 0.34f, 1.5f), rot, Gold, Gold);
        // 岩晶（金色八面感：两个叠起来的球）
        Ball(p0 + rot * new Vector3(0f, 3.6f, 0.9f), 0.42f, Gold, 6, 1.2f, 0f);
        Ball(p0 + rot * new Vector3(0f, 4.15f, 0.9f), 0.26f, Gold, 6, 1.2f, 0f);

        // 神像身后的背屏
        Cyl(c + rot * new Vector3(0f, 0.72f, -1.6f), 4.6f, 2.6f, 2.2f, 8, LimestoneDark, false);

        // 四角石灯
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            StoneLamp(c + new Vector3(Mathf.Cos(a) * (R - 1.6f), 0.7f, Mathf.Sin(a) * (R - 1.6f)));
        }

        // 香炉 + 供桌
        Vector3 altar = c + rot * new Vector3(0f, 0.7f, 3.4f);
        Box(altar + Vector3.up * 0.45f, new Vector3(2.4f, 0.9f, 1.1f), rot, Wood, Wood);
        Box(altar + Vector3.up * 0.95f, new Vector3(2.6f, 0.14f, 1.3f), rot, WoodLight, Wood);
        Cyl(altar + Vector3.up * 1.02f, 0.5f, 0.42f, 0.5f, 8, Gold, true);
        Flames.Add(altar + Vector3.up * 1.3f);

        // 台边一圈小旗
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f + 0.2f;
            Box(c + new Vector3(Mathf.Cos(a) * (R - 0.6f), 1.6f, Mathf.Sin(a) * (R - 0.6f)),
                new Vector3(0.08f, 1.8f, 0.08f), Vermilion, VermilionDark);
        }
    }

    // ================= 9. 石林遗迹 =================

    /// <summary>石林遗迹：残破的石柱 / 石阶 + 摩崖石刻 + 倒下的石碑（建在峰林之间）</summary>
    static void BuildStoneForest(Vector3 c, float yaw, float variant)
    {
        int seed = (int)(c.x * 11f + c.z * 17f);

        // 半埋的铺地
        Disc(c + Vector3.up * 0.1f, 0f, 12.0f, 20, Quaternion.identity, LimestoneDark);
        Disc(c + Vector3.up * 0.3f, 0f, 6.0f, 16, Quaternion.identity, Limestone);

        // 残柱一圈：高矮不一，有几根带柱头
        int n = 6 + Mathf.RoundToInt(variant * 4f);
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n + H(i, seed, 71) * 0.3f;
            float r = 7.6f + H(i, seed, 72) * 1.4f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

            float hgt = 2.0f + H(i, seed, 76) * 6.0f;
            bool broken = H(i, seed, 77) > 0.5f;
            if (broken) hgt *= 0.4f;

            Box(p + Vector3.up * 0.2f, new Vector3(1.4f, 0.4f, 1.4f), LimestoneDark, LimestoneDark);
            Cyl(p + Vector3.up * 0.4f, hgt, 0.52f, 0.46f, 8, Limestone);

            if (!broken)
            {
                Box(p + Vector3.up * (0.4f + hgt + 0.18f), new Vector3(1.3f, 0.36f, 1.3f), Limestone, LimestoneDark);
                Box(p + Vector3.up * (0.4f + hgt + 0.56f), new Vector3(1.5f, 0.34f, 1.5f), LimestoneDark, LimestoneDark);
            }
            else
            {
                Box(p + Vector3.up * (0.4f + hgt), new Vector3(1.1f, 0.26f, 1.1f),
                    Quaternion.Euler(7f, 22f, 5f), Limestone, LimestoneDark);
            }
        }

        // 中央的方形祭台 + 摩崖石刻（一块立着的大石板）
        Box(c + Vector3.up * 0.66f, new Vector3(2.6f, 0.62f, 2.6f), Quaternion.Euler(0f, 18f, 0f), Limestone, LimestoneDark);
        Box(c + Vector3.up * 1.2f, new Vector3(1.8f, 0.44f, 1.8f), Quaternion.Euler(0f, 44f, 0f), LimestoneDark, LimestoneDark);

        Vector3 stele = c + new Vector3(Mathf.Cos(yaw * Mathf.Deg2Rad) * 4.2f, 0f, Mathf.Sin(yaw * Mathf.Deg2Rad) * 4.2f);
        var stRot = Quaternion.Euler(0f, yaw, 0f);
        Box(stele + Vector3.up * 0.3f, new Vector3(2.4f, 0.6f, 1.2f), stRot, LimestoneDark, LimestoneDark);
        Box(stele + Vector3.up * 2.2f, new Vector3(2.0f, 3.6f, 0.6f), stRot, Limestone, LimestoneDark);
        for (int i = 0; i < 5; i++)   // 石刻：五道横纹
            Box(stele + stRot * new Vector3(0f, 1.1f + i * 0.55f, 0.34f), new Vector3(1.3f, 0.14f, 0.08f), stRot,
                LimestoneDark, LimestoneDark);

        // 倒下的柱身 + 碎石
        for (int i = 0; i < 3; i++)
        {
            float a = H(i, seed, 81) * Mathf.PI * 2f;
            float r = 9.5f + H(i, seed, 82) * 2.0f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            var lieRot = Quaternion.Euler(0f, H(i, seed, 83) * 360f, 0f);
            Box(p + Vector3.up * 0.54f, new Vector3(0.54f, 0.54f, 4.8f), lieRot, Limestone, LimestoneDark);
            Box(p + lieRot * new Vector3(0f, 0.54f, 2.4f), new Vector3(1.3f, 0.4f, 1.3f), lieRot, LimestoneDark, LimestoneDark);
        }
        for (int i = 0; i < 10; i++)
        {
            float a = H(i, seed, 92) * Mathf.PI * 2f;
            float r = 4.5f + H(i, seed, 93) * 6.0f;
            float s = 0.5f + H(i, seed, 94) * 1.1f;
            Box(c + new Vector3(Mathf.Cos(a) * r, 0.28f, Mathf.Sin(a) * r),
                new Vector3(s, s * 0.7f, s * 0.9f),
                Quaternion.Euler(H(i, seed, 95) * 26f, H(i, seed, 96) * 360f, 0f), Limestone, LimestoneDark);
        }
    }

    // ================= 10. 梯田农舍 =================

    /// <summary>梯田农舍：一间小民居 + 农具 + 稻草堆 + 篱笆（建在梯田上）</summary>
    static void BuildTerraceHut(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        LiyueHouse(c, yaw, 5.0f, 4.2f, 2.8f, 9);

        // 稻草堆（三堆）+ 农具
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = c + rot * new Vector3(-4.4f + i * 1.5f, 0f, 3.2f);
            Cyl(p, 0.5f, 0.85f, 0.7f, 8, Rice, true);
            Ball(p + Vector3.up * 0.95f, 0.85f, Rice, 6, 0.8f, i * 0.3f);
        }

        // 篱笆小圈
        for (int i = 0; i < 14; i++)
        {
            float a = i * Mathf.PI * 2f / 14f;
            float r = 7.4f;
            if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 270f)) < 22f) continue;   // 留个口
            Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Box(p + Vector3.up * 0.6f, new Vector3(0.14f, 1.2f, 0.14f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), Bamboo, Bamboo * 0.8f);
        }

        // 晒谷架
        Vector3 rack = c + rot * new Vector3(3.8f, 0f, -2.4f);
        for (int s = -1; s <= 1; s += 2)
            Box(rack + rot * new Vector3(s * 1.2f, 0.9f, 0f), new Vector3(0.14f, 1.8f, 0.14f), rot, Bamboo, Bamboo * 0.8f);
        Box(rack + Vector3.up * 1.7f, new Vector3(2.8f, 0.14f, 0.14f), rot, Bamboo, Bamboo * 0.8f);
        for (int i = -1; i <= 1; i++)
            Box(rack + rot * new Vector3(i * 0.8f, 1.25f, 0f), new Vector3(0.6f, 0.7f, 0.08f), rot, Rice, Rice * 0.9f);
    }

    // ================= 11. 码头 =================

    /// <summary>码头：石砌岸台 + 木栈桥 + 系船柱 + 两条小船 + 鱼篓 + 火盆（建在水边）</summary>
    static void BuildDock(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        // 岸台
        Box(c + Vector3.up * 0.25f, new Vector3(9.0f, 0.5f, 4.0f), rot, Limestone, LimestoneDark);
        for (int i = 0; i < 5; i++)
            Box(c + rot * new Vector3(-4.0f + i * 2.0f, 0.6f, -1.8f), new Vector3(1.9f, 0.2f, 0.2f), rot,
                Wood, Wood);

        // 栈桥（伸向水里）：木面 + 木桩
        for (int i = 0; i < 5; i++)
        {
            Vector3 p = c + rot * new Vector3(0f, 0f, 2.6f + i * 2.2f);
            Box(p + Vector3.up * 0.62f, new Vector3(2.6f, 0.24f, 2.3f), rot, WoodLight, Wood);
            for (int s = -1; s <= 1; s += 2)
                Box(p + rot * new Vector3(s * 1.05f, -0.6f, 0f), new Vector3(0.24f, 2.6f, 0.24f), rot, Wood, Wood);
        }

        // 系船柱
        for (int s = -1; s <= 1; s += 2)
        {
            Cyl(c + rot * new Vector3(s * 1.6f, 0.7f, 2.6f), 0.8f, 0.22f, 0.18f, 6, Wood, true);
        }

        // 两条小船（停在栈桥旁边）
        for (int b = 0; b < 2; b++)
        {
            Vector3 p = c + rot * new Vector3(b == 0 ? -3.6f : 3.6f, 0.2f, 5.4f + b * 1.4f);
            var bRot = rot * Quaternion.Euler(0f, b == 0 ? 14f : -12f, 0f);
            Box(p, new Vector3(1.5f, 0.6f, 4.4f), bRot, Wood, WoodLight);
            Box(p + Vector3.up * 0.34f, new Vector3(1.1f, 0.2f, 3.6f), bRot, Wood, Wood);
            // 船头船尾翘起
            Box(p + bRot * new Vector3(0f, 0.45f, 2.3f), new Vector3(0.9f, 0.5f, 0.9f), bRot * Quaternion.Euler(24f, 0f, 0f), Wood, Wood);
            // 桨
            Box(p + bRot * new Vector3(0.9f, 0.5f, -0.4f), new Vector3(0.12f, 0.1f, 2.2f), bRot * Quaternion.Euler(0f, 0f, 18f), Wood, Wood);
        }

        // 鱼篓 + 木箱
        for (int i = 0; i < 3; i++)
            Cyl(c + rot * new Vector3(-3.4f + i * 1.0f, 0.5f, 1.2f), 0.7f, 0.5f, 0.58f, 8, Bamboo, true);
        Box(c + rot * new Vector3(3.6f, 0.85f, -1.0f), new Vector3(0.9f, 0.9f, 0.9f), rot, WoodLight, Wood);

        // 火盆
        Vector3 brazier = c + rot * new Vector3(-4.2f, 0.5f, -1.6f);
        Cyl(brazier, 0.6f, 0.5f, 0.6f, 8, LimestoneDark, true);
        Flames.Add(brazier + Vector3.up * 0.7f);

        // 柱上的灯笼
        for (int s = -1; s <= 1; s += 2)
            Lantern(c + rot * new Vector3(s * 4.2f, 2.6f, -1.8f), 0.4f, LanternRed, Gold);
    }
}
