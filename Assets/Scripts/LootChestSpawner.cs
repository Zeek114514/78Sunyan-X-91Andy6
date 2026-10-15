using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 宝箱分布：把 5 个品质的宝箱按固定哈希铺到世界各处。
///   · 低品质多、高品质少，而且高品质往难走的地方去（史诗在密林深处、高级在雪山山腰、华丽在雪线以上 / 冰湖一带）。
///   · 全部确定性生成（固定哈希选点），每次进游戏位置一样。
///   · 刻意不写进 MondstadtTown.Markers，所以大地图和小地图上都不会显示宝箱 —— 得自己走出去找。
///   · 一部分宝箱刷成封印宝箱：由这里刷出守卫怪（丘丘人 / 史莱姆，高品质配一只暴徒）并登记到宝箱名下，
///     玩家把守卫清光，封印才会破，之后才能开。
/// </summary>
public class LootChestSpawner : MonoBehaviour
{
    /// <summary>每个品质铺几个（普通 / 精致 / 史诗 / 高级 / 华丽）。地图 850 米，按面积比原来多算一半</summary>
    static readonly int[] Counts = { 18, 12, 7, 5, 3 };

    const float MinSpacing   = 34f;   // 宝箱之间至少隔这么远
    const float MinSpawnDist = 14f;   // 出生点附近不摆，免得一睁眼就白给一个

    OpenWorld _world;
    Transform _player;
    VoxelInteraction _inventory;
    Material _material;
    Transform _root;
    Vector3 _spawn;

    readonly List<Vector2> _used = new List<Vector2>();
    int _built, _sealedCount, _guardCount;

    public void Init(OpenWorld world, Transform player, VoxelInteraction inventory, Material material)
    {
        _world = world;
        _player = player;
        _inventory = inventory;
        _material = material;
        _spawn = world.SpawnPoint;

        _root = new GameObject("LootChests").transform;
        _root.SetParent(transform, false);

        for (int t = 0; t < Counts.Length; t++)
            PlaceTier((LootChest.Tier)t, Counts[t]);

        Debug.Log($"[LootChestSpawner] 宝箱分布完成：共 {_built} 个（其中封印 {_sealedCount} 个，守卫 {_guardCount} 只）。" +
                  "宝箱不进大地图标记，要自己在世界里找（靠近 12 米内会显示名字，按 E 开启）。");
    }

    // ---------- 选点 ----------

    void PlaceTier(LootChest.Tier tier, int count)
    {
        var pool = Collect(tier, true);
        if (pool.Count < count * 4) pool.AddRange(Collect(tier, false));   // 严条件不够就放宽区域要求

        // 打散后按顺序取（哈希排序，确定性）
        pool.Sort((a, b) => Hash01((int)a.x, (int)a.y, 213).CompareTo(Hash01((int)b.x, (int)b.y, 213)));

        int placed = 0;
        foreach (var p in pool)
        {
            if (placed >= count) break;

            bool tooClose = false;
            foreach (var u in _used)
                if (Vector2.Distance(p, u) < MinSpacing) { tooClose = true; break; }
            if (tooClose) continue;

            Build(p, tier);
            _used.Add(p);
            placed++;
        }

        if (placed < count)
            Debug.LogWarning($"[LootChestSpawner] {LootChest.TierName(tier)} 只铺下 {placed}/{count} 个（合适的地点不够）。");
    }

    List<Vector2> Collect(LootChest.Tier tier, bool strict)
    {
        var pool = new List<Vector2>();

        for (float x = 20f; x < OpenWorld.Size - 20f; x += 8f)
        {
            for (float z = 20f; z < OpenWorld.Size - 20f; z += 8f)
            {
                float jx = x + (Hash01((int)x, (int)z, 211) - 0.5f) * 6f;
                float jz = z + (Hash01((int)x, (int)z, 212) - 0.5f) * 6f;
                if (Suitable(jx, jz, tier, strict)) pool.Add(new Vector2(jx, jz));
            }
        }
        return pool;
    }

    bool Suitable(float x, float z, LootChest.Tier tier, bool strict)
    {
        if (!OpenWorld.InBounds(x, z)) return false;
        if (OpenWorld.Height(x, z) < OpenWorld.WaterLevel + 1.2f) return false;    // 不摆在水里 / 浅滩上
        // 箱子要"稳稳坐在地上"：坡太陡会一头扎进土里、一头悬空。0.26 太松，实测会出现半埋的箱子
        if (OpenWorld.Slope(x, z) > 0.12f) return false;
        // 地台的边坡过渡带一律不摆（平整区里、过渡带外都可以）。
        // 这一条同时覆盖野外建筑的台子和解密场地的台子 —— 不然箱子会落在台坎上，看着就是"位置不对"
        if (OpenWorld.OnPadSlope(x, z)) return false;
        if (OpenWorld.IsFrozenLake(x, z)) return false;                            // 冰湖正中间不摆（看着像浮在冰面上）
        if (MondstadtTown.IsRoad(x, z)) return false;                              // 不堵在大路上
        if (OpenWorld.OnTownStreet(x, z)) return false;

        var p = new Vector2(x, z);

        // 城里建筑密、容易穿模：护城河外一圈起才摆
        if (Vector2.Distance(p, new Vector2(MondstadtTown.CenterX, MondstadtTown.CenterZ))
            < MondstadtTown.MoatOuter + 8f) return false;

        // 避开丘丘人营地的棚子 / 木箱，免得宝箱卡在摆设里
        foreach (var site in MonsterSpawner.CampSites)
            if (Vector2.Distance(p, site) < 15f) return false;

        // ★ 自己占地的整个影响范围都要避开（占地半径 + 平整余量 + 边坡过渡带）。
        //   只按占地半径避让的话，箱子会落在村落外圈的台坎上 —— 半埋进土里
        if (WildSiteSpawner.InsideSiteArea(x, z, 3f)) return false;

        // 璃月那边的野外建筑同理（两套选点各管一半地图）
        if (LiyueSiteSpawner.InsideSiteArea(x, z, 3f)) return false;

        // 避开解密场地：宝箱不能刷在石台上、更不能压在方碑里
        if (PuzzleSpawner.NearPuzzle(x, z, 6f)) return false;

        // 璃月的石灯阵 / 石鼓同理
        if (LiyuePuzzleSpawner.NearLiyuePuzzle(x, z, 6f)) return false;

        // 璃月港（阶段 E）：城里是人工台地，箱子按地形高度摆会沉到台面底下去 —— 整片让开
        if (LiyueHarbor.InCity(x, z)) return false;

        if (Vector2.Distance(p, new Vector2(_spawn.x, _spawn.z)) < MinSpawnDist) return false;

        return !strict || RegionOk(tier, x, z);
    }

    /// <summary>高品质宝箱往难走的地方放（严条件；候选不够时会被放宽）</summary>
    static bool RegionOk(LootChest.Tier tier, float x, float z)
    {
        switch (tier)
        {
            case LootChest.Tier.Epic:       // 密林深处
                return OpenWorld.ForestDensity(x, z) > 0.42f;

            case LootChest.Tier.Advanced:   // 雪山山腰
                return OpenWorld.SnowFactor(x, z) > 0.15f;

            case LootChest.Tier.Luxurious:  // 雪线以上 / 冰湖那一带
                return OpenWorld.SnowFactor(x, z) > 0.55f || NearFrozenLake(x, z, 60f);

            default:
                return true;
        }
    }

    /// <summary>离冰湖够近（冰湖坐标是 OpenWorld 私有的，这里用它的公开判定 + 四周采样来判断）</summary>
    static bool NearFrozenLake(float x, float z, float radius)
    {
        if (OpenWorld.IsFrozenLake(x, z)) return true;

        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            if (OpenWorld.IsFrozenLake(x + Mathf.Cos(a) * radius, z + Mathf.Sin(a) * radius)) return true;
        }
        return false;
    }

    // ---------- 生成 ----------

    void Build(Vector2 p, LootChest.Tier tier)
    {
        var go = new GameObject("LootChest_" + LootChest.TierName(tier));
        go.transform.SetParent(_root, false);
        go.transform.position = new Vector3(p.x, _world.SurfaceY(p.x, p.y), p.y);

        var chest = go.AddComponent<LootChest>();
        chest.Init(_world, _player, _inventory, _material, tier, Hash01((int)p.x, (int)p.y, 221) * 360f);

        // 一部分箱子是封印宝箱：守卫真刷出来了才立封印（刷不出来就当普通宝箱，免得出现永远打不开的箱子）
        if (Hash01((int)p.x, (int)p.y, 222) < LootChest.SealChance(tier))
        {
            int guards = SpawnGuards(chest, tier);
            if (guards > 0)
            {
                chest.Seal();
                _sealedCount++;
            }
        }

        _built++;
    }

    /// <summary>给封印宝箱刷守卫：围着箱子站一圈，尽量站在干地上</summary>
    int SpawnGuards(LootChest chest, LootChest.Tier tier)
    {
        Vector3 c = chest.transform.position;
        int wanted = LootChest.GuardCount(tier);
        int spawned = 0;

        for (int i = 0; i < wanted; i++)
        {
            float a = Hash01(i, (int)c.x, 231 + i) * Mathf.PI * 2f;
            float r = 3.0f + Hash01(i, (int)c.z, 241) * 3.4f;

            float gx = c.x + Mathf.Cos(a) * r;
            float gz = c.z + Mathf.Sin(a) * r;

            // 落在水里就往箱子这边缩一点再试
            if (OpenWorld.Height(gx, gz) < OpenWorld.WaterLevel + 1.0f)
            {
                gx = c.x + Mathf.Cos(a) * r * 0.45f;
                gz = c.z + Mathf.Sin(a) * r * 0.45f;
            }

            if (!OpenWorld.InBounds(gx, gz)) continue;
            if (OpenWorld.Height(gx, gz) < OpenWorld.WaterLevel + 0.6f) continue;
            if (OpenWorld.Slope(gx, gz) > 0.45f) continue;

            var go = new GameObject("ChestGuard");
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(gx, _world.SurfaceY(gx, gz) + 0.3f, gz);

            var monster = go.AddComponent<VoxelMonster>();
            monster.Init(_material, _player, _world, _inventory, GuardKind(tier, i, gx, gz));

            chest.AddGuard(monster);
            spawned++;
        }

        _guardCount += spawned;
        return spawned;
    }

    /// <summary>守卫种类：高品质的第一个是暴徒看家，水边的换史莱姆，其余是丘丘人</summary>
    static VoxelMonster.Kind GuardKind(LootChest.Tier tier, int index, float x, float z)
    {
        if (index == 0 && tier >= LootChest.Tier.Epic) return VoxelMonster.Kind.HilichurlBrute;
        if (OpenWorld.Height(x, z) < OpenWorld.WaterLevel + 5f) return VoxelMonster.Kind.SlimeWater;
        return index % 3 == 2 ? VoxelMonster.Kind.SlimeGrass : VoxelMonster.Kind.Hilichurl;
    }

    // ---------- 工具 ----------

    /// <summary>0~1 的确定性哈希（和世界其它生成同一套算法）</summary>
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
