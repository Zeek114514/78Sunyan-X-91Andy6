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

    public const float Size = 600f;        // 地图边长（米）—— 比上一版大了一圈，东北角整片留给龙脊雪山
    const int   Res  = 400;                // 每边的格子数（1.5 米一格，精度和上一版基本一致）
    public const float Cell = Size / Res;  // 一格 1.5 米

    /// <summary>水面高度：比它低的地方就是湖 / 河</summary>
    public const float WaterLevel = 3.2f;

    /// <summary>雪线：比它高的地方开始积雪（上色、怪物分布、严寒值都用它）</summary>
    public const float SnowLine = 62f;

    // ---------- 龙脊雪山（地图东北角那一整片） ----------
    // 一条西南 → 东北走向的大山脊：主峰约 205 米高、山体铺开三百多米，
    // 再叠上山脊噪声（锯齿感）、两条 U 形冰川谷、谷口一道冰碛垄、鞍部一个冰蚀盆地（冰湖）。
    const float MassifPeakX = 440f, MassifPeakZ = 442f;    // 主峰中心
    const float FrozenLakeX = 474f, FrozenLakeZ = 470f;    // 冰湖（冻住的湖面，平的、能走）

    // ---------- 高度场缓存 ----------
    // 地形高度是十几个三角函数算出来的，到处直接调用太贵；启动时先烘一张高度场，
    // 之后地形网格 / 上色 / 种树 / 怪物选点 / AI 查询全部走它（双线性取样），比现算还快一点。
    static float[] _heightField;
    static int     _fieldRes;

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

    Mesh _terrainMesh;
    Mesh _propsMesh;
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

    /// <summary>地表高度（解析公式）：丘陵、河流、道路、城岛、护城河、龙脊雪山全在这算</summary>
    public static float HeightRaw(float x, float z)
    {
        // 二次元草原那种圆润的丘陵
        float h = 5.6f
                + 2.7f * Mathf.Sin(x * 0.034f) * Mathf.Cos(z * 0.030f)
                + 1.4f * Mathf.Sin(x * 0.071f + 2.1f) * Mathf.Sin(z * 0.061f + 0.6f)
                + 0.7f * Mathf.Sin(x * 0.13f + z * 0.11f);

        // 东北方向的山麓：从城东北一路缓缓抬向龙脊雪山（起点 380 米，免得城东地面被抬太高把东桥埋掉）
        float ridge = Mathf.Clamp01((x + z - 380f) / 260f);
        h += ridge * ridge * 46f;

        // 西边一个湖盆
        float lx = x - 85f, lz = z - 118f;
        h -= 12f * Mathf.Clamp01(1f - Mathf.Sqrt(lx * lx + lz * lz) / 56f);

        // 城南一条河：挖到水面以下
        float riverD = Mathf.Abs(z - RiverZ(x));
        if (riverD < RiverHalfWidth)
        {
            float carve = Mathf.Clamp01(1f - riverD / RiverHalfWidth);
            h = Mathf.Lerp(h, 0.8f, carve * carve);
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
        // 一条西南 → 东北走向的大山脊：七座峰叠在一起，山体铺开三百多米、主峰两百米高，
        // 从城里往东北看就是一整片压在视野上的雪山（这是参考真实雪山"一条主脊 + 若干肩峰"的做法）
        float massif = 0f;
        massif += Peak(x, z, 440f, 442f, 145f, 205f);   // 主峰（最高）
        massif += Peak(x, z, 386f, 494f, 105f, 150f);   // 西南肩峰
        massif += Peak(x, z, 500f, 378f, 100f, 158f);   // 东北肩峰
        massif += Peak(x, z, 326f, 516f,  78f, 104f);   // 脊首小峰
        massif += Peak(x, z, 534f, 338f,  66f,  96f);   // 脊尾小峰
        massif += Peak(x, z, 452f, 396f,  60f, 132f);   // 鞍部上的小峰

        if (massif > 0f)
        {
            // 山脊噪声：低频大起伏 + 高频碎石感，按山体高度收敛（山脚几乎是平的，不破坏草原）
            float mask = Mathf.Clamp01(massif / 70f);
            float a = (Mathf.PerlinNoise(x * 0.045f, z * 0.045f) - 0.5f) * 2f;
            float b = (Mathf.PerlinNoise(x * 0.115f + 11f, z * 0.115f + 7f) - 0.5f) * 2f;
            h += massif + mask * (a * 16f + b * 5f);
        }

        // 冰川谷：从主峰往西南 / 往南各切一条 U 形谷（谷底平缓、谷壁陡，谷里可以走人）
        h -= Valley(x, z, MassifPeakX, MassifPeakZ, 300f, 322f, 34f, 26f);
        h -= Valley(x, z, MassifPeakX, MassifPeakZ, 336f, 486f, 28f, 20f);

        // 冰碛垄：谷口堆一道矮垄（冰川堆积地貌）
        h += Mound(x, z, 348f, 322f, 48f, 9f);

        // 冰蚀盆地：鞍部上一个浅碗，冻住以后就是冰湖
        float fdx = x - FrozenLakeX, fdz = z - FrozenLakeZ;
        float lakeBowl = Mathf.Clamp01(1f - Mathf.Sqrt(fdx * fdx + fdz * fdz) / 30f);
        h -= lakeBowl * lakeBowl * 13f;

        // 南桥桥面下那一小段地：压到桥面以下，免得地面从桥面里冒出来（只在桥正下方那一条，看不见）
        if (Mathf.Abs(x - MondstadtTown.CenterX) < 5.5f &&
            z > MondstadtTown.SouthBridgeZ + 3f && z < MondstadtTown.SouthBridgeZ + 16f)
            h = Mathf.Min(h, 7.4f);

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

        BuildHeightField();      // 先把高度场烘好：后面的地形网格 / 上色 / 种树 / 雪山营地全都查它，快很多
        BuildTerrain();
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

    // ---------- 地形网格 ----------

    void BuildTerrain()
    {
        int n = Res + 1;
        var verts = new Vector3[n * n];
        var cols  = new Color[n * n];
        var blend = new Vector2[n * n];      // UV2：x 岩石权重 / y 沙滩权重，交给着色器混贴图

        for (int iz = 0; iz < n; iz++)
        {
            for (int ix = 0; ix < n; ix++)
            {
                float x = ix * Cell, z = iz * Cell;
                verts[iz * n + ix] = new Vector3(x, Height(x, z), z);
                cols[iz * n + ix] = TerrainColor(x, z);
                blend[iz * n + ix] = TerrainBlend(x, z);
            }
        }

        var tris = new List<int>((Res * Res) * 6);
        for (int iz = 0; iz < Res; iz++)
        {
            for (int ix = 0; ix < Res; ix++)
            {
                int i0 = iz * n + ix;
                int i1 = i0 + 1;
                int i2 = i0 + n;
                int i3 = i2 + 1;

                tris.Add(i0); tris.Add(i2); tris.Add(i1);   // 从上看是逆时针
                tris.Add(i1); tris.Add(i2); tris.Add(i3);
            }
        }

        _terrainMesh = new Mesh { name = "OpenWorldTerrain" };
        _terrainMesh.indexFormat = IndexFormat.UInt32;
        _terrainMesh.SetVertices(verts);
        _terrainMesh.SetColors(cols);
        _terrainMesh.SetUVs(1, blend);                      // UV2 = 贴图混合权重
        _terrainMesh.SetTriangles(tris, 0);
        _terrainMesh.RecalculateNormals();                  // 共享顶点 → 平滑法线
        _terrainMesh.RecalculateBounds();
        _terrainMesh.RecalculateTangents();

        var filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = _terrainMesh;

        var renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _terrainMaterial;         // 地形走带贴图的材质

        gameObject.AddComponent<MeshCollider>().sharedMesh = _terrainMesh;
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
        float sand = Mathf.SmoothStep(WaterLevel + 1.2f, WaterLevel + 0.2f, y);      // 越靠水面越像沙滩

        // 雪面上不混草地 / 沙滩贴图（保持干净的平涂白），陡面还是走岩石贴图
        if (SnowFactor(x, z) > 0.5f) sand = 0f;

        return new Vector2(rock, sand);
    }

    /// <summary>按高度和坡度决定地表颜色，并压上朝向明暗 + 一点色差。
    /// 二次元风：高饱和平涂绿 + 明显的坡面岩石色 + 湖岸一圈奶白沙，噪点压得很低，避免写实感。</summary>
    static Color TerrainColor(float x, float z)
    {
        const float d = Cell;
        Vector3 normal = new Vector3(Height(x - d, z) - Height(x + d, z), 2f * d,
                                     Height(x, z - d) - Height(x, z + d)).normalized;

        float y = Height(x, z);
        float slope = 1f - normal.y;

        // 二次元配色：饱和、干净、不写实（贴近参考图那种草原绿）
        Color grassLight = new Color32(0xa9, 0xe0, 0x6b, 255);
        Color grass      = new Color32(0x7f, 0xd0, 0x52, 255);
        Color forest     = new Color32(0x5c, 0xb4, 0x45, 255);
        Color rockLight  = new Color32(0xcd, 0xc6, 0xb5, 255);
        Color rock       = new Color32(0xac, 0xa6, 0x97, 255);
        Color sand       = new Color32(0xf2, 0xe6, 0xb8, 255);
        Color paveLight  = new Color32(0xd7, 0xd0, 0xbe, 255);
        Color pave       = new Color32(0xbd, 0xb6, 0xa4, 255);

        Color c;

        if (y < WaterLevel + 0.7f)                                       // 湖边一圈浅滩
            c = Color.Lerp(sand, grass, Mathf.InverseLerp(WaterLevel - 0.4f, WaterLevel + 0.7f, y) * 0.35f);
        else if (slope > 0.24f)                                          // 陡坡露岩
            c = Color.Lerp(rock, rockLight, Mathf.PerlinNoise(x * 0.09f, z * 0.09f));
        else if (OnTownStreet(x, z) || IsRoad(x, z))                     // 城里的石板街 + 城门外的路
            c = Color.Lerp(pave, paveLight, Mathf.PerlinNoise(x * 0.4f, z * 0.4f));
        else                                                             // 草地：成林的地方压深一点，像森林
        {
            float grove = ForestDensity(x, z);
            c = Color.Lerp(grass, grassLight, Mathf.PerlinNoise(x * 0.05f, z * 0.05f));
            c = Color.Lerp(c, forest, Mathf.SmoothStep(0.45f, 0.85f, grove) * 0.55f);
        }

        // ---------- 龙脊雪山：雪线以上压成雪，陡面留岩石色，山脚平滑过渡 ----------
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
            Color highRock  = new Color32(0x94, 0x8f, 0x89, 255);

            Color snowCol = Color.Lerp(snowShade, snowLight, Mathf.PerlinNoise(x * 0.05f, z * 0.05f));
            Color rockFace = Color.Lerp(rock, highRock, Mathf.PerlinNoise(x * 0.07f, z * 0.07f));
            Color surface = Color.Lerp(snowCol, rockFace, Mathf.SmoothStep(0.30f, 0.62f, slope));
            c = Color.Lerp(c, surface, Mathf.Clamp01(snow * 1.35f));
        }

        float lit = 0.80f + 0.20f * normal.y;                            // 面朝天更亮：只留大块柔和起伏
        c *= lit;
        c.a = 1f;
        return c.linear;
    }

    // ---------- 树和石头（全部合并成一个网格） ----------

    void BuildProps()
    {
        _verts.Clear(); _cols.Clear(); _tris.Clear();

        Vector3 spawn = SpawnPoint;

        for (int gx = 3; gx < Res - 3; gx += 2)
        {
            for (int gz = 3; gz < Res - 3; gz += 2)
            {
                float x = (gx + Hash01(gx, gz, 11)) * Cell;
                float z = (gz + Hash01(gx, gz, 12)) * Cell;
                float y = Height(x, z);

                if (IsDeepWater(x, z)) continue;                       // 不种在水里
                if (Slope(x, z) > 0.22f) continue;                     // 峭壁上不种
                if (Vector2.Distance(new Vector2(x, z), new Vector2(spawn.x, spawn.z)) < 22f) continue;   // 出生点一圈留空
                if (Vector2.Distance(new Vector2(x, z), new Vector2(MondstadtTown.CenterX, MondstadtTown.CenterZ))
                    < MondstadtTown.IslandR + 11f) continue;           // 城郊留空（城里更不可能长树）
                if (Vector2.Distance(new Vector2(x, z), new Vector2(MondstadtTown.CathedralX, MondstadtTown.CathedralZ))
                    < MondstadtTown.CathedralR + 7f) continue;         // 大教堂高台：留给教堂自己那几棵
                if (IsRoad(x, z)) continue;                            // 路上不种
                if (Mathf.Abs(x - MondstadtTown.CenterX) < 7f &&
                    z > MondstadtTown.SouthBridgeZ - 4f &&
                    z < MondstadtTown.CenterZ - MondstadtTown.WallR + 4f) continue;      // 南桥桥面
                if (Mathf.Abs(z - MondstadtTown.CenterZ) < 7f &&
                    x > MondstadtTown.CenterX + MondstadtTown.WallR - 4f &&
                    x < MondstadtTown.EastBridgeFarX + 4f) continue;                     // 东桥桥面
                if (NearDock(x, z, 7f)) continue;                                        // 木栈桥码头那一条

                // 成簇的树：低频噪声决定这一片密不密 → 大片开阔草地夹着树丛（蒙德郊外那种）
                float grove = ForestDensity(x, z);
                float roll = Hash01(gx, gz, 13);

                // 雪线附近到雪线以上：只长雪松 / 冰晶 / 雪岩，没有草原那种树（真实的树线）
                float snow = SnowFactor(x, z);
                if (snow > 0.35f)
                {
                    if (snow > 0.85f)                                   // 雪线以上：冰晶 + 雪岩
                    {
                        if (roll > 0.90f) AppendIceCrystal(x, y, z, Hash01(gx, gz, 17));
                        else if (roll > 0.80f) AppendSnowRock(x, y, z, Hash01(gx, gz, 15));
                    }
                    else                                                // 树线附近：稀疏雪松
                    {
                        if (roll < 0.45f) AppendSnowTree(x, y, z, Hash01(gx, gz, 14));
                        else if (roll > 0.90f) AppendSnowRock(x, y, z, Hash01(gx, gz, 15));
                    }
                    continue;
                }

                if (roll < grove * grove * grove * 0.6f)               // 只有噪声高的地方才成林
                    AppendTree(x, y, z, Hash01(gx, gz, 14));
                else if (roll > 0.97f)
                    AppendRock(x, y, z, Hash01(gx, gz, 15));
            }
        }

        if (_verts.Count == 0) return;

        _propsMesh = new Mesh { name = "OpenWorldProps" };
        _propsMesh.indexFormat = IndexFormat.UInt32;
        _propsMesh.SetVertices(_verts);
        _propsMesh.SetColors(_cols);
        _propsMesh.SetTriangles(_tris, 0);
        _propsMesh.RecalculateNormals();
        _propsMesh.RecalculateBounds();

        var go = new GameObject("Props");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = _propsMesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = _material;
    }

    // ---------- 龙脊雪山上的固定地标 ----------

    /// <summary>雪山上的固定地标：山脚前哨营地（木屋 + 火堆）和主峰顶的登顶石堆。
    /// 两处都记到世界地图上，照着地图就能找到雪山。</summary>
    void BuildSnowLandmarks()
    {
        _verts.Clear(); _cols.Clear(); _tris.Clear();

        Color wood   = new Color32(0x6b, 0x4a, 0x2f, 255);
        Color wood2  = new Color32(0x8a, 0x66, 0x42, 255);
        Color hide   = new Color32(0x9c, 0x7c, 0x5a, 255);
        Color stone  = new Color32(0xac, 0xa6, 0x9c, 255);
        Color stoneD = new Color32(0x8e, 0x89, 0x82, 255);
        Color snowC  = new Color32(0xf4, 0xf9, 0xff, 255);

        // 前哨营地：搭在雪山南麓缓坡上（雪线下面一点）
        Vector3 camp = new Vector3(372f, Height(372f, 356f), 356f);
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

        MondstadtTown.Markers.Add(new MondstadtTown.MapMarker { name = "雪山前哨营地", x = camp.x, z = camp.z, kind = 10 });
        MondstadtTown.Markers.Add(new MondstadtTown.MapMarker { name = "龙脊雪山主峰", x = top.x, z = top.z, kind = 11 });
        MondstadtTown.Markers.Add(new MondstadtTown.MapMarker { name = "冰湖", x = FrozenLakeX, z = FrozenLakeZ, kind = 12 });

        Debug.Log($"[OpenWorld] 龙脊雪山：主峰 {top.y:0} 米，前哨营地 {camp.y:0} 米，冰湖 ({FrozenLakeX},{FrozenLakeZ})");
    }

    /// <summary>一个盒子（顶面 / 侧面分开上色）：绕序和城里那套一致，法线朝外</summary>
    void AppendBox(Vector3 center, Vector3 size, Quaternion rot, Color top, Color side)
    {
        int baseIndex = _verts.Count;
        Vector3 hh = size * 0.5f;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? -hh.x : hh.x,
                                         (i & 2) == 0 ? -hh.y : hh.y,
                                         (i & 4) == 0 ? -hh.z : hh.z);
            _verts.Add(center + rot * corner);
            _cols.Add(((i & 2) == 0 ? side : top).linear);
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
        for (int i = 0; i < faces.Length; i++) _tris.Add(baseIndex + faces[i]);
    }

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

    /// <summary>一块石头：压扁的低模球</summary>
    void AppendRock(float x, float y, float z, float seed)
    {
        float r = 0.7f + seed * 1.1f;
        Color c = Color.Lerp(new Color32(0xa8, 0x9f, 0x8e, 255), new Color32(0xc6, 0xbd, 0xab, 255), seed);
        AppendBlob(new Vector3(x, y - r * 0.35f, z), r, c, 6, 0.72f, seed);
    }

    /// <summary>低模球（经纬度球 + 每点轻微抖动，避免看起来太圆太规整）</summary>
    void AppendBlob(Vector3 center, float radius, Color color, int segments, float squash, float seed)
    {
        int rings = Mathf.Max(3, segments / 2);
        int baseIndex = _verts.Count;

        for (int r = 0; r <= rings; r++)
        {
            float phi = (float)r / rings * Mathf.PI;
            float y = Mathf.Cos(phi) * radius * squash;
            float ring = Mathf.Sin(phi) * radius;

            for (int s = 0; s <= segments; s++)
            {
                float theta = (float)s / segments * Mathf.PI * 2f;
                float jitter = 0.88f + 0.22f * Hash01(r * 37 + s, Mathf.RoundToInt(seed * 997f), 21);

                _verts.Add(center + new Vector3(Mathf.Sin(theta) * ring, y, Mathf.Cos(theta) * ring) * jitter);
                _cols.Add(color.linear);
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

                _tris.Add(i0); _tris.Add(i2); _tris.Add(i1);
                _tris.Add(i1); _tris.Add(i2); _tris.Add(i3);
            }
        }
    }

    /// <summary>棱柱（树干）：下粗上细</summary>
    void AppendPrism(Vector3 basePos, float height, float bottomR, float topR, int sides, Color color)
    {
        int baseIndex = _verts.Count;

        for (int i = 0; i <= sides; i++)                       // 下半圈
        {
            float a = (float)i / sides * Mathf.PI * 2f;
            _verts.Add(basePos + new Vector3(Mathf.Cos(a) * bottomR, 0f, Mathf.Sin(a) * bottomR));
            _cols.Add(color.linear);
        }
        for (int i = 0; i <= sides; i++)                       // 上半圈
        {
            float a = (float)i / sides * Mathf.PI * 2f;
            _verts.Add(basePos + new Vector3(Mathf.Cos(a) * topR, height, Mathf.Sin(a) * topR));
            _cols.Add(color.linear);
        }

        for (int i = 0; i < sides; i++)
        {
            int lo0 = baseIndex + i, lo1 = lo0 + 1;
            int hi0 = baseIndex + sides + 1 + i, hi1 = hi0 + 1;

            _tris.Add(lo0); _tris.Add(hi0); _tris.Add(lo1);
            _tris.Add(lo1); _tris.Add(hi0); _tris.Add(hi1);
        }
    }

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

    /// <summary>0~1 的确定性哈希</summary>
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
}
