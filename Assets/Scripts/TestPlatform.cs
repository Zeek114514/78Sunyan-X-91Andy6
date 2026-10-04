using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 测试用浮空大平台：一座 120 × 44 米的悬空平台，高度 120 米（纯浮空，底下只有装饰岩体）。
/// 左边 5 排共 50 个孙琰、右边 5 排共 50 个 Andy，玩家一进游戏就被传送到平台上看它们。
/// 这 100 个阵型 NPC 都是 holdPosition（站着不动），所以队形不会散，正好一次看 100 个角色的
/// 模型 / 站立动画 / 性能；平台上还留了 120 米长的边距，方便跑起来看远近处的表现。
/// </summary>
public class TestPlatform : MonoBehaviour
{
    public const float PlatformY = 120f;                  // 平台顶面离地高度
    public const int   LeftCount  = 50;                   // 左边孙琰数量
    public const int   RightCount = 50;                   // 右边 Andy 数量

    const int   Rows    = 5;                              // 各 5 排
    const int   PerRow  = 10;                             // 每排 10 个
    const float ColStep = 3.6f;                           // 列间距（沿 X）
    const float RowStep = 4.4f;                           // 排间距（沿 Z）
    const float BlockX  = 42f;                            // 阵型中心离平台中线多远
    const float FacingYaw = 180f;                         // 全部朝 -Z 站（面向玩家传送点）

    Vector3 _center;                                      // 平台中心（顶面高度）

    /// <summary>玩家被传送上去的落点（平台前缘中央，稍微高一点免得卡进地板）</summary>
    public Vector3 PlayerSpawnPoint => _center + new Vector3(0f, 1.2f, -17f);

    public void Init(OpenWorld world, Transform player)
    {
        _center = new Vector3(150f, PlatformY, 30f);

        BuildPlatform(world.Material);
        int spawned = SpawnFormations(world, player);

        MondstadtTown.Markers.Add(new MondstadtTown.MapMarker { name = "测试平台（100 个 NPC）", x = _center.x, z = _center.z, kind = 13 });

        Debug.Log($"[TestPlatform] 浮空平台建好：中心 ({_center.x:0}, {_center.y:0}, {_center.z:0})，" +
                  $"平台 120 × 44 米；孙琰 {LeftCount} 个 + Andy {RightCount} 个 = {spawned} 个阵型 NPC（全部 holdPosition）。");
    }

    // ---------- 阵型 ----------

    /// <summary>左边 5 排孙琰、右边 5 排 Andy，两边镜像对称，中间留出通路给玩家站</summary>
    int SpawnFormations(OpenWorld world, Transform player)
    {
        int count = 0;

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < PerRow; col++)
            {
                // 左边：孙琰（从平台中线往左铺）
                Vector3 left = _center + new Vector3(-BlockX + col * ColStep, 0.15f, -8.8f + row * RowStep);
                SpawnOne("NpcSunYan_" + row + "_" + col, VoxelNpcData.SunYan(), left, world, player);
                count++;

                // 右边：Andy（镜像）
                Vector3 right = _center + new Vector3(BlockX - col * ColStep, 0.15f, -8.8f + row * RowStep);
                SpawnOne("NpcAndy_" + row + "_" + col, VoxelNpcData.Andy(), right, world, player);
                count++;
            }
        }

        return count;
    }

    void SpawnOne(string goName, VoxelNpcData data, Vector3 pos, OpenWorld world, Transform player)
    {
        data.holdPosition = true;                    // 站着不动，保持队形

        var go = new GameObject(goName);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, FacingYaw, 0f);

        var npc = go.AddComponent<VoxelNpc>();
        npc.data = data;
        npc.Init(world.Material, player, world);
    }

    // ---------- 平台本体 ----------

    /// <summary>平台：甲板 + 金边 + 栏杆 + 底下的浮空岩体 + 左右两块的阵型地贴 + 中央观礼台 + 旗杆</summary>
    void BuildPlatform(Material material)
    {
        var v = new List<Vector3>();
        var c = new List<Color>();
        var t = new List<int>();

        Color deck   = new Color32(0xd9, 0xd3, 0xc3, 255);
        Color deck2  = new Color32(0xc6, 0xbf, 0xae, 255);
        Color stone  = new Color32(0xa9, 0xa3, 0x95, 255);
        Color stoneD = new Color32(0x8b, 0x86, 0x7a, 255);
        Color gold   = new Color32(0xe8, 0xd0, 0x8f, 255);
        Color leftC  = new Color32(0x6f, 0xb8, 0xe0, 255);   // 孙琰那半边
        Color rightC = new Color32(0xf0, 0x9a, 0x6a, 255);   // Andy 那半边
        Color wood   = new Color32(0x7c, 0x58, 0x3a, 255);

        const float W = 120f, D = 44f;
        float halfW = W * 0.5f, halfD = D * 0.5f;

        // 主甲板（顶面 y = 0）与底部收分
        Box(v, c, t, _center + new Vector3(0f, -3f, 0f), new Vector3(W, 6f, D), Quaternion.identity, deck, stone);
        Box(v, c, t, _center + new Vector3(0f, -9f, 0f), new Vector3(W - 8f, 6f, D - 6f), Quaternion.identity, stone, deck2);
        Box(v, c, t, _center + new Vector3(0f, -18f, 0f), new Vector3(66f, 12f, 24f), Quaternion.identity, stoneD, stone);
        Box(v, c, t, _center + new Vector3(0f, -30f, 0f), new Vector3(30f, 12f, 12f), Quaternion.identity, stoneD, stoneD);

        // 金色压边（四条边各一条）
        Box(v, c, t, _center + new Vector3(0f, -0.1f, -halfD + 0.8f), new Vector3(W, 0.4f, 1.6f), Quaternion.identity, gold, gold);
        Box(v, c, t, _center + new Vector3(0f, -0.1f,  halfD - 0.8f), new Vector3(W, 0.4f, 1.6f), Quaternion.identity, gold, gold);
        Box(v, c, t, _center + new Vector3(-halfW + 0.8f, -0.1f, 0f), new Vector3(1.6f, 0.4f, D), Quaternion.identity, gold, gold);
        Box(v, c, t, _center + new Vector3( halfW - 0.8f, -0.1f, 0f), new Vector3(1.6f, 0.4f, D), Quaternion.identity, gold, gold);

        // 左右两块的阵型地贴（一眼看出哪边是谁）
        Box(v, c, t, _center + new Vector3(-26f, 0.05f, 0f), new Vector3(40f, 0.1f, 24f), Quaternion.identity, leftC, leftC);
        Box(v, c, t, _center + new Vector3( 26f, 0.05f, 0f), new Vector3(40f, 0.1f, 24f), Quaternion.identity, rightC, rightC);

        // 栏杆：四周立柱子 + 两道横杆（免得以后换成会动的 NPC 掉下去）
        for (float x = -halfW + 2f; x <= halfW - 2f; x += 4f)
        {
            Rail(v, c, t, _center + new Vector3(x, 0.8f, -halfD + 0.6f));
            Rail(v, c, t, _center + new Vector3(x, 0.8f,  halfD - 0.6f));
        }
        for (float z = -halfD + 6f; z <= halfD - 6f; z += 4f)
        {
            Rail(v, c, t, _center + new Vector3(-halfW + 0.6f, 0.8f, z));
            Rail(v, c, t, _center + new Vector3( halfW - 0.6f, 0.8f, z));
        }
        Box(v, c, t, _center + new Vector3(0f, 1.35f, -halfD + 0.6f), new Vector3(W - 3f, 0.25f, 0.3f), Quaternion.identity, wood, wood);
        Box(v, c, t, _center + new Vector3(0f, 1.35f,  halfD - 0.6f), new Vector3(W - 3f, 0.25f, 0.3f), Quaternion.identity, wood, wood);
        Box(v, c, t, _center + new Vector3(-halfW + 0.6f, 1.35f, 0f), new Vector3(0.3f, 0.25f, D - 3f), Quaternion.identity, wood, wood);
        Box(v, c, t, _center + new Vector3( halfW - 0.6f, 1.35f, 0f), new Vector3(0.3f, 0.25f, D - 3f), Quaternion.identity, wood, wood);

        // 中央观礼台（玩家落点）：两级台阶 + 台面
        Box(v, c, t, _center + new Vector3(0f, 0.15f, -17f), new Vector3(24f, 0.3f, 16f), Quaternion.identity, deck2, stone);
        Box(v, c, t, _center + new Vector3(0f, 0.5f, -19f), new Vector3(18f, 1.0f, 12f), Quaternion.identity, deck, stone);
        for (int s = -1; s <= 1; s += 2)
        {
            // 两根旗杆，站在台阶两侧
            Vector3 pole = _center + new Vector3(s * 12f, 0f, -19f);
            Box(v, c, t, pole + Vector3.up * 4f, new Vector3(0.3f, 8f, 0.3f), Quaternion.identity, wood, wood);
            Box(v, c, t, pole + Vector3.up * 7.6f, new Vector3(0.12f, 2.0f, 2.6f), Quaternion.identity,
                new Color32(0x5f, 0xa8, 0xd8, 255), new Color32(0x4c, 0x8f, 0xc4, 255));
        }

        // 合成一个网格 + 碰撞体（100 个 NPC 和玩家都踩在上面）
        var mesh = new Mesh { name = "TestPlatform" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(v);
        mesh.SetColors(c);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("TestPlatformMesh");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    /// <summary>一根栏杆柱（顶面小一点，看着像立柱）</summary>
    static void Rail(List<Vector3> v, List<Color> c, List<int> t, Vector3 basePos)
    {
        Color post = new Color32(0xb8, 0xb1, 0xa1, 255);
        Box(v, c, t, basePos, new Vector3(0.28f, 1.7f, 0.28f), Quaternion.identity, post, post);
    }

    /// <summary>一个盒子（顶面 / 侧面分开上色）：绕序和城里那套一致，法线朝外</summary>
    static void Box(List<Vector3> verts, List<Color> cols, List<int> tris,
                    Vector3 center, Vector3 size, Quaternion rot, Color top, Color side)
    {
        int baseIndex = verts.Count;
        Vector3 h = size * 0.5f;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? -h.x : h.x,
                                         (i & 2) == 0 ? -h.y : h.y,
                                         (i & 4) == 0 ? -h.z : h.z);
            verts.Add(center + rot * corner);
            cols.Add(((i & 2) == 0 ? side : top).linear);
        }

        int[] faces =
        {
            0, 2, 3, 0, 3, 1,
            4, 5, 7, 4, 7, 6,
            0, 1, 5, 0, 5, 4,
            2, 6, 7, 2, 7, 3,
            1, 3, 7, 1, 7, 5,
            0, 4, 6, 0, 6, 2,
        };
        for (int i = 0; i < faces.Length; i++) tris.Add(baseIndex + faces[i]);
    }
}
