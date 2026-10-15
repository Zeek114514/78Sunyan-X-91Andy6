using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 开放世界（二次元草原风）：一张平滑起伏的地形网格 + 湖水 + 合并成一个网格的树和石头。
/// 地图中央偏北是湖心岛上的石城（MondstadtTown）：城岛被护城河围住，南面一座长石桥通大陆，
/// 东面一座短石桥通城墙外的大教堂岩石高台 —— 这几处地形都是 Height() 里算出来的，
/// 所以城、桥、高台和地形严丝合缝，不存在悬空或陷地。
/// 顶点色按高度和坡度分区（草地 / 岩石 / 沙滩 / 石板），并烘焙了朝向上的明暗，
/// 贴图只提供很轻的颗粒感，整体是平涂的动画风；树石和地形共用同一套材质，雾效一致。
/// 全部是确定性生成（固定哈希），每次进游戏地图一样。
/// </summary>
public class OpenWorld : MonoBehaviour
{
    // ---------- 世界尺寸 ----------

    public const float Size = 1800f;       // 地图边长（米）= 1800 / 1080 格 ≈ 1.667 米。
                                           // 从 850 → 1392（璃月）→ 1800（稻妻）：
                                           // 稻妻是岛国，得先有海 —— 旧大陆（原来那 1392 见方）之外
                                           // 整片沉成外海，群岛摆在海面上（见 InazumaIsles）
    const int   Res  = 1080;               // 每边的格子数。★ 格子从 1.5 收到 1.667 米是**故意的**：
                                           //   面积涨到 1.67 倍，格子不放大一点，地形顶点会从 86 万涨到 144 万
    public const float Cell = Size / Res;  // ≈ 1.667 米

    /// <summary>地形（和树石）按 4×4 = 16 块各出一张网格。
    /// 1392 米做成一张单网格是 86 万顶点 / 172 万面 —— 既没有视锥剔除，MeshCollider 也要烘很久。
    /// 分块之后通常只画得到 4~6 块，碰撞体也是按块建的</summary>
    public const int TerrainChunks = 4;

    /// <summary>每块的格数（一块 = 232 格 = 348 米）</summary>
    public const int ChunkCells = Res / TerrainChunks;

    /// <summary>水面高度：比它低的地方就是湖 / 河</summary>
    public const float WaterLevel = 3.2f;

    /// <summary>雪线：比它高的地方开始积雪（上色、怪物分布、严寒值都用它）</summary>
    public const float SnowLine = 62f;

    // ---------- 「Andy的雪顶」（压在两国的分界上） ----------
    // 主峰 + 两座肩峰的一座孤立雪山；再叠上山脊噪声（锯齿感）、两条 U 形冰川谷、
    // 谷口一道冰碛垄、鞍部一个冰蚀盆地（冰湖）。
    // ★ 位置压在蒙德和璃月的分界线上（原神里雪山也正好在两国之间）。
    //   从原来的 (440,442) **整体平移** 到 (720,620) —— 山体内部各特征的相对位置一个字没改，
    //   所以形状和以前完全一样，只是搬了个地方。
    const float MassifPeakX = 720f, MassifPeakZ = 620f;    // 主峰中心
    const float FrozenLakeX = 754f, FrozenLakeZ = 648f;    // 冰湖（冻住的湖面，平的、能走）

    /// <summary>雪顶主峰的中心（XZ）。给"贴着雪山摆东西"的生成器用 —— 是 HeightRaw 里的常量，
    /// 不依赖运行时状态，所以 [BeforeSceneLoad] 的选点阶段也问得到</summary>
    public static Vector2 MassifPeakPos => new Vector2(MassifPeakX, MassifPeakZ);

    /// <summary>雪顶前哨营地的位置（算法见 FindCampSpot：按高度找雪线下的缓坡）。
    /// 也是纯地形计算，选点阶段问得到</summary>
    public static Vector2 SnowCampPos { get { var c = FindCampSpot(); return new Vector2(c.x, c.z); } }

    // ---------- 高度场缓存 ----------
    // 地形高度是十几个三角函数算出来的，到处直接调用太贵；启动时先烘一张高度场，
    // 之后地形网格 / 上色 / 种树 / 怪物选点 / AI 查询全部走它（双线性取样），比现算还快一点。
    static float[] _heightField;
    static int     _fieldRes;

    // ---------- 野外建筑地台 ----------
    // 野外建筑（村落 / 设施 / 遗迹）不能歪在坡上：选址先定下来，把要盖房子的地方登记成"地台"，
    // HeightRaw 末尾让地形平滑地让到地台高度，然后才烘高度场 —— 和城岛台地、大教堂高台是同一个套路。
    // 登记必须发生在 BuildHeightField() 之前，所以选点走 [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]
    // （见 WildSiteSpawner.PlanSites / PuzzleSpawner.PlanSites），而不是等 OpenWorld.Awake。
    public struct Pad
    {
        public float x, z, r, y;
        public float f;            // 过渡带宽度（米）：从平整半径 r 往外 f 米内平滑回自然地形
    }

    static readonly List<Pad> _pads = new List<Pad>();

    /// <summary>忽略地台、只按自然地形算高度。给编辑器自检 / 诊断用：
    /// 只有关掉地台，才量得出"这块地台到底削掉 / 填了多少土"（= 台坎有多高）。
    /// 运行时永远是 false，不要拿它做游戏逻辑。</summary>
    public static bool IgnorePads;

    /// <summary>登记一块地台：中心 (x,z)、平整半径 r、目标高度 y、往外 falloff 米平滑过渡。
    /// 过渡带要按地台大小给：大台子给窄过渡带，边缘就是一道陡坎（会看出人工平台）</summary>
    public static void AddPad(float x, float z, float r, float y, float falloff) =>
        _pads.Add(new Pad { x = x, z = z, r = r, y = y, f = Mathf.Max(4f, falloff) });

    /// <summary>清空地台（重新规划野外建筑之前调用）</summary>
    public static void ClearPads() => _pads.Clear();

    /// <summary>丢掉已经烘好的高度场。
    /// 选点必须按解析公式算：上一局烘出来的高度场里已经含了上一局的地台，拿它选点结果就不可复现了。</summary>
    public static void DiscardHeightField() => _heightField = null;

    // ---------- 禁植区 ----------
    // 野外建筑 / 解密场地在规划阶段（BeforeSceneLoad，早于 BuildProps）把占地登记进来，
    // 种树摆石头时会照着避让 —— 否则树会从房子里、石台上长出来。

    struct KeepOut
    {
        public float x, z, r;
    }

    static readonly List<KeepOut> _keepOut = new List<KeepOut>();

    /// <summary>登记一块"不许长树石"的地（野外建筑 / 解密场地用）</summary>
    public static void AddKeepOut(float x, float z, float r) =>
        _keepOut.Add(new KeepOut { x = x, z = z, r = r });

    /// <summary>清空禁植区（重新规划野外内容之前调用）</summary>
    public static void ClearKeepOut() => _keepOut.Clear();

    static bool InKeepOut(float x, float z)
    {
        for (int i = 0; i < _keepOut.Count; i++)
        {
            float dx = x - _keepOut[i].x, dz = z - _keepOut[i].z;
            float r = _keepOut[i].r;
            if (dx * dx + dz * dz < r * r) return true;
        }
        return false;
    }

    /// <summary>这一点是不是落在某个野外建筑 / 解密场地的占地里（和种树摆石头用的是同一张表）。
    /// 传送锚点这类"不能杵进房子和石台里"的东西要问它</summary>
    public static bool InSiteKeepOut(float x, float z) => InKeepOut(x, z);

    /// <summary>这一点是不是落在地台的边坡过渡带上（平整区之外、自然地形之内）。
    /// 过渡带是斜的：宝箱 / 树石摆在这儿会一半埋进土里、一半悬空。
    /// 放静态物件之前都要问一下 —— 平整区里（d ≤ r）和过渡带之外（d ≥ r + f）都可以放</summary>
    public static bool OnPadSlope(float x, float z)
    {
        for (int i = 0; i < _pads.Count; i++)
        {
            var p = _pads[i];
            float dx = x - p.x, dz = z - p.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d > p.r && d < p.r + p.f) return true;
        }
        return false;
    }

    /// <summary>这一点周围再放一块 (r, falloff) 的地台，会不会和**已经登记过的**地台互相拉歪。
    /// 判定和 WildSiteSpawner.PadGap 是同一条：两边平整半径之和 + 较大的那个过渡带。
    ///
    /// ★ 直接查已登记的 _pads，所以不管前面是哪个生成器登记的都能挡住 ——
    ///   分属不同生成器的两块地台互相重叠，一样会把跨在两块上的建筑拉歪。</summary>
    public static bool PadConflict(float x, float z, float r, float falloff, float margin)
    {
        for (int i = 0; i < _pads.Count; i++)
        {
            var p = _pads[i];
            float need = r + p.r + Mathf.Max(falloff, p.f) + margin;
            float dx = x - p.x, dz = z - p.z;
            if (dx * dx + dz * dz < need * need) return true;
        }
        return false;
    }

    /// <summary>城南那条河的中心线（东西走向，蛇形）</summary>
    public static float RiverZ(float x) => 132f + 9f * Mathf.Sin(x * 0.022f);

    /// <summary>河的一半宽度</summary>
    public const float RiverHalfWidth = 12f;

    /// <summary>出生点：桥南的路边，面朝城门</summary>
    public Vector3 SpawnPoint
    {
        get
        {
            const float x = MondstadtTown.CenterX, z = 100f;
            return new Vector3(x, SurfaceY(x, z) + 2.5f, z);
        }
    }

    Material _material;                 // 树石 / 城的顶点色材质（Voxel/Demo）
    Material _terrainMaterial;          // 地形专用：顶点色 × 草/岩/沙三张贴图（Voxel/TerrainBlend）
    Material _stoneMaterial;            // 城的墙体：顶点色 × 石墙贴图（Voxel/TexUnlit）
    Material _roofMaterial;             // 城的屋顶：顶点色 × 瓦片贴图（Voxel/TexUnlit）
    Material _waterMaterial;
    readonly List<Vector3> _verts = new List<Vector3>();
    readonly List<Color>   _cols  = new List<Color>();
    readonly List<int>     _tris  = new List<int>();

    /// <summary>顶点色材质（掉落物之类要跟着用同一套）</summary>
    public Material Material => _material;

    /// <summary>石墙材质（墙 / 石柱用；野外建筑和城共用，保证材质语言一致）</summary>
    public Material StoneMaterial => _stoneMaterial;

    /// <summary>瓦片材质（屋顶用；野外建筑和城共用）</summary>
    public Material RoofMaterial => _roofMaterial;

    // ---------- 地形高度 ----------

    /// <summary>地表高度（米）：优先查启动时烘好的高度场（双线性取样），还没烘就用解析公式现算</summary>
    public static float Height(float x, float z)
    {
        if (_heightField == null) return HeightRaw(x, z);

        float fx = Mathf.Clamp(x / Cell, 0f, _fieldRes - 1.001f);
        float fz = Mathf.Clamp(z / Cell, 0f, _fieldRes - 1.001f);

        int x0 = (int)fx, z0 = (int)fz;
        int x1 = Mathf.Min(x0 + 1, _fieldRes - 1);
        int z1 = Mathf.Min(z0 + 1, _fieldRes - 1);
        float tx = fx - x0, tz = fz - z0;

        int n = _fieldRes;
        float h00 = _heightField[z0 * n + x0], h10 = _heightField[z0 * n + x1];
        float h01 = _heightField[z1 * n + x0], h11 = _heightField[z1 * n + x1];

        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }

    /// <summary>把整张地图的高度烘进高度场（生成地形网格之前调用一次）</summary>
    void BuildHeightField()
    {
        _fieldRes = Res + 1;
        _heightField = new float[_fieldRes * _fieldRes];

        for (int iz = 0; iz < _fieldRes; iz++)
        {
            float z = iz * Cell;
            for (int ix = 0; ix < _fieldRes; ix++)
                _heightField[iz * _fieldRes + ix] = HeightRaw(ix * Cell, z);
        }
    }

    /// <summary>
    /// 基底地形：多倍频 Perlin + 域扭曲，两个国度各一套振幅。
    ///
    /// ★ 为什么换掉原来的"三组正弦叠加"：
    ///   正弦叠加的周期是死的（2π/0.034 ≈ 185 米、2π/0.071 ≈ 88 米），整张图就是个规则蛋托；
    ///   再叠上"水面固定 3.2 米、基底最低只有 0.8 米"这一条，**每个波谷都会自动变成一个水塘** ——
    ///   世界地图上就是一片按网格重复的"水塘 + 沙滩"图案（截图里一眼能看出来）。
    ///
    /// 现在改成：
    ///   · 三层 Perlin（波长约 620 / 150 / 45 米）叠大中小起伏，频率互相不成整数倍，没有可见周期；
    ///   · 采样坐标先用两层低频噪声**扭曲**一下（domain warp），把 Perlin 自己的方向性格子感也打散；
    ///   · 两个国度共用同一套噪声（地形"同源"，过渡带不打架），只是振幅不同：
    ///     蒙德是缓和草原丘陵（10±3.7 米），璃月起伏更大、更硬（14±7.6 米）；
    ///   · **两边的下限都抬到水面之上**（≥6.3 米），所以除了刻意挖出来的盆地
    ///     （湖 / 河 / 港湾 / 冰湖 / 内湾外湾）以外，地图上不会再有莫名其妙的水塘。
    /// </summary>
    static float BaseTerrain(float x, float z)
    {
        // 域扭曲：把采样点揉一下。两层不同相位的低频噪声分别推 x 和 z
        float wx = x + (Mathf.PerlinNoise(x * 0.0031f, z * 0.0031f) - 0.5f) * 170f;
        float wz = z + (Mathf.PerlinNoise(x * 0.0031f + 37.4f, z * 0.0031f + 11.9f) - 0.5f) * 170f;

        float big  = Mathf.PerlinNoise(wx * 0.0016f, wz * 0.0016f);        // 波长约 620 米：大缓丘
        float mid  = Mathf.PerlinNoise(wx * 0.0067f, wz * 0.0067f);        // 波长约 150 米：坡地
        float fine = Mathf.PerlinNoise(wx * 0.0220f, wz * 0.0220f);        // 波长约 45 米：碎起伏

        // 蒙德：缓和、圆润（10 ± 3.7 米）
        float mond = 10.0f
                   + (big  - 0.5f) * 4.0f
                   + (mid  - 0.5f) * 2.4f
                   + (fine - 0.5f) * 1.0f;

        // 璃月：同一套噪声，振幅更大、更硬朗（14 ± 7.6 米）——峰林 / 梯田再叠在上面
        float liyue = 14.0f
                    + (big  - 0.5f) * 10.0f
                    + (mid  - 0.5f) * 4.0f
                    + (fine - 0.5f) * 1.2f;

        return Mathf.Lerp(mond, liyue, LiyueWeight(x, z));
    }

    /// <summary>地表高度（解析公式）：丘陵、河流、道路、城岛、护城河、龙脊雪山全在这算</summary>
    public static float HeightRaw(float x, float z)
    {
        float h = BaseTerrain(x, z);

        // 东北方向的山麓：从城东北一路缓缓抬向龙脊雪山（起点 380 米，免得城东地面被抬太高把东桥埋掉）
        float ridge = Mathf.Clamp01((x + z - 380f) / 260f);

        // ★ 地图从 600 扩到 850 之后，x+z 会把东南半张地图都推到 ridge=1，
        //   整片被抬成一块 46 米的高地。所以再乘一个"离山体越远越弱"的权重：
        //   山麓只出现在龙脊雪山周围，远处的东南还是草原丘陵。
        //   权重半径从 (300,320) 收到 (150,190)：雪山是"一座山"，不是"半张地图的山麓"。
        float fmx = x - MassifPeakX, fmz = z - MassifPeakZ;
        float massifNear = Mathf.Clamp01(1f - (Mathf.Sqrt(fmx * fmx + fmz * fmz) - 150f) / 190f);

        h += ridge * ridge * 46f * massifNear;

        // 西边一个湖盆
        float lx = x - 85f, lz = z - 118f;
        h -= 12f * Mathf.Clamp01(1f - Mathf.Sqrt(lx * lx + lz * lz) / 56f);

        // 城南一条河：**先压出一条河谷，再在谷底挖出水面**。
        // ★ 基底抬到 10~14 米之后，直接往 0.8 米挖会变成一条 9 米深的峡谷 ——
        //   所以先让河两侧 60 米内平滑落到 5.5 米的河谷底，再在河心挖到水下 1.6 米
        float riverD = Mathf.Abs(z - RiverZ(x));
        float riverValley = Mathf.Clamp01(1f - riverD / 60f);
        if (riverValley > 0f)
            h = Mathf.Lerp(h, 5.5f, Mathf.SmoothStep(0f, 1f, riverValley));

        if (riverD < RiverHalfWidth)
        {
            float carve = Mathf.Clamp01(1f - riverD / RiverHalfWidth);
            h = Mathf.Lerp(h, 1.6f, carve * carve);
        }
        else if (MondstadtTown.IsRoad(x, z))
        {
            // 城南那条路：从旷野缓缓升到桥面高度，正好和南桥南端接平（河道那段不填，留给桥）
            float t = Mathf.Clamp01((z - 70f) / (MondstadtTown.SouthBridgeZ - 70f));
            h = Mathf.Lerp(h, MondstadtTown.GroundY, t);
        }

        float dx = x - MondstadtTown.CenterX, dz = z - MondstadtTown.CenterZ;
        float md = Mathf.Sqrt(dx * dx + dz * dz);

        // 城岛台地：城内整平，边缘平滑落向水面
        float town = Mathf.Clamp01(1f - (md - MondstadtTown.IslandR) / 10f);
        h = Mathf.Lerp(h, MondstadtTown.GroundY, town);

        // 大教堂岩石高台（城东，比城内高 3 米）—— 先垫起来，护城河最后再挖
        float cdx = x - MondstadtTown.CathedralX, cdz = z - MondstadtTown.CathedralZ;
        float cat = Mathf.Clamp01(1f - (Mathf.Sqrt(cdx * cdx + cdz * cdz) - MondstadtTown.CathedralR) / 12f);
        h = Mathf.Lerp(h, MondstadtTown.CathedralY, cat);

        // 环城护城河：城岛外面挖一圈水，让城变成湖心岛（最后执行，所以它能压过前面的抬升）。
        // 河面要够宽、内岸要够贴近城墙，否则城岛外那圈地面会被自然起伏顶到 8 米以上、把两岸的桥埋掉
        float wIn  = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MondstadtTown.MoatInner - 3f, MondstadtTown.MoatInner + 1f, md));
        float wOut = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MondstadtTown.MoatOuter + 3f, MondstadtTown.MoatOuter - 1f, md));
        float moat = Mathf.Min(wIn, wOut);
        if (moat > 0f) h = Mathf.Lerp(h, 0.5f, moat);

        // ---------- 龙脊雪山 ----------
        // 一座**收在一处的**山体：主峰 + 两座肩峰，山体铺开两百多米。
        //
        // ★ 上一版是"七座峰叠成一条三百多米长的山脊"，地图从 600 扩到 850 之后，
        //   雪山加上它的山麓（ridge 那一段）占了整张图将近四分之一，比例明显过大。
        //   现在收成一座孤立的雪山：只保留主峰和两座肩峰，山脚半径也从 145 收到 105，
        //   远处的 ridge 山麓权重（见上面的 massifNear）同时收紧到山体周围 300 米内 ——
        //   翻过山脚就是草原，不再是一路缓坡抬到天边。
        // ★ 从"三个独立圆锥"改成**一条连绵山脊**：主脊是一条两段的折线，两座肩峰改成
        //   从主脊伸出去的短支脊。取 max 而不是相加 —— 相加会在脊线交叉处叠成尖塔。
        float massif = 0f;
        massif = Mathf.Max(massif, Ridge(x, z, new Vector2(628f, 522f), new Vector2(722f, 622f), 152f, 176f));
        massif = Mathf.Max(massif, Ridge(x, z, new Vector2(722f, 622f), new Vector2(812f, 712f), 152f, 166f));
        massif = Mathf.Max(massif, Ridge(x, z, new Vector2(652f, 662f), new Vector2(702f, 634f),  92f, 112f));
        massif = Mathf.Max(massif, Ridge(x, z, new Vector2(744f, 600f), new Vector2(790f, 556f),  88f, 118f));

        if (massif > 0f)
        {
            // 山脊噪声：低频大起伏 + 高频碎石感，按山体高度收敛（山脚几乎是平的，不破坏草原）
            float mask = Mathf.Clamp01(massif / 70f);
            float a = (Mathf.PerlinNoise(x * 0.045f, z * 0.045f) - 0.5f) * 2f;
            float b = (Mathf.PerlinNoise(x * 0.115f + 11f, z * 0.115f + 7f) - 0.5f) * 2f;
            h += massif + mask * (a * 16f + b * 5f);
        }

        // 冰川谷：从主峰往西南 / 往南各切一条 U 形谷（谷底平缓、谷壁陡，谷里可以走人）
        h -= Valley(x, z, MassifPeakX, MassifPeakZ, 620f, 540f, 26f, 18f);
        h -= Valley(x, z, MassifPeakX, MassifPeakZ, 656f, 664f, 22f, 14f);

        // 冰碛垄：谷口堆一道矮垄（冰川堆积地貌）
        h += Mound(x, z, 660f, 530f, 38f, 7f);

        // 冰蚀盆地：鞍部上一个浅碗，冻住以后就是冰湖
        float fdx = x - FrozenLakeX, fdz = z - FrozenLakeZ;
        float lakeBowl = Mathf.Clamp01(1f - Mathf.Sqrt(fdx * fdx + fdz * fdz) / 30f);
        h -= lakeBowl * lakeBowl * 13f;

        // 南桥桥面下那一小段地：压到桥面以下，免得地面从桥面里冒出来（只在桥正下方那一条，看不见）
        if (Mathf.Abs(x - MondstadtTown.CenterX) < 5.5f &&
            z > MondstadtTown.SouthBridgeZ + 3f && z < MondstadtTown.SouthBridgeZ + 16f)
            h = Mathf.Min(h, 7.4f);

        // ---------- 璃月：喀斯特峰林 / 梯田 / 港湾 ----------
        // ★ 整段用 lw（璃月权重）缩放：蒙德侧 lw = 0，这一个 if 直接跳过，
        //   所以"蒙德那边的高度和加璃月之前一模一样"是天然成立的（自检里也验这一条）
        float lw = LiyueWeight(x, z);
        if (lw > 0.001f)
        {
            // 峰林：细而陡的一丛岩峰
            var peaks = KarstPeaks();
            for (int i = 0; i < peaks.Length; i++)
                h += Karst(x, z, peaks[i].x, peaks[i].z, peaks[i].r, peaks[i].h) * lw;

            // 梯田：把高度量化成一级级台阶 —— 等高线会自动变成田埂（真实的梯田就是这么长的）
            float terrace = TerraceMask(x, z) * lw;
            if (terrace > 0.001f)
            {
                float step = Mathf.Floor(h / TerraceStep) * TerraceStep;
                h = Mathf.Lerp(h, step, terrace);
            }

            // 港湾：码头台地 + 内湾 + 外湾（见 LiyueHarbor）
            h = LiyueHarbor.ShapeTerrain(h, x, z, lw);
        }

        // ---------- 稻妻：外海 + 群岛（见 InazumaIsles） ----------
        // ★ 放在最后、且**不吃 lw**：稻妻整片都在旧大陆之外的海上，
        //   那边 LiyueWeight 已经是 0（见 LiyueWeight 里的稻妻判据），所以这里独立成一段
        h = InazumaIsles.ShapeTerrain(h, x, z);

        // ---------- 野外建筑地台 ----------
        // 每个村落 / 遗迹 / 设施脚下垫一块平地（选点在 BeforeSceneLoad 阶段就算好了，见 _pads 的注释）。
        // ★ 一定要放在**最后**（护城河 / 雪山 / 璃月 / 稻妻全都之后）：地台是"人工整过的地面"，
        //   该压过前面所有的自然起伏。原先这段排在雪山之前，稻妻的岛形一叠上来就把岛上的地台
        //   整个盖掉了 —— 将军府那几层台地全长在坡上，传送锚点自然"不在地面上"。
        for (int i = 0; !IgnorePads && i < _pads.Count; i++)
        {
            var pad = _pads[i];
            float pdx = x - pad.x, pdz = z - pad.z;
            float pt = Mathf.Clamp01(1f - (Mathf.Sqrt(pdx * pdx + pdz * pdz) - pad.r) / pad.f);
            if (pt > 0f) h = Mathf.Lerp(h, pad.y, pt);
        }

        return Mathf.Max(h, -1f);
    }

    // ---------- 龙脊雪山的几件地形工具 ----------

    /// <summary>一座山峰：平滑圆锥（d ≥ r 时返回 0）；指数 1.35 让山顶略尖、山腰最陡</summary>
    static float Peak(float x, float z, float cx, float cz, float r, float height)
    {
        float dx = x - cx, dz = z - cz;
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        if (d >= r) return 0f;
        return height * Mathf.Pow(1f - d / r, 1.35f);
    }

    /// <summary>连绵山脊：到线段 ab 的距离决定高度，脊线最高、两侧按 1.5 次幂落下。
    /// 几段接起来（取 max）就是一条折来折去的山脊 —— 比"几个圆锥叠在一起"更像真山</summary>
    static float Ridge(float x, float z, Vector2 a, Vector2 b, float halfWidth, float height)
    {
        float dx = b.x - a.x, dz = b.y - a.y;
        float len2 = dx * dx + dz * dz;
        float t = len2 < 0.001f ? 0f : Mathf.Clamp01(((x - a.x) * dx + (z - a.y) * dz) / len2);

        float px = a.x + dx * t, pz = a.y + dz * t;
        float d = Mathf.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
        if (d >= halfWidth) return 0f;

        return height * Mathf.Pow(1f - d / halfWidth, 1.5f);
    }

    /// <summary>U 形冰川谷：沿 (ax,az) → (bx,bz) 这条线切下去，谷底平、谷壁陡</summary>
    static float Valley(float x, float z, float ax, float az, float bx, float bz, float halfWidth, float depth)
    {
        float dx = bx - ax, dz = bz - az;
        float len2 = dx * dx + dz * dz;
        if (len2 < 0.001f) return 0f;

        float t = Mathf.Clamp01(((x - ax) * dx + (z - az) * dz) / len2);
        float px = ax + dx * t, pz = az + dz * t;
        float d = Mathf.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
        if (d >= halfWidth) return 0f;

        float u = 1f - d / halfWidth;
        return depth * u * u * (3f - 2f * u);
    }

    /// <summary>一个小土丘（冰碛垄之类）</summary>
    static float Mound(float x, float z, float cx, float cz, float r, float height)
    {
        float dx = x - cx, dz = z - cz;
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        if (d >= r) return 0f;
        float u = 1f - d / r;
        return height * u * u * (3f - 2f * u);
    }

    // ---------- 国度分区（蒙德 ⇄ 璃月） ----------
    //
    // 一条缓慢起伏的分界线（不是正南正北的直线，那样太人工），两侧各 ~110 米平滑过渡。
    // 地形风格按权重混；**建筑只认"这是哪国"**（不混搭），分界带留作"两边都不建"的缓冲，
    // 免得出现德式房子挨着中式房子。

    /// <summary>璃月权重：0 = 蒙德，1 = 璃月</summary>
    public static float LiyueWeight(float x, float z)
    {
        // ★ 稻妻自成一个国度，不算璃月 —— 否则璃月的建筑 / 峰林 / 梯田会一路铺到岛上。
        //   InazumaIsles.RegionWeight 是纯解析的（不吃 Perlin），先按它划走，省掉每次采样的噪声开销
        float iw = InazumaIsles.RegionWeight(x, z);
        if (iw > 0.999f) return 0f;

        // 分界线随 z 缓慢起伏（两条不同频率的正弦叠一下，避免看出周期）
        float edge = 750f + 70f * Mathf.Sin(z * 0.0042f) + 38f * Mathf.Sin(z * 0.0113f + 2.1f);
        float lw = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge - 110f, edge + 110f, x));

        return lw * (1f - iw);
    }

    /// <summary>这一点算不算璃月的地（建筑用它决定风格；分界带里返回 false，两边都不建）</summary>
    public static bool IsLiyueLand(float x, float z) => LiyueWeight(x, z) > 0.65f;

    /// <summary>这一点算不算蒙德的地（分界带里返回 false，稻妻那边也返回 false）</summary>
    public static bool IsMondstadtLand(float x, float z) =>
        LiyueWeight(x, z) < 0.35f && InazumaIsles.RegionWeight(x, z) < 0.35f;

    /// <summary>这一点算不算稻妻的地（岛上内陆；建筑选点用）</summary>
    public static bool IsInazumaLand(float x, float z) => InazumaIsles.LandWeight(x, z) > 0.6f;

    // ---------- 璃月地形 ----------

    /// <summary>梯田区：圆心 / 半径。这一带的地形按高度量化成一级级台阶，上色换成水田绿。
    /// ★ 半径从 190 收到 120：190 米半径（380 米直径）整片量化成 2.6 米台阶，
    ///   从空中看就是一大块规则的人工阶梯，也是"地形重复"的一大来源。收成谷地尺度更像真梯田</summary>
    const float TerraceX = 1020f, TerraceZ = 470f, TerraceR = 120f;
    const float TerraceStep = 2.6f;      // 一级台阶多高（米）

    /// <summary>喀斯特峰林的分布范围（璃月中部偏北，避开港城和梯田）。
    /// 峰簇 / 石柱走廊都撒在这个框里，别的系统（选点时的"靠近峰林"判定）也照它算</summary>
    const float KarstX0 = 950f, KarstX1 = 1350f, KarstZ0 = 560f, KarstZ1 = 880f;

    /// <summary>这一点在不在峰林那一片（区域名 / 大地图分色用）。峰体是散点算出来的，
    /// 这里给的是它们所在的外框 —— 报区域名、标地图用不着精确到单座峰</summary>
    public static bool InKarstRegion(float x, float z) =>
        x > KarstX0 && x < KarstX1 && z > KarstZ0 && z < KarstZ1;

    struct KarstPeak { public float x, z, r, h; }
    static KarstPeak[] _karst;

    /// <summary>
    /// 峰林的位置：**成簇**（每簇沿一条短脊线排 5~7 座）+ 两条"石柱走廊"。
    ///
    /// ★ 原来是"22 座均匀撒在矩形里"—— 从地图 / 空中看就是一片打地鼠式的同形圆锥，太假。
    ///   真峰林是沿着断裂带 / 河谷**成串**长出来的，所以这里按簇 + 走廊排，簇心之间也拉开距离。
    /// 固定哈希，第一次用时生成并缓存（HeightRaw 烘地图时要调 90 万次，不能现撒随机数）
    /// </summary>
    static KarstPeak[] KarstPeaks()
    {
        if (_karst != null) return _karst;

        var list = new List<KarstPeak>();

        // ① 五个峰簇：每簇沿自己的一条短脊线排 5~7 座，两侧带抖动（成串但不呆板）
        Vector2[] clusters =
        {
            new Vector2(0.09f, 0.28f), new Vector2(0.42f, 0.46f), new Vector2(0.74f, 0.26f),
            new Vector2(0.25f, 0.75f), new Vector2(0.62f, 0.84f),
        };

        for (int c = 0; c < clusters.Length; c++)
        {
            float cx = KarstX0 + (KarstX1 - KarstX0) * clusters[c].x;
            float cz = KarstZ0 + (KarstZ1 - KarstZ0) * clusters[c].y;

            float dir = LowPoly.Hash01(c, 21, 3011) * 360f;                      // 这一簇的走向
            float len = 90f + LowPoly.Hash01(c, 22, 3012) * 110f;                // 这一簇摊开的长度
            float dx = Mathf.Cos(dir * Mathf.Deg2Rad), dz = Mathf.Sin(dir * Mathf.Deg2Rad);
            int n = 5 + (int)(LowPoly.Hash01(c, 23, 3013) * 3f);

            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0.5f : i / (float)(n - 1);
                float side = (LowPoly.Hash01(c * 31 + i, 24, 3014) - 0.5f) * 48f;   // 侧向抖动

                list.Add(new KarstPeak
                {
                    x = cx + dx * ((t - 0.5f) * len) - dz * side,
                    z = cz + dz * ((t - 0.5f) * len) + dx * side,
                    r = 13f + LowPoly.Hash01(c * 17 + i, 25, 3015) * 15f,           // 细：13~28 米
                    h = 32f + LowPoly.Hash01(c * 19 + i, 26, 3016) * 40f,           // 高：32~72 米
                });
            }
        }

        // ② 两条石柱走廊：比峰簇更细、更高、更密，看着就是"石林"
        Vector2[] corridors = { new Vector2(0.30f, 0.16f), new Vector2(0.55f, 0.90f) };
        for (int c = 0; c < corridors.Length; c++)
        {
            float cx = KarstX0 + (KarstX1 - KarstX0) * corridors[c].x;
            float cz = KarstZ0 + (KarstZ1 - KarstZ0) * corridors[c].y;

            float dir = (c == 0 ? 24f : 156f) * Mathf.Deg2Rad;
            float dx = Mathf.Cos(dir), dz = Mathf.Sin(dir);

            for (int i = 0; i < 7; i++)
            {
                float t = (i - 3f) * 26f;
                float side = (LowPoly.Hash01(c * 29 + i, 27, 3017) - 0.5f) * 18f;

                list.Add(new KarstPeak
                {
                    x = cx + dx * t - dz * side,
                    z = cz + dz * t + dx * side,
                    r = 9f + LowPoly.Hash01(c * 23 + i, 28, 3018) * 7f,             // 更细：9~16 米
                    h = 40f + LowPoly.Hash01(c * 31 + i, 29, 3019) * 34f,           // 更高：40~74 米
                });
            }
        }

        _karst = list.ToArray();
        return _karst;
    }

    /// <summary>一座喀斯特岩峰：细而陡。指数 0.45 → 峰壁近乎直立、峰顶收得很快，
    /// 这是石灰岩峰林的样子，和雪山那种宽缓圆锥完全不是一个形状</summary>
    static float Karst(float x, float z, float cx, float cz, float r, float height)
    {
        float dx = x - cx, dz = z - cz;
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        if (d >= r) return 0f;
        return height * Mathf.Pow(1f - d / r, 0.45f);
    }

    /// <summary>梯田区权重（0~1）：中心一块完全量化，往外平滑收掉</summary>
    static float TerraceMask(float x, float z)
    {
        float dx = x - TerraceX, dz = z - TerraceZ;
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        float t = Mathf.Clamp01(1f - (d - TerraceR * 0.5f) / (TerraceR * 0.5f));
        return t * t * (3f - 2f * t);
    }

    /// <summary>这一点算不算梯田（上色用）：和地形量化用的是同一个 mask</summary>
    public static float TerraceWeight(float x, float z) => TerraceMask(x, z) * LiyueWeight(x, z);

    /// <summary>雪山强度 0~1：地形上色、怪物分布、严寒值都看它（0 = 平原草原）</summary>
    public static float SnowFactor(float x, float z)
    {
        float h = Height(x, z);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SnowLine - 18f, SnowLine + 16f, h));
    }

    /// <summary>冰湖（冻住的湖面）：平的，能走上去</summary>
    public static bool IsFrozenLake(float x, float z)
    {
        float dx = x - FrozenLakeX, dz = z - FrozenLakeZ;
        return dx * dx + dz * dz < 26f * 26f && Height(x, z) > SnowLine - 34f;
    }

    /// <summary>城南那条从大陆通到南桥的石板路（不种树）</summary>
    public static bool IsRoad(float x, float z) => MondstadtTown.IsRoad(x, z);

    /// <summary>城里是不是走在石板街上（地形按这个铺石板色）</summary>
    public static bool OnTownStreet(float x, float z) => MondstadtTown.OnTownStreet(x, z);

    /// <summary>成林的密度（0~1）：树和地图上画森林都用它</summary>
    public static float ForestDensity(float x, float z) => Mathf.PerlinNoise(x * 0.018f, z * 0.018f);

    /// <summary>这一点离木栈桥码头那条线有多近（近到 margin 以内就不种树，免得树从栈道上长出来）</summary>
    static bool NearDock(float x, float z, float margin)
    {
        float a = MondstadtTown.DockDeg * Mathf.Deg2Rad;
        var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        var from = new Vector2(MondstadtTown.CenterX, MondstadtTown.CenterZ);
        var p = new Vector2(x, z) - from;

        float along = Vector2.Dot(p, dir);
        if (along < MondstadtTown.IslandR - 6f) return false;                        // 还没到岸边
        if (along > MondstadtTown.IslandR + MondstadtTown.DockLen + 10f) return false;

        return (p - dir * along).magnitude < margin;
    }

    /// <summary>按世界坐标取地面高度（NPC / 掉落物落地用）</summary>
    public float SurfaceY(float x, float z) => Height(Mathf.Clamp(x, 0f, Size), Mathf.Clamp(z, 0f, Size));

    /// <summary>这一点算不算"水里"（NPC 判断能不能走过去）</summary>
    public static bool IsDeepWater(float x, float z) => Height(x, z) < WaterLevel - 0.35f;

    /// <summary>是否在可活动范围内（留一圈边界，别走出去）</summary>
    public static bool InBounds(float x, float z) => x > 2f && z > 2f && x < Size - 2f && z < Size - 2f;

    /// <summary>这一点的坡度（0 平地 ~ 1 垂直），NPC 用来避开峭壁</summary>
    public static float Slope(float x, float z)
    {
        const float d = 1.2f;
        Vector3 n = new Vector3(Height(x - d, z) - Height(x + d, z), 2f * d, Height(x, z - d) - Height(x, z + d)).normalized;
        return 1f - n.y;
    }

    // ---------- 生命周期 ----------

    void Awake()
    {
        var shader = Resources.Load<Shader>("Shaders/VoxelDemo");
        if (shader == null) shader = Shader.Find("Voxel/Demo");
        _material = new Material(shader) { name = "OpenWorldMaterial" };

        // 地形单独一套材质：顶点色 × 三张 AI 生成的无缝贴图（草地 / 岩石 / 沙滩）
        var terrainShader = Resources.Load<Shader>("Shaders/TerrainBlend");
        if (terrainShader == null) terrainShader = Shader.Find("Voxel/TerrainBlend");
        _terrainMaterial = new Material(terrainShader) { name = "OpenWorldTerrainMaterial" };
        _terrainMaterial.SetTexture("_GrassMap", Resources.Load<Texture2D>("MatGrass"));
        _terrainMaterial.SetTexture("_RockMap",  Resources.Load<Texture2D>("MatStoneWall"));
        _terrainMaterial.SetTexture("_SandMap",  Resources.Load<Texture2D>("MatSand"));

        // 二次元风的贴图权重：贴图只留很轻的颗粒感（0.10），画面基本是顶点色的平涂色块
        _terrainMaterial.SetFloat("_TexScale", 0.35f);
        _terrainMaterial.SetFloat("_TexGain",  1.12f);
        _terrainMaterial.SetFloat("_Detail",   0.10f);

        // 城的墙体 / 屋顶：顶点色 × 单张贴图（石墙、瓦片）
        var texShader = Resources.Load<Shader>("Shaders/TexUnlit");
        if (texShader == null) texShader = Shader.Find("Voxel/TexUnlit");

        _stoneMaterial = new Material(texShader) { name = "TownStoneMaterial" };
        _stoneMaterial.SetTexture("_MainMap", Resources.Load<Texture2D>("MatStoneWall"));

        _roofMaterial = new Material(texShader) { name = "TownRoofMaterial" };
        _roofMaterial.SetTexture("_MainMap", Resources.Load<Texture2D>("MatRoofTile"));

        var waterShader = Resources.Load<Shader>("Shaders/VoxelWater");
        if (waterShader == null) waterShader = Shader.Find("Voxel/Water");
        _waterMaterial = new Material(waterShader) { name = "OpenWorldWaterMaterial" };

        BuildHeightField();      // 先把高度场烘好：后面的地形网格 / 上色 / 种树 / 雪顶营地全都查它，快很多
        BuildTerrain();

        // 璃月港的占地也要禁植：选点阶段（BeforeSceneLoad）还看不到港口，
        // 所以在烘场景这一步补登记一条，免得城里长出一片竹子
        AddKeepOut(LiyueHarbor.HarborX, LiyueHarbor.HarborZ, LiyueHarbor.QuayR + 12f);

        BuildProps();
        BuildSnowLandmarks();
        BuildTown();
        BuildWater();
        SetupPostProcessing();
        TryApplySkybox();
    }

    public void SetFog(Color color, float start, float end)
    {
        if (_material != null)
        {
            _material.SetColor("_FogColor", color);
            _material.SetFloat("_FogStart", start);
            _material.SetFloat("_FogEnd", end);
        }
        if (_terrainMaterial != null)
        {
            _terrainMaterial.SetColor("_FogColor", color);
            _terrainMaterial.SetFloat("_FogStart", start);
            _terrainMaterial.SetFloat("_FogEnd", end);
        }
        if (_stoneMaterial != null)
        {
            _stoneMaterial.SetColor("_FogColor", color);
            _stoneMaterial.SetFloat("_FogStart", start);
            _stoneMaterial.SetFloat("_FogEnd", end);
        }
        if (_roofMaterial != null)
        {
            _roofMaterial.SetColor("_FogColor", color);
            _roofMaterial.SetFloat("_FogStart", start);
            _roofMaterial.SetFloat("_FogEnd", end);
        }
        if (_waterMaterial != null)
        {
            _waterMaterial.SetColor("_FogColor", color);
            _waterMaterial.SetFloat("_FogStart", start);
            _waterMaterial.SetFloat("_FogEnd", end);
        }
    }

    // ---------- 地形网格（按块） ----------

    /// <summary>
    /// 地形：4×4 = 16 块，每块一张网格 + 一个 MeshCollider，16 块共用同一个材质。
    /// 相邻块在边界那一列上各存一份**完全相同**的顶点（位置和法线都由 x,z 唯一决定），
    /// 所以看不出缝 —— 这一点由编辑器自检逐点比对相邻块来保证。
    /// </summary>
    void BuildTerrain()
    {
        for (int cz = 0; cz < TerrainChunks; cz++)
        {
            for (int cx = 0; cx < TerrainChunks; cx++)
            {
                var mesh = BuildTerrainChunkMesh(cx, cz);

                var go = new GameObject($"Terrain_{cx}_{cz}");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = _terrainMaterial;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
        }
    }

    /// <summary>
    /// 建一块地形网格。**纯函数**：只依赖高度场和上色公式，不需要实例状态，
    /// 所以编辑器自检可以直接调它来逐点比对相邻块的接缝。
    ///
    /// ★ 法线用高度场的中心差分**解析算**，不能用 `RecalculateNormals`：
    ///   分块之后每块只看得到自己的三角形，边界上算出来的法线会和邻块不一致，
    ///   地形上会出现一道道硬边。解析法线只依赖 (x,z)，天然连续。
    ///
    /// ★ 不生成切线：地形着色器 Voxel/TerrainBlend 是纯 unlit 的，
    ///   只读 POSITION / COLOR / TEXCOORD1，没有法线贴图 —— 切线流在这里纯属浪费
    ///   （86 万顶点就是 10MB + 一次很慢的重算）。
    /// </summary>
    public static Mesh BuildTerrainChunkMesh(int cx, int cz)
    {
        int n = ChunkCells + 1;
        int baseIx = cx * ChunkCells;
        int baseIz = cz * ChunkCells;

        var verts = new Vector3[n * n];
        var cols  = new Color[n * n];
        var norms = new Vector3[n * n];
        var blend = new Vector2[n * n];      // UV2：x 岩石权重 / y 沙滩权重

        for (int iz = 0; iz < n; iz++)
        {
            for (int ix = 0; ix < n; ix++)
            {
                float x = (baseIx + ix) * Cell;
                float z = (baseIz + iz) * Cell;
                int k = iz * n + ix;

                verts[k] = new Vector3(x, Height(x, z), z);
                cols[k]  = TerrainColor(x, z);
                norms[k] = TerrainNormal(x, z);
                blend[k] = TerrainBlend(x, z);
            }
        }

        var tris = new List<int>(ChunkCells * ChunkCells * 6);
        for (int iz = 0; iz < ChunkCells; iz++)
        {
            for (int ix = 0; ix < ChunkCells; ix++)
            {
                int i0 = iz * n + ix;
                int i1 = i0 + 1;
                int i2 = i0 + n;
                int i3 = i2 + 1;

                tris.Add(i0); tris.Add(i2); tris.Add(i1);   // 从上看是逆时针
                tris.Add(i1); tris.Add(i2); tris.Add(i3);
            }
        }

        var mesh = new Mesh { name = $"OpenWorldTerrain_{cx}_{cz}" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetNormals(norms);
        mesh.SetUVs(1, blend);                      // UV2 = 贴图混合权重
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>解析法线：拿高度场的中心差分算出来的朝上法线。
    /// 和 TerrainColor / TerrainBlend 里用的是同一套取法，块与块之间完全一致 → 没有接缝硬边</summary>
    public static Vector3 TerrainNormal(float x, float z)
    {
        float hx = Height(x - Cell, z) - Height(x + Cell, z);
        float hz = Height(x, z - Cell) - Height(x, z + Cell);
        return new Vector3(hx, 2f * Cell, hz).normalized;
    }

    /// <summary>贴图混合权重：陡坡走向岩石，接近水面走向沙滩，其余是草地</summary>
    static Vector2 TerrainBlend(float x, float z)
    {
        const float d = Cell;
        Vector3 normal = new Vector3(Height(x - d, z) - Height(x + d, z), 2f * d,
                                     Height(x, z - d) - Height(x, z + d)).normalized;

        float y = Height(x, z);
        // 阈值调高：只有真正的陡坡才混岩石贴图，缓坡保持草地色，
        // 免得草原上到处是灰紫色斑块（岩石贴图偏冷色，混到绿地上很脏）
        float rock = Mathf.SmoothStep(0.30f, 0.52f, 1f - normal.y);
        float sand = Mathf.SmoothStep(WaterLevel + 4.5f, WaterLevel + 0.5f, y);     // 越靠水面越像沙滩

        // 雪面上不混草地 / 沙滩贴图（保持干净的平涂白），陡面还是走岩石贴图
        if (SnowFactor(x, z) > 0.5f) sand = 0f;

        // 璃月侧：整片是石灰岩地貌，岩石贴图的权重整体抬一点（山体看着更硬朗、更"石头"）
        float lw = LiyueWeight(x, z);
        if (lw > 0.001f)
        {
            float rockLiyue = Mathf.SmoothStep(0.16f, 0.42f, 1f - normal.y);
            rock = Mathf.Lerp(rock, Mathf.Max(rock, rockLiyue), lw);
        }

        return new Vector2(rock, sand);
    }

    // 雪线以上的岩石面颜色（和蒙德配色里的岩石色一致；雪顶压在两国分界上，两边共用）
    static readonly Color SnowRockFace = new Color32(0xac, 0xa6, 0x97, 255);
    static readonly Color SnowRockHigh = new Color32(0x94, 0x8f, 0x89, 255);

    /// <summary>按高度和坡度决定地表颜色，并压上朝向明暗 + 一点色差。
    /// 二次元风：高饱和平涂色 + 明显的坡面岩石色 + 水边一圈奶白浅滩，噪点压得很低，避免写实感。
    ///
    /// 两个国度各一套调色板（蒙德草原绿 / 璃月石灰岩黄褐土），按 LiyueWeight 混。
    /// ★ 对调色板本身没有权重时（蒙德侧 lw = 0）走的是和以前**完全一样**的那条路 ——
    ///   这是"加璃月不影响蒙德"的保证，自检里会验。</summary>
    static Color TerrainColor(float x, float z)
    {
        const float d = Cell;
        Vector3 normal = new Vector3(Height(x - d, z) - Height(x + d, z), 2f * d,
                                     Height(x, z - d) - Height(x, z + d)).normalized;

        float y = Height(x, z);
        float slope = 1f - normal.y;

        Color c = MondstadtColor(x, z, y, slope);

        float lw = LiyueWeight(x, z);
        if (lw > 0.001f) c = Color.Lerp(c, LiyueColor(x, z, y, slope), lw);

        // 稻妻：苔绿 / 火山岩 / 白沙那套（按岛上陆地权重混；外海海底仍是沙滩色，被水面盖住）
        float iw = InazumaIsles.LandWeight(x, z);
        if (iw > 0.001f) c = Color.Lerp(c, InazumaIsles.LandColor(x, z, y, slope), iw);

        // ---------- 雪线以上压成雪（两国通用：雪顶正好压在分界线上） ----------
        float snow = SnowFactor(x, z);
        if (IsFrozenLake(x, z))                                          // 冰湖：冻住的湖面
        {
            Color iceCol = new Color32(0xbf, 0xe2, 0xf0, 255);
            c = Color.Lerp(iceCol, new Color32(0xf2, 0xfa, 0xff, 255),
                           Mathf.PerlinNoise(x * 0.12f, z * 0.12f) * 0.5f);
        }
        else if (snow > 0.01f)
        {
            Color snowLight = new Color32(0xf7, 0xfb, 0xff, 255);
            Color snowShade = new Color32(0xd6, 0xe4, 0xf2, 255);

            Color snowCol = Color.Lerp(snowShade, snowLight, Mathf.PerlinNoise(x * 0.05f, z * 0.05f));
            Color rockFace = Color.Lerp(SnowRockFace, SnowRockHigh, Mathf.PerlinNoise(x * 0.07f, z * 0.07f));
            Color surface = Color.Lerp(snowCol, rockFace, Mathf.SmoothStep(0.30f, 0.62f, slope));
            c = Color.Lerp(c, surface, Mathf.Clamp01(snow * 1.35f));
        }

        float lit = 0.80f + 0.20f * normal.y;                            // 面朝天更亮：只留大块柔和起伏
        c *= lit;
        c.a = 1f;
        return c.linear;
    }

    /// <summary>蒙德的地表配色：饱和草原绿 + 湖岸奶白沙 + 城里的石板灰</summary>
    static Color MondstadtColor(float x, float z, float y, float slope)
    {
        // 二次元配色：饱和、干净、不写实（贴近参考图那种草原绿）
        Color grassLight = new Color32(0xa9, 0xe0, 0x6b, 255);
        Color grass      = new Color32(0x7f, 0xd0, 0x52, 255);
        Color forest     = new Color32(0x5c, 0xb4, 0x45, 255);
        Color rockLight  = new Color32(0xcd, 0xc6, 0xb5, 255);
        Color rock       = new Color32(0xac, 0xa6, 0x97, 255);
        Color sand       = new Color32(0xf2, 0xe6, 0xb8, 255);
        Color paveLight  = new Color32(0xd7, 0xd0, 0xbe, 255);
        Color pave       = new Color32(0xbd, 0xb6, 0xa4, 255);

        // 水边：从水面往上 4.5 米内平滑过渡到草地，边界再用噪声抖一下。
        // ★ 原来是"水面 1 米内直接切沙滩色"——世界地图上每个水塘都套一圈硬边沙环，看着特别割裂
        if (y < WaterLevel + 4.5f)
        {
            float t = Mathf.InverseLerp(WaterLevel - 0.8f, WaterLevel + 4.5f, y);
            float n = Mathf.PerlinNoise(x * 0.055f, z * 0.055f);
            return Color.Lerp(sand, grass, Mathf.Clamp01(t * (0.7f + n * 0.6f)));
        }

        if (slope > 0.24f)                                               // 陡坡露岩
            return Color.Lerp(rock, rockLight, Mathf.PerlinNoise(x * 0.09f, z * 0.09f));

        if (OnTownStreet(x, z) || IsRoad(x, z))                          // 城里的石板街 + 城门外的路
            return Color.Lerp(pave, paveLight, Mathf.PerlinNoise(x * 0.4f, z * 0.4f));

        // 草地：成林的地方压深一点，像森林
        float grove = ForestDensity(x, z);
        Color c = Color.Lerp(grass, grassLight, Mathf.PerlinNoise(x * 0.05f, z * 0.05f));
        return Color.Lerp(c, forest, Mathf.SmoothStep(0.45f, 0.85f, grove) * 0.55f);
    }

    /// <summary>
    /// 璃月的地表配色：石灰岩暖灰 + 黄褐土 + 梯田水田绿 + 竹林青绿。
    /// 和蒙德那套"饱和草原绿"是两个世界，一眼能看出换了国度。
    /// </summary>
    static Color LiyueColor(float x, float z, float y, float slope)
    {
        Color soilLight = new Color32(0xdc, 0xc0, 0x8a, 255);   // 黄褐土（亮）
        Color soil      = new Color32(0xc9, 0xa9, 0x6e, 255);   // 黄褐土
        Color grassY     = new Color32(0x93, 0xbf, 0x5c, 255);  // 偏黄的草绿（璃月的主色：比蒙德那套冷、干一点）
        Color grassYLit  = new Color32(0xb2, 0xd6, 0x78, 255);  // 偏黄的草绿（亮）
        Color limeLight = new Color32(0xcf, 0xc8, 0xba, 255);   // 石灰岩（亮）
        Color lime      = new Color32(0xb9, 0xb2, 0xa4, 255);   // 石灰岩
        Color paddy     = new Color32(0x8f, 0xbf, 0x6a, 255);   // 水田
        Color paddyLit  = new Color32(0xa8, 0xd4, 0x8a, 255);   // 水田反光
        Color grove     = new Color32(0x74, 0x9a, 0x58, 255);   // 竹林底
        Color bank      = new Color32(0xe0, 0xd3, 0xa8, 255);   // 水边滩涂

        Color c;

        if (y < WaterLevel + 4.5f)                                   // 水边滩涂：同样放宽 + 噪声抖边界
        {
            float t = Mathf.InverseLerp(WaterLevel - 0.8f, WaterLevel + 4.5f, y);
            float n = Mathf.PerlinNoise(x * 0.055f, z * 0.055f);
            c = Color.Lerp(bank, Color.Lerp(soil, grassY, 0.85f), Mathf.Clamp01(t * (0.7f + n * 0.6f)));
        }
        else if (slope > 0.20f)                                      // 陡坡 = 石灰岩崖面（峰林就长这样）
            c = Color.Lerp(lime, limeLight, Mathf.PerlinNoise(x * 0.09f, z * 0.09f));
        else
        {
            // 平缓地：**以草为主**（偏黄一点的草绿，和蒙德那种饱和绿区分开），
            // 只在低频噪声选中的地方露黄褐土。
            // ★ 原来是整片刷土色 —— 于是整个璃月看着像一片黄沙平原（截图里那种"沙漠感"）
            float n = Mathf.PerlinNoise(x * 0.045f, z * 0.045f);
            float dry = Mathf.SmoothStep(0.52f, 0.82f, Mathf.PerlinNoise(x * 0.019f + 5.3f, z * 0.019f + 2.7f));

            c = Color.Lerp(grassY, grassYLit, n);
            c = Color.Lerp(c, Color.Lerp(soil, soilLight, n), dry * 0.85f);

            float groveN = ForestDensity(x, z);
            c = Color.Lerp(c, grove, Mathf.SmoothStep(0.45f, 0.85f, groveN) * 0.5f);
        }

        // 梯田：台阶面（平的）压成水田绿、台阶立面（陡的）留田埂土色 —— 真实梯田就是这个观感
        float terrace = TerraceWeight(x, z);
        if (terrace > 0.01f)
        {
            Color paddyCol = Color.Lerp(paddy, paddyLit, Mathf.PerlinNoise(x * 0.07f, z * 0.07f));
            Color t = Color.Lerp(soil, paddyCol, Mathf.SmoothStep(0.14f, 0.03f, slope));
            c = Color.Lerp(c, t, terrace);
        }

        return c;
    }

    /// <summary>在山坡上找一处"雪线下面一点的缓坡"搭前哨营地：从主峰往西南走，
    /// 一边走一边看地面高度，走到刚跌破雪线就停下（按高度找，不写死坐标 —— 山体尺寸改了也不会落到山外）</summary>
    static Vector3 FindCampSpot()
    {
        float bestX = MassifPeakX - 90f, bestZ = MassifPeakZ + 90f;

        for (float d = 50f; d <= 300f; d += 5f)
        {
            float cx = MassifPeakX - d * 0.62f;      // 朝西南
            float cz = MassifPeakZ + d * 0.78f;
            if (!InBounds(cx, cz)) break;

            bestX = cx; bestZ = cz;
            if (Height(cx, cz) < SnowLine - 20f) break;   // 跌破雪线 20 米 → 就是山脚缓坡
        }

        return new Vector3(bestX, Height(bestX, bestZ), bestZ);
    }

    // ---------- 树和石头（按块合并成网格） ----------

    /// <summary>
    /// 树石：和地形用同一套 4×4 分块，每块一张网格（共用同一个顶点色材质）。
    /// 分块的好处是树多的那一块才需要渲染，而且合并网格不会因为总顶点数爆掉。
    /// 判定逻辑（水 / 坡 / 禁植区 / 城郊留空 / 雪线树种 / 成林噪声）一个字没改，
    /// 只是把同一个格子按"落在哪一块"分到不同的网格里 —— 每个格子仍然只处理一次。
    /// </summary>
    void BuildProps()
    {
        Vector3 spawn = SpawnPoint;

        for (int tz = 0; tz < TerrainChunks; tz++)
        {
            for (int tx = 0; tx < TerrainChunks; tx++)
            {
                // 这一块负责的格子范围（最外圈留 3 格不种，和以前一样）
                int gx0 = Mathf.Max(3, tx * ChunkCells);
                int gx1 = Mathf.Min(Res - 3, (tx + 1) * ChunkCells);
                int gz0 = Mathf.Max(3, tz * ChunkCells);
                int gz1 = Mathf.Min(Res - 3, (tz + 1) * ChunkCells);

                _verts.Clear(); _cols.Clear(); _tris.Clear();

                for (int gx = gx0; gx < gx1; gx += 2)
                {
                    for (int gz = gz0; gz < gz1; gz += 2)
                    {
                        AppendPropAt(gx, gz, spawn);
                    }
                }

                // 这一块没有树石就跳过（空网格不用建）
                if (_verts.Count == 0) continue;

                var mesh = new Mesh { name = $"OpenWorldProps_{tx}_{tz}" };
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_verts);
                mesh.SetColors(_cols);
                mesh.SetTriangles(_tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                var go = new GameObject($"Props_{tx}_{tz}");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = _material;
            }
        }
    }

    /// <summary>某一格上长什么树 / 石头（原来内层循环的整套判断，逻辑一个字没改）。
    /// 顶点直接写进共享的 _verts/_cols/_tris，由调用方决定这些顶点归哪一块网格</summary>
    void AppendPropAt(int gx, int gz, Vector3 spawn)
    {
        float x = (gx + Hash01(gx, gz, 11)) * Cell;
        float z = (gz + Hash01(gx, gz, 12)) * Cell;
        float y = Height(x, z);

        if (IsDeepWater(x, z)) return;                             // 不种在水里
        if (Slope(x, z) > 0.22f) return;                           // 峭壁上不种
        if (InKeepOut(x, z)) return;                               // 野外建筑 / 解密场地的占地：树石让路
        // 注：地台的边坡过渡带**不禁**树石 —— 那边的地形虽然斜，但树石是按脚下
        //     实际地面高度摆的，不会悬空；禁掉反而会让每个村子外面秃一大圈
        if (Vector2.Distance(new Vector2(x, z), new Vector2(spawn.x, spawn.z)) < 22f) return;   // 出生点一圈留空
        if (Vector2.Distance(new Vector2(x, z), new Vector2(MondstadtTown.CenterX, MondstadtTown.CenterZ))
            < MondstadtTown.IslandR + 11f) return;                 // 城郊留空（城里更不可能长树）
        if (Vector2.Distance(new Vector2(x, z), new Vector2(MondstadtTown.CathedralX, MondstadtTown.CathedralZ))
            < MondstadtTown.CathedralR + 7f) return;               // 大教堂高台：留给教堂自己那几棵
        if (IsRoad(x, z)) return;                                  // 路上不种
        if (Mathf.Abs(x - MondstadtTown.CenterX) < 7f &&
            z > MondstadtTown.SouthBridgeZ - 4f &&
            z < MondstadtTown.CenterZ - MondstadtTown.WallR + 4f) return;      // 南桥桥面
        if (Mathf.Abs(z - MondstadtTown.CenterZ) < 7f &&
            x > MondstadtTown.CenterX + MondstadtTown.WallR - 4f &&
            x < MondstadtTown.EastBridgeFarX + 4f) return;                     // 东桥桥面
        if (NearDock(x, z, 7f)) return;                                        // 木栈桥码头那一条

        // 成簇的树：低频噪声决定这一片密不密 → 大片开阔草地夹着树丛（蒙德郊外那种）
        float grove = ForestDensity(x, z);
        float roll = Hash01(gx, gz, 13);

        // 雪线附近到雪线以上：只长雪松 / 冰晶 / 雪岩，没有草原那种树（真实的树线）
        float snow = SnowFactor(x, z);
        if (snow > 0.35f)
        {
            if (snow > 0.85f)                                      // 雪线以上：冰晶 + 雪岩
            {
                if (roll > 0.90f) AppendIceCrystal(x, y, z, Hash01(gx, gz, 17));
                else if (roll > 0.80f) AppendSnowRock(x, y, z, Hash01(gx, gz, 15));
            }
            else                                                   // 树线附近：稀疏雪松
            {
                if (roll < 0.45f) AppendSnowTree(x, y, z, Hash01(gx, gz, 14));
                else if (roll > 0.90f) AppendSnowRock(x, y, z, Hash01(gx, gz, 15));
            }
            return;
        }

        // 稻妻：樱 / 松 / 竹，石头是黑灰的火山岩
        if (IsInazumaLand(x, z))
        {
            float pick = Hash01(gx, gz, 16);
            if (roll < grove * grove * grove * 0.62f)
            {
                if (pick < 0.46f) AppendSakura(x, y, z, Hash01(gx, gz, 14));
                else if (pick < 0.78f) AppendPine(x, y, z, Hash01(gx, gz, 18));
                else AppendBamboo(x, y, z, Hash01(gx, gz, 14));
            }
            else if (roll > 0.94f) AppendRock(x, y, z, Hash01(gx, gz, 15));
            return;
        }

        // 璃月侧：成林的地方长竹、石头换成石灰岩
        if (IsLiyueLand(x, z))
        {
            if (roll < grove * grove * grove * 0.6f) AppendBamboo(x, y, z, Hash01(gx, gz, 14));
            else if (roll > 0.96f) AppendRock(x, y, z, Hash01(gx, gz, 15));
            return;
        }

        if (roll < grove * grove * grove * 0.6f)                   // 只有噪声高的地方才成林
            AppendTree(x, y, z, Hash01(gx, gz, 14));
        else if (roll > 0.97f)
            AppendRock(x, y, z, Hash01(gx, gz, 15));
    }

    // ---------- 「Andy的雪顶」上的固定地标 ----------

    /// <summary>雪顶上的固定地标：山脚前哨营地（木屋 + 火堆）和主峰顶的登顶石堆。
    /// 两处都记到世界地图上，照着地图就能找到雪顶。</summary>
    void BuildSnowLandmarks()
    {
        _verts.Clear(); _cols.Clear(); _tris.Clear();

        Color wood   = new Color32(0x6b, 0x4a, 0x2f, 255);
        Color wood2  = new Color32(0x8a, 0x66, 0x42, 255);
        Color hide   = new Color32(0x9c, 0x7c, 0x5a, 255);
        Color stone  = new Color32(0xac, 0xa6, 0x9c, 255);
        Color stoneD = new Color32(0x8e, 0x89, 0x82, 255);
        Color snowC  = new Color32(0xf4, 0xf9, 0xff, 255);

        // 前哨营地：搭在雪山南麓的缓坡上（雪线下面一点）
        //
        // ★ 位置**按实际高度找**，不写死坐标：山体尺寸一改（比如这次把雪山收小），
        //   写死的坐标就会落到山体外的平地上，营地就悬在草原里了。
        //   这里从主峰往西南走，走到地面降到雪线以下 20 米左右站住。
        Vector3 camp = FindCampSpot();

        var rot = Quaternion.Euler(0f, 24f, 0f);

        // 木屋：地基 + 墙 + 两面坡顶 + 烟囱
        AppendBox(camp + Vector3.up * 0.35f, new Vector3(4.6f, 0.7f, 4.2f), rot, stoneD, stone);
        AppendBox(camp + Vector3.up * 1.85f, new Vector3(4.0f, 2.3f, 3.6f), rot, hide, wood2);
        AppendBox(camp + Vector3.up * 3.55f, new Vector3(5.0f, 0.32f, 4.6f), rot * Quaternion.Euler(0f, 0f, 30f), snowC, wood);
        AppendBox(camp + Vector3.up * 3.55f, new Vector3(5.0f, 0.32f, 4.6f), rot * Quaternion.Euler(0f, 0f, -30f), snowC, wood);
        AppendBox(camp + rot * new Vector3(1.7f, 4.5f, 1.2f), new Vector3(0.75f, 1.7f, 0.75f), rot, stoneD, stoneD);

        // 门口火堆：石圈 + 交叉柴
        Vector3 fire = camp + rot * new Vector3(0f, 0f, 3.8f);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            AppendBox(fire + new Vector3(Mathf.Cos(a) * 1.05f, 0.16f, Mathf.Sin(a) * 1.05f),
                      new Vector3(0.4f, 0.3f, 0.4f), Quaternion.identity, stone, stoneD);
        }
        for (int i = 0; i < 3; i++)
            AppendBox(fire + Vector3.up * (0.3f + i * 0.14f), new Vector3(0.18f, 0.18f, 1.7f),
                      Quaternion.Euler(0f, i * 55f, 0f), wood, wood);

        // 主峰登顶石堆 + 旗杆（登顶打卡点）
        Vector3 top = new Vector3(MassifPeakX, Height(MassifPeakX, MassifPeakZ), MassifPeakZ);
        for (int i = 0; i < 5; i++)
        {
            float r = 1.5f - i * 0.24f;
            AppendBlob(top + Vector3.up * (0.5f + i * 0.55f), r, Color.Lerp(stone, snowC, 0.55f), 6, 0.6f, i * 0.31f);
        }
        AppendPrism(top + Vector3.up * 3.1f, 4.2f, 0.22f, 0.16f, 5, wood2);
        AppendBox(top + Vector3.up * 6.6f, new Vector3(0.12f, 1.3f, 1.9f), Quaternion.identity,
                  new Color32(0x5f, 0xa8, 0xd8, 255), new Color32(0x4c, 0x8f, 0xc4, 255));

        // 合成一个网格 + 碰撞
        var mesh = new Mesh { name = "SnowLandmarks" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(_verts);
        mesh.SetColors(_cols);
        mesh.SetTriangles(_tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("SnowLandmarks");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = _material;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;

        MondstadtTown.Markers.Add(new MondstadtTown.MapMarker { name = "雪顶前哨营地", x = camp.x, z = camp.z, kind = 10 });
        MondstadtTown.Markers.Add(new MondstadtTown.MapMarker { name = "Andy的雪顶主峰", x = top.x, z = top.z, kind = 11 });
        MondstadtTown.Markers.Add(new MondstadtTown.MapMarker { name = "冰湖", x = FrozenLakeX, z = FrozenLakeZ, kind = 12 });

        Debug.Log($"[OpenWorld] Andy的雪顶：主峰 {top.y:0} 米，前哨营地 {camp.y:0} 米，冰湖 ({FrozenLakeX},{FrozenLakeZ})");
    }

    /// <summary>一个盒子（顶面 / 侧面分开上色）：绕序和城里那套一致，法线朝外</summary>
    void AppendBox(Vector3 center, Vector3 size, Quaternion rot, Color top, Color side)
        => LowPoly.Box(_verts, _cols, _tris, center, size, rot, top, side);

    /// <summary>雪松：树干 + 三层锥形树冠，每层顶上一团雪</summary>
    void AppendSnowTree(float x, float y, float z, float seed)
    {
        float h = 4.0f + seed * 3.2f;
        AppendPrism(new Vector3(x, y, z), h * 0.5f, 0.28f, 0.2f, 5, new Color32(0x5a, 0x46, 0x38, 255));

        Color needle  = new Color32(0x2f, 0x5a, 0x46, 255);
        Color snowCap = new Color32(0xf0, 0xf7, 0xff, 255);

        for (int i = 0; i < 3; i++)
        {
            float t = i / 2f;
            float r = (2.0f - t * 0.8f) * (0.85f + seed * 0.3f);
            float layerH = h * 0.42f;
            float layerY = y + h * 0.18f + i * h * 0.22f;

            AppendPrism(new Vector3(x, layerY, z), layerH, r, r * 0.14f, 6, needle);
            AppendBlob(new Vector3(x, layerY + layerH, z), r * 0.52f, snowCap, 5, 0.45f, seed + i * 0.4f);
        }
    }

    /// <summary>冰晶：两三根淡蓝尖柱，雪线以上的地标感</summary>
    void AppendIceCrystal(float x, float y, float z, float seed)
    {
        Color ice = Color.Lerp(new Color32(0x9f, 0xd8, 0xef, 255), new Color32(0xe6, 0xf7, 0xff, 255), seed);
        int n = 2 + Mathf.RoundToInt(seed * 2f);

        for (int i = 0; i < n; i++)
        {
            float a = Hash01((int)x + i, (int)z, 19) * Mathf.PI * 2f;
            float r = 0.4f + Hash01(i, (int)z, 21) * 0.55f;
            float hh = 1.5f + Hash01(i, (int)x, 23) * 2.8f;
            AppendPrism(new Vector3(x + Mathf.Cos(a) * r, y - 0.25f, z + Mathf.Sin(a) * r), hh, 0.3f, 0.04f, 4, ice);
        }
    }

    /// <summary>雪岩：一块石头 + 顶上压着一层雪</summary>
    void AppendSnowRock(float x, float y, float z, float seed)
    {
        float r = 0.8f + seed * 1.4f;
        AppendBlob(new Vector3(x, y - r * 0.32f, z), r, new Color32(0xb0, 0xac, 0xa4, 255), 6, 0.7f, seed);
        AppendBlob(new Vector3(x, y + r * 0.3f, z), r * 0.88f, new Color32(0xf4, 0xf9, 0xff, 255), 6, 0.42f, seed + 0.4f);
    }

    /// <summary>湖心那座蒙德风小城：一个网格 + 三个子网格（石墙贴图 / 屋顶贴图 / 顶点色）+ MeshCollider</summary>
    void BuildTown()
    {
        _verts.Clear(); _cols.Clear(); _tris.Clear();
        MondstadtTown.Append(_verts, _cols, _tris);
        if (_verts.Count == 0) return;

        var mesh = new Mesh { name = "MondstadtTown" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(_verts);
        mesh.SetColors(_cols);
        mesh.subMeshCount = 3;
        mesh.SetTriangles(MondstadtTown.SubTris[0], 0);   // 墙体：石墙贴图
        mesh.SetTriangles(MondstadtTown.SubTris[1], 1);   // 屋顶：瓦片贴图
        mesh.SetTriangles(MondstadtTown.SubTris[2], 2);   // 木结构 / 雕像 / 旗帜：顶点色
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("Town");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { _stoneMaterial, _roofMaterial, _material };

        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    /// <summary>一丛竹子：几根细高的青绿竹竿 + 顶上一撮叶。
    /// 竹竿又细又高（7~13 米、半径只有 0.1 米，比蒙德的树瘦得多），成片长起来一眼就是竹林</summary>
    void AppendBamboo(float x, float y, float z, float seed)
    {
        int stalks = 3 + Mathf.RoundToInt(seed * 3f);
        Color stalk = new Color32(0x8d, 0xb8, 0x57, 255);
        Color leaf  = new Color32(0x5f, 0x99, 0x44, 255);

        for (int i = 0; i < stalks; i++)
        {
            float a = Hash01(i, Mathf.RoundToInt(seed * 997f), 31) * Mathf.PI * 2f;
            float r = Hash01(i, Mathf.RoundToInt(seed * 883f), 32) * 1.15f;
            float h = 7f + Hash01(i, Mathf.RoundToInt(seed * 771f), 33) * 6f;
            float bx = x + Mathf.Cos(a) * r;
            float bz = z + Mathf.Sin(a) * r;

            AppendPrism(new Vector3(bx, y - 0.2f, bz), h, 0.1f, 0.07f, 4, stalk);

            // 顶上的叶：两团压扁的小球，比竹竿颜色深
            AppendBlob(new Vector3(bx, y + h, bz), 0.78f, leaf, 5, 0.5f, seed + i * 0.3f);
            AppendBlob(new Vector3(bx + 0.3f, y + h - 1.3f, bz - 0.2f), 0.56f, leaf * 0.94f, 5, 0.5f, seed + i * 0.5f);
        }
    }

    /// <summary>一棵树：锥形树干 + 一两团树冠（四成是樱花粉）</summary>
    void AppendTree(float x, float y, float z, float seed)
    {
        bool cherry = seed > 0.6f;
        float height = 4.5f + seed * 2.5f;
        float trunkR = 0.28f;

        AppendPrism(new Vector3(x, y, z), height, trunkR, trunkR * 0.62f, 6,
                    new Color32(0x8a, 0x63, 0x42, 255));

        Color leaf = cherry ? new Color32(0xf6, 0xa8, 0xc8, 255) : new Color32(0x5b, 0xb2, 0x4c, 255);
        float canopyR = cherry ? 2.0f : 2.6f;

        AppendBlob(new Vector3(x, y + height, z), canopyR, leaf, 7, 0.78f, seed);
        AppendBlob(new Vector3(x + 0.7f, y + height - 0.9f, z - 0.5f), canopyR * 0.72f, leaf * 0.94f, 6, 0.8f, seed + 0.3f);
        AppendBlob(new Vector3(x - 0.6f, y + height - 0.6f, z + 0.6f), canopyR * 0.66f, leaf * 0.98f, 6, 0.8f, seed + 0.7f);
    }

    /// <summary>樱花树（稻妻）：树干斜一点、树冠是一大片压扁的粉，比蒙德那棵"樱"更粉更铺开</summary>
    void AppendSakura(float x, float y, float z, float seed)
    {
        float height = 3.6f + seed * 2.0f;
        Color trunk = new Color32(0x5f, 0x4a, 0x3c, 255);
        Color pink  = Color.Lerp(new Color32(0xf7, 0xb2, 0xcd, 255), new Color32(0xe6, 0x8f, 0xb4, 255), seed);

        // 树干：两段，上面那段歪出去一点（樱花的姿态）
        AppendPrism(new Vector3(x, y, z), height, 0.26f, 0.19f, 6, trunk);
        Vector3 lean = new Vector3(0.5f + seed * 0.4f, height * 0.86f, 0.2f);
        AppendPrism(new Vector3(x + lean.x, y + lean.y, z + lean.z), height * 0.55f, 0.15f, 0.09f, 5, trunk);

        // 树冠：一大两小，压得很扁，铺得像伞
        Vector3 top = new Vector3(x + lean.x, y + lean.y + height * 0.55f, z + lean.z);
        AppendBlob(top, 2.5f, pink, 8, 0.5f, seed);
        AppendBlob(top + new Vector3(1.3f, -0.5f, 0.5f), 1.6f, pink * 0.94f, 6, 0.5f, seed + 0.3f);
        AppendBlob(top + new Vector3(-1.1f, -0.7f, -0.6f), 1.5f, pink * 1.04f, 6, 0.55f, seed + 0.7f);
    }

    /// <summary>松树（稻妻）：细高树干 + 三层收小的深绿伞盖（和雪松同一个骨架，颜色更暖）</summary>
    void AppendPine(float x, float y, float z, float seed)
    {
        float height = 6.5f + seed * 4.0f;
        Color trunk = new Color32(0x4e, 0x3d, 0x30, 255);
        Color leaf  = Color.Lerp(new Color32(0x3d, 0x6b, 0x45, 255), new Color32(0x52, 0x82, 0x4e, 255), seed);

        AppendPrism(new Vector3(x, y, z), height * 0.62f, 0.3f, 0.2f, 6, trunk);

        for (int i = 0; i < 3; i++)
        {
            float t = i / 2f;
            float r = 2.2f - t * 1.15f;
            float hh = height * (0.22f + t * 0.04f);
            float by = y + height * (0.42f + t * 0.28f);
            AppendPrism(new Vector3(x, by, z), hh, r, r * 0.18f, 7, leaf * (1f - t * 0.10f));
        }
    }

    /// <summary>一块石头：压扁的低模球</summary>
    void AppendRock(float x, float y, float z, float seed)    {
        float r = 0.7f + seed * 1.1f;
        Color c = Color.Lerp(new Color32(0xa8, 0x9f, 0x8e, 255), new Color32(0xc6, 0xbd, 0xab, 255), seed);
        AppendBlob(new Vector3(x, y - r * 0.35f, z), r, c, 6, 0.72f, seed);
    }

    /// <summary>低模球（经纬度球 + 每点轻微抖动，避免看起来太圆太规整）</summary>
    void AppendBlob(Vector3 center, float radius, Color color, int segments, float squash, float seed)
        => LowPoly.Sphere(_verts, _cols, _tris, center, radius, color, segments, squash, 1f, seed);

    /// <summary>棱柱（树干）：下粗上细</summary>
    void AppendPrism(Vector3 basePos, float height, float bottomR, float topR, int sides, Color color)
        => LowPoly.Prism(_verts, _cols, _tris, basePos, height, bottomR, topR, sides, color);

    // ---------- 湖水 ----------

    void BuildWater()
    {
        var go = new GameObject("Water");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(Size * 0.5f, WaterLevel, Size * 0.5f);

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildQuad(Size, Size);

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _waterMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    static Mesh BuildQuad(float w, float d)
    {
        var mesh = new Mesh { name = "OpenWorldWaterQuad" };
        mesh.SetVertices(new List<Vector3>
        {
            new Vector3(-w * 0.5f, 0f, -d * 0.5f), new Vector3(w * 0.5f, 0f, -d * 0.5f),
            new Vector3(w * 0.5f, 0f,  d * 0.5f),  new Vector3(-w * 0.5f, 0f,  d * 0.5f),
        });

        Color tint = ((Color)new Color32(0x45, 0xb7, 0xd4, 0xff)).linear;
        mesh.SetColors(new List<Color> { tint * 0.96f, tint * 0.96f, tint, tint });
        mesh.SetTriangles(new List<int> { 0, 2, 1, 0, 3, 2 }, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------- 天空 / 后处理 ----------

    /// <summary>有生成好的全景天空盒就用上（没有就保持纯色天空）</summary>
    void TryApplySkybox()
    {
        var panorama = Resources.Load<Texture2D>("SkyboxMondstadt");
        if (panorama == null) return;

        var shader = Shader.Find("Skybox/Panoramic");
        if (shader == null) return;

        var mat = new Material(shader);
        mat.SetTexture("_MainTex", panorama);
        mat.SetFloat("_Mapping", 1f);        // 1 = 经纬度全景
        mat.SetFloat("_ImageType", 0f);
        mat.SetFloat("_Exposure", 1.05f);
        RenderSettings.skybox = mat;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.68f, 0.72f);

        var cam = Camera.main;
        if (cam != null) cam.clearFlags = CameraClearFlags.Skybox;

        DynamicGI.UpdateEnvironment();
        Debug.Log("[OpenWorld] 已应用天空盒 Resources/SkyboxMondstadt");
    }

    /// <summary>后处理：稍微加一点泛光和饱和度，画面更像动画</summary>
    void SetupPostProcessing()
    {
        var go = new GameObject("WorldPostFx");
        go.transform.SetParent(transform, false);

        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = profile;

        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.55f);
        bloom.threshold.Override(1.0f);
        bloom.scatter.Override(0.7f);

        var color = profile.Add<ColorAdjustments>(true);
        color.saturation.Override(12f);
        color.contrast.Override(6f);
        color.postExposure.Override(0.15f);

        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.18f);
        vignette.smoothness.Override(0.5f);
    }

    // ---------- 工具 ----------

    /// <summary>0~1 的确定性哈希（实现在 LowPoly 里，全地图共用一套算法）</summary>
    static float Hash01(int a, int b, int salt) => LowPoly.Hash01(a, b, salt);
}
