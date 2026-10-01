using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 运行时自动搭场景：直接按 Play 就能玩，不需要手改 SampleScene。
/// 因为脚本是在场景加载后创建对象，所以空场景也能跑起来。
/// </summary>
public static class VoxelBootstrap
{
    static readonly Color SkyColor = new Color(0.62f, 0.82f, 0.94f);   // 和网页版同一个天蓝
    const float FogStart = 34f;
    const float FogEnd   = 96f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        // 如果场景里已经手动摆过一套，就不重复生成
        if (Object.FindObjectOfType<VoxelWorld>() != null) return;

        // ---------- 1. 世界 ----------
        var worldGo = new GameObject("VoxelWorld");
        worldGo.AddComponent<MeshFilter>();
        worldGo.AddComponent<MeshRenderer>();
        worldGo.AddComponent<MeshCollider>();

        var world = worldGo.AddComponent<VoxelWorld>();   // Awake 里生成地形并建网格
        world.SetFog(SkyColor, FogStart, FogEnd);

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
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = SkyColor;
        cam.nearClipPlane   = 0.1f;
        cam.farClipPlane    = 220f;
        cam.fieldOfView     = 75f;

        player.Init(cam.transform);

        // ---------- 4. 选中框（12 条边构成的线框立方体）----------
        var highlightGo = new GameObject("VoxelHighlight");
        highlightGo.AddComponent<MeshFilter>().sharedMesh = BuildWireCubeMesh(1.004f);
        highlightGo.AddComponent<MeshRenderer>().sharedMaterial = world.VoxelMaterial;
        highlightGo.SetActive(false);

        // ---------- 5. 交互与 HUD ----------
        var interaction = playerGo.AddComponent<VoxelInteraction>();
        interaction.Init(world, cam.transform, highlightGo.transform);

        playerGo.AddComponent<VoxelHud>().Init(interaction);

        Debug.Log("[VoxelDemo] 世界生成完毕，点击画面开始游玩。");
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
