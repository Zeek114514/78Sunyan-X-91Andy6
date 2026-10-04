using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 湖畔石城（原创布局，按参考图）：建在湖心岛上的圆形石城。
/// 一圈外墙 + 24 座塔楼把城围住；南门接一座跨水长石桥通向大陆（出生点那边），
/// 东门接一座短石桥通往城墙外的大教堂岩石高台；城岛西南水边有木栈桥码头。
/// 城里是中央广场（风神像 + 水池 + 花坛 + 旗杆）、一圈环路、两圈红顶民居，
/// 4 座风车立在民居之间，另有酒馆 / 铁匠铺 / 方形瞭望塔三个地标。
///
/// 【放置规则】所有零件都要先在"占位表"里过一遍间隙检测（TryReserve）：撞到障碍或已放置的东西就换位置。
/// 障碍（广场 / 环路 / 城门大道 / 上墙坡道 / 城墙）是固定不动的，只有摆放物会去躲它们；
/// 摆放物之间互相也要躲开，所以不存在两栋房子穿模、风车插进屋顶这种事。
/// Append() 结束时会做一次自检并把结果打进日志（Overlap 必须为 0）。
///
/// 全部低模 + 顶点色，和地形共用材质；整城合成一个网格并挂 MeshCollider，
/// 玩家能过桥进城、上城墙、被墙挡住。尺寸都是绝对米数，改常量就能整体调整。
/// </summary>
public static class MondstadtTown
{
    // ================= 布局常量（所有零件都按这套坐标摆，改这里 = 改整座城的布局） =================

    public const float CenterX = 150f;                 // 城中心
    public const float CenterZ = 205f;
    public const float GroundY = 8f;                   // 城内地面高度

    public const int   WallSides = 24;                 // 城墙 24 边形（正好有一段正对南门、一段正对东门）
    public const float WallR     = 54f;                // 城墙中线半径
    public const float WallT     = 3f;                 // 墙厚
    public const float WallH     = 10f;                // 墙高（从城内地面算起）
    public const float WallTopY  = GroundY + WallH;    // 墙顶走道高度

    public const float IslandR   = WallR + 3f;         // 城岛台地平整半径（地形用）
    public const float MoatInner = WallR + 5f;         // 护城河内沿（地形用）
    public const float MoatOuter = WallR + 21f;        // 护城河外沿（地形用）

    public const float PlazaR     = 17f;               // 中央广场占位半径（含雕像 / 水池 / 花坛 / 旗杆）
    public const float StreetR    = 32f;               // 城内环路半径
    public const float StreetHalf = 2.4f;              // 环路半宽（地形铺石板用）

    public const float SouthGateDeg = 270f;            // 南门：接长桥，通往出生点
    public const float EastGateDeg  = 0f;              // 东门：接短桥，通往大教堂

    public const float SouthBridgeZ     = CenterZ - WallR - 42f;   // 112：南桥南端（大陆一侧，桥面接城门外道路）
    public const float SouthBridgeHalfW = 3.8f;

    public const float CathedralX = 276f;              // 大教堂岩石高台中心（城东，隔着护城河）
    public const float CathedralZ = 205f;
    public const float CathedralR = 50f;               // 台地平整半径（要够大，东桥才落在平顶上而不是斜坡上）
    public const float CathedralY = GroundY + 3f;      // 台地高度（比城内高 3 米）

    public const float EastBridgeFarX  = CathedralX - CathedralR + 4f;   // 230：东桥东端（落在台地平顶内 4 米）
    public const float EastBridgeHalfW = 3.2f;

    public const float DockDeg = 225f;                 // 码头方位（城岛西南水边）
    public const float DockLen = 20f;                  // 栈桥长度

    // 配色
    static readonly Color Stone     = new Color32(0xd8, 0xd0, 0xbc, 255);
    static readonly Color StoneDark = new Color32(0xb3, 0xa9, 0x95, 255);
    static readonly Color Roof      = new Color32(0xc2, 0x55, 0x3c, 255);
    static readonly Color RoofAlt   = new Color32(0x9c, 0x4a, 0x37, 255);
    static readonly Color Wood      = new Color32(0x8a, 0x62, 0x40, 255);
    static readonly Color WoodDark  = new Color32(0x6b, 0x4a, 0x2f, 255);
    static readonly Color Gold      = new Color32(0xe8, 0xd0, 0x8f, 255);
    static readonly Color Glass     = new Color32(0x6a, 0xc8, 0xea, 255);
    static readonly Color Window    = new Color32(0x50, 0x5e, 0x76, 255);
    static readonly Color Flag      = new Color32(0x5f, 0xa8, 0xd8, 255);
    static readonly Color Leaf      = new Color32(0x63, 0xbe, 0x52, 255);
    static readonly Color Blossom   = new Color32(0xf6, 0xa8, 0xc8, 255);

    // ================= 摆放结果（给地图用） =================

    /// <summary>地标类型：0 城门 1 桥 2 教堂 3 风车 4 广场 5 酒馆 6 铁匠铺 7 瞭望塔 8 码头</summary>
    public struct MapMarker
    {
        public string name;
        public float  x, z;
        public byte   kind;
    }

    /// <summary>大写地图 / 小地图显示的地标（Append 时自动填好）</summary>
    public static readonly List<MapMarker> Markers = new List<MapMarker>();

    /// <summary>地图上要画的线（桥 / 栈桥）：起点终点 + 类型（0 石桥 1 木栈桥）</summary>
    public struct MapLine
    {
        public float x0, z0, x1, z1;
        public byte  kind;
    }

    public static readonly List<MapLine> MapLines = new List<MapLine>();

    /// <summary>地图上的色块（屋顶 / 铺地 / 木构），让大地图能看出城的形状</summary>
    public struct MapBlob
    {
        public float x, z, r;
        public byte  color;      // 0 红顶 1 石板/浅色 2 木质
    }

    public static readonly List<MapBlob> Blobs = new List<MapBlob>();

    static void Blob(float x, float z, float r, byte color) =>
        Blobs.Add(new MapBlob { x = x, z = z, r = r, color = color });

    /// <summary>三个子网格的三角形：0 = 石墙（贴图），1 = 屋顶（贴图），2 = 其余（顶点色）</summary>
    public static readonly List<int>[] SubTris = { new List<int>(), new List<int>(), new List<int>() };

    // ================= 占位表 =================

    struct Spot
    {
        public float x, z, r;
    }

    static readonly List<Spot> _obstacles = new List<Spot>();   // 固定障碍（互不检测）
    static readonly List<Spot> _placed    = new List<Spot>();   // 摆放物（互相 + 和障碍都不许重叠）

    static void Obstacle(float x, float z, float r) => _obstacles.Add(new Spot { x = x, z = z, r = r });

    static bool ClearOf(List<Spot> list, float x, float z, float r)
    {
        for (int i = 0; i < list.Count; i++)
        {
            float dx = x - list[i].x, dz = z - list[i].z;
            float rr = r + list[i].r;
            if (dx * dx + dz * dz < rr * rr) return false;
        }
        return true;
    }

    /// <summary>这一点空不空：和所有障碍、所有已放置物都隔开才算空</summary>
    static bool IsFree(float x, float z, float r) => ClearOf(_obstacles, x, z, r) && ClearOf(_placed, x, z, r);

    /// <summary>试着占一个位置：空就占下来并返回 true，撞了就返回 false（调用方换位置）</summary>
    static bool TryReserve(float x, float z, float r)
    {
        if (!IsFree(x, z, r)) return false;
        _placed.Add(new Spot { x = x, z = z, r = r });
        return true;
    }

    // 自检计数
    static int _placedCount, _rejectedCount, _overlaps;
    static readonly List<string> _overlapReport = new List<string>();

    // ================= 方位工具 =================

    /// <summary>第 i 段城墙的起点方位角（弧度）。偏移半格，让第 17 段正对南门、第 23 段正对东门</summary>
    static float Angle(int i) => (i * 360f / WallSides + 180f / WallSides) * Mathf.Deg2Rad;

    static Quaternion Yaw(float degrees) => Quaternion.Euler(0f, degrees, 0f);

    /// <summary>环上一点（角度用弧度）</summary>
    static Vector3 OnRing(float angle, float radius) =>
        new Vector3(CenterX + Mathf.Cos(angle) * radius, GroundY, CenterZ + Mathf.Sin(angle) * radius);

    /// <summary>某个方位角对应的段号（0 ~ WallSides-1）。段 i 覆盖 [7.5+15i, 22.5+15i] 度，
    /// 所以南门（270°）落在第 17 段、东门（0°）落在第 23 段，门洞正好对着大道</summary>
    static int SegmentOf(float degrees) =>
        Mathf.FloorToInt(Mathf.Repeat(degrees - 180f / WallSides, 360f) / (360f / WallSides)) % WallSides;

    /// <summary>这个方位角上，房子该朝哪（背对城心朝外）</summary>
    static float OutwardYaw(float degrees) => 90f - degrees;

    /// <summary>按"半径 + 方位角"取一个位置</summary>
    static Vector3 SpotAt(float radius, float degrees)
    {
        float a = degrees * Mathf.Deg2Rad;
        return new Vector3(CenterX + Mathf.Cos(a) * radius, GroundY, CenterZ + Mathf.Sin(a) * radius);
    }

    /// <summary>城里是不是石板街：中央广场 + 一圈环路 + 南北 / 东西两条城门大道</summary>
    public static bool OnTownStreet(float x, float z)
    {
        float dx = x - CenterX, dz = z - CenterZ;
        float r = Mathf.Sqrt(dx * dx + dz * dz);

        if (r < PlazaR - 0.5f) return true;                                // 广场（含外圈水池）
        if (Mathf.Abs(r - StreetR) < StreetHalf) return true;              // 环路
        if (Mathf.Abs(dx) < 3.2f && dz < 0f && r < WallR) return true;     // 南门大道
        if (Mathf.Abs(dz) < 3.2f && dx > 0f && r < WallR) return true;     // 东门大道
        return false;
    }

    /// <summary>城外的入城道路（南桥南端往南一直到地图边）</summary>
    public static bool IsRoad(float x, float z) =>
        Mathf.Abs(x - CenterX) < 3.6f && z > 34f && z < SouthBridgeZ + 4f;

    // ================= 生成入口 =================

    /// <summary>生成整座城：顶点 / 颜色写进传入的列表，三角形按材质分在 SubTris 的三个子网格里</summary>
    public static void Append(List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        foreach (var list in SubTris) list.Clear();
        _obstacles.Clear();
        _placed.Clear();
        Markers.Clear();
        MapLines.Clear();
        Blobs.Clear();
        _placedCount = _rejectedCount = _overlaps = 0;
        _overlapReport.Clear();

        BuildObstacles();

        BuildWall(verts, cols, tris);
        BuildWallRamp(verts, cols, tris);
        BuildSouthBridge(verts, cols, tris);
        BuildEastBridge(verts, cols, tris);
        BuildDock(verts, cols, tris);
        BuildPlaza(verts, cols, tris);

        // 摆放顺序很重要：先把重要的大件（地标 / 拱门 / 风车）占好位置，
        // 再让民居去填空，最后才是路灯和树这些街边小件（它们被挤掉一两个不影响观感）
        BuildLandmarks(verts, cols, tris);
        BuildArches(verts, cols, tris);
        BuildWindmills(verts, cols, tris);
        BuildHouses(verts, cols, tris);
        BuildStreetProps(verts, cols, tris);
        BuildCathedral(verts, cols, tris);

        VerifyLayout();
    }

    /// <summary>先把固定障碍登记好：广场、环路、两条城门大道、上墙坡道、城墙。
    /// 后面所有摆放物都会绕开它们，所以不会有房子压街、风车插墙。</summary>
    static void BuildObstacles()
    {
        Obstacle(CenterX, CenterZ, PlazaR);

        // 环路：沿中线每 3 度一个小圆，连成一条环带
        for (int i = 0; i < 120; i++)
        {
            float a = i * 3f;
            Vector3 p = SpotAt(StreetR, a);
            Obstacle(p.x, p.z, StreetHalf);
        }

        // 两条城门大道：从广场外沿一直顶到城墙
        for (float r = PlazaR + 1f; r <= WallR; r += 3f)
        {
            Vector3 s = SpotAt(r, SouthGateDeg);
            Obstacle(s.x, s.z, 3.2f);
            Vector3 e = SpotAt(r, EastGateDeg);
            Obstacle(e.x, e.z, 3.2f);
        }

        // 上墙坡道（正北）：一条 8 米宽的走廊，别在下面长房子（坡道会从街道上方跨过去）
        for (float r = PlazaR; r <= WallR - 2f; r += 3f)
        {
            Vector3 p = SpotAt(r, 90f);
            Obstacle(p.x, p.z, 4.2f);
        }

        // 城墙本身
        for (int i = 0; i < WallSides; i++)
        {
            Vector3 p = OnRing(Angle(i), WallR);
            Obstacle(p.x, p.z, WallT * 0.5f + 0.6f);
        }
    }

    /// <summary>按颜色把三角形分到子网格：石墙 / 屋顶走贴图，其它保留顶点色</summary>
    static List<int> Route(Color color)
    {
        if (Same(color, Stone) || Same(color, StoneDark)) return SubTris[0];
        if (Same(color, Roof)  || Same(color, RoofAlt))   return SubTris[1];
        return SubTris[2];
    }

    static bool Same(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f;

    // ================= 城墙 / 塔楼 / 城门 =================

    static void BuildWall(List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        int southSeg = SegmentOf(SouthGateDeg);      // 17
        int eastSeg  = SegmentOf(EastGateDeg);       // 23

        var gateVerts = new HashSet<int>();          // 两个门洞两侧的塔楼加大
        gateVerts.Add(southSeg); gateVerts.Add((southSeg + 1) % WallSides);
        gateVerts.Add(eastSeg);  gateVerts.Add((eastSeg + 1) % WallSides);

        for (int i = 0; i < WallSides; i++)
        {
            if (i == southSeg || i == eastSeg) continue;      // 这一格是门洞，不砌墙

            float a0 = Angle(i), a1 = Angle(i + 1);
            Vector3 p0 = OnRing(a0, WallR), p1 = OnRing(a1, WallR);
            Vector3 c = (p0 + p1) * 0.5f;
            float len = Vector3.Distance(p0, p1);
            float yaw = -Mathf.Atan2(p1.z - p0.z, p1.x - p0.x) * Mathf.Rad2Deg;
            var rot = Yaw(yaw);

            // 墙身（稍微加长一点，接缝藏在塔楼里）
            AppendBox(verts, cols, tris, new Vector3(c.x, GroundY + WallH * 0.5f, c.z),
                      new Vector3(len * 1.14f, WallH, WallT), rot, Stone, StoneDark);

            Vector3 inward = new Vector3(CenterX - c.x, 0f, CenterZ - c.z).normalized;

            // 内侧墙顶走道（木板）+ 内侧矮护墙
            AppendBox(verts, cols, tris,
                      new Vector3(c.x + inward.x * WallT * 0.42f, WallTopY - 0.22f, c.z + inward.z * WallT * 0.42f),
                      new Vector3(len * 1.05f, 0.44f, WallT * 1.02f), rot, Wood, WoodDark);
            AppendBox(verts, cols, tris,
                      new Vector3(c.x + inward.x * WallT * 0.95f, WallTopY + 0.5f, c.z + inward.z * WallT * 0.95f),
                      new Vector3(len * 1.05f, 1.0f, 0.4f), rot, Stone, StoneDark);

            // 外侧垛口
            for (int m = -1; m <= 1; m++)
            {
                Vector3 top = c + (p1 - p0).normalized * (m * len * 0.3f) - inward * (WallT * 0.4f);
                AppendBox(verts, cols, tris, new Vector3(top.x, WallTopY + 0.55f, top.z),
                          new Vector3(1.7f, 1.0f, WallT * 0.5f), rot, StoneDark, StoneDark);
            }
        }

        // 每个墙角一座塔（门洞两侧的做成更大的门楼）
        for (int i = 0; i < WallSides; i++)
        {
            Vector3 p = OnRing(Angle(i), WallR);
            bool gate = gateVerts.Contains(i);
            float height = gate ? 13.5f : 15.5f;
            float radius = gate ? 4.2f : 3.4f;

            AppendCylinder(verts, cols, tris, new Vector3(p.x, GroundY, p.z), height, radius, radius * 0.92f, 8,
                           gate ? Stone : StoneDark);
            AppendCylinder(verts, cols, tris, new Vector3(p.x, GroundY + height, p.z),
                           gate ? 2.6f : 5.0f, radius * 1.32f, gate ? 0.5f : 0.12f, 8, RoofAlt);

            if (gate)
            {
                // 门楼：塔身开一排小窗
                for (int w = 0; w < 3; w++)
                {
                    float a = Angle(i) + (w - 1) * 0.30f;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    AppendBox(verts, cols, tris, new Vector3(p.x, GroundY + 9.4f, p.z) + d * 3.6f,
                              new Vector3(1.3f, 2.4f, 0.5f), Yaw(-a * Mathf.Rad2Deg + 90f), Window, Window);
                }
            }
        }

        // 两个门洞：门楣 + 上面的墙 + 两扇木门
        BuildGate(verts, cols, tris, southSeg, "南门");
        BuildGate(verts, cols, tris, eastSeg, "东门");
    }

    static void BuildGate(List<Vector3> verts, List<Color> cols, List<int> tris, int seg, string name)
    {
        Vector3 g0 = OnRing(Angle(seg), WallR), g1 = OnRing(Angle(seg + 1), WallR);
        Vector3 gm = (g0 + g1) * 0.5f;
        float span = Vector3.Distance(g0, g1);
        float yaw = -Mathf.Atan2(g1.z - g0.z, g1.x - g0.x) * Mathf.Rad2Deg;
        var rot = Yaw(yaw);

        // 门楣（下沿离地 5.5 米，玩家随便过）
        AppendBox(verts, cols, tris, new Vector3(gm.x, GroundY + 6.1f, gm.z),
                  new Vector3(span * 1.02f, 1.2f, WallT), rot, Stone, StoneDark);
        // 门楣以上的墙身
        AppendBox(verts, cols, tris, new Vector3(gm.x, GroundY + (6.7f + WallH) * 0.5f, gm.z),
                  new Vector3(span * 1.02f, WallH - 6.7f, WallT), rot, Stone, StoneDark);
        // 垛口
        for (int m = -1; m <= 1; m++)
        {
            Vector3 off = rot * new Vector3(m * span * 0.3f, 0f, -WallT * 0.4f);
            AppendBox(verts, cols, tris, new Vector3(gm.x + off.x, WallTopY + 0.55f, gm.z + off.z),
                      new Vector3(1.7f, 1.0f, WallT * 0.5f), rot, StoneDark, StoneDark);
        }

        // 门洞两侧各立一段石墩，做出门框的感觉 —— 中间必须留出通路，
        // 之前这里放的是两扇木门，正好把整个门洞堵死，玩家根本进不了城
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 side = (g1 - g0).normalized;
            Vector3 pos = gm + side * (s * span * 0.42f);
            AppendBox(verts, cols, tris, new Vector3(pos.x, GroundY + 2.2f, pos.z),
                      new Vector3(span * 0.14f, 4.4f, WallT * 0.9f), rot, StoneDark, StoneDark);
        }

        Markers.Add(new MapMarker { name = name, x = gm.x, z = gm.z, kind = 0 });
    }

    /// <summary>上墙坡道（正北）：从广场外沿斜着爬上墙顶走道，玩家能走上去绕城一圈</summary>
    static void BuildWallRamp(List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        float startR = PlazaR, endR = WallR - 2.5f;
        float rise = WallH - 0.5f;
        float run = endR - startR;
        float length = Mathf.Sqrt(run * run + rise * rise);
        float slope = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

        Vector3 dir = new Vector3(Mathf.Cos(90f * Mathf.Deg2Rad), 0f, Mathf.Sin(90f * Mathf.Deg2Rad));
        Vector3 mid = new Vector3(CenterX, GroundY + rise * 0.5f, CenterZ) + dir * ((startR + endR) * 0.5f);

        var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(-slope, 0f, 0f);
        var side = Quaternion.LookRotation(dir) * Vector3.right;

        AppendBox(verts, cols, tris, mid, new Vector3(4.0f, 0.6f, length), rot, Wood, WoodDark);

        for (int s = -1; s <= 1; s += 2)
            AppendBox(verts, cols, tris, mid + side * (s * 2.1f) + Vector3.up * 0.8f,
                      new Vector3(0.35f, 1.1f, length), rot, StoneDark, StoneDark);
    }

    // ================= 桥 =================

    /// <summary>南桥：从城门外一直到大陆（跨过护城河和城南河），桥上带拱洞与桥墩</summary>
    static void BuildSouthBridge(List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        float x = CenterX;
        float zGate = CenterZ - WallR;                 // 151
        float zFar  = SouthBridgeZ;                    // 112
        float w = SouthBridgeHalfW * 2f;
        float midZ = (zGate + zFar) * 0.5f;
        float len = zGate - zFar;

        // 桥面 + 两侧矮墙
        AppendBox(verts, cols, tris, new Vector3(x, GroundY - 0.3f, midZ),
                  new Vector3(w, 0.6f, len), Quaternion.identity, Stone, StoneDark);
        for (int s = -1; s <= 1; s += 2)
        {
            AppendBox(verts, cols, tris, new Vector3(x + s * (w * 0.5f - 0.25f), GroundY + 0.62f, midZ),
                      new Vector3(0.5f, 1.0f, len), Quaternion.identity, StoneDark, StoneDark);
            for (int k = 0; k < 9; k++)
            {
                float z = Mathf.Lerp(zGate - 1f, zFar + 1f, (k + 0.5f) / 9f);
                AppendBox(verts, cols, tris, new Vector3(x + s * (w * 0.5f - 0.25f), GroundY + 1.55f, z),
                          new Vector3(0.62f, 0.9f, 0.62f), Quaternion.identity, Stone, StoneDark);
            }
        }

        // 桥墩（插到水里）+ 墩间拱券
        for (int i = 0; i < 5; i++)
        {
            float z = Mathf.Lerp(zGate - 2f, zFar + 2f, (i + 0.5f) / 5f);
            AppendBox(verts, cols, tris, new Vector3(x, 3.4f, z),
                      new Vector3(2.2f, 9.2f, 2.2f), Quaternion.identity, StoneDark, StoneDark);
            if (i < 4)
            {
                float z2 = Mathf.Lerp(zGate - 2f, zFar + 2f, (i + 1.5f) / 5f);
                AppendBox(verts, cols, tris, new Vector3(x, GroundY - 1.3f, (z + z2) * 0.5f),
                          new Vector3(w * 0.86f, 2.4f, Mathf.Abs(z2 - z) * 0.42f), Quaternion.identity, Stone, StoneDark);
            }
        }

        MapLines.Add(new MapLine { x0 = x, z0 = zGate, x1 = x, z1 = zFar, kind = 0 });
        Markers.Add(new MapMarker { name = "南桥", x = x, z = midZ, kind = 1 });
    }

    /// <summary>东桥：从东门跨过护城河，接到大教堂高台（桥面从城内 8 米升到高台 11 米）。
    /// 东端刻意伸进台地平顶内 4 米，保证桥面和地面接平、不会插进岩石斜坡里</summary>
    static void BuildEastBridge(List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        float z = CenterZ;
        float xNear = CenterX + WallR;                 // 204
        float xFar  = EastBridgeFarX;                  // 230
        float w = EastBridgeHalfW * 2f;
        float midX = (xNear + xFar) * 0.5f;
        float len = xFar - xNear;
        float rise = CathedralY - GroundY;             // 3
        float angle = -Mathf.Atan2(rise, len) * Mathf.Rad2Deg;   // 往东逐渐抬高

        var rot = Quaternion.Euler(0f, 90f, angle);    // 局部 Z 朝东
        AppendBox(verts, cols, tris, new Vector3(midX, (GroundY + CathedralY) * 0.5f - 0.3f, z),
                  new Vector3(w, 0.6f, len + 3f), rot, Stone, StoneDark);

        for (int s = -1; s <= 1; s += 2)
            AppendBox(verts, cols, tris, new Vector3(midX, (GroundY + CathedralY) * 0.5f + 0.6f, z + s * (w * 0.5f - 0.2f)),
                      new Vector3(0.45f, 1.2f, len + 3f), rot, StoneDark, StoneDark);

        // 两个桥墩
        for (int i = 0; i < 2; i++)
        {
            float px = Mathf.Lerp(xNear + 4f, xFar - 6f, i);
            AppendBox(verts, cols, tris, new Vector3(px, 3.2f, z),
                      new Vector3(2.0f, 8.8f, 2.0f), Quaternion.identity, StoneDark, StoneDark);
        }

        MapLines.Add(new MapLine { x0 = xNear, z0 = z, x1 = xFar, z1 = z, kind = 0 });
        Markers.Add(new MapMarker { name = "教堂桥", x = midX, z = z, kind = 1 });
    }

    /// <summary>木栈桥码头（城岛西南水边）：一段斜坡 + 一条伸进水里的木栈道</summary>
    static void BuildDock(List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        float a = DockDeg * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        Vector3 start = new Vector3(CenterX, GroundY, CenterZ) + dir * IslandR;   // 从城岛岸边起坡
        float deckY = OpenWorld.WaterLevel + 2.4f;     // 栈桥面高度（比水面高一点）
        const float rampRun = 6f;

        // 岸到栈桥的斜坡
        Vector3 rampMid = start + dir * (rampRun * 0.5f);
        var rampRot = Quaternion.LookRotation(dir) *
                      Quaternion.Euler(Mathf.Atan2(GroundY - deckY, rampRun) * Mathf.Rad2Deg, 0f, 0f);
        float rampLen = Mathf.Sqrt(rampRun * rampRun + (GroundY - deckY) * (GroundY - deckY));
        AppendBox(verts, cols, tris, new Vector3(rampMid.x, (GroundY + deckY) * 0.5f, rampMid.z),
                  new Vector3(5f, 0.5f, rampLen + 0.6f), rampRot, Wood, WoodDark);

        // 栈道
        Vector3 mid = start + dir * (rampRun + DockLen * 0.5f);
        AppendBox(verts, cols, tris, new Vector3(mid.x, deckY, mid.z),
                  new Vector3(5.5f, 0.5f, DockLen), Yaw(OutwardYaw(DockDeg)), Wood, WoodDark);

        // 桥桩
        for (int i = 0; i < 5; i++)
        {
            float t = (i + 0.5f) / 5f;
            Vector3 p = start + dir * (rampRun + DockLen * t);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 off = new Vector3(-dir.z, 0f, dir.x) * (s * 2.4f);
                AppendBox(verts, cols, tris, new Vector3(p.x + off.x, 2.6f, p.z + off.z),
                          new Vector3(0.6f, 5.4f, 0.6f), Quaternion.identity, WoodDark, WoodDark);
            }
        }

        Vector3 end = start + dir * (rampRun + DockLen);
        MapLines.Add(new MapLine { x0 = start.x, z0 = start.z, x1 = end.x, z1 = end.z, kind = 1 });
        Markers.Add(new MapMarker { name = "码头", x = mid.x, z = mid.z, kind = 8 });
    }

    // ================= 中央广场 =================

    static void BuildPlaza(List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        Vector3 c = new Vector3(CenterX, GroundY, CenterZ);

        // 三级圆台阶
        for (int k = 0; k < 3; k++)
        {
            float r = 11f - k * 2f;
            AppendCylinder(verts, cols, tris, c + Vector3.up * (k * 0.3f), 0.3f, r, r * 0.97f, 16,
                           (k % 2 == 0) ? Stone : StoneDark);
        }

        // 水池：台阶外面一圈浅水
        AppendRing(verts, cols, tris, c + Vector3.up * 0.06f, 11.4f, 15.6f, 20, Glass);

        // 基座 + 柱身 + 风神立像（身体 + 头 + 双翼 + 金球）
        AppendCylinder(verts, cols, tris, c + Vector3.up * 0.9f, 1.0f, 3.0f, 2.4f, 12, StoneDark);
        AppendCylinder(verts, cols, tris, c + Vector3.up * 1.9f, 7.0f, 1.5f, 1.1f, 10, Stone);

        Vector3 top = c + Vector3.up * 8.9f;
        AppendCylinder(verts, cols, tris, top, 2.6f, 0.85f, 1.0f, 8, Stone);
        AppendSphere(verts, cols, tris, top + Vector3.up * 3.0f, 0.8f, Stone, 8);
        for (int w = -1; w <= 1; w += 2)
        {
            var wingRot = Quaternion.Euler(0f, 0f, w * 32f);
            AppendBox(verts, cols, tris, top + Vector3.up * 1.8f + wingRot * new Vector3(w * 2.1f, 0f, 0f),
                      new Vector3(4.4f, 0.4f, 0.9f), wingRot, Stone, StoneDark);
        }
        AppendSphere(verts, cols, tris, top + Vector3.up * 4.7f, 0.55f, Gold, 8);

        // 四个花坛 + 四面蓝白旗（摆在四个斜角，避开两条城门大道）
        for (int i = 0; i < 4; i++)
        {
            float deg = 45f + i * 90f;
            float a = deg * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

            Vector3 bed = c + dir * 13.6f;
            AppendCylinder(verts, cols, tris, bed, 0.5f, 1.5f, 1.4f, 8, StoneDark);
            AppendSphere(verts, cols, tris, bed + Vector3.up * 0.95f, 1.15f, Leaf, 7);
            for (int f = 0; f < 3; f++)
            {
                float fa = a + (f - 1) * 0.3f;
                Vector3 flower = c + new Vector3(Mathf.Cos(fa), 0f, Mathf.Sin(fa)) * 13.6f;
                AppendSphere(verts, cols, tris, flower + Vector3.up * 1.6f, 0.36f,
                             (f % 2 == 0) ? Blossom : Gold, 6);
            }

            Vector3 pole = c + dir * 16.2f;
            AppendCylinder(verts, cols, tris, pole, 8.4f, 0.24f, 0.2f, 6, Wood);
            AppendBox(verts, cols, tris, pole + Vector3.up * 7.1f,
                      new Vector3(0.14f, 2.3f, 3.1f), Yaw(OutwardYaw(deg) + 90f), Flag, Flag);
        }

        Markers.Add(new MapMarker { name = "中央广场", x = CenterX, z = CenterZ, kind = 4 });
        Blob(CenterX, CenterZ, PlazaR * 0.75f, 1);
    }

    // ================= 三个地标 =================

    static void BuildLandmarks(List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        // 三个地标都放在"外圈民居之外、城墙之内"那一圈空地上，角度彼此分开，
        // 也刻意避开 4 座风车的角度（110 / 180 / 240 / 300），保证大家都能放得下
        BuildTavern(verts, cols, tris, 44f, 32f);      // 酒馆：东北
        BuildSmithy(verts, cols, tris, 44f, 148f);     // 铁匠铺：西北
        BuildWatchtower(verts, cols, tris, 44f, 212f); // 方形瞭望塔：西南（参考图里城内那座方塔）
    }

    static void BuildTavern(List<Vector3> verts, List<Color> cols, List<int> tris, float radius, float deg)
    {
        Vector3 p = SpotAt(radius, deg);
        const float footprint = 7.0f;      // 9.0 × 9.8 的房子外接圆 ≈ 6.65，加一点余量
        if (!TryReserve(p.x, p.z, footprint)) { _rejectedCount++; return; }

        float yaw = OutwardYaw(deg);
        var rot = Yaw(yaw);
        float w = 9.0f, d = 9.8f, h1 = 5.0f, h2 = 3.6f;

        AppendBox(verts, cols, tris, p + Vector3.up * 0.7f, new Vector3(w * 1.06f, 1.4f, d * 1.06f), rot, StoneDark, StoneDark);
        AppendBox(verts, cols, tris, p + Vector3.up * (h1 * 0.5f + 1.4f), new Vector3(w, h1, d), rot, Stone, StoneDark);
        AppendBox(verts, cols, tris, p + Vector3.up * (h1 + h2 * 0.5f + 1.4f), new Vector3(w * 0.8f, h2, d * 0.82f), rot, Stone, StoneDark);
        AppendGable(verts, cols, tris, p + Vector3.up * (h1 + h2 + 1.4f), w * 0.95f, d * 0.98f, 3.2f, rot, Roof);

        // 挑出来的招牌
        AppendBox(verts, cols, tris, p + Vector3.up * (h1 + 3.1f) + rot * new Vector3(0f, 0f, -d * 0.62f),
                  new Vector3(0.35f, 0.35f, 3.0f), rot, Wood, WoodDark);
        AppendBox(verts, cols, tris, p + Vector3.up * (h1 + 2.2f) + rot * new Vector3(0f, 0f, -d * 0.62f - 1.3f),
                  new Vector3(2.8f, 1.5f, 0.24f), rot, Gold, Gold);

        // 门口两张桌子
        for (int t = -1; t <= 1; t += 2)
        {
            Vector3 tp = p + rot * new Vector3(t * 5.4f, 0f, -d * 0.85f);
            AppendBox(verts, cols, tris, tp + Vector3.up * 1.05f, new Vector3(2.2f, 0.28f, 2.2f), rot, Wood, WoodDark);
            for (int k = 0; k < 4; k++)
            {
                Vector3 off = rot * new Vector3((k % 2 == 0 ? -0.85f : 0.85f), 0f, (k < 2 ? -0.85f : 0.85f));
                AppendBox(verts, cols, tris, tp + off + Vector3.up * 0.52f,
                          new Vector3(0.26f, 1.05f, 0.26f), rot, WoodDark, WoodDark);
            }
        }

        Markers.Add(new MapMarker { name = "酒馆", x = p.x, z = p.z, kind = 5 });
        Blob(p.x, p.z, footprint * 0.8f, 0);
    }

    static void BuildSmithy(List<Vector3> verts, List<Color> cols, List<int> tris, float radius, float deg)
    {
        Vector3 p = SpotAt(radius, deg);
        if (!TryReserve(p.x, p.z, 6.4f)) { _rejectedCount++; return; }

        var rot = Yaw(OutwardYaw(deg));
        float w = 8.4f, d = 7.8f, h = 5.0f;

        AppendBox(verts, cols, tris, p + Vector3.up * 0.7f, new Vector3(w * 1.06f, 1.4f, d * 1.06f), rot, StoneDark, StoneDark);
        AppendBox(verts, cols, tris, p + Vector3.up * (h * 0.5f + 1.4f), new Vector3(w, h, d), rot, Stone, StoneDark);
        AppendGable(verts, cols, tris, p + Vector3.up * (h + 1.4f), w * 1.08f, d * 1.1f, 3.0f, rot, RoofAlt);

        // 石炉 + 炉口
        Vector3 f = p + rot * new Vector3(w * 0.78f, 0f, d * 0.46f);
        AppendBox(verts, cols, tris, f + Vector3.up * 1.8f, new Vector3(3.2f, 3.6f, 3.2f), rot, StoneDark, StoneDark);
        AppendBox(verts, cols, tris, f + Vector3.up * 1.7f + rot * new Vector3(0f, 0f, -1.7f),
                  new Vector3(1.7f, 1.3f, 0.35f), rot, Gold, Gold);

        // 烟囱
        AppendCylinder(verts, cols, tris, p + rot * new Vector3(w * 0.3f, 0f, d * 0.3f) + Vector3.up * (h + 1.4f),
                       7.0f, 1.2f, 0.9f, 8, StoneDark);

        Markers.Add(new MapMarker { name = "铁匠铺", x = p.x, z = p.z, kind = 6 });
        Blob(p.x, p.z, 6.0f, 0);
    }

    /// <summary>方形瞭望塔（参考图里城内那座方塔）：方塔身 + 出挑的平台 + 四坡顶</summary>
    static void BuildWatchtower(List<Vector3> verts, List<Color> cols, List<int> tris, float radius, float deg)
    {
        Vector3 p = SpotAt(radius, deg);
        if (!TryReserve(p.x, p.z, 5.2f)) { _rejectedCount++; return; }

        var rot = Yaw(OutwardYaw(deg));
        float side = 6.4f, h = 21f;

        AppendBox(verts, cols, tris, p + Vector3.up * (h * 0.5f), new Vector3(side, h, side), rot, Stone, StoneDark);
        // 塔身腰线
        AppendBox(verts, cols, tris, p + Vector3.up * (h * 0.55f), new Vector3(side * 1.1f, 0.6f, side * 1.1f), rot, StoneDark, StoneDark);

        // 顶部出挑平台 + 栏杆
        AppendBox(verts, cols, tris, p + Vector3.up * (h + 0.4f), new Vector3(side * 1.32f, 0.8f, side * 1.32f), rot, StoneDark, StoneDark);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            AppendBox(verts, cols, tris, p + Vector3.up * (h + 1.5f) + dir * (side * 0.62f),
                      new Vector3(side * 1.3f, 1.4f, 0.35f), Yaw(90f - a * Mathf.Rad2Deg), StoneDark, StoneDark);
        }

        // 四坡顶 + 金顶球
        AppendCylinder(verts, cols, tris, p + Vector3.up * (h + 0.8f), 4.0f, side * 0.92f, 0.3f, 4, RoofAlt);
        AppendSphere(verts, cols, tris, p + Vector3.up * (h + 5.0f), 0.5f, Gold, 6);

        Markers.Add(new MapMarker { name = "瞭望塔", x = p.x, z = p.z, kind = 7 });
        Blob(p.x, p.z, 5.0f, 1);
    }

    // ================= 风车 =================

    /// <summary>城内 4 座风车（参考图里最显眼的那几座）：塔身 + 四片叶轮，叶轮朝城心方向。
    /// 角度特意贴近两条城门大道和上墙坡道的"禁放区"——那里本来就不能盖房子，
    /// 风车站上去等于白捡 4 个位置，不会挤掉民居（叶轮抬得比屋顶高，也不会插进房顶）</summary>
    static void BuildWindmills(List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        float[] degs = { 12f, 103f, 258f, 282f };
        int placed = 0;

        for (int i = 0; i < degs.Length; i++)
        {
            float deg = degs[i];
            Vector3 p = SpotAt(44f, deg);
            if (!TryReserve(p.x, p.z, 5.5f)) { _rejectedCount++; continue; }

            float yaw = OutwardYaw(deg);                  // 朝外 → 叶轮那一面朝城心
            var rot = Yaw(yaw);
            float bodyH = 16f, r = 3.0f;

            AppendCylinder(verts, cols, tris, p, bodyH, r * 1.15f, r * 0.7f, 10, Stone);
            // 塔身上的木箍
            for (int k = 0; k < 3; k++)
                AppendCylinder(verts, cols, tris, p + Vector3.up * (3.5f + k * 4.6f), 0.5f, r * 0.95f, r * 0.95f, 10, WoodDark);
            // 锥顶
            AppendCylinder(verts, cols, tris, p + Vector3.up * bodyH, 2.6f, r * 0.85f, 0.2f, 10, RoofAlt);

            // 叶轮：轮毂往城心一侧伸出来，而且抬到 14.5 米高，叶片最低处也在屋顶之上
            Vector3 hub = p + Vector3.up * 14.5f - rot * new Vector3(0f, 0f, r * 0.95f);
            for (int b = 0; b < 4; b++)
            {
                var bladeRot = rot * Quaternion.Euler(0f, 0f, b * 45f);
                Vector3 dir = bladeRot * Vector3.up;
                AppendBox(verts, cols, tris, hub + dir * 3.0f, new Vector3(0.55f, 6.4f, 0.3f), bladeRot, Wood, WoodDark);
            }
            AppendSphere(verts, cols, tris, hub, 0.75f, StoneDark, 6);

            Markers.Add(new MapMarker { name = "风车" + (i + 1), x = p.x, z = p.z, kind = 3 });
            Blob(p.x, p.z, 3.4f, 2);
            placed++;
        }

        Debug.Log($"[MondstadtTown] 城内风车放置 {placed}/{degs.Length} 座。");
    }

    // ================= 入城大道上的石拱门 =================

    /// <summary>入城大道上的石拱门：两根柱子（站在街道两侧）+ 横梁 + 逐级内收的拱顶。
    /// 南北两条城门大道各一座（270° 南门 / 0° 东门），位置取在广场与环路之间那段</summary>
    static void BuildArches(List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        float[] avenues = { SouthGateDeg, EastGateDeg };

        foreach (float deg in avenues)
        {
            const float r = 24f, half = 7.0f, h = 7.2f;
            Vector3 c = SpotAt(r, deg);
            var rot = Yaw(OutwardYaw(deg));

            bool ok = true;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 p = c + rot * new Vector3(s * half, 0f, 0f);
                if (!TryReserve(p.x, p.z, 1.8f)) { ok = false; _rejectedCount++; }
            }
            if (!ok) continue;

            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 p = c + rot * new Vector3(s * half, 0f, 0f);
                AppendBox(verts, cols, tris, p + Vector3.up * (h * 0.5f), new Vector3(1.7f, h, 1.7f), rot, Stone, StoneDark);
                AppendBox(verts, cols, tris, p + Vector3.up * (h - 0.35f), new Vector3(2.3f, 0.7f, 2.3f), rot, StoneDark, StoneDark);
            }

            AppendBox(verts, cols, tris, c + Vector3.up * (h + 0.6f),
                      new Vector3(half * 2f + 2.8f, 1.2f, 1.9f), rot, Stone, StoneDark);
            for (int k = 0; k < 3; k++)
                AppendBox(verts, cols, tris, c + Vector3.up * (h + 1.35f + k * 0.6f),
                          new Vector3((half * 2f + 2.4f) * (1f - k * 0.28f), 0.6f, 1.5f), rot, StoneDark, StoneDark);
        }
    }

    // ================= 街道家具：路灯 + 行道树 =================

    static void BuildStreetProps(List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        // 环路外侧的路灯（站在环路外沿边上，角度和民居槽位错开，基本不占民居的位置）
        for (int i = 0; i < 12; i++)
        {
            float deg = i * 30f + 15f;
            Vector3 p = SpotAt(StreetR + 3.0f, deg);
            if (!TryReserve(p.x, p.z, 0.3f)) { _rejectedCount++; continue; }

            AppendCylinder(verts, cols, tris, p, 5.0f, 0.3f, 0.24f, 6, Wood);
            AppendSphere(verts, cols, tris, p + Vector3.up * 5.35f, 0.6f, Gold, 6);
        }

        // 城墙脚下的花坛：贴着墙根摆一圈小花，不占民居的位置也不会长到屋顶上去
        for (int i = 0; i < 8; i++)
        {
            float deg = i * 45f + 20f;
            Vector3 p = SpotAt(50f, deg);
            if (!TryReserve(p.x, p.z, 0.7f)) { _rejectedCount++; continue; }

            AppendCylinder(verts, cols, tris, p, 0.45f, 1.1f, 1.0f, 8, StoneDark);
            AppendSphere(verts, cols, tris, p + Vector3.up * 0.75f, 0.8f,
                         (i % 2 == 0) ? Leaf : Blossom, 7);
        }
    }

    // ================= 民居 =================

    /// <summary>两圈红顶民居：内圈贴着广场外侧、体量小一点（可用宽度只有 3 米多），
    /// 外圈贴着城墙内侧、体量大一些。每栋房子放之前先按"外接圆 + 余量"做间隙检测，
    /// 放不下就跳过 —— 所以绝不会和风车、地标、街道、别的房子打架。</summary>
    static void BuildHouses(List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        int placed = 0, tried = 0;

        // 半径 / 候选槽位数 / 起始角 / 抖动 / 面宽范围 / 进深范围
        // 半径按"屋顶外接圆 + 余量"和广场 / 环路 / 城墙的可用宽度算：
        //   内圈 23.3（小房子）：17(广场)+4.3 ≈ 21.3 到 32-2.4-4.3 ≈ 25.3 之间
        //   外圈 42.5：32+2.4+6.3 ≈ 40.7 到 54-2.1-6.3 ≈ 45.6 之间（外面那圈留给地标 / 风车 / 花坛）
        float[] rings  = { 23.3f, 42.5f };
        int[]   counts = { 16, 20 };
        float[] starts = { 6f, 2f };
        float[] jitter = { 0.5f, 1.0f };
        float[] minW   = { 4.2f, 5.4f };
        float[] maxW   = { 4.8f, 6.6f };
        float[] minD   = { 4.6f, 6.0f };
        float[] maxD   = { 5.2f, 7.4f };

        for (int ring = 0; ring < rings.Length; ring++)
        {
            for (int slot = 0; slot < counts[ring]; slot++)
            {
                float deg = starts[ring] + slot * (360f / counts[ring]);
                float seed = Hash01(slot + ring * 97, ring, 31);
                float r = rings[ring] + (seed - 0.5f) * 2f * jitter[ring];
                Vector3 p = SpotAt(r, deg);
                if (OnTownStreet(p.x, p.z)) { _rejectedCount++; continue; }

                // 房子尺度：面宽 / 进深 / 层数（种子一样，每次进游戏尺寸不变）
                float w = Mathf.Lerp(minW[ring], maxW[ring], seed);
                float d = Mathf.Lerp(minD[ring], maxD[ring], Hash01(slot + ring * 61, ring, 37));
                int   floors = (Hash01(slot * 3 + 1, ring, 41) > 0.72f) ? 2 : 1;
                float h = 4.2f + (floors - 1) * 2.8f;

                // 间隙用"屋顶"的外接圆算：屋顶比墙体还宽（出檐 18%~20%），
                // 只按墙体算的话两栋相邻房子的屋檐会互相插进去
                float roofW = w * 1.18f, roofD = d * 1.2f;
                float clear = 0.5f * Mathf.Sqrt(roofW * roofW + roofD * roofD) + 0.35f;
                tried++;
                if (!TryReserve(p.x, p.z, clear)) { _rejectedCount++; continue; }

                float yaw = OutwardYaw(deg) + ((slot % 2 == 0) ? 0f : 90f);   // 一半顺着街、一半横着
                BuildHouse(verts, cols, tris, p, yaw, w, d, h, seed);
                Blob(p.x, p.z, clear, 0);                                     // 地图上画成红顶
                placed++;
            }
        }

        Debug.Log($"[MondstadtTown] 民居放置 {placed}/{tried} 栋（其余位置被广场 / 环路 / 风车 / 地标占用而跳过）。");
    }

    /// <summary>一栋民居：石基座 + 墙体 + 双坡红顶（带出檐）+ 烟囱 + 两扇窗</summary>
    static void BuildHouse(List<Vector3> verts, List<Color> cols, List<int> tris,
                           Vector3 p, float yaw, float w, float d, float h, float seed)
    {
        var rot = Yaw(yaw);
        Color roof = (seed > 0.5f) ? Roof : RoofAlt;

        AppendBox(verts, cols, tris, p + Vector3.up * 0.65f, new Vector3(w * 1.08f, 1.3f, d * 1.08f), rot, StoneDark, StoneDark);
        AppendBox(verts, cols, tris, p + Vector3.up * (h * 0.5f + 1.3f), new Vector3(w, h, d), rot, Stone, StoneDark);
        AppendGable(verts, cols, tris, p + Vector3.up * (h + 1.3f), w * 1.18f, d * 1.2f, h * 0.62f + 1.4f, rot, roof);

        // 烟囱
        AppendBox(verts, cols, tris, p + Vector3.up * (h + h * 0.62f + 1.3f) + rot * new Vector3(w * 0.28f, 0f, d * 0.28f),
                  new Vector3(0.95f, 2.2f, 0.95f), rot, StoneDark, StoneDark);

        // 朝街那面的两扇窗
        for (int k = -1; k <= 1; k += 2)
        {
            Vector3 off = rot * new Vector3(k * w * 0.26f, 0f, -d * 0.5f);
            AppendBox(verts, cols, tris, p + Vector3.up * (h * 0.55f + 1.3f) + off,
                      new Vector3(1.3f, 1.6f, 0.35f), rot, Window, Window);
            if (h > 6f)
                AppendBox(verts, cols, tris, p + Vector3.up * (h + 0.6f) + off,
                          new Vector3(1.3f, 1.6f, 0.35f), rot, Window, Window);
        }
    }

    // ================= 大教堂（城东高台上） =================

    static void BuildCathedral(List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        float cx = CathedralX, cz = CathedralZ, y0 = CathedralY;

        // 高台上的神职人员聚落：石阶 + 广场铺地
        AppendRing(verts, cols, tris, new Vector3(cx, y0 + 0.05f, cz), 0f, CathedralR - 4f, 28, StoneDark);
        // 朝向城心（西）的台阶
        for (int k = 0; k < 4; k++)
            AppendBox(verts, cols, tris, new Vector3(cx - CathedralR + 2f + k * 1.6f, y0 + 0.25f + k * 0.25f, cz),
                      new Vector3(1.8f, 0.5f, 16f), Quaternion.identity, Stone, StoneDark);

        // 主殿：面朝西，十字平面
        float w = 20f, d = 30f, h = 17f;
        var rot = Quaternion.identity;
        AppendBox(verts, cols, tris, new Vector3(cx + 2f, y0 + h * 0.5f, cz), new Vector3(w, h, d), rot, Stone, StoneDark);
        AppendBox(verts, cols, tris, new Vector3(cx + 2f, y0 + h * 0.42f, cz + d * 0.16f),
                  new Vector3(w * 1.42f, h * 0.84f, d * 0.34f), rot, Stone, StoneDark);
        AppendGable(verts, cols, tris, new Vector3(cx + 2f, y0 + h, cz), w * 1.04f, d * 1.05f, 8.0f, rot, Roof);
        AppendGable(verts, cols, tris, new Vector3(cx + 2f, y0 + h * 0.84f, cz + d * 0.16f), w * 1.45f, d * 0.4f, 6.0f, rot, RoofAlt);

        // 正立面（朝西）：玫瑰窗 + 两侧尖拱窗 + 大门
        float front = cx + 2f - w * 0.5f;
        AppendCylinder(verts, cols, tris, new Vector3(front - 0.25f, y0 + 11.6f, cz), 0.5f, 3.0f, 3.0f, 16, Glass);
        for (int s = -1; s <= 1; s += 2)
        {
            AppendBox(verts, cols, tris, new Vector3(front - 0.25f, y0 + 10.4f, cz + s * 6.2f),
                      new Vector3(0.6f, 7.0f, 2.4f), rot, Window, Window);
            AppendCylinder(verts, cols, tris, new Vector3(front - 0.25f, y0 + 13.9f, cz + s * 6.2f),
                           0.6f, 1.2f, 1.2f, 10, Window);
        }
        AppendBox(verts, cols, tris, new Vector3(front - 1.2f, y0 + 3.6f, cz), new Vector3(1.6f, 7.2f, 5.0f), rot, Wood, WoodDark);

        // 两侧飞扶壁
        for (int s = -1; s <= 1; s += 2)
        {
            for (int k = -1; k <= 1; k++)
            {
                float z = cz + k * (d * 0.3f);
                AppendBox(verts, cols, tris, new Vector3(cx + 2f + s * (w * 0.5f + 3.4f), y0 + 7.4f, z),
                          new Vector3(2.0f, 14.8f, 2.2f), rot, StoneDark, StoneDark);
                AppendBox(verts, cols, tris, new Vector3(cx + 2f + s * (w * 0.5f + 1.7f), y0 + 13.2f, z),
                          new Vector3(3.4f, 1.2f, 2.0f), rot, Stone, StoneDark);
                AppendBox(verts, cols, tris, new Vector3(cx + 2f + s * (w * 0.5f + 0.4f), y0 + 9.0f, z),
                          new Vector3(0.6f, 6.0f, 2.0f), rot, Window, Window);
            }
        }

        // 钟塔：立在十字交叉处，塔身 + 百叶窗 + 环廊 + 收分的尖顶（参考图里最高的那座）
        Vector3 tower = new Vector3(cx + 2f, y0, cz + d * 0.16f);
        AppendCylinder(verts, cols, tris, tower, 32f, 6.2f, 4.0f, 10, Stone);
        for (int s = 0; s < 4; s++)
        {
            float a = s * Mathf.PI * 0.5f + 0.4f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            for (int k = 0; k < 2; k++)
                AppendBox(verts, cols, tris, tower + Vector3.up * (22f + k * 5.5f) + dir * 3.1f,
                          new Vector3(2.0f, 3.4f, 0.6f), Yaw(-a * Mathf.Rad2Deg + 90f), Window, Window);
        }
        AppendCylinder(verts, cols, tris, tower + Vector3.up * 32f, 0.8f, 4.2f, 3.9f, 10, StoneDark);
        for (int s = 0; s < 4; s++)
        {
            float a = s * Mathf.PI * 0.5f;
            AppendBox(verts, cols, tris, tower + Vector3.up * 33.4f + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 4.4f,
                      new Vector3(1.4f, 1.2f, 1.4f), Quaternion.identity, StoneDark, StoneDark);
        }
        AppendCylinder(verts, cols, tris, tower + Vector3.up * 34.2f, 4.4f, 3.9f, 2.2f, 10, Stone);
        AppendCylinder(verts, cols, tris, tower + Vector3.up * 38.6f, 7.5f, 2.2f, 0.15f, 10, Gold);
        AppendSphere(verts, cols, tris, tower + Vector3.up * 46.4f, 0.9f, Gold, 8);

        // 高台边上的几棵树，别让石头台面太空
        for (int i = 0; i < 6; i++)
        {
            float deg = 30f + i * 52f;
            float a = deg * Mathf.Deg2Rad;
            Vector3 p = new Vector3(cx + Mathf.Cos(a) * (CathedralR - 9f), y0, cz + Mathf.Sin(a) * (CathedralR - 9f));
            AppendCylinder(verts, cols, tris, p, 4.6f, 0.6f, 0.4f, 6, WoodDark);
            AppendSphere(verts, cols, tris, p + Vector3.up * 5.6f, 2.2f, (i % 2 == 0) ? Leaf : Blossom, 8);
        }

        Markers.Add(new MapMarker { name = "大教堂", x = cx, z = cz, kind = 2 });
        Blob(cx + 2f, cz, 17f, 1);
    }

    // ================= 自检 =================

    /// <summary>布局自检：任何两个"摆放物"之间、摆放物和障碍之间都不许重叠。
    /// 结果打进日志，Overlap 必须是 0，不是 0 就说明有零件错位了。</summary>
    static void VerifyLayout()
    {
        for (int i = 0; i < _placed.Count; i++)
        {
            for (int j = i + 1; j < _placed.Count; j++)
            {
                float dx = _placed[i].x - _placed[j].x, dz = _placed[i].z - _placed[j].z;
                float rr = _placed[i].r + _placed[j].r;
                if (dx * dx + dz * dz < rr * rr * 0.998f)
                {
                    _overlaps++;
                    if (_overlapReport.Count < 8)
                        _overlapReport.Add($"({_placed[i].x:0.#},{_placed[i].z:0.#})r{_placed[i].r:0.#} ↔ " +
                                           $"({_placed[j].x:0.#},{_placed[j].z:0.#})r{_placed[j].r:0.#}");
                }
            }
        }

        _placedCount = _placed.Count;

        string tail = _overlaps == 0
            ? "Overlap = 0，布局正常"
            : "Overlap = " + _overlaps + "（" + string.Join("；", _overlapReport) + "）";

        Debug.Log($"[MondstadtTown] 布局自检：摆放 {_placedCount} 件，因冲突跳过 {_rejectedCount} 次，障碍 {_obstacles.Count} 个；{tail}");
    }

    /// <summary>0~1 确定性哈希（每个位置一样，重进游戏不会变）</summary>
    static float Hash01(int a, int b, int salt)
    {
        unchecked
        {
            int n = a * 73856093 ^ b * 19349663 ^ salt * 83492791;
            n = (n ^ (n >> 13)) * 1274126177;
            uint u = (uint)(n ^ (n >> 16));
            return u / 4294967295f;
        }
    }

    // ================= 低模零件（都支持完整旋转） =================

    static void AppendBox(List<Vector3> verts, List<Color> cols, List<int> tris,
                          Vector3 center, Vector3 size, Quaternion rot, Color top, Color side)
    {
        int baseIndex = verts.Count;
        Vector3 h = size * 0.5f;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? -h.x : h.x,
                                         (i & 2) == 0 ? -h.y : h.y,
                                         (i & 4) == 0 ? -h.z : h.z);
            verts.Add(center + rot * corner);
            cols.Add(((i & 2) == 0 ? side : top).linear);
        }

        int[] faces =
        {
            0, 2, 3, 0, 3, 1,
            4, 5, 7, 4, 7, 6,
            0, 1, 5, 0, 5, 4,
            2, 6, 7, 2, 7, 3,
            1, 3, 7, 1, 7, 5,
            0, 4, 6, 0, 6, 2,
        };
        var outTris = Route(top);          // 一整个盒子归一个子网格（按顶面颜色判断）
        for (int i = 0; i < faces.Length; i++) outTris.Add(baseIndex + faces[i]);
    }

    static void AppendGable(List<Vector3> verts, List<Color> cols, List<int> tris,
                            Vector3 baseCenter, float width, float depth, float height, Quaternion rot, Color color)
    {
        int baseIndex = verts.Count;
        float hw = width * 0.5f, hd = depth * 0.5f;
        Color c = color.linear;

        Vector3[] v =
        {
            new Vector3(-hw, 0f, -hd), new Vector3( hw, 0f, -hd), new Vector3( hw, 0f,  hd), new Vector3(-hw, 0f,  hd),
            new Vector3(  0f, height, -hd), new Vector3(  0f, height,  hd),
        };
        foreach (var p in v) { verts.Add(baseCenter + rot * p); cols.Add(c); }

        int[] faces = { 0, 1, 4, 2, 3, 5, 0, 4, 5, 0, 5, 3, 1, 2, 5, 1, 5, 4 };
        var outTris = Route(color);
        for (int i = 0; i < faces.Length; i++) outTris.Add(baseIndex + faces[i]);
    }

    static void AppendCylinder(List<Vector3> verts, List<Color> cols, List<int> tris,
                               Vector3 basePos, float height, float bottomR, float topR, int sides, Color color)
    {
        int baseIndex = verts.Count;
        Color c = color.linear;
        var outTris = Route(color);

        for (int i = 0; i <= sides; i++)
        {
            float a = (float)i / sides * Mathf.PI * 2f;
            verts.Add(basePos + new Vector3(Mathf.Cos(a) * bottomR, 0f, Mathf.Sin(a) * bottomR));
            cols.Add(c);
        }
        for (int i = 0; i <= sides; i++)
        {
            float a = (float)i / sides * Mathf.PI * 2f;
            verts.Add(basePos + new Vector3(Mathf.Cos(a) * topR, height, Mathf.Sin(a) * topR));
            cols.Add(c);
        }

        for (int i = 0; i < sides; i++)
        {
            int lo0 = baseIndex + i, lo1 = lo0 + 1;
            int hi0 = baseIndex + sides + 1 + i, hi1 = hi0 + 1;
            outTris.Add(lo0); outTris.Add(hi0); outTris.Add(lo1);
            outTris.Add(lo1); outTris.Add(hi0); outTris.Add(hi1);
        }
    }

    static void AppendSphere(List<Vector3> verts, List<Color> cols, List<int> tris,
                             Vector3 center, float radius, Color color, int segments)
    {
        int rings = Mathf.Max(3, segments / 2);
        int baseIndex = verts.Count;
        Color c = color.linear;
        var outTris = Route(color);

        for (int r = 0; r <= rings; r++)
        {
            float phi = (float)r / rings * Mathf.PI;
            float y = Mathf.Cos(phi) * radius;
            float ring = Mathf.Sin(phi) * radius;

            for (int s = 0; s <= segments; s++)
            {
                float theta = (float)s / segments * Mathf.PI * 2f;
                verts.Add(center + new Vector3(Mathf.Sin(theta) * ring, y, Mathf.Cos(theta) * ring));
                cols.Add(c);
            }
        }

        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < segments; s++)
            {
                int i0 = baseIndex + r * (segments + 1) + s;
                int i1 = i0 + 1;
                int i2 = i0 + segments + 1;
                int i3 = i2 + 1;
                outTris.Add(i0); outTris.Add(i2); outTris.Add(i1);
                outTris.Add(i1); outTris.Add(i2); outTris.Add(i3);
            }
        }
    }

    /// <summary>水平圆环（广场水池、教堂铺地都靠它；innerR = 0 就是一个圆盘）</summary>
    static void AppendRing(List<Vector3> verts, List<Color> cols, List<int> tris,
                           Vector3 center, float innerR, float outerR, int sides, Color color)
    {
        int baseIndex = verts.Count;
        Color c = color.linear;
        var outTris = Route(color);

        for (int i = 0; i <= sides; i++)
        {
            float a = (float)i / sides * Mathf.PI * 2f;
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            verts.Add(center + new Vector3(ca * innerR, 0f, sa * innerR));
            cols.Add(c);
            verts.Add(center + new Vector3(ca * outerR, 0f, sa * outerR));
            cols.Add(c);
        }

        for (int i = 0; i < sides; i++)
        {
            int i0 = baseIndex + i * 2;
            int i1 = i0 + 1;
            int i2 = i0 + 2;
            int i3 = i0 + 3;
            outTris.Add(i0); outTris.Add(i2); outTris.Add(i1);
            outTris.Add(i1); outTris.Add(i2); outTris.Add(i3);
        }
    }
}
