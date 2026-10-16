using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 草地细节层：在地形上按格撒"草卡片"（交叉的两片小面片，顶点色上深下浅），
/// 和树石一样在**生成世界时一次性烘进分块网格**，运行期不花任何开销。
///
/// 只长在该长草的地方：水面附近的沙滩、陡坡露岩、路面上、城里石板街、野外建筑 / 解密场地占地、
/// 雪线附近都不长；密度按国度分（蒙德草原最密，璃月偏干稀一点，稻妻是秋色），
/// 林子下面也压稀（树挡了光）。配色直接取地形那套调色板，所以草地和地面是一个颜色体系，不会"浮"在上面。
///
/// 顶点色 + 世界材质（和地形 / 树石共用），不碰贴图；两片面片都写正反两组三角形，
/// 所以从任何角度看都是实心的，不需要改着色器做双面。
/// </summary>
public static class VoxelGrass
{
    const int   Step        = 3;        // 每隔几格撒一次（Cell ≈ 1.67 米，3 格 ≈ 5 米一处）
    const float MinHeight   = 0.30f;    // 草卡片高度范围（米）
    const float MaxHeight   = 0.62f;
    const float WidthScale  = 0.85f;    // 卡片宽度 = 高度 × 这个系数

    static readonly List<Vector3> _verts = new List<Vector3>();
    static readonly List<Color>   _cols  = new List<Color>();
    static readonly List<int>     _tris  = new List<int>();

    // 地表调色板（和 OpenWorld.MondstadtColor / LiyueColor 同一套）
    static readonly Color MondRoot   = new Color32(0x5c, 0xb4, 0x45, 255);
    static readonly Color MondTip    = new Color32(0xa9, 0xe0, 0x6b, 255);
    static readonly Color LiyueRoot  = new Color32(0x93, 0xbf, 0x5c, 255);
    static readonly Color LiyueTip   = new Color32(0xb2, 0xd6, 0x78, 255);
    static readonly Color AutumnRoot = new Color32(0xa8, 0x86, 0x4e, 255);
    static readonly Color AutumnTip  = new Color32(0xd8, 0xbb, 0x78, 255);

    /// <summary>撒满全图（由 OpenWorld 在树石之后调用）</summary>
    public static void Build(Transform parent, Material material)
    {
        int res = Mathf.RoundToInt(OpenWorld.Size / OpenWorld.Cell);
        int chunkCells = OpenWorld.ChunkCells;
        int placed = 0;

        for (int tx = 0; tx < OpenWorld.TerrainChunks; tx++)
        for (int tz = 0; tz < OpenWorld.TerrainChunks; tz++)
        {
            _verts.Clear(); _cols.Clear(); _tris.Clear();

            int gx0 = tx * chunkCells, gx1 = Mathf.Min(res, gx0 + chunkCells);
            int gz0 = tz * chunkCells, gz1 = Mathf.Min(res, gz0 + chunkCells);

            for (int gx = gx0; gx < gx1; gx += Step)
            for (int gz = gz0; gz < gz1; gz += Step)
            {
                if (AppendClump(gx, gz)) placed++;
            }

            if (_verts.Count == 0) continue;          // 这一块没草，不用建网格

            var mesh = new Mesh { name = $"Grass_{tx}_{tz}" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_verts);
            mesh.SetColors(_cols);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject($"Grass_{tx}_{tz}");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        Debug.Log($"[VoxelGrass] 草地细节层完成：{placed} 处（格距 {Step * OpenWorld.Cell:0.#} 米）。");
    }

    /// <summary>这一格要不要长草；长就写进当前块的顶点表</summary>
    static bool AppendClump(int gx, int gz)
    {
        float x = (gx + LowPoly.Hash01(gx, gz, 31)) * OpenWorld.Cell;
        float z = (gz + LowPoly.Hash01(gx, gz, 32)) * OpenWorld.Cell;

        if (!OpenWorld.InBounds(x, z)) return false;

        float y = OpenWorld.Height(x, z);
        if (y < OpenWorld.WaterLevel + 0.9f) return false;                  // 水里 / 沙滩上不长
        if (OpenWorld.Slope(x, z) > 0.20f) return false;                    // 陡坡露岩，不长
        if (OpenWorld.InSiteKeepOut(x, z)) return false;                    // 建筑 / 解密场地占地
        if (OpenWorld.IsRoad(x, z) || OpenWorld.OnTownStreet(x, z)) return false;

        float snow = OpenWorld.SnowFactor(x, z);
        if (snow > 0.30f) return false;                                     // 雪线附近开始没有草

        float liyue  = OpenWorld.LiyueWeight(x, z);
        float autumn = OpenWorld.IsInazumaLand(x, z) ? 1f : 0f;

        // 密度：草原最密，璃月偏干、稻妻秋色各稀一点；林子下面压到一半（树挡了光）
        float density = 0.58f * (1f - 0.42f * liyue) * (1f - 0.30f * autumn);
        if (OpenWorld.ForestDensity(x, z) > 0.62f) density *= 0.5f;
        if (LowPoly.Hash01(gx, gz, 33) > density) return false;

        float k      = LowPoly.Hash01(gx, gz, 34);
        float height = Mathf.Lerp(MinHeight, MaxHeight, k);
        float width  = height * WidthScale;
        float yaw    = LowPoly.Hash01(gx, gz, 35) * Mathf.PI * 2f;

        // 上深下浅：根部深、梢部亮（和地形草地色同一个体系，只是亮一点，看着更透气）
        Color root = Color.Lerp(Color.Lerp(MondRoot, LiyueRoot, liyue), AutumnRoot, autumn);
        Color tip  = Color.Lerp(Color.Lerp(MondTip,  LiyueTip,  liyue), AutumnTip,  autumn);
        float vary = 0.86f + 0.28f * LowPoly.Hash01(gx, gz, 36);
        root *= vary; root.a = 1f;
        tip  *= vary; tip.a  = 1f;

        AppendQuadPair(new Vector3(x, y - 0.05f, z), height, width, yaw, root, tip);
        return true;
    }

    /// <summary>两片交叉的草卡片（正反两组三角形都写，双面各自可见）</summary>
    static void AppendQuadPair(Vector3 basePos, float height, float width, float yaw, Color root, Color tip)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            float angle = yaw + pass * Mathf.PI * 0.5f;
            Vector3 side = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (width * 0.5f);

            int b = _verts.Count;
            _verts.Add(basePos - side);                               _cols.Add(root.linear);
            _verts.Add(basePos + side);                               _cols.Add(root.linear);
            _verts.Add(basePos + side * 0.34f + Vector3.up * height); _cols.Add(tip.linear);
            _verts.Add(basePos - side * 0.34f + Vector3.up * height); _cols.Add(tip.linear);

            _tris.Add(b + 0); _tris.Add(b + 2); _tris.Add(b + 1);
            _tris.Add(b + 0); _tris.Add(b + 3); _tris.Add(b + 2);

            _tris.Add(b + 1); _tris.Add(b + 2); _tris.Add(b + 0);
            _tris.Add(b + 2); _tris.Add(b + 3); _tris.Add(b + 0);
        }
    }
}
