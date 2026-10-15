using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 野外建筑的选点与落地（村落 / 设施 / 遗迹，几何在 WildBuildings 里）。
///
/// 【为什么选点要提前到 BeforeSceneLoad】
/// 野外建筑脚下要有一块平整的地台，否则一整座村子会斜在坡上。地台是在 OpenWorld.HeightRaw 里
/// 混合出来的，而高度场在 OpenWorld.Awake 里烘一次 —— 所以选址必须发生在"烘高度场之前"。
/// 于是这里用 [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] 先把点选完、把地台登记好，
/// 等 OpenWorld.Awake 烘高度场时地形就已经让出平地了（和城岛台地、大教堂高台同一个套路）。
///
/// 【选点规则】全部确定性哈希，每次进游戏一样：
///   · 只在平原（x+z &lt; 470）、雪线以下、坡度小、离水面够高的草地上
///   · 避开城 / 护城河 / 大教堂高台 / 湖 / 河 / 冰湖 / 马路 / 出生点
///   · 互相之间按"占地半径 + 间距"拉开；挑点用"离已选点最远优先"，保证在大地图上铺得开
/// 木桥是特例：它必须正好压在河上，所以单独摆、也不给它垫地台（否则河床会被填平）。
/// </summary>
public class WildSiteSpawner : MonoBehaviour
{
    /// <summary>一处野外建筑</summary>
    public struct WildSite
    {
        public WildKind kind;
        public float x, z;         // 平面位置
        public float y;            // 地台高度（= 选址当时的自然地面高度）
        public float yaw;          // 朝向
        public float variant;      // 0~1 的造型变体
        public float radius;       // 占地半径（其它生成器要按这个避让）
        public float param;        // 少数建筑的额外尺寸（木桥：半跨长；其余为 0）
    }

    /// <summary>所有野外建筑的位置。怪物 / 宝箱 / 解密场地都要避开这里</summary>
    public static readonly List<WildSite> Sites = new List<WildSite>();

    // 和 OpenWorld.HeightRaw 里那几个盆地 / 高台对齐的常量（那边是私有的，这里复制一份并保持同步）
    const float LakeX = 85f, LakeZ = 118f, LakeR = 56f;
    // 和 OpenWorld.SpawnPoint 对齐（那边是实例属性，规划阶段还没有实例）
    const float SpawnX = MondstadtTown.CenterX, SpawnZ = 100f;

    // ---------- 地台几何 ----------
    //
    // ★ 地台为什么会互相打架：HeightRaw 里每块地台都是 Lerp(h, pad.y, t)，
    //   两块地台重叠时后面的会把前面的往自己高度上拉。于是同一栋建筑跨在两块地台上时，
    //   零件一半在这个高度、一半在那个高度 —— 看着就是"很多地方浮空 / 陷地"。
    //   所以要保证：**任何一块地台的平整区，都不被别人的边坡过渡带碰到**。
    //
    //   A 的平整区不被 B 影响 ⇒ d ≥ 平整半径(A) + 平整半径(B) + 过渡带(B)
    //   两边都要成立 ⇒ d ≥ A+B 的平整半径之和 + max(两边过渡带)

    /// <summary>地台的平整半径</summary>
    public static float PadRadius(float fp) => fp + 6f;

    /// <summary>两块地台共存的最近距离（再近就会互相拉歪，跨在两块上的建筑会浮空）</summary>
    public static float PadGap(float fpA, float fpB) =>
        PadRadius(fpA) + PadRadius(fpB) + Mathf.Max(Falloff(fpA), Falloff(fpB));

    /// <summary>每种的铺法：数量 + 先后顺序。顺序 = 挑地方的优先级，
    /// 越挑地方（要高、要平、占地大）越排前面，小件最后见缝插针。
    ///
    /// 数量是按"地台不许互相重叠"定的：每块地台连同过渡带要独占一圈空地，
    /// 两块挨太近时后登记的会把前一块往自己高度上拉 —— 跨在两块上的建筑就会一半高一半低（浮空）。
    /// 所以这个数量和地图尺寸是绑死的：600 米最多铺 15 处，扩到 850 米（面积翻倍）才放得下 24 处。
    /// 自检里的"全部铺下 N/N + 地台重叠 0 处"是这两条的红线。</summary>
    static readonly WildKind[] Plan =
    {
        WildKind.Village, WildKind.Village, WildKind.Village,
        WildKind.WindShrine, WildKind.WindShrine,                       // 要高要平，排在前面先挑
        WildKind.Windmill, WildKind.Windmill,
        WildKind.Watchtower, WildKind.Watchtower, WildKind.Watchtower,
        WildKind.WaterMill, WildKind.WaterMill,
        WildKind.HuntersHut, WildKind.HuntersHut,
        WildKind.LumberCamp, WildKind.LumberCamp,
        WildKind.RuinPillars, WildKind.RuinPillars,
        WildKind.RuinWall, WildKind.RuinWall,
        WildKind.RestCamp, WildKind.RestCamp, WildKind.RestCamp, WildKind.RestCamp,
    };

    /// <summary>三座村落的片区锚点：北偏西 / 北偏东 / 西南。
    /// 不指定的话三座村子会挤在河以北那条带子里（河南岸被护城河和地台影响范围切掉一大块）</summary>
    static readonly Vector2[] VillageAnchors =
    {
        new Vector2(200f, 45f),
        new Vector2(390f, 50f),
        new Vector2(90f, 320f),
    };

    // ---------- 候选点缓存 ----------

    struct Cand
    {
        public float x, z, y, slope, forest, snow;
        public bool  water;        // 26 米内有没有水面（水车磨坊要挑这种地方）
    }

    static readonly List<Cand> _cand = new List<Cand>();

    /// <summary>候选点的哈希洗牌顺序。挑点时按这个顺序扫，取"第一个和已有建筑都拉得开"的点 ——
    /// 比"离已选点最远优先"自然得多（后者会把建筑全顶到可行区域的边角上，路上看着像围一圈）</summary>
    static readonly List<int> _order = new List<int>();

    // ---------- 实例状态 ----------

    Material _material;
    Transform _root;
    readonly List<Transform> _flames = new List<Transform>();
    int _villagers;

    public static bool Planned { get; private set; }

    // ================= 规划（静态，烘高度场之前跑） =================

    /// <summary>野外内容的统一规划入口：野外建筑 + 解密场地。必须发生在 OpenWorld 烘高度场之前。
    ///
    /// ★ 为什么要有一个统一入口：两个 PlanSites 各自会往 OpenWorld 里登记地台，
    ///   如果各自还顺手 ClearPads() 一次，谁后跑就会把对方刚登记的地台清掉 ——
    ///   而 [RuntimeInitializeOnLoadMethod] 之间的执行顺序是没有保证的。
    ///   所以"清空 + 登记"只在这里做一次。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoPlan() => PlanEverything();

    public static void PlanEverything()
    {
        OpenWorld.ClearPads();
        OpenWorld.ClearKeepOut();
        OpenWorld.DiscardHeightField();     // 必须按解析地形选点，不能用到上一局烘出来的高度场

        PlanSites();
        LiyueSiteSpawner.PlanSites();       // 璃月那边的野外建筑（风格不同，选点规则也另写一套）
        InazumaSiteSpawner.PlanIsles();     // 稻妻：岛是我们自己堆的，布局表直接铺（地台 / 禁植区在这里登记）
        PuzzleSpawner.PlanSites();
        LiyuePuzzleSpawner.PlanSites();     // 璃月那边的解密场地（石灯阵 / 石鼓）
        TeleportSpawner.PlanAnchors();      // 传送锚点：位置取自上面这些规划结果（村口 / 城 / 码头）
    }

    /// <summary>选点 + 登记地台 + 登记禁植区</summary>
    public static void PlanSites()
    {
        Sites.Clear();
        _cand.Clear();

        BuildCandidates();

        // 按 Plan 里列出的顺序铺：先铺大件（村落），后面小件再往空地里塞
        var used = new List<WildKind>();
        foreach (var kind in Plan)
        {
            int already = used.FindAll(k => k == kind).Count;   // 这种已经铺了几个
            TryPlace(kind, already);
            used.Add(kind);
        }

        PlaceBridges();

        // 地台 + 禁植区：所有点选完之后再一次性登记。
        // 中途登记的话，后面的候选点会看见前面垫出来的平地 —— 选点结果就不再可复现了。
        foreach (var s in Sites)
        {
            if (s.kind == WildKind.Bridge) continue;            // 桥压在河上，垫地台会把河床填平
            OpenWorld.AddPad(s.x, s.z, PadRadius(s.radius), s.y, Falloff(s.radius));

            // 禁植区只圈到"建筑自己的占地外面一点点"，**不圈地台的边坡带** ——
            // 边坡带上的树石是按脚下真实地面摆的，不会悬空；圈进去反而会让村子外面秃一大圈，
            // 看着特别空（这是上一版"村庄附近没有植被"的原因）
            OpenWorld.AddKeepOut(s.x, s.z, s.radius + 8f);
        }

        Planned = true;
        Debug.Log($"[WildSiteSpawner] 规划完成：共 {Sites.Count} 处野外建筑（候选点 {_cand.Count} 个，" +
                  $"地台 {CountPads()} 块、禁植区 {CountPads()} 处，固定哈希选点，重进游戏一致）");
    }

    static int CountPads()
    {
        int n = 0;
        foreach (var s in Sites) if (s.kind != WildKind.Bridge) n++;
        return n;
    }

    /// <summary>把整张地图扫一遍，缓存每个候选点的地形数据（后面各种建筑都在这份缓存上筛，省掉重复的地形查询）；
    /// 顺便按哈希把候选点洗一次牌，挑点时就照这个顺序扫</summary>
    static void BuildCandidates()
    {
        for (float x = 22f; x < OpenWorld.Size - 22f; x += 7f)
        {
            for (float z = 22f; z < OpenWorld.Size - 22f; z += 7f)
            {
                float jx = x + (H((int)x, (int)z, 301) - 0.5f) * 6f;
                float jz = z + (H((int)x, (int)z, 302) - 0.5f) * 6f;

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
            H((int)_cand[a].x, (int)_cand[a].z, 701).CompareTo(H((int)_cand[b].x, (int)_cand[b].z, 701)));
    }

    /// <summary>铺一处：按洗好的顺序扫候选点，取第一个"地形合适、又和已有建筑拉得开"的位置。
    /// 村落按各自的片区锚点排顺序（先看锚点附近，不行再往外走）</summary>
    static void TryPlace(WildKind kind, int index)
    {
        float fp = WildBuildings.Footprint(kind);

        // 村落按各自的片区锚点排顺序，从锚点往外走；其它种类按哈希洗牌顺序扫。
        // ★ 每种从洗牌顺序的不同位置开始扫（扫到头绕回开头）：如果所有种类都从下标 0 开始扫，
        //   大家就都落在洗牌开头那一小片区域里 —— 实测会挤成一条线。
        // ★ 按锚点排序时**不能**再旋转起点，否则等于从锚点旁边随机跳到几十米外，锚点就白设了。
        bool anchored = kind == WildKind.Village && index < VillageAnchors.Length;
        var order = anchored ? OrderNear(VillageAnchors[index]) : _order;
        int start = anchored ? 0 : (int)(H(1, (int)kind * 37 + index, 811) * order.Count);

        for (int n = 0; n < order.Count; n++)
        {
            var c = _cand[order[(start + n) % order.Count]];
            if (!Suitable(kind, c)) continue;
            if (NearPad(c.x, c.z, fp, 0f)) continue;        // 地台不许和已有的重叠（否则会互相拉歪）

            Sites.Add(new WildSite
            {
                kind = kind,
                x = c.x, z = c.z, y = c.y,
                yaw = H((int)c.x, (int)c.z, 501) * 360f,
                variant = H((int)c.x, (int)c.z, 502),
                radius = fp,
            });
            return;
        }

        Debug.LogWarning($"[WildSiteSpawner] {WildBuildings.DisplayName(kind)} 没找到合适的位置（候选被前面的建筑占光了）。");
    }

    /// <summary>候选顺序改成"离锚点最近优先"（同距离用哈希打散，保证确定性）。
    /// 只给村落用，一次算一个，用完就丢。
    /// ★ 比较函数必须是严格的（不能带容差）：带容差会破坏传递性，List.Sort 出来的顺序就不可信了。</summary>
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

            return H((int)_cand[a].x, (int)_cand[a].z, 701).CompareTo(H((int)_cand[b].x, (int)_cand[b].z, 701));
        });

        return _anchored;
    }

    /// <summary>地台往外多少米平滑回自然地形。按地台大小给：小台子 8 米就够，
    /// 大台子（村落）给窄过渡带的话边缘就是一道陡坎 —— 但太宽又会削到旁边的河岸，所以卡在 13 米</summary>
    static float Falloff(float fp) => Mathf.Clamp(fp * 0.4f, 8f, 13f);

    /// <summary>这一种允许的最大坡度（房子要真平，遗迹可以稍微野一点）</summary>
    static float MaxSlope(WildKind kind)
    {
        switch (kind)
        {
            case WildKind.Village:    return 0.10f;
            case WildKind.WindShrine: return 0.10f;
            case WildKind.Windmill:   return 0.12f;
            default:                  return 0.18f;
        }
    }

    /// <summary>这一点的地形 / 位置能不能放这种东西</summary>
    static bool Suitable(WildKind kind, Cand c)
    {
        if (!OpenWorld.InBounds(c.x, c.z)) return false;
        if (c.snow > 0.05f) return false;                                   // 雪线以上不盖房
        if (c.y < OpenWorld.WaterLevel + 2.8f) return false;                // 离水面留足余量（地台要削一块地）
        if (c.slope > MaxSlope(kind)) return false;
        if (OpenWorld.IsFrozenLake(c.x, c.z)) return false;
        if (MondstadtTown.IsRoad(c.x, c.z)) return false;
        if (OpenWorld.OnTownStreet(c.x, c.z)) return false;

        // 注：原来这里有一条 "x+z > 560 就别放了（那是雪山）" 的硬边界，
        // 地图扩大 + 加璃月之后这条早就没意义了，改成按"这是哪国"来管（见下面）

        // 这里只管蒙德：璃月那边是另一套建筑风格（喀斯特峰林 + 红柱黛瓦），由 LiyueSiteSpawner 铺。
        // 分界带（LiyueWeight 0.35~0.65）两边都不建 —— 免得德式房子挨着中式房子
        if (!OpenWorld.IsMondstadtLand(c.x, c.z)) return false;

        float fp = WildBuildings.Footprint(kind);
        float reach = fp + 6f + Falloff(fp);       // 这块地台的完整影响半径（平整区 + 边坡带）
        var p = new Vector2(c.x, c.z);

        // 离地图边界留出整个占地：村落占地半径 30，贴边会直接把篱笆铺到地图外面去。
        // 这里只看占地半径 —— 地台把地图外的地形削了也无所谓，那边根本没有网格
        if (c.x - fp < 6f || c.z - fp < 6f) return false;
        if (c.x + fp > OpenWorld.Size - 6f || c.z + fp > OpenWorld.Size - 6f) return false;

        // 河 / 湖 / 护城河 / 大教堂高台：按"地台影响范围"避让，不然地台会把水面削掉一块
        if (Vector2.Distance(p, new Vector2(MondstadtTown.CenterX, MondstadtTown.CenterZ))
            < MondstadtTown.MoatOuter + reach + 2f) return false;
        if (Vector2.Distance(p, new Vector2(MondstadtTown.CathedralX, MondstadtTown.CathedralZ))
            < MondstadtTown.CathedralR + reach) return false;
        if (Vector2.Distance(p, new Vector2(LakeX, LakeZ)) < LakeR + reach) return false;
        if (Mathf.Abs(c.z - OpenWorld.RiverZ(c.x)) < OpenWorld.RiverHalfWidth + reach) return false;
        if (Vector2.Distance(p, new Vector2(SpawnX, SpawnZ)) < fp + 26f) return false;

        if (!RegionOk(kind, c)) return false;
        return FlatEnough(kind, c, reach);
    }

    /// <summary>这一点离已有的地台太近了吗 —— 近到对方会把这块地的平整区拉歪。
    /// 这是"建筑浮空"的根因防线：两块地台的平整区重叠时，后登记的会把前一块往自己高度上拉，
    /// 跨在两块地台上的建筑就会一半高一半低。fp = 想放的东西自己的占地半径</summary>
    public static bool NearPad(float x, float z, float fp, float margin)
    {
        for (int i = 0; i < Sites.Count; i++)
        {
            var s = Sites[i];
            float need = PadGap(fp, s.radius) + margin;
            float dx = x - s.x, dz = z - s.z;
            if (dx * dx + dz * dz < need * need) return true;
        }
        return false;
    }

    /// <summary>这块地本身够不够平：在地台过渡带的外沿取一圈自然地形，和地台高度最多差多少。
    /// 差得多 = 地台边缘那道坡会很陡（一眼看出是人工整出来的）。
    ///
    /// 阈值按地台大小缩放：小台子（篝火点）差 2.5 米就够缓了，大台子（村落，影响半径 48 米）
    /// 要求 2.5 米就基本不可能选址 —— 折算成坡是一样的陡缓，所以按半径放。</summary>
    static bool FlatEnough(WildKind kind, Cand c, float reach)
    {
        float fp = WildBuildings.Footprint(kind);
        float allow = Mathf.Clamp(2.4f + fp * 0.07f, 2.4f, 4.5f);

        // 遗迹本来就该长在荒地里：地面起伏大一点反而更像废墟，而且石阵 / 断墙本身就参差不齐，
        // 地台削得糙一点看不出来，所以给它们放宽一半
        if (kind == WildKind.RuinPillars || kind == WildKind.RuinWall) allow *= 1.5f;

        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            float h = OpenWorld.Height(c.x + Mathf.Cos(a) * reach, c.z + Mathf.Sin(a) * reach);
            if (Mathf.Abs(h - c.y) > allow) return false;
        }
        return true;
    }

    /// <summary>每种建筑的"脾气"：风车要在空旷地、猎人小屋要在林子里、水车要在水边……</summary>
    static bool RegionOk(WildKind kind, Cand c)
    {
        switch (kind)
        {
            case WildKind.Village:     return c.forest < 0.78f && c.y < 34f;
            case WildKind.Windmill:    return c.forest < 0.45f;
            case WildKind.Watchtower:  return c.slope < 0.14f;
            case WildKind.HuntersHut:  return c.forest > 0.40f;
            case WildKind.LumberCamp:  return c.forest > 0.34f;
            case WildKind.WaterMill:   return c.water;
            case WildKind.RestCamp:    return c.slope < 0.12f;
            case WildKind.WindShrine:  return c.y > OpenWorld.WaterLevel + 3.5f;
            case WildKind.RuinPillars: return c.y < 30f;
            default:                   return true;
        }
    }

    // ================= 木桥（特例：必须压在河上，而且没有地台） =================

    /// <summary>要架几座木桥。桥位不写死，由 PlaceBridges 沿河自己找</summary>
    const int BridgeCount = 2;

    /// <summary>一处候选过河点</summary>
    struct Crossing
    {
        public float x, zc;      // 河心
        public float half;       // 半跨
        public float deck;       // 桥面高度
        public float score;      // 越小越好（两岸高差 + 离出生点的距离）
    }

    /// <summary>
    /// 木桥：**沿河扫一遍自己找过河点**，而不是写死坐标。
    /// 条件是"两岸高差小 + 跨度合适"，再从中挑出互相隔得远的两处。
    ///
    /// ★ 原来桥位写死在 x = 245 / 330。地形一改（比如这次把雪山收小），
    ///   其中一个位置的两岸高差就超标、桥被筛掉，只剩一座 —— 写死坐标经不起地形调整。
    /// </summary>
    static void PlaceBridges()
    {
        var found = new List<Crossing>();

        for (float x = 170f; x < OpenWorld.Size - 70f; x += 8f)
        {
            float zc = OpenWorld.RiverZ(x);
            var here = new Vector2(x, zc);

            // 河在湖盆里 / 太贴着城的那几段不架桥（城里那座桥已经管这一段了）
            if (Vector2.Distance(here, new Vector2(LakeX, LakeZ)) < LakeR + 30f) continue;
            if (Vector2.Distance(here, new Vector2(MondstadtTown.CenterX, MondstadtTown.CenterZ))
                < MondstadtTown.MoatOuter + 40f) continue;

            // 往两侧找上岸点：地面高出水面 1.6 米就算岸
            float zP = 0f, zN = 0f;
            bool okP = false, okN = false;
            for (int i = 1; i <= 60 && !okP; i++)
                if (OpenWorld.Height(x, zc + i) > OpenWorld.WaterLevel + 1.6f) { zP = zc + i; okP = true; }
            for (int i = 1; i <= 60 && !okN; i++)
                if (OpenWorld.Height(x, zc - i) > OpenWorld.WaterLevel + 1.6f) { zN = zc - i; okN = true; }
            if (!okP || !okN) continue;

            float gP = OpenWorld.Height(x, zP), gN = OpenWorld.Height(x, zN);
            float diff = Mathf.Abs(gP - gN);
            float half = Mathf.Max(zP - zc, zc - zN) + 4f;   // 再往岸里多铺 4 米

            if (half < 8f || half > 30f) continue;           // 太窄不像河、太宽就不是桥了
            if (diff > 7f) continue;                         // 两岸高差太大 → 桥面是平的，一头会显得很突兀

            // 打分：两岸高差为主，再按"离出生点多远"加一点权重 ——
            // 条件一样好时优先挑近的那座，免得第二座桥被甩到地图另一头（玩家要跑六百米才遇到）
            float away = Vector2.Distance(here, new Vector2(SpawnX, SpawnZ));
            found.Add(new Crossing
            {
                x = x, zc = zc, half = half,
                deck = Mathf.Max(gP, gN) + 0.35f,
                score = diff * 4f + away * 0.02f,
            });
        }

        // 分低的优先；挑的时候还要求两座桥互相隔开 200 米，别挤在一起
        found.Sort((a, b) => a.score.CompareTo(b.score));

        int placed = 0;
        foreach (var c in found)
        {
            if (placed >= BridgeCount) break;

            bool tooClose = false;
            foreach (var s in Sites)
                if (s.kind == WildKind.Bridge && Mathf.Abs(s.x - c.x) < 200f) { tooClose = true; break; }
            if (tooClose) continue;

            Sites.Add(new WildSite
            {
                kind = WildKind.Bridge,
                x = c.x, z = c.zc,
                y = c.deck - 2.95f,          // BuildBridge 里桥面在 c.y + 2.95
                yaw = 0f,                    // 桥沿 Z 铺开，正好横跨东西走向的河
                variant = H((int)c.x, (int)c.zc, 601),
                radius = WildBuildings.Footprint(WildKind.Bridge),
                param = c.half,              // 半跨长
            });

            placed++;
            Debug.Log($"[WildSiteSpawner] 木桥：选在 x={c.x:0}（半跨 {c.half:0.0} 米，离出生点 {Vector2.Distance(new Vector2(c.x, c.zc), new Vector2(SpawnX, SpawnZ)):0} 米）");
        }

        if (placed < BridgeCount)
            Debug.LogWarning($"[WildSiteSpawner] 木桥只找到 {placed}/{BridgeCount} 处合适的过河点" +
                             $"（候选 {found.Count} 处）—— 两岸高差太大的地方不适合架平桥。");
    }

    /// <summary>这一点是不是落在某处野外建筑的"地台影响范围"里 —— 平整半径 + 边坡过渡带，再加 extra。
    ///
    /// ★ 为什么不能只用 NearSite：地台的平整区外面还有一圈 8~13 米的边坡过渡带。
    ///   东西摆在过渡带上会一半埋进土里、一半悬空（宝箱"位置不对"就是这么来的）。
    ///   宝箱 / 怪物用这个避让；解密场地要用 NearPad（要求更严，连平整区都不许挨着）。
    ///   木桥没有地台，按占地半径处理就够了。</summary>
    public static bool InsideSiteArea(float x, float z, float extra)
    {
        for (int i = 0; i < Sites.Count; i++)
        {
            var s = Sites[i];
            if (s.kind == WildKind.Bridge) continue;        // 桥没有地台
            float dx = x - s.x, dz = z - s.z;
            float need = PadRadius(s.radius) + Falloff(s.radius) + extra;
            if (dx * dx + dz * dz < need * need) return true;
        }
        return false;
    }

    /// <summary>所有野外建筑的地台都登记完之后，给别人用的一致性检查（自检报告用）</summary>
    public static int CountPadOverlaps()
    {
        int bad = 0;
        for (int i = 0; i < Sites.Count; i++)
        {
            if (Sites[i].kind == WildKind.Bridge) continue;
            for (int j = i + 1; j < Sites.Count; j++)
            {
                if (Sites[j].kind == WildKind.Bridge) continue;
                float need = PadGap(Sites[i].radius, Sites[j].radius);
                float dx = Sites[i].x - Sites[j].x, dz = Sites[i].z - Sites[j].z;
                if (dx * dx + dz * dz < need * need) bad++;
            }
        }
        return bad;
    }

    // ================= 自检报告 =================

    /// <summary>把规划结果整理成一段可读的报告：每处建筑的位置、地台高度、离最近邻居的余量、
    /// 以及地台边缘的"台坎"有多高（台坎太高说明那块地本来很斜，会看出一个人工平台）。
    /// 编辑器菜单 VoxelDemo → 验证野外建筑选点 会跑它，见 Assets/Editor/WildSiteCheck.cs。</summary>
    public static string PlanReport()
    {
        var sb = new System.Text.StringBuilder();

        // 每种应该铺几个 / 实际铺了几个
        sb.Append("计划：");
        foreach (WildKind kind in System.Enum.GetValues(typeof(WildKind)))
        {
            int want = 0;
            foreach (var k in Plan) if (k == kind) want++;

            int got = 0;
            foreach (var s in Sites) if (s.kind == kind) got++;

            if (want == 0 && got == 0 && kind != WildKind.Bridge) continue;
            if (kind == WildKind.Bridge) want = BridgeCount;    // 桥不在 Plan 里（沿河自己找位），数量看 BridgeCount
            sb.Append($"{WildBuildings.DisplayName(kind)} {got}/{want}；");
        }
        sb.AppendLine();

        sb.AppendLine($"候选点 {_cand.Count} 个，地台 {CountPads()} 块，共铺下 {Sites.Count} 处：");

        float worstMargin = float.MaxValue;
        float worstStep = 0f;
        int problems = 0;

        for (int i = 0; i < Sites.Count; i++)
        {
            var s = Sites[i];
            var name = WildBuildings.DisplayName(s.kind);

            // 离最近邻居的余量：按"两块地台共存的最近距离"算，负数 = 地台已经重叠（会互相拉歪）
            float margin = float.MaxValue;
            for (int k = 0; k < Sites.Count; k++)
            {
                if (k == i) continue;
                var o = Sites[k];
                bool bridged = s.kind == WildKind.Bridge || o.kind == WildKind.Bridge;
                float need = bridged
                    ? s.radius * 0.5f + o.radius * 0.5f            // 桥没有地台，按占地算
                    : PadGap(s.radius, o.radius);
                float d = Vector2.Distance(new Vector2(s.x, s.z), new Vector2(o.x, o.z));
                margin = Mathf.Min(margin, d - need);
            }
            if (Sites.Count < 2) margin = 999f;

            // 地台边缘的削填深度：在地台外沿采样"没有地台时的自然地形"，和地台高度差多少，
            // 就是这块台地要削掉 / 填起来多少土（也就是玩家会看到多高的台坎）。
            // 必须临时关掉地台才量得到自然地形，否则量到的是已经垫平的地台自己。
            float step = 0f;
            float naturalSlope = 0f;
            if (s.kind != WildKind.Bridge)
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

            sb.AppendLine($"  {name} ({s.x:0},{s.z:0})  地台高 {s.y:0.0}m  朝向 {s.yaw:0}°  " +
                          $"离最近邻居 {margin:0.0}m  台坎 {step:0.0}m  原地坡度 {naturalSlope:0.000}");
        }

        sb.AppendLine($"最小邻居余量 {worstMargin:0.0}m（负数 = 地台重叠，建筑会浮空），" +
                      $"最大台坎 {worstStep:0.0}m（> 5m 会看出明显的人工台坎），" +
                      $"地台重叠 {CountPadOverlaps()} 处，异常 {problems} 项");
        return sb.ToString();
    }

    // ================= 落地 =================

    public void Init(OpenWorld world, Transform player)
    {
        _material = world.Material;

        _root = new GameObject("WildBuildings").transform;
        _root.SetParent(transform, false);

        if (Sites.Count == 0)
        {
            Debug.LogWarning("[WildSiteSpawner] 没有已规划的建筑（PlanSites 没跑？），跳过落地。");
            return;
        }

        // ---- 几何：全部合成一个网格，三个子网格共用城里的石墙 / 瓦片 / 顶点色材质 ----
        WildBuildings.Begin();
        foreach (var s in Sites)
            WildBuildings.Append(s.kind, new Vector3(s.x, world.SurfaceY(s.x, s.z), s.z), s.yaw, s.variant, s.param);

        var mesh = WildBuildings.BuildMesh("WildBuildings");
        var go = new GameObject("WildBuildingMesh");
        go.transform.SetParent(_root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { world.StoneMaterial, world.RoofMaterial, world.Material };
        go.AddComponent<MeshCollider>().sharedMesh = mesh;

        // ---- 火苗：村落中央的篝火、休息点 / 伐木场 / 瞭望塔的火堆 ----
        foreach (var p in WildBuildings.Flames)
        {
            var flame = new GameObject("Flame").transform;
            flame.SetParent(_root, false);
            flame.position = p;
            MonsterSpawner.BuildFlame(flame, _material);
            _flames.Add(flame);
        }

        // ---- 地图标记 + 大色块（村落能看到一片红顶） ----
        int villages = 0;
        foreach (var s in Sites)
        {
            MondstadtTown.Markers.Add(new MondstadtTown.MapMarker
            {
                name = WildBuildings.DisplayName(s.kind),
                x = s.x, z = s.z,
                kind = WildBuildings.MarkerKind(s.kind),
            });

            if (s.kind == WildKind.Village) villages++;
        }

        foreach (var h in WildBuildings.Houses)
            MondstadtTown.Blobs.Add(new MondstadtTown.MapBlob { x = h.x, z = h.y, r = h.z, color = 0 });

        foreach (var s in Sites)
        {
            if (s.kind == WildKind.Village)
                MondstadtTown.Blobs.Add(new MondstadtTown.MapBlob { x = s.x, z = s.z, r = s.radius * 0.85f, color = 1 });
            else if (s.kind == WildKind.WindShrine || s.kind == WildKind.RuinPillars || s.kind == WildKind.RuinWall)
                MondstadtTown.Blobs.Add(new MondstadtTown.MapBlob { x = s.x, z = s.z, r = s.radius * 0.8f, color = 2 });
        }

        // ---- 村民：每座村子住 1~2 个 ----
        foreach (var s in Sites)
            if (s.kind == WildKind.Village) SpawnVillagers(world, player, s);

        Debug.Log($"[WildSiteSpawner] 落地完成：{Sites.Count} 处野外建筑，{_flames.Count} 处篝火，" +
                  $"村落 {villages} 座，{_villagers} 个村民，" +
                  $"顶点 {WildBuildings.SubTris[0].Count / 3 + WildBuildings.SubTris[1].Count / 3 + WildBuildings.SubTris[2].Count / 3} 个面");
    }

    // ---------- 村民 ----------

    /// <summary>村落的村民：复用 NPC 那一套（VoxelNpc + VoxelNpcData），不碰 NavMesh —— 寻路是后面的事。
    /// 只在村里走动、会看着玩家；不追不打不捡东西（村民就该过自己的日子）</summary>
    void SpawnVillagers(OpenWorld world, Transform player, WildSite s)
    {
        // 每座村子 1~2 个（哈希决定，重进游戏人数一样）
        int count = 1 + (int)(H((int)s.x, (int)s.z, 1201) * 2f);

        for (int i = 0; i < count; i++)
        {
            float a = (s.yaw + i * 141f + H(i, (int)s.x, 1202) * 50f) * Mathf.Deg2Rad;
            float r = 5.5f + H(i, (int)s.z, 1203) * 6.5f;
            float x = s.x + Mathf.Cos(a) * r;
            float z = s.z + Mathf.Sin(a) * r;

            // 落到水里 / 太陡就往村心缩，直到找到站得住的地方
            for (int k = 0; k < 8; k++)
            {
                if (OpenWorld.InBounds(x, z) &&
                    OpenWorld.Height(x, z) > OpenWorld.WaterLevel + 1f &&
                    OpenWorld.Slope(x, z) < 0.35f) break;

                x = Mathf.Lerp(x, s.x, 0.45f);
                z = Mathf.Lerp(z, s.z, 0.45f);
            }

            var go = new GameObject($"Villager{i}");
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(x, world.SurfaceY(x, z), z);

            var npc = go.AddComponent<VoxelNpc>();
            npc.data = VillagerData(i);
            npc.Init(_material, player, world);

            _villagers++;
        }
    }

    /// <summary>村民的数据：就在村里转悠、会看玩家；不追人、不躲人、不捡原石、不刷播报。
    /// 模型暂时沿用现有的角色模型（Resources 下只有两个），以后有村民专用的再换 modelResourcePath。
    /// 蒙德和璃月的村落共用这一份（璃月那边见 LiyueSiteSpawner.SpawnVillagers）</summary>
    public static VoxelNpcData VillagerData(int index)
    {
        var data = new VoxelNpcData();

        data.displayName = index == 0 ? "村民" : "村民·小孩";
        data.wanderRange = 12f;      // 村里随便逛
        data.walkSpeed   = 1.5f;     // 比玩家和孙琰都慢，一看就是在过日子
        data.noticeRange = 7f;
        data.seekGems    = false;    // 不捡原石（那是孙琰的爱好）
        data.usesHunger  = false;    // 村民不进饥饿系统，省得份量算错
        data.quiet       = true;     // 不往聊天栏刷"谁打了谁"

        return data;
    }

    void Update()
    {
        // 篝火跳动的火苗（和怪物营地那套一样）
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
