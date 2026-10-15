using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 运行时自动搭场景：直接按 Play 就能玩，不需要手改 SampleScene。
/// 因为脚本是在场景加载后创建对象，所以空场景也能跑起来。
/// </summary>
public static class VoxelBootstrap
{
    static readonly Color SkyColor = new Color(0.56f, 0.80f, 0.95f);   // 提瓦特那种明亮的青空
    static readonly Color FogColor = new Color(0.76f, 0.89f, 0.98f);   // 远处一层奶白的雾，画面更通透
    const float FogStart = 250f;                                       // 雾拉远：站在城里也要能看见东北边的「Andy的雪顶」
    const float FogEnd   = 2100f;                                      // 地图扩到 1800 米（稻妻在东南海上），雾的远端跟着放远

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        // 如果场景里已经手动摆过一套，就不重复生成
        if (Object.FindObjectOfType<OpenWorld>() != null) return;

        // ---------- 1. 开放世界地形（蒙德草原风：平滑丘陵 + 湖水 + 树石 + 后处理）----------
        var worldGo = new GameObject("OpenWorld");
        var world = worldGo.AddComponent<OpenWorld>();    // Awake 里把地形、树石、湖水、后处理都生成好
        world.SetFog(FogColor, FogStart, FogEnd);

        // ---------- 2. 玩家 ----------
        var playerGo = new GameObject("Player");

        var controller = playerGo.AddComponent<CharacterController>();
        controller.height     = 1.8f;
        controller.radius     = 0.3f;
        controller.center     = new Vector3(0f, 0.9f, 0f);
        controller.slopeLimit = 90f;     // 允许站在方块边缘
        controller.stepOffset = 0.4f;
        controller.skinWidth  = 0.02f;

        playerGo.transform.position = world.SpawnPoint;

        var player = playerGo.AddComponent<VoxelPlayer>();

        // ---------- 3. 相机（复用场景里那台，URP 的相机数据组件能保留下来）----------
        Camera cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }

        cam.transform.SetParent(playerGo.transform, false);
        cam.clearFlags      = CameraClearFlags.SolidColor;   // 有天空盒时 OpenWorld 会把它改成 Skybox
        cam.backgroundColor = SkyColor;
        cam.nearClipPlane   = 0.1f;
        cam.farClipPlane   = 3200f;    // 地图 1800 米（对角 2546）+ 东北边有雪顶、东南海上有稻妻，裁剪面要跟着拉开
        cam.fieldOfView     = 72f;

        player.Init(cam.transform, world);

        // ---------- 4. 数值系统（生命/饱食度）----------
        var stats = playerGo.AddComponent<VoxelStats>();
        stats.Init(player);

        // ---------- 5. 交互与 HUD ----------
        var interaction = playerGo.AddComponent<VoxelInteraction>();
        interaction.Init(world, cam.transform);

        playerGo.AddComponent<VoxelHud>().Init(interaction, player, stats);

        // ---------- 5.5 Esc 菜单（成就 / 角色 / 地区 / 操作）----------
        // 和 HUD / 祈愿 / 大地图并列：Esc 打开、Esc 关，打开时放开鼠标（玩家控制自然停）
        playerGo.AddComponent<VoxelMenu>().Init(interaction, player, stats);

        // ---------- 6. 世界地图 / 小地图 ----------
        // ★ 地图创建放到 8.5（所有生成器之后）再调用 —— 原因见那里的注释：
        //   底图是一次性烘出来的，村落 / 港城的色块要是晚于烘焙登记，地图上就一个色块都没有

        // ---------- 6.5 玩家普攻（鼠标左键，用来打野外怪）----------
        var combat = playerGo.AddComponent<VoxelCombat>();
        combat.Init(cam.transform);

        // ---------- 6. 对话框（必须在 VoxelStats 之后添加，Init 里会去找它）----------
        playerGo.AddComponent<VoxelChat>().Init(player);

        // ---------- 7. 抽卡页面（按 P 开关；目前只有界面，卡池内容待定）----------
        playerGo.AddComponent<VoxelGacha>().Init(interaction);

        // ---------- 7.4 野外建筑：村落 / 各类设施 / 遗迹（地形地台由 WildSiteSpawner.PlanSites 提前垫好）----------
        // 必须在怪物和宝箱之前：后面两者都要避开野外建筑，别把营地扎进村子里
        var siteGo = new GameObject("WildSiteSpawner");
        siteGo.AddComponent<WildSiteSpawner>().Init(world, playerGo.transform);

        // ---------- 7.45 璃月野外建筑：村落 / 楼阁 / 牌坊 / 吊脚楼 / 码头 / 石拱桥……（风格和蒙德完全不同）----------
        // 和蒙德那边一样要排在怪物 / 宝箱之前：它们都要避开璃月建筑
        var liyueGo = new GameObject("LiyueSiteSpawner");
        liyueGo.AddComponent<LiyueSiteSpawner>().Init(world, playerGo.transform);

        // ---------- 7.46 稻妻：将军府城 + 港口 + 两座离岛（岛的地形在 InazumaIsles，地台在 PlanIsles 登记）----------
        // 和璃月那边一样要排在怪物 / 宝箱之前：它们都要避开稻妻建筑
        var inazumaGo = new GameObject("InazumaSiteSpawner");
        inazumaGo.AddComponent<InazumaSiteSpawner>().Init(world, playerGo.transform);

        // ---------- 7.5 野外怪：丘丘人 / 史莱姆 + 丘丘人营地（分布用固定哈希，见 MonsterSpawner）----------
        var spawnerGo = new GameObject("MonsterSpawner");
        spawnerGo.AddComponent<MonsterSpawner>().Init(world, playerGo.transform, interaction);

        // ---------- 7.6 战利品宝箱：5 个品质散落在世界各处（不进大地图标记），部分要清掉守卫才能开 ----------
        // 必须在 MonsterSpawner 之后：营地位置要先定下来，宝箱才知道该避开哪儿
        var chestGo = new GameObject("LootChestSpawner");
        chestGo.AddComponent<LootChestSpawner>().Init(world, playerGo.transform, interaction, world.Material);

        // ---------- 7.7 解密场地：元素方碑（最后一件事 —— 它要避开建筑、营地、宝箱）----------
        var puzzleGo = new GameObject("PuzzleSpawner");
        puzzleGo.AddComponent<PuzzleSpawner>().Init(world, playerGo.transform, interaction);

        // ---------- 7.75 璃月解密场地：石灯阵 / 石鼓（玩法同一套，外形和称呼不同）----------
        var liyuePuzzleGo = new GameObject("LiyuePuzzleSpawner");
        liyuePuzzleGo.AddComponent<LiyuePuzzleSpawner>().Init(world, playerGo.transform, interaction);

        // ---------- 7.8 传送锚点（走近点亮；之后在大地图上点它传送）----------
        // 必须排在所有建筑生成器之后：定落脚点要用 Physics 探"这儿有没有墙 / 篱笆"；
        // 又必须排在 8.5 的世界地图之前 —— 地图要把锚点图钉画上去
        var anchorGo = new GameObject("TeleportSpawner");
        anchorGo.AddComponent<TeleportSpawner>().Init(world, playerGo.transform);

        // ---------- 8. NPC：出生点附近的两个小人，数据在 VoxelNpcData 里 ----------
        SpawnNpc("NpcSunYan", VoxelNpcData.SunYan(),   3,   3, 0f, world, playerGo.transform);
        SpawnNpc("NpcAndy",   VoxelNpcData.Andy(),   -10, -10, 3f, world, playerGo.transform);

        // ---------- 8.5 世界地图 / 小地图（左上角小地图常驻，M 键开大地图）----------
        // ★ 必须排在所有生成器之后：大地图的底图是在 Init 里一次性烘出来的（1024×1024），
        //   而村落 / 遗迹 / 璃月港那些"色块 + 街线 + 地标"是各自在 Init 里登记的 ——
        //   地图先烘的话一个色块都画不上去，地图上只剩图钉（图钉是每帧现画的，
        //   所以看起来就是"有标记、没有城"，璃月港那片更是空的）。
        playerGo.AddComponent<WorldMap>().Init(world, playerGo.transform);

        // 注：浮空测试平台（左右各 50 个 NPC）已经撤掉，脚本 Assets/Scripts/TestPlatform.cs 保留着，
        // 以后要再压测 100 个角色，把下面三行取消注释即可（玩家会被传送到平台上）：
        // var platformGo = new GameObject("TestPlatform");
        // var platform = platformGo.AddComponent<TestPlatform>();
        // platform.Init(world, playerGo.transform); 然后 TeleportPlayer(playerGo, platform.PlayerSpawnPoint);

        // ---------- 9. 开幕动画（标题画面）----------
        // 放在最后：世界已经生成好，标题就浮在真实场景上面（相机在标题期间缓慢横移当运镜）。
        // 标题播放期间会把玩家的移动 / 攻击 / 交互三个组件关掉，点"进入游戏"才交还控制权
        var titleGo = new GameObject("TitleScreen");
        titleGo.AddComponent<TitleScreen>().Init(cam, player, combat, interaction);

        Debug.Log("[VoxelDemo] 开放世界生成完毕。WASD 移动 / 空格 跳跃 / 左键 攻击 / Q 丢道具 / E 开宝箱，" +
                  "T 对话框，P 祈愿，M 世界地图，/fly 飞行，/eat 恢复饱食度。");
    }

    /// <summary>按数据在出生点附近生成一个 NPC；落点在水边就往旁边挪，找块干地站。
    /// spawnHeight 是离地高度：给 0 直接站在地面上，给正数就从空中落下</summary>
    static void SpawnNpc(string goName, VoxelNpcData data, float offsetX, float offsetZ, float spawnHeight,
                         OpenWorld world, Transform player)
    {
        Vector3 spawn = world.SpawnPoint;
        float x = spawn.x + offsetX;
        float z = spawn.z + offsetZ;

        // 落在水边 / 水里就往旁边挪（和 NPC 走路时的避水规则同一个余量）
        for (int i = 0; i < 10 && OpenWorld.Height(x, z) < OpenWorld.WaterLevel + 1f; i++)
        {
            x -= 2f;
            z += 2f;
        }

        x = Mathf.Clamp(x, 3f, OpenWorld.Size - 3f);
        z = Mathf.Clamp(z, 3f, OpenWorld.Size - 3f);

        var npcGo = new GameObject(goName);
        npcGo.transform.position = new Vector3(x, world.SurfaceY(x, z) + spawnHeight, z);

        var npc = npcGo.AddComponent<VoxelNpc>();
        npc.data = data;
        npc.Init(world.Material, player, world);
    }

    static Mesh BuildWireCubeMesh(float size)
    {
        float h = size * 0.5f;

        Vector3[] c =
        {
            new Vector3(-h, -h, -h), new Vector3( h, -h, -h), new Vector3( h, -h,  h), new Vector3(-h, -h,  h),
            new Vector3(-h,  h, -h), new Vector3( h,  h, -h), new Vector3( h,  h,  h), new Vector3(-h,  h,  h),
        };

        int[] edges = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };

        var verts = new Vector3[edges.Length];
        var cols  = new Color[edges.Length];
        for (int i = 0; i < edges.Length; i++)
        {
            verts[i] = c[edges[i]];
            cols[i]  = Color.black;
        }

        var mesh = new Mesh { name = "VoxelHighlightMesh" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetColors(cols);

        var indices = new int[verts.Length];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        mesh.SetIndices(indices, MeshTopology.Lines, 0);
        mesh.RecalculateBounds();

        return mesh;
    }
}
