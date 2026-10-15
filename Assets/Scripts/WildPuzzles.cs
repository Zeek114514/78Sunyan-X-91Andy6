using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 解密场地的三种模式（低 → 高难度）：
///   AllLit   全部点亮 —— 场地上几根元素方碑，挨个按 E 点亮，全亮即解。
///   Ordered  按顺序点亮 —— 方碑顶端各有一颗不同颜色的宝石，场地入口的提示石碑从上到下
///                        刻着要求的顺序；必须按这个顺序点亮，点错全部熄灭重来。
///   Guarded  解密 + 守护战 —— 点亮方碑之后，机关启动的同时会冒出守卫怪，
///                        要连怪一起清掉才能开中央的宝箱。
/// </summary>
public enum PuzzleKind : byte { AllLit = 0, Ordered, Guarded }

/// <summary>
/// 解密场地的**外观风格**：蒙德是元素方碑，璃月是石灯阵 / 石鼓（阶段 F）。
///
/// 玩法（AllLit / Ordered / Guarded）、交互（走到跟前按 E）、奖励宝箱、守卫战全部共用 ——
/// 只有外形、称呼和石台尺寸不同。璃月那两套的几何在 <c>LiyuePuzzles.cs</c>
/// （LiyueBuildings 的 partial），这里只做风格分派。
/// </summary>
public enum PuzzleStyle : byte { Monolith = 0, LiyueLamp, LiyueDrum }

/// <summary>
/// 解密场地的几何：圆形石台 + 方碑 + 入口火把 + 机关台 + 提示石碑。
///
/// 分两层：
///   · 静态部分（石台 / 铺地 / 火把 / 机关台 / 提示石碑）走 AppendGround，合成到野外那一个网格里；
///   · 可交互的方碑走 BuildMonolithMesh —— 方碑要单独成 GameObject（要点亮 / 熄灭），
///     而且"亮"和"暗"是两个网格直接换，不改材质 —— 世界的着色器是自定义顶点色的，改发光会把渲染搞复杂。
///
/// 所有摆放尺寸都由 PlatformRadius / RingRadius / NodeCount / NodePos 统一算，
/// 几何和运行时（PuzzleSite）用同一套，保证方碑正好落在石台上。
/// </summary>
public static class WildPuzzles
{
    // ---------- 配色（和野外建筑同一套色系） ----------

    static readonly Color Stone     = new Color32(0xd8, 0xd0, 0xbc, 255);
    static readonly Color StoneDark = new Color32(0xb3, 0xa9, 0x95, 255);
    static readonly Color StoneMoss = new Color32(0xa8, 0xb0, 0x94, 255);
    static readonly Color Wood      = new Color32(0x8a, 0x62, 0x40, 255);
    static readonly Color WoodDark  = new Color32(0x6b, 0x4a, 0x2f, 255);
    static readonly Color Iron      = new Color32(0x6d, 0x6a, 0x72, 255);
    static readonly Color Cloth     = new Color32(0xb8, 0x8a, 0x52, 255);

    /// <summary>方碑宝石的四种颜色：宝石颜色 = 这根方碑的身份，提示石碑照着这个颜色排序</summary>
    public static readonly Color[] GemColors =
    {
        new Color32(0xe0, 0x5c, 0x4c, 255),   // 红
        new Color32(0x5c, 0x9c, 0xe0, 255),   // 蓝
        new Color32(0x66, 0xc4, 0x76, 255),   // 绿
        new Color32(0xe6, 0xc4, 0x54, 255),   // 黄
    };

    // ---------- 尺寸（几何与运行时共用） ----------

    /// <summary>石台半径</summary>
    public static float PlatformRadius(PuzzleKind kind)
    {
        switch (kind)
        {
            case PuzzleKind.AllLit:  return 7.5f;
            case PuzzleKind.Ordered: return 8.5f;
            default:                 return 9.5f;      // Guarded：还要站得下守卫
        }
    }

    /// <summary>方碑所在的圈半径</summary>
    public static float RingRadius(PuzzleKind kind) => PlatformRadius(kind) * 0.66f;

    /// <summary>石台半径（按风格取：璃月那两套的石台在 LiyueBuildings 里）</summary>
    public static float PlatformRadius(PuzzleKind kind, PuzzleStyle style) =>
        style == PuzzleStyle.Monolith ? PlatformRadius(kind) : LiyueBuildings.PuzzlePlatformRadius(kind);

    /// <summary>节点所在的圈半径（按风格取）</summary>
    public static float RingRadius(PuzzleKind kind, PuzzleStyle style) => PlatformRadius(kind, style) * 0.66f;

    /// <summary>几根方碑</summary>
    public static int NodeCount(PuzzleKind kind) => kind == PuzzleKind.AllLit ? 3 : 4;

    /// <summary>第 index 根方碑在世界里的位置（石台中心 + 朝向 + 圈上均分）
    /// —— 半径由调用方给：蒙德和璃月的石台大小不一样，几何和运行时必须用同一个</summary>
    public static Vector3 NodePos(Vector3 center, float yaw, int count, float ring, int index)
    {
        float a = (yaw + index * 360f / count) * Mathf.Deg2Rad;
        return center + new Vector3(Mathf.Cos(a) * ring, 0f, Mathf.Sin(a) * ring);
    }

    /// <summary>第 index 根方碑在世界里的位置（用这种风格自己的圈半径）</summary>
    public static Vector3 NodePos(Vector3 center, float yaw, PuzzleKind kind, int index) =>
        NodePos(center, yaw, NodeCount(kind), RingRadius(kind), index);

    /// <summary>第 index 根方碑面向石台中心、再背对一点，玩家绕着走能看清正面</summary>
    public static float NodeYaw(Vector3 center, Vector3 pos) =>
        Mathf.Atan2(center.x - pos.x, center.z - pos.z) * Mathf.Rad2Deg;

    /// <summary>要求点亮的顺序（一个 0..n-1 的排列）。用哈希排，固定不变</summary>
    public static int[] RequiredOrder(PuzzleKind kind, float variant, int seed)
    {
        int n = NodeCount(kind);
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;

        // 按哈希打分排序（插排就够了，n 只有 3~4）
        for (int i = 1; i < n; i++)
        {
            int cur = order[i];
            float key = LowPoly.Hash01(cur, seed, 911);
            int j = i - 1;
            while (j >= 0 && LowPoly.Hash01(order[j], seed, 911) > key) { order[j + 1] = order[j]; j--; }
            order[j + 1] = cur;
        }

        // 排出来正好是"原样顺序"的话，换一下，免得看起来根本不需要提示
        if (n >= 2 && order[0] == 0 && order[1] == 1)
        {
            int t = order[0]; order[0] = order[n - 1]; order[n - 1] = t;
        }

        return order;
    }

    /// <summary>奖励宝箱放在石台中央的机关台上（和 AppendGround 里那个机关台对齐）</summary>
    public static Vector3 RewardPos(Vector3 center) => center + Vector3.up * 1.24f;

    // ================= 静态几何（合进野外那一个网格） =================

    static readonly List<Vector3> _v = new List<Vector3>();
    static readonly List<Color>   _c = new List<Color>();

    /// <summary>三个子网格：0 石墙贴图 / 1 瓦片贴图 / 2 顶点色（和其它野外建筑同一个路由规则）</summary>
    public static readonly List<int>[] SubTris = { new List<int>(), new List<int>(), new List<int>() };

    /// <summary>场地入口火把的世界坐标（落地时补火苗）</summary>
    public static readonly List<Vector3> Flames = new List<Vector3>();

    public static void Begin()
    {
        _v.Clear();
        _c.Clear();
        foreach (var list in SubTris) list.Clear();
        Flames.Clear();
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

    static List<int> T(Color color)
    {
        if (Same(color, Stone) || Same(color, StoneDark)) return SubTris[0];
        return SubTris[2];
    }

    static bool Same(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f;

    static void Box(Vector3 center, Vector3 size, Quaternion rot, Color top, Color side) =>
        LowPoly.Box(_v, _c, T(top), center, size, rot, top, side);

    static void Cyl(Vector3 basePos, float height, float bottomR, float topR, int sides, Color color, bool capTop = false) =>
        LowPoly.Cylinder(_v, _c, T(color), basePos, height, bottomR, topR, sides, color, capTop);

    static void Ball(Vector3 center, float radius, Color color, int segments, float squash = 1f, float seed = 0f) =>
        LowPoly.Sphere(_v, _c, T(color), center, radius, color, segments, squash, seed > 0f ? 1f : 0f, seed);

    static void Disc(Vector3 center, float innerR, float outerR, int sides, Quaternion rot, Color color) =>
        LowPoly.Ring(_v, _c, T(color), center, innerR, outerR, sides, rot, color);

    /// <summary>把一处场地铺到列表里。order 为 null 就不刻顺序提示（简单 / 守护模式）</summary>
    public static void AppendGround(PuzzleKind kind, Vector3 center, float yaw, int[] order)
    {
        float R = PlatformRadius(kind);
        float rM = RingRadius(kind);
        var rot = Quaternion.Euler(0f, yaw, 0f);
        int seed = (int)(center.x * 13f + center.z * 29f);

        // 台基 + 铺地：外圈一层毛石，里圈打磨过的石板，中央一圈青苔
        Cyl(center + Vector3.up * 0.02f, 0.34f, R + 1.2f, R + 1.0f, 24, StoneDark, true);
        Cyl(center + Vector3.up * 0.36f, 0.30f, R, R - 0.1f, 24, Stone, true);
        Disc(center + Vector3.up * 0.67f, 0f, R - 1.0f, 24, Quaternion.identity, StoneMoss);
        Disc(center + Vector3.up * 0.70f, 0f, 3.4f, 18, Quaternion.identity, StoneDark);

        // 台阶（朝入口）
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = center + rot * new Vector3(0f, 0f, R + 1.9f + i * 1.1f);
            Box(p + Vector3.up * (0.12f - i * 0.03f), new Vector3(5.2f - i * 0.6f, 0.26f, 1.2f), rot, Stone, StoneDark);
        }

        // 入口两侧的火把（一直亮着，只是氛围；不是解密的一部分）
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 p = center + rot * new Vector3(s * 2.4f, 0f, R + 1.4f);
            AppendTorch(p);
            Flames.Add(p + Vector3.up * 2.05f);
        }

        // 中央机关台：解开了宝箱才会出现在它上面
        Box(center + Vector3.up * 0.86f, new Vector3(2.5f, 0.42f, 2.5f), rot, Stone, StoneDark);
        Box(center + Vector3.up * 1.14f, new Vector3(2.0f, 0.2f, 2.0f), rot, StoneDark, StoneDark);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            Vector3 p = center + new Vector3(Mathf.Cos(a) * 1.65f, 0f, Mathf.Sin(a) * 1.65f);
            Cyl(p + Vector3.up * 0.67f, 0.72f, 0.24f, 0.18f, 6, Stone, true);
            Ball(p + Vector3.up * 1.52f, 0.2f, Iron, 5, 0.8f, 0f);
        }

        // 提示石碑（只有"按顺序"模式有）：从上到下刻着要求的宝石颜色
        if (order != null)
        {
            Vector3 p = center + rot * new Vector3(3.6f, 0f, R - 0.4f);
            var sRot = rot * Quaternion.Euler(0f, -16f, 0f);
            Box(p + Vector3.up * 0.24f, new Vector3(1.9f, 0.48f, 1.0f), sRot, StoneDark, StoneDark);
            Box(p + Vector3.up * 1.95f, new Vector3(1.6f, 3.4f, 0.55f), sRot, Stone, StoneDark);

            // 刻痕：order 里第 i 个 → 从上往下第 i 颗宝石
            for (int i = 0; i < order.Length; i++)
            {
                float y = 3.35f - i * 0.78f;
                Box(p + sRot * new Vector3(0f, y, 0.32f), new Vector3(1.05f, 0.12f, 0.08f), sRot, StoneDark, StoneDark);
                Ball(p + sRot * new Vector3(0f, y - 0.3f, 0.34f), 0.21f, GemColors[order[i]], 6, 0.85f, 0f);
            }

            // 石碑顶上的风向标，一眼能认出是提示碑
            Box(p + Vector3.up * 3.9f, new Vector3(0.16f, 0.9f, 0.16f), sRot, Iron, Iron);
            Box(p + Vector3.up * 4.3f, new Vector3(0.9f, 0.5f, 0.08f), sRot * Quaternion.Euler(0f, 24f, 0f), Cloth, Cloth);
        }

        // 台面外围散几块碎石 + 几丛矮草色的石包，别让石台显得太干净
        int rocks = 5 + (int)(LowPoly.Hash01((int)center.x, (int)center.z, 921) * 4f);
        for (int i = 0; i < rocks; i++)
        {
            float a = LowPoly.Hash01(i, seed, 931) * Mathf.PI * 2f;
            float r = R + 0.4f + LowPoly.Hash01(i, seed, 932) * 1.4f;
            float s = 0.5f + LowPoly.Hash01(i, seed, 933) * 1.0f;
            Box(center + new Vector3(Mathf.Cos(a) * r, 0.35f, Mathf.Sin(a) * r),
                new Vector3(s, s * 0.7f, s * 0.9f),
                Quaternion.Euler(LowPoly.Hash01(i, seed, 934) * 26f, LowPoly.Hash01(i, seed, 935) * 360f, 0f),
                Stone, StoneDark);
        }
    }

    /// <summary>一支木火把（立杆 + 火盆）。火苗由落地时单独挂（要看得到跳动）</summary>
    static void AppendTorch(Vector3 basePos)
    {
        Cyl(basePos, 1.9f, 0.14f, 0.11f, 6, Wood);
        Cyl(basePos + Vector3.up * 1.9f, 0.36f, 0.3f, 0.24f, 8, Iron, true);
        Box(basePos + Vector3.up * 0.12f, new Vector3(0.7f, 0.24f, 0.7f), Quaternion.identity, StoneDark, StoneDark);
    }

    // ================= 可交互的方碑 =================
    //
    // ★ 方碑 / 宝石走"单子网格 + 单个顶点色材质"这条独立通道（不用上面那套按颜色路由到贴图子网格的规则）：
    //   它们是独立 GameObject，挂三个材质只是因为要给石墙 / 瓦片两张贴图留位置，纯属浪费。
    //   用 LowPoly.BuildMesh 出来的就是单子网格、带顶点色的网格，配 world.Material 正好。

    static Mesh _monolithDark, _monolithLit;
    static readonly Mesh[] _gemLit = new Mesh[4];
    static readonly Mesh[] _gemDim = new Mesh[4];

    static readonly List<Vector3> _pv = new List<Vector3>();
    static readonly List<Color>   _pc = new List<Color>();
    static readonly List<int>     _pt = new List<int>();

    /// <summary>方碑网格：石柱 + 顶端宝石座。lit=true 的石柱上多三圈亮色刻纹。
    /// 暗 / 亮各只建一次，全场方碑共用</summary>
    public static Mesh MonolithMesh(bool lit)
    {
        if (lit && _monolithLit != null) return _monolithLit;
        if (!lit && _monolithDark != null) return _monolithDark;

        _pv.Clear(); _pc.Clear(); _pt.Clear();
        var tris = _pt;

        Color shaft  = lit ? Stone : new Color32(0x9a, 0x96, 0x8c, 255);
        Color groove = lit ? new Color32(0x8c, 0xe6, 0xd2, 255) : new Color32(0x6a, 0x67, 0x60, 255);

        // 底座 + 柱身 + 柱头
        LowPoly.Box(_pv, _pc, tris, new Vector3(0f, 0.22f, 0f), new Vector3(1.35f, 0.44f, 1.35f),
                    Quaternion.identity, StoneDark, StoneDark);
        LowPoly.Cylinder(_pv, _pc, tris, Vector3.zero, 3.0f, 0.5f, 0.44f, 10, shaft);
        LowPoly.Box(_pv, _pc, tris, new Vector3(0f, 3.02f, 0f), new Vector3(1.15f, 0.34f, 1.15f),
                    Quaternion.identity, StoneDark, StoneDark);
        LowPoly.Box(_pv, _pc, tris, new Vector3(0f, 3.5f, 0f), new Vector3(0.9f, 0.5f, 0.9f),
                    Quaternion.identity, Stone, StoneDark);

        // 柱身上的刻纹：暗的时候只有凹槽，亮的时候整圈发光色
        for (int i = 0; i < 3; i++)
            LowPoly.Box(_pv, _pc, tris, new Vector3(0f, 0.75f + i * 0.85f, 0f), new Vector3(1.06f, 0.1f, 1.06f),
                        Quaternion.identity, groove, groove);

        // 顶端宝石座
        LowPoly.Box(_pv, _pc, tris, new Vector3(0f, 3.88f, 0f), new Vector3(0.62f, 0.26f, 0.62f),
                    Quaternion.identity, StoneDark, StoneDark);

        var mesh = LowPoly.BuildMesh(lit ? "PuzzleMonolithLit" : "PuzzleMonolithDark", _pv, _pc, tris);
        if (lit) _monolithLit = mesh; else _monolithDark = mesh;
        return mesh;
    }

    /// <summary>方碑顶端的宝石（每种颜色 × 亮/暗 各只建一次，各个方碑共用）。
    /// ★ 暗的宝石也要看得清颜色 —— 不然"提示石碑上刻的颜色"和"方碑上的颜色"对不上，玩家没法解</summary>
    public static Mesh GemMesh(int gemColor, bool lit)
    {
        gemColor = Mathf.Clamp(gemColor, 0, GemColors.Length - 1);
        var cache = lit ? _gemLit : _gemDim;
        if (cache[gemColor] != null) return cache[gemColor];

        Color c = GemColors[gemColor];
        if (!lit) c = Color.Lerp(c, new Color32(0x78, 0x74, 0x6e, 255), 0.62f);

        _pv.Clear(); _pc.Clear(); _pt.Clear();
        LowPoly.Sphere(_pv, _pc, _pt, new Vector3(0f, 0.3f, 0f), 0.3f, c, 6);

        var mesh = LowPoly.BuildMesh("PuzzleGem" + gemColor + (lit ? "Lit" : "Dim"), _pv, _pc, _pt);
        cache[gemColor] = mesh;
        return mesh;
    }

    // ================= 按风格分派（蒙德 / 璃月共用同一套玩法，只有外形不同） =================

    /// <summary>节点本体网格：方碑 / 石灯 / 石鼓</summary>
    public static Mesh NodeMesh(PuzzleStyle style, bool lit) =>
        style == PuzzleStyle.Monolith ? MonolithMesh(lit) : LiyueBuildings.PuzzleNodeMesh(style, lit);

    /// <summary>节点上那点"身份色"：方碑顶端的宝石 / 石灯的灯罩 / 石鼓上的印记。
    /// ★ 暗的也必须看得清颜色 —— 顺序模式靠颜色对照入口石碑上的提示</summary>
    public static Mesh GlowMesh(PuzzleStyle style, int colorIndex, bool lit) =>
        style == PuzzleStyle.Monolith ? GemMesh(colorIndex, lit) : LiyueBuildings.PuzzleGlowMesh(style, colorIndex, lit);
}
