using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 野外怪的分布 + 丘丘人营地（铺法参考原神那种"野外三三两两、营地成组"的感觉）：
///  · 丘丘人营地：挑开阔平坦、离城 / 护城河 / 马路 / 出生点都够远的草地扎营，
///    每个营地有棚子、篝火、吊锅、木桩、图腾旗、木箱、晾架，营地周围 3~4 只丘丘人（含 1 只暴徒看家）。
///  · 史莱姆：喜欢水边和草地 —— 按"自己站在干地上、但附近有水"选点，成组刷 1~3 只（水边多水史莱姆）。
///  · 全部用固定哈希选点，每次进游戏分布完全一致；不会刷在城里、护城河、桥上和马路上。
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    const int   CampCount        = 9;     // 营地数（地图 850 米，按面积比原来 600 米多算一半）
    const int   SlimeGroupCount  = 14;    // 史莱姆组数
    const float MinCampSpacing    = 62f;  // 营地之间至少隔这么远
    const float MinSlimeSpacing   = 34f;  // 史莱姆组之间至少隔这么远

    OpenWorld _world;
    Transform _player;
    VoxelInteraction _inventory;
    Material _material;
    Transform _root;

    /// <summary>营地位置（宝箱分布要避开营地里的棚子 / 木箱，免得刷在建筑里）</summary>
    public static readonly List<Vector2> CampSites = new List<Vector2>();

    readonly List<Vector2> _campSites  = new List<Vector2>();
    readonly List<Vector2> _slimeSites = new List<Vector2>();
    readonly List<Transform> _flames   = new List<Transform>();

    int _campsBuilt, _slimesBuilt;

    public void Init(OpenWorld world, Transform player, VoxelInteraction inventory)
    {
        _world = world;
        _player = player;
        _inventory = inventory;
        _material = world.Material;

        CampSites.Clear();          // 静态列表，重开一局要清干净

        _root = new GameObject("Monsters").transform;
        _root.SetParent(transform, false);

        PickCampSites();
        PickSlimeSites();

        foreach (var site in _campSites) BuildCamp(site.x, site.y);
        foreach (var site in _slimeSites) BuildSlimeGroup(site.x, site.y);

        Debug.Log($"[MonsterSpawner] 分布完成：丘丘人营地 {_campsBuilt} 处，史莱姆 {_slimesBuilt} 组，" +
                  $"怪物总数 {VoxelMonster.All.Count}（固定哈希选点，重进游戏一致）");
    }

    void Update()
    {
        // 篝火跳动的火苗（只有营地里那几簇，很便宜）
        for (int i = 0; i < _flames.Count; i++)
        {
            var f = _flames[i];
            if (f == null) continue;

            float s = 1f + Mathf.Sin(Time.time * 7f + i) * 0.14f;
            f.localScale = new Vector3(0.55f * s, 0.75f * s, 0.55f * s);
            f.localRotation = Quaternion.Euler(0f, Time.time * 90f, 0f);
        }
    }

    // ---------- 选点 ----------

    /// <summary>野外通用选点：够干、够平、不在密林 / 城里 / 护城河 / 马路上，且离出生点有点距离</summary>
    bool Suitable(float x, float z, float minTownExtra, float minSpawnDist, float maxForest, List<Vector2> used, float minSpacing)
    {
        if (!OpenWorld.InBounds(x, z)) return false;

        if (OpenWorld.Height(x, z) < OpenWorld.WaterLevel + 2.6f) return false;     // 离水面留足余量
        if (OpenWorld.Slope(x, z) > 0.18f) return false;                             // 要平地
        if (OpenWorld.ForestDensity(x, z) > maxForest) return false;                 // 别扎在密林里
        if (MondstadtTown.IsRoad(x, z)) return false;

        // 别扎进地台的边坡过渡带（同时覆盖野外建筑和解密场地的台子）：
        // 那里是斜坡，营地摆上去会一半埋进土里
        if (OpenWorld.OnPadSlope(x, z)) return false;

        // 野外建筑的整个影响范围（占地 + 平整余量 + 边坡带）都要躲开（蒙德 + 璃月两套）
        if (WildSiteSpawner.InsideSiteArea(x, z, 6f)) return false;
        if (LiyueSiteSpawner.InsideSiteArea(x, z, 6f)) return false;
        if (PuzzleSpawner.NearPuzzle(x, z, 8f)) return false;                         // 也别扎进解密场地
        if (LiyuePuzzleSpawner.NearLiyuePuzzle(x, z, 8f)) return false;               // 璃月的石灯阵 / 石鼓同理

        // 璃月港（阶段 E）：城里是人工台地，营地的棚子按"地形高度"摆会埋进台面里 —— 整片让开
        if (LiyueHarbor.InCity(x, z)) return false;

        var p = new Vector2(x, z);
        float townDist = Vector2.Distance(p, new Vector2(MondstadtTown.CenterX, MondstadtTown.CenterZ));
        if (townDist < MondstadtTown.MoatOuter + minTownExtra) return false;          // 避开城和护城河

        Vector3 spawn = _world.SpawnPoint;
        if (Vector2.Distance(p, new Vector2(spawn.x, spawn.z)) < minSpawnDist) return false;

        foreach (var u in used)
            if (Vector2.Distance(p, u) < minSpacing) return false;

        return true;
    }

    /// <summary>收集候选点，再用"离已选点最远优先"方式挑，保证营地在大地图上铺得开</summary>
    void PickCampSites()
    {
        var candidates = new List<Vector2>();

        for (float x = 24f; x < OpenWorld.Size - 24f; x += 7f)
        {
            for (float z = 24f; z < OpenWorld.Size - 24f; z += 7f)
            {
                float jx = x + (Hash01((int)x, (int)z, 11) - 0.5f) * 5f;
                float jz = z + (Hash01((int)x, (int)z, 12) - 0.5f) * 5f;
                if (Suitable(jx, jz, 18f, 46f, 0.55f, _campSites, 0f))
                    candidates.Add(new Vector2(jx, jz));
            }
        }

        while (_campSites.Count < CampCount && candidates.Count > 0)
        {
            int bestIndex = 0;
            float bestScore = -1f;

            for (int i = 0; i < candidates.Count; i++)
            {
                float score = float.MaxValue;
                if (_campSites.Count > 0)
                {
                    score = float.MaxValue;
                    foreach (var s in _campSites)
                        score = Mathf.Min(score, Vector2.Distance(candidates[i], s));
                }

                if (score > bestScore) { bestScore = score; bestIndex = i; }
            }

            if (_campSites.Count > 0 && bestScore < MinCampSpacing) break;   // 铺不开了就少几个营地

            _campSites.Add(candidates[bestIndex]);
            candidates.RemoveAt(bestIndex);
        }
    }

    /// <summary>史莱姆选点：站的地方是干的，但附近有水（水边史莱姆）；再补几组内陆草地的</summary>
    void PickSlimeSites()
    {
        var candidates = new List<Vector2>();

        for (float x = 20f; x < OpenWorld.Size - 20f; x += 5f)
        {
            for (float z = 20f; z < OpenWorld.Size - 20f; z += 5f)
            {
                float px = x + (Hash01((int)x, (int)z, 21) - 0.5f) * 4f;
                float pz = z + (Hash01((int)x, (int)z, 22) - 0.5f) * 4f;

                float h = OpenWorld.Height(px, pz);
                if (h < OpenWorld.WaterLevel + 1.4f || h > OpenWorld.WaterLevel + 5.5f) continue;   // 太浅会淹、太远不像水边
                if (!Suitable(px, pz, 12f, 26f, 0.8f, _slimeSites, MinSlimeSpacing)) continue;
                if (!WaterNearby(px, pz, 15f)) continue;

                candidates.Add(new Vector2(px, pz));
            }
        }

        // 内陆草地也放两组（哈希挑，保证确定性）
        for (float x = 40f; x < OpenWorld.Size - 40f && candidates.Count < SlimeGroupCount + 6; x += 23f)
        {
            for (float z = 40f; z < OpenWorld.Size - 40f; z += 23f)
            {
                if (Hash01((int)x, (int)z, 31) < 0.86f) continue;
                if (Suitable(x, z, 14f, 30f, 0.7f, _slimeSites, MinSlimeSpacing))
                    candidates.Add(new Vector2(x, z));
            }
        }

        // 打散后按顺序取（用哈希排序，确定性）
        candidates.Sort((a, b) => Hash01((int)a.x, (int)a.y, 41).CompareTo(Hash01((int)b.x, (int)b.y, 41)));

        foreach (var c in candidates)
        {
            if (_slimeSites.Count >= SlimeGroupCount) break;

            bool tooClose = false;
            foreach (var s in _slimeSites)
                if (Vector2.Distance(c, s) < MinSlimeSpacing) { tooClose = true; break; }
            if (tooClose) continue;

            _slimeSites.Add(c);
        }
    }

    /// <summary>附近有没有水面（史莱姆喜欢待在水边）</summary>
    bool WaterNearby(float x, float z, float radius)
    {
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            if (OpenWorld.Height(x + Mathf.Cos(a) * radius, z + Mathf.Sin(a) * radius) < OpenWorld.WaterLevel + 0.4f)
                return true;
        }
        return false;
    }

    // ---------- 刷怪 ----------

    void SpawnMonster(VoxelMonster.Kind kind, float x, float z)
    {
        var go = new GameObject("Monster_" + kind);
        go.transform.SetParent(_root, false);
        go.transform.position = new Vector3(x, _world.SurfaceY(x, z) + 0.3f, z);
        go.transform.rotation = Quaternion.Euler(0f, Hash01((int)x, (int)z, 51) * 360f, 0f);

        var m = go.AddComponent<VoxelMonster>();
        m.Init(_material, _player, _world, _inventory, kind);
    }

    void BuildCamp(float cx, float cz)
    {
        float yaw = Hash01((int)cx, (int)cz, 61) * 360f;
        var rot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 c = new Vector3(cx, _world.SurfaceY(cx, cz), cz);

        var mesher = new Mesher();
        BuildCampProps(mesher, c, rot);

        var go = mesher.Build("HilichurlCamp", _root, _material);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);   // 顶点已经是世界坐标

        // 篝火火苗：单独一个会自动跳动的小物件
        var flame = new GameObject("Flame").transform;
        flame.SetParent(go.transform, false);
        flame.position = c + rot * new Vector3(0.4f, 0.55f, 1.1f);
        BuildFlame(flame, _material);
        _flames.Add(flame);

        // 营地里的丘丘人：1 只暴徒 + 2~3 只普通
        int normal = 2 + Mathf.RoundToInt(Hash01((int)cx, (int)cz, 71) * 1.4f);
        SpawnMonster(VoxelMonster.Kind.HilichurlBrute, cx + Mathf.Cos(yaw * Mathf.Deg2Rad) * 2.2f, cz + Mathf.Sin(yaw * Mathf.Deg2Rad) * 2.2f);

        for (int i = 0; i < normal; i++)
        {
            float a = (yaw + 70f + i * 95f) * Mathf.Deg2Rad;
            float r = 4.2f + Hash01(i, (int)cx, 81) * 3.4f;
            SpawnMonster(VoxelMonster.Kind.Hilichurl, cx + Mathf.Cos(a) * r, cz + Mathf.Sin(a) * r);
        }

        // 营地边上一两只史莱姆（原神营地旁边也常蹲着史莱姆）
        if (Hash01((int)cx, (int)cz, 91) > 0.35f)
        {
            float a = (yaw + 210f) * Mathf.Deg2Rad;
            SpawnMonster(VoxelMonster.Kind.SlimeWater, cx + Mathf.Cos(a) * 7.5f, cz + Mathf.Sin(a) * 7.5f);
        }

        MondstadtTown.Markers.Add(new MondstadtTown.MapMarker { name = "丘丘人营地", x = cx, z = cz, kind = 9 });
        CampSites.Add(new Vector2(cx, cz));
        _campsBuilt++;
    }

    /// <summary>营地摆设：棚子 + 篝火石圈 + 吊锅 + 木桩 + 图腾旗 + 木箱 + 晾架（全部木石色，合一个网格）</summary>
    void BuildCampProps(Mesher m, Vector3 c, Quaternion rot)
    {
        Color wood  = new Color32(0x6b, 0x4a, 0x2f, 255);
        Color wood2 = new Color32(0x87, 0x63, 0x40, 255);
        Color hide  = new Color32(0x8d, 0x6a, 0x4a, 255);
        Color stone = new Color32(0xa8, 0xa2, 0x93, 255);
        Color cloth = new Color32(0x9c, 0x3b, 0x33, 255);
        Color bone  = new Color32(0xe4, 0xdc, 0xc8, 255);

        // 棚子：两根前柱 + 两根后柱 + 斜屋顶 + 背板
        for (int s = -1; s <= 1; s += 2)
        {
            m.Box(c + rot * new Vector3(s * 1.7f, 1.0f, -1.6f), new Vector3(0.22f, 2.0f, 0.22f), rot, wood2, wood);
            m.Box(c + rot * new Vector3(s * 1.7f, 0.5f,  1.6f), new Vector3(0.22f, 1.0f, 0.22f), rot, wood2, wood);
        }
        m.Box(c + rot * new Vector3(0f, 2.15f, 0f), new Vector3(4.0f, 0.18f, 4.4f), rot * Quaternion.Euler(14f, 0f, 0f), hide, hide);
        m.Box(c + rot * new Vector3(0f, 1.0f, -1.75f), new Vector3(3.5f, 2.0f, 0.16f), rot, hide, wood);

        // 篝火：石圈 + 交叉的柴
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            m.Box(c + rot * new Vector3(0.4f + Mathf.Cos(a) * 0.85f, 0.14f, 1.1f + Mathf.Sin(a) * 0.85f),
                  new Vector3(0.36f, 0.28f, 0.36f), rot, stone, stone);
        }
        for (int i = 0; i < 3; i++)
            m.Box(c + rot * new Vector3(0.4f, 0.26f + i * 0.12f, 1.1f), new Vector3(0.16f, 0.16f, 1.5f),
                  rot * Quaternion.Euler(0f, i * 55f, 0f), wood, wood);

        // 吊锅：三根斜杆 + 锅 + 吊链
        Vector3 pot = c + rot * new Vector3(2.8f, 0f, 0.4f);
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.PI * 2f / 3f + 0.4f;
            Vector3 lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.55f;
            m.Box(pot + rot * new Vector3(lean.x, 0.85f, lean.z), new Vector3(0.14f, 1.8f, 0.14f),
                  rot * Quaternion.Euler(Mathf.Sin(a) * 16f, 0f, -Mathf.Cos(a) * 16f), wood2, wood);
        }
        m.Box(pot + rot * new Vector3(0f, 0.45f, 0f), new Vector3(0.14f, 0.55f, 0.14f), rot, wood, wood);
        m.Box(pot + rot * new Vector3(0f, 0.62f, 0f), new Vector3(0.8f, 0.5f, 0.8f), rot, new Color32(0x4a, 0x4a, 0x50, 255), new Color32(0x3a, 0x3a, 0x40, 255));

        // 图腾旗：杆 + 横杆 + 布 + 骨饰
        Vector3 banner = c + rot * new Vector3(-1.0f, 0f, -4.4f);
        m.Box(banner + Vector3.up * 2.1f, new Vector3(0.24f, 4.2f, 0.24f), rot, wood2, wood);
        m.Box(banner + Vector3.up * 3.6f, new Vector3(1.5f, 0.18f, 0.18f), rot, wood2, wood);
        m.Box(banner + rot * new Vector3(0.45f, 2.9f, 0f), new Vector3(0.9f, 1.5f, 0.1f), rot, cloth, cloth);
        m.Box(banner + Vector3.up * 4.35f, new Vector3(0.4f, 0.36f, 0.4f), rot, bone, bone);

        // 木桩栅栏：一排尖桩 + 一道横杆
        for (int i = 0; i < 6; i++)
        {
            float x = -5.2f + i * 0.95f;
            float h = 1.2f + Hash01(i, (int)c.x, 101) * 0.7f;
            m.Box(c + rot * new Vector3(x, h * 0.5f, 3.4f), new Vector3(0.18f, h, 0.18f), rot, wood2, wood);
        }
        m.Box(c + rot * new Vector3(-2.8f, 1.05f, 3.4f), new Vector3(5.4f, 0.16f, 0.16f), rot, wood2, wood);

        // 木箱：两个并排 + 一个叠上去
        m.Box(c + rot * new Vector3(3.7f, 0.45f, -2.7f), new Vector3(0.9f, 0.9f, 0.9f), rot, wood2, wood);
        m.Box(c + rot * new Vector3(4.7f, 0.45f, -2.4f), new Vector3(0.9f, 0.9f, 0.9f), rot * Quaternion.Euler(0f, 18f, 0f), wood2, wood);
        m.Box(c + rot * new Vector3(3.9f, 1.35f, -2.6f), new Vector3(0.8f, 0.8f, 0.8f), rot * Quaternion.Euler(0f, -12f, 0f), wood2, wood);

        // 晾架：两根杆 + 横杆 + 三条挂着的肉干
        Vector3 rack = c + rot * new Vector3(2.6f, 0f, 3.6f);
        for (int s = -1; s <= 1; s += 2)
            m.Box(rack + rot * new Vector3(s * 1.1f, 1.05f, 0f), new Vector3(0.16f, 2.1f, 0.16f), rot, wood2, wood);
        m.Box(rack + Vector3.up * 1.95f, new Vector3(2.5f, 0.14f, 0.14f), rot, wood2, wood);
        for (int i = -1; i <= 1; i++)
            m.Box(rack + rot * new Vector3(i * 0.6f, 1.45f, 0f), new Vector3(0.3f, 0.8f, 0.08f), rot, hide, hide);
    }

    /// <summary>火苗：一内一外两个小球（橙色外焰 + 亮黄内焰），由各自的分发器每帧抖动。
    /// 野外建筑的篝火（WildSiteSpawner）也用同一个火苗，放一起才是一套火</summary>
    public static void BuildFlame(Transform parent, Material material)
    {
        var outer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        outer.name = "FlameOuter";
        outer.transform.SetParent(parent, false);
        outer.transform.localScale = new Vector3(0.55f, 0.85f, 0.55f);
        BakeFlat(outer, new Color32(0xf2, 0x7a, 0x24, 255), material);
        Destroy(outer.GetComponent<Collider>());

        var inner = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        inner.name = "FlameInner";
        inner.transform.SetParent(parent, false);
        inner.transform.localScale = new Vector3(0.32f, 0.5f, 0.32f);
        inner.transform.localPosition = new Vector3(0f, -0.1f, 0f);
        BakeFlat(inner, new Color32(0xff, 0xdc, 0x6e, 255), material);
        Destroy(inner.GetComponent<Collider>());
    }

    static void BakeFlat(GameObject go, Color color, Material material)
    {
        var mesh = go.GetComponent<MeshFilter>().mesh;
        var colors = new Color[mesh.vertexCount];
        for (int i = 0; i < colors.Length; i++) colors[i] = color.linear;
        mesh.SetColors(colors);
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    void BuildSlimeGroup(float x, float z)
    {
        bool nearWater = WaterNearby(x, z, 15f);
        int count = 1 + Mathf.RoundToInt(Hash01((int)x, (int)z, 111) * 1.8f);

        for (int i = 0; i < count; i++)
        {
            float a = Hash01(i, (int)x, 121) * Mathf.PI * 2f;
            float r = 1.2f + Hash01(i, (int)z, 131) * 3.2f;
            float sx = x + Mathf.Cos(a) * r;
            float sz = z + Mathf.Sin(a) * r;

            // 水边主要是水史莱姆，内陆按哈希分草 / 火
            VoxelMonster.Kind kind;
            if (nearWater) kind = Hash01(i, (int)z, 141) > 0.25f ? VoxelMonster.Kind.SlimeWater
                                                                 : (Hash01(i, (int)z, 142) > 0.5f ? VoxelMonster.Kind.SlimeGrass : VoxelMonster.Kind.SlimeFire);
            else kind = Hash01(i, (int)z, 143) > 0.5f ? VoxelMonster.Kind.SlimeGrass : VoxelMonster.Kind.SlimeFire;

            SpawnMonster(kind, sx, sz);
        }

        _slimesBuilt++;
    }

    // ---------- 工具 ----------

    /// <summary>0~1 确定性哈希（和城里那套同一个算法，保证每次进游戏分布一样）</summary>
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

    /// <summary>把营地的一堆木石零件合成一个网格（静态物件，省 draw call，也带 MeshCollider 挡人）</summary>
    sealed class Mesher
    {
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Color> _c = new List<Color>();
        readonly List<int> _t = new List<int>();

        /// <summary>一个盒子（八顶点，绕序和城里那套一致：法线朝外）</summary>
        public void Box(Vector3 center, Vector3 size, Quaternion rot, Color top, Color side)
        {
            int baseIndex = _v.Count;
            Vector3 h = size * 0.5f;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? -h.x : h.x,
                                             (i & 2) == 0 ? -h.y : h.y,
                                             (i & 4) == 0 ? -h.z : h.z);
                _v.Add(center + rot * corner);
                _c.Add(((i & 2) == 0 ? side : top).linear);
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
            for (int i = 0; i < faces.Length; i++) _t.Add(baseIndex + faces[i]);
        }

        public GameObject Build(string name, Transform parent, Material material)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_v);
            mesh.SetColors(_c);
            mesh.SetTriangles(_t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }
    }
}
