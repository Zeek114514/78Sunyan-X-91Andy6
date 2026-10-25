using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 「Andy的港门」的几何与布局（阶段 E）：璃月的主城 —— 依山面海的大港。
///
/// 【为什么是 LiyueBuildings 的 partial】
/// 港城要重用那四个中式零件（ChineseRoof / RedColumn / Lantern / HorseHeadWall）和 Box / Cyl / Prism
/// 这些底层图元，它们都是 LiyueBuildings 的私有成员。做成 partial 就直接能用，
/// 不用把一堆私有图元改成 internal，也不用把一千多行的零件库再撑大一千行。
///
/// 【城的骨架】
///   · 局部坐标：城心 = LiyueHarbor 的码头台地中心，"向海" = 城心 → 内湾中心。
///     P(沿街, 向海, 高度) 把局部坐标换到世界坐标；所有位置都从 LiyueHarbor 的常量算 ——
///     地形改了城跟着走，不会建到水里或坡上去。
///   · 主码头：沿内湾岸线一段段铺（岸线是圆台地被内湾啃出来的弧，不是直线，所以用 HarborShore 现量）
///   · 四层街道：下街贴水，逐层往内陆抬 2.8 米，每层一块台面 + 三级内收的挡土墙 + 望柱栏杆 + 临街民居
///   · 上城（第四层）：一大块台地，上面是岩神像广场、钟楼，背后用两级宽台阶接回地面
///   · 吊脚楼群：沿内湾岸线杵在水上（柱子扎进水里）
///   · 地标：主码头、楼阁（三层街）、牌坊（上城入口 + 码头中轴）、钟楼、灯塔
///
/// 【和地形的关系】
/// 地形的台地 / 内湾 / 外湾全在 LiyueHarbor.ShapeTerrain 里（阶段 B）。这里只负责"往上摆东西"，
/// 一个数字都不另写 —— 唯一的例外是岸线：湾是圆的，岸线到城心的距离随沿街位置变化，
/// 所以档口位置用 HarborShore() 现量（1.5 米一档扫到地面落水为止）。
/// </summary>
public static partial class LiyueBuildings
{
    // ================= 局部坐标系 =================

    /// <summary>向海方向的单位向量（城心 → 内湾中心）</summary>
    static Vector2 HarborSea()
    {
        float dx = LiyueHarbor.InnerBayX - LiyueHarbor.HarborX;
        float dz = LiyueHarbor.InnerBayZ - LiyueHarbor.HarborZ;
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        return new Vector2(dx / d, dz / d);
    }

    /// <summary>向海方向的 yaw：本地 +z 朝海（码头、吊脚楼、临街民居都朝它）</summary>
    public static float HarborYaw()
    {
        var s = HarborSea();
        return Mathf.Atan2(s.x, s.y) * Mathf.Rad2Deg;
    }

    static Quaternion HarborRot() => Quaternion.Euler(0f, HarborYaw(), 0f);

    /// <summary>局部坐标 → 世界坐标：along = 沿街（本地 +x），sea = 向海（本地 +z，越大越靠海）</summary>
    static Vector3 P(float along, float sea, float y)
    {
        var s = HarborSea();
        var a = new Vector2(s.y, -s.x);                       // 沿街方向（本地 +x）
        return new Vector3(LiyueHarbor.HarborX + a.x * along + s.x * sea, y,
                           LiyueHarbor.HarborZ + a.y * along + s.y * sea);
    }

    /// <summary>沿"向海"方向量：地面刚落到水面（岸线）在多少米处。
    /// 内湾是圆的，岸线到城心的距离随沿街位置变化 —— 码头 / 吊脚楼都要按它摆，不能写死距离</summary>
    static float HarborShore(float along)
    {
        for (float sea = 45f; sea <= 215f; sea += 1.5f)
        {
            var p = P(along, sea, 0f);
            if (OpenWorld.Height(p.x, p.z) <= OpenWorld.WaterLevel + 0.05f) return sea;
        }
        return LiyueHarbor.QuayR;                            // 兜底：正好在台地边缘
    }

    // ================= 占位表（摆放 + 自检共用） =================
    //
    // 港城的零件全都摆在同一个局部坐标系里（along 沿街 / sea 向海 / y 高度），局部到世界只差一次
    // 刚体旋转 —— 所以"谁压到谁"直接在局部平面上判，和世界坐标等价，还不用管朝向。
    //
    // 分两类：
    //   · 台面（floor）：可踩的人造地面（四层街道、码头石台、广场铺地）。零件摆在上面是设计意图，
    //     所以台面之间、台面和零件之间都**不算**重叠；它的用处是判"零件有没有悬空 / 埋进台面"
    //   · 零件（prop）：系船柱、货箱、摊子、旗、灯笼串、民居、地标、船…… 互相不许在平面和高度上都压到
    //
    // 这份表是自检的唯一依据，所以**建几何时给的落点必须和登记用同一份数字**（见 HarborLayoutReport）

    /// <summary>零件"该站在什么上"：自检按它判悬空</summary>
    public const byte AnchorDeck   = 0;   // 人造台面（台面顶要和落点齐平）
    public const byte AnchorGround = 1;   // 自然地形（地形高度要和落点齐平）
    public const byte AnchorWater  = 2;   // 水上 / 水里（吊脚楼、停靠的船、吊在半空的灯笼串）：不判落地

    public struct HarborSpot
    {
        public string name;
        public float x, z;                  // 世界坐标（判地形高度用）
        public float a, s;                  // 局部坐标：沿街 / 向海（判重叠用）
        public float ra, rs;                // 平面半尺寸：沿街 / 向海（零件就是半径）
        public float y0, y1;                // 高度范围
        public byte anchor;
    }

    /// <summary>登记在册的零件</summary>
    public static readonly List<HarborSpot> HarborProps = new List<HarborSpot>();

    /// <summary>登记在册的台面（容器）</summary>
    public static readonly List<HarborSpot> HarborFloors = new List<HarborSpot>();

    /// <summary>因为位置已被占而没摆下的零件（名字 + 局部坐标）。★ 这个名单是修布局的线索：
    /// 名字里出现地标就说明那块地被小件抢了，要挪的是小件</summary>
    static readonly List<string> _culled = new List<string>();

    static int _propTotal;               // 尝试摆下的零件总数（含被挤掉的）

    /// <summary>登记一个零件：pos = 它落地的那个点（世界坐标，和建几何时给的同一个），r = 平面半径，
    /// height = 总高。anchor = 它该站在什么上。返回 false = 这个位置已经被别的零件占了 ——
    /// 调用方要么干脆不摆（街边小件），要么挪位置（地标）</summary>
    static bool Prop(string name, Vector3 pos, float r, float height, byte anchor = AnchorDeck)
        => PropBox(name, pos, r, r, height, anchor);

    /// <summary>登记一个"长条"零件（挡土墙、栏杆这类沿街铺开的）：ra / rs 分开给</summary>
    static bool PropBox(string name, Vector3 pos, float ra, float rs, float height, byte anchor = AnchorWater)
    {
        HarborLocal(pos, out float a, out float s);
        var spot = new HarborSpot
        {
            name = name, x = pos.x, z = pos.z, a = a, s = s, ra = ra, rs = rs,
            y0 = pos.y, y1 = pos.y + height, anchor = anchor,
        };

        _propTotal++;

        foreach (var o in HarborProps)
        {
            if (!SpotHit(spot, o)) continue;
            _culled.Add($"{name}({a:0.#},{s:0.#}) ↔ {o.name}({o.a:0.#},{o.s:0.#})");
            return false;
        }

        HarborProps.Add(spot);
        return true;
    }

    /// <summary>登记一块可踩的人造台面：a / s = 中心，ra / rs = 沿街 / 向海半尺寸，top = 台面顶高度</summary>
    static void Floor(string name, float a, float s, float ra, float rs, float top)
        => HarborFloors.Add(new HarborSpot { name = name, a = a, s = s, ra = ra, rs = rs, y0 = top, y1 = top });

    /// <summary>两个占位算不算"压到一起"：平面（沿街 / 向海）上都压到 **且** 高度上也压到。
    /// 高度那一关是必须的 —— 否则广场上吊在半空的灯笼串会被判成压在石灯上。
    /// 两边都是圆的（ra == rs）按圆判：方判等于把半径放大 1.41 倍，斜着挨着的两件会被误判</summary>
    static bool SpotHit(in HarborSpot p, in HarborSpot q)
    {
        const float slack = 0.35f;       // 容差：贴边、互相蹭 0.35 米以内不算压
        float da = p.a - q.a, ds = p.s - q.s;

        if (p.ra == p.rs && q.ra == q.rs)
        {
            float rr = p.ra + q.ra - slack;
            if (da * da + ds * ds >= rr * rr) return false;
        }
        else
        {
            if (Mathf.Abs(da) > p.ra + q.ra - slack) return false;
            if (Mathf.Abs(ds) > p.rs + q.rs - slack) return false;
        }

        return p.y0 < q.y1 - 0.15f && q.y0 < p.y1 - 0.15f;
    }

    /// <summary>世界坐标 → 局部坐标（沿街 / 向海），P 的逆运算</summary>
    static void HarborLocal(Vector3 p, out float a, out float s)
    {
        var sea = HarborSea();
        var along = new Vector2(sea.y, -sea.x);
        float dx = p.x - LiyueHarbor.HarborX, dz = p.z - LiyueHarbor.HarborZ;
        a = dx * along.x + dz * along.y;
        s = dx * sea.x + dz * sea.y;
    }

    /// <summary>第 lv 层台面的顶（0 下街 → 3 上城）</summary>
    static float DeckTop(int lv) => lv == 0 ? Deck0 : lv == 1 ? Deck1 : lv == 2 ? Deck2 : Deck3;

    // ================= 城的竖向分层 =================

    const float StreetStep = 2.8f;                           // 每层街道的高差
    const float Deck0 = LiyueHarbor.QuayY + 0.3f;            // 6.8  下街（临水那条）
    const float Deck1 = Deck0 + StreetStep;                  // 9.6  二层街
    const float Deck2 = Deck1 + StreetStep;                  // 12.4 三层街
    const float Deck3 = Deck2 + StreetStep;                  // 15.2 上城

    // 各层台地在"向海"方向上的范围（米；越小越靠内陆）。
    // ★ 最外沿（下街的 82 米）必须留在岸线以内：岸线沿街 -59 / 0 / 59 米处约 91 / 98 / 112 米，
    //   所以最外沿卡在 82 才有一圈余量（第一版铺到 96，西端直接悬在水上，自检里抓出来了）
    const float Sea0A = 64f,   Sea0B = 82f;
    const float Sea1A = 42f,   Sea1B = 64f;
    const float Sea2A = 20f,   Sea2B = 42f;
    const float Sea3A = -98f,  Sea3B = 20f;

    /// <summary>岩神像广场（上城）的中心，局部坐标。铺地、石像、石灯、旗子都从它算 ——
    /// HarborGround 登记台面时也要用同一份数字</summary>
    const float PlazaU = -22f, PlazaSea = -34f;

    /// <summary>楼阁（三层街的地标）的位置，局部坐标。★ 三层街是最窄的一层（进深只有 22 米），
    /// 临街民居那一排在 Sea2A+4.6、灯笼串吊在 Sea2B-3.4，中间只剩一条带 —— 楼阁要落在这条带里
    /// （沿街方向对准两栋民居之间的空档，免得压到谁）</summary>
    const float PagodaU = 27.5f, PagodaSea = Sea2A + 14f;

    /// <summary>把整座港城拼进缓冲区。调用方负责 Begin / BuildMesh（见 LiyueSiteSpawner.Init）</summary>
    public static void AppendHarbor()
    {
        HarborProps.Clear();
        HarborFloors.Clear();
        _culled.Clear();
        _propTotal = 0;

        HarborGround();      // ① 先登记可踩的台面（自检要拿它判零件悬空）
        HarborLandmarks();   // ② 地标先摆：楼阁 / 牌坊 / 钟楼 / 灯塔的位置是设计上的骨架
        HarborWharf();       // 主码头（含船 / 吊架 / 货箱）
        HarborStreets();     // 四层街道 + 上城
        HarborUpperTown();   // 上城街区（台面 172×118 米，不铺满就是一大片空石板）
        HarborStiltRow();    // 吊脚楼群（柱子扎进内湾）
        HarborPlaza();       // 岩神像广场
        HarborInland();      // 上城背后的宽台阶，把台地接回地面
    }

    /// <summary>登记所有可踩的人造台面。★ 必须和建几何时用的是同一套数字（都从 LiyueHarbor / DeckRange 来），
    /// 否则自检会拿错台面高度去判零件悬空。码头石台不在这里 —— 它是一段段按岸线算出来的，由 HarborWharf 自己登记</summary>
    static void HarborGround()
    {
        for (int lv = 0; lv < 4; lv++)
        {
            DeckRange(lv, out float seaA, out float seaB, out float len);
            Floor($"第 {lv + 1} 层街道", 0f, (seaA + seaB) * 0.5f, len * 0.5f, (seaB - seaA) * 0.5f, DeckTop(lv));
        }

        Floor("岩神像广场", PlazaU, PlazaSea, 37f, 28f, Deck3 + 0.56f);
    }

    // ================= 主码头 =================

    static void HarborWharf()
    {
        var rot = HarborRot();

        // 码头石台分 11 段（每段 20 米）：内沿统一压在下街那条边（Sea0B）上、外沿按"自己这一段里
        // 最靠内陆的那处岸线"往里退 2.5 米 —— 内湾是圆的，一次铺 42 米的话东端会有一整角伸到水面外
        // （自检里那处"浮水"就是它）。段与段搭 1 米，外沿形成一级级阶梯：石砌码头本来就是直线段拼的。
        for (int k = -5; k <= 5; k++)
        {
            float u = k * 20f;
            float edge = Mathf.Min(Mathf.Min(HarborShore(u - 10f), HarborShore(u - 5f)),
                                   Mathf.Min(HarborShore(u + 5f), HarborShore(u + 10f))) - 2.5f;
            float inner = Sea0B + 1f;                        // 和下街台面搭 1 米，走上去是平的
            float deep = edge - inner;
            float seaC = (edge + inner) * 0.5f;

            Floor("码头石台", u, seaC, 11f, deep * 0.5f, Deck0);

            // 石台：实心块，**从水面以下**一直砌到台面。★ 底原来只砌到 10.8 米（QuayY - 2.2），
            // 而外沿岸坡只有 4~5 米高 —— 站在船上能看见码头底下悬着 6 米高的洞
            float yBottom = OpenWorld.WaterLevel - 2f;
            Box(P(u, seaC, (Deck0 + yBottom) * 0.5f), new Vector3(22f, Deck0 - yBottom, deep), rot,
                Limestone, LimestoneDark);

            for (int i = -1; i <= 1; i++)                    // 系船柱
            {
                var bollard = P(u + i * 6f, edge - 1.8f, Deck0);
                if (!Prop("系船柱", bollard, 0.35f, 1.1f)) continue;
                Cyl(bollard, 1.1f, 0.3f, 0.26f, 6, Wood, true);
            }

            if (k % 2 == 0)                                  // 货箱 / 鱼篓 / 火盆：隔一段摆一处
            {
                // ★ 占位表登记的是"落地点"，Box 给的是盒心 —— 两个都要按台面算，不然箱子会悬空半米
                var crateBase = P(u + 3f, edge - 9f, Deck0);
                if (Prop("货箱", crateBase, 0.75f, 1.0f))
                    Box(crateBase + Vector3.up * 0.5f, new Vector3(1.0f, 1.0f, 1.0f), rot, WoodLight, Wood);

                var crate2Base = P(u + 4.4f, edge - 10f, Deck0);
                if (Prop("货箱", crate2Base, 0.6f, 0.7f))
                    Box(crate2Base + Vector3.up * 0.35f, new Vector3(0.8f, 0.7f, 0.8f), rot, WoodLight, Wood);

                for (int i = 0; i < 3; i++)
                {
                    var basket = P(u - 7f + i * 1.1f, edge - 8.6f, Deck0);
                    if (!Prop("鱼篓", basket, 0.4f, 0.7f)) continue;
                    Cyl(basket, 0.7f, 0.5f, 0.58f, 8, Bamboo, true);
                }

                var brazier = P(u - 2.5f, edge - 8f, Deck0);   // 火盆要和鱼篓那一堆错开（原来压在一起）
                if (Prop("火盆", brazier, 0.55f, 0.6f))
                {
                    Cyl(brazier, 0.6f, 0.5f, 0.6f, 8, LimestoneDark, true);
                    Flames.Add(brazier + Vector3.up * 0.7f);
                }
            }
        }

        // 停靠的船：船位在水里（AnchorWater），★ 船位要和吊脚楼错开 ——
        //   两样都在岸线外侧，摆同一个沿街位置就会叠在一起
        Boat(-45f, 8f, 96f, 12f);
        Boat(-15f, 11f, 82f, 10f);
        Boat(15f, 9f, 110f, 14f);

        // 木吊架（装卸）：两根柱子 + 横梁 + 吊绳挂钩，都站在码头石台上（★ 原来给的是 0.3 米的悬浮高度）
        const float craneA = 16f, craneS = 96f;

        for (int i = -1; i <= 1; i += 2)
        {
            var post = P(craneA + i * 4f, craneS, Deck0);
            if (!Prop("吊架柱", post, 0.4f, 6.4f)) continue;
            Cyl(post, 6.4f, 0.34f, 0.28f, 6, Wood);
        }

        Box(P(craneA, craneS, Deck0 + 6.4f), new Vector3(9.4f, 0.4f, 0.4f), rot, Wood, WoodLight);
        Box(P(craneA + 2f, craneS, Deck0 + 5.15f), new Vector3(0.14f, 2.5f, 0.14f), rot, Cloth, Cloth);

        var hook = P(craneA + 2f, craneS, Deck0 + 3.65f);
        if (Prop("吊钩", hook, 0.7f, 1.0f, AnchorWater))
            Box(hook + Vector3.up * 0.25f, new Vector3(1.2f, 0.5f, 1.2f), rot, Wood, WoodLight);

        // 停靠的船：★ 占位按"船身"登记（船长 90° 横在岸边 → 沿街方向只有 2 米多宽），
        //   按半径记会把旁边 5 米内的东西全判成压上了
        void Boat(float along, float outTo, float yaw, float len)
        {
            var pos = P(along, HarborShore(along) + outTo, OpenWorld.WaterLevel + 0.15f);
            if (!PropBox("停靠的船", pos, 1.4f, len * 0.5f, 5f, AnchorWater)) return;
            HarborBoat(pos, HarborYaw() + yaw, len);
        }

        // 吊架横梁下面挂一排灯笼（挂在梁上）。★ 原来那串挂在 92 米处、两头不靠，等于吊在半空
        HarborLanterns(P(craneA - 4f, craneS, Deck0 + 6.3f), P(craneA + 4.4f, craneS, Deck0 + 6.3f), 3);
    }

    static void HarborBoat(Vector3 water, float yaw, float len)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        Box(water + Vector3.up * 0.45f, new Vector3(2.0f, 0.9f, len), rot, Wood, WoodLight);
        Box(water + Vector3.up * 0.92f, new Vector3(1.5f, 0.2f, len * 0.86f), rot, WoodLight, Wood);

        for (int s = -1; s <= 1; s += 2)                     // 船头船尾翘起
            Box(water + rot * new Vector3(0f, 1.15f, s * (len * 0.5f + 0.15f)), new Vector3(1.7f, 0.5f, 1.0f),
                rot * Quaternion.Euler(s * -24f, 0f, 0f), Wood, WoodLight);

        Prism(water + rot * new Vector3(0f, 1.0f, -len * 0.08f), 1.15f, 1.05f, 0.95f, 6, Tile);   // 乌篷
        Cyl(water + rot * new Vector3(0f, 1.0f, len * 0.30f), 3.4f, 0.09f, 0.08f, 5, Wood);       // 桅杆
        Lantern(water + rot * new Vector3(0f, 4.5f, len * 0.30f), 0.4f, LanternRed, Gold);
    }

    // ================= 四层街道 =================

    static void HarborStreets()
    {
        // ★ 石阶要先登记：它是上下层的唯一通道，不能被街边小件（旗子 / 摊子 / 灯笼）挤掉。
        //   占位表按登记顺序排优先级，谁先占谁得
        for (int s = -1; s <= 1; s++)
        {
            float u = StairU(s + 1);
            // ★ 石阶必须架在"上下两层台面的交界线"（Sea_lv_A）上，不是台面临海那一条边 ——
            //   架在临海沿的话，台阶会从下街的地面往上爬到上街的高度，尽头悬在离墙十三米的空中，
            //   而且会横穿临海侧栏杆（第一版就是这么错的，自检抓出来了）
            HarborStairs(u, Sea0A + 6f, Sea0A, Deck0, Deck1);
            HarborStairs(u, Sea1A + 6f, Sea1A, Deck1, Deck2);
            HarborStairs(u, Sea2A + 6f, Sea2A, Deck2, Deck3);
        }

        HarborTerrace(0, Deck0, Sea0A, Sea0B, 118f, 11);
        HarborTerrace(1, Deck1, Sea1A, Sea1B, 116f, 22);
        HarborTerrace(2, Deck2, Sea2A, Sea2B, 120f, 33);
        HarborTerrace(3, Deck3, Sea3A, Sea3B, 172f, 44);
    }

    /// <summary>三条上下台阶的沿街位置（中轴 + 两侧各一条）。民居 / 摊子要按它让开走廊</summary>
    static float StairU(int i) => (i - 1) * 46f;

    /// <summary>这一处沿街位置在不在上下台阶的走廊里（台阶宽 8 米 + 民居出檐的余量）。
    /// 台阶架在下层台面的临街那一条上，走廊里的民居 / 摊子要让开</summary>
    static bool NearStairCorridor(float u)
    {
        for (int i = 0; i < 3; i++)
            if (Mathf.Abs(u - StairU(i)) < 8f) return true;
        return false;
    }

    /// <summary>一层街道：台面 + 临海侧三级内收的挡土墙 + 望柱栏杆 + 临街民居 / 摊子 / 旗子</summary>
    static void HarborTerrace(int lv, float yTop, float seaA, float seaB, float len, int seed)
    {
        var rot = HarborRot();
        float seaC = (seaA + seaB) * 0.5f;
        float deep = Mathf.Abs(seaB - seaA);

        // 台体做成**实心块**：从地面一直砌到台面。只铺一块薄板的话，东西两侧（没有挡土墙的那两边）
        // 会露出"台子悬在半空"的底边，玩家绕着城走一圈就看见
        float yGround = LiyueHarbor.QuayY - 0.6f;
        Box(P(0f, seaC, (yTop + yGround) * 0.5f), new Vector3(len, yTop - yGround, deep), rot,
            new Color(.64f, .62f, .56f), LimestoneDark);

        // Inlaid paving gives each street a pedestrian axis and a readable scale.
        for (float u = -len * .5f + 2f; u < len * .5f - 1f; u += 3f)
        {
            Box(P(u, seaB - 6.7f, yTop + .025f), new Vector3(2.86f, .045f, 2.7f), rot,
                new Color(.68f, .66f, .59f), LimestoneDark);
            if (!NearStairCorridor(u))
                Box(P(u, seaB - 8.25f, yTop + .03f), new Vector3(2.86f, .05f, .12f), rot, Gold * .8f, LimestoneDark);
        }

        // 挡土墙：从地面往上三级内收（一整面 8 米高的平墙看着像块巨石）
        float yBottom = LiyueHarbor.QuayY - 0.6f;
        int steps = Mathf.Clamp(Mathf.RoundToInt((yTop - yBottom) / 2.6f), 1, 4);
        float sh = (yTop - yBottom) / steps;
        for (int i = 0; i < steps; i++)
            Box(P(0f, seaB - 0.7f - i * 0.55f, yBottom + sh * (i + 0.5f)),
                new Vector3(len, sh, 1.5f), rot, Limestone, LimestoneDark);

        // 临海侧栏杆：望柱 + 两道横杆 + 台面压条
        int posts = Mathf.Max(2, Mathf.RoundToInt(len / 4f));
        for (int i = 0; i <= posts; i++)
        {
            float u = -len * 0.5f + len * i / posts;
            Box(P(u, seaB - 1.1f, yTop + 0.55f), new Vector3(0.24f, 1.1f, 0.24f), rot, LimestoneDark, LimestoneDark);
        }
        Box(P(0f, seaB - 1.1f, yTop + 0.98f), new Vector3(len, 0.16f, 0.2f), rot, LimestoneDark, LimestoneDark);
        Box(P(0f, seaB - 1.1f, yTop + 0.46f), new Vector3(len, 0.12f, 0.16f), rot, LimestoneDark, LimestoneDark);
        Box(P(0f, seaB - 0.35f, yTop - 0.02f), new Vector3(len, 0.14f, 0.7f), rot, Limestone, Limestone);

        // 临街民居：沿街一排，都朝海。★ 间距按 16 米（原来 20 米）、空段概率 0.15（原来 0.28）——
        //   一层台面只有 18~22 米进深，一排房子排稀了整条街就空
        // Deliberate frontage lots: a stair and sightline always separates the clusters.
        float[] lots = { -34f, -17f, 17f, 34f };
        for (int i = 0; i < lots.Length; i++)
        {
            float u = lots[i];
            HarborPremise type = lv == 0 ? HarborPremise.Warehouse
                : lv == 1 ? (i % 2 == 0 ? HarborPremise.Shop : HarborPremise.Teahouse)
                : (i % 2 == 0 ? HarborPremise.Residence : HarborPremise.Guild);
            HarborPremiseAt(u, seaA + 5.2f + (i % 2) * .8f, yTop, HarborYaw(), type, seed + i);
        }

        // 街边石灯：临海那一条靠近台面两端各一盏（中间那段留给摊子和灯笼串，摆中间会撞上）
        for (int i = 0; i < 2; i++)
        {
            float u = (i == 0 ? -1f : 1f) * (len * 0.5f - 12f);
            var lampAt = P(u, seaB - 3.2f, yTop);
            if (!Prop("街边石灯", lampAt, 0.9f, 2.7f)) continue;
            HarborStoneLamp(lampAt, HarborYaw() + 180f);
        }

        // Market vendors belong to the lower commercial street.
        for (int i = 0; i < (lv == 1 ? 2 : 0); i++)
        {
            float u = -len * 0.5f + 22f + (len - 44f) * i;
            if (NearStairCorridor(u)) continue;
            var stallAt = P(u, seaB - 4.5f, yTop);
            if (!Prop("摊子", stallAt, 2.2f, 3.6f)) continue;
            HarborStall(stallAt, HarborYaw());
        }

        // 旗子的半径要含那面布（往 +x 挑 1.85 米），否则自检看不出旗布戳进隔壁民居的檐里。
        // ★ 位置要落在"最后一栋民居和台面端头之间"：原来写在 len/2 - 16 处，正好撞上两侧的石阶走廊
        Flag(-len * 0.5f + 5f, seaA + 3f, yTop);
        Flag(len * 0.5f - 5f, seaA + 3f, yTop);

        void Flag(float along, float sea, float y)
        {
            var flagAt = P(along, sea, y);
            if (!Prop("旗子", flagAt, 1.6f, 6.2f)) return;
            HarborFlag(flagAt, HarborYaw(), Vermilion);
        }

        // 灯笼串要吊在摊子的幌子之上（摊子顶 3.7 米）—— 吊低了会和摊子压到一起
        HarborLanterns(P(-len * 0.5f + 10f, seaB - 3.4f, yTop + 5.5f),
                       P(len * 0.5f - 10f, seaB - 3.4f, yTop + 5.5f), Mathf.RoundToInt(len / 22f));
    }

    // ================= 上城街区 =================

    /// <summary>
    /// 上城（第四层台面）的街区：台面有 172 × 118 米，只摆广场和钟楼的话就是一大片空石板
    /// （实机里"建筑太少太空旷"说的就是它）。这里按"排"铺民居 —— 内城两条、临海一条、
    /// 广场东西两侧各一两列，中间夹一条集市，再把石灯和行道树补上。
    ///
    /// 位置全部过占用表：和广场 / 钟楼 / 石阶挤在一起的会被跳过（报告里"让位"那几行就是）。
    /// 所以这里可以放心按理想网格铺，不用先算一遍谁挨着谁。
    /// </summary>
    static void HarborUpperTown()
    {
        // Upper town is a quiet courtyard district, not another grid of cottages.
        HarborPremiseAt(-58, -79, Deck3, HarborYaw(), HarborPremise.Guild, 90);
        HarborPremiseAt(-33, -80, Deck3, HarborYaw(), HarborPremise.Residence, 91);
        HarborPremiseAt(24, -77, Deck3, HarborYaw(), HarborPremise.Teahouse, 92);
        HarborPremiseAt(55, -74, Deck3, HarborYaw() - 90, HarborPremise.Residence, 93);
        HarborPremiseAt(57, -43, Deck3, HarborYaw() - 90, HarborPremise.Shop, 94);
        HarborPremiseAt(56, -15, Deck3, HarborYaw() - 90, HarborPremise.Guild, 95);
        HarborPremiseAt(-76, -17, Deck3, HarborYaw() + 90, HarborPremise.Teahouse, 96);

        // ---------- 街边石灯：沿临海那一条每隔一段一盏 ----------
        for (int i = 0; i < 5; i++)
        {
            float a = -72f + i * 36f;
            var at = P(a, Sea3B - 6f, Deck3);
            if (!Prop("街边石灯", at, 0.9f, 2.7f)) continue;
            HarborStoneLamp(at, HarborYaw() + 180f, flame: i == 2);
        }

        // ---------- 行道树：内城两条街之间、临海那条街的内侧各点几棵 ----------
        for (int i = 0; i < 6; i++)
        {
            HarborTree(P(-75f + i * 30f, Sea3A + 22f, Deck3), H(9, i, 77) + 0.2f);
            HarborTree(P(-60f + i * 24f, Sea3B - 10f, Deck3), H(10, i, 78) + 0.2f, 0.9f);
        }
    }

    /// <summary>行道树：石台基 + 木色树干 + 三团树冠（广场 / 上城点几棵，不然全是石板太干）。
    /// 占位自己登记，挤不下就返回 false（调用方不用管）</summary>
    static bool HarborTree(Vector3 basePos, float seed, float scale = 1f)
    {
        if (!Prop("行道树", basePos, 2.1f * scale, 7f * scale)) return false;

        float h = (4.2f + seed * 1.6f) * scale;
        var leaf = Color.Lerp(new Color32(0x4f, 0x8f, 0x3f, 255), new Color32(0x71, 0xab, 0x4e, 255), seed);

        Disc(basePos + Vector3.up * 0.14f, 0.9f * scale, 1.5f * scale, 8, HarborRot(), LimestoneDark);
        Prism(basePos + Vector3.up * 0.2f, h, 0.24f * scale, 0.17f * scale, 6, Wood);
        Ball(basePos + Vector3.up * (0.2f + h + 0.9f * scale), 1.75f * scale, leaf, 7, 0.78f, seed);
        Ball(basePos + Vector3.up * (0.2f + h + 0.1f * scale), 1.2f * scale, leaf * 0.9f, 6, 0.85f, seed + 0.4f);
        Ball(basePos + Vector3.up * (0.2f + h + 1.7f * scale), 1.0f * scale, leaf * 1.05f, 6, 0.8f, seed + 0.8f);
        return true;
    }

    /// <summary>石阶：从下层台面走到上层台面。每一级一块石板，走得上去就行</summary>
    static void HarborStairs(float along, float seaFrom, float seaTo, float yFrom, float yTo)
    {
        // 整段石阶按一个占位登记（它是斜的、不落地，所以用 AnchorWater）：
        // 自检靠它看石阶有没有和民居 / 挡土墙压到一起
        var mid = P(along, (seaFrom + seaTo) * 0.5f, Mathf.Min(yFrom, yTo));
        if (!Prop("石阶", mid, 4.2f, Mathf.Abs(yTo - yFrom) + 0.4f, AnchorWater)) return;

        int n = Mathf.Max(2, Mathf.RoundToInt(Mathf.Abs(yTo - yFrom) / 0.4f));
        float dSea = (seaTo - seaFrom) / n;

        for (int i = 0; i < n; i++)
        {
            float top = Mathf.Lerp(yFrom, yTo, (i + 1f) / n);
            Box(P(along, seaFrom + dSea * (i + 0.5f), top - 0.16f),
                new Vector3(8f, 0.32f, Mathf.Abs(dSea) + 0.4f), HarborRot(), Limestone, LimestoneDark);
        }
    }

    // ================= 吊脚楼群 =================

    /// <summary>临内湾的一排吊脚楼：柱子扎进水里，楼面挑在水上（江南水乡那种）。
    /// 位置按岸线现量 —— 内湾是圆的，一条直线排过去有的会站到水里、有的会上岸</summary>
    static void HarborStiltRow()
    {
        for (int i = -2; i <= 2; i++)
        {
            float u = i * 30f;

            // ★ 吊脚楼要整栋挑在**水里**（岸线往外 3 米），不能压着码头石台的外沿 ——
            //   原来写在岸线里 3.5 米处，正好和码头那排系船柱叠在一起（自检抓出来的）
            float sea = HarborShore(u) + 3f;

            var p = P(u, sea, 0f);
            float y = Mathf.Clamp(OpenWorld.Height(p.x, p.z),
                                  OpenWorld.WaterLevel + 0.7f, LiyueHarbor.QuayY - 0.5f);

            // 占位按实际底面登记（6.2 × 5 的木屋 + 挑出去的阳台），半径记会把旁边的船也圈进来
            var at = new Vector3(p.x, y, p.z);
            if (!PropBox("吊脚楼", at, 3.6f, 4.0f, 9f, AnchorWater)) continue;

            Append(LiyueKind.StiltHouse, at, HarborYaw(), H(i, 9, 63));
        }
    }

    // ================= 岩神像广场 =================

    static void HarborPlaza()
    {
        var rot = HarborRot();
        const float u = PlazaU, sea = PlazaSea;

        Box(P(u, sea, Deck3 + 0.28f), new Vector3(74f, 0.56f, 56f), rot, new Color(.69f, .67f, .60f), LimestoneDark);   // 铺地
        Disc(P(u, sea, Deck3 + 0.6f), 7.5f, 9.5f, 16, rot, LimestoneDark);                            // 中心圆坛

        var shrineAt = P(u, sea, Deck3 + 0.58f);                                                      // 岩神像
        // ★ 广场正中是「七天神像 · 岩」：它是要能用的传送点 + 回复点，几何由 TeleportAnchor 运行时生成
        //   （见 StatuePoint），这里只登记占位，免得别的零件摆到它身上
        Prop("七天神像 · 岩", shrineAt, 5.5f, 15f);

        for (int i = -1; i <= 1; i += 2)
            for (int j = -1; j <= 1; j += 2)
            {
                var lampAt = P(u + i * 30f, sea + j * 22f, Deck3 + 0.56f);
                if (!Prop("广场石灯", lampAt, 0.9f, 2.7f)) continue;
                HarborStoneLamp(lampAt, HarborYaw());
            }

        PlazaFlag(u - 34f, sea + 24f);
        PlazaFlag(u + 34f, sea + 24f);

        // 广场临海一侧拉两段灯笼串
        HarborLanterns(P(u - 30f, sea + 20f, Deck3 + 5.4f), P(u - 8f, sea + 20f, Deck3 + 5.4f), 6);
        HarborLanterns(P(u + 8f, sea + 20f, Deck3 + 5.4f), P(u + 30f, sea + 20f, Deck3 + 5.4f), 6);

        // ---------- 集市：铺地临海那侧摆一排摊子（74 × 56 米的广场，只有神像和四盏石灯太空） ----------
        for (int i = 0; i < 2; i++)
        {
            var at = P(u - 26f + i * 52f, sea + 25f, Deck3 + 0.56f);        // 站在铺地上（高 0.56）
            if (!Prop("集市摊子", at, 2.2f, 3.6f)) continue;
            HarborStall(at, HarborYaw());
        }

        // 广场四角种树（对着四盏石灯，略往里收，免得压上）
        for (int i = -1; i <= 1; i += 2)
            for (int j = -1; j <= 1; j += 2)
                HarborTree(P(u + i * 25f, sea + j * 15f, Deck3 + 0.56f), 0.25f + 0.2f * (i + j + 2), 0.85f);

        void PlazaFlag(float along, float s)
        {
            var at = P(along, s, Deck3 + 0.56f);
            if (!Prop("旗子", at, 1.6f, 6.2f)) return;
            HarborFlag(at, HarborYaw(), Vermilion);
        }
    }

    // ================= 地标 =================

    static void HarborLandmarks()
    {
        // ★ 楼阁要落在"两侧石阶走廊之间、和民居那一排错开"的空档里（19.2 = 10.5 和 28.8 两栋民居的中间）。
        //   原来写在 52 处，正好压在上下石阶（±46）和上城那一排民居上
        var pagodaAt = P(PagodaU, PagodaSea, Deck2);
        if (Prop("楼阁", pagodaAt, 5.6f, 14f))
            Append(LiyueKind.Pagoda, pagodaAt, HarborYaw() + 180f, 0.62f);

        // 牌坊：一道在上城入口（内陆侧中轴），一道在码头中轴（临海端）
        var gateInland = P(0f, -78f, Deck3);
        if (Prop("牌坊", gateInland, 3.2f, 7.6f))
            Append(LiyueKind.Paifang, gateInland, HarborYaw(), 0.5f);

        // ★ 码头那道牌坊站在码头石台上（原来给了 0.3 米的悬浮高度）
        var gateWharf = P(0f, 88f, Deck0);
        if (Prop("牌坊", gateWharf, 3.2f, 7.6f))
            Append(LiyueKind.Paifang, gateWharf, HarborYaw() + 180f, 0.75f);

        // 钟楼：上城角上，钟亭里挂一口大钟
        var bellAt = P(-62f, -52f, Deck3);
        if (Prop("钟楼", bellAt, 5f, 22.5f))
            HarborBellTower(bellAt, HarborYaw() + 30f);

        // 灯塔：主码头东端，站在码头石台上（★ 它脚下是石台不是自然地形，锚点原来记错了）；
        // 往内陆收 14 米是为了躲开码头外沿那排系船柱
        var lightAt = P(92f, HarborShore(92f) - 14f, Deck0);
        if (Prop("灯塔", lightAt, 4.5f, 20f))
            HarborLighthouse(lightAt, HarborYaw() + 180f);
    }

    /// <summary>钟楼：石台基 + 三层收分的塔身（腰檐出挑）+ 顶层钟亭（四根朱柱 + 大钟 + 中式屋顶）</summary>
    static void HarborBellTower(Vector3 b, float yaw)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        Box(b + Vector3.up * 0.4f, new Vector3(9.0f, 0.8f, 9.0f), rot, Limestone, LimestoneDark);
        Box(b + Vector3.up * 2.9f, new Vector3(7.2f, 4.2f, 7.2f), rot, WallWhite, WallShade);
        Box(b + Vector3.up * 5.2f, new Vector3(8.6f, 0.5f, 8.6f), rot, Tile, Ridge);              // 腰檐
        Box(b + Vector3.up * 7.5f, new Vector3(6.2f, 4.2f, 6.2f), rot, Vermilion, VermilionDark);
        Box(b + Vector3.up * 9.85f, new Vector3(7.6f, 0.5f, 7.6f), rot, Tile, Ridge);

        // 顶层钟亭：柱脚落在腰檐上（10.1）、柱顶托住楼板，楼板再往上用短柱顶到屋顶。
        // ★ 原来柱脚悬在腰檐上方 2 米、屋顶又悬在楼板上方 2.8 米 —— 整座塔看着像几块零件飘在半空
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                RedColumn(b + rot * new Vector3(sx * 2.4f, 10.1f, sz * 2.4f), 5.5f, 0.28f, rot,
                          Vermilion, Limestone, Gold);

        Box(b + Vector3.up * 15.83f, new Vector3(6.8f, 0.46f, 6.8f), rot, Wood, WoodLight);       // 钟亭楼板
        Cyl(b + Vector3.up * 16.2f, 1.05f, 1.7f, 0.72f, 8, Gold);                                 // 大钟
        Box(b + Vector3.up * 16.1f, new Vector3(0.2f, 1.2f, 0.2f), rot, Wood, Wood);              // 钟锤杆

        for (int sx = -1; sx <= 1; sx += 2)                                                       // 楼板 → 屋顶的短柱
            for (int sz = -1; sz <= 1; sz += 2)
                Box(b + rot * new Vector3(sx * 2.4f, 17.5f, sz * 2.4f), new Vector3(0.34f, 2.8f, 0.34f), rot,
                    Vermilion, VermilionDark);

        ChineseRoof(b + Vector3.up * 18.9f, 8.8f, 8.8f, 2.9f, 0.75f, rot, Tile, Ridge);
        Ball(b + Vector3.up * 21.8f, 0.55f, Gold, 8, 1f, 0f);                                      // 宝顶
    }

    /// <summary>灯塔：八角石台 + 逐段收分的塔身（白墙朱红腰线）+ 观景平台 + 灯笼室 + 顶灯</summary>
    static void HarborLighthouse(Vector3 b, float yaw)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        Cyl(b, 1.3f, 4.4f, 4.0f, 8, Limestone, true);

        float y = 1.3f;
        for (int i = 0; i < 6; i++)
        {
            float r = 3.1f - i * 0.26f;
            Cyl(b + Vector3.up * y, 2.2f, r, r - 0.2f, 8, i % 2 == 0 ? WallWhite : Vermilion);
            y += 2.2f;
        }

        Cyl(b + Vector3.up * y, 0.45f, 2.6f, 2.45f, 8, Limestone, true);                          // 平台
        for (int i = 0; i < 8; i++)                                                               // 平台栏杆
        {
            float a = i * Mathf.PI * 0.25f;
            Box(b + new Vector3(Mathf.Sin(a) * 2.1f, y + 0.85f, Mathf.Cos(a) * 2.1f),
                new Vector3(0.16f, 0.8f, 0.16f), rot, LimestoneDark, LimestoneDark);
        }

        Box(b + Vector3.up * (y + 1.9f), new Vector3(3.0f, 2.6f, 3.0f), rot, Vermilion, VermilionDark);   // 灯笼室
        for (int i = 0; i < 4; i++)                                                                       // 四面的窗
            Box(b + rot * new Vector3(0f, y + 1.9f, 1.55f), new Vector3(1.9f, 1.5f, 0.12f),
                Quaternion.Euler(0f, yaw + i * 90f, 0f), Gold, Gold);

        ChineseRoof(b + Vector3.up * (y + 3.3f), 4.6f, 4.6f, 1.5f, 0.45f, rot, Tile, Ridge);
        Ball(b + Vector3.up * (y + 5.4f), 1.0f, Gold, 8, 1f, 0f);                                  // 顶灯
        Flames.Add(b + Vector3.up * (y + 5.0f));                                                   // 火苗（夜里就是港口的灯）
    }

    // ================= 上城背后的台阶 =================

    static void HarborInland()
    {
        var rot = HarborRot();

        // 上城背后：三级内收的挡土墙（和临海侧同一套做法），把 21.7 米的台地落回 13 米的地面。
        // ★ 三级踏步必须是"最靠台面那一级最高、往内陆逐级降低" —— 反过来的话最内陆那一级会顶到
        //   台面高度、中间夹出一条沟，看着就是台阶搭反了（原来写的就是反的）
        // ★ 每条墙还要让开三条落地石阶的走廊，否则石阶会从墙里穿过去
        float yBottom = LiyueHarbor.QuayY;
        float sh = (Deck3 - yBottom) / 3f;

        // 墙分段（沿街 -75 ~ 75；三条走廊在 -60 / 0 / 60，各让开 5 米）
        float[] segLo = { -75f, -55f, 5f, 65f };
        float[] segHi = { -65f, -5f, 55f, 75f };

        for (int i = 0; i < 3; i++)
        {
            float sea = Sea3A - 0.7f - i * 1.2f;
            float top = Deck3 - sh * i;                      // 第 0 级顶到台面，往内陆逐级降
            float yc = top - sh * 0.5f;

            for (int j = 0; j < segLo.Length; j++)
            {
                float aC = (segLo[j] + segHi[j]) * 0.5f;
                float aLen = segHi[j] - segLo[j];

                PropBox("挡土墙", P(aC, sea, yc - sh * 0.5f), aLen * 0.5f, 0.8f, sh);
                Box(P(aC, sea, yc), new Vector3(aLen, sh, 1.6f), rot, Limestone, LimestoneDark);
            }
        }

        // 三条石阶把台地接到地面（中轴 + 两侧各一条）：一级 0.4 米，走得上去 ——
        // 挡土墙本身是爬不上去的，玩家从内陆进城要走这三条
        for (int i = -1; i <= 1; i++)
            HarborStairs(i * 60f, Sea3A - 0.5f, Sea3A - 15f, Deck3, LiyueHarbor.QuayY);

        // 台面内侧沿一排石灯。★ 石灯要写在台面**里面**（Sea3A + 1.6）——
        //   原来写在台面外（Sea3A - 1.6）等于吊在 8.7 米高的挡土墙上方的空气里
        for (int i = -2; i <= 2; i++)
            HarborStoneLamp2(i * 34f, Sea3A + 1.6f, Deck3, flame: i == 0);
    }

    /// <summary>台面内侧的石灯：占位表登记过才摆（挤不下就干脆不摆）</summary>
    static void HarborStoneLamp2(float along, float sea, float y, bool flame)
    {
        var at = P(along, sea, y);
        if (!Prop("石灯", at, 0.9f, 2.7f)) return;
        HarborStoneLamp(at, HarborYaw() + 180f, flame);
    }

    enum HarborPremise { Warehouse, Shop, Teahouse, Residence, Guild }

    // Purpose-built silhouettes share construction details, rather than sharing one house mesh.
    static void HarborPremiseAt(float along, float sea, float y, float yaw, HarborPremise type, int seed)
    {
        bool warehouse = type == HarborPremise.Warehouse;
        bool tea = type == HarborPremise.Teahouse;
        bool guild = type == HarborPremise.Guild;
        bool home = type == HarborPremise.Residence;
        float w = warehouse ? 11f : guild ? 12f : tea ? 10f : home ? 7.8f : 9f;
        float d = warehouse ? 6.6f : tea ? 7f : 6f;
        float h = warehouse ? 3.8f : home ? 5.5f : guild ? 6.7f : tea ? 6f : 4.6f;
        var at = P(along, sea, y);
        // Bounds include the veranda, roof and all facade goods, even for side-facing lots.
        float radius = Mathf.Sqrt((w + 2) * (w + 2) + (d + 3) * (d + 3)) * .5f;
        string name = warehouse ? "港务货栈" : tea ? "临海茶楼" : guild ? "商会会馆" : home ? "庭院住宅" : "沿街商铺";
        float relativeYaw = (yaw - HarborYaw()) * Mathf.Deg2Rad;
        float ca = Mathf.Abs(Mathf.Cos(relativeYaw)), sa = Mathf.Abs(Mathf.Sin(relativeYaw));
        float ra = ca * (w * .5f + 1f) + sa * (d * .5f + 1.8f);
        float rs = sa * (w * .5f + 1f) + ca * (d * .5f + 1.8f);
        if (!PropBox(name, at, ra, rs, h + 3.4f, AnchorDeck)) return;
        var rot = Quaternion.Euler(0, yaw, 0);
        Vector3 Q(float x, float height, float z) => at + rot * new Vector3(x, height, z);
        void Block(float x, float height, float z, float sx, float sy, float sz, Color color)
            => Box(Q(x, height, z), new Vector3(sx, sy, sz), rot, color, color * .88f);

        Color plaster = home ? WallWhite : guild ? new Color(.79f, .66f, .48f) : new Color(.86f, .78f, .61f);
        Color timber = warehouse ? Wood : new Color(.38f, .18f, .12f);
        Color roof = warehouse ? Tile : new Color(.18f, .32f, .33f);
        Block(0, .18f, 0, w + .55f, .36f, d + .55f, LimestoneDark);
        Block(0, h * .5f + .36f, 0, w, h, d, plaster);
        // The ridge runs along the street frontage, with shallow eaves instead of a towering wedge.
        ChineseRoof(Q(0, h + .36f, 0), d + 1.8f, w + 1.8f, d * .28f, .23f,
            rot * Quaternion.Euler(0, 90, 0), roof, Ridge);
        for (int i = -1; i <= 1; i++)
        {
            Block(i * (w * .5f - .12f), h * .5f + .36f, d * .5f + .03f, .22f, h, .24f, timber);
            if (i != 0) Block(i * (w * .5f - .12f), h * .5f + .36f, -d * .5f, .22f, h, .24f, timber);
        }
        Block(0, .68f, d * .5f + .08f, w, .35f, .16f, timber);
        Block(0, 3.05f, d * .5f + .08f, w, .22f, .22f, timber);
        Block(0, 1.55f, d * .5f + .09f, warehouse ? 3.4f : 1.45f, 2.35f, .16f, Wood);
        Block(0, 1.55f, d * .5f + .19f, .06f, 2.25f, .05f, Gold);
        for (int side = -1; side <= 1; side += 2)
        {
            Window(side * w * .3f, 1.95f, warehouse ? .9f : 1.6f, 1.35f);
            if (h > 5) Window(side * w * .3f, 4.55f, 1.45f, 1.45f);
        }
        void Window(float x, float height, float ww, float wh)
        {
            Block(x, height, d * .5f + .12f, ww, wh, .12f, new Color(.22f, .27f, .25f));
            for (int bar = -1; bar <= 1; bar++)
            {
                Block(x + bar * ww * .34f, height, d * .5f + .2f, .065f, wh, .07f, WoodLight);
                Block(x, height + bar * wh * .34f, d * .5f + .2f, ww, .065f, .07f, WoodLight);
            }
        }

        if (!warehouse && !home)
        {
            // A supported street canopy and a signboard make the entrance legible.
            ChineseRoof(Q(0, 2.85f, d * .5f + .5f), 2f, w + .5f, .5f, .1f,
                rot * Quaternion.Euler(0, 90, 0), roof, Ridge);
            for (int side = -1; side <= 1; side += 2)
            {
                Cyl(Q(side * (w * .5f - .45f), 0, d * .5f + .85f), 2.9f, .12f, .12f, 8, timber, true);
                Lantern(Q(side * w * .33f, 2.85f, d * .5f + .8f), .26f, LanternRed, Gold);
            }
            Block(0, 2.6f, d * .5f + .26f, 2.3f, .44f, .12f, Wood);
            Block(0, 2.6f, d * .5f + .34f, 1.95f, .06f, .035f, Gold);
        }
        if (tea || guild)
        {
            Block(0, 3.45f, d * .5f + .45f, w - .4f, .18f, 1.1f, WoodLight);
            for (int post = -3; post <= 3; post++)
                Block(post * (w - 1) / 6, 3.98f, d * .5f + .95f, .09f, 1f, .09f, timber);
            Block(0, 4.48f, d * .5f + .95f, w - .4f, .12f, .14f, WoodLight);
            if (guild)
                ChineseRoof(Q(0, h + 1.15f, 0), 3.8f, 5.4f, 1.05f, .16f,
                    rot * Quaternion.Euler(0, 90, 0), roof, Ridge);
        }
        if (warehouse)
        {
            for (int i = 0; i < 3; i++)
            {
                float x = -w * .5f + 1.1f + i * 1.25f;
                Block(x, .65f, d * .5f + .8f, 1f, 1.3f, .9f, WoodLight);
                Block(x, .65f, d * .5f + 1.27f, .12f, 1.3f, .05f, Wood);
                Block(x, 1.05f, d * .5f + 1.27f, 1f, .08f, .05f, Wood);
            }
        }
        if (home)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Cyl(Q(side * (w * .5f - .45f), 0, d * .5f + .65f), .55f, .32f, .43f, 8, LimestoneDark, true);
                Ball(Q(side * (w * .5f - .45f), .95f, d * .5f + .65f), .6f, Bamboo, 7, .8f, seed);
            }
        }
        Houses.Add(new Vector4(at.x, at.z, radius, seed));
    }

    // ================= 小零件 =================

    /// <summary>石灯：须弥座 + 石柱 + 灯室 + 小屋顶（中式路灯）</summary>
    static void HarborStoneLamp(Vector3 pos, float yaw, bool flame = false)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        Box(pos + Vector3.up * 0.2f, new Vector3(1.1f, 0.4f, 1.1f), rot, Limestone, LimestoneDark);
        Cyl(pos + Vector3.up * 0.4f, 1.5f, 0.26f, 0.22f, 6, Limestone, true);
        Box(pos + Vector3.up * 2.0f, new Vector3(0.9f, 0.6f, 0.9f), rot, Gold, Gold);
        ChineseRoof(pos + Vector3.up * 2.3f, 1.8f, 1.8f, 0.6f, 0.22f, rot, Tile, Ridge);

        if (flame) Flames.Add(pos + Vector3.up * 2.5f);
    }

    /// <summary>街边摊子：木台面 + 两根竹柱 + 中式顶 + 一盏灯</summary>
    static void HarborStall(Vector3 pos, float yaw)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        Box(pos + Vector3.up * .85f, new Vector3(3.2f, .16f, 1.8f), rot, WoodLight, Wood);
        for (int x = -1; x <= 1; x += 2)
        for (int z = -1; z <= 1; z += 2)
            Cyl(pos + rot * new Vector3(x * 1.4f, 0, z * .7f), 2.4f, .08f, .08f, 6, Wood);
        ThickQuad(pos + rot * new Vector3(-1.7f, 2.5f, -1), pos + rot * new Vector3(-1.7f, 2.25f, 1.1f),
            pos + rot * new Vector3(1.7f, 2.25f, 1.1f), pos + rot * new Vector3(1.7f, 2.5f, -1), .05f, Cloth, WoodLight);
        for (int i = -1; i <= 1; i++)
        {
            Box(pos + rot * new Vector3(i * .88f, 1.05f, 0), new Vector3(.72f, .24f, 1.1f), rot, Wood, Wood);
            for (int j = 0; j < 3; j++)
                Ball(pos + rot * new Vector3(i * .88f + (j - 1) * .18f, 1.25f, .15f), .13f,
                    i == 0 ? new Color(.84f, .53f, .12f) : new Color(.62f, .23f, .12f), 6);
        }
        Lantern(pos + rot * new Vector3(1.2f, 2.4f, .7f), .25f, LanternRed, Gold);
    }

    /// <summary>旗子：木杆 + 一面布 + 杆顶金球（码头和广场都是红布，风一吹就是港口的样子）</summary>
    static void HarborFlag(Vector3 pos, float yaw, Color cloth)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);

        Cyl(pos, 6.0f, 0.14f, 0.11f, 6, Wood);
        Box(pos + rot * new Vector3(0.95f, 5.0f, 0f), new Vector3(1.8f, 1.5f, 0.08f), rot, cloth, cloth);
        Box(pos + rot * new Vector3(1.85f, 5.9f, 0f), new Vector3(0.12f, 0.12f, 0.12f), rot, Gold, Gold);
    }

    /// <summary>两点之间挂一排红灯笼（灯笼零件自带吊线，所以给的是吊点）。
    /// 灯笼是吊在半空的，占位表里按 AnchorWater 登记 —— 只参与"别和别的东西压到一起"，
    /// 不参与"落没落地"（它本来就不落地）</summary>
    static void HarborLanterns(Vector3 from, Vector3 to, int count)
    {
        // 先把两端的吊绳拉出来：灯笼零件自带一小截吊线，但整串没有绳子的话，
        // 一排灯笼就是"凭空吊在半空"（自检看不出这种事，只有看画面才知道）
        Vector3 dir = to - from;
        float len = dir.magnitude;
        if (len > 0.1f)
            Box((from + to) * 0.5f, new Vector3(0.07f, 0.07f, len), Quaternion.LookRotation(dir.normalized, Vector3.up),
                Wood, Wood);

        for (int i = 0; i < count; i++)
        {
            float t = (i + 0.5f) / count;
            var top = Vector3.Lerp(from, to, t);
            if (!Prop("灯笼串", top + Vector3.down * 1.3f, 0.5f, 1.3f, AnchorWater)) continue;
            Lantern(top, 0.32f, LanternRed, Gold);
        }
    }

    // ================= 给自检用的几个只读口子 =================

    /// <summary>岸线到城心的距离（沿街 along 处）—— WildSiteCheck 报告用</summary>
    public static float HarborShoreAt(float along) => HarborShore(along);

    /// <summary>城里的某个点（沿街 / 向海 / 高度）—— 传送锚点这类"贴着港城摆东西"用得上</summary>
    public static Vector3 HarborPoint(float along, float sea, float y = 0f) => P(along, sea, y);

    /// <summary>岩神像（七天神像 · 岩）在广场中心的落点。它的几何由 TeleportAnchor 在运行时生成，
    /// 所以这里给它一个"查得到位置"的口子（落点高度 = 广场铺地顶面）</summary>
    public static Vector3 StatuePoint() => P(PlazaU, PlazaSea, Deck3 + 0.58f);

    /// <summary>这一点脚下是不是港城的可踩台面（石台 / 铺地）：是的话返回它的顶面高度。
    /// 港城的台面是一块块人工砌起来的石板，**比地形高** —— 站在上面不算浮空。
    /// 传送锚点 / 其它"贴着港城摆东西"的都要按这个判，不能只看地形高度。
    /// 调用时机：台面表在拼港城几何（AppendHarbor）时登记，所以运行时 Init 顺序、
    /// 以及编辑器自检里，这一步都必须排在港城那一步之后。</summary>
    public static bool OnHarborDeck(float x, float z, out float top)
    {
        top = 0f;
        if (HarborFloors.Count == 0) return false;

        HarborLocal(new Vector3(x, 0f, z), out float a, out float s);

        bool found = false;
        for (int i = 0; i < HarborFloors.Count; i++)
        {
            var f = HarborFloors[i];
            if (Mathf.Abs(a - f.a) > f.ra || Mathf.Abs(s - f.s) > f.rs) continue;
            if (found && f.y0 <= top) continue;

            found = true;
            top = f.y0;
        }

        return found;
    }

    /// <summary>主码头台面的高度（= 下街那一层）。站在码头上摆东西要用它，不能用地形高度 ——
    /// 码头是砌在台地上的石台，比地形高 0.8 米，按地形高度摆会埋进石板里</summary>
    public static float HarborWharfTopY() => Deck0;

    /// <summary>主码头石台在沿街 along 处的**向海中心线**：石台宽 22 米，从岸线往里 2.5 米砌起
    /// （HarborWharf 里的 seaC = 岸线 - 2.5 - 11）。要在码头上摆东西（传送锚点 / 灯笼柱）
    /// 就用这个，别自己猜距离 —— 猜大了会落到台面外面、悬在台地斜坡上方</summary>
    public static float HarborWharfSea(float along) => HarborShore(along) - 13.5f;

    /// <summary>四层台面的高度（下街 → 上城）—— WildSiteCheck 报告用</summary>
    public static Vector4 HarborDeckHeights() => new Vector4(Deck0, Deck1, Deck2, Deck3);

    /// <summary>第 lv 层台地的（内陆侧 sea、临海侧 sea、沿街长度）。
    /// ★ 只有这一处定义四层的范围：HarborTerrace 的调用、HarborWorstGround 的自检、
    ///   大地图的街线都用它 —— 改一层的高度或进深只要动这里</summary>
    static void DeckRange(int lv, out float seaA, out float seaB, out float len)
    {
        switch (lv)
        {
            case 0:  seaA = Sea0A; seaB = Sea0B; len = 118f; break;
            case 1:  seaA = Sea1A; seaB = Sea1B; len = 116f; break;
            case 2:  seaA = Sea2A; seaB = Sea2B; len = 120f; break;
            default: seaA = Sea3A; seaB = Sea3B; len = 172f; break;
        }
    }

    /// <summary>四层台面脚下最低的那处地面高度（顺带报出在哪）。
    /// 台面必须建在岸上：低于水面说明那块台地是浮在水上的（阶段 E 第一版就踩了这个坑，
    /// 因为外湾 130 米的过渡带把浅水啃到了城心附近）。</summary>
    public static float HarborWorstGround(out string where)
    {
        float worst = 9999f;
        where = "";

        for (int lv = 0; lv < 4; lv++)
        {
            DeckRange(lv, out float seaA, out float seaB, out float len);

            for (int i = -1; i <= 1; i++)
            {
                float u = i * len * 0.5f;                 // 取台面四角（最外沿那一条）
                for (int j = 0; j < 2; j++)
                {
                    float sea = j == 0 ? seaA : seaB;
                    var p = P(u, sea, 0f);
                    float h = OpenWorld.Height(p.x, p.z);

                    if (h < worst)
                    {
                        worst = h;
                        where = $"第 {lv + 1} 层（沿街 {u:0}、向海 {sea:0}）";
                    }
                }
            }
        }
        return worst;
    }

    /// <summary>
    /// 港城布局自检（WildSiteCheck 里那份报告的正文）。查三件事：
    ///   ① 零件之间有没有压到一起 —— 平面（沿街 / 向海）和高度都压上才算，吊在半空的灯笼串不会误判；
    ///   ② 零件有没有悬空 / 埋进台面 —— 站在人造台面上的，台面顶必须在落点附近；
    ///      站在自然地形上的，地形高度必须在落点附近；
    ///   ③ 台面有没有浮在水上 —— 四角 + 中心落在水面以下，就说明那块台子是悬在水面上的。
    ///
    /// 摆放时因为位置已被占而让位的零件（"让位"那几行）不算异常，但**地标**出现在名单里
    /// 就说明它那块地被小件抢了，要挪的是小件。
    /// </summary>
    public static string HarborLayoutReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"零件 {HarborProps.Count} / {_propTotal} 件，台面 {HarborFloors.Count} 块，" +
                      $"因位置已被占而让位 {_culled.Count} 件");
        for (int i = 0; i < Mathf.Min(16, _culled.Count); i++) sb.AppendLine($"  · 让位：{_culled[i]}");
        if (_culled.Count > 16) sb.AppendLine($"  · …另有 {_culled.Count - 16} 件");

        // ① 重叠：登记时已经挡掉了，这里再逐对复查一遍 —— 自检的意义就是不依赖登记那一步
        int overlaps = 0;
        var overlapText = new List<string>();

        for (int i = 0; i < HarborProps.Count; i++)
            for (int j = i + 1; j < HarborProps.Count; j++)
            {
                if (!SpotHit(HarborProps[i], HarborProps[j])) continue;
                overlaps++;
                if (overlapText.Count < 6)
                    overlapText.Add($"{HarborProps[i].name}({HarborProps[i].a:0.#},{HarborProps[i].s:0.#}) ↔ " +
                                    $"{HarborProps[j].name}({HarborProps[j].a:0.#},{HarborProps[j].s:0.#})");
            }

        for (int i = 0; i < overlapText.Count; i++) sb.AppendLine($"  · 重叠：{overlapText[i]}");

        // ② 悬空 / 埋进台面
        int floating = 0;
        var floatText = new List<string>();

        foreach (var p in HarborProps)
        {
            if (p.anchor == AnchorWater) continue;                     // 水上 / 吊在半空的：不判落地

            if (p.anchor == AnchorGround)
            {
                float h = OpenWorld.Height(p.x, p.z);
                if (Mathf.Abs(h - p.y0) <= 0.6f) continue;
                floating++;
                if (floatText.Count < 6) floatText.Add($"{p.name} 落点 {p.y0:0.0} 米 / 地面 {h:0.0} 米");
                continue;
            }

            // 站在人造台面上的：找脚下那块台面，台面顶要和落点基本齐平
            bool on = false;
            float gap = 0f;
            foreach (var f in HarborFloors)
            {
                if (Mathf.Abs(p.a - f.a) > f.ra + p.ra || Mathf.Abs(p.s - f.s) > f.rs + p.rs) continue;
                float d = p.y0 - f.y0;
                if (!on || Mathf.Abs(d) < Mathf.Abs(gap)) { on = true; gap = d; }
            }

            if (on && gap <= 0.25f && gap >= -1.5f) continue;
            floating++;
            if (floatText.Count < 6)
                floatText.Add(on ? $"{p.name}({p.a:0.#},{p.s:0.#}) 离台面 {gap:+0.00;-0.00} 米"
                                 : $"{p.name}({p.a:0.#},{p.s:0.#}) 脚下没有台面");
        }

        for (int i = 0; i < floatText.Count; i++) sb.AppendLine($"  · 悬空：{floatText[i]}");

        // ③ 台面浮水
        int wet = 0;
        var wetText = new List<string>();

        foreach (var f in HarborFloors)
        {
            for (int i = 0; i < 5; i++)
            {
                float ca = f.a + (i == 0 ? -f.ra : i == 1 ? f.ra : 0f);
                float cs = f.s + (i == 2 ? -f.rs : i == 3 ? f.rs : 0f);
                var w = P(ca, cs, 0f);
                float h = OpenWorld.Height(w.x, w.z);
                if (h > OpenWorld.WaterLevel + 0.3f) continue;

                wet++;
                if (wetText.Count < 6)
                    wetText.Add($"{f.name}({ca:0},{cs:0}) 脚下 {h:0.0} 米（水面 {OpenWorld.WaterLevel:0.0}）");
                break;                                                 // 一块台面报一次就够
            }
        }

        for (int i = 0; i < wetText.Count; i++) sb.AppendLine($"  · 浮水：{wetText[i]}");

        sb.AppendLine($"重叠 {overlaps} 处；悬空 / 埋深 {floating} 件；台面浮在水上 {wet} 块");
        sb.AppendLine(overlaps == 0 && floating == 0 && wet == 0
            ? "璃月港布局自检：通过"
            : "璃月港布局自检：★ 不通过");

        HarborLayoutProblems = overlaps + floating + wet;
        return sb.ToString();
    }

    /// <summary>布局自检的异常数（重叠 + 悬空 / 埋深 + 浮水）—— 给 WildSiteCheck 判通过 / 不通过用</summary>
    public static int HarborLayoutProblems { get; private set; }

    // ================= 大地图标记 =================
    /// <summary>港城的地标标记（运行时由 LiyueSiteSpawner 调一次；几何自检不会调它，免得重复登记）</summary>
    public static void HarborMarkers()
    {
        float yaw = HarborYaw();

        void Mark(string name, byte kind, float along, float sea)
        {
            var p = P(along, sea, 0f);
            MondstadtTown.Markers.Add(new MondstadtTown.MapMarker { name = name, x = p.x, z = p.z, kind = kind });
        }

        Mark("主码头", 43, 0f, 92f);
        Mark("灯塔", 41, 92f, HarborShore(92f) - 9f);
        Mark("楼阁", 31, PagodaU, PagodaSea);
        Mark("钟楼", 42, -62f, -52f);
        Mark("岩神像广场", 37, -22f, -34f);
        Mark("上城", 44, 0f, -70f);

        // 城本身在大地图上是一整块聚落色（石色：白墙黛瓦那种；码头那条另用木色）
        MondstadtTown.Blobs.Add(new MondstadtTown.MapBlob
        {
            x = LiyueHarbor.HarborX, z = LiyueHarbor.HarborZ, r = 132f, color = 1,
        });

        // 四层街道各画一条街线（和蒙德城里那三条街同一套画法）—— 这样地图上的城"有形状"，
        // 不是只有一个色块。★ 这些线也要在 WorldMap 烘底图之前登记（见 VoxelBootstrap 8.5）
        for (int lv = 0; lv < 4; lv++)
        {
            DeckRange(lv, out float seaA, out float seaB, out float len);
            float seaC = (seaA + seaB) * 0.5f;
            var a = P(-len * 0.5f, seaC, 0f);
            var b = P(len * 0.5f, seaC, 0f);

            MondstadtTown.MapLines.Add(new MondstadtTown.MapLine
            {
                x0 = a.x, z0 = a.z, x1 = b.x, z1 = b.z,
                kind = 0,                                   // 0 = 石板路（浅色宽线）
            });
        }

        // 主码头：沿湾的弧线画成一条折线（木色）
        for (int k = -2; k <= 2; k++)
        {
            float u0 = k * 40f - 21f, u1 = k * 40f + 21f;
            var a = P(u0, HarborShore(u0) - 13f, 0f);
            var b = P(u1, HarborShore(u1) - 13f, 0f);

            MondstadtTown.MapLines.Add(new MondstadtTown.MapLine
            {
                x0 = a.x, z0 = a.z, x1 = b.x, z1 = b.z,
                kind = 1,                                   // 1 = 木栈桥（深色细线）
            });
        }
    }
}
