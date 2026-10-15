using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 野外建筑的种类。
/// 前十一个是"人住 / 人用的"，后三个是"遗迹"。
/// 顺序就是选点、地图标记、日志统计里的顺序，不要随便插队。
/// </summary>
public enum WildKind : byte
{
    Village = 0,     // 村落：一圈民居 + 水井 + 农田 + 谷仓 + 栅栏
    Watchtower,      // 瞭望塔：木塔 + 梯子 + 旗
    Windmill,        // 风车：石塔身 + 四叶风车
    WaterMill,       // 水车磨坊：水边木屋 + 大水轮 + 引水槽
    Bridge,          // 木桥：跨河的石墩 + 木面 + 栏杆
    HuntersHut,      // 猎人小屋：小木屋 + 兽皮架 + 箭靶
    LumberCamp,      // 伐木场：帐篷 + 锯木架 + 木堆
    RestCamp,        // 篝火休息点：石圈火堆 + 长凳
    RuinPillars,     // 断柱石阵：残柱 + 倒塌的柱身 + 柱础
    WindShrine,      // 风神祭坛：圆台 + 立碑 + 中央浮空石
    RuinWall,        // 残墙：断裂石墙 + 半塌拱门
}

/// <summary>
/// 野外建筑的几何生成：村落 / 设施 / 遗迹，全部低模 + 顶点色。
/// 和城（MondstadtTown）一样分三个子网格：0 石墙贴图 / 1 瓦片贴图 / 2 顶点色，
/// 所以野外建筑和城共用同一套材质，放在一起是一套视觉语言。
///
/// 用法：Begin() → 对每个站点 Append(kind, center, yaw, variant) → BuildMesh()。
/// 坐标都是世界坐标（center.y 要传地面高度），所以合出来的网格直接挂在原点即可。
///
/// 全部尺寸按米，和城一致。改这里的数字 = 改野外建筑的长相。
/// </summary>
public static class WildBuildings
{
    // ---------- 配色（和 MondstadtTown 同一套，改这里两边一起变） ----------

    static readonly Color Stone     = new Color32(0xd8, 0xd0, 0xbc, 255);
    static readonly Color StoneDark = new Color32(0xb3, 0xa9, 0x95, 255);
    static readonly Color Roof      = new Color32(0xc2, 0x55, 0x3c, 255);
    static readonly Color RoofAlt   = new Color32(0x9c, 0x4a, 0x37, 255);
    static readonly Color Wood      = new Color32(0x8a, 0x62, 0x40, 255);
    static readonly Color WoodDark  = new Color32(0x6b, 0x4a, 0x2f, 255);
    static readonly Color WoodLight = new Color32(0xa8, 0x7c, 0x52, 255);
    static readonly Color Gold      = new Color32(0xe8, 0xd0, 0x8f, 255);
    static readonly Color Window    = new Color32(0x50, 0x5e, 0x76, 255);
    static readonly Color Flag      = new Color32(0x5f, 0xa8, 0xd8, 255);
    static readonly Color Wheat     = new Color32(0xe0, 0xc4, 0x6a, 255);
    static readonly Color Cloth     = new Color32(0xb8, 0x8a, 0x52, 255);
    static readonly Color Hide      = new Color32(0x9c, 0x7c, 0x5a, 255);
    static readonly Color Iron      = new Color32(0x6d, 0x6a, 0x72, 255);
    static readonly Color Moss      = new Color32(0x7d, 0x8f, 0x6a, 255);

    // ---------- 生成状态 ----------

    static readonly List<Vector3> _v = new List<Vector3>();
    static readonly List<Color>   _c = new List<Color>();

    /// <summary>三个子网格：0 石墙（贴图）/ 1 屋顶瓦片（贴图）/ 2 其余（顶点色）</summary>
    public static readonly List<int>[] SubTris = { new List<int>(), new List<int>(), new List<int>() };

    /// <summary>需要放"活火苗"的位置（村落中央的篝火、休息点的火堆）。落地时由 WildSiteSpawner 补上火苗</summary>
    public static readonly List<Vector3> Flames = new List<Vector3>();

    /// <summary>村落的民居位置（地图上画红顶色块用）</summary>
    public static readonly List<Vector4> Houses = new List<Vector4>();   // xy = 位置，z = 半径，w 保留

    // ================= 对外接口 =================

    /// <summary>清空上一轮生成的内容</summary>
    public static void Begin()
    {
        _v.Clear();
        _c.Clear();
        foreach (var list in SubTris) list.Clear();
        Flames.Clear();
        Houses.Clear();
    }

    /// <summary>这栋建筑占地多大（选点时算间距、算地台大小、算其它生成器要躲多远）。
    /// ★ 要把"伸出去的零件"也算进来：村落的院子（农田 / 晒架 / 稻草人，r≈35）、瞭望塔的坡道（r≈7.8）。
    ///   算小了的话，别的东西（宝箱 / 树）就会正好长在那块地上</summary>
    public static float Footprint(WildKind kind)
    {
        switch (kind)
        {
            case WildKind.Village:     return 30f;   // 地台半径 36：罩住栅栏(25.5)和院子(35)
            case WildKind.Watchtower:  return 9f;    // 坡道尽头离塔心 7.8
            case WildKind.Windmill:    return 9f;
            case WildKind.WaterMill:   return 10f;
            case WildKind.Bridge:      return 17f;
            case WildKind.HuntersHut:  return 7f;
            case WildKind.LumberCamp:  return 10f;
            case WildKind.RestCamp:    return 5f;
            case WildKind.RuinPillars: return 13f;
            case WildKind.WindShrine:  return 11f;
            case WildKind.RuinWall:    return 12f;
            default:                   return 8f;
        }
    }

    /// <summary>地图标记种类（和 WorldMap.MarkerColor 里那套编号对齐）</summary>
    public static byte MarkerKind(WildKind kind)
    {
        switch (kind)
        {
            case WildKind.Village:     return 14;
            case WildKind.Watchtower:  return 15;
            case WildKind.Windmill:    return 16;
            case WildKind.WaterMill:   return 17;
            case WildKind.Bridge:      return 18;
            case WildKind.HuntersHut:  return 19;
            case WildKind.LumberCamp:  return 20;
            case WildKind.RestCamp:    return 21;
            case WildKind.WindShrine:  return 23;
            default:                   return 22;   // 遗迹两类共用冷灰
        }
    }

    /// <summary>地图 / 日志上的名字</summary>
    public static string DisplayName(WildKind kind)
    {
        switch (kind)
        {
            case WildKind.Village:     return "野外村落";
            case WildKind.Watchtower:  return "瞭望塔";
            case WildKind.Windmill:    return "风车";
            case WildKind.WaterMill:   return "水车磨坊";
            case WildKind.Bridge:      return "木桥";
            case WildKind.HuntersHut:  return "猎人小屋";
            case WildKind.LumberCamp:  return "伐木场";
            case WildKind.RestCamp:    return "篝火休息点";
            case WildKind.RuinPillars: return "断柱石阵";
            case WildKind.WindShrine:  return "风神祭坛";
            case WildKind.RuinWall:    return "残墙遗迹";
            default:                   return "遗迹";
        }
    }

    /// <summary>地图大色块的颜色（0 红顶 / 1 石板浅色 / 2 木质）</summary>
    public static byte BlobColor(WildKind kind) => kind == WildKind.Village ? (byte)0 : (byte)1;

    /// <summary>把一栋（一组）建筑的几何写进列表。center.y 传地面高度，yaw 是朝向，variant 是哈希出来的造型变体。
    /// param 是少数几种建筑需要的额外尺寸（目前只有木桥用它传"半跨长"，其余传 0）</summary>
    public static void Append(WildKind kind, Vector3 center, float yaw, float variant, float param = 0f)
    {
        switch (kind)
        {
            case WildKind.Village:     BuildVillage(center, yaw, variant); break;
            case WildKind.Watchtower:  BuildWatchtower(center, yaw, variant); break;
            case WildKind.Windmill:    BuildWindmill(center, yaw, variant); break;
            case WildKind.WaterMill:   BuildWaterMill(center, yaw, variant); break;
            case WildKind.Bridge:      BuildBridge(center, yaw, variant, param); break;
            case WildKind.HuntersHut:  BuildHuntersHut(center, yaw, variant); break;
            case WildKind.LumberCamp:  BuildLumberCamp(center, yaw, variant); break;
            case WildKind.RestCamp:    BuildRestCamp(center, yaw, variant); break;
            case WildKind.RuinPillars: BuildRuinPillars(center, yaw, variant); break;
            case WildKind.WindShrine:  BuildWindShrine(center, yaw, variant); break;
            case WildKind.RuinWall:    BuildRuinWall(center, yaw, variant); break;
        }
    }

    /// <summary>把生成好的几何合成一个三子网格的 Mesh</summary>
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

    /// <summary>按颜色把三角形分到子网格：石墙 / 屋顶走贴图，其它保留顶点色（和城里同一套规则）</summary>
    static List<int> T(Color color)
    {
        if (Same(color, Stone) || Same(color, StoneDark)) return SubTris[0];
        if (Same(color, Roof)  || Same(color, RoofAlt))   return SubTris[1];
        return SubTris[2];
    }

    static bool Same(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f;

    // ================= 零件快捷方式 =================

    static void Box(Vector3 center, Vector3 size, Quaternion rot, Color top, Color side) =>
        LowPoly.Box(_v, _c, T(top), center, size, rot, top, side);

    static void Box(Vector3 center, Vector3 size, Color top, Color side) =>
        LowPoly.Box(_v, _c, T(top), center, size, Quaternion.identity, top, side);

    static void Gable(Vector3 baseCenter, float width, float depth, float height, Quaternion rot, Color color) =>
        LowPoly.Gable(_v, _c, T(color), baseCenter, width, depth, height, rot, color);

    static void Cyl(Vector3 basePos, float height, float bottomR, float topR, int sides, Color color, bool capTop = false) =>
        LowPoly.Cylinder(_v, _c, T(color), basePos, height, bottomR, topR, sides, color, capTop);

    static void Prism(Vector3 basePos, float height, float bottomR, float topR, int sides, Color color) =>
        LowPoly.Prism(_v, _c, T(color), basePos, height, bottomR, topR, sides, color);

    static void Ball(Vector3 center, float radius, Color color, int segments, float squash = 1f, float seed = 0f) =>
        LowPoly.Sphere(_v, _c, T(color), center, radius, color, segments, squash, seed > 0f ? 1f : 0f, seed);

    static void Disc(Vector3 center, float innerR, float outerR, int sides, Quaternion rot, Color color) =>
        LowPoly.Ring(_v, _c, T(color), center, innerR, outerR, sides, rot, color);

    static float H(int a, int b, int salt) => LowPoly.Hash01(a, b, salt);

    // ================= 民居（村落 / 磨坊 / 小屋共用） =================

    /// <summary>一栋蒙德风民居：石基 + 木框墙 + 双坡红瓦顶 + 门 + 窗 + 烟囱。
    /// 屋顶的屋脊沿局部 Z 轴（也就是"门朝 +Z"那面是正面）</summary>
    static void House(Vector3 center, float yaw, float w, float d, float wallH,
                      Color wall, Color roofCol, bool chimney, int salt)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        // 石基（稍微外扩一点，视觉上像踩在石台上）
        Box(center + Vector3.up * 0.22f, new Vector3(w + 0.45f, 0.44f, d + 0.45f), rot, StoneDark, StoneDark);

        // 墙体
        float baseY = 0.44f + wallH * 0.5f;
        Box(center + rot * new Vector3(0f, baseY, 0f), new Vector3(w, wallH, d), rot, wall, wall);

        // 四角木柱 + 腰线（木结构外露，这是蒙德民居最明显的特征）
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 p = center + rot * new Vector3(sx * (w * 0.5f - 0.06f), baseY, sz * (d * 0.5f - 0.06f));
                Box(p, new Vector3(0.2f, wallH, 0.2f), rot, WoodDark, WoodDark);
            }
        }
        Box(center + rot * new Vector3(0f, 0.44f + wallH * 0.62f, 0f), new Vector3(w + 0.12f, 0.16f, d + 0.12f), rot, Wood, WoodDark);

        // 双坡屋顶：出檐 0.55 米，屋脊高度按面宽算
        Gable(center + Vector3.up * (0.44f + wallH), w + 1.1f, d + 1.1f, w * 0.40f, rot, roofCol);

        // 门（正面 +Z）与门框
        Vector3 door = center + rot * new Vector3(0f, 0.44f + 0.85f, d * 0.5f + 0.03f);
        Box(door, new Vector3(1.05f, 1.7f, 0.12f), rot, Wood, WoodDark);
        Box(door + rot * new Vector3(0f, 0.92f, -0.02f), new Vector3(1.35f, 0.16f, 0.2f), rot, WoodDark, WoodDark);

        // 窗（前面两扇 + 侧面各一扇）
        float wy = 0.44f + wallH * 0.58f;
        for (int s = -1; s <= 1; s += 2)
        {
            Box(center + rot * new Vector3(s * w * 0.27f, wy, d * 0.5f + 0.03f), new Vector3(0.62f, 0.7f, 0.1f), rot, Window, WoodDark);
        }
        for (int s = -1; s <= 1; s += 2)
        {
            Box(center + rot * new Vector3(s * (w * 0.5f + 0.03f), wy, 0f), new Vector3(0.1f, 0.7f, 0.62f), rot, Window, WoodDark);
        }

        // 烟囱
        if (chimney)
        {
            float cy = 0.44f + wallH;
            Box(center + rot * new Vector3(w * 0.28f, cy + 0.75f, -d * 0.22f), new Vector3(0.52f, 1.5f, 0.52f), rot, StoneDark, StoneDark);
            Box(center + rot * new Vector3(w * 0.28f, cy + 1.56f, -d * 0.22f), new Vector3(0.72f, 0.16f, 0.72f), rot, Stone, StoneDark);
        }

        Houses.Add(new Vector4(center.x, center.z, Mathf.Max(w, d) * 0.5f + 1.2f, salt));
    }

    // ================= 1. 村落 =================

    /// <summary>
    /// 村落：一圈木栅栏 + 内圈 3 栋民居 + 外圈 5 栋民居（其中一栋是加高的谷仓）+ 村心的水井 / 篝火 / 木箱，
    /// 篱笆外的"院子"放农田 / 晾晒架 / 稻草人。
    ///
    /// 【布局约束】所有东西都排在地台的平整半径（38 米）以内，而且半径分层互不重叠：
    ///   村心 r &lt; 7 ｜ 内圈民居 r = 10.5（径向 7.55~13.45）｜ 外圈民居 r = 19.5（16.55~22.45）
    ///   ｜ 栅栏 r = 25.5 ｜ 院子 r = 28~35
    /// ★ 之前谷仓放在 r=7、内圈民居在 r=11（半宽 3.5 → 内侧 7.5），两块确实叠在一起 —— 现在谷仓并入外圈。
    /// </summary>
    static void BuildVillage(Vector3 c, float yaw, float variant)
    {
        int seed = (int)(c.x * 7f + c.z * 13f);
        float yawRad = yaw * Mathf.Deg2Rad;

        // 地面：一片压平的土黄铺地，把村子和草地分开
        Disc(c + Vector3.up * 0.05f, 0f, 26f, 24, Quaternion.identity, new Color32(0xc9, 0xb8, 0x92, 255));
        Disc(c + Vector3.up * 0.09f, 0f, 5.0f, 16, Quaternion.identity, StoneDark);

        // ---- 民居：内圈 3 栋（r=10.5）+ 外圈 5 栋（r=19.5），绕中心错落 ----
        const float ringA = 10.5f, ringB = 19.5f;
        int inner = 3, outer = 5;

        for (int i = 0; i < inner; i++)
        {
            float a = yawRad + i * Mathf.PI * 2f / inner + (H(i, seed, 31) - 0.5f) * 0.22f;
            var p = c + new Vector3(Mathf.Cos(a) * ringA, 0f, Mathf.Sin(a) * ringA);
            House(p, -a * Mathf.Rad2Deg + 90f + (H(i, seed, 33) - 0.5f) * 16f,
                  5.0f + H(i, seed, 34) * 0.8f, 4.2f + H(i, seed, 35) * 0.6f, 2.9f + H(i, seed, 36) * 0.3f,
                  Cream(H(i, seed, 37)), H(i, seed, 38) > 0.5f ? Roof : RoofAlt, H(i, seed, 39) > 0.4f, i);
        }

        for (int i = 0; i < outer; i++)
        {
            float a = yawRad + i * Mathf.PI * 2f / outer + 0.35f + (H(i, seed, 41) - 0.5f) * 0.18f;
            var p = c + new Vector3(Mathf.Cos(a) * ringB, 0f, Mathf.Sin(a) * ringB);

            if (i == 0)
            {
                // 外圈的第一栋做成谷仓：加高、加大双坡顶、正面一扇大门（放在外圈就不会和内圈挤）
                var barnRot = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
                Box(p + Vector3.up * 0.25f, new Vector3(6.0f, 0.5f, 4.8f), barnRot, StoneDark, StoneDark);
                Box(p + Vector3.up * 2.6f, new Vector3(5.4f, 4.2f, 4.2f), barnRot, Wood, WoodDark);
                Gable(p + Vector3.up * 4.7f, 6.4f, 5.2f, 2.6f, barnRot, RoofAlt);
                Box(p + barnRot * new Vector3(0f, 2.2f, 2.15f), new Vector3(2.3f, 3.2f, 0.14f), barnRot, WoodDark, WoodDark);
                Houses.Add(new Vector4(p.x, p.z, 4.6f, i));
                continue;
            }

            House(p, -a * Mathf.Rad2Deg + 90f + (H(i, seed, 43) - 0.5f) * 22f,
                  5.0f + H(i, seed, 44) * 0.9f, 4.2f + H(i, seed, 45) * 0.7f, 2.7f + H(i, seed, 46) * 0.4f,
                  Cream(H(i, seed, 47)), H(i, seed, 48) > 0.5f ? Roof : RoofAlt, H(i, seed, 49) > 0.5f, i + inner);
        }

        // ---- 村心：水井（石圈 + 木架 + 摇柄）----
        Vector3 well = c + new Vector3(Mathf.Cos(yawRad + 1.0f) * 4.6f, 0f, Mathf.Sin(yawRad + 1.0f) * 4.6f);
        Cyl(well + Vector3.up * 0.1f, 0.85f, 1.15f, 1.05f, 12, StoneDark, true);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + 0.4f;
            Box(well + new Vector3(Mathf.Cos(a) * 1.25f, 1.0f, Mathf.Sin(a) * 1.25f), new Vector3(0.22f, 1.8f, 0.22f), Wood, WoodDark);
        }
        Box(well + Vector3.up * 2.0f, new Vector3(3.0f, 0.18f, 0.18f), Wood, WoodDark);
        Gable(well + Vector3.up * 2.05f, 3.4f, 2.6f, 0.9f, Quaternion.identity, Roof);
        Box(well + Vector3.up * 1.45f, new Vector3(0.5f, 0.55f, 0.5f), WoodDark, WoodDark);

        // ---- 村心：木箱 + 木桶（和十字路口错开）----
        Vector3 crate = c + new Vector3(Mathf.Cos(yawRad + 2.5f) * 5.6f, 0f, Mathf.Sin(yawRad + 2.5f) * 5.6f);
        Box(crate + Vector3.up * 0.45f, new Vector3(0.9f, 0.9f, 0.9f), WoodLight, WoodDark);
        Box(crate + Vector3.up * 1.32f, new Vector3(0.75f, 0.75f, 0.75f), WoodLight, WoodDark);
        Cyl(crate + new Vector3(1.4f, 0f, 0.3f), 0.9f, 0.42f, 0.38f, 8, Wood, true);

        // ---- 村心篝火：石圈 + 交叉柴（火苗登录到 Flames，落地时补上火苗） ----
        Box(c + Vector3.up * 0.14f, new Vector3(2.6f, 0.2f, 2.6f), StoneDark, StoneDark);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            Box(c + new Vector3(Mathf.Cos(a) * 0.85f, 0.2f, Mathf.Sin(a) * 0.85f), new Vector3(0.34f, 0.26f, 0.34f), Stone, StoneDark);
        }
        for (int i = 0; i < 3; i++)
            Box(c + Vector3.up * (0.32f + i * 0.12f), new Vector3(0.16f, 0.16f, 1.4f), Quaternion.Euler(0f, i * 55f, 0f), Wood, WoodDark);
        Flames.Add(c + Vector3.up * 0.4f);

        // ---- 木栅栏一圈（两个出入口：正门 + 后门）----
        //
        // ★ 横杆必须按"相邻两根柱子的实际连线"来摆，不能用"切线方向 + 固定长度"：
        //   上一版就是这么写的，而且只给每 4 根柱子架一道横杆（相邻两道横杆隔 13.4 米，
        //   横杆本身却只有 3.4 米长），再叠上柱子半径的抖动 —— 结果横杆一段段悬空、跟柱子对不上，
        //   看着就是"栅栏错位"。现在先把柱子位置全算出来，再逐段连线架杆。
        float gateA = yawRad + 1.0f, gateB = yawRad - 1.9f;
        const float fenceR = 25.5f;
        const int postCount = 48;

        var posts = new List<Vector3>();
        var postHeights = new List<float>();

        for (int i = 0; i < postCount; i++)
        {
            float a = yawRad + i * Mathf.PI * 2f / postCount;

            // 两个出入口各留 12°（用零向量占位，保证相邻索引仍能连线）
            if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, gateA * Mathf.Rad2Deg)) < 12f) { posts.Add(Vector3.zero); postHeights.Add(0f); continue; }
            if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, gateB * Mathf.Rad2Deg)) < 12f) { posts.Add(Vector3.zero); postHeights.Add(0f); continue; }

            float r = fenceR + (H(i, seed, 51) - 0.5f) * 0.35f;
            float hgt = 1.22f + H(i, seed, 52) * 0.26f;
            posts.Add(c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            postHeights.Add(hgt);
        }

        // 逐段架横杆：上下两道，按两根柱子的连线定方向和长度
        for (int i = 0; i < posts.Count; i++)
        {
            Vector3 p0 = posts[i];
            Vector3 p1 = posts[(i + 1) % posts.Count];
            if (p0 == Vector3.zero || p1 == Vector3.zero) continue;      // 这一格是出入口

            Vector3 mid = (p0 + p1) * 0.5f;
            Vector3 dir = p1 - p0;
            float len = dir.magnitude;
            if (len < 0.05f) continue;

            // 盒子的长度就沿局部 Z，所以朝向直接用 Atan2(dir.x, dir.z) 对齐连线即可。
            // ★ 这里曾经多加了 90°：横杆于是垂直栅栏线往外戳，相邻两根方向不同就交叉成 X，
            //   看着又长又乱（实机上就是这个症状）
            float yawDeg = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            var railRot = Quaternion.Euler(0f, yawDeg, 0f);

            // 取两根柱子的较矮者，保证横杆不会冒出柱头
            float top = Mathf.Min(postHeights[i], postHeights[(i + 1) % posts.Count]);

            Box(mid + Vector3.up * (top * 0.76f), new Vector3(0.20f, 0.13f, len * 1.04f), railRot, Wood, WoodDark);
            Box(mid + Vector3.up * (top * 0.42f), new Vector3(0.16f, 0.11f, len * 1.04f), railRot, Wood, WoodDark);
        }

        // 柱子最后画：画在横杆之上，接缝看着更干净
        for (int i = 0; i < posts.Count; i++)
        {
            if (posts[i] == Vector3.zero) continue;

            float a = yawRad + i * Mathf.PI * 2f / postCount;
            var yawRot = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);

            Box(posts[i] + Vector3.up * (postHeights[i] * 0.5f),
                new Vector3(0.22f, postHeights[i], 0.22f), yawRot, Wood, WoodDark);
            // 柱头削成四棱锥，像削过的木桩（上一版是 45° 斜切方块，显得笨重）
            Cyl(posts[i] + Vector3.up * postHeights[i], 0.26f, 0.17f, 0.02f, 4, WoodDark);
        }

        // ---- 正门木牌坊（对着主入口，一眼能认出这是个村子）----
        Vector3 gate = c + new Vector3(Mathf.Cos(gateA) * fenceR, 0f, Mathf.Sin(gateA) * fenceR);
        var gateRot = Quaternion.Euler(0f, -gateA * Mathf.Rad2Deg + 90f, 0f);
        for (int s = -1; s <= 1; s += 2)
        {
            Box(gate + gateRot * new Vector3(s * 2.3f, 1.95f, 0f), new Vector3(0.34f, 3.9f, 0.34f), gateRot, WoodDark, WoodDark);
            Box(gate + gateRot * new Vector3(s * 2.3f, 0.25f, 0f), new Vector3(0.75f, 0.5f, 0.75f), gateRot, Stone, StoneDark);
        }
        Box(gate + Vector3.up * 4.05f, new Vector3(5.7f, 0.3f, 0.5f), gateRot, WoodLight, WoodDark);
        Gable(gate + Vector3.up * 4.2f, 6.4f, 1.5f, 0.9f, gateRot, Roof);

        // ---- 院子（篱笆外，正门偏一侧）：农田 + 晾晒架 + 稻草人 ----
        // 全部落在地台平整半径内，所以不用各自去探地面高度
        float yardA = yawRad - 0.75f;                       // 和正门错开 43°，不堵门
        Vector3 farm = c + new Vector3(Mathf.Cos(yardA) * 30f, 0f, Mathf.Sin(yardA) * 30f);
        var farmRot = Quaternion.Euler(0f, -yardA * Mathf.Rad2Deg + 90f, 0f);   // 长边切向、短边径向

        for (int row = -2; row <= 2; row++)
        {
            Box(farm + farmRot * new Vector3(0f, 0.12f, row * 1.5f), new Vector3(8.5f, 0.24f, 1.1f), farmRot,
                new Color32(0x8a, 0x6f, 0x4a, 255), new Color32(0x74, 0x5c, 0x3d, 255));
            for (int k = -3; k <= 3; k++)
                Box(farm + farmRot * new Vector3(k * 1.25f, 0.4f, row * 1.5f), new Vector3(0.7f, 0.58f, 0.8f), farmRot,
                    Wheat, new Color32(0xc4, 0xa8, 0x54, 255));
        }

        // 田边稻草人
        Vector3 scare = farm + farmRot * new Vector3(4.9f, 0f, -3.2f);
        Box(scare + Vector3.up * 0.9f, new Vector3(0.16f, 1.8f, 0.16f), WoodDark, WoodDark);
        Box(scare + Vector3.up * 1.45f, new Vector3(1.5f, 0.14f, 0.14f), Wood, WoodDark);
        Box(scare + Vector3.up * 1.35f, new Vector3(0.85f, 0.9f, 0.35f), Cloth, Cloth);
        Ball(scare + Vector3.up * 2.0f, 0.36f, Wheat, 6, 1f, 0f);

        // 晾晒架（院子的另一头）
        float rackA = yawRad + 0.9f;
        Vector3 rack = c + new Vector3(Mathf.Cos(rackA) * 29f, 0f, Mathf.Sin(rackA) * 29f);
        var rackRot = Quaternion.Euler(0f, -rackA * Mathf.Rad2Deg + 90f, 0f);
        for (int s = -1; s <= 1; s += 2)
            Box(rack + rackRot * new Vector3(s * 1.5f, 1.05f, 0f), new Vector3(0.18f, 2.1f, 0.18f), rackRot, Wood, WoodDark);
        Box(rack + Vector3.up * 1.95f, new Vector3(3.4f, 0.16f, 0.16f), rackRot, Wood, WoodDark);
        for (int i = -1; i <= 1; i++)
            Box(rack + rackRot * new Vector3(i * 0.8f, 1.45f, 0f), new Vector3(0.36f, 0.85f, 0.09f), rackRot, Hide, Hide);
    }

    /// <summary>民居墙面的奶白 / 浅黄 / 淡青三选一（和城里民居一个色系）</summary>
    static Color Cream(float t)
    {
        if (t < 0.34f) return new Color32(0xf0, 0xe6, 0xcf, 255);
        if (t < 0.67f) return new Color32(0xe6, 0xd6, 0xb4, 255);
        return new Color32(0xd8, 0xdd, 0xcf, 255);
    }

    // ================= 2. 瞭望塔 =================

    /// <summary>
    /// 木结构瞭望塔：四腿塔身 + 交叉斜撑 + 顶棚，塔顶一圈围栏 + 旗 + 火盆。
    ///
    /// 【怎么上去】塔外挂一条**实心木坡道**，从地面一路斜上接到台面边缘。
    /// ★ 原来这里只有一条装饰用的梯子 —— 梯子的横档是分开的小方块，不构成连续可走面，
    ///   CharacterController 根本爬不上去，玩家到了塔下只能干瞪眼。现在改成实心坡道：
    ///   台面高度 4.6 米、坡角约 42°，玩家的 slopeLimit 是 90°，走上去没问题。
    ///
    /// 占地半径要给到能罩住坡道（坡道尽头离塔心约 7.8 米），所以 Footprint = 9。
    /// </summary>
    static void BuildWatchtower(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float h = 4.4f;                       // 塔腿高度（台面高度 = 0.28 + h）

        // 石基
        Box(c + Vector3.up * 0.28f, new Vector3(5.0f, 0.56f, 5.0f), rot, Stone, StoneDark);

        // 四根塔腿 + 交叉斜撑
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 leg = c + rot * new Vector3(sx * 1.75f, 0.28f + h * 0.5f, sz * 1.75f);
                Box(leg, new Vector3(0.3f, h, 0.3f), rot, WoodLight, WoodDark);
            }
        }
        for (int level = 0; level < 2; level++)
        {
            float y = 1.3f + level * 1.9f;
            Box(c + rot * new Vector3(0f, y, 1.75f), new Vector3(3.9f, 0.16f, 0.16f), rot, Wood, WoodDark);
            Box(c + rot * new Vector3(0f, y, -1.75f), new Vector3(3.9f, 0.16f, 0.16f), rot, Wood, WoodDark);
            Box(c + rot * new Vector3(0f, y + 0.7f, 0f), new Vector3(0.16f, 0.16f, 3.9f), rot * Quaternion.Euler(0f, 0f, 18f), Wood, WoodDark);
        }

        // 平台 + 围栏（南北两侧做完整，-X 一侧留一个 1.7 米宽的缺口给坡道）
        float deckY = 0.28f + h;
        Box(c + Vector3.up * (deckY + 0.15f), new Vector3(5.2f, 0.3f, 5.2f), rot, Wood, WoodDark);

        for (int s = -1; s <= 1; s += 2)
            Box(c + rot * new Vector3(0f, deckY + 0.85f, s * 2.35f), new Vector3(5.2f, 0.9f, 0.16f), rot, WoodLight, WoodDark);

        Box(c + rot * new Vector3(2.35f, deckY + 0.85f, 0f), new Vector3(0.16f, 0.9f, 4.7f), rot, WoodLight, WoodDark);
        for (int s = -1; s <= 1; s += 2)
            Box(c + rot * new Vector3(-2.35f, deckY + 0.85f, s * 1.6f), new Vector3(0.16f, 0.9f, 1.5f), rot, WoodLight, WoodDark);

        BuildWatchtowerRamp(c, rot, deckY, -1f);   // 坡道挂在 -X 一侧（正好对着围栏缺口）

        // 顶上：旗杆 + 火盆 + 一只木箱（**不做屋顶**）
        // ★ 原来这里是十字搭的两片双坡屋顶，底面离台面只有 1.35 米 —— 玩家 1.8 米高，
        //   一走上平台就顶着屋顶走不动。拆掉屋顶、只留旗和火盆，平台才是能站的
        Box(c + rot * new Vector3(1.55f, deckY + 1.75f, -1.55f), new Vector3(0.16f, 3.5f, 0.16f), rot, WoodDark, WoodDark);
        Box(c + rot * new Vector3(2.35f, deckY + 3.05f, -1.55f), new Vector3(1.5f, 0.85f, 0.08f), rot, Flag, Flag);

        Vector3 brazier = c + rot * new Vector3(-1.55f, 0f, 1.55f);
        Cyl(brazier + Vector3.up * (deckY + 0.30f), 0.5f, 0.5f, 0.7f, 8, StoneDark, true);
        Ball(brazier + Vector3.up * (deckY + 0.92f), 0.42f, new Color32(0xd8, 0x6a, 0x2c, 255), 6, 0.7f, 0f);
        Flames.Add(brazier + Vector3.up * (deckY + 0.85f));

        Box(c + rot * new Vector3(1.5f, deckY + 0.68f, 1.5f), new Vector3(0.8f, 0.8f, 0.8f), rot, WoodLight, WoodDark);
    }

    /// <summary>塔外的实心木坡道：从地面斜上接到台面边缘（side = -1 / +1 决定挂在哪一侧）。
    /// 用一整块斜板 + 两侧扶手，中间没有缝 —— 这样 CharacterController 才走得上去</summary>
    static void BuildWatchtowerRamp(Vector3 c, Quaternion rot, float deckY, float side)
    {
        const float angle = 42f;                                   // 坡角
        float rise = deckY + 0.30f;                                 // 要爬的高度（比台面略高一点，接得实）
        float run  = rise / Mathf.Tan(angle * Mathf.Deg2Rad);        // 水平投影长度
        const float halfW = 0.85f;                                  // 坡道半宽

        // 上端落在台面边缘（离塔心 2.55），下端往塔外退 run 米
        Vector3 top    = c + rot * new Vector3(side * 2.55f, rise, 0f);
        Vector3 bottom = c + rot * new Vector3(side * (2.55f + run), 0f, 0f);
        Vector3 mid    = (top + bottom) * 0.5f;
        Vector3 dir    = top - bottom;

        var rampRot = Quaternion.LookRotation(dir.normalized, Vector3.up);

        // 斜板（局部 Z 沿坡面、Y 是板厚）
        Box(mid, new Vector3(halfW * 2f, 0.26f, dir.magnitude), rampRot, Wood, WoodDark);

        // 两侧扶手：立杆 + 顶杆
        for (int s = -1; s <= 1; s += 2)
        {
            var rail = rampRot * new Vector3(s * (halfW + 0.06f), 0f, 0f);
            Box(mid + rail + Vector3.up * 0.75f, new Vector3(0.12f, 1.5f, 0.12f), rampRot, WoodDark, WoodDark);
            Box(mid + rail + rampRot * new Vector3(0f, 0.82f, 0f), new Vector3(0.16f, 0.14f, dir.magnitude * 0.92f), rampRot, Wood, WoodDark);
            Box(mid + rail + Vector3.up * 0.5f, new Vector3(0.1f, 0.1f, dir.magnitude * 0.8f), rampRot, WoodDark, WoodDark);
        }

        // 坡道下端的踏脚台（免得板底直接悬在地面上方）
        Vector3 foot = bottom - rot * new Vector3(side * 0.6f, 0f, 0f);
        Box(foot + Vector3.up * 0.12f, new Vector3(halfW * 2f + 0.5f, 0.35f, 1.6f), rot, StoneDark, StoneDark);
    }

    // ================= 3. 风车 =================

    static void BuildWindmill(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float bodyH = 8.2f, r = 3.3f;

        // 石圆塔身（底粗顶细）+ 顶盖
        Cyl(c + Vector3.up * 0.1f, 0.7f, r + 0.7f, r + 0.55f, 16, StoneDark, true);   // 台基
        Cyl(c + Vector3.up * 0.7f, bodyH, r, r * 0.86f, 16, Stone, false);
        Prism(c + Vector3.up * (0.7f + bodyH), 2.6f, r * 0.95f, 0.35f, 16, Roof);      // 锥顶
        Disc(c + Vector3.up * (0.7f + bodyH + 0.02f), 0f, r * 0.9f, 16, Quaternion.identity, RoofAlt);

        // 门 + 窗（沿塔身盘旋往上）
        Box(c + rot * new Vector3(0f, 1.85f, r - 0.06f), new Vector3(1.2f, 2.3f, 0.2f), rot, Wood, WoodDark);
        for (int i = 0; i < 3; i++)
        {
            float a = 40f + i * 95f;
            float rad = a * Mathf.Deg2Rad;
            Vector3 p = c + new Vector3(Mathf.Cos(rad) * (r - 0.05f), 3.1f + i * 1.7f, Mathf.Sin(rad) * (r - 0.05f));
            Box(p, new Vector3(0.62f, 0.72f, 0.16f), Quaternion.Euler(0f, -a + 90f, 0f), Window, WoodDark);
        }

        // 风车叶：轮毂 + 四支叶片（每支 = 主梁 + 一排格栅板），整体偏在塔身前面
        Vector3 hub = c + rot * new Vector3(0f, 0f, r + 0.85f);
        hub.y = c.y + 0.7f + bodyH * 0.78f;
        var hubRot = rot;
        for (int arm = 0; arm < 4; arm++)
        {
            float a = arm * 90f + 18f;
            var armRot = hubRot * Quaternion.Euler(0f, 0f, a);
            Box(hub + armRot * new Vector3(0f, 3.0f, 0f), new Vector3(0.32f, 6.0f, 0.3f), armRot, WoodLight, WoodDark);
            for (int k = 1; k <= 5; k++)
            {
                Box(hub + armRot * new Vector3(0f, 1.1f + k * 1.0f, 0.06f), new Vector3(1.5f, 0.72f, 0.08f), armRot, Cloth, Cloth);
            }
        }
        Box(hub, new Vector3(0.9f, 0.9f, 0.9f), hubRot, WoodDark, WoodDark);
        Cyl(hub + hubRot * new Vector3(0f, 0f, -0.3f), 0.5f, 0.22f, 0.22f, 6, Iron, true);
    }

    // ================= 4. 水车磨坊 =================

    static void BuildWaterMill(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        // 木屋（比村民居瘦一点、高一点，因为是磨坊）
        House(c, yaw, 6.4f, 5.2f, 3.4f, new Color32(0xdf, 0xcf, 0xa8, 255), Roof, true, 5);

        // 水轮：立在大的一侧，半径 3.2，靠一圈 16 边
        Vector3 wheel = c + rot * new Vector3(4.5f, 2.1f, 0f);
        var wheelRot = rot * Quaternion.Euler(0f, 0f, 90f);       // 圆面朝侧面
        Disc(wheel, 2.55f, 3.2f, 20, wheelRot, WoodDark);          // 外圈
        Disc(wheel, 0.85f, 1.25f, 12, wheelRot, WoodDark);         // 内圈
        Cyl(wheel + wheelRot * new Vector3(0f, 0f, -0.5f), 1.0f, 0.35f, 0.35f, 8, WoodDark, true);   // 轮轴
        for (int i = 0; i < 8; i++)                                 // 辐条
        {
            var spokeRot = wheelRot * Quaternion.Euler(0f, 0f, i * 45f);
            Box(wheel + spokeRot * new Vector3(0f, 1.9f, 0f), new Vector3(0.22f, 3.8f, 0.22f), spokeRot, WoodLight, WoodDark);
        }
        for (int i = 0; i < 16; i++)                                // 叶片
        {
            float a = i * 22.5f;
            var armRot = wheelRot * Quaternion.Euler(0f, 0f, a);
            Box(wheel + armRot * new Vector3(0f, 2.9f, 0.35f), new Vector3(1.1f, 0.5f, 0.16f), armRot, WoodDark, WoodDark);
        }

        // 引水木槽：从屋里伸到水轮上方
        Vector3 flumeA = c + rot * new Vector3(1.6f, 3.3f, 0f);
        Vector3 flumeB = wheel + Vector3.up * 3.6f;
        Vector3 mid = (flumeA + flumeB) * 0.5f;
        Vector3 dir = flumeB - flumeA;
        var flumeRot = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
        Box(mid, new Vector3(0.95f, dir.magnitude, 0.5f), flumeRot, WoodLight, WoodDark);

        // 门口的磨盘 + 麻袋
        Box(c + rot * new Vector3(-3.4f, 0.5f, 2.2f), new Vector3(0.3f, 1.0f, 0.3f), rot, WoodDark, WoodDark);
        Cyl(c + rot * new Vector3(-3.4f, 1.0f, 2.2f), 0.28f, 1.0f, 0.95f, 14, Stone, true);
        Box(c + rot * new Vector3(-4.4f, 0.4f, 1.2f), new Vector3(0.8f, 0.8f, 0.7f), rot, Cloth, Cloth);
        Box(c + rot * new Vector3(-4.5f, 1.05f, 1.25f), new Vector3(0.62f, 0.55f, 0.55f), rot, Cloth, Cloth);
    }

    // ================= 5. 木桥 =================

    /// <summary>
    /// 跨河的木桥（沿局部 Z 轴铺开，center 是河中心线）。
    /// halfSpan 由选点阶段按"两岸实际上岸位置"算出来传进来。
    ///
    /// 【踩过的坑】
    ///  · 桥面原来用三段拼（中段 2.95、两端 2.82）→ 接缝处出现折角，看着像断了一样。现在一整块直板。
    ///  · 桥高原来取两岸的 max、桥台还是固定 3.2 米高的盒子 → 岸低的那一头整个吊在半空。
    ///    现在两个桥台**各自按脚下真实地形往下扎**，桥墩也从河床扎到桥面，没有任何悬空件。
    /// </summary>
    static void BuildBridge(Vector3 c, float yaw, float variant, float halfSpan)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float halfW = 2.4f;
        if (halfSpan < 7f) halfSpan = 16f;          // 兜底（正常由选点阶段给准）

        const float deckY = 2.95f;                  // 桥面相对 c 的高度（c.y = 桥面高 - 2.95）
        float deckTop = c.y + deckY + 0.17f;

        // ---- 桥面：一整块直板，不做分段 ----
        Box(c + Vector3.up * deckY, new Vector3(halfW * 2f, 0.34f, halfSpan * 2f), rot, Wood, WoodDark);

        // ---- 两端桥台：从桥面一直扎到各自脚下的地面 ----
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 at = c + rot * new Vector3(0f, 0f, s * (halfSpan - 1.5f));
            float g = OpenWorld.Height(at.x, at.z);
            float bottom = Mathf.Min(g, c.y) - 1.5f;
            float h = Mathf.Max(1.5f, deckTop - bottom);
            float midY = (deckTop + bottom) * 0.5f;

            Box(new Vector3(at.x, midY, at.z), new Vector3(halfW * 2f + 1.2f, h, 3.2f), rot, Stone, StoneDark);
            Box(new Vector3(at.x, deckTop + 0.1f, at.z), new Vector3(halfW * 2f + 1.6f, 0.3f, 3.7f), rot, StoneDark, StoneDark);
        }

        // ---- 水中桥墩：从河床扎到桥面（按实际地形算长度，不悬空） ----
        for (int s = -1; s <= 1; s += 2)
        {
            float zz = s * halfSpan * 0.46f;
            for (int col = -1; col <= 1; col += 2)
            {
                Vector3 at = c + rot * new Vector3(col * 1.35f, 0f, zz);
                float g = OpenWorld.Height(at.x, at.z);
                float top = c.y + deckY - 0.18f;
                float bottom = g - 1.2f;
                float h = Mathf.Max(0.8f, top - bottom);
                Box(new Vector3(at.x, (top + bottom) * 0.5f, at.z), new Vector3(0.46f, h, 0.46f), rot, WoodDark, WoodDark);
            }

            Vector3 mid = c + rot * new Vector3(0f, 0f, zz);
            Box(new Vector3(mid.x, c.y + deckY - 0.34f, mid.z), new Vector3(halfW * 2f - 0.7f, 0.3f, 0.55f), rot, Wood, WoodDark);
        }

        // ---- 栏杆：立柱 + 上下两道横杆 ----
        int posts = Mathf.Max(5, Mathf.RoundToInt(halfSpan * 2f / 2.6f));
        for (int s = -1; s <= 1; s += 2)
        {
            for (int i = 0; i <= posts; i++)
            {
                float zz = -halfSpan + 1.0f + i * (halfSpan * 2f - 2.0f) / posts;
                Box(c + rot * new Vector3(s * halfW, deckY + 0.62f, zz), new Vector3(0.18f, 0.95f, 0.18f), rot, WoodDark, WoodDark);
            }
            Box(c + rot * new Vector3(s * halfW, deckY + 1.05f, 0f), new Vector3(0.2f, 0.16f, halfSpan * 2f - 1.8f), rot, Wood, WoodDark);
            Box(c + rot * new Vector3(s * halfW, deckY + 0.36f, 0f), new Vector3(0.16f, 0.12f, halfSpan * 2f - 1.8f), rot, Wood, WoodDark);
        }
    }

    // ================= 6. 猎人小屋 =================

    static void BuildHuntersHut(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        House(c, yaw, 5.0f, 4.2f, 2.7f, new Color32(0xcf, 0xc0, 0xa2, 255), RoofAlt, true, 7);

        // 兽皮架
        Vector3 rack = c + rot * new Vector3(3.9f, 0f, 1.6f);
        var rackRot = rot * Quaternion.Euler(0f, 12f, 0f);
        for (int s = -1; s <= 1; s += 2)
            Box(rack + rackRot * new Vector3(s * 1.1f, 1.0f, 0f), new Vector3(0.16f, 2.0f, 0.16f), rackRot, Wood, WoodDark);
        Box(rack + Vector3.up * 1.85f, new Vector3(2.5f, 0.14f, 0.14f), rackRot, Wood, WoodDark);
        Box(rack + rackRot * new Vector3(0f, 1.25f, 0f), new Vector3(1.1f, 1.2f, 0.1f), rackRot, Hide, Hide);
        Box(rack + rackRot * new Vector3(0.9f, 1.35f, 0f), new Vector3(0.7f, 0.85f, 0.09f), rackRot, Hide, Hide);

        // 箭靶：木架 + 一圈草绳 + 中心红心
        Vector3 target = c + rot * new Vector3(-3.6f, 0f, 2.6f);
        var tRot = rot * Quaternion.Euler(0f, -14f, 0f);
        Box(target + tRot * new Vector3(-0.9f, 0.9f, 0f), new Vector3(0.16f, 1.8f, 0.16f), tRot, Wood, WoodDark);
        Box(target + tRot * new Vector3(0.9f, 0.9f, 0f), new Vector3(0.16f, 1.8f, 0.16f), tRot, Wood, WoodDark);
        Disc(target + Vector3.up * 1.6f, 0f, 0.95f, 16, tRot * Quaternion.Euler(90f, 0f, 0f), Cloth);
        Disc(target + Vector3.up * 1.6f + tRot * new Vector3(0f, 0f, 0.04f), 0f, 0.42f, 12, tRot * Quaternion.Euler(90f, 0f, 0f), new Color32(0xc0, 0x4a, 0x3a, 255));

        // 柴堆 + 凳子
        for (int i = 0; i < 3; i++)
        {
            for (int k = 0; k < 3; k++)
                Box(c + rot * new Vector3(-4.6f + k * 0.42f, 0.22f + i * 0.42f, -1.4f), new Vector3(0.4f, 0.4f, 2.4f), rot, Wood, WoodDark);
        }
        Box(c + rot * new Vector3(2.2f, 0.45f, -2.6f), new Vector3(1.0f, 0.16f, 1.0f), rot, WoodLight, WoodDark);
        for (int s = -1; s <= 1; s += 2)
            Box(c + rot * new Vector3(2.2f + s * 0.35f, 0.22f, -2.6f), new Vector3(0.14f, 0.45f, 0.9f), rot, WoodDark, WoodDark);
    }

    // ================= 7. 伐木场 =================

    static void BuildLumberCamp(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        // A 字帐篷：两片斜面 + 后挡板 + 门口的帘子
        var tentRot = rot * Quaternion.Euler(0f, 8f, 0f);
        Vector3 tent = c + tentRot * new Vector3(-2.6f, 0f, -1.2f);
        Box(tent + tentRot * new Vector3(0f, 1.35f, -0.95f), new Vector3(3.6f, 2.7f, 0.16f), tentRot * Quaternion.Euler(24f, 0f, 0f), Cloth, Cloth);
        Box(tent + tentRot * new Vector3(0f, 1.35f, 0.95f), new Vector3(3.6f, 2.7f, 0.16f), tentRot * Quaternion.Euler(-24f, 0f, 0f), Cloth, Cloth);
        Box(tent + tentRot * new Vector3(1.72f, 0.95f, 0f), new Vector3(0.16f, 1.9f, 1.9f), tentRot, Cloth, Cloth);
        Box(tent + tentRot * new Vector3(0f, 2.62f, 0f), new Vector3(3.8f, 0.18f, 0.3f), tentRot, Wood, WoodDark);

        // 锯木架：X 形腿 + 横着的原木 + 锯
        Vector3 saw = c + rot * new Vector3(3.0f, 0f, 0.6f);
        var sawRot = rot * Quaternion.Euler(0f, -20f, 0f);
        for (int s = -1; s <= 1; s += 2)
        {
            Box(saw + sawRot * new Vector3(s * 1.6f, 0.62f, 0.28f), new Vector3(0.14f, 1.5f, 0.14f), sawRot * Quaternion.Euler(20f, 0f, 0f), Wood, WoodDark);
            Box(saw + sawRot * new Vector3(s * 1.6f, 0.62f, -0.28f), new Vector3(0.14f, 1.5f, 0.14f), sawRot * Quaternion.Euler(-20f, 0f, 0f), Wood, WoodDark);
        }
        Box(saw + sawRot * new Vector3(0f, 1.15f, 0f), new Vector3(0.55f, 0.55f, 4.6f), sawRot, WoodLight, WoodDark);
        Box(saw + sawRot * new Vector3(0.6f, 1.55f, 0f), new Vector3(0.06f, 0.35f, 1.5f), sawRot, Iron, Iron);

        // 木堆：三层原木
        for (int layer = 0; layer < 3; layer++)
        {
            int n = 4 - layer;
            for (int k = 0; k < n; k++)
            {
                Vector3 p = c + rot * new Vector3(-3.0f + k * 0.62f, 0.32f + layer * 0.6f, 2.6f);
                Box(p, new Vector3(0.55f, 0.55f, 3.4f), rot, WoodLight, WoodDark);
            }
        }

        // 树桩 + 斧头 + 散落木头
        Vector3 stump = c + rot * new Vector3(1.4f, 0f, -2.6f);
        Cyl(stump, 0.55f, 0.62f, 0.58f, 10, WoodDark, true);
        Box(stump + Vector3.up * 0.75f, new Vector3(0.1f, 0.85f, 0.1f), rot * Quaternion.Euler(0f, 0f, 28f), Wood, WoodDark);
        Box(stump + Vector3.up * 1.02f, new Vector3(0.42f, 0.3f, 0.1f), rot * Quaternion.Euler(0f, 0f, 28f), Iron, Iron);
        Box(c + rot * new Vector3(4.6f, 0.28f, 2.2f), new Vector3(0.5f, 0.5f, 2.2f), rot * Quaternion.Euler(0f, 34f, 0f), WoodLight, WoodDark);

        // 篝火
        Vector3 fire = c + rot * new Vector3(0.4f, 0f, 2.2f);
        for (int i = 0; i < 7; i++)
        {
            float a = i * Mathf.PI * 2f / 7f;
            Box(fire + new Vector3(Mathf.Cos(a) * 0.75f, 0.14f, Mathf.Sin(a) * 0.75f), new Vector3(0.3f, 0.24f, 0.3f), Stone, StoneDark);
        }
        Flames.Add(fire + Vector3.up * 0.3f);
    }

    // ================= 8. 篝火休息点 =================

    static void BuildRestCamp(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        // 火塘：一大圈石头 + 交叉柴 + 三脚架吊锅
        Box(c + Vector3.up * 0.12f, new Vector3(3.2f, 0.18f, 3.2f), StoneDark, StoneDark);
        for (int i = 0; i < 9; i++)
        {
            float a = i * Mathf.PI * 2f / 9f;
            Box(c + new Vector3(Mathf.Cos(a) * 1.25f, 0.18f, Mathf.Sin(a) * 1.25f), new Vector3(0.42f, 0.32f, 0.42f), Stone, StoneDark);
        }
        for (int i = 0; i < 4; i++)
            Box(c + Vector3.up * (0.3f + i * 0.1f), new Vector3(0.15f, 0.15f, 1.9f), Quaternion.Euler(0f, i * 45f, 0f), Wood, WoodDark);
        Flames.Add(c + Vector3.up * 0.38f);

        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.PI * 2f / 3f + 0.5f;
            Box(c + new Vector3(Mathf.Cos(a) * 0.85f, 0.95f, Mathf.Sin(a) * 0.85f), new Vector3(0.14f, 1.9f, 0.14f),
                Quaternion.Euler(Mathf.Sin(a) * 15f, 0f, -Mathf.Cos(a) * 15f), Wood, WoodDark);
        }
        Box(c + Vector3.up * 0.72f, new Vector3(0.6f, 0.45f, 0.6f), Iron, Iron);

        // 两根长凳
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 bench = c + rot * new Vector3(s * 3.0f, 0f, 0.6f);
            var bRot = rot * Quaternion.Euler(0f, 90f * s, 0f);
            Box(bench + Vector3.up * 0.48f, new Vector3(2.6f, 0.16f, 0.7f), bRot, WoodLight, WoodDark);
            for (int k = -1; k <= 1; k += 2)
                Box(bench + bRot * new Vector3(k * 1.0f, 0.24f, 0f), new Vector3(0.16f, 0.48f, 0.6f), bRot, WoodDark, WoodDark);
        }

        // 旁边一堆柴
        for (int i = 0; i < 6; i++)
            Box(c + rot * new Vector3(2.9f + (i % 2) * 0.42f, 0.2f + (i / 2) * 0.4f, -2.2f), new Vector3(0.4f, 0.4f, 1.9f),
                rot * Quaternion.Euler(0f, 12f * i, 0f), Wood, WoodDark);
    }

    // ================= 9. 断柱石阵 =================

    static void BuildRuinPillars(Vector3 c, float yaw, float variant)
    {
        int seed = (int)(c.x * 11f + c.z * 17f);

        // 半埋的铺地（一个大圆盘 + 一圈台阶），边角故意做出破损感
        Disc(c + Vector3.up * 0.12f, 0f, 12.5f, 20, Quaternion.identity, StoneDark);
        Disc(c + Vector3.up * 0.32f, 4.6f, 9.6f, 20, Quaternion.identity, Stone);
        for (int i = 0; i < 14; i++)
        {
            float a = H(i, seed, 61) * Mathf.PI * 2f;
            float r = 5.5f + H(i, seed, 62) * 6.0f;
            Box(c + new Vector3(Mathf.Cos(a) * r, 0.5f, Mathf.Sin(a) * r),
                new Vector3(1.2f + H(i, seed, 63) * 1.6f, 0.7f, 1.2f + H(i, seed, 64) * 1.6f),
                Quaternion.Euler(0f, H(i, seed, 65) * 360f, 0f), Moss, StoneDark);
        }

        // 立柱：6~8 根，绕中心一圈，高度参差（有的断了、有的柱头被砸掉）
        int n = 6 + Mathf.RoundToInt(variant * 3f);
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n + H(i, seed, 71) * 0.3f;
            float r = 7.4f + H(i, seed, 72) * 0.9f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            var capRot = Quaternion.Euler(0f, H(i, seed, 74) * 360f, 0f);

            float shaftH = 2.2f + H(i, seed, 76) * 5.6f;
            bool broken = H(i, seed, 77) > 0.55f;
            if (broken) shaftH *= 0.45f;

            // 柱础
            Box(p + Vector3.up * 0.22f, new Vector3(1.5f, 0.44f, 1.5f), capRot, StoneDark, StoneDark);
            // 柱身
            Cyl(p + Vector3.up * 0.44f, shaftH, 0.56f, 0.5f, 10, Stone, false);

            if (!broken)
            {
                // 完整的柱头：两层压顶石
                Box(p + Vector3.up * (0.44f + shaftH + 0.2f), new Vector3(1.5f, 0.42f, 1.5f), capRot, Stone, StoneDark);
                Box(p + Vector3.up * (0.44f + shaftH + 0.62f), new Vector3(1.7f, 0.38f, 1.7f), capRot, StoneDark, StoneDark);
            }
            else
            {
                // 断口：盖一块歪着的不平整碎石
                Box(p + Vector3.up * (0.44f + shaftH), new Vector3(1.25f, 0.3f, 1.25f), Quaternion.Euler(6f, 20f, 4f), Stone, StoneDark);
            }
        }

        // 倒在地上的柱身（两三根，堆在边上）+ 石头周围一圈青苔
        for (int i = 0; i < 3; i++)
        {
            float a = H(i, seed, 81) * Mathf.PI * 2f;
            float r = 10.0f + H(i, seed, 82) * 2.2f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            var lieRot = Quaternion.Euler(0f, H(i, seed, 83) * 360f, 0f);

            Disc(p + Vector3.up * 0.02f, 0f, 1.6f + H(i, seed, 84) * 0.8f, 10, Quaternion.identity, Moss);
            Box(p + Vector3.up * 0.58f, new Vector3(0.58f, 0.58f, 5.2f), lieRot, Stone, StoneDark);
            Box(p + lieRot * new Vector3(0f, 0.58f, 2.6f), new Vector3(1.4f, 0.42f, 1.4f), lieRot, StoneDark, StoneDark);
        }

        // 中央的祭台残座
        Box(c + Vector3.up * 0.62f, new Vector3(2.6f, 0.6f, 2.6f), Quaternion.Euler(0f, 18f, 0f), Stone, StoneDark);
        Box(c + Vector3.up * 1.14f, new Vector3(1.9f, 0.44f, 1.9f), Quaternion.Euler(0f, 42f, 0f), StoneDark, StoneDark);
    }

    // ================= 10. 风神祭坛 =================

    static void BuildWindShrine(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        // 圆形石台：外圈台基层 + 内圈铺地
        Cyl(c + Vector3.up * 0.02f, 0.3f, 9.2f, 8.9f, 20, StoneDark, true);
        Cyl(c + Vector3.up * 0.32f, 0.34f, 8.0f, 7.9f, 20, Stone, true);
        Disc(c + Vector3.up * 0.68f, 5.6f, 6.4f, 20, Quaternion.identity, StoneDark);
        Disc(c + Vector3.up * 0.70f, 0f, 5.6f, 20, Quaternion.identity, new Color32(0xbf, 0xd8, 0xd2, 255));

        // 四角小柱 + 顶上风铃
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * 7.1f, 0.66f, Mathf.Sin(a) * 7.1f);
            Cyl(p, 3.4f, 0.36f, 0.3f, 8, Stone, false);
            Box(p + Vector3.up * 3.5f, new Vector3(0.95f, 0.28f, 0.95f), Quaternion.identity, StoneDark, StoneDark);
            Ball(p + Vector3.up * 3.95f, 0.42f, Gold, 6, 0.8f, 0f);
        }

        // 三面立碑（刻着风的纹样）
        for (int i = 0; i < 3; i++)
        {
            float a = yaw * Mathf.Deg2Rad + i * Mathf.PI * 2f / 3f - Mathf.PI * 0.5f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * 5.2f, 0.7f, Mathf.Sin(a) * 5.2f);
            var sRot = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
            Box(p + Vector3.up * 0.25f, new Vector3(1.5f, 0.5f, 0.7f), sRot, StoneDark, StoneDark);
            Box(p + Vector3.up * 2.1f, new Vector3(1.25f, 3.4f, 0.42f), sRot, Stone, StoneDark);
            Box(p + sRot * new Vector3(0f, 2.35f, 0.24f), new Vector3(0.7f, 0.7f, 0.08f), sRot, new Color32(0x7e, 0xd6, 0xc0, 255), StoneDark);
            Box(p + sRot * new Vector3(0f, 1.35f, 0.24f), new Vector3(0.95f, 0.14f, 0.08f), sRot, new Color32(0x7e, 0xd6, 0xc0, 255), StoneDark);
        }

        // 中央祭坛 + 悬浮的风之石
        Box(c + Vector3.up * 1.1f, new Vector3(2.2f, 0.8f, 2.2f), rot, Stone, StoneDark);
        Box(c + Vector3.up * 1.62f, new Vector3(1.6f, 0.3f, 1.6f), rot, StoneDark, StoneDark);
        Box(c + Vector3.up * 2.9f, new Vector3(1.2f, 1.2f, 1.2f), Quaternion.Euler(0f, 24f, 24f), new Color32(0x86, 0xdc, 0xc6, 255), new Color32(0x5c, 0xba, 0xa8, 255));
        Box(c + Vector3.up * 4.15f, new Vector3(0.75f, 0.75f, 0.75f), Quaternion.Euler(18f, 42f, 12f), Gold, new Color32(0xc8, 0xb0, 0x6a, 255));

        // 台阶（朝入口）
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = c + rot * new Vector3(0f, 0f, 9.6f + i * 1.05f);
            Box(p + Vector3.up * (0.1f - i * 0.02f), new Vector3(5.0f - i * 0.6f, 0.24f, 1.15f), rot, Stone, StoneDark);
        }
    }

    // ================= 11. 残墙 =================

    static void BuildRuinWall(Vector3 c, float yaw, float variant)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        int seed = (int)(c.x * 19f + c.z * 23f);

        // 铺地
        Disc(c + Vector3.up * 0.1f, 0f, 11.0f, 18, Quaternion.identity, StoneDark);
        Disc(c + Vector3.up * 0.22f, 0f, 8.0f, 18, Quaternion.identity, Moss);

        // 主墙体：一段 18 米长的石墙，上沿参差（分成 9 段，每段高度不同）
        float wallLen = 18f, seg = wallLen / 9f;
        for (int i = 0; i < 9; i++)
        {
            float x = -wallLen * 0.5f + seg * (i + 0.5f);
            float hgt = 1.6f + H(i, seed, 91) * 5.2f;
            // 中间偏右故意塌一大块
            if (i == 5 || i == 6) hgt *= 0.35f;

            Box(c + rot * new Vector3(x, 0.22f + hgt * 0.5f, 0f), new Vector3(seg * 1.02f, hgt, 1.35f), rot, Stone, StoneDark);
            // 顶部压顶石（矮的段就没有）
            if (hgt > 3.0f)
                Box(c + rot * new Vector3(x, 0.22f + hgt + 0.16f, 0f), new Vector3(seg * 1.02f, 0.32f, 1.65f), rot, StoneDark, StoneDark);
        }

        // 半塌的拱门：两侧塔柱 + 上半圈的拱石
        Vector3 arch = c + rot * new Vector3(0f, 0f, 3.6f);
        for (int s = -1; s <= 1; s += 2)
        {
            Box(arch + rot * new Vector3(s * 3.4f, 2.6f, 0f), new Vector3(2.0f, 5.2f, 3.0f), rot, Stone, StoneDark);
            Box(arch + rot * new Vector3(s * 3.4f, 5.4f, 0f), new Vector3(2.4f, 0.5f, 3.4f), rot, StoneDark, StoneDark);
        }
        for (int i = 0; i <= 6; i++)                    // 拱石（上半圈用一排小方块拼）
        {
            float t = i / 6f;
            float a = Mathf.PI * (1f - t);
            Vector3 p = arch + rot * new Vector3(Mathf.Cos(a) * 3.4f, 4.4f + Mathf.Sin(a) * 2.3f, 0f);
            Box(p, new Vector3(1.35f, 0.85f, 2.6f), rot * Quaternion.Euler(0f, 0f, -a * Mathf.Rad2Deg + 90f), Stone, StoneDark);
        }

        // 散落的石块
        for (int i = 0; i < 12; i++)
        {
            float a = H(i, seed, 92) * Mathf.PI * 2f;
            float r = 4.0f + H(i, seed, 93) * 6.2f;
            float s = 0.6f + H(i, seed, 94) * 1.3f;
            Box(c + new Vector3(Mathf.Cos(a) * r, 0.3f, Mathf.Sin(a) * r),
                new Vector3(s, s * 0.75f, s * 0.9f),
                Quaternion.Euler(H(i, seed, 95) * 30f, H(i, seed, 96) * 360f, H(i, seed, 97) * 30f),
                Stone, StoneDark);
        }
    }
}
