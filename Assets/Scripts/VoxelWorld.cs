using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 体素世界：地形数据 + 只生成“裸露面”的网格 + 碰撞体。
/// 顶点色里同时烘焙了方块颜色、六个面的固定明暗、以及每格轻微色差，
/// 所以着色器不需要参与光照也能有体积感。
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class VoxelWorld : MonoBehaviour
{
    public const int SizeX = 56;
    public const int SizeY = 28;
    public const int SizeZ = 56;

    public const byte Air   = 0;
    public const byte Grass = 1;
    public const byte Dirt  = 2;
    public const byte Stone = 3;
    public const byte Sand  = 4;
    public const byte Water = 5;    // 水源（湖泊与水桶倒出的）
    public const byte Flow1 = 6;    // 离水源 1 格的流水
    public const byte Flow2 = 7;    // 离水源 2 格
    public const byte Flow3 = 8;    // 离水源 3 格（最远，不再横向铺开）

    // 提瓦特那几样：雪顶、树干、树冠（绿 / 樱花粉）、花丛
    public const byte Snow   = 9;
    public const byte Wood   = 10;
    public const byte Leaf   = 11;
    public const byte Cherry = 12;
    public const byte Flower = 13;

    // ---------- 地貌参数（想换风格改这几个数就行）----------

    /// <summary>海平面：比它矮的列灌水，岸边自动成沙滩（西南角那片浅湾）</summary>
    public const int SeaLevel = 7;

    /// <summary>岩石线：比它高的地形整成台地并露出岩壁（璃月那种层岩）</summary>
    const int RockLine = 13;

    /// <summary>雪线：再往上就是雪顶（东北角那座雪山）</summary>
    const int SnowLine = 20;

    const float WaterTickSeconds = 0.25f;   // 水每流动一格的时间

    /// <summary>水源 + 各种距离的流水</summary>
    public static bool IsWater(byte t) => t >= Water && t <= Flow3;

    readonly byte[] _blocks = new byte[SizeX * SizeY * SizeZ];

    Mesh _mesh;
    Material _material;
    MeshFilter _filter;
    MeshRenderer _renderer;
    MeshCollider _collider;

    Mesh _waterMesh;
    Material _waterMaterial;
    MeshFilter _waterFilter;
    MeshRenderer _waterRenderer;

    float _waterTimer;
    bool _waterActive;      // 方块有变动才扫描，静止的水不耗性能

    // 六个面：法线方向 / 四个角点（从外面看逆时针）/ 明暗系数
    static readonly Vector3Int[] FaceNormals =
    {
        new Vector3Int( 1,  0,  0), new Vector3Int(-1,  0,  0),
        new Vector3Int( 0,  1,  0), new Vector3Int( 0, -1,  0),
        new Vector3Int( 0,  0,  1), new Vector3Int( 0,  0, -1),
    };

    static readonly Vector3[][] FaceCorners =
    {
        new[] { new Vector3(1, 0, 1), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1) }, // +X
        new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0) }, // -X
        new[] { new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0) }, // +Y 顶
        new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) }, // -Y 底
        new[] { new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1) }, // +Z
        new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) }, // -Z
    };

    // 六个面的明暗：往动画那种干净、少对比的方向调了一档（原神的白天光照比较柔和）
    static readonly float[] FaceShade = { 0.88f, 0.83f, 1.00f, 0.62f, 0.92f, 0.80f };

    public Material VoxelMaterial => _material;

    /// <summary>出生点：世界中心地表往上 3 格</summary>
    public Vector3 SpawnPoint =>
        new Vector3(SizeX * 0.5f, SurfaceHeight(SizeX / 2, SizeZ / 2) + 3f, SizeZ * 0.5f);

    void Awake()
    {
        _filter   = GetComponent<MeshFilter>();
        _renderer = GetComponent<MeshRenderer>();
        _collider = GetComponent<MeshCollider>();

        var shader = Resources.Load<Shader>("Shaders/VoxelDemo");
        if (shader == null) shader = Shader.Find("Voxel/Demo");

        _material = new Material(shader) { name = "VoxelDemoMaterial" };
        _renderer.sharedMaterial = _material;

        var waterShader = Resources.Load<Shader>("Shaders/VoxelWater");
        if (waterShader == null) waterShader = Shader.Find("Voxel/Water");

        _waterMaterial = new Material(waterShader) { name = "VoxelWaterMaterial" };

        // 水画在单独的透明网格里，且没有碰撞体 → 人可以走进水里
        var waterGo = new GameObject("VoxelWaterMesh");
        waterGo.transform.SetParent(transform, false);
        _waterFilter   = waterGo.AddComponent<MeshFilter>();
        _waterRenderer = waterGo.AddComponent<MeshRenderer>();
        _waterRenderer.sharedMaterial = _waterMaterial;

        GenerateTerrain();
        Rebuild();
        BuildBorderWalls();
    }

    public void SetFog(Color color, float start, float end)
    {
        if (_material != null)
        {
            _material.SetColor("_FogColor", color);
            _material.SetFloat("_FogStart", start);
            _material.SetFloat("_FogEnd", end);
        }
        if (_waterMaterial != null)
        {
            _waterMaterial.SetColor("_FogColor", color);
            _waterMaterial.SetFloat("_FogStart", start);
            _waterMaterial.SetFloat("_FogEnd", end);
        }
    }

    // ---------- 方块读写 ----------

    public byte GetBlock(int x, int y, int z)
    {
        if (x < 0 || y < 0 || z < 0 || x >= SizeX || y >= SizeY || z >= SizeZ) return Air;
        return _blocks[Index(x, y, z)];
    }

    public void SetBlock(int x, int y, int z, byte value)
    {
        if (x < 0 || y < 0 || z < 0 || x >= SizeX || y >= SizeY || z >= SizeZ) return;
        _blocks[Index(x, y, z)] = value;
        _waterActive = true;    // 方块变了，水可能要重新流动
    }

    static int Index(int x, int y, int z) => (y * SizeZ + z) * SizeX + x;

    /// <summary>某一列的地表高度：从上往下找第一个实心方块，返回它上面一格的 y（水不算地面）</summary>
    public int GroundHeight(int x, int z)
    {
        for (int y = SizeY - 1; y >= 0; y--)
        {
            byte t = GetBlock(x, y, z);
            if (t != Air && !IsWater(t)) return y + 1;
        }
        return 1;
    }

    // ---------- 地形 ----------

    /// <summary>
    /// 高度场：草原的圆润丘陵 + 东南角抬成台地（璃月层岩）+ 东北角一座雪峰 + 西南角一片浅湾。
    /// 全是固定函数，没有随机，所以每次进游戏地图都一样。
    /// </summary>
    int SurfaceHeight(int x, int z) => SurfaceHeight(x, z, out _);

    /// <summary>高度；顺带用 rock 告诉调用方这一列有多"岩"（台地 / 雪山那两片才露岩壁）</summary>
    int SurfaceHeight(int x, int z, out float rock)
    {
        // 草原的圆润起伏
        float hills = 3.0f * Mathf.Sin(x * 0.15f) * Mathf.Cos(z * 0.14f)
                    + 1.4f * Mathf.Sin(x * 0.36f + 1.3f) * Mathf.Sin(z * 0.30f + 0.7f)
                    + 0.9f * Mathf.Cos(x * 0.08f + z * 0.10f);

        // 三块地貌的权重（离得越近越接近 1）
        float plateau = RegionWeight(x, z, 47f, 47f, 22f);   // 东南：璃月式台地
        float peak    = RegionWeight(x, z, 45f, 10f, 14f);   // 东北：雪山
        float bay     = RegionWeight(x, z, 9f, 47f, 18f);    // 西南：浅湾

        rock = Mathf.Max(plateau, peak * peak);

        float h = 10f + hills * (1f - 0.7f * plateau)
                       + 8f * plateau
                       + 16f * peak * peak
                       - 9f * bay;

        // 只有台地 / 雪山那两片整成三格一级的台阶；草原的起伏保持圆润，不会冒出孤零零的岩柱
        if (h > RockLine && rock > 0.02f) h = RockLine + Mathf.Round((h - RockLine) / 3f) * 3f;

        return Mathf.Clamp(Mathf.RoundToInt(h), 2, SizeY - 3);
    }

    /// <summary>离 (cx,cz) 越近越接近 1 的平滑权重，用来把地图切成几块地貌</summary>
    static float RegionWeight(int x, int z, float cx, float cz, float radius)
    {
        float dx = x - cx, dz = z - cz;
        return Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dz * dz) / radius);
    }

    void GenerateTerrain()
    {
        for (int x = 0; x < SizeX; x++)
        {
            for (int z = 0; z < SizeZ; z++)
            {
                int h = SurfaceHeight(x, z, out float rock);

                // 紧挨着水面的那一圈铺沙滩（离水远一点的洼地还是草地）
                bool beach = h <= SeaLevel + 1 && RegionWeight(x, z, 9f, 47f, 18f) > 0.30f;

                for (int y = 0; y <= h; y++)
                {
                    byte t;

                    if (y < h)
                    {
                        // 地表下两格是土；岩壁区域直接是石头
                        t = (y >= h - 2 && h <= RockLine) ? Dirt : Stone;
                    }
                    else if (h >= SnowLine) t = Snow;                 // 雪顶
                    else if (h > RockLine && rock > 0.05f) t = Stone; // 台地露出来的岩壁
                    else if (beach)         t = Sand;
                    else                    t = Grass;

                    SetBlock(x, y, z, t);
                }

                // 海平面以下灌水：浅湾和低洼地自动变成水面
                for (int y = h + 1; y <= SeaLevel; y++) SetBlock(x, y, z, Water);
            }
        }

        GenerateLakes();
        PlantScenery();
    }

    /// <summary>在草地中低处挖几个碗形水潭并灌满水；固定种子，位置每次一致</summary>
    void GenerateLakes()
    {
        var rng = new System.Random(1337);
        var centers = new List<Vector2Int>();

        for (int attempt = 0; attempt < 600 && centers.Count < 4; attempt++)
        {
            int cx = rng.Next(4, SizeX - 4);
            int cz = rng.Next(4, SizeZ - 4);
            int h = SurfaceHeight(cx, cz);
            if (h < SeaLevel + 2 || h > 12) continue;     // 只在草地中低处挖（海边本来就有水了）
            if (IsNearSpawn(cx, cz, 7f)) continue;        // 出生点旁边留出来

            bool tooClose = false;                        // 湖和湖之间留点距离
            for (int i = 0; i < centers.Count; i++)
            {
                if (Mathf.Abs(cx - centers[i].x) + Mathf.Abs(cz - centers[i].y) < 10)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            float radius = 2.8f + (float)rng.NextDouble() * 1.7f;
            int waterLevel = h - 1;                       // 水面比周围地表低一格

            for (int dx = -4; dx <= 4; dx++)
            {
                for (int dz = -4; dz <= 4; dz++)
                {
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > radius) continue;

                    int x = cx + dx, z = cz + dz;
                    int colH = SurfaceHeight(x, z);
                    int depth = Mathf.Max(1, Mathf.RoundToInt((1f - d / radius) * 3.4f));  // 中心最深
                    int bottom = Mathf.Max(1, colH - depth);

                    for (int y = colH; y >= bottom; y--)  // 先挖出碗
                        SetBlock(x, y, z, Air);

                    // 周围地表比水面高的柱子才灌水，保证湖水不外溢
                    if (colH > waterLevel)
                        for (int y = bottom; y <= waterLevel; y++)
                            SetBlock(x, y, z, Water);
                    else if (d > radius && d <= radius + 1.2f)
                        SetBlock(x, colH, z, Sand);       // 岸边铺一圈沙，像个小水潭
                }
            }

            centers.Add(new Vector2Int(cx, cz));
        }

        if (centers.Count > 0)
            Debug.Log("[VoxelDemo] 在沙地里生成了 " + centers.Count + " 个湖泊：" + string.Join("  ", centers));
    }

    // ---------- 绿化：树和花丛 ----------

    /// <summary>在草地上撒树和花丛：稀疏、按格点撒（不然会叠成一坨），出生点附近留一片空地</summary>
    void PlantScenery()
    {
        for (int x = 2; x < SizeX - 2; x++)
        {
            for (int z = 2; z < SizeZ - 2; z++)
            {
                int h = SurfaceHeight(x, z);
                if (GetBlock(x, h, z) != Grass) continue;      // 只在草地上种
                if (IsNearSpawn(x, z, 6f)) continue;           // 出生点附近留空

                float roll = Hash01(x, z, 17);

                // 树：隔格撒，绿树里混三成樱花
                if (roll < 0.30f && x % 2 == 0 && z % 2 == 0)
                    PlantTree(x, h + 1, z, Hash01(x, z, 91) < 0.35f);
                else if (roll > 0.82f)
                    SetBlock(x, h + 1, z, Flower);             // 花丛
            }
        }
    }

    /// <summary>一棵树：树干 3~5 格 + 一团圆乎乎的树冠（cherry = 樱花）</summary>
    void PlantTree(int x, int y, int z, bool cherry)
    {
        int trunk = 3 + Mathf.RoundToInt(Hash01(x, z, 5) * 2f);
        byte leaf = cherry ? Cherry : Leaf;

        for (int i = 0; i < trunk; i++) SetBlock(x, y + i, z, Wood);

        // 树冠：以树干顶端为中心的球（纵向压扁一点），太尖的角切掉
        int top = y + trunk;
        float radius = cherry ? 2.3f : 3.0f;
        int r = Mathf.CeilToInt(radius);

        for (int dx = -r; dx <= r; dx++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    float d = Mathf.Sqrt(dx * dx + dz * dz + dy * dy * 1.4f);
                    if (d > radius) continue;

                    SetBlock(x + dx, top + dy, z + dz, leaf);
                }
            }
        }
    }

    /// <summary>把这一列地表之上的东西清掉（树、花）：NPC 生成前用一下，免得长在树里</summary>
    public bool ClearAboveGround(int x, int z)
    {
        if (x < 0 || z < 0 || x >= SizeX || z >= SizeZ) return false;

        bool changed = false;
        for (int y = GroundHeight(x, z); y < SizeY; y++)
        {
            byte t = GetBlock(x, y, z);
            if (t == Air || IsWater(t)) continue;

            SetBlock(x, y, z, Air);
            changed = true;
        }
        return changed;
    }

    /// <summary>0~1 的确定性哈希：同一个坐标每次得到同一个值</summary>
    static float Hash01(int x, int z, int salt)
    {
        unchecked
        {
            int n = x * 73856093 ^ z * 19349663 ^ salt * 83492791;
            n = (n ^ (n >> 13)) * 1274126177;
            uint u = (uint)(n ^ (n >> 16));
            return u / 4294967295f;
        }
    }

    /// <summary>离出生点（世界中心）还不到 radius 格</summary>
    static bool IsNearSpawn(int x, int z, float radius)
    {
        float dx = x - SizeX * 0.5f, dz = z - SizeZ * 0.5f;
        return dx * dx + dz * dz < radius * radius;
    }

    /// <summary>地图四周加一圈看不见的墙，免得走出边缘摔进虚空</summary>
    void BuildBorderWalls()
    {
        const float thickness = 1f;
        float height = SizeY + 12f;      // 飞行也翻不出去

        // 四面墙正好贴着世界外沿（世界对象在原点，这里用局部坐标）
        MakeWall(new Vector3(-thickness * 0.5f, height * 0.5f, SizeZ * 0.5f), new Vector3(thickness, height, SizeZ + thickness * 2f));
        MakeWall(new Vector3(SizeX + thickness * 0.5f, height * 0.5f, SizeZ * 0.5f), new Vector3(thickness, height, SizeZ + thickness * 2f));
        MakeWall(new Vector3(SizeX * 0.5f, height * 0.5f, -thickness * 0.5f), new Vector3(SizeX + thickness * 2f, height, thickness));
        MakeWall(new Vector3(SizeX * 0.5f, height * 0.5f, SizeZ + thickness * 0.5f), new Vector3(SizeX + thickness * 2f, height, thickness));
    }

    void MakeWall(Vector3 center, Vector3 size)
    {
        var wall = new GameObject("VoxelBorderWall");
        wall.transform.SetParent(transform, false);
        var box = wall.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
    }

    // ---------- 水的流动 ----------

    void Update()
    {
        if (!_waterActive) return;

        _waterTimer += Time.deltaTime;
        if (_waterTimer < WaterTickSeconds) return;
        _waterTimer = 0f;

        if (SimulateWater())
            RebuildWater();          // 只动水，不用重建实体网格
        else
            _waterActive = false;    // 一轮下来没有变化 → 静水，停止扫描
    }

    /// <summary>
    /// 一轮流动：先向下（下落水距离重置为 1），落不了才横向铺开，离水源最多 3 格。
    /// 流水必须有供给（上方有水、或旁边有离水源更近的水），水源被方块堵住后，下游逐圈干涸。
    /// 改动先攒起来，扫完统一生效，防止一格水在同一轮里连滚好几格。
    /// </summary>
    bool SimulateWater()
    {
        Dictionary<int, byte> changes = null;

        for (int y = 0; y < SizeY; y++)
        {
            for (int z = 0; z < SizeZ; z++)
            {
                for (int x = 0; x < SizeX; x++)
                {
                    byte t = _blocks[Index(x, y, z)];
                    if (!IsWater(t)) continue;

                    // 水源（湖、水桶倒出的）永远不干；流水断了供给就标记消失
                    if (t > Water && !IsSupported(x, y, z, t))
                    {
                        Mark(ref changes, x, y, z, Air);
                        continue;                 // 快干的水这一轮不再向外铺
                    }

                    // 下面是空气 → 先往下流（世界最底层当实心）
                    if (y > 0 && GetBlock(x, y - 1, z) == Air)
                    {
                        Mark(ref changes, x, y - 1, z, Flow1);
                        continue;
                    }

                    // 横向铺开：铺出去的水比自己离水源更远一格；Flow3 到头不再铺
                    if (t < Flow3)
                    {
                        byte next = (byte)(t + 1);
                        if (GetBlock(x + 1, y, z) == Air) Mark(ref changes, x + 1, y, z, next);
                        if (GetBlock(x - 1, y, z) == Air) Mark(ref changes, x - 1, y, z, next);
                        if (GetBlock(x, y, z + 1) == Air) Mark(ref changes, x, y, z + 1, next);
                        if (GetBlock(x, y, z - 1) == Air) Mark(ref changes, x, y, z - 1, next);
                    }
                }
            }
        }

        if (changes == null) return false;

        foreach (var kv in changes)
            _blocks[kv.Key] = kv.Value;
        return true;
    }

    /// <summary>流水是否还有供给：上方有水（落水柱），或四邻有离水源更近的水</summary>
    bool IsSupported(int x, int y, int z, byte self)
    {
        if (IsWater(GetBlock(x, y + 1, z))) return true;
        return HasFeeder(x + 1, y, z, self) || HasFeeder(x - 1, y, z, self) ||
               HasFeeder(x, y, z + 1, self) || HasFeeder(x, y, z - 1, self);
    }

    bool HasFeeder(int x, int y, int z, byte self)
    {
        byte nb = GetBlock(x, y, z);
        return nb >= Water && nb < self;
    }

    static void Mark(ref Dictionary<int, byte> changes, int x, int y, int z, byte value)
    {
        if (x < 0 || y < 0 || z < 0 || x >= SizeX || y >= SizeY || z >= SizeZ) return;

        if (changes == null)
        {
            changes = new Dictionary<int, byte>(64);
            changes.Add(Index(x, y, z), value);
            return;
        }

        // 同一格被几个方向浇到时，保留离水源更近（值更小）的那份
        if (changes.TryGetValue(Index(x, y, z), out byte old))
        {
            if (value < old) changes[Index(x, y, z)] = value;
        }
        else
        {
            changes.Add(Index(x, y, z), value);
        }
    }

    // ---------- 网格重建 ----------

    /// <summary>只在方块被挖掉/放下后调用，不要每帧调；会同时重建水体网格</summary>
    public void Rebuild()
    {
        RebuildSolid();
        RebuildWater();
    }

    void RebuildSolid()
    {
        var verts = new List<Vector3>();
        var cols  = new List<Color>();
        var tris  = new List<int>();

        for (int y = 0; y < SizeY; y++)
        {
            for (int z = 0; z < SizeZ; z++)
            {
                for (int x = 0; x < SizeX; x++)
                {
                    byte t = GetBlock(x, y, z);
                    if (t == Air || IsWater(t)) continue;    // 水画在单独的透明网格里

                    float jitter = Jitter(x, y, z);

                    for (int f = 0; f < 6; f++)
                    {
                        Vector3Int n = FaceNormals[f];
                        // 邻居是实心就不生成这个面；水的另一侧照画，隔着水能看见地形
                        byte nb = GetBlock(x + n.x, y + n.y, z + n.z);
                        if (nb != Air && !IsWater(nb)) continue;

                        // 颜色在 gamma 空间算好（和网页版一致），再转 linear 交给管线
                        Color c = FaceColor(t, f, x, y, z) * (FaceShade[f] * jitter);
                        c.a = 1f;
                        c = c.linear;

                        int baseIndex = verts.Count;   // 这个面的起始顶点
                        Vector3[] corners = FaceCorners[f];
                        for (int i = 0; i < 4; i++)
                        {
                            Vector3 p = corners[i];
                            verts.Add(new Vector3(x + p.x, y + p.y, z + p.z));
                            cols.Add(c);
                        }
                        // 一个四边形拆成两个三角形
                        tris.Add(baseIndex + 0); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
                        tris.Add(baseIndex + 0); tris.Add(baseIndex + 2); tris.Add(baseIndex + 3);
                    }
                }
            }
        }

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "VoxelMesh" };
            _filter.sharedMesh = _mesh;
        }

        _mesh.Clear();
        // 顶点数可能超过 65535，必须用 32 位索引
        _mesh.indexFormat = IndexFormat.UInt32;
        _mesh.SetVertices(verts);
        _mesh.SetColors(cols);
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateNormals();   // 每个面独享顶点 → 逐面法线，方块感
        _mesh.RecalculateBounds();

        // 先置空再赋值，强制 MeshCollider 重新烘焙
        _collider.sharedMesh = null;
        _collider.sharedMesh = _mesh;
    }

    /// <summary>水体网格：只画贴着空气的面，半透明、无碰撞体</summary>
    void RebuildWater()
    {
        var verts = new List<Vector3>();
        var cols  = new List<Color>();
        var tris  = new List<int>();

        for (int y = 0; y < SizeY; y++)
        {
            for (int z = 0; z < SizeZ; z++)
            {
                for (int x = 0; x < SizeX; x++)
                {
                    byte t = GetBlock(x, y, z);
                    if (!IsWater(t)) continue;

                    float jitter = Jitter(x, y, z);

                    for (int f = 0; f < 6; f++)
                    {
                        Vector3Int n = FaceNormals[f];
                        if (GetBlock(x + n.x, y + n.y, z + n.z) != Air) continue;   // 水贴着实心/水不用画

                        Color c = FaceColor(t, f, x, y, z) * (FaceShade[f] * jitter);
                        c.a = 0.68f;
                        c = c.linear;

                        int baseIndex = verts.Count;
                        Vector3[] corners = FaceCorners[f];
                        for (int i = 0; i < 4; i++)
                        {
                            Vector3 p = corners[i];
                            verts.Add(new Vector3(x + p.x, y + p.y, z + p.z));
                            cols.Add(c);
                        }
                        tris.Add(baseIndex + 0); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
                        tris.Add(baseIndex + 0); tris.Add(baseIndex + 2); tris.Add(baseIndex + 3);
                    }
                }
            }
        }

        if (_waterMesh == null)
        {
            _waterMesh = new Mesh { name = "VoxelWaterMesh" };
            _waterFilter.sharedMesh = _waterMesh;
        }

        _waterMesh.Clear();
        _waterMesh.indexFormat = IndexFormat.UInt32;
        _waterMesh.SetVertices(verts);
        _waterMesh.SetColors(cols);
        _waterMesh.SetTriangles(tris, 0);
        _waterMesh.RecalculateNormals();
        _waterMesh.RecalculateBounds();
    }

    // ---------- 颜色与色差 ----------

    /// <summary>方块颜色（提瓦特配色：饱和的草绿、青绿的水、奶油沙滩、暖灰岩、樱花粉）</summary>
    static Color FaceColor(byte t, int f, int x, int y, int z)
    {
        if (t == Grass)
        {
            if (f == 2) return new Color32(0x7f, 0xc9, 0x4f, 255);   // 顶：明亮草绿
            if (f == 3) return new Color32(0x6b, 0x8f, 0x3f, 255);   // 底：草根
            return new Color32(0x62, 0xa8, 0x3e, 255);               // 侧面：略深的绿
        }
        if (t == Dirt)   return new Color32(0x9c, 0x74, 0x4c, 255);  // 暖棕的土
        if (t == Stone)  return new Color32(0xb3, 0xaa, 0x99, 255);  // 璃月那种暖灰岩
        if (t == Sand)   return new Color32(0xe9, 0xdd, 0xb2, 255);  // 奶油色沙滩
        if (t == Snow)   return new Color32(0xf3, 0xf7, 0xff, 255);
        if (t == Wood)   return new Color32(0x7b, 0x5b, 0x3d, 255);
        if (t == Leaf)   return new Color32(0x4f, 0xa8, 0x46, 255);
        if (t == Cherry) return new Color32(0xf4, 0xa6, 0xc6, 255);  // 樱花粉

        if (t == Flower)
        {
            if (f != 2) return new Color32(0x63, 0xa8, 0x44, 255);   // 侧底还是叶子绿

            // 顶面按位置挑花色：一片草地里能开出黄 / 粉 / 白三种
            float pick = Hash01(x, z, 7);
            if (pick < 0.34f) return new Color32(0xff, 0xd6, 0x6b, 255);
            if (pick < 0.67f) return new Color32(0xf7, 0xa8, 0xc8, 255);
            return new Color32(0xf6, 0xf2, 0xe6, 255);
        }

        if (IsWater(t)) return new Color32(0x45, 0xb7, 0xd4, 255);   // 青绿的水（网格与 HUD 共用）
        return Color.magenta;
    }

    /// <summary>给 HUD 用的方块代表色（取顶面颜色）</summary>
    public static Color UiColor(byte type) => FaceColor(type, 2, 0, 0, 0);

    /// <summary>每格 ±6% 的确定性色差，避免大片死平</summary>
    static float Jitter(int x, int y, int z)
    {
        unchecked
        {
            int n = x * 374761393 + y * 668265263 + z * 1274126177;
            n = (n ^ (n >> 13)) * 1274126177;
            uint u = (uint)(n ^ (n >> 16));
            return 0.94f + (u / 4294967295f) * 0.12f;
        }
    }
}
