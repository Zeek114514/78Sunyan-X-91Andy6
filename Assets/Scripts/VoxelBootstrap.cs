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
    const float FogStart = 130f;                                       // 雾拉远：站在城里也要能看见东北边的龙脊雪山
    const float FogEnd   = 620f;

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
        cam.farClipPlane    = 1200f;    // 地图大了（600 米）+ 东北边有雪山，裁剪面要跟着拉开
        cam.fieldOfView     = 72f;

        player.Init(cam.transform, world);

        // ---------- 4. 数值系统（生命/饱食度）----------
        var stats = playerGo.AddComponent<VoxelStats>();
        stats.Init(player);

        // ---------- 5. 交互与 HUD ----------
        var interaction = playerGo.AddComponent<VoxelInteraction>();
        interaction.Init(world, cam.transform);

        playerGo.AddComponent<VoxelHud>().Init(interaction, player, stats);

        // ---------- 6. 世界地图 / 小地图（左上角小地图常驻，M 键开大地图）----------
        playerGo.AddComponent<WorldMap>().Init(world, playerGo.transform);

        // ---------- 6.5 玩家普攻（鼠标左键，用来打野外怪）----------
        playerGo.AddComponent<VoxelCombat>().Init(cam.transform);

        // ---------- 6. 对话框（必须在 VoxelStats 之后添加，Init 里会去找它）----------
        playerGo.AddComponent<VoxelChat>().Init(player);

        // ---------- 7. 抽卡页面（按 P 开关；目前只有界面，卡池内容待定）----------
        playerGo.AddComponent<VoxelGacha>().Init(interaction);

        // ---------- 7.5 野外怪：丘丘人 / 史莱姆 + 丘丘人营地（分布用固定哈希，见 MonsterSpawner）----------
        var spawnerGo = new GameObject("MonsterSpawner");
        spawnerGo.AddComponent<MonsterSpawner>().Init(world, playerGo.transform, interaction);

        // ---------- 8. NPC：出生点附近的两个小人，数据在 VoxelNpcData 里 ----------
        SpawnNpc("NpcSunYan", VoxelNpcData.SunYan(),   3,   3, 0f, world, playerGo.transform);
        SpawnNpc("NpcAndy",   VoxelNpcData.Andy(),   -10, -10, 3f, world, playerGo.transform);

        // ---------- 9. 测试用浮空大平台：左 50 个孙琰 / 右 50 个 Andy，各站 5 排，玩家直接被传送上去 ----------
        var platformGo = new GameObject("TestPlatform");
        var platform = platformGo.AddComponent<TestPlatform>();
        platform.Init(world, playerGo.transform);
        TeleportPlayer(playerGo, platform.PlayerSpawnPoint);

        Debug.Log("[VoxelDemo] 开放世界生成完毕，点击画面开始游玩。WASD 移动 / 空格 跳跃 / 左键 攻击 / Q 丢道具，" +
                  "T 对话框，P 祈愿，M 世界地图，/fly 飞行，/eat 恢复饱食度。" +
                  "（本局出生在浮空测试平台上：左 50 孙琰 / 右 50 Andy）");
    }

    /// <summary>把玩家瞬移到指定位置（先关掉 CharacterController 再瞬移，免得被物理推回去）</summary>
    static void TeleportPlayer(GameObject playerGo, Vector3 pos)
    {
        var cc = playerGo.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        playerGo.transform.position = pos;
        if (cc != null) cc.enabled = true;
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
