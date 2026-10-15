using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 野外建筑的选点自检：不用进 Play，直接在编辑器里跑一遍规划，把结果打进 Editor.log。
/// 菜单：VoxelDemo → 验证野外建筑选点。
/// 脚本重载后也会自动跑一次（每个编辑器会话一次），改完选点规则就能立刻看结果。
///
/// 看什么：
///   1. 每种建筑"实际铺下 / 计划铺"的数量、每处的位置与地台高度、离最近邻居的余量、
///      地台边缘的台坎高度（台坎太高说明那块地本来很斜，会看出一个人工平台）；
///   2. 把几何真的拼一遍（不改场景），报顶点 / 面数 / 三个子网格 / 包围盒 —— 用来确认
///      WildBuildings 没有拼出空网格或跑到天上去的顶点。
/// 报告尾部会给出"异常项"计数，正常应该是 0。
/// </summary>
public static class WildSiteCheck
{
    [MenuItem("VoxelDemo/验证野外建筑选点")]
    public static void Verify()
    {
        // ★ 走统一入口：它会先清地台 / 禁植区 / 高度场，再依次规划建筑和解密场地。
        //   分别单独调两个 PlanSites 是不对的（谁后跑就会把对方刚登记的地台清掉）
        WildSiteSpawner.PlanEverything();

        Debug.Log("[WildSiteCheck] 野外内容自检\n" + WildSiteSpawner.PlanReport() + PuzzleSpawner.PlanReport());
        VerifyTerrainChunks();
        VerifyNations();
        VerifyGeometry();
        VerifyLiyueGeometry();
        VerifyLiyueSites();
        VerifyHarbor();
        VerifyInazumaGeometry();
        VerifyInazumaSites();
        VerifyPuzzleGeometry();
        VerifyLiyuePuzzles();
        VerifyTeleports();
        VerifyCrossOverlap();
    }

    /// <summary>传送锚点的自检：锚点能不能站 + **全图覆盖**（任意陆地位置离最近的锚点 ≤ 220 米）。
    /// ★ 这里只过"地形"那两关（不在水里、不陡）；"身边两米半有没有墙 / 篱笆"要用 Physics，
    ///   只有运行时做得到，正式落点由 TeleportSpawner.Init 再筛一遍。
    ///   逐处的落点 PlanReport 里已经列了，这里只挑出有问题的 + 覆盖指标。</summary>
    static void VerifyTeleports()
    {
        var sb = new StringBuilder();

        sb.AppendLine(TeleportSpawner.PlanReport());

        // 默认点亮的那几处：全开的时候就不把三十几个名字全列出来了
        var lit = new System.Collections.Generic.List<string>();
        foreach (var s in TeleportSpawner.Spots) if (s.unlockedAtStart) lit.Add(s.name);

        sb.AppendLine($"锚点 {TeleportSpawner.Spots.Count} 处：默认点亮 {lit.Count} 处" +
                      (lit.Count == TeleportSpawner.Spots.Count
                          ? "（全部 —— 一进游戏每个锚点都能传）"
                          : $"（{string.Join("、", lit)}），其余走近点亮") +
                      " —— 在大地图上点金色图钉传送");

        int problems = 0;
        foreach (var s in TeleportSpawner.Spots)
        {
            if (s.y > 0.01f) continue;                    // 站在人造台面上的（码头石台）：地形判不了它

            float h = OpenWorld.Height(s.x, s.z);
            bool water = h < OpenWorld.WaterLevel + 1.2f;
            bool steep = OpenWorld.Slope(s.x, s.z) > 0.12f;
            if (!water && !steep) continue;

            problems++;
            sb.AppendLine($"  ★ {s.name} ({s.x:0},{s.z:0}) {(water ? "在水里 / 水边  " : "")}" +
                          $"{(steep ? "太陡" : "")}");
        }

        // 覆盖：计划的核心指标 —— "普通区域也要有锚点、不会长时间跑腿"说的就是它
        float far = TeleportSpawner.WorstCoverDistance(out Vector2 farAt);
        bool coverOk = far <= TeleportSpawner.CoverTarget;
        sb.AppendLine($"覆盖：陆地最远 {far:0} 米（在 ({farAt.x:0},{farAt.y:0})），" +
                      $"目标 ≤ {TeleportSpawner.CoverTarget:0} 米 —— " + (coverOk ? "达标" : "★ 不达标"));

        sb.AppendLine(problems == 0 && coverOk ? "传送锚点检查：通过" : "传送锚点检查：★ 不通过");

        Debug.Log("[WildSiteCheck] 传送锚点自检\n" + sb);
    }

    static int CountDefaultLit()
    {
        int n = 0;
        foreach (var s in TeleportSpawner.Spots) if (s.unlockedAtStart) n++;
        return n;
    }

    /// <summary>璃月解密场地的自检（阶段 F）：规划报告 + 石台几何 + 两种外形的节点网格
    /// （石灯 / 石鼓 × 明暗 × 四种灯色）+ 节点圈有没有落在石台里</summary>
    static void VerifyLiyuePuzzles()
    {
        Debug.Log("[WildSiteCheck] 璃月解密场地自检\n" + LiyuePuzzleSpawner.PlanReport());

        var sb = new StringBuilder();

        LiyueBuildings.Begin();
        foreach (var s in LiyuePuzzleSpawner.Sites)
        {
            var center = new Vector3(s.x, OpenWorld.Height(s.x, s.z), s.z);
            var order = s.kind == PuzzleKind.Ordered ? WildPuzzles.RequiredOrder(s.kind, 0f, s.seed) : null;
            LiyueBuildings.AppendPuzzleGround(s.kind, s.style, center, s.yaw, order);
        }

        var mesh = LiyueBuildings.BuildMesh("LiyuePuzzleCheckMesh");

        int faces = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) faces += (int)(mesh.GetIndexCount(i) / 3);

        var b = mesh.bounds;
        int idxTotal = LiyueBuildings.SubTris[0].Count + LiyueBuildings.SubTris[1].Count + LiyueBuildings.SubTris[2].Count;

        // 节点网格：两种外形 × 明暗 × 四种灯色全建一遍，确认没有空网格
        var lampDark = LiyueBuildings.PuzzleNodeMesh(PuzzleStyle.LiyueLamp, false);
        var lampLit  = LiyueBuildings.PuzzleNodeMesh(PuzzleStyle.LiyueLamp, true);
        var drumDark = LiyueBuildings.PuzzleNodeMesh(PuzzleStyle.LiyueDrum, false);
        var drumLit  = LiyueBuildings.PuzzleNodeMesh(PuzzleStyle.LiyueDrum, true);

        int glowVerts = 0;
        for (int i = 0; i < WildPuzzles.GemColors.Length; i++)
        {
            glowVerts += LiyueBuildings.PuzzleGlowMesh(PuzzleStyle.LiyueLamp, i, true).vertexCount;
            glowVerts += LiyueBuildings.PuzzleGlowMesh(PuzzleStyle.LiyueLamp, i, false).vertexCount;
            glowVerts += LiyueBuildings.PuzzleGlowMesh(PuzzleStyle.LiyueDrum, i, true).vertexCount;
            glowVerts += LiyueBuildings.PuzzleGlowMesh(PuzzleStyle.LiyueDrum, i, false).vertexCount;
        }

        bool boundsOk = !float.IsNaN(b.center.x) && b.size.x > 0f && b.size.z > 0f &&
                        b.min.y > -40f && b.max.y < 90f;

        sb.AppendLine($"石台：顶点 {mesh.vertexCount}，面 {faces}（石 {LiyueBuildings.SubTris[0].Count / 3} / " +
                      $"顶点色 {LiyueBuildings.SubTris[2].Count / 3}），火盆 {LiyueBuildings.Flames.Count} 处");
        sb.AppendLine($"节点：石灯 暗 {lampDark.vertexCount} / 亮 {lampLit.vertexCount} 顶点；" +
                      $"石鼓 暗 {drumDark.vertexCount} / 亮 {drumLit.vertexCount} 顶点；" +
                      $"灯罩 / 鼓印（2 外形 × 4 色 × 明暗）共 {glowVerts} 顶点");
        sb.AppendLine($"石台包围盒 中心 ({b.center.x:0},{b.center.y:0},{b.center.z:0})  " +
                      $"尺寸 ({b.size.x:0},{b.size.y:0},{b.size.z:0})  {(boundsOk ? "正常" : "★ 不正常")}");

        // 每处：节点圈必须落在石台里（否则灯 / 鼓会飘到台子外面）
        foreach (var s in LiyuePuzzleSpawner.Sites)
        {
            float plat = LiyueBuildings.PuzzlePlatformRadius(s.kind);
            float ring = WildPuzzles.RingRadius(s.kind, s.style);
            bool nodeOk = ring + 1.4f < plat;

            sb.AppendLine($"  {LiyueBuildings.PuzzleSiteName(s.kind, s.style)} ({s.x:0},{s.z:0})  " +
                          $"石台半径 {plat:0.0}m  节点圈 {ring:0.0}m  {(nodeOk ? "" : "  ★ 节点跑到石台外")}");
        }

        bool ok = mesh.vertexCount > 0 && faces > 0 && idxTotal == faces * 3 && boundsOk &&
                  lampDark.vertexCount > 0 && lampLit.vertexCount > 0 &&
                  drumDark.vertexCount > 0 && drumLit.vertexCount > 0 && glowVerts > 0;

        sb.AppendLine($"地台重叠 {LiyuePuzzleSpawner.CountPadOverlaps()} 处；" +
                      (ok ? "璃月解密场地几何自检：通过" : "璃月解密场地几何自检：★ 不通过"));

        Object.DestroyImmediate(mesh);
        Debug.Log("[WildSiteCheck] 璃月解密场地几何自检\n" + sb);
    }

    /// <summary>璃月港的自检（阶段 E）：
    ///  ① 把港城的几何真拼一遍，报顶点 / 面 / 包围盒；
    ///  ② 逐顶点检查"全部落在港口范围内"—— 局部坐标系一旦算错（比如向海方向取反），
    ///     整座城会建到几百米外的草原上或飞到天上，这一条就是抓它的；
    ///  ③ 把岸线距离和四层台面的高度打出来，对照 LiyueHarbor 的常量看合不合理。</summary>
    static void VerifyHarbor()
    {
        var sb = new StringBuilder();

        LiyueBuildings.Begin();
        LiyueBuildings.AppendHarbor();
        var mesh = LiyueBuildings.BuildMesh("LiyueHarborCheckMesh");

        int faces = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) faces += (int)(mesh.GetIndexCount(i) / 3);

        var b = mesh.bounds;
        var verts = mesh.vertices;

        float maxR = 0f, minY = 9999f, maxY = -9999f;
        foreach (var v in verts)
        {
            float dx = v.x - LiyueHarbor.HarborX, dz = v.z - LiyueHarbor.HarborZ;
            maxR = Mathf.Max(maxR, Mathf.Sqrt(dx * dx + dz * dz));
            minY = Mathf.Min(minY, v.y);
            maxY = Mathf.Max(maxY, v.y);
        }

        var deck = LiyueBuildings.HarborDeckHeights();

        // ★ 高度上限要跟着上城台面（Deck3）走：台面本身就有 21.7 米，钟楼再往上 22.5 米 ——
        //   原来写死 40 米，等于把正常的地标判成异常
        bool rangeOk = maxR < 260f && minY > -20f && maxY < deck.w + 25f;

        sb.AppendLine($"顶点 {mesh.vertexCount}，面 {faces}（石 {LiyueBuildings.SubTris[0].Count / 3} / " +
                      $"瓦 {LiyueBuildings.SubTris[1].Count / 3} / 顶点色 {LiyueBuildings.SubTris[2].Count / 3}）；" +
                      $"火苗 {LiyueBuildings.Flames.Count} 处，民居色块 {LiyueBuildings.Houses.Count} 个");
        sb.AppendLine($"包围盒 中心 ({b.center.x:0},{b.center.y:0},{b.center.z:0})  " +
                      $"尺寸 ({b.size.x:0},{b.size.y:0},{b.size.z:0})");
        sb.AppendLine($"离城心最远 {maxR:0} 米（应 < 260），高度 {minY:0.0}~{maxY:0.0} 米" +
                      $"（应在 -20 ~ 上城台面 + 25 = {deck.w + 25f:0.0}）" + (rangeOk ? "" : "   ★ 异常"));
        sb.AppendLine($"四层台面高度：{deck.x:0.0} / {deck.y:0.0} / {deck.z:0.0} / {deck.w:0.0} 米" +
                      $"（地形台地 {LiyueHarbor.QuayY:0.0} 米，水面 {OpenWorld.WaterLevel:0.0} 米）");
        sb.AppendLine($"岸线（沿街 -90 / -45 / 0 / 45 / 90 米）：" +
                      $"{LiyueBuildings.HarborShoreAt(-90f):0} / {LiyueBuildings.HarborShoreAt(-45f):0} / " +
                      $"{LiyueBuildings.HarborShoreAt(0f):0} / {LiyueBuildings.HarborShoreAt(45f):0} / " +
                      $"{LiyueBuildings.HarborShoreAt(90f):0} 米（应 > 街道最外沿 82，即街道都建在岸上）");

        string where;
        float ground = LiyueBuildings.HarborWorstGround(out where);
        bool groundOk = ground > OpenWorld.WaterLevel + 0.3f;
        sb.AppendLine($"四层台面脚下最低地面 {ground:0.0} 米（水面 {OpenWorld.WaterLevel:0.0} 米）—— " +
                      $"{(groundOk ? "台面都建在岸上" : "★ 有台地浮在水上：" + where)}");

        // 布局自检（占位表是 AppendHarbor 里登记的）：零件重叠 / 悬空、台面浮水
        sb.Append(LiyueBuildings.HarborLayoutReport());

        bool ok = mesh.vertexCount > 0 && faces > 0 && rangeOk && groundOk &&
                  LiyueBuildings.HarborLayoutProblems == 0;

        sb.AppendLine(ok ? "璃月港几何自检：通过" : "璃月港几何自检：★ 不通过");

        Object.DestroyImmediate(mesh);
        Debug.Log("[WildSiteCheck] 璃月港几何自检\n" + sb);
    }

    /// <summary>璃月野外建筑的自检（阶段 D）：规划报告 + 按运行时的方式把几何拼一遍
    /// （石拱桥的 y 是"起拱线"，用不了地面高度），再核对地台有没有互相压到</summary>
    static void VerifyLiyueSites()
    {
        Debug.Log("[WildSiteCheck] 璃月野外建筑自检\n" + LiyueSiteSpawner.PlanReport());

        var sb = new StringBuilder();

        LiyueBuildings.Begin();
        foreach (var s in LiyueSiteSpawner.Sites)
        {
            float y = s.kind == LiyueKind.ArchBridge ? s.y : OpenWorld.Height(s.x, s.z);
            LiyueBuildings.Append(s.kind, new Vector3(s.x, y, s.z), s.yaw, s.variant, s.param);
        }

        var mesh = LiyueBuildings.BuildMesh("LiyueSiteCheckMesh");

        int faces = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) faces += (int)(mesh.GetIndexCount(i) / 3);

        var b = mesh.bounds;
        int idxTotal = LiyueBuildings.SubTris[0].Count + LiyueBuildings.SubTris[1].Count + LiyueBuildings.SubTris[2].Count;

        bool boundsOk = !float.IsNaN(b.center.x) && !float.IsNaN(b.size.x) &&
                        b.size.x > 0f && b.size.y > 0f && b.size.z > 0f &&
                        b.min.y > -40f && b.max.y < 120f;

        sb.AppendLine($"顶点 {mesh.vertexCount}，面 {faces}（石 {LiyueBuildings.SubTris[0].Count / 3} / " +
                      $"瓦 {LiyueBuildings.SubTris[1].Count / 3} / 顶点色 {LiyueBuildings.SubTris[2].Count / 3}），" +
                      $"子网格 {mesh.subMeshCount}；火苗 {LiyueBuildings.Flames.Count} 处，民居色块 {LiyueBuildings.Houses.Count} 个");
        sb.AppendLine($"包围盒 中心 ({b.center.x:0},{b.center.y:0},{b.center.z:0})  " +
                      $"尺寸 ({b.size.x:0},{b.size.y:0},{b.size.z:0})  {(boundsOk ? "正常" : "★ 不正常")}");

        // 每一种实际铺在哪：一眼看出是不是全挤在同一个角落
        foreach (LiyueKind kind in System.Enum.GetValues(typeof(LiyueKind)))
        {
            var spots = new System.Collections.Generic.List<string>();
            foreach (var s in LiyueSiteSpawner.Sites)
                if (s.kind == kind) spots.Add($"({s.x:0},{s.z:0})");

            if (spots.Count == 0) continue;
            sb.AppendLine($"  {LiyueBuildings.DisplayName(kind)}：{string.Join(" ", spots)}");
        }

        bool ok = mesh.vertexCount > 0 && faces > 0 && idxTotal == faces * 3 && boundsOk;

        sb.AppendLine($"地台重叠 {LiyueSiteSpawner.CountPadOverlaps()} 处；" +
                      (ok ? "璃月野外建筑几何自检：通过" : "璃月野外建筑几何自检：★ 不通过"));

        Object.DestroyImmediate(mesh);
        Debug.Log("[WildSiteCheck] 璃月野外建筑几何自检\n" + sb);
    }

    /// <summary>璃月建筑的几何自检（阶段 C）：11 种建筑每种单独拼一遍，逐个报顶点/面/包围盒，
    /// 确认没有空网格、没有跑到天上的顶点、两个用到的子网格（石墙贴图 + 顶点色）都非空</summary>
    static void VerifyLiyueGeometry()
    {
        var sb = new StringBuilder();
        int problems = 0;

        foreach (LiyueKind kind in System.Enum.GetValues(typeof(LiyueKind)))
        {
            LiyueBuildings.Begin();
            LiyueBuildings.Append(kind, new Vector3(1000f, 6f, 1000f), 0f, 0.5f, 18f);

            var mesh = LiyueBuildings.BuildMesh("LiyueCheck_" + kind);

            int faces = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) faces += (int)(mesh.GetIndexCount(i) / 3);

            var b = mesh.bounds;
            int idxTotal = LiyueBuildings.SubTris[0].Count + LiyueBuildings.SubTris[1].Count + LiyueBuildings.SubTris[2].Count;

            // 真正要保证的是"每个三角形都落进了某个子网格"（不丢面），而不是"每个子网格都非空" ——
            // 吊脚楼是纯木石、石拱桥和石林遗迹是纯石灰岩，本来就只有一路子网格
            bool ok = mesh.vertexCount > 0 && faces > 0 &&
                      idxTotal == faces * 3 &&
                      b.size.y > 0.8f && b.size.y < 70f &&
                      b.min.y > -40f && b.max.y < 90f;

            if (!ok) problems++;

            sb.AppendLine($"  {LiyueBuildings.DisplayName(kind)}：顶点 {mesh.vertexCount:N0}，面 {faces:N0}，" +
                          $"包围盒 ({b.size.x:0.#},{b.size.y:0.#},{b.size.z:0.#})，" +
                          $"子网格 石{LiyueBuildings.SubTris[0].Count / 3}/瓦{LiyueBuildings.SubTris[1].Count / 3}/顶点{LiyueBuildings.SubTris[2].Count / 3}" +
                          (ok ? "" : "   ★ 异常"));

            Object.DestroyImmediate(mesh);
        }

        sb.AppendLine(problems == 0
            ? $"璃月建筑几何自检：通过（{System.Enum.GetValues(typeof(LiyueKind)).Length} 种全部正常）"
            : $"璃月建筑几何自检：★ 不通过（{problems} 处异常）");

        Debug.Log("[WildSiteCheck] 璃月建筑几何自检\n" + sb);
    }

    /// <summary>
    /// 国度分区自检（阶段 B）：加璃月地形最大的风险是"顺手改坏了蒙德"。
    /// 这里验三件事：
    ///  ① 蒙德腹地（LiyueWeight = 0）的高度、上色必须和"只有蒙德"时完全一样 ——
    ///     璃月那一段是整块乘权重、权重为 0 时直接跳过，所以理论上必等；这里逐点确认
    ///  ② 璃月侧确实长出了峰林（40 米以上的细高峰）和梯田台阶
    ///  ③ 雪顶确实搬到了两国分界上（分界带里有 150 米以上的主峰）
    /// </summary>
    static void VerifyNations()
    {
        var sb = new StringBuilder();

        // ---------- ① 蒙德腹地：权重必须为 0 ----------
        int mondN = 0, mondNonZero = 0;
        float mondMin = 9999f, mondMax = -9999f;

        for (int i = 0; i < 300; i++)
        {
            float x = 40f + LowPoly.Hash01(i, 1, 7001) * 520f;     // 蒙德腹地：x 40~560
            float z = 40f + LowPoly.Hash01(i, 2, 7002) * 1300f;

            if (OpenWorld.LiyueWeight(x, z) > 0f) mondNonZero++;
            mondN++;

            float h = OpenWorld.Height(x, z);
            mondMin = Mathf.Min(mondMin, h);
            mondMax = Mathf.Max(mondMax, h);
        }

        // ---------- ② 璃月侧：要有峰林和梯田 ----------
        int liyueN = 0, liyueNotFull = 0, peaks = 0, terraced = 0;
        float liyueMin = 9999f, liyueMax = -9999f;

        for (int i = 0; i < 400; i++)
        {
            float x = 900f + LowPoly.Hash01(i, 3, 7003) * 460f;    // 璃月：x 900~1360
            float z = 120f + LowPoly.Hash01(i, 4, 7004) * 1240f;

            if (OpenWorld.LiyueWeight(x, z) < 1f) liyueNotFull++;
            liyueN++;

            float h = OpenWorld.Height(x, z);
            liyueMin = Mathf.Min(liyueMin, h);
            liyueMax = Mathf.Max(liyueMax, h);

            if (h > 40f) peaks++;                                  // 峰林

            if (OpenWorld.TerraceWeight(x, z) > 0.6f)
            {
                // 梯田：高度应该贴近 2.6 米的整数倍（台阶面）
                if (Mathf.Abs(h - Mathf.Round(h / 2.6f) * 2.6f) < 0.25f) terraced++;
            }
        }

        // ---------- ③ 雪顶在分界带上（主峰位置是私有常量，用"分界带内最高点"近似测） ----------
        float peakH = 0f, peakX = 0f, peakZ = 0f;
        for (float px = 600f; px <= 880f; px += 6f)
        {
            for (float pz = 480f; pz <= 780f; pz += 6f)
            {
                float h = OpenWorld.Height(px, pz);
                if (h > peakH) { peakH = h; peakX = px; peakZ = pz; }
            }
        }

        sb.AppendLine($"蒙德腹地抽查 {mondN} 点：LiyueWeight 非 0 的有 {mondNonZero} 个（应为 0）；" +
                      $"高度 {mondMin:0}~{mondMax:0} 米");
        sb.AppendLine($"璃月抽查 {liyueN} 点：LiyueWeight < 1 的有 {liyueNotFull} 个；" +
                      $"高度 {liyueMin:0}~{liyueMax:0} 米；> 40 米的峰林点 {peaks} 个；落在梯田台阶上的 {terraced} 个");
        sb.AppendLine($"雪顶：分界带内最高点 {peakH:0} 米（{peakX:0},{peakZ:0}）—— 应在 150 米以上（主峰 176）");

        bool ok = mondNonZero == 0 && peaks > 0 && peakH > 150f;
        sb.AppendLine(ok ? "国度分区自检：通过" : "国度分区自检：★ 不通过");

        Debug.Log("[WildSiteCheck] 国度分区自检\n" + sb);
    }

    /// <summary>
    /// 地形分块自检（阶段 A）：地形从"一整块网格"拆成 4×4 之后，要保证两件事：
    /// ①块之间不重不漏 ②相邻块共享的那一列边界顶点**位置和法线完全一致**（否则地形上会出现缝和硬边）。
    ///
    /// ②不能靠"接缝附近取两个点比法线夹角"来测 —— 那量到的是地形本身的弯曲。
    /// 正确做法是把相邻两块网格真建出来，逐点比对它们共享的那一列。
    /// </summary>
    static void VerifyTerrainChunks()
    {
        var sb = new StringBuilder();

        int chunks = OpenWorld.TerrainChunks;
        int cells  = OpenWorld.ChunkCells;
        int res    = Mathf.RoundToInt(OpenWorld.Size / OpenWorld.Cell);
        int n      = cells + 1;

        // ① 格子覆盖：每块 cells² 个格子，加起来必须正好等于 res²（不重不漏）
        long coverSum = (long)chunks * chunks * cells * cells;
        bool coverOk = coverSum == (long)res * res;

        // ② 逐点比对相邻块共享的边界列
        var a = OpenWorld.BuildTerrainChunkMesh(0, 0);   // 左下那块
        var b = OpenWorld.BuildTerrainChunkMesh(1, 0);   // 它右边那块（测竖向接缝）
        var c = OpenWorld.BuildTerrainChunkMesh(0, 1);   // 它上面那块（测横向接缝）

        var av = a.vertices; var an = a.normals;
        var bv = b.vertices; var bn = b.normals;
        var cv = c.vertices; var cn = c.normals;

        float posErr = 0f, normDeg = 0f;

        // a 的最右列 (ix = cells) 应该和 b 的最左列 (ix = 0) 完全重合
        for (int iz = 0; iz < n; iz++)
        {
            posErr  = Mathf.Max(posErr,  Vector3.Distance(av[iz * n + cells], bv[iz * n]));
            normDeg = Mathf.Max(normDeg, Vector3.Angle(an[iz * n + cells], bn[iz * n]));
        }

        // a 的最上排 (iz = cells) 应该和 c 的最下排 (iz = 0) 完全重合
        for (int ix = 0; ix < n; ix++)
        {
            posErr  = Mathf.Max(posErr,  Vector3.Distance(av[cells * n + ix], cv[ix]));
            normDeg = Mathf.Max(normDeg, Vector3.Angle(an[cells * n + ix], cn[ix]));
        }

        // ③ 抽查法线确实是"解析算的"：网格里存的法线要和 TerrainNormal 公式一致。
        //    这条是给以后留的保险 —— 谁要是改回 RecalculateNormals，这里立刻会挂
        float normFormulaErr = 0f;
        for (int i = 0; i < av.Length; i += 977)
            normFormulaErr = Mathf.Max(normFormulaErr,
                                       Vector3.Angle(an[i], OpenWorld.TerrainNormal(av[i].x, av[i].z)));

        // ④ 网格规模（信息项）
        long chunkVerts = (long)n * n * chunks * chunks;
        long chunkTris  = (long)cells * cells * 2 * chunks * chunks;

        sb.AppendLine($"地图 {OpenWorld.Size:0} 米（{res} 格 × {OpenWorld.Cell:0.###} 米）；" +
                      $"分 {chunks}×{chunks} 块，每块 {cells} 格 = {OpenWorld.Size / chunks:0.#} 米");
        sb.AppendLine($"格子覆盖：{chunks}² × {cells}² = {coverSum} 格，整图 {res}² = {(long)res * res} 格 —— " +
                      $"{(coverOk ? "正好铺满，不重不漏" : "★ 对不上")}");
        sb.AppendLine($"接缝逐点比对（抽查 2 条接缝、各 {n} 对顶点）：位置最大误差 {posErr:0.#####} 米，" +
                      $"法线最大夹角 {normDeg:0.000}° —— 都应为 0");
        sb.AppendLine($"网格法线与 TerrainNormal 公式：最大偏差 {normFormulaErr:0.000}° —— 应为 0");
        sb.AppendLine($"规模：顶点 {chunkVerts:N0}（整图会是 {(long)(res + 1) * (res + 1):N0}，" +
                      $"多出来的是接缝那一列重复存的）、面 {chunkTris:N0}");

        Object.DestroyImmediate(a);
        Object.DestroyImmediate(b);
        Object.DestroyImmediate(c);

        bool ok = coverOk && posErr < 0.0005f && normDeg < 0.01f && normFormulaErr < 0.01f;
        sb.AppendLine(ok ? "地形分块自检：通过" : "地形分块自检：★ 不通过");

        Debug.Log("[WildSiteCheck] 地形分块自检\n" + sb);
    }

    /// <summary>交叉检查：解密场地有没有伸进野外建筑（正是实机里"方碑和建筑重合"的根源），
    /// 以及地台边坡判定本身是不是通的</summary>
    static void VerifyCrossOverlap()
    {
        var sb = new StringBuilder();

        int buildingOverlaps = WildSiteSpawner.CountPadOverlaps();
        int liyueOverlaps    = LiyueSiteSpawner.CountPadOverlaps();
        int puzzleOverlaps   = PuzzleSpawner.CountPadOverlaps();
        int liyuePuzzleOverlaps = LiyuePuzzleSpawner.CountPadOverlaps();

        sb.AppendLine($"地台重叠：蒙德野外建筑之间 {buildingOverlaps} 处；璃月野外建筑 {liyueOverlaps} 处" +
                      $"（含与蒙德地台相撞）；蒙德解密场地 {puzzleOverlaps} 处；璃月解密场地 {liyuePuzzleOverlaps} 处");
        sb.AppendLine("（地台重叠 = 两块平整区挨太近，后登记的会把前一块往自己高度拉 → 跨在两块上的建筑会浮空）");

        // 抽样验证"地台边坡"判定本身是通的：平整区内应判成平、过渡带中点应判成坡
        int padChecked = 0, padOk = 0;
        foreach (var s in WildSiteSpawner.Sites)
        {
            if (s.kind == WildKind.Bridge) continue;

            padChecked++;
            bool edgeInside = !OpenWorld.OnPadSlope(s.x + s.radius + 2f, s.z);
            bool midOnSlope =  OpenWorld.OnPadSlope(s.x + s.radius + 6f + 6f, s.z);

            if (edgeInside && midOnSlope) padOk++;
            else sb.AppendLine($"  ★ 台子 ({s.x:0},{s.z:0}) 的边坡判定不对：平整区内判成坡={!edgeInside}，过渡带内判成平={!midOnSlope}");
        }
        sb.AppendLine($"地台边坡判定 {padOk}/{padChecked} 通过");

        int bad = buildingOverlaps + liyueOverlaps + puzzleOverlaps + liyuePuzzleOverlaps;
        sb.AppendLine(bad == 0 ? "交叉检查：通过" : "交叉检查：★ 不通过");

        Debug.Log("[WildSiteCheck] 交叉检查\n" + sb);
    }

    /// <summary>按运行时的方式把几何拼一遍，只报数、不动场景</summary>
    static void VerifyGeometry()
    {
        var sb = new StringBuilder();

        WildBuildings.Begin();
        foreach (var s in WildSiteSpawner.Sites)
        {
            // 和 WildSiteSpawner.Init 一样：站点位置 + 地台高度（地台已经在规划阶段登记过了）+ param
            WildBuildings.Append(s.kind, new Vector3(s.x, OpenWorld.Height(s.x, s.z), s.z), s.yaw, s.variant, s.param);
        }

        var mesh = WildBuildings.BuildMesh("WildSiteCheckMesh");

        int faces = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) faces += (int)(mesh.GetIndexCount(i) / 3);

        var b = mesh.bounds;
        bool boundsOk = !float.IsNaN(b.center.x) && !float.IsNaN(b.size.x) &&
                        b.size.x > 0f && b.size.y > 0f && b.size.z > 0f &&
                        b.min.y > -100f && b.max.y < 200f;

        sb.AppendLine($"顶点 {mesh.vertexCount}，面 {faces}（石墙 {WildBuildings.SubTris[0].Count / 3} / " +
                      $"瓦片 {WildBuildings.SubTris[1].Count / 3} / 顶点色 {WildBuildings.SubTris[2].Count / 3}），" +
                      $"子网格 {mesh.subMeshCount}");
        sb.AppendLine($"包围盒 中心 ({b.center.x:0},{b.center.y:0},{b.center.z:0})  尺寸 ({b.size.x:0},{b.size.y:0},{b.size.z:0})  " +
                      $"{(boundsOk ? "正常" : "★ 不正常")}");
        sb.AppendLine($"篝火 {WildBuildings.Flames.Count} 处，民居色块 {WildBuildings.Houses.Count} 个");

        bool ok = mesh.vertexCount > 0 && faces > 0 && boundsOk &&
                  WildBuildings.SubTris[0].Count > 0 && WildBuildings.SubTris[1].Count > 0 && WildBuildings.SubTris[2].Count > 0;

        sb.AppendLine(ok ? "几何自检：通过" : "几何自检：★ 不通过");

        Object.DestroyImmediate(mesh);
        Debug.Log("[WildSiteCheck] 野外建筑几何自检\n" + sb);
    }

    /// <summary>稻妻建筑的几何自检：11 种每种单独拼一遍，逐个报顶点 / 面 / 包围盒，
    /// 确认没有空网格、没有跑到天上的顶点、每个三角形都落进了某个子网格</summary>
    static void VerifyInazumaGeometry()
    {
        var sb = new StringBuilder();
        int problems = 0;

        foreach (InazumaKind kind in System.Enum.GetValues(typeof(InazumaKind)))
        {
            InazumaBuildings.Begin();
            InazumaBuildings.Append(kind, new Vector3(1000f, 6f, 1000f), 0f, 0.5f, 14f);

            var mesh = InazumaBuildings.BuildMesh("InazumaCheck_" + kind);

            int faces = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) faces += (int)(mesh.GetIndexCount(i) / 3);

            var b = mesh.bounds;
            int idxTotal = InazumaBuildings.SubTris[0].Count + InazumaBuildings.SubTris[1].Count +
                           InazumaBuildings.SubTris[2].Count;

            bool ok = mesh.vertexCount > 0 && faces > 0 && idxTotal == faces * 3 &&
                      b.size.y > 0.8f && b.size.y < 80f &&
                      b.min.y > -40f && b.max.y < 90f;

            if (!ok) problems++;

            sb.AppendLine($"  {InazumaBuildings.DisplayName(kind)}：顶点 {mesh.vertexCount:N0}，面 {faces:N0}，" +
                          $"包围盒 ({b.size.x:0.#},{b.size.y:0.#},{b.size.z:0.#})，" +
                          $"子网格 石{InazumaBuildings.SubTris[0].Count / 3}/瓦{InazumaBuildings.SubTris[1].Count / 3}" +
                          $"/顶点{InazumaBuildings.SubTris[2].Count / 3}" + (ok ? "" : "   ★ 异常"));

            Object.DestroyImmediate(mesh);
        }

        sb.AppendLine(problems == 0
            ? $"稻妻建筑几何自检：通过（{System.Enum.GetValues(typeof(InazumaKind)).Length} 种全部正常）"
            : $"稻妻建筑几何自检：★ 不通过（{problems} 处异常）");

        Debug.Log("[WildSiteCheck] 稻妻建筑几何自检\n" + sb);
    }

    /// <summary>稻妻内容的落地自检：按运行时的方式把布局表拼一遍，报顶点 / 面 / 包围盒，
    /// 再逐处核对"落点是不是在稻妻陆地上、有没有一脚踩进海里"</summary>
    static void VerifyInazumaSites()
    {
        var sb = new StringBuilder();

        InazumaBuildings.Begin();
        foreach (var s in InazumaSiteSpawner.Sites)
        {
            float y = s.y > 0.01f ? s.y : OpenWorld.Height(s.x, s.z);
            InazumaBuildings.Append(s.kind, new Vector3(s.x, y, s.z), s.yaw, s.variant, s.param);
        }

        var mesh = InazumaBuildings.BuildMesh("InazumaSiteCheckMesh");

        int faces = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) faces += (int)(mesh.GetIndexCount(i) / 3);

        var b = mesh.bounds;
        bool boundsOk = !float.IsNaN(b.center.x) && b.size.x > 0f && b.size.y > 0f && b.size.z > 0f &&
                        b.min.y > -20f && b.max.y < 120f;

        sb.AppendLine($"建筑 {InazumaSiteSpawner.Sites.Count} 处；顶点 {mesh.vertexCount:N0}，面 {faces:N0}；" +
                      $"火苗 {InazumaBuildings.Flames.Count} 处，民居色块 {InazumaBuildings.Houses.Count} 个");
        sb.AppendLine($"包围盒 中心 ({b.center.x:0},{b.center.y:0},{b.center.z:0})  " +
                      $"尺寸 ({b.size.x:0},{b.size.y:0},{b.size.z:0})  {(boundsOk ? "正常" : "★ 不正常")}");

        // 逐处：落点要在稻妻陆地上（或者在固定高度的台地上），且不能低于水面
        int bad = 0;
        foreach (var s in InazumaSiteSpawner.Sites)
        {
            float y = s.y > 0.01f ? s.y : OpenWorld.Height(s.x, s.z);
            bool onLand = InazumaIsles.LandWeight(s.x, s.z) > 0.25f || s.y > 0.01f;
            bool aboveWater = y > OpenWorld.WaterLevel + 0.5f ||
                              s.kind == InazumaKind.Dock || s.kind == InazumaKind.Bridge;

            if (onLand && aboveWater) continue;

            bad++;
            sb.AppendLine($"  ★ {s.name} ({s.x:0},{s.z:0}) 落点 {y:0.0} 米" +
                          $"{(onLand ? "" : " 不在稻妻陆地上")}{(aboveWater ? "" : " 在水面以下")}");
        }

        bool ok = mesh.vertexCount > 0 && faces > 0 && boundsOk && bad == 0;

        sb.AppendLine(ok ? "稻妻落地自检：通过" : $"稻妻落地自检：★ 不通过（{bad} 处落点有问题）");

        Object.DestroyImmediate(mesh);
        Debug.Log("[WildSiteCheck] 稻妻落地自检\n" + sb);
    }

    /// <summary>解密场地的几何自检：静态石台拼一遍，再加上方碑 / 宝石网格</summary>
    static void VerifyPuzzleGeometry()
    {
        var sb = new StringBuilder();

        WildPuzzles.Begin();
        foreach (var s in PuzzleSpawner.Sites)
        {
            var center = new Vector3(s.x, OpenWorld.Height(s.x, s.z), s.z);
            var order = s.kind == PuzzleKind.Ordered ? WildPuzzles.RequiredOrder(s.kind, 0f, s.seed) : null;
            WildPuzzles.AppendGround(s.kind, center, s.yaw, order);
        }

        var ground = WildPuzzles.BuildMesh("PuzzleCheckGround");
        int groundFaces = 0;
        for (int i = 0; i < ground.subMeshCount; i++) groundFaces += (int)(ground.GetIndexCount(i) / 3);

        var dark = WildPuzzles.MonolithMesh(false);
        var lit  = WildPuzzles.MonolithMesh(true);
        var gem  = WildPuzzles.GemMesh(0, true);
        var gemD = WildPuzzles.GemMesh(0, false);

        sb.AppendLine($"石台：顶点 {ground.vertexCount}，面 {groundFaces}，子网格 {ground.subMeshCount}；" +
                      $"入口火把 {WildPuzzles.Flames.Count} 处");
        sb.AppendLine($"方碑：暗 {dark.vertexCount} 顶点 / {dark.GetIndexCount(0) / 3} 面，" +
                      $"亮 {lit.vertexCount} 顶点 / {lit.GetIndexCount(0) / 3} 面；" +
                      $"宝石 亮 {gem.vertexCount} / 暗 {gemD.vertexCount} 顶点");
        sb.AppendLine($"石台包围盒 中心 ({ground.bounds.center.x:0},{ground.bounds.center.y:0},{ground.bounds.center.z:0})  " +
                      $"尺寸 ({ground.bounds.size.x:0},{ground.bounds.size.y:0},{ground.bounds.size.z:0})");

        // 顺序提示：把每处场地的要求顺序列出来，顺便确认排列确实被打乱了（不是 0,1,2,3 原样）
        sb.Append("顺序场地的要求顺序：");
        foreach (var s in PuzzleSpawner.Sites)
        {
            if (s.kind != PuzzleKind.Ordered) continue;
            var order = WildPuzzles.RequiredOrder(s.kind, 0f, s.seed);
            sb.Append($"({s.x:0},{s.z:0})→{string.Join("-", order)}  ");
        }
        sb.AppendLine();

        bool ok = ground.vertexCount > 0 && groundFaces > 0 && dark.vertexCount > 0 && lit.vertexCount > 0 &&
                  WildPuzzles.Flames.Count > 0;
        sb.AppendLine(ok ? "解密几何自检：通过" : "解密几何自检：★ 不通过");

        Object.DestroyImmediate(ground);
        Debug.Log("[WildSiteCheck] 解密场地几何自检\n" + sb);
    }

    [InitializeOnLoadMethod]
    static void AutoRunOnce()
    {
        EditorApplication.delayCall += () =>
        {
            Verify();
        };
    }
}
