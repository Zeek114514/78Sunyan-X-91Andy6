using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 稻妻内容的规划与落地（几何在 <see cref="InazumaBuildings"/> 里）。
///
/// 【和蒙德 / 璃月的那两套不一样的地方】
///   蒙德和璃月是"选点"——拿地形的各种指标去扫，找出能盖房子的地方（那边地形是自然生成的）。
///   稻妻反过来：岛是我们自己堆出来的，主要建筑的位置**在设计地形的时候就定了**
///   （将军府在高台上、城下町在坡上、港口在西岸的小湾里、神社和五重塔各占一座离岛）。
///   所以这里不用扫描选点，直接把这张布局表铺出来 —— 位置全是有意摆的，改起来一眼能看懂。
///
/// 【两个阶段】
///   · <see cref="PlanIsles"/>（烘高度场之前）：登记地台（城下町 / 港口 / 离岛的平地）和禁植区。
///     地台必须抢在烘高度场之前登记，否则台子是"浮"在烘好的地形上的。
///   · <see cref="Init"/>（所有生成器之后）：把几何拼成一个网格，登记地图色块 / 街线 / 地标，
///     再在村里放几个 NPC。
/// </summary>
public class InazumaSiteSpawner : MonoBehaviour
{
    /// <summary>一处稻妻建筑：位置是**世界坐标**（岛是手摆的，不绕局部坐标系）</summary>
    public struct Site
    {
        public InazumaKind kind;
        public float x, z;
        public float y;          // 0 = 用地形高度；> 0 = 固定高度（码头那种）
        public float yaw;
        public float variant;
        public float param;      // 少数几种要的额外尺寸（木桥用它传半跨）
        public float radius;     // 占地半径
        public string name;
    }

    public static readonly List<Site> Sites = new List<Site>();

    /// <summary>将军府前广场（七天神像 · 雷）的落点 —— 传送锚点 / 神像都用它</summary>
    public static Vector3 StatuePoint => new Vector3(InazumaIsles.CastleX, InazumaIsles.CastleY, InazumaIsles.CastleZ + 30f);

    // 各块人工台地的高度：城下町一整块（在将军府高台南边的坡上）/ 港口码头 / 两座离岛 / 山脚村子
    const float TownY = 21.0f, QuayY = 6.6f;
    const float ShrineIsleY = 12.5f, SpireIsleY = 26.0f, MountainVillageY = 29.0f;

    // ================= 规划（烘高度场之前） =================

    /// <summary>登记地台 + 禁植区。★ 必须排在 OpenWorld.BuildHeightField 之前（见 WildSiteSpawner.PlanEverything）</summary>
    public static void PlanIsles()
    {
        // ---------- 地台：城下町一整块 + 港口 + 两座离岛 + 山脚村子 ----------
        // ★ 城下町用**一整块**台地，不切成两级：两级台地的过渡带会互相压（后登记的那块把前一块
        //   往自己高度拉），台面上就会出现一条斜带 —— 站在那儿的东西全歪，锚点还会被判"脚下不平"。
        //   台地中心压得离将军府高台足够远（101 米），免得把高台也一起压平
        OpenWorld.AddPad(InazumaIsles.CastleX, InazumaIsles.CastleZ - 92f, 70f, TownY, 18f);                                    // 城下町
        OpenWorld.AddPad(InazumaIsles.PortX, InazumaIsles.PortZ + 50f, 40f, QuayY, 14f);               // 港口码头
        OpenWorld.AddPad(InazumaIsles.ShrineX, InazumaIsles.ShrineZ + 6f, 40f, ShrineIsleY, 16f);      // 神社离岛
        OpenWorld.AddPad(InazumaIsles.SpireX, InazumaIsles.SpireZ + 4f, 24f, SpireIsleY, 12f);         // 岩柱离岛（塔）
        OpenWorld.AddPad(1548f, 1662f, 34f, MountainVillageY, 16f);                                    // 鸣神山脚村子

        // ---------- 禁植区：城里 / 港口 / 离岛不长树石（樱花是我们自己摆的） ----------
        OpenWorld.AddKeepOut(InazumaIsles.CastleX, InazumaIsles.CastleZ - 60f, 110f);
        OpenWorld.AddKeepOut(InazumaIsles.PortX, InazumaIsles.PortZ + 44f, 62f);
        OpenWorld.AddKeepOut(InazumaIsles.ShrineX, InazumaIsles.ShrineZ + 4f, 58f);
        OpenWorld.AddKeepOut(InazumaIsles.SpireX, InazumaIsles.SpireZ + 4f, 44f);
        OpenWorld.AddKeepOut(1548f, 1662f, 50f);

        BuildSites();
    }

    /// <summary>稻妻的布局表：将军府城 → 城下町 → 港口 → 离岛 → 山脚村子。
    /// 位置是设计地形时定好的，不扫描选点；改布局直接改这张表</summary>
    static void BuildSites()
    {
        Sites.Clear();

        float cx = InazumaIsles.CastleX, cz = InazumaIsles.CastleZ, cy = InazumaIsles.CastleY;

        // ---------- ① 将军府（高台上） ----------
        Add(InazumaKind.Tenshu, cx, cz + 16f, cy, 180f, 0.65f);

        // 高台前广场：三道鸟居 + 一圈石灯笼（广场铺地在 BuildTenshu 里）
        Add(InazumaKind.Torii, cx, cz - 12f, cy, 180f, 0.9f);
        Add(InazumaKind.Torii, cx - 16f, cz - 30f, cy, 180f, 0.6f);
        Add(InazumaKind.Torii, cx + 16f, cz - 30f, cy, 180f, 0.6f);

        for (int i = -1; i <= 1; i += 2)
        {
            Add(InazumaKind.StoneLantern, cx + i * 10f, cz - 4f, cy, 0f, 0.5f);
            Add(InazumaKind.StoneLantern, cx + i * 12f, cz - 24f, cy, 0f, 0.5f);
        }

        Add(InazumaKind.Sakura, cx - 30f, cz - 8f, cy, 0f, 0.4f);
        Add(InazumaKind.Sakura, cx + 30f, cz - 8f, cy, 0f, 0.6f);

        // ---------- ② 城下町（一整块台地上，一条东西向的街） ----------
        // 街中心压在地台中心（cz-92），两侧各一排町屋；整块台地一样平，
        // 房子 / 石灯 / 樱花都落在这块平地上，不会一半埋进坡里
        float row1 = cz - 83f, row2 = cz - 101f;

        for (int i = 0; i < 5; i++)
        {
            float x = cx - 48f + i * 26f;
            Add(InazumaKind.Machiya, x, row1, 0f, 0f, H(i, 1, 11));
            Add(InazumaKind.Machiya, x + 13f, row2, 0f, 180f, H(i, 2, 12));
        }

        Add(InazumaKind.Teahouse, cx + 44f, cz - 92f, 0f, 200f, 0.5f);
        Add(InazumaKind.Watchtower, cx - 58f, cz - 92f, 0f, 30f, 0f);
        Add(InazumaKind.Sakura, cx + 30f, cz - 68f, 0f, 0f, 0.5f);
        Add(InazumaKind.Sakura, cx - 40f, cz - 118f, 0f, 0f, 0.5f);
        Add(InazumaKind.StoneLantern, cx - 20f, cz - 92f, 0f, 0f, 0.5f);
        Add(InazumaKind.StoneLantern, cx + 20f, cz - 92f, 0f, 0f, 0.5f);
        Add(InazumaKind.Torii, cx - 30f, cz - 92f, 0f, 90f, 0.7f);
        Add(InazumaKind.Torii, cx + 30f, cz - 92f, 0f, 270f, 0.7f);

        // ---------- ③ 港口（西岸小湾的北侧码头） ----------
        float px = InazumaIsles.PortX, pz = InazumaIsles.PortZ + 50f, py = QuayY;

        Add(InazumaKind.Dock, px, pz - 38f, 0f, 180f, 0.5f);                    // 栈桥伸进湾里
        Add(InazumaKind.Machiya, px - 16f, pz, py, 160f, 0.5f);                  // 港口的町屋（朝海）
        Add(InazumaKind.Machiya, px + 14f, pz + 2f, py, 200f, 0.7f);
        Add(InazumaKind.Teahouse, px - 2f, pz + 16f, py, 180f, 0.5f);
        Add(InazumaKind.Watchtower, px + 30f, pz + 10f, py, 240f, 0f);
        Add(InazumaKind.StoneLantern, px - 8f, pz - 12f, py, 0f, 0.5f);
        Add(InazumaKind.StoneLantern, px + 8f, pz - 12f, py, 0f, 0.5f);
        Add(InazumaKind.Sakura, px + 22f, pz + 24f, 0f, 0f, 0.5f);

        // ---------- ④ 神社离岛 ----------
        float sx = InazumaIsles.ShrineX, sz = InazumaIsles.ShrineZ + 6f;
        Add(InazumaKind.Shrine, sx, sz - 4f, ShrineIsleY, 20f, 0.55f);
        Add(InazumaKind.Torii, sx - 14f, sz + 22f, 0f, 200f, 0.7f);
        Add(InazumaKind.Sakura, sx + 24f, sz - 16f, 0f, 0f, 0.6f);
        Add(InazumaKind.Sakura, sx - 22f, sz - 24f, 0f, 0f, 0.4f);
        Add(InazumaKind.StoneLantern, sx + 10f, sz + 18f, 0f, 0f, 0.5f);

        // ---------- ⑤ 岩柱离岛：五重塔 ----------
        Add(InazumaKind.Pagoda, InazumaIsles.SpireX, InazumaIsles.SpireZ + 4f, SpireIsleY, 20f, 0.6f);
        Add(InazumaKind.StoneLantern, InazumaIsles.SpireX - 9f, InazumaIsles.SpireZ - 4f, 0f, 0f, 0.5f);
        Add(InazumaKind.StoneLantern, InazumaIsles.SpireX + 9f, InazumaIsles.SpireZ - 4f, 0f, 0f, 0.5f);

        // ---------- ⑥ 鸣神山脚的村子 ----------
        float mx = 1548f, mz = 1662f, my = MountainVillageY;
        Add(InazumaKind.Machiya, mx - 14f, mz - 6f, my, 20f, 0.6f);
        Add(InazumaKind.Machiya, mx + 12f, mz - 8f, my, 160f, 0.5f);
        Add(InazumaKind.Teahouse, mx + 2f, mz + 14f, my, 200f, 0.5f);
        Add(InazumaKind.Torii, mx - 24f, mz - 22f, my, 30f, 0.6f);
        Add(InazumaKind.Torii, mx + 22f, mz - 24f, my, 150f, 0.5f);
        Add(InazumaKind.Sakura, mx - 26f, mz + 16f, 0f, 0f, 0.6f);
        Add(InazumaKind.Sakura, mx + 28f, mz + 12f, 0f, 0f, 0.4f);
        Add(InazumaKind.Sakura, mx + 6f, mz - 26f, 0f, 0f, 0.5f);
    }

    static void Add(InazumaKind kind, float x, float z, float y, float yaw, float variant, float param = 0f)
    {
        float fp = InazumaBuildings.Footprint(kind);
        Sites.Add(new Site
        {
            kind = kind, x = x, z = z, y = y, yaw = yaw, variant = variant, param = param,
            radius = fp,
            name = InazumaBuildings.DisplayName(kind),
        });
    }

    static float H(int a, int b, int salt) => LowPoly.Hash01(a, b, salt);

    // ================= 落地 =================

    Material _material;
    Transform _root;
    readonly List<Transform> _flames = new List<Transform>();

    public void Init(OpenWorld world, Transform player)
    {
        _material = world.Material;

        _root = new GameObject("InazumaBuildings").transform;
        _root.SetParent(transform, false);

        if (Sites.Count == 0)
        {
            Debug.LogWarning("[InazumaSiteSpawner] 没有已规划的稻妻建筑（PlanIsles 没跑？），跳过落地。");
            return;
        }

        // ---- 几何：一个网格，三个子网格共用城里的石墙 / 瓦片 / 顶点色材质 ----
        InazumaBuildings.Begin();
        foreach (var s in Sites)
        {
            float y = s.y > 0.01f ? s.y : world.SurfaceY(s.x, s.z);
            InazumaBuildings.Append(s.kind, new Vector3(s.x, y, s.z), s.yaw, s.variant, s.param);
        }

        var mesh = InazumaBuildings.BuildMesh("InazumaBuildings");
        var go = new GameObject("InazumaBuildingMesh");
        go.transform.SetParent(_root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { world.StoneMaterial, world.RoofMaterial, world.Material };
        go.AddComponent<MeshCollider>().sharedMesh = mesh;

        // ---- 火苗：石灯笼 / 茶屋的炉子 / 码头的火盆 ----
        foreach (var p in InazumaBuildings.Flames)
        {
            var flame = new GameObject("Flame").transform;
            flame.SetParent(_root, false);
            flame.position = p;
            MonsterSpawner.BuildFlame(flame, _material);
            _flames.Add(flame);
        }

        // ---- 地图：地标图钉 + 聚落色块 + 三条街线 ----
        foreach (var s in Sites)
            MondstadtTown.Markers.Add(new MondstadtTown.MapMarker
            {
                name = s.name, x = s.x, z = s.z, kind = InazumaBuildings.MarkerKind(s.kind),
            });

        foreach (var h in InazumaBuildings.Houses)
            MondstadtTown.Blobs.Add(new MondstadtTown.MapBlob { x = h.x, z = h.y, r = h.z, color = 2 });

        // 聚落整块上色（将军府那块用浅色石板，和民居的木色分开）
        MondstadtTown.Blobs.Add(new MondstadtTown.MapBlob
        {
            x = InazumaIsles.CastleX, z = InazumaIsles.CastleZ - 30f, r = 86f, color = 1,
        });
        MondstadtTown.Blobs.Add(new MondstadtTown.MapBlob
        {
            x = InazumaIsles.PortX, z = InazumaIsles.PortZ + 46f, r = 40f, color = 2,
        });

        // 城下町两条街 + 高台前那条参道
        AddStreet(InazumaIsles.CastleX - 62f, InazumaIsles.CastleZ - 62f, InazumaIsles.CastleX + 62f, InazumaIsles.CastleZ - 62f);
        AddStreet(InazumaIsles.CastleX - 76f, InazumaIsles.CastleZ - 104f, InazumaIsles.CastleX + 76f, InazumaIsles.CastleZ - 104f);
        AddStreet(InazumaIsles.CastleX - 14f, InazumaIsles.CastleZ - 150f, InazumaIsles.CastleX - 14f, InazumaIsles.CastleZ - 62f);

        // ---- 村民：将军府城 + 港口 + 村子各放两个（和蒙德 / 璃月同一套 NPC） ----
        SpawnVillagers(world, player, InazumaIsles.CastleX - 6f, InazumaIsles.CastleZ - 62f, 2);
        SpawnVillagers(world, player, InazumaIsles.PortX, InazumaIsles.PortZ + 50f, 2);
        SpawnVillagers(world, player, 1548f, 1662f, 2);

        Debug.Log($"[InazumaSiteSpawner] 稻妻落地完成：{Sites.Count} 处建筑，" +
                  $"{InazumaBuildings.Flames.Count} 处火苗，{mesh.vertexCount} 顶点");
    }

    static void AddStreet(float x0, float z0, float x1, float z1) =>
        MondstadtTown.MapLines.Add(new MondstadtTown.MapLine { x0 = x0, z0 = z0, x1 = x1, z1 = z1, kind = 0 });

    /// <summary>在某一处放 n 个村民（找得到干地才放，和璃月那边一个做法）</summary>
    void SpawnVillagers(OpenWorld world, Transform player, float x, float z, int n)
    {
        for (int i = 0; i < n; i++)
        {
            float a = i * 2.1f;
            float vx = Mathf.Clamp(x + Mathf.Cos(a) * 7f, 4f, OpenWorld.Size - 4f);
            float vz = Mathf.Clamp(z + Mathf.Sin(a) * 7f, 4f, OpenWorld.Size - 4f);

            if (world.SurfaceY(vx, vz) < OpenWorld.WaterLevel + 2f) continue;
            if (!OpenWorld.IsInazumaLand(vx, vz) && InazumaIsles.LandWeight(vx, vz) < 0.4f) continue;

            var go = new GameObject("NpcInazuma" + i);
            go.transform.position = new Vector3(vx, world.SurfaceY(vx, vz), vz);

            var npc = go.AddComponent<VoxelNpc>();
            npc.data = VoxelNpcData.SunYan();
            npc.Init(world.Material, player, world);
        }
    }
}
