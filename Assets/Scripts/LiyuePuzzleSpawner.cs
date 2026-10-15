using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 璃月解密场地的选点与落地（阶段 F；外形在 LiyuePuzzles.cs，玩法是共用的 PuzzleSite）。
///
/// 【和蒙德那一套（PuzzleSpawner）同构，四处不同】
///   ① 只在璃月的地上选（IsLiyueLand），分界带两边都不摆；
///   ② 港城（LiyueHarbor）整片让开：城里是人工台地，石台按地形高度摆会埋进台面里；
///   ③ 外形分两种：石灯阵（高，灯罩亮起来）和石鼓（矮，鼓面印记亮起来）；
///   ④ 地台冲突直接查 OpenWorld 里已登记的 _pads（OpenWorld.PadConflict）——
///      璃月这边前面已经登记过野外建筑和港口外的地台，跨生成器也要挡得住。
///
/// 规划时机：和蒙德一起跑（WildSiteSpawner.PlanEverything），不能自己再挂一个
/// [RuntimeInitializeOnLoadMethod]：两个入口各自清地台的话，谁后跑就会把对方登记的台子清掉。
/// </summary>
public class LiyuePuzzleSpawner : MonoBehaviour
{
    /// <summary>一处璃月解密场地</summary>
    public struct LiyuePuzzleSpot
    {
        public PuzzleKind  kind;
        public PuzzleStyle style;
        public float x, z;      // 石台中心
        public float y;         // 地台高度
        public float yaw;       // 入口朝向
        public float radius;    // 占地半径
        public int   seed;      // 顺序 / 灯色的种子：几何和运行时必须用同一个
    }

    /// <summary>所有璃月解密场地。其它生成器要避开这里</summary>
    public static readonly List<LiyuePuzzleSpot> Sites = new List<LiyuePuzzleSpot>();

    /// <summary>铺法：五种。（玩法、外形）成对排：
    ///   石灯阵·顺序 —— 要挨着地标，最挑地方，先挑
    ///   石鼓·守护   —— 密林 / 高处
    ///   石灯阵·简单 —— 开阔地，当引子
    ///   石鼓·顺序   —— 再要一处挨地标的
    ///   石灯阵·守护 —— 再要一处偏的
    /// </summary>
    static readonly PuzzleStyle[] PlanStyle =
    {
        PuzzleStyle.LiyueLamp, PuzzleStyle.LiyueDrum, PuzzleStyle.LiyueLamp,
        PuzzleStyle.LiyueDrum, PuzzleStyle.LiyueLamp,
    };

    static readonly PuzzleKind[] PlanKind =
    {
        PuzzleKind.Ordered, PuzzleKind.Guarded, PuzzleKind.AllLit,
        PuzzleKind.Ordered, PuzzleKind.Guarded,
    };

    // ---------- 候选点 ----------

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

    // ================= 尺寸（和 LiyuePuzzles 里的石台对齐） =================

    /// <summary>占地半径：石台 + 台阶 / 火盆 / 石碑伸出去那几米</summary>
    public static float Footprint(PuzzleKind kind) => LiyueBuildings.PuzzlePlatformRadius(kind) + 4.5f;

    static float Falloff() => 8f;

    /// <summary>地台的平整半径</summary>
    static float PadRadius(float fp) => fp + 4f;

    /// <summary>地台总共影响多大范围（平整区 + 过渡带）</summary>
    static float Reach(PuzzleKind kind) => PadRadius(Footprint(kind)) + Falloff();

    // ================= 规划（静态，烘高度场之前跑） =================

    public static void PlanSites()
    {
        Sites.Clear();
        _cand.Clear();

        BuildCandidates();

        for (int i = 0; i < PlanKind.Length; i++) TryPlace(PlanKind[i], PlanStyle[i], i);

        // 地台 + 禁植区：所有点选完之后再一次性登记
        foreach (var s in Sites)
        {
            OpenWorld.AddPad(s.x, s.z, PadRadius(Footprint(s.kind)), s.y, Falloff());
            OpenWorld.AddKeepOut(s.x, s.z, Footprint(s.kind) + 8f);
        }

        Debug.Log($"[LiyuePuzzleSpawner] 规划完成：共 {Sites.Count} 处璃月解密场地" +
                  $"（石灯阵 {Count(PuzzleStyle.LiyueLamp)} / 石鼓 {Count(PuzzleStyle.LiyueDrum)}；" +
                  $"顺序 {Count(PuzzleKind.Ordered)} / 守护 {Count(PuzzleKind.Guarded)} / 简单 {Count(PuzzleKind.AllLit)}）");
    }

    static int Count(PuzzleStyle style)
    {
        int n = 0;
        foreach (var s in Sites) if (s.style == style) n++;
        return n;
    }

    static int Count(PuzzleKind kind)
    {
        int n = 0;
        foreach (var s in Sites) if (s.kind == kind) n++;
        return n;
    }

    /// <summary>计划里这一种（玩法 + 外形）要铺几个</summary>
    static int Want(PuzzleKind kind, PuzzleStyle style)
    {
        int n = 0;
        for (int i = 0; i < PlanKind.Length; i++) if (PlanKind[i] == kind && PlanStyle[i] == style) n++;
        return n;
    }

    static void BuildCandidates()
    {
        const float X0 = 740f;                                  // 璃月的地最早从 x ≈ 800 开始

        for (float x = X0; x < OpenWorld.Size - 26f; x += 8f)
        {
            for (float z = 26f; z < OpenWorld.Size - 26f; z += 8f)
            {
                float jx = x + (LowPoly.Hash01((int)x, (int)z, 4101) - 0.5f) * 7f;
                float jz = z + (LowPoly.Hash01((int)x, (int)z, 4102) - 0.5f) * 7f;

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
            LowPoly.Hash01((int)_cand[a].x, (int)_cand[a].z, 4111)
                    .CompareTo(LowPoly.Hash01((int)_cand[b].x, (int)_cand[b].z, 4111)));
    }

    static void TryPlace(PuzzleKind kind, PuzzleStyle style, int index)
    {
        float fp = Footprint(kind);

        // 每种从洗牌顺序的不同位置开始扫（都从 0 开始扫会挤成一片）
        int start = (int)(LowPoly.Hash01(5, (int)style * 53 + (int)kind * 17 + index, 4121) * _order.Count);

        for (int n = 0; n < _order.Count; n++)
        {
            var c = _cand[_order[(start + n) % _order.Count]];
            if (!Suitable(kind, c)) continue;

            // ★ 自己的地台要等全部选完才登记，所以 OpenWorld.PadConflict 看不到"同类" ——
            //   这里必须再和自己已选的场地比一次，否则两处石台会挨在一起（第一版就重叠了 7.7 米）
            if (NearOwnPad(c.x, c.z, fp, 0f)) continue;

            Sites.Add(new LiyuePuzzleSpot
            {
                kind = kind,
                style = style,
                x = c.x, z = c.z, y = c.y,
                yaw = LowPoly.Hash01((int)c.x, (int)c.z, 4131) * 360f,
                seed = (int)(c.x * 13f + c.z * 29f),
                radius = fp,
            });
            return;
        }

        Debug.LogWarning($"[LiyuePuzzleSpawner] {LiyueBuildings.PuzzleSiteName(kind, style)} 没找到合适的位置" +
                         $"（璃月能摆石台的空地本来就少，候选被前面的内容占光了）。");
    }

    static bool Suitable(PuzzleKind kind, Cand c)
    {
        float fp = Footprint(kind);
        float reach = Reach(kind);

        if (!OpenWorld.InBounds(c.x, c.z)) return false;
        if (!OpenWorld.IsLiyueLand(c.x, c.z)) return false;                  // 只在璃月
        if (c.y < OpenWorld.WaterLevel + 2.8f) return false;
        if (c.slope > 0.12f) return false;
        if (OpenWorld.SnowFactor(c.x, c.z) > 0.05f) return false;
        if (OpenWorld.IsFrozenLake(c.x, c.z)) return false;
        if (MondstadtTown.IsRoad(c.x, c.z) || OpenWorld.OnTownStreet(c.x, c.z)) return false;

        // 港城整片让开（城里是人工台地，石台摆上去会埋进台面里）
        if (LiyueHarbor.NearCity(c.x, c.z, 40f)) return false;

        // 离地图边界留出整个占地
        if (c.x - fp < 6f || c.z - fp < 6f) return false;
        if (c.x + fp > OpenWorld.Size - 6f || c.z + fp > OpenWorld.Size - 6f) return false;

        // 河：石台会把水面削掉一块
        if (Mathf.Abs(c.z - OpenWorld.RiverZ(c.x)) < OpenWorld.RiverHalfWidth + reach) return false;

        // ★ 地台不许和**任何**已登记的地台重叠：直接查 OpenWorld 的登记表，
        //   所以前面蒙德/璃月野外建筑登记过的台子都挡得住（跨生成器重叠一样会把建筑拉歪）
        if (OpenWorld.PadConflict(c.x, c.z, PadRadius(fp), Falloff(), 0f)) return false;

        // 水边那三种（吊脚楼 / 码头 / 石拱桥）没有地台，PadConflict 管不到，单独躲一下
        if (LiyueSiteSpawner.NearLiyueSite(c.x, c.z, 12f)) return false;

        // 丘丘人营地（运行时才定下来，这条主要是给"重进游戏后的第二次规划"兜底）
        var p = new Vector2(c.x, c.z);
        foreach (var camp in MonsterSpawner.CampSites)
            if (Vector2.Distance(p, camp) < fp + 12f) return false;

        if (!FlatEnough(kind, c, reach)) return false;
        return RegionOk(kind, c);
    }

    /// <summary>这块地本身够不够平：在地台过渡带的外沿取一圈自然地形，和地台高度最多差多少。
    /// 容差比野外建筑宽（石台本来就会把地面压平，台坎高一点反而像"人工垒的祭坛"）</summary>
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

    /// <summary>三种模式分层：简单的在开阔地，顺序的挨着璃月的地标，守护的在偏僻处</summary>
    static bool RegionOk(PuzzleKind kind, Cand c)
    {
        switch (kind)
        {
            case PuzzleKind.AllLit:
                return c.forest < 0.55f && c.y < 26f;

            case PuzzleKind.Ordered:
                // 挨着璃月的地标（村落 / 楼阁 / 石林 / 梯田），玩家才会路过
                return NearLiyueLandmark(c.x, c.z, 150f) || OpenWorld.TerraceWeight(c.x, c.z) > 0.25f;

            default:   // Guarded：密林里 / 高处
                return c.forest > 0.40f || c.y > 15f;
        }
    }

    /// <summary>附近有没有已经铺好的璃月野外建筑（顺序模式的场地要挨着地标）</summary>
    static bool NearLiyueLandmark(float x, float z, float radius)
    {
        for (int i = 0; i < LiyueSiteSpawner.Sites.Count; i++)
        {
            float dx = x - LiyueSiteSpawner.Sites[i].x, dz = z - LiyueSiteSpawner.Sites[i].z;
            if (dx * dx + dz * dz < radius * radius) return true;
        }
        return false;
    }

    /// <summary>同一个生成器自己的场地之间也不许地台重叠。
    /// ★ 自己的台子要等全部选完才登记，OpenWorld.PadConflict 看不到它们</summary>
    static bool NearOwnPad(float x, float z, float fp, float margin)
    {
        for (int i = 0; i < Sites.Count; i++)
        {
            var s = Sites[i];
            float need = PadRadius(fp) + PadRadius(s.radius) + Falloff() + margin;
            float dx = x - s.x, dz = z - s.z;
            if (dx * dx + dz * dz < need * need) return true;
        }
        return false;
    }

    /// <summary>离这一点最近的一处璃月解密场地有多远（其它生成器避让用）</summary>
    public static bool NearLiyuePuzzle(float x, float z, float extra)
    {
        for (int i = 0; i < Sites.Count; i++)
        {
            var s = Sites[i];
            float dx = x - s.x, dz = z - s.z;
            float need = s.radius + extra;
            if (dx * dx + dz * dz < need * need) return true;
        }
        return false;
    }

    /// <summary>璃月场地之间的地台重叠数 + 和野外建筑地台相撞的次数（正常必须是 0）</summary>
    public static int CountPadOverlaps()
    {
        int bad = 0;

        for (int i = 0; i < Sites.Count; i++)
        {
            if (WildSiteSpawner.NearPad(Sites[i].x, Sites[i].z, Sites[i].radius, 0f)) bad++;
            if (LiyueSiteSpawner.NearOwnPad(Sites[i].x, Sites[i].z, Sites[i].radius, 0f)) bad++;

            for (int j = i + 1; j < Sites.Count; j++)
            {
                float need = PadRadius(Sites[i].radius) + PadRadius(Sites[j].radius) + Falloff();
                float dx = Sites[i].x - Sites[j].x, dz = Sites[i].z - Sites[j].z;
                if (dx * dx + dz * dz < need * need) bad++;
            }
        }
        return bad;
    }

    // ================= 自检报告 =================

    /// <summary>规划结果的可读报告（编辑器菜单 VoxelDemo → 验证野外建筑选点 会一起打出来）</summary>
    public static string PlanReport()
    {
        var sb = new System.Text.StringBuilder();

        sb.Append("计划：");
        for (int i = 0; i < PlanKind.Length; i++)
        {
            var name = LiyueBuildings.PuzzleSiteName(PlanKind[i], PlanStyle[i]);
            int got = 0;
            foreach (var s in Sites)
                if (s.kind == PlanKind[i] && s.style == PlanStyle[i]) got++;

            sb.Append($"{name} {got}/1；");
        }
        sb.AppendLine();

        // 分条件诊断：铺不下时一眼看出是哪一条把候选点全筛掉了（用最挑地方的"顺序"档做样本）
        {
            var kind = PuzzleKind.Ordered;
            float fp = Footprint(kind);
            float reach = Reach(kind);
            int liyue = 0, okHeight = 0, okSlope = 0, okHarbor = 0, okPad = 0, okFlat = 0, okRegion = 0;

            foreach (var c in _cand)
            {
                if (!OpenWorld.IsLiyueLand(c.x, c.z)) continue;
                liyue++;
                if (c.y >= OpenWorld.WaterLevel + 2.8f) okHeight++;
                if (c.slope <= 0.12f) okSlope++;
                if (!LiyueHarbor.NearCity(c.x, c.z, 40f)) okHarbor++;
                if (!OpenWorld.PadConflict(c.x, c.z, PadRadius(fp), Falloff(), 0f)) okPad++;
                if (FlatEnough(kind, c, reach)) okFlat++;
                if (RegionOk(kind, c)) okRegion++;
            }

            sb.AppendLine($"  【诊断·按顺序档】璃月的地 {liyue} / 高度坡度够 {okHeight} / 不在港口区 {okHarbor} / " +
                          $"不压已有地台 {okPad} / 地够平 {okFlat} / 区域合适 {okRegion}");
        }

        sb.AppendLine($"候选点 {_cand.Count} 个，共铺下 {Sites.Count} 处：");

        int problems = 0;
        foreach (var s in Sites)
        {
            float margin = float.MaxValue;
            foreach (var o in Sites)
            {
                if (o.x == s.x && o.z == s.z) continue;
                float need = PadRadius(s.radius) + PadRadius(o.radius) + Falloff();
                margin = Mathf.Min(margin, Vector2.Distance(new Vector2(s.x, s.z), new Vector2(o.x, o.z)) - need);
            }
            if (Sites.Count < 2) margin = 999f;
            if (margin < 0f) problems++;

            sb.AppendLine($"  {LiyueBuildings.PuzzleSiteName(s.kind, s.style)} ({s.x:0},{s.z:0})  " +
                          $"地台高 {s.y:0.0}m  朝向 {s.yaw:0}°  离最近邻居 {margin:0.0}m  " +
                          $"原地坡度 {OpenWorld.Slope(s.x, s.z):0.000}  seed {s.seed}");
        }

        sb.AppendLine($"璃月解密场地异常 {problems} 项；地台重叠 {CountPadOverlaps()} 处");
        return sb.ToString();
    }

    // ================= 落地 =================

    public void Init(OpenWorld world, Transform player, VoxelInteraction inventory)
    {
        _material = world.Material;

        _root = new GameObject("LiyuePuzzleSites").transform;
        _root.SetParent(transform, false);

        if (Sites.Count == 0)
        {
            Debug.LogWarning("[LiyuePuzzleSpawner] 没有已规划的璃月解密场地（PlanSites 没跑？），跳过落地。");
            return;
        }

        // ---- 静态几何（石台 / 铺地 / 火盆 / 石刻 / 经幢）合成一个网格 ----
        LiyueBuildings.Begin();
        foreach (var s in Sites)
        {
            var center = new Vector3(s.x, world.SurfaceY(s.x, s.z), s.z);
            var order = s.kind == PuzzleKind.Ordered ? WildPuzzles.RequiredOrder(s.kind, 0f, s.seed) : null;
            LiyueBuildings.AppendPuzzleGround(s.kind, s.style, center, s.yaw, order);
        }

        var mesh = LiyueBuildings.BuildMesh("LiyuePuzzleGrounds");
        var go = new GameObject("LiyuePuzzleGroundMesh");
        go.transform.SetParent(_root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { world.StoneMaterial, world.RoofMaterial, world.Material };
        go.AddComponent<MeshCollider>().sharedMesh = mesh;

        // ---- 入口火盆的火苗 ----
        foreach (var p in LiyueBuildings.Flames)
        {
            var flame = new GameObject("Flame").transform;
            flame.SetParent(_root, false);
            flame.position = p;
            MonsterSpawner.BuildFlame(flame, _material);
            _flames.Add(flame);
        }

        // ---- 每个场地挂一个 PuzzleSite（玩法是共用的，只是外形不同）----
        foreach (var s in Sites)
        {
            var siteGo = new GameObject("LiyuePuzzleSite");
            siteGo.transform.SetParent(_root, false);

            var site = siteGo.AddComponent<PuzzleSite>();
            site.Init(world, player, inventory, _material, s.kind, RewardTier(s.kind),
                      new Vector3(s.x, world.SurfaceY(s.x, s.z), s.z), s.yaw, s.seed, s.style);

            MondstadtTown.Markers.Add(new MondstadtTown.MapMarker
            {
                name = PuzzleSite.SiteName(s.kind, s.style),
                x = s.x, z = s.z,
                kind = MarkerKind(s.style),
            });
        }

        Debug.Log($"[LiyuePuzzleSpawner] 落地完成：{Sites.Count} 处璃月解密场地，" +
                  $"{LiyueBuildings.SubTris[0].Count / 3 + LiyueBuildings.SubTris[2].Count / 3} 个面，" +
                  $"{_flames.Count} 处火盆");
    }

    /// <summary>大地图标记编号：45 石灯阵 / 46 石鼓（配色见 WorldMap.MarkerColor）</summary>
    static byte MarkerKind(PuzzleStyle style) => style == PuzzleStyle.LiyueDrum ? (byte)46 : (byte)45;

    /// <summary>奖励品质：和蒙德同一套口径（简单给精致 / 顺序给史诗 / 守护给高级）</summary>
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
