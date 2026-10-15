using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 璃月野外建筑的选点与落地（几何在 LiyueBuildings 里）。
///
/// 【和蒙德那一套（<see cref="WildSiteSpawner"/>）同构，三处不同】
///   ① 只在"璃月的地"上选（<see cref="OpenWorld.IsLiyueLand"/>）；分界带两边都不建 ——
///      免得白墙黛瓦挨着德式木屋；
///   ② 港口（<see cref="LiyueHarbor"/>，阶段 E）自己占的那一大片整片让开。选点发生在烘高度场之前，
///      那时还看不到港口（OpenWorld 要等烘场景时才补登记港口的禁植区），所以这里显式排除；
///   ③ 多了三种"靠着水"的建筑：吊脚楼 / 码头要本地 +z 朝水（前排木柱扎进水里），
///      石拱桥要正好跨在河上。这三种**都不垫地台** —— 垫了会把水填平。
///
/// 规划时机：和蒙德一起跑（<see cref="WildSiteSpawner.PlanEverything"/>），不能自己再挂一个
/// [RuntimeInitializeOnLoadMethod]：两个入口各自清地台的话，谁后跑就会把对方登记的台子清掉。
/// </summary>
public class LiyueSiteSpawner : MonoBehaviour
{
    /// <summary>一处璃月野外建筑</summary>
    public struct LiyueSite
    {
        public LiyueKind kind;
        public float x, z;         // 平面位置
        public float y;            // 地台高度（水边的几种 = 岸地高度；石拱桥 = 起拱线高度）
        public float yaw;          // 朝向（水边的几种：本地 +z 指向水）
        public float variant;      // 0~1 的造型变体
        public float radius;       // 占地半径
        public float param;        // 石拱桥：半跨长（从桥心到岸）；其余为 0
    }

    /// <summary>所有璃月野外建筑的位置。怪物 / 宝箱 / 解密场地都要避开这里</summary>
    public static readonly List<LiyueSite> Sites = new List<LiyueSite>();

    // ---------- 地台几何（和蒙德同一套公式，两边算出来的净空才可比） ----------

    /// <summary>地台的平整半径</summary>
    public static float PadRadius(float fp) => fp + 6f;

    /// <summary>地台边缘往外多少米平滑回自然地形</summary>
    static float Falloff(float fp) => Mathf.Clamp(fp * 0.4f, 8f, 13f);

    /// <summary>两块地台共存的最近距离（再看一遍 WildSiteSpawner 上那段注释：近了就会互相拉歪）</summary>
    public static float PadGap(float fpA, float fpB) =>
        PadRadius(fpA) + PadRadius(fpB) + Mathf.Max(Falloff(fpA), Falloff(fpB));

    /// <summary>水边 / 跨水的三种不垫地台</summary>
    static bool Padless(LiyueKind kind) =>
        kind == LiyueKind.StiltHouse || kind == LiyueKind.Dock || kind == LiyueKind.ArchBridge;

    /// <summary>要"本地 +z 朝水"的两种</summary>
    static bool Waterfront(LiyueKind kind) =>
        kind == LiyueKind.StiltHouse || kind == LiyueKind.Dock;

    /// <summary>每种的铺法：数量 + 先后顺序。越挑地方（要求平、要求靠水、占地大）越排前面。</summary>
    static readonly LiyueKind[] Plan =
    {
        LiyueKind.Village, LiyueKind.Village,             // 占地 30 米、要求最平，先挑（锚点见下）
        LiyueKind.Shrine,                                 // 岩神像：要高地
        LiyueKind.StoneForest, LiyueKind.StoneForest,      // 石林遗迹：挨着峰林
        LiyueKind.Pagoda,                                  // 楼阁
        LiyueKind.WaterMill,                               // 水车：要挨着水
        LiyueKind.TerraceHut, LiyueKind.TerraceHut,         // 梯田农舍：要落在梯田上
        LiyueKind.Paifang,                                 // 牌坊：立在村口，所以排在村落之后
        LiyueKind.TeaStall, LiyueKind.TeaStall,            // 茶棚：河边的歇脚处
    };

    /// <summary>两座村落的片区锚点：一个在河北岸、一个在河南岸的平原。
    /// 不指定的话两座村子会挤在同一片平地上（璃月能盖房的平地本来就不多）。</summary>
    static readonly Vector2[] VillageAnchors =
    {
        new Vector2(958f, 58f),
        new Vector2(884f, 300f),
    };

    /// <summary>水边的两种各铺几处；石拱桥沿河自己找位，数量在 BridgeCount</summary>
    const int StiltHouseCount = 2;
    const int DockCount = 2;
    const int BridgeCount = 2;

    // ---------- 港口（阶段 E）的保留范围 ----------
    //
    // 港口布局（主码头 / 四层街道 / 吊脚楼群 / 楼阁 / 牌坊 / 岩神像广场 / 钟楼 / 灯塔）要靠水、
    // 要占一大片，所以这一圈整片留给它：野外建筑一律不进。等阶段 E 写完，那边想用哪块就用哪块。
    const float HarborKeepR = LiyueHarbor.QuayR + 110f;               // 港城 + 一圈街道的余量
    const float InnerBayKeepR = LiyueHarbor.InnerBayR + 45f;          // 内湾（吊脚楼群围着它）

    static bool InHarborZone(float x, float z) =>
        Mathf.Sqrt((x - LiyueHarbor.HarborX) * (x - LiyueHarbor.HarborX) +
                   (z - LiyueHarbor.HarborZ) * (z - LiyueHarbor.HarborZ)) < HarborKeepR ||
        Mathf.Sqrt((x - LiyueHarbor.InnerBayX) * (x - LiyueHarbor.InnerBayX) +
                   (z - LiyueHarbor.InnerBayZ) * (z - LiyueHarbor.InnerBayZ)) < InnerBayKeepR;

    // ---------- 峰林范围（和 OpenWorld 里 KarstX0… 那几个私有常量保持同步） ----------

    const float KarstX0 = 950f, KarstX1 = 1350f, KarstZ0 = 560f, KarstZ1 = 880f;

    static bool NearKarst(float x, float z, float pad) =>
        x > KarstX0 - pad && x < KarstX1 + pad && z > KarstZ0 - pad && z < KarstZ1 + pad;

    // ---------- 候选点缓存 ----------

    struct Cand
    {
        public float x, z, y, slope, forest, snow;
        public bool water;         // 26 米内有没有水面（水车 / 码头 / 吊脚楼要挑这种地方）
    }

    static readonly List<Cand> _cand = new List<Cand>();
    static readonly List<int> _order = new List<int>();

    // ---------- 实例状态 ----------

    Material _material;
    Transform _root;
    readonly List<Transform> _flames = new List<Transform>();
    int _villagers;

    public static bool Planned { get; private set; }

    // ================= 规划（静态，烘高度场之前跑） =================

    /// <summary>选点 + 登记地台 + 登记禁植区。由 WildSiteSpawner.PlanEverything 统一调用。</summary>
    public static void PlanSites()
    {
        Sites.Clear();
        _cand.Clear();

        BuildCandidates();

        // 先铺计划里的（大件在前，小件见缝插针）
        var used = new List<LiyueKind>();
        foreach (var kind in Plan)
        {
            int already = used.FindAll(k => k == kind).Count;
            TryPlace(kind, already);
            used.Add(kind);
        }

        // 再铺靠水的两种（它们不用地台，但要找"水在本地 +z"的岸线）
        for (int i = 0; i < StiltHouseCount; i++) TryPlaceWaterfront(LiyueKind.StiltHouse, i);
        for (int i = 0; i < DockCount; i++) TryPlaceWaterfront(LiyueKind.Dock, i);

        // 石拱桥最后：跨在河上，沿河自己找位
        PlaceBridges();

        // 地台 + 禁植区：所有点选完之后再一次性登记（中途登记会让后面的候选点看见前面垫平的台子）
        foreach (var s in Sites)
        {
            if (Padless(s.kind)) continue;
            OpenWorld.AddPad(s.x, s.z, PadRadius(s.radius), s.y, Falloff(s.radius));
            OpenWorld.AddKeepOut(s.x, s.z, s.radius + 8f);
        }

        Planned = true;
        Debug.Log($"[LiyueSiteSpawner] 规划完成：共 {Sites.Count} 处璃月野外建筑" +
                  $"（地台 {CountPads()} 块、禁植区 {CountPads()} 处，候选点 {_cand.Count} 个，固定哈希选点，重进游戏一致）");
    }

    static int CountPads()
    {
        int n = 0;
        foreach (var s in Sites) if (!Padless(s.kind)) n++;
        return n;
    }

    /// <summary>把璃月那半张地图扫一遍，缓存候选点的地形数据（后面各种建筑都在这份缓存上筛）；顺便按哈希洗牌</summary>
    static void BuildCandidates()
    {
        const float X0 = 740f;                              // 璃月的地最早从 x ≈ 800 开始（分界带随 z 起伏），多留一点余量

        for (float x = X0; x < OpenWorld.Size - 22f; x += 7f)
        {
            for (float z = 22f; z < OpenWorld.Size - 22f; z += 7f)
            {
                float jx = x + (H((int)x, (int)z, 3301) - 0.5f) * 6f;
                float jz = z + (H((int)x, (int)z, 3302) - 0.5f) * 6f;

                var c = new Cand
                {
                    x = jx, z = jz,
                    y      = OpenWorld.Height(jx, jz),
                    slope  = OpenWorld.Slope(jx, jz),
                    forest = OpenWorld.ForestDensity(jx, jz),
                    snow   = OpenWorld.SnowFactor(jx, jz),
                };

                for (int i = 0; i < 8 && !c.water; i++)
                {
                    float a = i * Mathf.PI * 0.25f;
                    if (OpenWorld.Height(jx + Mathf.Cos(a) * 26f, jz + Mathf.Sin(a) * 26f) < OpenWorld.WaterLevel + 0.4f)
                        c.water = true;
                }

                _cand.Add(c);
            }
        }

        _order.Clear();
        for (int i = 0; i < _cand.Count; i++) _order.Add(i);
        _order.Sort((a, b) =>
            H((int)_cand[a].x, (int)_cand[a].z, 3701).CompareTo(H((int)_cand[b].x, (int)_cand[b].z, 3701)));
    }

    /// <summary>铺一处：按洗好的顺序扫候选点，取第一个"地形合适、又和已有建筑拉得开"的位置。
    /// 村落按各自的片区锚点排顺序（先看锚点附近，不行再往外走）</summary>
    static void TryPlace(LiyueKind kind, int index)
    {
        float fp = LiyueBuildings.Footprint(kind);

        bool anchored = kind == LiyueKind.Village && index < VillageAnchors.Length;
        var order = anchored ? OrderNear(VillageAnchors[index]) : _order;
        int start = anchored ? 0 : (int)(H(2, (int)kind * 41 + index, 3811) * order.Count);

        for (int n = 0; n < order.Count; n++)
        {
            var c = _cand[order[(start + n) % order.Count]];
            if (!Suitable(kind, c)) continue;
            if (WildSiteSpawner.NearPad(c.x, c.z, fp, 0f)) continue;      // 和蒙德的地台也不许撞（保险）
            if (NearOwnPad(c.x, c.z, fp, 0f)) continue;

            Sites.Add(new LiyueSite
            {
                kind = kind,
                x = c.x, z = c.z, y = c.y,
                yaw = H((int)c.x, (int)c.z, 3501) * 360f,
                variant = H((int)c.x, (int)c.z, 3502),
                radius = fp,
            });
            return;
        }

        Debug.LogWarning($"[LiyueSiteSpawner] {LiyueBuildings.DisplayName(kind)} 没找到合适的位置" +
                         $"（璃月能盖房的平地不多，候选被前面的建筑占光了）。");
    }

    /// <summary>候选顺序改成"离锚点最近优先"（同距离用哈希打散，保证确定性）。只给村落用。</summary>
    static readonly List<int> _anchored = new List<int>();

    static List<int> OrderNear(Vector2 anchor)
    {
        _anchored.Clear();
        _anchored.AddRange(_order);

        _anchored.Sort((a, b) =>
        {
            float da = (_cand[a].x - anchor.x) * (_cand[a].x - anchor.x) + (_cand[a].z - anchor.y) * (_cand[a].z - anchor.y);
            float db = (_cand[b].x - anchor.x) * (_cand[b].x - anchor.x) + (_cand[b].z - anchor.y) * (_cand[b].z - anchor.y);
            if (da != db) return da < db ? -1 : 1;

            return H((int)_cand[a].x, (int)_cand[a].z, 3701).CompareTo(H((int)_cand[b].x, (int)_cand[b].z, 3701));
        });

        return _anchored;
    }

    /// <summary>这一种允许的最大坡度（房子要真平，遗迹可以稍微野一点）</summary>
    static float MaxSlope(LiyueKind kind)
    {
        switch (kind)
        {
            case LiyueKind.Village:    return 0.10f;
            case LiyueKind.Shrine:     return 0.10f;
            case LiyueKind.Pagoda:     return 0.12f;
            case LiyueKind.TerraceHut: return 0.12f;
            default:                   return 0.18f;
        }
    }

    /// <summary>这一点的地形 / 位置能不能放这种东西（水边的两种走 WaterfrontOk，不经过这里）</summary>
    static bool Suitable(LiyueKind kind, Cand c)
    {
        float fp = LiyueBuildings.Footprint(kind);
        float reach = PadRadius(fp) + Falloff(fp);      // 地台的完整影响半径

        if (!OpenWorld.InBounds(c.x, c.z)) return false;
        if (!OpenWorld.IsLiyueLand(c.x, c.z)) return false;                 // 只在璃月（分界带不建）
        if (c.snow > 0.05f) return false;
        if (c.y < OpenWorld.WaterLevel + 2.8f) return false;
        if (c.slope > MaxSlope(kind)) return false;
        if (OpenWorld.IsFrozenLake(c.x, c.z)) return false;
        if (MondstadtTown.IsRoad(c.x, c.z)) return false;
        if (OpenWorld.OnTownStreet(c.x, c.z)) return false;

        if (InHarborZone(c.x, c.z)) return false;                           // 港口自己那片，整片让开

        // 离地图边界留出整个占地（村落占地 30，贴边会把篱笆铺到地图外面去）
        if (c.x - fp < 6f || c.z - fp < 6f) return false;
        if (c.x + fp > OpenWorld.Size - 6f || c.z + fp > OpenWorld.Size - 6f) return false;

        // 河：按地台影响范围避让，不然地台会把水面削掉一块
        if (Mathf.Abs(c.z - OpenWorld.RiverZ(c.x)) < OpenWorld.RiverHalfWidth + reach) return false;

        if (!FlatEnough(kind, c, reach)) return false;
        return RegionOk(kind, c);
    }

    /// <summary>这一点离已有的地台太近了吗（璃月自己的；蒙德那边由 WildSiteSpawner.NearPad 管）</summary>
    public static bool NearOwnPad(float x, float z, float fp, float margin)
    {
        for (int i = 0; i < Sites.Count; i++)
        {
            var s = Sites[i];
            if (Padless(s.kind)) continue;
            float need = PadGap(fp, s.radius) + margin;
            float dx = x - s.x, dz = z - s.z;
            if (dx * dx + dz * dz < need * need) return true;
        }
        return false;
    }

    /// <summary>这块地本身够不够平：在地台过渡带的外沿取一圈自然地形，和地台高度最多差多少
    /// （差得多 = 台坎很陡，一眼看出是人工整出来的）。阈值按地台大小缩放，和蒙德同一条口径。</summary>
    static bool FlatEnough(LiyueKind kind, Cand c, float reach)
    {
        float fp = LiyueBuildings.Footprint(kind);
        float allow = Mathf.Clamp(2.4f + fp * 0.07f, 2.4f, 4.5f);

        // 遗迹本来就该长在荒地 / 石林里，地面起伏大一点更像废墟
        if (kind == LiyueKind.StoneForest) allow *= 1.5f;

        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            float h = OpenWorld.Height(c.x + Mathf.Cos(a) * reach, c.z + Mathf.Sin(a) * reach);
            if (Mathf.Abs(h - c.y) > allow) return false;
        }
        return true;
    }

    /// <summary>每种建筑的"脾气"：梯田农舍要在梯田上、石林要在峰林里、楼阁立在高处……</summary>
    static bool RegionOk(LiyueKind kind, Cand c)
    {
        switch (kind)
        {
            case LiyueKind.Village:    return c.forest < 0.78f && c.y < 42f;
            case LiyueKind.Shrine:     return c.y > OpenWorld.WaterLevel + 6f;
            case LiyueKind.StoneForest: return NearKarst(c.x, c.z, 160f);
            case LiyueKind.Pagoda:     return c.y < 48f;
            case LiyueKind.WaterMill:  return c.water;
            case LiyueKind.TerraceHut: return OpenWorld.TerraceWeight(c.x, c.z) > 0.25f;
            case LiyueKind.Paifang:    return NearSites(LiyueKind.Village, c.x, c.z, 170f);
            case LiyueKind.TeaStall:   return Mathf.Abs(c.z - OpenWorld.RiverZ(c.x)) < 150f;
            default:                   return true;
        }
    }

    /// <summary>附近有没有某一类已经铺好的璃月建筑（牌坊要立在村口）</summary>
    static bool NearSites(LiyueKind kind, float x, float z, float radius)
    {
        for (int i = 0; i < Sites.Count; i++)
        {
            if (Sites[i].kind != kind) continue;
            float dx = x - Sites[i].x, dz = z - Sites[i].z;
            if (dx * dx + dz * dz < radius * radius) return true;
        }
        return false;
    }

    // ================= 靠水的两种：吊脚楼 / 码头 =================
    //
    // 这两种的几何都朝本地 +z 铺开（吊脚楼前排木柱扎进水里、码头的栈桥伸进水里），
    // 所以选点要做两件事：① 找到岸线；② 把 yaw 摆成"本地 +z 指向水"。
    // 不垫地台 —— 垫了会把水填平，前排柱子就扎不到水里了。

    static void TryPlaceWaterfront(LiyueKind kind, int index)
    {
        float fp = LiyueBuildings.Footprint(kind);

        int start = (int)(H(3, (int)kind * 53 + index, 3821) * _order.Count);

        for (int n = 0; n < _order.Count; n++)
        {
            var c = _cand[_order[(start + n) % _order.Count]];
            if (!c.water) continue;                                  // 26 米内没水，直接跳过（省掉八向探水）
            if (!OpenWorld.InBounds(c.x, c.z)) continue;
            if (!OpenWorld.IsLiyueLand(c.x, c.z)) continue;
            if (c.snow > 0.05f) continue;
            if (InHarborZone(c.x, c.z)) continue;
            if (c.x - fp < 6f || c.z - fp < 6f) continue;
            if (c.x + fp > OpenWorld.Size - 6f || c.z + fp > OpenWorld.Size - 6f) continue;

            // 八个方向里挑第一个成立的：水面必须在本地 +z 那一侧
            for (int d = 0; d < 8; d++)
            {
                float ang = d * Mathf.PI * 0.25f;
                float dx = Mathf.Sin(ang), dz = Mathf.Cos(ang);
                if (!WaterfrontOk(kind, c, dx, dz)) continue;

                // ★ 水边这几种没有地台，NearOwnPad 不管它们 —— 所以这里要按"离最近一处建筑的距离"
                //   自己拉开，否则第二处码头会挑到和第一处同一段岸线（第一版两处码头就完全重合了）
                if (NearLiyueSite(c.x, c.z, 12f)) continue;
                if (WildSiteSpawner.NearPad(c.x, c.z, fp, 0f)) continue;
                if (NearOwnPad(c.x, c.z, fp, 0f)) continue;

                Sites.Add(new LiyueSite
                {
                    kind = kind,
                    x = c.x, z = c.z, y = c.y,
                    yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg,        // 本地 +z → 水
                    variant = H((int)c.x, (int)c.z, 3512 + d),
                    radius = fp,
                });
                return;
            }
        }

        Debug.LogWarning($"[LiyueSiteSpawner] {LiyueBuildings.DisplayName(kind)} 没找到合适的岸线" +
                         $"（要岸够平、水又刚好在正面）。");
    }

    /// <summary>把 (x,z) 沿 (dx,dz) 量一条剖面，判断这里够不够格放"靠水的建筑"。
    /// 本地 +z 就是 (dx,dz)；身后是岸、身前是水。</summary>
    static bool WaterfrontOk(LiyueKind kind, Cand c, float dx, float dz)
    {
        const float water = OpenWorld.WaterLevel;

        // 侧面（本地 ±x）的平整度：岸台 / 木屋都横着铺 5~9 米，左右别是斜坡
        float lx = dz, lz = -dx;                                  // 本地 +x 方向
        if (Mathf.Abs(Probe(c.x + lx * 4.5f, c.z + lz * 4.5f) - c.y) > 0.9f) return false;
        if (Mathf.Abs(Probe(c.x - lx * 4.5f, c.z - lz * 4.5f) - c.y) > 0.9f) return false;

        if (kind == LiyueKind.StiltHouse)
        {
            // 岸要低：木柱总长 6.6 米、楼面 3.6 米，柱脚在 c.y - 3.0 —— 岸太高柱子就扎不到水下
            if (c.y < water + 0.6f || c.y > water + 3.0f) return false;
            if (Probe(c.x - dx * 4f, c.z - dz * 4f) < c.y - 1.4f) return false;      // 身后还得是岸
            if (Probe(c.x + dx * 3f, c.z + dz * 3f) > water - 0.25f) return false;   // 身前 3 米必须已经是水
            if (Probe(c.x + dx * 9f, c.z + dz * 9f) > water - 0.8f) return false;    // 9 米外要有 0.8 米水深
            return true;                                                            // （浅水田不算：柱脚会落在田里）
        }

        // 码头：岸台在岸上、栈桥伸进水里（栈桥铺到本地 +z 11.4 米）
        if (c.y < water + 1.0f) return false;
        if (Mathf.Abs(Probe(c.x + dx * 2f, c.z + dz * 2f) - c.y) > 1.0f) return false;   // 岸台底下要平
        if (Probe(c.x + dx * 6f, c.z + dz * 6f) > water - 0.25f) return false;           // 6 米外是水
        if (Probe(c.x + dx * 13f, c.z + dz * 13f) > water - 0.8f) return false;          // 13 米外要有 0.8 米水深
        if (Probe(c.x - dx * 7f, c.z - dz * 7f) < c.y - 1.8f) return false;              // 身后是岸
        return true;
    }

    static float Probe(float x, float z) => OpenWorld.Height(x, z);

    // ================= 石拱桥（跨在河上，没有地台） =================
    //
    // 和蒙德的木桥一个思路：**沿河自己找过河点**，不写死坐标。
    // ★ 璃月的河就是城南那条河（OpenWorld.HeightRaw 里那段挖河对全图都生效），
    //   所以璃月侧的过河点在 z ≈ 132 那条带子上。
    //
    // 起拱线高度 c.y 取"河心地面高度"（和蒙德的木桥一样，几何里桥面从 c.y 往上长）：
    //   · 拱脚在 c.y（河床附近，水下）→ 拱券从水里冒出来、拱顶正好顶到桥面下沿 —— 就是石拱桥的样子
    //   · 所以额外要求"桥面高度 ≈ 两岸高度"，否则要么埋进水里、要么在岸上架一座高塔

    struct Crossing
    {
        public float x, z;        // 河心
        public float half;        // 半跨
        public float y;           // 起拱线（= 河心地面高度）
        public float yaw;         // 本地 +z 指向远的那一岸
        public float score;       // 越小越好（两岸高差 + 离港口的距离）
    }

    static void PlaceBridges()
    {
        var found = new List<Crossing>();

        // ① 河：河心位置是解析的（RiverZ），沿着 x 直接扫 —— 河的水面带只有 ±4 米宽，
        //    8 米一格的网格几乎必然整条漏掉（第一版就是把河漏掉了，最后架到了内陆水潭上）
        for (float x = 800f; x < OpenWorld.Size - 70f; x += 8f)
        {
            float zc = OpenWorld.RiverZ(x);
            if (!OpenWorld.IsLiyueLand(x, zc)) continue;
            if (InHarborZone(x, zc)) continue;

            if (TryCrossing(x, zc, 0f, 1f, out var cr))
            {
                cr.score -= 40f;                                  // 河上的桥优先：玩家过河才遇得到
                found.Add(cr);
            }
        }

        // ② 内陆水域（峰林里的水潭之类）：撒网格 × 四个轴线方向
        for (float x = 800f; x < OpenWorld.Size - 40f; x += 8f)
        {
            for (float z = 40f; z < OpenWorld.Size - 40f; z += 8f)
            {
                if (Mathf.Abs(z - OpenWorld.RiverZ(x)) < OpenWorld.RiverHalfWidth + 8f) continue;   // 河那段①扫过了
                if (!OpenWorld.IsLiyueLand(x, z)) continue;
                if (InHarborZone(x, z)) continue;
                if (OpenWorld.Height(x, z) > OpenWorld.WaterLevel - 0.35f) continue;                // 要在有水深的地方

                for (int a = 0; a < 4; a++)                                                        // 四个轴线方向
                {
                    float ang = a * 45f * Mathf.Deg2Rad;
                    if (TryCrossing(x, z, Mathf.Sin(ang), Mathf.Cos(ang), out var cr)) found.Add(cr);
                }
            }
        }

        found.Sort((a, b) => a.score.CompareTo(b.score));

        int placed = 0;
        foreach (var c in found)
        {
            if (placed >= BridgeCount) break;

            // 桥两端要留出落脚的地方：别的建筑挨在桥头会看着挤（水边那几种没有地台，不会互相拉歪）
            if (NearLiyueSite(c.x, c.z, 25f)) continue;

            bool tooClose = false;
            foreach (var s in Sites)
                if (s.kind == LiyueKind.ArchBridge &&
                    Mathf.Sqrt((s.x - c.x) * (s.x - c.x) + (s.z - c.z) * (s.z - c.z)) < 200f) { tooClose = true; break; }
            if (tooClose) continue;

            Sites.Add(new LiyueSite
            {
                kind = LiyueKind.ArchBridge,
                x = c.x, z = c.z, y = c.y,
                yaw = c.yaw,
                variant = H((int)c.x, (int)c.z, 3601),
                radius = LiyueBuildings.Footprint(LiyueKind.ArchBridge),
                param = c.half,                                  // 半跨（BuildArchBridge 用它算起拱高度）
            });

            placed++;
            Debug.Log($"[LiyueSiteSpawner] 石拱桥：选在 ({c.x:0},{c.z:0})，" +
                      $"半跨 {c.half:0.0} 米、桥面高 {c.y + c.half * 0.42f + 0.36f:0.0} 米（河心地面 {c.y:0.0} 米）");
        }

        if (placed < BridgeCount)
            Debug.LogWarning($"[LiyueSiteSpawner] 石拱桥只找到 {placed}/{BridgeCount} 处合适的过河点" +
                             $"（候选 {found.Count} 处）—— 河太宽 / 两岸高差太大的地方不适合架平拱桥。");
    }

    /// <summary>在 (x,z) 沿 (dx,dz) 这一条轴上找一处过河点：两岸都要在 26 米内、都是璃月的陆地</summary>
    static bool TryCrossing(float x, float z, float dx, float dz, out Crossing cr)
    {
        cr = default;

        const float Max = 26f;
        float water = OpenWorld.WaterLevel;

        float pDist = 0f, pG = 0f, nDist = 0f, nG = 0f;
        bool pOk = false, nOk = false;

        for (float d = 1f; d <= Max; d += 1f)
        {
            if (!pOk)
            {
                float h = OpenWorld.Height(x + dx * d, z + dz * d);
                if (h > water + 1.6f) { pDist = d; pG = h; pOk = true; }
            }
            if (!nOk)
            {
                float h = OpenWorld.Height(x - dx * d, z - dz * d);
                if (h > water + 1.6f) { nDist = d; nG = h; nOk = true; }
            }
            if (pOk && nOk) break;
        }

        if (!pOk || !nOk) return false;
        if (Mathf.Min(pDist, nDist) < 3f) return false;                 // 太窄，不是河道
        if (Mathf.Abs(pG - nG) > 4f) return false;                      // 两岸高差太大 → 平拱桥会一头突兀

        float half = Mathf.Max(pDist, nDist) + 3.5f;                    // 再往岸里多铺一点
        if (half < 8f || half > 22f) return false;

        float sign = pDist >= nDist ? 1f : -1f;                         // 本地 +z 指向远的那一岸
        float bx = dx * sign, bz = dz * sign;
        float yaw = Mathf.Atan2(bx, bz) * Mathf.Rad2Deg;

        // 拱脚（c.y）取河心地面高度 → 桥面 = c.y + 半跨×0.42 + 0.36。
        // 桥面要和两岸齐平才对：太高像高塔、太低拱顶会冒出桥面
        float y = OpenWorld.Height(x, z);
        float deck = y + half * 0.42f + 0.36f;
        float bank = Mathf.Max(pG, nG);

        if (deck < water + 1.2f) return false;                          // 桥面离水面太近
        if (Mathf.Abs(deck - bank) > 2.6f) return false;                // 桥面和两岸齐平（±2.6 米）

        // 两岸都是璃月的地（免得桥一头架在蒙德的地界上）
        if (!OpenWorld.IsLiyueLand(x + bx * pDist, z + bz * pDist)) return false;
        if (!OpenWorld.IsLiyueLand(x + bx * (pDist + 12f), z + bz * (pDist + 12f))) return false;

        float awayHarbor = Mathf.Sqrt((x - LiyueHarbor.HarborX) * (x - LiyueHarbor.HarborX) +
                                      (z - LiyueHarbor.HarborZ) * (z - LiyueHarbor.HarborZ));

        cr = new Crossing
        {
            x = x, z = z, half = half, y = y, yaw = yaw,
            score = Mathf.Abs(pG - nG) * 4f + awayHarbor * 0.02f,       // 条件一样好时优先离港口近的那座
        };
        return true;
    }

    // ================= 自检报告 =================

    /// <summary>规划结果的可读报告（编辑器菜单 VoxelDemo → 验证野外建筑选点 会一起打出来）</summary>
    public static string PlanReport()
    {
        var sb = new System.Text.StringBuilder();

        sb.Append("计划：");
        foreach (LiyueKind kind in System.Enum.GetValues(typeof(LiyueKind)))
        {
            int want = Want(kind);
            int got = Count(kind);
            if (want == 0 && got == 0) continue;
            sb.Append($"{LiyueBuildings.DisplayName(kind)} {got}/{want}；");
        }
        sb.AppendLine();

        // 分条件诊断：铺不下时一眼看出是哪一条把候选点全筛掉了
        {
            var kind = LiyueKind.Village;
            float fp = LiyueBuildings.Footprint(kind);
            float reach = PadRadius(fp) + Falloff(fp);
            int liyue = 0, okHeight = 0, okSlope = 0, okHarbor = 0, okRiver = 0, okFlat = 0, okRegion = 0;

            foreach (var c in _cand)
            {
                if (!OpenWorld.IsLiyueLand(c.x, c.z)) continue;
                liyue++;
                if (c.y >= OpenWorld.WaterLevel + 2.8f && c.snow <= 0.05f) okHeight++;
                if (c.slope <= MaxSlope(kind)) okSlope++;
                if (!InHarborZone(c.x, c.z)) okHarbor++;
                if (Mathf.Abs(c.z - OpenWorld.RiverZ(c.x)) >= OpenWorld.RiverHalfWidth + reach) okRiver++;
                if (FlatEnough(kind, c, reach)) okFlat++;
                if (RegionOk(kind, c)) okRegion++;
            }

            sb.AppendLine($"  【诊断·按村落】璃月的地 {liyue} / 高度坡度够 {okHeight} / 不在港口区 {okHarbor} / " +
                          $"离河够远 {okRiver} / 地够平 {okFlat} / 区域合适 {okRegion}");
        }

        sb.AppendLine($"候选点 {_cand.Count} 个，地台 {CountPads()} 块，共铺下 {Sites.Count} 处：");

        float worstMargin = float.MaxValue;
        float worstStep = 0f;
        int problems = 0;

        for (int i = 0; i < Sites.Count; i++)
        {
            var s = Sites[i];
            var name = LiyueBuildings.DisplayName(s.kind);

            float margin = float.MaxValue;
            for (int k = 0; k < Sites.Count; k++)
            {
                if (k == i) continue;
                var o = Sites[k];
                bool padless = Padless(s.kind) || Padless(o.kind);
                float need = padless
                    ? s.radius * 0.5f + o.radius * 0.5f                  // 水边 / 跨水的按占地算
                    : PadGap(s.radius, o.radius);
                float d = Vector2.Distance(new Vector2(s.x, s.z), new Vector2(o.x, o.z));
                margin = Mathf.Min(margin, d - need);
            }
            if (Sites.Count < 2) margin = 999f;

            // 台坎：临时关掉地台量"没有地台时的自然地形"，和地台高度差多少（= 玩家会看到多高的台坎）
            float step = 0f;
            float naturalSlope = 0f;
            if (!Padless(s.kind))
            {
                OpenWorld.IgnorePads = true;
                float rim = s.radius + 6f + Falloff(s.radius);
                for (int a = 0; a < 12; a++)
                {
                    float ang = a * Mathf.PI * 2f / 12f;
                    float h = OpenWorld.HeightRaw(s.x + Mathf.Cos(ang) * rim, s.z + Mathf.Sin(ang) * rim);
                    step = Mathf.Max(step, Mathf.Abs(h - s.y));
                }
                naturalSlope = OpenWorld.Slope(s.x, s.z);
                OpenWorld.IgnorePads = false;
            }

            worstMargin = Mathf.Min(worstMargin, margin);
            worstStep = Mathf.Max(worstStep, step);

            if (margin < 0f) problems++;
            if (step > 5f) problems++;

            string extra = s.kind == LiyueKind.ArchBridge
                ? $"  半跨 {s.param:0.0}m  桥面高 {s.y + s.param * 0.42f + 0.36f:0.0}m"
                : "";

            sb.AppendLine($"  {name} ({s.x:0},{s.z:0})  地台高 {s.y:0.0}m  朝向 {s.yaw:0}°  " +
                          $"离最近邻居 {margin:0.0}m  台坎 {step:0.0}m  原地坡度 {naturalSlope:0.000}{extra}");
        }

        sb.AppendLine($"最小邻居余量 {worstMargin:0.0}m（负数 = 地台重叠，建筑会浮空），" +
                      $"最大台坎 {worstStep:0.0}m（> 5m 会看出明显的人工台坎），" +
                      $"地台重叠 {CountPadOverlaps()} 处，异常 {problems} 项");
        return sb.ToString();
    }

    static int Want(LiyueKind kind)
    {
        int n = 0;
        switch (kind)
        {
            case LiyueKind.StiltHouse: return StiltHouseCount;
            case LiyueKind.Dock:       return DockCount;
            case LiyueKind.ArchBridge: return BridgeCount;
        }
        foreach (var k in Plan) if (k == kind) n++;
        return n;
    }

    static int Count(LiyueKind kind)
    {
        int n = 0;
        foreach (var s in Sites) if (s.kind == kind) n++;
        return n;
    }

    /// <summary>璃月自己的地台重叠数 + 和蒙德地台撞上的次数（正常必须是 0）</summary>
    public static int CountPadOverlaps()
    {
        int bad = 0;

        for (int i = 0; i < Sites.Count; i++)
        {
            if (Padless(Sites[i].kind)) continue;
            if (WildSiteSpawner.NearPad(Sites[i].x, Sites[i].z, Sites[i].radius, 0f)) bad++;

            for (int j = i + 1; j < Sites.Count; j++)
            {
                if (Padless(Sites[j].kind)) continue;
                float need = PadGap(Sites[i].radius, Sites[j].radius);
                float dx = Sites[i].x - Sites[j].x, dz = Sites[i].z - Sites[j].z;
                if (dx * dx + dz * dz < need * need) bad++;
            }
        }
        return bad;
    }

    /// <summary>这一点是不是落在某处璃月建筑的"地台影响范围"里（平整区 + 边坡带 + extra）。
    /// 宝箱 / 怪物 / 解密场地用它避让（水边和跨水的三种没有地台，按占地半径算）。</summary>
    public static bool InsideSiteArea(float x, float z, float extra)
    {
        for (int i = 0; i < Sites.Count; i++)
        {
            var s = Sites[i];
            float need = Padless(s.kind)
                ? s.radius + extra
                : PadRadius(s.radius) + Falloff(s.radius) + extra;
            float dx = x - s.x, dz = z - s.z;
            if (dx * dx + dz * dz < need * need) return true;
        }
        return false;
    }

    /// <summary>附近有没有璃月建筑（其它生成器用，比 InsideSiteArea 宽松一点）</summary>
    public static bool NearLiyueSite(float x, float z, float extra)
    {
        for (int i = 0; i < Sites.Count; i++)
        {
            float need = Sites[i].radius + extra;
            float dx = x - Sites[i].x, dz = z - Sites[i].z;
            if (dx * dx + dz * dz < need * need) return true;
        }
        return false;
    }

    // ================= 落地 =================

    public void Init(OpenWorld world, Transform player)
    {
        _material = world.Material;

        _root = new GameObject("LiyueBuildings").transform;
        _root.SetParent(transform, false);

        if (Sites.Count == 0)
        {
            Debug.LogWarning("[LiyueSiteSpawner] 没有已规划的璃月建筑（PlanSites 没跑？），跳过落地。");
            return;
        }

        // ---- 几何：全部合成一个网格，三个子网格共用城里的石墙 / 瓦片 / 顶点色材质 ----
        // 石拱桥的 y 是"起拱线"，用不了 SurfaceY（那是河床），其余几种落在地面上
        LiyueBuildings.Begin();
        foreach (var s in Sites)
        {
            float y = s.kind == LiyueKind.ArchBridge ? s.y : world.SurfaceY(s.x, s.z);
            LiyueBuildings.Append(s.kind, new Vector3(s.x, y, s.z), s.yaw, s.variant, s.param);
        }

        var mesh = LiyueBuildings.BuildMesh("LiyueBuildings");
        var go = new GameObject("LiyueBuildingMesh");
        go.transform.SetParent(_root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { world.StoneMaterial, world.RoofMaterial, world.Material };
        go.AddComponent<MeshCollider>().sharedMesh = mesh;

        // ---- 火苗：茶棚的炉子、码头的火盆 ----
        foreach (var p in LiyueBuildings.Flames)
        {
            var flame = new GameObject("Flame").transform;
            flame.SetParent(_root, false);
            flame.position = p;
            MonsterSpawner.BuildFlame(flame, _material);
            _flames.Add(flame);
        }

        // ---- 地图标记 + 大色块（村落能看到一片黛瓦） ----
        int villages = 0;
        foreach (var s in Sites)
        {
            MondstadtTown.Markers.Add(new MondstadtTown.MapMarker
            {
                name = LiyueBuildings.DisplayName(s.kind),
                x = s.x, z = s.z,
                kind = LiyueBuildings.MarkerKind(s.kind),
            });

            if (s.kind == LiyueKind.Village) villages++;
        }

        foreach (var h in LiyueBuildings.Houses)
            MondstadtTown.Blobs.Add(new MondstadtTown.MapBlob { x = h.x, z = h.y, r = h.z, color = LiyueBuildings.BlobColor(LiyueKind.Village) });

        foreach (var s in Sites)
        {
            if (s.kind == LiyueKind.Village)
                MondstadtTown.Blobs.Add(new MondstadtTown.MapBlob { x = s.x, z = s.z, r = s.radius * 0.85f, color = 1 });
            else if (s.kind == LiyueKind.Shrine || s.kind == LiyueKind.StoneForest || s.kind == LiyueKind.Pagoda)
                MondstadtTown.Blobs.Add(new MondstadtTown.MapBlob { x = s.x, z = s.z, r = s.radius * 0.8f, color = 2 });
        }

        // ---- 村民：每座村落 1~2 个（和蒙德那边同一套 NPC，不碰 NavMesh） ----
        foreach (var s in Sites)
            if (s.kind == LiyueKind.Village) SpawnVillagers(world, player, s);

        // ---- 璃月港（阶段 E）：港城自己一整块网格 ----
        // 地形（台地 + 内外湾）在 LiyueHarbor.ShapeTerrain 里已经压好了，这里只往上摆东西。
        // 位置全部按 LiyueHarbor 的常量算 —— 不用选点、不用地台，所以也不受地台重叠那套约束
        LiyueBuildings.Begin();
        LiyueBuildings.AppendHarbor();

        var harborMesh = LiyueBuildings.BuildMesh("LiyueHarbor");
        var harborGo = new GameObject("LiyueHarborMesh");
        harborGo.transform.SetParent(_root, false);
        harborGo.AddComponent<MeshFilter>().sharedMesh = harborMesh;

        var harborRenderer = harborGo.AddComponent<MeshRenderer>();
        harborRenderer.sharedMaterials = new[] { world.StoneMaterial, world.RoofMaterial, world.Material };
        harborGo.AddComponent<MeshCollider>().sharedMesh = harborMesh;

        foreach (var p in LiyueBuildings.Flames)
        {
            var flame = new GameObject("HarborFlame").transform;
            flame.SetParent(_root, false);
            flame.position = p;
            MonsterSpawner.BuildFlame(flame, _material);
            _flames.Add(flame);
        }

        LiyueBuildings.HarborMarkers();

        int harborFaces = LiyueBuildings.SubTris[0].Count / 3 + LiyueBuildings.SubTris[1].Count / 3 +
                          LiyueBuildings.SubTris[2].Count / 3;

        Debug.Log($"[LiyueSiteSpawner] 落地完成：{Sites.Count} 处璃月野外建筑，{_flames.Count} 处火苗，" +
                  $"村落 {villages} 座，{_villagers} 个村民，" +
                  $"{LiyueBuildings.SubTris[0].Count / 3 + LiyueBuildings.SubTris[1].Count / 3 + LiyueBuildings.SubTris[2].Count / 3} 个面");
        Debug.Log($"[LiyueSiteSpawner] 璃月港（阶段 E）落地完成：顶点 {harborMesh.vertexCount}，面 {harborFaces}，" +
                  $"地标 {MondstadtTown.Markers.Count} 个（含野外）");
    }

    void SpawnVillagers(OpenWorld world, Transform player, LiyueSite s)
    {
        int count = 1 + (int)(H((int)s.x, (int)s.z, 3901) * 2f);

        for (int i = 0; i < count; i++)
        {
            float a = (s.yaw + i * 141f + H(i, (int)s.x, 3902) * 50f) * Mathf.Deg2Rad;
            float r = 5.5f + H(i, (int)s.z, 3903) * 6.5f;
            float x = s.x + Mathf.Cos(a) * r;
            float z = s.z + Mathf.Sin(a) * r;

            for (int k = 0; k < 8; k++)
            {
                if (OpenWorld.InBounds(x, z) &&
                    OpenWorld.Height(x, z) > OpenWorld.WaterLevel + 1f &&
                    OpenWorld.Slope(x, z) < 0.35f) break;

                x = Mathf.Lerp(x, s.x, 0.45f);
                z = Mathf.Lerp(z, s.z, 0.45f);
            }

            var go = new GameObject($"LiyueVillager{i}");
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(x, world.SurfaceY(x, z), z);

            var npc = go.AddComponent<VoxelNpc>();
            npc.data = WildSiteSpawner.VillagerData(i);
            npc.Init(_material, player, world);

            _villagers++;
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

    static float H(int a, int b, int salt) => LowPoly.Hash01(a, b, salt);
}
