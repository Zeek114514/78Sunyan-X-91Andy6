using UnityEngine;

/// <summary>
/// 「Andy的港门」：璃月的主城，一座依山面海的大港（大港版）。
///
/// 这个文件分两半：
///   · 地形（阶段 B）：把港口那一片压平成码头台地，再挖出内湾 / 外湾 —— 外湾一直铺到地图东南边缘，
///     看起来就是通向外海
///   · 几何与布局（阶段 E）：主码头、四层街道、吊脚楼群、楼阁、牌坊、岩神像广场、钟楼、灯塔
///     —— 代码在 <c>LiyueHarborCity.cs</c>（LiyueBuildings 的 partial，为了复用那四个中式零件）
///
/// 尺寸常量全放这里：HeightRaw（地形）和布局用的**必须是同一套数字**，否则城会建到坡上去。
/// </summary>
public static class LiyueHarbor
{
    // ---------- 位置（璃月东南角，靠着出海口） ----------

    public const float HarborX = 1100f, HarborZ = 1010f;   // 城心 = 码头台地中心
    public const float QuayR   = 150f;                     // 台地平整半径（大港版：160 米级）
    public const float QuayY   = 13f;                      // 码头地面高度
                                                          // ★ 跟着基底地形一起抬上来的：新基底的璃月侧是
                                                          //   14±7.6 米，台地还压在 6.5 的话，整座城会变成
                                                          //   一个陷进地里的坑（四周比城高 7 米）
    public const float QuayFalloff = 30f;                  // 台地边缘往外多少米落到自然地形

    public const float InnerBayX = 1300f, InnerBayZ = 1150f, InnerBayR = 105f;   // 内湾（吊脚楼围着它）
    public const float BayX      = 1350f, BayZ      = 1300f, BayR      = 240f;   // 外湾（一直铺到地图东南边缘）

    /// <summary>
    /// 璃月港的地形：先压平台地，再挖内外双湾。
    ///
    /// ★ 顺序很重要：台地在前、挖湾在后 —— 这样湾会把台地的东南边缘"啃"掉，形成水面线。
    ///   反过来台地会把挖好的湾填平。lw = 璃月权重（蒙德侧为 0，直接原样返回）
    /// </summary>
    public static float ShapeTerrain(float h, float x, float z, float lw)
    {
        if (lw <= 0.001f) return h;

        // ① 码头台地（和蒙德城岛同一套写法：压到固定高度、边缘平滑落下）
        float quay = CircleMask(x, z, HarborX, HarborZ, QuayR, QuayFalloff);
        h = Mathf.Lerp(h, QuayY, quay * lw);

        // ② 内湾：挖到水面以下 5 米，吊脚楼就探在它上面
        float inner = CircleMask(x, z, InnerBayX, InnerBayZ, InnerBayR, 60f);
        h = Mathf.Lerp(h, -5f, inner * lw);

        // ③ 外湾：更深更大；影响范围一直铺到地图东南边缘，所以那边整片是海水
        //    —— 视觉上就是"港口外面是外海"
        //
        // ★ 过渡带从 130 收到 60：130 米的过渡带意味着距湾心 240~370 米都是浅滩，
        //   而城心离湾心只有 383 米 —— 浅水一直啃到离城心 50 米的地方，
        //   港城那 150 米半径的台地有一大半被泡在水里（阶段 E 摆四层街道时才发现的）。
        //   收到 60 米后岸线退回台地边缘（约 100 米处），外湾依旧是"铺到地图东南边缘"的外海。
        float outer = CircleMask(x, z, BayX, BayZ, BayR, 60f);
        h = Mathf.Lerp(h, -9f, outer * lw);

        return h;
    }

    /// <summary>这一点在不在港城范围内。别的生成器（怪物 / 宝箱）要整片让开：
    /// 城里是人工台地，按"地形高度"摆的东西会埋进台面里或悬在半空</summary>
    public static bool InCity(float x, float z) => NearCity(x, z, 20f);

    /// <summary>离城心比"台地半径 + extra"还近就算在城里（璃月解密场地要留更大余量）</summary>
    public static bool NearCity(float x, float z, float extra)
    {
        float dx = x - HarborX, dz = z - HarborZ;
        float r = QuayR + extra;
        return dx * dx + dz * dz < r * r;
    }

    /// <summary>圆心 mask：半径 r 以内是 1，往外 falloff 米平滑收到 0</summary>
    static float CircleMask(float x, float z, float cx, float cz, float r, float falloff)
    {
        float dx = x - cx, dz = z - cz;
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        float t = Mathf.Clamp01(1f - (d - r) / falloff);
        return t * t * (3f - 2f * t);
    }
}
