using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 草地细节层：在地形上按格撒"草丛"（每丛 3 片窄卡片，各自随机朝向、微微外倾），
/// 和树石一样在**生成世界时一次性烘进分块网格**，运行期不花任何开销。
///
/// 【外观要点（第一版就是踩在这里：撒出来像一根根竹笋）】
///   · 一丛多片：单片的交叉面片在这个尺度下就是一根尖锥，必须几片凑成"簇"才像草；
///   · 矮而宽：高 0.22~0.42 米、宽是高的约 1.9 倍，顶部只收敛到一半（收太狠就成锥子了）；
///   · 颜色贴地：根部比地面稍深一点、梢部比地面亮一点，整体和地形同一色系，
///     这样它读起来是"草地上的绒毛"，而不是插在地上的深色小物件。
///
/// 只长在该长草的地方：水面附近沙滩、陡坡露岩、路面、城里石板街、野外建筑 / 解密场地占地、
/// 雪线附近都不长；密度按国度分（蒙德草原最密，璃月偏干稀一点，稻妻秋色），林子下面也压稀。
///
/// 性能旋钮（真机掉帧就动这两个）：Step 格距（越小越密）、BladesPerTuft 每丛片数。
/// </summary>
public static class VoxelGrass
{
    const int   Step           = 2;        // 每隔几格撒一丛（Cell ≈ 1.67 米，2 格 ≈ 3.3 米一处）
    const int   BladesPerTuft  = 3;        // 一丛几片卡片
    const float MinHeight      = 0.22f;    // 卡片高度范围（米）
    const float MaxHeight      = 0.42f;
    const float WidthScale     = 1.9f;     // 卡片宽度 = 高度 × 这个系数（宽一点才不像尖锥）
    const float TopTaper       = 0.5f;     // 顶部宽度相对底部的比例
    const float BaseDensity    = 0.72f;    // 基础密度（再按国度 / 林地打折）

    static readonly List<Vector3> _verts = new List<Vector3>();
    static readonly List<Color>   _cols  = new List<Color>();
    static readonly List<int>     _tris  = new List<int>();

    // 地表调色板（和 OpenWorld.MondstadtColor / LiyueColor 同一套：草丛要和地面一个色系）
    static readonly Color MondGrass   = new Color32(0x7f, 0xd0, 0x52, 255);
    static readonly Color MondLight   = new Color32(0xa9, 0xe0, 0x6b, 255);
    static readonly Color LiyueGrass  = new Color32(0x93, 0xbf, 0x5c, 255);
    static readonly Color LiyueLight  = new Color32(0xb2, 0xd6, 0x78, 255);
    static readonly Color AutumnGrass = new Color32(0xc7, 0x8d, 0x62, 255);
    static readonly Color AutumnLight = new Color32(0xdd, 0xac, 0x74, 255);

    /// <summary>撒满全图（由 OpenWorld 在树石之后调用）</summary>
    public static void Build(Transform parent, Material material)
    {
        int res = Mathf.RoundToInt(OpenWorld.Size / OpenWorld.Cell);
        int chunkCells = OpenWorld.ChunkCells;
        int tufts = 0, blades = 0;

        for (int tx = 0; tx < OpenWorld.TerrainChunks; tx++)
        for (int tz = 0; tz < OpenWorld.TerrainChunks; tz++)
        {
            _verts.Clear(); _cols.Clear(); _tris.Clear();

            int gx0 = tx * chunkCells, gx1 = Mathf.Min(res, gx0 + chunkCells);
            int gz0 = tz * chunkCells, gz1 = Mathf.Min(res, gz0 + chunkCells);

            for (int gx = gx0; gx < gx1; gx += Step)
            for (int gz = gz0; gz < gz1; gz += Step)
            {
                if (AppendTuft(gx, gz)) { tufts++; blades += BladesPerTuft; }
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

        Debug.Log($"[VoxelGrass] 草地细节层完成：{tufts} 丛 / {blades} 片（格距 {Step * OpenWorld.Cell:0.#} 米）。");
    }

    /// <summary>这一格要不要长草；长就写进当前块的顶点表</summary>
    static bool AppendTuft(int gx, int gz)
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

        float density = BaseDensity * (1f - 0.40f * liyue) * (1f - 0.30f * autumn);
        if (OpenWorld.ForestDensity(x, z) > 0.62f) density *= 0.55f;         // 林下压稀（树挡了光）
        if (LowPoly.Hash01(gx, gz, 33) > density) return false;

        // 根部稍深、梢部稍亮：整体就是地面那一套绿，只是多了明暗层次
        Color grass = Color.Lerp(Color.Lerp(MondGrass, LiyueGrass, liyue), AutumnGrass, autumn);
        Color light = Color.Lerp(Color.Lerp(MondLight, LiyueLight, liyue), AutumnLight, autumn);
        Color root  = grass * 0.88f;
        Color tip   = Color.Lerp(grass, light, 0.75f);
        root.a = 1f; tip.a = 1f;

        for (int i = 0; i < BladesPerTuft; i++)
        {
            int salt = 40 + i * 4;
            float ox = (LowPoly.Hash01(gx, gz, salt)     - 0.5f) * 0.9f;     // 丛内偏移（米）
            float oz = (LowPoly.Hash01(gx, gz, salt + 1) - 0.5f) * 0.9f;
            float k  = LowPoly.Hash01(gx, gz, salt + 2);
            float height = Mathf.Lerp(MinHeight, MaxHeight, k);
            float yaw    = LowPoly.Hash01(gx, gz, salt + 3) * Mathf.PI * 2f;

            float bx = x + ox, bz = z + oz;
            float by = OpenWorld.Height(bx, bz) - 0.03f;                     // 每片各自贴地，坡上不悬空

            AppendBlade(new Vector3(bx, by, bz), height, yaw, root, tip,
                        0.92f + 0.16f * LowPoly.Hash01(gx, gz, 60 + i));
        }

        return true;
    }

    /// <summary>一片草叶：底宽顶窄的四边形，正反两组三角形都写（单面片会被背面剔除）</summary>
    static void AppendBlade(Vector3 basePos, float height, float yaw, Color root, Color tip, float tint)
    {
        float width = height * WidthScale;
        Vector3 side = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * (width * 0.5f);
        Vector3 lean = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * (height * 0.18f);   // 微微外倾

        Color r = root * tint; r.a = 1f;
        Color t = tip  * tint; t.a = 1f;

        int b = _verts.Count;
        _verts.Add(basePos - side);                                            _cols.Add(r.linear);
        _verts.Add(basePos + side);                                            _cols.Add(r.linear);
        _verts.Add(basePos + side * TopTaper + Vector3.up * height + lean);    _cols.Add(t.linear);
        _verts.Add(basePos - side * TopTaper + Vector3.up * height + lean);    _cols.Add(t.linear);

        _tris.Add(b + 0); _tris.Add(b + 2); _tris.Add(b + 1);
        _tris.Add(b + 0); _tris.Add(b + 3); _tris.Add(b + 2);

        _tris.Add(b + 1); _tris.Add(b + 2); _tris.Add(b + 0);
        _tris.Add(b + 2); _tris.Add(b + 3); _tris.Add(b + 0);
    }
}
