using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 传送锚点的选点与落地（锚点本体见 <see cref="TeleportAnchor"/>，大地图那头见 <see cref="WorldMap"/>）。
///
/// 【放在哪】三层：
///   ① 关键锚点（Major，大地图上带名字）：出生点 / 蒙德城（南门大道）/ 大教堂高台 / 七天神像 ×3 /
///      蒙德村落 ×2 / 「Andy的雪顶」前哨营地 / 雪顶观景台 / 璃月港 · 山门 / 璃月港 · 主码头 /
///      璃月村落 ×2 / 稻妻的将军府 · 港口 · 神社离岛 · 五重塔 · 山脚村子
///      —— 城镇、地标、村口那一类；
///   ② 普通锚点（野外锚点 NN）：按网格铺满全图，**任意陆地位置离最近的锚点不超过 CoverTarget 米**
///      —— 这是"不会长时间跑腿"的硬指标，自检里会打印实测的最远距离（见 <see cref="CoverageReport"/>）；
///   ③ 默认全开：所有锚点一进游戏就都是点亮状态（要改成"走近点亮"，见 PlanAnchors 末尾）。
///
/// 【为什么村口锚点落在村外 33 米处】
///   村落的布局是分层的（村心 r&lt;7 / 内圈 10.5 / 外圈 19.5 / 篱笆 25.5），房子之间只剩一米宽的缝 ——
///   锚点杵进村里就会卡在房子或篱笆上。所以放在"篱笆外、地台平整区以内"那一圈（r = 半径 + 3），
///   既在村子边上、又是干净的平地，和原神里村口的传送点一个位置关系。
///
/// 【和地台的关系】
///   锚点是块小石台，直接坐在现成的地面上，**不登记地台**（不占用地台预算、也不会把地形拉歪）。
///   但反过来要避让别人的地台：不能杵进房子 / 石台里（禁植区），也别贴着地台边坡（会一半埋进土里）。
///
/// 【时机】
///   规划位置（PlanAnchors，纯地形计算）可以在烘高度场前后随便跑，自检也用它；
///   真正落地（Init）放在所有建筑生成器之后 —— 那时候才能用 Physics 探一下"这儿有没有挡住"，
///   而且必须在 WorldMap.Init（烘底图）之前，大地图才会把锚点图钉画上去。
/// </summary>
public class TeleportSpawner : MonoBehaviour
{
    /// <summary>一处锚点的理想位置（还没做贴墙检查）</summary>
    public struct AnchorSpot
    {
        public string name;
        public float  x, z;
        public float  yaw;        // 村子这类"绕着中心找"的，用它的正面方向当第一个候选方向
        public float  ring;       // 候选圈半径（0 = 就用这个点，不再绕圈找）
        public float  y;          // 固定的站立高度（0 = 用地形高度）；码头那种"砌在人造台面上"的要用它
        public bool   unlockedAtStart;
        public bool   major;      // 关键锚点（城镇 / 地标 / 村口）：大地图上的图钉更大、名字一定会写出来
        public Vector2 lookAt;    // 传送落地后面朝哪（XZ）。(0,0) = 没指定 → 有候选圈的朝锚点自己（村口朝村里）
        public Vector3 resolved;  // 落地时定下来的真实位置
        public string how;        // 最终落在哪个候选上（自检报告里看得到）
        public TeleportAnchor.AnchorKind kind;   // 石柱锚点 / 七天神像（神像落地后走神像那一套：外形 + 回满生命体力）
    }

    public static readonly List<AnchorSpot> Spots = new List<AnchorSpot>();

    // ---------- 村口那一圈：篱笆 25.5，地台平整区到 36 ----------
    const float VillageRing = 33f;

    // ---------- 覆盖（普通锚点的网格） ----------
    /// <summary>目标覆盖半径（米）：任意陆地位置离最近的锚点不超过它。
    /// 计划要求"普通区域也要有锚点、不会长时间跑腿"就是这个数</summary>
    public const float CoverTarget = 220f;

    /// <summary>网格的名义间距。每次规划按当前 Size 均分（1800 / 7 ≈ 257 米），
    /// 方格最坏点（格心）离最近格点 = 间距 / √2 ≈ 182 米 —— 给"绕开水面 / 建筑"的挪位留了余量</summary>
    const float CoverStep = 260f;

    /// <summary>一个格点最多挪多远去找能站的地方（越远覆盖指标越差，所以卡住不放）</summary>
    const float CoverSearch = 25f;

    /// <summary>离已有锚点比这个还近的格点就不加了（省得锚点扎堆；必须小于"间距 - 挪位上限"）</summary>
    const float CoverSkip = 195f;

    /// <summary>锚点自己的占地半径 + 边坡余量：用来判"会不会撞进别人的地台"</summary>
    const float AnchorPadR = 2f, AnchorPadF = 4f, AnchorPadMargin = 3f;

    // ---------- 「Andy的雪顶」观景台 ----------
    // 雪顶那一片的天然地面没一块能站：山脊上叠的是 ±16 米的山脊噪声，±2.5 米内高低差远超 0.7 米，
    // 石台放上去会一半埋进土里一半悬空（冰湖那处就是这么挂掉的）。
    // 所以照野外建筑那套办法，先在脊线**东北端外沿**压一小块人工平台，再往平台上放锚点 ——
    // 台面高度取原地地形高度、四周留 16 米过渡带，压出来就是山肩上的一块小平地。
    const float LookoutX = 851f, LookoutZ = 751f;      // 脊线 (722,622)-(812,712) 的末端再往东北走约 55 米
    const float LookoutPadR = 10f, LookoutPadF = 16f;

    // ================= 规划（纯地形，编辑器自检也跑得到） =================

    /// <summary>规划全部锚点：先关键点，再按网格铺满全图。位置用到三边的野外建筑列表，
    /// 所以必须排在它们之后</summary>
    public static void PlanAnchors()
    {
        Spots.Clear();

        // ① 出生点：桥南的路边（OpenWorld.SpawnPoint 就是这里），往旁边挪 3 米，别正好压在出生点上。
        //    面朝城门 —— 一睁眼就知道该往哪走
        Add("出生点", MondstadtTown.CenterX - 3f, 100f, 0f, 0f, unlockedAtStart: true, major: true,
            lookAt: new Vector2(MondstadtTown.CenterX, MondstadtTown.CenterZ));

        // ② 蒙德城：南城门大道上（广场半径 17 之外、环路 32 之内），广场上的雕像和水池都躲开了
        Add("蒙德城", MondstadtTown.CenterX, MondstadtTown.CenterZ - (MondstadtTown.PlazaR + 8f),
            MondstadtTown.SouthGateDeg, 0f, false, major: true,
            lookAt: new Vector2(MondstadtTown.CenterX, MondstadtTown.CenterZ));

        // ③ 大教堂高台：绕台地中心 26 米找（台地平整半径 50，26 米还在平顶上，
        //    又躲开了教堂本身的体块和东桥），面朝教堂
        Add("大教堂高台", MondstadtTown.CathedralX, MondstadtTown.CathedralZ, 0f, 26f, false, major: true,
            lookAt: new Vector2(MondstadtTown.CathedralX, MondstadtTown.CathedralZ));

        // ③.5 七天神像：三个国家各一尊，位置由各国自己出（蒙德在中央广场正中，璃月在港城的岩神像广场，
        //      稻妻在将军府高台前）。神像既是传送点，走近还会回满生命与体力 —— 就是原神那套
        Add("七天神像 · 风", MondstadtTown.CenterX, MondstadtTown.CenterZ, 0f, 0f, unlockedAtStart: true,
            y: MondstadtTown.GroundY, major: true, kind: TeleportAnchor.AnchorKind.StatueWind);

        {
            Vector3 statue = LiyueBuildings.StatuePoint();
            Add("七天神像 · 岩", statue.x, statue.z, 0f, 0f, unlockedAtStart: true, y: statue.y, major: true,
                kind: TeleportAnchor.AnchorKind.StatueGeo);
        }

        // ③.6 稻妻：将军府高台前 / 港口 / 两座离岛 / 山脚村子。
        //      ★ 位置全部落在 InazumaSiteSpawner 登记的地台**中心**，高度让它自己按地形算（y 不填）——
        //        稻妻的地台是"就地压平"的地形，写死高度反而会在两块地台叠在一起的地方出错
        //        （上町 / 下町两块地台是搭着的，写死 26.5 的地方实际被下町那块压成了 20 米）。
        {
            Vector3 statue = InazumaSiteSpawner.StatuePoint;
            Add("七天神像 · 雷", statue.x, statue.z, 0f, 0f, unlockedAtStart: true, y: statue.y, major: true,
                kind: TeleportAnchor.AnchorKind.StatueThunder);

            Add("稻妻 · 将军府", InazumaIsles.CastleX, InazumaIsles.CastleZ - 92f, 0f, 0f, true, major: true,
                lookAt: new Vector2(InazumaIsles.CastleX, InazumaIsles.CastleZ));
            Add("稻妻 · 港口", InazumaIsles.PortX, InazumaIsles.PortZ + 50f, 0f, 0f, true, major: true,
                lookAt: new Vector2(InazumaIsles.PortX, InazumaIsles.PortZ));
            Add("稻妻 · 神社离岛", InazumaIsles.ShrineX, InazumaIsles.ShrineZ + 6f, 0f, 0f, true, major: true,
                lookAt: new Vector2(InazumaIsles.ShrineX, InazumaIsles.ShrineZ));
            Add("稻妻 · 五重塔", InazumaIsles.SpireX, InazumaIsles.SpireZ + 4f, 0f, 0f, true, major: true,
                lookAt: new Vector2(InazumaIsles.SpireX, InazumaIsles.SpireZ));
            Add("稻妻 · 山脚村子", 1548f, 1662f, 0f, 0f, true, major: true, lookAt: new Vector2(1548f, 1640f));
        }

        // ④⑤ 蒙德村落：取前两座（顺序和规划顺序一致，重进游戏一样）
        int n = 0;
        foreach (var s in WildSiteSpawner.Sites)
        {
            if (s.kind != WildKind.Village) continue;
            if (n >= 2) break;
            Add($"蒙德村落 ·{(n == 0 ? "①" : "②")}", s.x, s.z, s.yaw, VillageRing, false, major: true);
            n++;
        }

        // ⑥ 「Andy的雪顶」前哨营地：绕营地 22 米找（营地里有帐篷和旗杆，别杵进去），面朝主峰
        {
            Vector2 camp = OpenWorld.SnowCampPos;
            Add("雪顶前哨营地", camp.x, camp.y, 0f, 22f, false, major: true,
                lookAt: OpenWorld.MassifPeakPos);
        }

        // ⑦ 「Andy的雪顶」观景台：先压平台再放锚点（原因见 Lookout* 的注释）。
        //    ★ 地台 / 禁植区必须在烘高度场之前登记 —— PlanAnchors 正好排在那一步
        //      （WildSiteSpawner.PlanEverything 里最后一句，OpenWorld.Awake 之前）
        {
            float py = OpenWorld.HeightRaw(LookoutX, LookoutZ);      // 取原地地形高度：平台是"就地压平"，不是堆一座塔
            OpenWorld.AddPad(LookoutX, LookoutZ, LookoutPadR, py, LookoutPadF);
            OpenWorld.AddKeepOut(LookoutX, LookoutZ, LookoutPadR + 4f);   // 平台上不长树石

            Add("雪顶观景台", LookoutX, LookoutZ, 0f, 0f, false, y: py, major: true,
                lookAt: OpenWorld.MassifPeakPos);
        }

        // ⑧ 冰湖：**不放锚点**（鞍部那一片和山脊一样是碎岩噪声，石台站不住）。
        //    雪顶这一带的覆盖由「观景台」和「前哨营地」两处保证（自检里有实测的最远距离）。

        // ⑨ 璃月港 · 山门：上城背后那条宽台阶的上端（内陆侧 120 米，还在台地平整半径 150 以内），
        //    面朝港城 —— 从内陆进城就是从这里进去
        {
            Vector3 p = LiyueBuildings.HarborPoint(0f, -120f);
            Add("Andy的港门 · 山门", p.x, p.z, 0f, 0f, false, major: true,
                lookAt: new Vector2(LiyueHarbor.HarborX, LiyueHarbor.HarborZ));
        }

        // ⑩ 璃月港 · 主码头：站在码头石台上（给固定高度，不按地形算），面朝内湾
        {
            var p = LiyueBuildings.HarborPoint(30f, LiyueBuildings.HarborWharfSea(30f));
            Add("Andy的港门 · 主码头", p.x, p.z, 0f, 0f, unlockedAtStart: true, y: LiyueBuildings.HarborWharfTopY(),
                major: true, lookAt: new Vector2(LiyueHarbor.InnerBayX, LiyueHarbor.InnerBayZ));
        }

        // ⑪ 璃月村落：同样取前两座
        int m = 0;
        foreach (var s in LiyueSiteSpawner.Sites)
        {
            if (s.kind != LiyueKind.Village) continue;
            if (m >= 2) break;
            Add($"璃月村落 ·{(m == 0 ? "①" : "②")}", s.x, s.z, s.yaw, VillageRing, false, major: true);
            m++;
        }

        AddCoverageAnchors();

        // 默认全开：一进游戏每个锚点都能传（想改回"走近点亮"，把这一段删掉即可）
        for (int i = 0; i < Spots.Count; i++)
        {
            var spot = Spots[i];
            spot.unlockedAtStart = true;
            Spots[i] = spot;
        }
    }

    /// <summary>按网格铺普通锚点：先把全图切成 CoverStep 的方格，格心附近找一块能站的地，
    /// 再补一轮"死角"（覆盖指标超标的地方）</summary>
    static void AddCoverageAnchors()
    {
        int side = Mathf.Max(2, Mathf.CeilToInt(OpenWorld.Size / CoverStep));
        float step = OpenWorld.Size / side;
        int made = 0;

        for (int ix = 0; ix < side; ix++)
        {
            for (int iz = 0; iz < side; iz++)
            {
                float gx = (ix + 0.5f) * step;
                float gz = (iz + 0.5f) * step;

                // 离已有锚点太近的格点不加（省得扎堆），挪得到的才加
                if (NearestSpotDistance(gx, gz) < CoverSkip) continue;
                if (!FindGridSpot(gx, gz, CoverSearch, out Vector3 pos)) continue;

                made++;
                Add($"野外锚点 {made:00}", pos.x, pos.z, 0f, 0f, unlockedAtStart: false, major: false,
                    lookAt: NearestMajor(pos.x, pos.z));
            }
        }

        FillCoverageHoles(ref made);
    }

    /// <summary>补死角：隔一段扫一遍全图，挑出"离最近锚点还很远"的点，再往那一带找落脚点。
    /// 最多补四轮、每轮最多八处 —— 覆盖指标够了就自己停</summary>
    static void FillCoverageHoles(ref int made)
    {
        for (int round = 0; round < 4; round++)
        {
            var holes = new List<Vector2>();

            for (float z = 20f; z < OpenWorld.Size; z += 40f)
            {
                for (float x = 20f; x < OpenWorld.Size; x += 40f)
                {
                    if (OpenWorld.Height(x, z) < OpenWorld.WaterLevel + 0.5f) continue;   // 水面不管
                    if (NearestSpotDistance(x, z) <= CoverSkip) continue;

                    // 和已挑出的死角离得够远才算新死角
                    bool merged = false;
                    for (int i = 0; i < holes.Count; i++)
                    {
                        if (Vector2.Distance(holes[i], new Vector2(x, z)) < 150f) { merged = true; break; }
                    }

                    if (!merged && holes.Count < 8) holes.Add(new Vector2(x, z));
                }
            }

            if (holes.Count == 0) break;

            int added = 0;
            for (int i = 0; i < holes.Count; i++)
            {
                if (!FindSpotNear(holes[i], 130f, 90f, out Vector3 pos)) continue;

                made++;
                added++;
                Add($"野外锚点 {made:00}", pos.x, pos.z, 0f, 0f, unlockedAtStart: false, major: false,
                    lookAt: NearestMajor(pos.x, pos.z));
            }

            if (added == 0) break;      // 这一轮一个都没塞进去，再扫也没用
        }
    }

    /// <summary>在格心附近找落脚点：先试格心本身，再绕三圈八个方向（越近越好）</summary>
    static bool FindGridSpot(float gx, float gz, float range, out Vector3 pos)
    {
        if (TryGround(gx, gz, out pos)) return true;

        for (int r = 1; r <= 3; r++)
        {
            float radius = range * r / 3f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                if (TryGround(gx + Mathf.Cos(a) * radius, gz + Mathf.Sin(a) * radius, out pos)) return true;
            }
        }

        pos = Vector3.zero;
        return false;
    }

    /// <summary>在某个死角附近找落脚点，并且要和已有锚点拉开 gap 米以上</summary>
    static bool FindSpotNear(Vector2 hole, float range, float gap, out Vector3 pos)
    {
        if (TryGround(hole.x, hole.y, out pos) && NearestSpotDistance(pos.x, pos.z) >= gap) return true;

        for (int r = 1; r <= 4; r++)
        {
            float radius = range * r / 4f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                float x = hole.x + Mathf.Cos(a) * radius;
                float z = hole.y + Mathf.Sin(a) * radius;

                if (!TryGround(x, z, out pos)) continue;
                if (NearestSpotDistance(pos.x, pos.z) < gap) continue;
                return true;
            }
        }

        pos = Vector3.zero;
        return false;
    }

    /// <summary>这一点能不能站：不在别人地台里、不贴边坡、地面合格</summary>
    static bool TryGround(float x, float z, out Vector3 pos)
    {
        pos = Vector3.zero;

        if (OpenWorld.InSiteKeepOut(x, z)) return false;                       // 别杵进野外建筑的占地里
        if (OpenWorld.PadConflict(x, z, AnchorPadR, AnchorPadF, AnchorPadMargin)) return false;   // 别贴着别人的地台

        float h = OpenWorld.Height(x, z);
        if (!GroundOk(x, z, h, built: false, runtime: false, out _)) return false;

        pos = new Vector3(x, h, z);
        return true;
    }

    /// <summary>离最近的锚点有多少米（还没定下真实位置的用规划位置）</summary>
    static float NearestSpotDistance(float x, float z)
    {
        float best = float.MaxValue;

        for (int i = 0; i < Spots.Count; i++)
        {
            Vector2 p = SpotPos(Spots[i]);
            float d = Mathf.Sqrt((x - p.x) * (x - p.x) + (z - p.y) * (z - p.y));
            if (d < best) best = d;
        }

        return Spots.Count > 0 ? best : 99999f;
    }

    /// <summary>锚点当前的参考位置：落地时定下来的优先，否则用规划位置</summary>
    static Vector2 SpotPos(AnchorSpot s) =>
        s.resolved != Vector3.zero ? new Vector2(s.resolved.x, s.resolved.z) : new Vector2(s.x, s.z);

    /// <summary>离得最近的关键锚点（落地后面朝它 —— 野外锚点不知道该朝哪，就朝着最近的地标）</summary>
    static Vector2 NearestMajor(float x, float z)
    {
        float best = float.MaxValue;
        Vector2 at = Vector2.zero;

        for (int i = 0; i < Spots.Count; i++)
        {
            if (!Spots[i].major) continue;

            float dx = x - Spots[i].x, dz = z - Spots[i].z;
            float d2 = dx * dx + dz * dz;
            if (d2 >= best) continue;

            best = d2;
            at = new Vector2(Spots[i].x, Spots[i].z);
        }

        return at;
    }

    static void Add(string name, float x, float z, float yaw, float ring, bool unlockedAtStart,
                    float y = 0f, bool major = false, Vector2 lookAt = default(Vector2),
                    TeleportAnchor.AnchorKind kind = TeleportAnchor.AnchorKind.Pillar)
    {
        Spots.Add(new AnchorSpot
        {
            name = name,
            x = x, z = z,
            yaw = yaw,
            ring = ring,
            y = y,
            unlockedAtStart = unlockedAtStart,
            major = major,
            lookAt = lookAt,
            kind = kind,
        });
    }

    /// <summary>这一点的站立高度：给了固定高度就用它（人造台面），否则取地形高度</summary>
    static float StandY(AnchorSpot spot, float x, float z) =>
        spot.y > 0.01f ? spot.y : OpenWorld.Height(x, z);

    /// <summary>
    /// 给一处锚点定落脚点：绕中心按固定顺序试几个候选方向，取第一个"地面平、不在水里、不贴墙"的。
    /// ★ "贴墙"那一关要用 Physics 射线，只有运行时才做得到；编辑器自检里只过前两关
    ///   （所以自检报告里写的是"地形可行"，最终位置以运行时为准，两者会一致 —— 固定点和村口那一圈都是干净的地）。
    /// </summary>
    public static bool ResolveSpot(AnchorSpot spot, bool runtime, out Vector3 pos, out string how)
    {
        pos = Vector3.zero;
        how = "";

        bool built = spot.y > 0.01f;      // 给了固定高度的 = 站在人造台面上（码头石台），不按地形判

        // 不需要绕圈找的（出生点 / 蒙德城 / 码头）：就那一个点
        if (spot.ring <= 0.01f)
        {
            float sy = StandY(spot, spot.x, spot.z);
            if (!GroundOk(spot.x, spot.z, sy, built, runtime, out string why))
            { how = $"固定点不可用（{why}）"; return false; }

            pos = new Vector3(spot.x, sy, spot.z);
            how = "固定点";
            return true;
        }

        // 带候选圈的（村口 / 大教堂高台 / 前哨营地）：绕着中心试八个方向，
        // 候选圈半径先 ring 再 ring+3（村口在篱笆外那一圈，前哨营地在帐篷外面）
        for (int r = 0; r < 2; r++)
        {
            float radius = spot.ring + r * 3f;
            for (int i = 0; i < 8; i++)
            {
                float a = (spot.yaw + i * 45f) * Mathf.Deg2Rad;
                float x = spot.x + Mathf.Cos(a) * radius;
                float z = spot.z + Mathf.Sin(a) * radius;

                float sy = StandY(spot, x, z);
                if (!GroundOk(x, z, sy, built, runtime, out _)) continue;

                pos = new Vector3(x, sy, z);
                how = $"候选圈 r={radius:0} 方向 {i}";
                return true;
            }
        }

        how = "八个方向都不行";
        return false;
    }

    /// <summary>
    /// 落脚点合格不合格：出界 / 在水里 / 太陡 / 脚下不平 / 贴着墙都算不合格。
    /// ★ built（给了固定高度的人造台面）也要跟脚下地形对一遍账 —— 见下面那段注释
    /// </summary>
    static bool GroundOk(float x, float z, float standY, bool built, bool runtime, out string why)
    {
        why = "";

        if (!OpenWorld.InBounds(x, z)) { why = "出界"; return false; }

        float h = OpenWorld.Height(x, z);

        if (!built)
        {
            if (h < OpenWorld.WaterLevel + 1.2f) { why = "在水里 / 水边"; return false; }
            if (OpenWorld.Slope(x, z) > 0.12f) { why = "太陡"; return false; }

            // 四向取一圈，看脚底下平不平（锚点是刚体，站在斜坡上会一半埋进去）
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                if (Mathf.Abs(OpenWorld.Height(x + Mathf.Cos(a) * 2.5f, z + Mathf.Sin(a) * 2.5f) - h) > 0.7f)
                {
                    why = "脚下不平";
                    return false;
                }
            }
        }
        else
        {
            // ★ 给了固定高度的（码头石台 / 岛上的人造台地）也要对一遍脚下：
            //   ① 埋进地形里 —— 传送过去就是掉进地形、从 MeshCollider 里穿下去掉出地图；
            //   ② 悬在地面上方 —— 台地没垫到它说的位置，锚点飘在半空（就是"锚点不在地上"）。
            //   站在**港城那种人工石台**上不算悬空（台面本来就比地形高），所以那种情况放行。
            float d = standY - h;
            bool onDeck = d > 0.9f && LiyueBuildings.OnHarborDeck(x, z, out _);

            if (d < -0.9f)
            {
                why = $"埋进地形里（锚点 {standY:0.0} 米 / 脚下地面 {h:0.0} 米）";
                return false;
            }
            if (d > 0.9f && !onDeck)
            {
                why = $"悬在地面上方（锚点 {standY:0.0} 米 / 脚下地面 {h:0.0} 米）";
                return false;
            }
        }

        // 运行时再过一关：身边两米半内有没有墙 / 房子 / 篱笆 / 货箱（水平四条射线，抬高 1.2 米避开地面）
        if (runtime)
        {
            Vector3 from = new Vector3(x, standY + 1.2f, z);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (Physics.Raycast(from, dir, 2.6f)) { why = "贴着墙"; return false; }
            }
        }

        return true;
    }

    // ================= 落地（运行时） =================

    Material _material;
    Transform _root;

    public void Init(OpenWorld world, Transform player)
    {
        _material = world.Material;

        _root = new GameObject("TeleportAnchors").transform;
        _root.SetParent(transform, false);

        if (Spots.Count == 0)
        {
            Debug.LogWarning("[TeleportSpawner] 没有已规划的锚点（PlanAnchors 没跑？），跳过落地。");
            return;
        }

        int built = 0, failed = 0;
        for (int i = 0; i < Spots.Count; i++)
        {
            var spot = Spots[i];

            if (!ResolveSpot(spot, runtime: true, out Vector3 pos, out string how))
            {
                Debug.LogWarning($"[TeleportSpawner] 锚点「{spot.name}」没找到落脚点：{how}");
                failed++;
                continue;
            }

            spot.resolved = pos;
            spot.how = how;
            Spots[i] = spot;

            // 落点：从锚点往外让一段（锚点自己那根柱子是有碰撞的），并且**按落点脚下真实的地面**定高度
            Vector3 landPos = ResolveLandSpot(spot, pos);

            var go = new GameObject("Anchor_" + spot.name);
            go.transform.SetParent(_root, false);

            var anchor = go.AddComponent<TeleportAnchor>();
            anchor.Init(spot.name, pos, landPos, FaceYaw(spot, landPos), spot.major, world, player,
                        _material, spot.unlockedAtStart, spot.kind);
            built++;
        }

        Debug.Log($"[TeleportSpawner] 传送锚点落地完成：{built}/{Spots.Count} 处" +
                  $"（关键 {CountMajor()} 处），默认点亮 {UnlockedAtStart()} 处，其余由玩家走近点亮。" +
                  CoverageReport());
    }

    /// <summary>
    /// 定传送落点：绕锚点由近到远试八个方向，取"脚下高度和锚点差不多、不在水里、不太陡"的那一处。
    ///
    /// ★ 这里踩过大坑：原来是把**锚点的高度**直接搬到落点上（落点往外挪 2.6 米，神像 7 米）。
    ///   落点那一格如果在坡上，地面比锚点高 —— 玩家一落地就埋进地形里，
    ///   接着从 MeshCollider 里穿下去，直接掉出地图；落在水里则是沉底。
    ///   现在按落点自己的地面高度定高度，并对高度差设卡（对不上就换个方向再试）。
    /// 找不到合适的就退回锚点脚下（宁可站在柱子边上，也别把人丢进地形里）。
    /// </summary>
    public static Vector3 ResolveLandSpot(AnchorSpot spot, Vector3 pos)
    {
        float near = spot.kind == TeleportAnchor.AnchorKind.Pillar ? 2.6f : 7f;   // 神像体量大，让开得多一点
        float[] radi = { near, near + 2.5f, near + 6f, near + 11f };

        foreach (float r in radi)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                float x = pos.x + Mathf.Cos(a) * r, z = pos.z + Mathf.Sin(a) * r;

                if (!OpenWorld.InBounds(x, z)) continue;

                float h = OpenWorld.Height(x, z);
                if (h < OpenWorld.WaterLevel + 0.6f) continue;                  // 水里 / 水边不要
                if (OpenWorld.Slope(x, z) > 0.24f) continue;                    // 陡坡不要
                if (Mathf.Abs(h - pos.y) > 1.2f) continue;                      // 高度对不上（坡下 / 台子外）不要

                return new Vector3(x, Mathf.Max(h, pos.y) + 0.05f, z);
            }
        }

        return pos + Vector3.up * 0.05f;
    }

    /// <summary>落地后玩家面朝哪（度）：给了 lookAt 就朝它，否则有候选圈的朝锚点自己（村口 → 朝村里）。
    /// 两者都没有（固定点又没指定）就给 NaN —— 表示别动玩家当前的朝向</summary>
    static float FaceYaw(AnchorSpot spot, Vector3 landPos)
    {
        Vector2 target = spot.lookAt != Vector2.zero ? spot.lookAt
                       : spot.ring > 0.01f ? new Vector2(spot.x, spot.z)
                       : Vector2.zero;

        if (target == Vector2.zero) return float.NaN;
        return Mathf.Atan2(target.x - landPos.x, target.y - landPos.z) * Mathf.Rad2Deg;   // Vector2.y 就是世界 Z
    }

    static int CountMajor()
    {
        int n = 0;
        foreach (var s in Spots) if (s.major) n++;
        return n;
    }

    static int UnlockedAtStart()
    {
        int n = 0;
        foreach (var s in Spots) if (s.unlockedAtStart) n++;
        return n;
    }

    /// <summary>覆盖指标：全图按 25 米撒点，取"离最近锚点最远"的那个距离（自检 / 报告用）。
    /// ★ 只算陆地（水面不算 —— 没人会站在海里）</summary>
    public static float WorstCoverDistance(out Vector2 at)
    {
        float worst = 0f;
        at = Vector2.zero;

        for (float z = 12.5f; z < OpenWorld.Size; z += 25f)
        {
            for (float x = 12.5f; x < OpenWorld.Size; x += 25f)
            {
                if (OpenWorld.Height(x, z) < OpenWorld.WaterLevel + 0.5f) continue;

                float d = NearestSpotDistance(x, z);
                if (d <= worst) continue;

                worst = d;
                at = new Vector2(x, z);
            }
        }

        return worst;
    }

    /// <summary>覆盖报告：最远距离 + 超标采样点的死角清单（最多列三处）</summary>
    public static string CoverageReport()
    {
        float worst = 0f;
        Vector2 worstAt = Vector2.zero;
        var holes = new List<Vector3>();          // (x, z, 距离)
        int over = 0;

        for (float z = 12.5f; z < OpenWorld.Size; z += 25f)
        {
            for (float x = 12.5f; x < OpenWorld.Size; x += 25f)
            {
                if (OpenWorld.Height(x, z) < OpenWorld.WaterLevel + 0.5f) continue;

                float d = NearestSpotDistance(x, z);
                if (d > worst) { worst = d; worstAt = new Vector2(x, z); }
                if (d <= CoverTarget) continue;

                over++;

                // 附近 120 米内已经记过就合并（留更差的那个），免得列表里全是同一片死角
                bool merged = false;
                for (int i = 0; i < holes.Count; i++)
                {
                    if ((holes[i].x - x) * (holes[i].x - x) + (holes[i].y - z) * (holes[i].y - z) >= 120f * 120f)
                        continue;

                    if (d > holes[i].z) holes[i] = new Vector3(x, z, d);
                    merged = true;
                    break;
                }

                if (!merged && holes.Count < 12) holes.Add(new Vector3(x, z, d));
            }
        }

        var sb = new StringBuilder();
        sb.Append($"\n覆盖：陆地最远 {worst:0} 米（在 ({worstAt.x:0},{worstAt.y:0})），目标 ≤ {CoverTarget:0} 米 —— ");

        if (over == 0) return sb.Append("达标。").ToString();

        holes.Sort((a, b) => b.z.CompareTo(a.z));
        sb.Append($"★ 不达标：超标采样点 {over} 个，死角：");
        for (int i = 0; i < Mathf.Min(3, holes.Count); i++)
            sb.Append($"({holes[i].x:0},{holes[i].y:0}) {holes[i].z:0} 米  ");

        return sb.ToString();
    }

    /// <summary>锚点规划报告（编辑器自检直接用它）：逐处列出落脚点、脚下地面、离最近锚点的余量，
    /// 再把"锚点悬空 / 埋进地里"和"落点站不住"的挑出来单独报</summary>
    public static string PlanReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"计划 {Spots.Count} 处锚点（其中关键锚点 {CountMajor()} 处；地形校验；贴墙检查只有运行时才做得到）：");

        int problems = 0;
        for (int i = 0; i < Spots.Count; i++)
        {
            var spot = Spots[i];
            bool ok = ResolveSpot(spot, runtime: false, out Vector3 pos, out string how);
            spot.resolved = ok ? pos : Vector3.zero;
            spot.how = how;
            Spots[i] = spot;
            if (!ok) problems++;
        }

        int offGround = 0, badLanding = 0;
        var offText = new List<string>();
        var landText = new List<string>();

        for (int i = 0; i < Spots.Count; i++)
        {
            var s = Spots[i];
            bool placed = s.resolved != Vector3.zero;

            float margin = float.MaxValue;
            for (int k = 0; k < Spots.Count; k++)
            {
                if (k == i) continue;
                margin = Mathf.Min(margin, Vector2.Distance(SpotPos(s), SpotPos(Spots[k])));
            }
            if (Spots.Count < 2) margin = 999f;

            string tag = s.major ? "关键" : "普通";

            if (!placed)
            {
                sb.AppendLine($"  [{tag}] {s.name}  ★ 地形校验不过：{s.how}  离最近锚点 {margin:0} m");
                continue;
            }

            // 锚点自己站得住吗：脚下真实地面和锚点高度对不对得上
            //  · 埋进地形（anchY 比地面低）→ 传送过去会掉出地图，必须修
            //  · 悬在地面上方 → 台地没垫到位；只有站在港城那种人工石台上才算正常
            float h = OpenWorld.Height(s.resolved.x, s.resolved.z);
            float d = s.resolved.y - h;
            bool sunk = d < -0.9f;
            bool onDeck = d > 0.9f && LiyueBuildings.OnHarborDeck(s.resolved.x, s.resolved.z, out _);
            bool groundOk = !sunk && (d <= 0.9f || onDeck);
            if (!groundOk)
            {
                offGround++;
                if (offText.Count < 6)
                    offText.Add($"{s.name}({s.resolved.x:0},{s.resolved.z:0}) 锚点 {s.resolved.y:0.0} 米 / " +
                                $"地面 {h:0.0} 米（差 {d:+0.0;-0.0}）");
            }

            // 传送落点站得住吗（站在港城那种人工石台上不算问题：台面本来就比地形高）
            Vector3 land = ResolveLandSpot(s, s.resolved);
            float lh = OpenWorld.Height(land.x, land.z);
            bool landOk = lh > OpenWorld.WaterLevel + 0.6f &&
                          (land.y - lh < 0.8f || LiyueBuildings.OnHarborDeck(land.x, land.z, out _));
            if (!landOk)
            {
                badLanding++;
                if (landText.Count < 6)
                    landText.Add($"{s.name} 落点 ({land.x:0},{land.z:0}) 高度 {land.y:0.0} / 地面 {lh:0.0}");
            }

            sb.AppendLine($"  [{tag}] {s.name}  地形可行位置 ({s.resolved.x:0},{s.resolved.z:0})  " +
                          $"地面高 {s.resolved.y:0.0}m  {s.how}  离最近锚点 {margin:0} m" +
                          (s.unlockedAtStart ? "  （默认点亮）" : "") +
                          (groundOk ? "" : "  ★ 锚点没踩在地面上") +
                          (landOk ? "" : "  ★ 落点站不住"));
        }

        foreach (var t in offText) sb.AppendLine($"  · 锚点悬空 / 埋地：{t}");
        foreach (var t in landText) sb.AppendLine($"  · 落点有问题：{t}");

        sb.AppendLine($"锚点异常 {problems} 项；锚点没踩在地上 {offGround} 处；落点站不住 {badLanding} 处");
        sb.Append(CoverageReport());
        return sb.ToString();
    }
}
