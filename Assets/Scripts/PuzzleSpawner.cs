using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 解密场地的选点与落地（几何在 WildPuzzles 里，运行时在 PuzzleSite 里）。
///
/// 和野外建筑一样，选点必须在"烘高度场之前"跑（[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]）：
/// 石台脚下也要垫一块平地，而地台是在 OpenWorld.HeightRaw 里混出来的。
///
/// 三种模式分层铺：
///   · 简单（AllLit）  —— 平原开阔地，路过就能看见，当引子
///   · 顺序（Ordered） —— 挨着野外村落 / 遗迹（附近有地标，玩家才会走过来）
///   · 守护（Guarded） —— 密林深处 / 地势高处，得跑远一点，还要打一场
/// 全部避开野外建筑、丘丘人营地、宝箱点、城 / 湖 / 河 / 马路，固定哈希选点，重进游戏一致。
/// </summary>
public class PuzzleSpawner : MonoBehaviour
{
    /// <summary>一处解密场地</summary>
    public struct PuzzleSpot
    {
        public PuzzleKind kind;
        public float x, z;      // 石台中心
        public float y;         // 地台高度
        public float yaw;       // 石台朝向（入口方向）
        public int   seed;      // 顺序 / 宝石颜色的种子，几何和运行时必须用同一个
        public float radius;    // 占地半径（其它生成器要按这个避让）
    }

    /// <summary>所有解密场地。其它生成器要避开这里</summary>
    public static readonly List<PuzzleSpot> Sites = new List<PuzzleSpot>();

    /// <summary>每种铺几个：顺序 2 / 守护 2 / 简单 3。
    ///
    /// 数量和地图尺寸是绑死的：方碑场地的石台占地 12~14 米，加上平整余量和过渡带，
    /// 每处要离野外建筑 40~70 米，而且场地之间也不许地台重叠（重叠会把彼此的平整区拉歪）。</summary>
    static readonly PuzzleKind[] Plan =
    {
        PuzzleKind.Ordered, PuzzleKind.Ordered,    // 最挑地方，先挑
        PuzzleKind.Guarded, PuzzleKind.Guarded,
        PuzzleKind.AllLit, PuzzleKind.AllLit, PuzzleKind.AllLit,
    };

    // 和 OpenWorld.HeightRaw 里那几个盆地 / 高台对齐的常量
    const float LakeX = 85f, LakeZ = 118f, LakeR = 56f;
    const float SpawnX = MondstadtTown.CenterX, SpawnZ = 100f;

    struct Cand
    {
        public float x, z, y, slope, forest;
    }

    static readonly List<Cand> _cand = new List<Cand>();
    static readonly List<int>  _order = new List<int>();

    // ---------- 实例状态 ----------

    Material _material;
    Transform _root;
    readonly List<Transform> _flames = new List<Transform>();

    // ================= 规划（静态，烘高度场之前跑） =================

    // 注意：这里**没有**自己的 RuntimeInitializeOnLoadMethod —— 统一走
    // WildSiteSpawner.PlanEverything()。原因见那边的注释：两个 PlanSites 各自清地台的话，
    // 执行顺序没保证，谁后跑就会把对方登记的台子清掉。

    /// <summary>占地半径：石台 + 台阶 / 火把 / 提示石碑伸出去那几米。
    /// 实测最远伸出的是台阶（石台半径 + 4.7 米），所以 +4.5 刚好罩住 ——
    /// 多算的话每处场地都要多占 2 米净空，本来就紧张的空间会更铺不下</summary>
    public static float Footprint(PuzzleKind kind) => WildPuzzles.PlatformRadius(kind) + 4.5f;

    static float Falloff() => 8f;

    /// <summary>地台的平整半径</summary>
    static float PadRadius(float fp) => fp + 4f;

    /// <summary>地台总共影响多大范围（平整区 + 边坡过渡带）。水和城要按它避让</summary>
    static float Reach(PuzzleKind kind) => PadRadius(Footprint(kind)) + Falloff();

    public static void PlanSites()
    {
        Sites.Clear();
        _cand.Clear();

        BuildCandidates();

        var used = new List<PuzzleKind>();
        foreach (var kind in Plan)
        {
            int index = used.FindAll(k => k == kind).Count;
            TryPlace(kind, index);
            used.Add(kind);
        }

        // 地台 + 禁植区：所有点选完之后再一次性登记（中途登记会让后面的候选点看见前面垫平的台子）
        foreach (var s in Sites)
        {
            OpenWorld.AddPad(s.x, s.z, PadRadius(Footprint(s.kind)), s.y, Falloff());
            OpenWorld.AddKeepOut(s.x, s.z, Footprint(s.kind) + 8f);   // 只圈场地自己，不圈边坡（别把周围弄秃）
        }

        Debug.Log($"[PuzzleSpawner] 规划完成：共 {Sites.Count} 处解密场地" +
                  $"（简单 {Count(PuzzleKind.AllLit)} / 顺序 {Count(PuzzleKind.Ordered)} / 守护 {Count(PuzzleKind.Guarded)}）");
    }

    static int Count(PuzzleKind kind)
    {
        int n = 0;
        foreach (var s in Sites) if (s.kind == kind) n++;
        return n;
    }

    /// <summary>计划里这一种要铺几个</summary>
    static int Want(PuzzleKind kind)
    {
        int n = 0;
        foreach (var k in Plan) if (k == kind) n++;
        return n;
    }

    static void BuildCandidates()
    {
        for (float x = 26f; x < OpenWorld.Size - 26f; x += 8f)
        {
            for (float z = 26f; z < OpenWorld.Size - 26f; z += 8f)
            {
                float jx = x + (LowPoly.Hash01((int)x, (int)z, 1101) - 0.5f) * 7f;
                float jz = z + (LowPoly.Hash01((int)x, (int)z, 1102) - 0.5f) * 7f;

                _cand.Add(new Cand
                {
                    x = jx, z = jz,
                    y      = OpenWorld.Height(jx, jz),
                    slope  = OpenWorld.Slope(jx, jz),
                    forest = OpenWorld.ForestDensity(jx, jz),
                });
            }
        }

        _order.Clear();
        for (int i = 0; i < _cand.Count; i++) _order.Add(i);
        _order.Sort((a, b) =>
            LowPoly.Hash01((int)_cand[a].x, (int)_cand[a].z, 1111)
                    .CompareTo(LowPoly.Hash01((int)_cand[b].x, (int)_cand[b].z, 1111)));
    }

    static void TryPlace(PuzzleKind kind, int index)
    {
        float fp = Footprint(kind);

        // 每种从洗牌顺序的不同位置开始扫（理由同 WildSiteSpawner：都从 0 开始扫会挤成一条线）
        int start = (int)(LowPoly.Hash01(3, (int)kind * 41 + index, 1121) * _order.Count);

        for (int n = 0; n < _order.Count; n++)
        {
            var c = _cand[_order[(start + n) % _order.Count]];
            if (!Suitable(kind, c)) continue;

            // 地台不许和已有的重叠（和野外建筑共用同一套判定）
            if (WildSiteSpawner.NearPad(c.x, c.z, fp, 0f)) continue;
            if (NearOwnPad(c.x, c.z, fp, 0f)) continue;

            Sites.Add(new PuzzleSpot
            {
                kind = kind,
                x = c.x, z = c.z, y = c.y,
                yaw = LowPoly.Hash01((int)c.x, (int)c.z, 1131) * 360f,
                seed = (int)(c.x * 13f + c.z * 29f),
                radius = fp,
            });
            return;
        }

        Debug.LogWarning($"[PuzzleSpawner] {PuzzleSite.SiteName(kind)} 没找到合适的位置（候选被其它内容占光了）。");
    }

    /// <summary>同一个解密场地自己的地台之间也不许重叠（判定方式和野外建筑一致）</summary>
    static bool NearOwnPad(float x, float z, float fp, float margin)
    {
        for (int i = 0; i < Sites.Count; i++)
        {
            var s = Sites[i];
            float need = PadRadius(fp) + PadRadius(Footprint(s.kind)) + Falloff() + margin;
            float dx = x - s.x, dz = z - s.z;
            if (dx * dx + dz * dz < need * need) return true;
        }
        return false;
    }

    /// <summary>场地之间 + 场地与野外建筑之间的地台重叠数（正常必须是 0，否则会有建筑浮空）</summary>
    public static int CountPadOverlaps()
    {
        int bad = 0;

        for (int i = 0; i < Sites.Count; i++)
        {
            float fpi = Footprint(Sites[i].kind);
            if (WildSiteSpawner.NearPad(Sites[i].x, Sites[i].z, fpi, 0f)) bad++;

            for (int j = i + 1; j < Sites.Count; j++)
            {
                float fpj = Footprint(Sites[j].kind);
                float need = PadRadius(fpi) + PadRadius(fpj) + Falloff();
                float dx = Sites[i].x - Sites[j].x, dz = Sites[i].z - Sites[j].z;
                if (dx * dx + dz * dz < need * need) bad++;
            }
        }
        return bad;
    }

    static bool Suitable(PuzzleKind kind, Cand c)
    {
        float fp = Footprint(kind);
        float reach = Reach(kind);

        if (!OpenWorld.InBounds(c.x, c.z)) return false;
        if (c.y < OpenWorld.WaterLevel + 2.8f) return false;
        if (c.slope > 0.12f) return false;
        if (OpenWorld.SnowFactor(c.x, c.z) > 0.05f) return false;
        if (OpenWorld.IsFrozenLake(c.x, c.z)) return false;
        if (MondstadtTown.IsRoad(c.x, c.z) || OpenWorld.OnTownStreet(c.x, c.z)) return false;
        // 注：原来的 "x+z > 560（雪山方向）" 硬边界在扩图之后取消了，改由 SnowFactor 判定

        // 离地图边界留出整个占地
        if (c.x - fp < 6f || c.z - fp < 6f) return false;
        if (c.x + fp > OpenWorld.Size - 6f || c.z + fp > OpenWorld.Size - 6f) return false;

        // 这里只管蒙德（璃月那边的解密场地由阶段 F 单独铺，外观是石灯阵 / 石鼓）
        if (!OpenWorld.IsMondstadtLand(c.x, c.z)) return false;

        var p = new Vector2(c.x, c.z);
        if (Vector2.Distance(p, new Vector2(MondstadtTown.CenterX, MondstadtTown.CenterZ))
            < MondstadtTown.MoatOuter + reach + 2f) return false;
        if (Vector2.Distance(p, new Vector2(MondstadtTown.CathedralX, MondstadtTown.CathedralZ))
            < MondstadtTown.CathedralR + reach) return false;
        if (Vector2.Distance(p, new Vector2(LakeX, LakeZ)) < LakeR + reach) return false;
        if (Mathf.Abs(c.z - OpenWorld.RiverZ(c.x)) < OpenWorld.RiverHalfWidth + reach) return false;
        if (Vector2.Distance(p, new Vector2(SpawnX, SpawnZ)) < 26f) return false;

        // 野外建筑：按"地台不重叠"避让（NearPad 内部就是"两边平整区之和 + 较大的过渡带"）。
        // ★ 只判"建筑占地半径 + 8"是不够的：漏了(1)方碑平台自己伸出去的 12~14 米、
        //   (2)地台外那圈 8~13 米的边坡过渡带 —— 之前村(201,48) 和场地(242,50) 相距 41 米
        //   就判成"够远"，结果石台整个伸进了村里。
        if (WildSiteSpawner.NearPad(c.x, c.z, fp, 0f)) return false;

        // 丘丘人营地
        foreach (var camp in MonsterSpawner.CampSites)
            if (Vector2.Distance(p, camp) < fp + 12f) return false;

        if (!FlatEnough(kind, c, reach)) return false;
        return RegionOk(kind, c);
    }

    /// <summary>这块地本身够不够平：在地台过渡带的外沿取一圈自然地形，和地台高度最多差多少。
    ///
    /// 容差比野外建筑宽（建筑 2.4~4.5，这里 4.5）：方碑场地的石台本来就会把地面压平，
    /// 台坎高一点反而像"人工垒起来的祭坛"。★ 这一条曾经卡死过整批场地 ——
    /// 天然平整的地方早被野外建筑占了（建筑要求更严），方碑再按同样标准就一个都挑不出来，
    /// 诊断里"地够平 630/4761"就是它的指纹。</summary>
    static bool FlatEnough(PuzzleKind kind, Cand c, float reach)
    {
        const float allow = 4.0f;

        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            float h = OpenWorld.Height(c.x + Mathf.Cos(a) * reach, c.z + Mathf.Sin(a) * reach);
            if (Mathf.Abs(h - c.y) > allow) return false;
        }
        return true;
    }

    /// <summary>三种模式分层：简单的在开阔地，顺序的挨着地标，守护的在偏僻处</summary>
    static bool RegionOk(PuzzleKind kind, Cand c)
    {
        switch (kind)
        {
            case PuzzleKind.AllLit:
                return c.forest < 0.55f && c.y < 22f;

            case PuzzleKind.Ordered:
                return NearLandmark(c.x, c.z, 130f);

            default:   // Guarded
                return c.forest > 0.40f || c.y > 15f;
        }
    }

    /// <summary>附近有没有野外建筑（顺序模式的场地要挨着地标，玩家才会路过）</summary>
    static bool NearLandmark(float x, float z, float radius)
    {
        foreach (var s in WildSiteSpawner.Sites)
        {
            float dx = x - s.x, dz = z - s.z;
            if (dx * dx + dz * dz < radius * radius) return true;
        }
        return false;
    }

    /// <summary>离这一点最近解密场地有多远（其它生成器避让用）</summary>
    public static bool NearPuzzle(float x, float z, float extra)
    {
        for (int i = 0; i < Sites.Count; i++)
        {
            var s = Sites[i];
            float need = s.radius + extra;
            float dx = x - s.x, dz = z - s.z;
            if (dx * dx + dz * dz < need * need) return true;
        }
        return false;
    }

    // ================= 自检报告 =================

    /// <summary>规划结果的可读报告（编辑器菜单 VoxelDemo → 验证野外建筑选点 会一起打出来）</summary>
    public static string PlanReport()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"计划：简单 {Count(PuzzleKind.AllLit)}/{Want(PuzzleKind.AllLit)}；" +
                      $"顺序 {Count(PuzzleKind.Ordered)}/{Want(PuzzleKind.Ordered)}；" +
                      $"守护 {Count(PuzzleKind.Guarded)}/{Want(PuzzleKind.Guarded)}；候选点 {_cand.Count} 个");

        // 分条件诊断：铺不下时一眼看出是哪一条把候选点全筛掉了
        {
            var kind = PuzzleKind.Ordered;
            float dfp = Footprint(kind);
            float dreach = Reach(kind);
            int okHeight = 0, okSlope = 0, okPad = 0, okFlat = 0, okRegion = 0, okCamp = 0;

            foreach (var c in _cand)
            {
                if (c.y >= OpenWorld.WaterLevel + 2.8f) okHeight++;
                if (c.slope <= 0.12f) okSlope++;
                if (!WildSiteSpawner.NearPad(c.x, c.z, dfp, 0f)) okPad++;
                if (FlatEnough(kind, c, dreach)) okFlat++;
                if (RegionOk(kind, c)) okRegion++;

                bool campOk = true;
                var cp = new Vector2(c.x, c.z);
                foreach (var camp in MonsterSpawner.CampSites)
                    if (Vector2.Distance(cp, camp) < dfp + 12f) { campOk = false; break; }
                if (campOk) okCamp++;
            }

            sb.AppendLine($"  【诊断·按顺序】高度够 {okHeight} / 坡度够平 {okSlope} / 不在建筑地台旁 {okPad} / " +
                          $"地够平 {okFlat} / 区域合适 {okRegion} / 离营地够远 {okCamp}");
        }

        int problems = 0;
        foreach (var s in Sites)
        {            float margin = float.MaxValue;
            for (int i = 0; i < Sites.Count; i++)
            {
                if (Sites[i].x == s.x && Sites[i].z == s.z) continue;
                float need = s.radius * 0.5f + Sites[i].radius * 0.5f;
                float d = Vector2.Distance(new Vector2(s.x, s.z), new Vector2(Sites[i].x, Sites[i].z));
                margin = Mathf.Min(margin, d - need);
            }
            if (Sites.Count < 2) margin = 999f;
            if (margin < 0f) problems++;

            sb.AppendLine($"  {PuzzleSite.SiteName(s.kind)} ({s.x:0},{s.z:0})  地台高 {s.y:0.0}m  朝向 {s.yaw:0}°  " +
                          $"离最近邻居 {margin:0.0}m  原地坡度 {OpenWorld.Slope(s.x, s.z):0.000}  seed {s.seed}");
        }

        sb.AppendLine($"解密场地异常 {problems} 项");
        return sb.ToString();
    }

    // ================= 落地 =================

    public void Init(OpenWorld world, Transform player, VoxelInteraction inventory)
    {
        _material = world.Material;

        _root = new GameObject("PuzzleSites").transform;
        _root.SetParent(transform, false);

        if (Sites.Count == 0)
        {
            Debug.LogWarning("[PuzzleSpawner] 没有已规划的解密场地（PlanSites 没跑？），跳过落地。");
            return;
        }

        // ---- 静态几何（石台 / 铺地 / 火把 / 机关台 / 提示石碑）全部合成一个网格 ----
        WildPuzzles.Begin();
        foreach (var s in Sites)
        {
            var center = new Vector3(s.x, world.SurfaceY(s.x, s.z), s.z);
            var order = s.kind == PuzzleKind.Ordered ? WildPuzzles.RequiredOrder(s.kind, 0f, s.seed) : null;
            WildPuzzles.AppendGround(s.kind, center, s.yaw, order);
        }

        var mesh = WildPuzzles.BuildMesh("PuzzleGrounds");
        var go = new GameObject("PuzzleGroundMesh");
        go.transform.SetParent(_root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { world.StoneMaterial, world.RoofMaterial, world.Material };
        go.AddComponent<MeshCollider>().sharedMesh = mesh;

        // ---- 入口火把的火苗 ----
        foreach (var p in WildPuzzles.Flames)
        {
            var flame = new GameObject("Flame").transform;
            flame.SetParent(_root, false);
            flame.position = p;
            MonsterSpawner.BuildFlame(flame, _material);
            _flames.Add(flame);
        }

        // ---- 每个场地挂一个 PuzzleSite（方碑是独立 GameObject，要点亮 / 熄灭）----
        foreach (var s in Sites)
        {
            var siteGo = new GameObject("PuzzleSite");
            siteGo.transform.SetParent(_root, false);

            var site = siteGo.AddComponent<PuzzleSite>();
            site.Init(world, player, inventory, _material, s.kind, RewardTier(s.kind),
                      new Vector3(s.x, world.SurfaceY(s.x, s.z), s.z), s.yaw, s.seed);

            MondstadtTown.Markers.Add(new MondstadtTown.MapMarker
            {
                name = PuzzleSite.SiteName(s.kind),
                x = s.x, z = s.z,
                kind = 24,
            });
        }

        Debug.Log($"[PuzzleSpawner] 落地完成：{Sites.Count} 处解密场地，{WildPuzzles.SubTris[0].Count / 3 + WildPuzzles.SubTris[2].Count / 3} 个面，" +
                  $"{_flames.Count} 处火把");
    }

    /// <summary>奖励品质：简单给普通 / 精致，顺序给精致 / 史诗，守护给史诗 / 高级</summary>
    static LootChest.Tier RewardTier(PuzzleKind kind)
    {
        switch (kind)
        {
            case PuzzleKind.Ordered: return LootChest.Tier.Epic;
            case PuzzleKind.Guarded: return LootChest.Tier.Advanced;
            default:                 return LootChest.Tier.Exquisite;
        }
    }

    void Update()
    {
        for (int i = 0; i < _flames.Count; i++)
        {
            var f = _flames[i];
            if (f == null) continue;

            float s = 1f + Mathf.Sin(Time.time * 7f + i) * 0.14f;
            f.localScale = new Vector3(0.55f * s, 0.75f * s, 0.55f * s);
            f.localRotation = Quaternion.Euler(0f, Time.time * 90f, 0f);
        }
    }
}
