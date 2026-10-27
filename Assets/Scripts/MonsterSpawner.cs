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
        Color wood = new Color32(101, 68, 43, 255), lightWood = new Color32(142, 107, 65, 255);
        Color hide = new Color32(145, 111, 70, 255), rope = new Color32(190, 165, 111, 255);
        Color stone = new Color32(142, 139, 125, 255), dark = new Color32(46, 41, 35, 255);
        Color bone = new Color32(223, 211, 177, 255), paint = new Color32(151, 57, 38, 255);
        Vector3 Q(float x, float y, float z) => c + rot * new Vector3(x, y, z);
        void Log(float x, float z, float height, float lean)
        {
            m.Log(Q(x, 0, z), Q(x + lean, height, z + .1f), .14f, wood);
            foreach (float y in new[] { .45f, height - .22f })
                m.Cylinder(Q(x + lean * y / height, y, z + .1f * y / height), .1f, .165f, .165f, rope);
        }
        // A-frame hide shelter: visible roof ribs, four grounded posts and a raised ridge.
        foreach (float z in new[] { -4.1f, -.6f })
        {
            Log(-2.25f, z, 2.05f, .12f); Log(2.25f, z, 2.05f, -.12f);
            m.Log(Q(-2.55f, 1.94f, z), Q(0, 3.28f, z), .12f, lightWood);
            m.Log(Q(0, 3.28f, z), Q(2.55f, 1.94f, z), .12f, lightWood);
        }
        m.Log(Q(0, 3.28f, -4.25f), Q(0, 3.28f, -.35f), .15f, wood);
        for (int side = -1; side <= 1; side += 2)
        {
            m.Log(Q(side * 2.25f, .25f, -4.1f), Q(side * 2.15f, 1.9f, -.6f), .1f, wood);
            for (int strip = 0; strip < 4; strip++)
            {
                float a = strip / 4f, b = (strip + 1f) / 4f;
                float ya = 3.23f - Mathf.Pow(a, 1.3f) * 1.3f, yb = 3.23f - Mathf.Pow(b, 1.3f) * 1.3f;
                Color cloth = Color.Lerp(hide, lightWood, strip % 2 == 0 ? .05f : .23f);
                m.Panel(Q(side * a * 2.6f, ya, -4.2f), Q(side * a * 2.6f, ya, -.45f),
                    Q(side * b * 2.6f, yb, -.45f), Q(side * b * 2.6f, yb, -4.2f), cloth);
                m.Log(Q(side * b * 2.6f, yb + .035f, -4.2f), Q(side * b * 2.6f, yb + .035f, -.45f), .025f, rope);
            }
        }
        // Uneven back planks; front and sides stay open for combat navigation.
        for (int plank = -5; plank <= 5; plank++)
        {
            float h = 1.25f + (plank % 3 + 3) * .12f;
            m.Box(Q(plank * .38f, h * .5f, -4.03f), new Vector3(.34f, h, .12f), rot, lightWood, wood);
        }
        // Preserve the original flame position. Stone hearth and hanging rounded cauldron share it.
        for (int rock = 0; rock < 10; rock++)
        {
            float a = rock * Mathf.PI * 2 / 10;
            m.Ball(Q(.4f + Mathf.Cos(a) * .85f, .16f, 1.1f + Mathf.Sin(a) * .85f), .21f, stone);
        }
        for (int leg = 0; leg < 3; leg++)
        {
            float a = leg * Mathf.PI * 2 / 3;
            m.Log(Q(.4f + Mathf.Cos(a) * 1.05f, 0, 1.1f + Mathf.Sin(a) * 1.05f), Q(.4f, 2.05f, 1.1f), .07f, wood);
        }
        m.Log(Q(.4f, 2.05f, 1.1f), Q(.4f, 1.3f, 1.1f), .025f, dark);
        m.Ball(Q(.4f, 1.03f, 1.1f), .38f, dark);
        m.Ring(Q(.4f, 1.25f, 1.1f), .27f, .39f, Quaternion.identity, stone);
        // Bound timber stakes form a short barricade, leaving both ends and the centre accessible.
        for (int stake = 0; stake < 7; stake++)
        {
            float x = -5 + stake * .57f, h = 1.35f + (stake % 3) * .17f;
            Log(x, 3.9f, h, (stake % 2 == 0 ? 1 : -1) * .1f);
            m.Cylinder(Q(x + (stake % 2 == 0 ? .1f : -.1f), h, 4), .23f, .12f, .015f, lightWood);
        }
        m.Log(Q(-5.2f, .85f, 3.85f), Q(-1.4f, .85f, 3.85f), .08f, wood);
        // Storage barrels with hoops, and a drying rack next to the cooking area.
        for (int barrel = 0; barrel < 2; barrel++)
        {
            float x = 3.3f + barrel * 1.05f;
            m.Cylinder(Q(x, 0, -2.9f), .95f, .43f, .39f, lightWood);
            foreach (float y in new[] { .15f, .74f }) m.Cylinder(Q(x, y, -2.9f), .075f, .45f, .45f, dark);
        }
        Log(2.1f, 3.5f, 2.1f, -.08f); Log(4.3f, 3.5f, 2.1f, .08f);
        m.Log(Q(1.9f, 2.05f, 3.5f), Q(4.5f, 2.05f, 3.5f), .09f, wood);
        for (int item = 0; item < 3; item++)
        {
            float x = 2.6f + item * .6f;
            m.Log(Q(x, 2, 3.5f), Q(x, 1.65f, 3.5f), .018f, rope);
            m.Ball(Q(x, 1.45f, 3.5f), .2f, hide);
        }
        // A carved mask on a supported totem gives the camp its own focal point.
        Log(-3.5f, -1.7f, 3f, .05f);
        m.Ball(Q(-3.45f, 2.45f, -1.58f), .4f, bone);
        foreach (int side in new[] { -1, 1 })
        {
            m.Ball(Q(-3.45f + side * .13f, 2.5f, -1.23f), .065f, dark);
            m.Log(Q(-3.45f + side * .2f, 2.65f, -1.58f), Q(-3.45f + side * .38f, 2.98f, -1.58f), .055f, bone);
        }
        m.Box(Q(-3.45f, 2.65f, -1.2f), new Vector3(.065f, .2f, .04f), rot, paint, paint);
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

        public void Ball(Vector3 centre, float radius, Color color)
            => LowPoly.Sphere(_v, _c, _t, centre, radius, color, 10);
        public void Cylinder(Vector3 position, float height, float radius, float top, Color color)
            => LowPoly.Cylinder(_v, _c, _t, position, height, radius, top, 8, color, true);
        public void Ring(Vector3 position, float inner, float outer, Quaternion rotation, Color color)
            => LowPoly.Ring(_v, _c, _t, position, inner, outer, 16, rotation, color);
        public void Log(Vector3 start, Vector3 end, float radius, Color color)
        {
            int first = _v.Count;
            Vector3 direction = end - start;
            LowPoly.Cylinder(_v, _c, _t, Vector3.zero, direction.magnitude, radius, radius * .8f, 7, color, true);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            for (int i = first; i < _v.Count; i++) _v[i] = start + rotation * _v[i];
        }
        public void Panel(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            if (Vector3.Cross(b - a, c - a).y < 0) { var swap = b; b = d; d = swap; }
            Vector3[] points = { a, b, c, d };
            Vector3 offset = Vector3.down * .055f;
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color shade)
            {
                int start = _v.Count;
                _v.AddRange(new[] { p0, p1, p2, p3 });
                for (int i = 0; i < 4; i++) _c.Add(shade.linear);
                _t.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            Quad(a, b, c, d, color);
            Quad(d + offset, c + offset, b + offset, a + offset, color * .8f);
            for (int i = 0; i < 4; i++)
                Quad(points[i], points[i] + offset, points[(i + 1) % 4] + offset, points[(i + 1) % 4], color * .9f);
        }

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
