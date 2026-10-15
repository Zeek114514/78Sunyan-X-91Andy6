using UnityEngine;

/// <summary>
/// 稻妻（岛国）：外海 + 群岛的地形。
///
/// 【地形怎么加进来的】
///   和 <see cref="LiyueHarbor"/> 一个套路：地形主体仍在 OpenWorld.HeightRaw 里，
///   这个文件只负责"稻妻那一片长什么样"，由 HeightRaw 末尾调用一次 <see cref="ShapeTerrain"/>。
///   好处是地形顺序（基底 → 雪顶 → 璃月 → 稻妻）一目了然，改稻妻不会碰到蒙德 / 璃月。
///
/// 【地理】
///   地图从 1392 扩到 1800 之后，旧大陆（原来那 1392 见方）之外全是海 —— 外海从旧大陆边缘
///   平滑沉到水面以下，所以璃月港那个朝向东南的外湾很自然地连进大洋。
///   稻妻的群岛就在东南海面上：主岛「鸣神岛」（北面一道山、岛心将军府高台、西岸一个港口小湾）
///   + 两座离岛（神社岛、岩柱岛）。
///
/// 【三层判据，各有各的用处】
///   · <see cref="RegionWeight"/>：**稻妻这一整片海区**（含海）的权重，纯解析、不吃噪声 ——
///     给"这块地归哪国"用（LiyueWeight / IsMondstadtLand 都拿它把稻妻划走），每次采样都要问，必须便宜。
///   · <see cref="LandWeight"/>：**岛上陆地**的权重（海岸线附近是 0，留给沙滩和码头）——
///     给建筑选点、植被、地表配色用。
///   · <see cref="SeaMask"/>：外海（旧大陆以外）的权重，地形下沉用它。
/// </summary>
public static class InazumaIsles
{
    // ---------- 外海 ----------
    // 旧大陆是 0~1392 那块方地，所以用 max(x,z) 判"出没出大陆"：出得太远就是深水区。
    // 内沿压在 1400（旧大陆边缘一点没动），外沿 1520 —— 中间那 120 米是环大陆的浅海 + 滩涂。
    const float SeaInner = 1400f, SeaOuter = 1520f;
    const float SeaFloor = -8f;            // 外海海底（水面 3.2 米，所以有 11 米水深）

    // ---------- 群岛 ----------
    public const float MainX = 1600f, MainZ = 1565f;       // 主岛（鸣神岛）中心
    public const float MainR = 195f;                       // 岛的名义半径（岸边还有噪声抖动）

    public const float CastleX = 1622f, CastleZ = 1592f;   // 将军府（天守）高台中心
    public const float CastleY = 33f, CastlePadR = 46f;    // 台面高度 / 平整半径

    public const float PortX = 1462f, PortZ = 1585f;       // 港口（主岛西岸的小湾），朝着大陆那侧
    public const float ShrineX = 1408f, ShrineZ = 1470f;   // 离岛 · 神社
    public const float SpireX = 1730f, SpireZ = 1408f;     // 离岛 · 岩柱（五重塔在那）

    /// <summary>主岛的地标（给选点 / 地图 / 锚点用）</summary>
    public static Vector2 MainIsle => new Vector2(MainX, MainZ);
    public static Vector2 CastlePos => new Vector2(CastleX, CastleZ);
    public static Vector2 PortPos => new Vector2(PortX, PortZ);
    public static Vector2 ShrineIsle => new Vector2(ShrineX, ShrineZ);
    public static Vector2 SpireIsle => new Vector2(SpireX, SpireZ);

    /// <summary> archipelago 大致的活动范围（编辑器自检抽样、怪物分布都用它）</summary>
    public const float RegionX0 = 1330f, RegionZ0 = 1330f, RegionX1 = 1800f, RegionZ1 = 1800f;

    // ================= 权重 =================

    /// <summary>稻妻海区权重（含海）：1 = 稻妻这一片，0 = 旧大陆。
    /// 纯解析（一个大圆 + 平滑边），不吃 Perlin —— 每次采样都要问它</summary>
    public static float RegionWeight(float x, float z)
    {
        // 以群岛中心画一个大圆覆盖整个群岛 + 一圈海
        const float cx = 1560f, cz = 1560f, r = 300f;
        float dx = x - cx, dz = z - cz;
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r + 90f, r - 40f, d));
    }

    /// <summary>外海权重：旧大陆之外（max(x,z) 越大越深）。和璃月外湾自然衔接</summary>
    public static float SeaMask(float x, float z) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SeaInner, SeaOuter, Mathf.Max(x, z)));

    /// <summary>岛上陆地权重：岛内 1、海里 0，海岸线一带压到 0（留给沙滩 / 码头）。
    /// 建筑选点、植被、地表配色都按它判</summary>
    public static float LandWeight(float x, float z)
    {
        float mask = IslandMask(x, z, out float inland);
        return Mathf.Clamp01(mask * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.16f, 0.44f, inland)));
    }

    // ================= 地形 =================

    /// <summary>稻妻的地形：先沉外海、再把群岛顶起来、最后整平将军府高台。
    /// ★ 顺序和璃月港一样关键：海在前、岛在后 —— 反过来岛会被海淹掉</summary>
    public static float ShapeTerrain(float h, float x, float z)
    {
        float sea = SeaMask(x, z);
        float mask = IslandMask(x, z, out float inland);

        if (sea > 0.001f || mask > 0.001f)
        {
            float hIsland = IsletHeight(x, z, inland, mask);

            h = Mathf.Lerp(h, SeaFloor, sea);      // ① 沉到海里
            h = Mathf.Lerp(h, hIsland, mask);      // ② 把岛顶起来
        }

        // ③ 将军府高台：整平成一块台地（和蒙德城岛、璃月码头台地同一个套路）
        float cd = Vector2.Distance(new Vector2(x, z), CastlePos);
        float pad = Mathf.Clamp01(1f - (cd - CastlePadR) / 30f);
        if (pad > 0f) h = Mathf.Lerp(h, CastleY, pad);

        // ④ 港口小湾：西岸挖一块能停船的水面（湾底 1.2 米，比水面低 2 米）
        h = Mathf.Lerp(h, 1.2f, Cove(x, z, PortX, PortZ, 46f, 30f));

        return h;
    }

    /// <summary>三座岛的着陆遮罩合在一起：返回"上岸权重"，inland = 从岸边到岛心的归一距离（0 岸 / 1 心）。
    /// 谁的面大听谁的 —— 岛和岛不叠，所以取 max 就够</summary>
    static float IslandMask(float x, float z, out float inland)
    {
        float m1 = OneIsland(x, z, MainX, MainZ, MainR, 30f, out float in1);
        float m2 = OneIsland(x, z, ShrineX, ShrineZ, 74f, 15f, out float in2);
        float m3 = OneIsland(x, z, SpireX, SpireZ, 62f, 13f, out float in3);

        if (m1 >= m2 && m1 >= m3) { inland = in1; return m1; }
        if (m2 >= m3) { inland = in2; return m2; }
        inland = in3;
        return m3;
    }

    /// <summary>一座岛的遮罩：圆 + 两层"按角度采样的噪声"把岸边揉出湾和岬。
    /// ★ 噪声用 (cosθ, sinθ) 当采样坐标 —— θ 在 ±π 处会跳，直接拿 θ 采会留下一道缝</summary>
    static float OneIsland(float x, float z, float cx, float cz, float r, float coast, out float inland)
    {
        float dx = x - cx, dz = z - cz;
        float d = Mathf.Sqrt(dx * dx + dz * dz);

        float ang = Mathf.Atan2(dz, dx);
        float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
        float wob = (Mathf.PerlinNoise(ca * 1.6f + cx * 0.013f, sa * 1.6f + cz * 0.013f) - 0.5f) * 0.34f
                  + (Mathf.PerlinNoise(ca * 4.3f + 11.3f, sa * 4.3f + 7.7f) - 0.5f) * 0.16f;

        float rr = Mathf.Max(r * (1f + wob), 8f);
        inland = Mathf.Clamp01(1f - d / rr);

        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rr + coast, rr - coast * 0.45f, d));
    }

    /// <summary>岛上的地面高度：海边 4.5 米左右的滩，往岛心抬到 20~26 米，
    /// 主岛北面再叠一道山脊（鸣神峰），最后加两层噪声</summary>
    static float IsletHeight(float x, float z, float inland, float mask)
    {
        float shore = 4.2f + Mathf.SmoothStep(0f, 1f, inland) * 22f;

        // 主岛：北面一道山脊（只有主岛那一片 inland 才大，所以只对主岛生效）
        float dMain = Vector2.Distance(new Vector2(x, z), MainIsle);
        float mainPart = Mathf.Clamp01(1f - (dMain - MainR) / 60f);
        if (mainPart > 0.01f)
        {
            float ridge = Mathf.Max(
                Ridge(x, z, new Vector2(1544f, 1626f), new Vector2(1652f, 1662f), 96f, 34f),
                Ridge(x, z, new Vector2(1652f, 1662f), new Vector2(1706f, 1608f), 82f, 24f));
            // 山脚也压在岛上：乘 mask 免得山脊伸到海里
            shore += ridge * mainPart;
        }

        // 岩柱岛：中间一根高岩柱（五重塔就立在上面）
        float dSpire = Vector2.Distance(new Vector2(x, z), SpireIsle);
        shore += Mathf.Clamp01(1f - dSpire / 34f) * 20f;

        // 两层噪声：低频起伏 + 高频碎石（岛内才生效，海边保持滩的平缓）
        float n = (Mathf.PerlinNoise(x * 0.011f, z * 0.011f) - 0.5f) * 9f
                + (Mathf.PerlinNoise(x * 0.047f + 3.1f, z * 0.047f + 8.4f) - 0.5f) * 2.4f;

        return shore + n * Mathf.Clamp01(inland * 1.4f);
    }

    /// <summary>港口小湾：一个平滑的圆形下凹（挖出能停船的水面）</summary>
    static float Cove(float x, float z, float cx, float cz, float r, float falloff)
    {
        float d = Vector2.Distance(new Vector2(x, z), new Vector2(cx, cz));
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r + falloff, r * 0.4f, d));
    }

    /// <summary>山脊（和雪顶那套同一个形状）：到线段 ab 的距离决定高度</summary>
    static float Ridge(float x, float z, Vector2 a, Vector2 b, float halfWidth, float height)
    {
        float dx = b.x - a.x, dz = b.y - a.y;
        float len2 = dx * dx + dz * dz;
        float t = len2 < 0.001f ? 0f : Mathf.Clamp01(((x - a.x) * dx + (z - a.y) * dz) / len2);

        float px = a.x + dx * t, pz = a.y + dz * t;
        float d = Mathf.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
        if (d >= halfWidth) return 0f;

        return height * Mathf.Pow(1f - d / halfWidth, 1.5f);
    }

    // ================= 地表配色 =================

    /// <summary>稻妻的地表配色：苔绿 + 火山岩灰 + 白沙 + 秋叶色斑。
    /// 和蒙德的饱和草原绿、璃月的黄褐土都拉开 —— 一眼能看出上了岛</summary>
    public static Color LandColor(float x, float z, float y, float slope)
    {
        Color sand      = new Color32(0xea, 0xdf, 0xbd, 255);   // 白沙
        Color sandDark  = new Color32(0xd2, 0xc4, 0x9e, 255);
        Color mossLight = new Color32(0x9c, 0xc0, 0x6a, 255);   // 苔绿（亮）
        Color moss      = new Color32(0x74, 0xa5, 0x53, 255);   // 苔绿
        Color autumn    = new Color32(0xc7, 0x8d, 0x62, 255);   // 秋叶 / 枯草
        Color rockLight = new Color32(0x94, 0x8e, 0x88, 255);   // 火山岩（亮）
        Color rock      = new Color32(0x6d, 0x68, 0x64, 255);   // 火山岩

        Color c;

        if (y < OpenWorld.WaterLevel + 4.5f)                         // 海边：白沙（噪声抖一下边界）
        {
            float t = Mathf.InverseLerp(OpenWorld.WaterLevel - 0.8f, OpenWorld.WaterLevel + 4.5f, y);
            float n = Mathf.PerlinNoise(x * 0.05f, z * 0.05f);
            c = Color.Lerp(sandDark, Color.Lerp(sand, moss, 0.55f), Mathf.Clamp01(t * (0.7f + n * 0.6f)));
        }
        else if (slope > 0.26f)                                      // 陡面：火山岩
            c = Color.Lerp(rock, rockLight, Mathf.PerlinNoise(x * 0.09f, z * 0.09f));
        else
        {
            float n = Mathf.PerlinNoise(x * 0.05f, z * 0.05f);
            c = Color.Lerp(moss, mossLight, n);

            // 秋色斑：低频噪声选中的地方露暖色（岛上的红叶 / 芒草）
            float dry = Mathf.SmoothStep(0.55f, 0.85f, Mathf.PerlinNoise(x * 0.017f + 4.1f, z * 0.017f + 9.2f));
            c = Color.Lerp(c, autumn, dry * 0.6f);
        }

        return c;
    }
}
