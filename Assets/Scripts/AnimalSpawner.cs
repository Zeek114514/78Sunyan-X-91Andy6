using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 野生动物的分布（"生态层"）：每种动物按**它该住的地方**选点，各成小群散在全图。
///
///   · 野兔 —— 哪儿都活得下去：草原、疏林、田埂都行，数量最多（最容易撞见的一种）；
///   · 鹿   —— 蒙德的开阔草原与疏林边缘，成 1~3 只的小群在草地上吃草；
///   · 野猪 —— 密林里拱土（林子越密越多），胆子小但皮厚，能挨几下；
///   · 岩羊 —— 璃月的岩坡 / 峰林脚下 / 雪山坡，专挑陡的地方站（玩家得爬上去才够得着）；
///   · 白鹭 —— 水边浅滩，站在膝盖深的水里；
///   · 狐狸 —— 林缘，雪线以上就是一身白的雪狐。
///
/// 选点沿用野外怪那套**固定哈希**：每次进游戏分布完全一样，不会"这次进游戏兔子在东边、下次在西边"。
/// 全部躲开：城里 / 护城河 / 马路 / 石板街、野外建筑与解密场地的地台、璃月港、稻妻城、
/// 传送台、丘丘人营地（不然一刷出来就被丘丘人围住）、以及出生点附近。
/// 必须排在 MonsterSpawner 之后 —— 要读它的营地位置；宝箱不在它的下游，顺序无妨。
///
/// 【性能旋钮】下面的 *Sites 常量就是野外动物的总量（总数 ≈ 点位 × 平均群大小）。
/// 真机掉帧先动这里；其次动 VoxelAnimal.SleepDistance（远处不跑 AI 的距离）。
/// </summary>
public class AnimalSpawner : MonoBehaviour
{
    // ---------- 调参 ----------
    const int RabbitSites = 20;   // 每种动物的"点位"数量
    const int DeerSites   = 8;
    const int BoarSites   = 6;
    const int GoatSites   = 6;
    const int CraneSites  = 6;
    const int FoxSites    = 5;

    const float MinSiteSpacing  = 24f;   // 同种点位之间至少隔这么远（免得全挤在一小片）
    const float MinCrossSpacing = 9f;    // 不同种也别叠在一起
    const float MinSpawnDist    = 22f;   // 离出生点留点距离：一睁眼就被一群兔子围着也怪
    const float CampClearance   = 26f;   // 避开丘丘人营地（动物站在营地里不像话）

    OpenWorld _world;
    Transform _player;
    VoxelInteraction _inventory;
    Material _material;
    Transform _root;

    struct Site { public VoxelAnimal.Kind kind; public Vector2 pos; }
    readonly List<Site> _sites = new List<Site>();
    readonly int[] _spawned = new int[6];

    public void Init(OpenWorld world, Transform player, VoxelInteraction inventory)
    {
        _world = world;
        _player = player;
        _inventory = inventory;
        _material = world.Material;

        _root = new GameObject("Animals").transform;
        _root.SetParent(transform, false);

        Plan(VoxelAnimal.Kind.Rabbit, RabbitSites,  9f, 201);
        Plan(VoxelAnimal.Kind.Deer,   DeerSites,   11f, 211);
        Plan(VoxelAnimal.Kind.Boar,   BoarSites,   11f, 221);
        Plan(VoxelAnimal.Kind.Goat,   GoatSites,   10f, 231);
        Plan(VoxelAnimal.Kind.Crane,  CraneSites,   9f, 241);
        Plan(VoxelAnimal.Kind.Fox,    FoxSites,    12f, 251);

        foreach (var site in _sites) BuildFamily(site);

        int total = 0;
        for (int i = 0; i < _spawned.Length; i++) total += _spawned[i];

        Debug.Log($"[AnimalSpawner] 分布完成：野兔 {_spawned[0]} / 鹿 {_spawned[1]} / 野猪 {_spawned[2]} / " +
                  $"岩羊 {_spawned[3]} / 白鹭 {_spawned[4]} / 狐狸 {_spawned[5]}，合计 {total} 只" +
                  $"（点位 {_sites.Count} 处，固定哈希选点，重进游戏一致）");
    }

    // ---------- 选点 ----------

    /// <summary>给一种动物挑点位：全图扫一遍收候选，再按固定哈希打散，够开就取（间距检查过）</summary>
    void Plan(VoxelAnimal.Kind kind, int count, float step, int salt)
    {
        var candidates = new List<Vector2>();

        for (float x = 22f; x < OpenWorld.Size - 22f; x += step)
        {
            for (float z = 22f; z < OpenWorld.Size - 22f; z += step)
            {
                float px = x + (Hash01((int)x, (int)z, salt)     - 0.5f) * step * 0.9f;
                float pz = z + (Hash01((int)x, (int)z, salt + 1) - 0.5f) * step * 0.9f;

                if (Habitat(kind, px, pz)) candidates.Add(new Vector2(px, pz));
            }
        }

        // 打散成"确定性的乱序"：不是取扫描到的前 N 个，而是让全图都有机会 —— 铺得开
        candidates.Sort((a, b) => Hash01((int)a.x, (int)a.y, salt + 2)
                                  .CompareTo(Hash01((int)b.x, (int)b.y, salt + 2)));

        int placed = 0;
        foreach (var c in candidates)
        {
            if (placed >= count) break;
            if (!Spaced(kind, c)) continue;

            _sites.Add(new Site { kind = kind, pos = c });
            placed++;
        }
    }

    /// <summary>和已经定下来的点位保持距离（同种远一点、异种也别贴在一起）</summary>
    bool Spaced(VoxelAnimal.Kind kind, Vector2 p)
    {
        foreach (var s in _sites)
        {
            float need = s.kind == kind ? MinSiteSpacing : MinCrossSpacing;
            if (Vector2.Distance(p, s.pos) < need) return false;
        }
        return true;
    }

    /// <summary>这一点适不适合住这种动物（地形 + 国度 + 避让各种人工设施）</summary>
    bool Habitat(VoxelAnimal.Kind kind, float x, float z)
    {
        if (!OpenWorld.InBounds(x, z)) return false;

        // ---- 通用避让：人工的东西都不去 ----
        if (OpenWorld.IsRoad(x, z) || OpenWorld.OnTownStreet(x, z)) return false;
        if (OpenWorld.OnPadSlope(x, z)) return false;
        if (OpenWorld.InSiteKeepOut(x, z)) return false;
        if (WildSiteSpawner.InsideSiteArea(x, z, 6f)) return false;
        if (LiyueSiteSpawner.InsideSiteArea(x, z, 6f)) return false;
        if (PuzzleSpawner.NearPuzzle(x, z, 8f)) return false;
        if (LiyuePuzzleSpawner.NearLiyuePuzzle(x, z, 8f)) return false;
        if (LiyueHarbor.InCity(x, z)) return false;
        if (InCamp(x, z)) return false;

        var p = new Vector2(x, z);
        float townDist = Vector2.Distance(p, new Vector2(MondstadtTown.CenterX, MondstadtTown.CenterZ));
        if (townDist < MondstadtTown.MoatOuter + 12f) return false;

        Vector3 spawn = _world.SpawnPoint;
        if (Vector2.Distance(p, new Vector2(spawn.x, spawn.z)) < MinSpawnDist) return false;

        // ---- 各自的栖息条件 ----
        float h      = OpenWorld.Height(x, z);
        float slope  = OpenWorld.Slope(x, z);
        float snow   = OpenWorld.SnowFactor(x, z);
        float forest = OpenWorld.ForestDensity(x, z);
        float lw     = OpenWorld.LiyueWeight(x, z);

        switch (kind)
        {
            case VoxelAnimal.Kind.Rabbit:
                // 哪儿都能活：只要不是水边、陡坡、雪山顶
                return h > OpenWorld.WaterLevel + 1.3f && slope < 0.28f && snow < 0.50f && forest < 0.86f;

            case VoxelAnimal.Kind.Deer:
                // 蒙德的开阔草原与疏林边缘（喜欢看得见远处的地方，不爱钻深林）
                return OpenWorld.IsMondstadtLand(x, z) && lw < 0.5f
                       && h > OpenWorld.WaterLevel + 2f && slope < 0.16f && snow < 0.30f && forest < 0.66f;

            case VoxelAnimal.Kind.Boar:
                // 密林里拱土：林子越密越像它的地盘（蒙德璃月都有）
                return forest > 0.44f && h > OpenWorld.WaterLevel + 2f && slope < 0.30f && snow < 0.30f;

            case VoxelAnimal.Kind.Goat:
                // 岩坡 / 峰林脚下 / 雪山坡：坡度 33°~47°，玩家爬得上去，但它站得比你稳
                return h > OpenWorld.WaterLevel + 3f && slope > 0.16f && slope < 0.55f
                       && (OpenWorld.InKarstRegion(x, z) || snow > 0.25f || lw > 0.40f);

            case VoxelAnimal.Kind.Crane:
                // 水边浅滩：脚踩着岸、水位差不到小腿
                return h > OpenWorld.WaterLevel - 0.10f && h < OpenWorld.WaterLevel + 1.20f && slope < 0.12f;

            default:
                // 狐狸：林缘、或者雪线以上（那里是白狐）
                return h > OpenWorld.WaterLevel + 1.6f && slope < 0.30f
                       && ((forest > 0.36f && forest < 0.80f && snow < 0.45f) || snow > 0.32f);
        }
    }

    /// <summary>是不是扎在丘丘人营地里（动物站在营地里太出戏）</summary>
    static bool InCamp(float x, float z)
    {
        var p = new Vector2(x, z);
        foreach (var c in MonsterSpawner.CampSites)
            if (Vector2.Distance(p, c) < CampClearance) return true;
        return false;
    }

    // ---------- 刷 ----------

    /// <summary>一个点位刷一小群（鹿成 1~3 只，野猪 / 岩羊 / 白鹭 1~2 只，狐狸独来独往）</summary>
    void BuildFamily(Site site)
    {
        int maxFamily = MaxFamily(site.kind);
        int count = 1 + Mathf.FloorToInt(Hash01((int)site.pos.x, (int)site.pos.y, 271) * maxFamily);
        count = Mathf.Clamp(count, 1, maxFamily);

        float baseYaw = Hash01((int)site.pos.x, (int)site.pos.y, 272) * 360f;

        for (int i = 0; i < count; i++)
        {
            float a = (baseYaw + i * 360f / count + (Hash01(i, (int)site.pos.x, 273) - 0.5f) * 44f) * Mathf.Deg2Rad;
            float r = i == 0 ? 0f : 1.8f + Hash01(i, (int)site.pos.y, 274) * 3.6f;

            float x = site.pos.x + Mathf.Cos(a) * r;
            float z = site.pos.y + Mathf.Sin(a) * r;

            // 群成员也要各自站得住：水边那群尤其要筛（往外一点就是深水）
            if (!Habitat(site.kind, x, z)) continue;

            var go = new GameObject("Animal_" + site.kind);
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(x, _world.SurfaceY(x, z) + 0.15f, z);
            go.transform.rotation = Quaternion.Euler(0f, Hash01(i, (int)x, 275) * 360f, 0f);

            var animal = go.AddComponent<VoxelAnimal>();
            bool snowy = site.kind == VoxelAnimal.Kind.Fox && OpenWorld.SnowFactor(x, z) > 0.32f;
            animal.Init(_material, _player, _world, _inventory, site.kind, snowy);

            _spawned[(int)site.kind]++;
        }
    }

    static int MaxFamily(VoxelAnimal.Kind kind)
    {
        switch (kind)
        {
            case VoxelAnimal.Kind.Rabbit: return 3;
            case VoxelAnimal.Kind.Deer:   return 3;
            case VoxelAnimal.Kind.Boar:   return 2;
            case VoxelAnimal.Kind.Goat:   return 2;
            case VoxelAnimal.Kind.Crane:  return 2;
            default:                      return 1;      // 狐狸独来独往
        }
    }

    // ---------- 工具 ----------

    /// <summary>0~1 确定性哈希（和城 / 野外怪同一套算法，保证每次进游戏布局一样）</summary>
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
