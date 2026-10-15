using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 璃月解密场地的外观（阶段 F）：**石灯阵** 和 **石鼓**。
///
/// 【为什么是 LiyueBuildings 的 partial】
/// 和港城同一个理由：要复用石灰岩 / 朱红 / 金 / 黛瓦这套配色、Box / Cyl / Prism / ChineseRoof / Lantern
/// 这些零件，它们都是 LiyueBuildings 的私有成员。做法也一致：静态几何合进 LiyueBuildings 的缓冲区
/// （调用方 Begin / BuildMesh），可交互的节点另走"单子网格 + 顶点色"的独立通道。
///
/// 【和蒙德那套的关系】
/// 玩法一模一样（见 PuzzleSite）：走到跟前按 E 点亮、全亮即解；顺序模式对照入口石刻；守护模式冒出守卫。
/// 变的只有外形、称呼和石台尺寸：
///   · 石灯阵 —— 一圈石灯，灯罩亮起来就是"点亮"；灯罩是四根小柱撑开的，从四面都能看见灯色
///   · 石鼓   —— 一圈石鼓，鼓面上那方印记亮起来就是"点亮"（矮，走近了正好在视线上）
///   · 维度都是"暗的时候也必须看得清颜色"，否则顺序模式没法解
///
/// ★ 石台尺寸必须和运行时（PuzzleSite）用同一套 —— 灯 / 鼓要正好落在石台圈上，
///   所以尺寸只在这里定义，运行时通过 WildPuzzles.PlatformRadius(kind, style) 取。
/// </summary>
public static partial class LiyueBuildings
{
    /// <summary>璃月石台半径（比蒙德那套略大：石灯 / 石鼓比方碑占地方）</summary>
    public static float PuzzlePlatformRadius(PuzzleKind kind)
    {
        switch (kind)
        {
            case PuzzleKind.AllLit:  return 8.0f;
            case PuzzleKind.Ordered: return 9.0f;
            default:                 return 10.0f;      // Guarded：还要站得下守卫
        }
    }

    /// <summary>场地名字（石灯阵 / 石鼓 + 玩法）</summary>
    public static string PuzzleSiteName(PuzzleKind kind, PuzzleStyle style)
    {
        string baseName = style == PuzzleStyle.LiyueDrum ? "石鼓" : "石灯阵";
        switch (kind)
        {
            case PuzzleKind.Ordered: return baseName + " · 顺序";
            case PuzzleKind.Guarded: return baseName + " · 守护";
            default:                 return baseName;
        }
    }

    // ================= 静态几何（合进 LiyueBuildings 的缓冲区） =================

    /// <summary>把一处璃月解密场地铺进缓冲区。order 为 null 就不刻顺序提示（简单 / 守护模式）</summary>
    public static void AppendPuzzleGround(PuzzleKind kind, PuzzleStyle style, Vector3 center, float yaw, int[] order)
    {
        float R = PuzzlePlatformRadius(kind);
        var rot = Quaternion.Euler(0f, yaw, 0f);
        int seed = (int)(center.x * 13f + center.z * 29f);

        // 台基 + 铺地：外圈毛石压边，里圈打磨过的石灰岩，中央一圈深色石
        Cyl(center + Vector3.up * 0.02f, 0.34f, R + 1.2f, R + 1.0f, 24, LimestoneDark, true);
        Cyl(center + Vector3.up * 0.36f, 0.30f, R, R - 0.1f, 24, Limestone, true);
        Disc(center + Vector3.up * 0.67f, 0f, R - 1.0f, 24, Quaternion.identity, Limestone);
        Disc(center + Vector3.up * 0.70f, 0f, 3.4f, 18, Quaternion.identity, LimestoneDark);
        Disc(center + Vector3.up * 0.72f, 0f, 2.2f, 18, Quaternion.identity, Tile);          // 中心一圈黛瓦色

        // 入口台阶（朝入口）
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = center + rot * new Vector3(0f, 0f, R + 1.9f + i * 1.1f);
            Box(p + Vector3.up * (0.12f - i * 0.03f), new Vector3(5.2f - i * 0.6f, 0.26f, 1.2f), rot,
                Limestone, LimestoneDark);
        }

        // 入口两侧的石火盆（一直亮着，只是氛围；火苗落地时单独挂）
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 p = center + rot * new Vector3(s * 2.6f, 0f, R + 1.5f);
            Cyl(p, 0.5f, 0.5f, 0.42f, 8, LimestoneDark, true);
            Cyl(p + Vector3.up * 0.5f, 1.1f, 0.24f, 0.3f, 8, Limestone, true);
            Cyl(p + Vector3.up * 1.6f, 0.34f, 0.46f, 0.4f, 8, Gold, true);
            Flames.Add(p + Vector3.up * 1.98f);
        }

        // 四角矮石柱（经幢）：顶上压一块金，纯装饰
        for (int i = 0; i < 4; i++)
        {
            float a = (yaw + 45f + i * 90f) * Mathf.Deg2Rad;
            Vector3 p = center + new Vector3(Mathf.Cos(a) * (R - 1.5f), 0f, Mathf.Sin(a) * (R - 1.5f));
            var pr = Quaternion.Euler(0f, yaw + 45f + i * 90f, 0f);

            Box(p + Vector3.up * 0.18f, new Vector3(0.9f, 0.36f, 0.9f), pr, LimestoneDark, LimestoneDark);
            Cyl(p + Vector3.up * 0.36f, 1.5f, 0.26f, 0.22f, 6, Limestone, true);
            Box(p + Vector3.up * 1.98f, new Vector3(0.62f, 0.26f, 0.62f), pr, Gold, Gold);
        }

        // 中央机关台（尺寸和蒙德那套完全一致：宝箱位置 WildPuzzles.RewardPos 靠它）
        Box(center + Vector3.up * 0.86f, new Vector3(2.5f, 0.42f, 2.5f), rot, Limestone, LimestoneDark);
        Box(center + Vector3.up * 1.14f, new Vector3(2.0f, 0.2f, 2.0f), rot, LimestoneDark, LimestoneDark);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            Vector3 p = center + new Vector3(Mathf.Cos(a) * 1.65f, 0f, Mathf.Sin(a) * 1.65f);
            Cyl(p + Vector3.up * 0.67f, 0.72f, 0.24f, 0.18f, 6, Limestone, true);
            Ball(p + Vector3.up * 1.52f, 0.2f, Gold, 5, 0.8f, 0f);
        }

        // 提示：顺序模式是"石刻时辰碑"—— 碑面从上到下四块小印记，颜色就是要求的顺序
        if (order != null)
        {
            Vector3 p = center + rot * new Vector3(3.9f, 0f, R - 0.6f);
            var sRot = rot * Quaternion.Euler(0f, -14f, 0f);

            Box(p + Vector3.up * 0.26f, new Vector3(2.2f, 0.52f, 1.15f), sRot, LimestoneDark, LimestoneDark);
            Box(p + Vector3.up * 2.05f, new Vector3(1.75f, 3.5f, 0.6f), sRot, Limestone, LimestoneDark);
            ChineseRoof(p + Vector3.up * 3.8f, 2.5f, 1.6f, 0.85f, 0.32f, sRot, Tile, Ridge);

            for (int i = 0; i < order.Length; i++)
            {
                float y = 3.25f - i * 0.8f;
                Box(p + sRot * new Vector3(0f, y, 0.34f), new Vector3(1.15f, 0.12f, 0.1f), sRot, Gold, Gold);
                Ball(p + sRot * new Vector3(0f, y - 0.33f, 0.37f), 0.22f, WildPuzzles.GemColors[order[i]], 6, 0.85f, 0f);
            }

            // 碑侧一盏红灯笼，一眼看出是"这里的规矩"
            Lantern(p + sRot * new Vector3(1.35f, 3.5f, 0f), 0.34f, LanternRed, Gold);
        }

        // 台面外围散几块碎石（和蒙德那套一样的手法，别让石台显得太干净）
        int rocks = 5 + (int)(H((int)center.x, (int)center.z, 942) * 4f);
        for (int i = 0; i < rocks; i++)
        {
            float a = H(i, seed, 943) * Mathf.PI * 2f;
            float r = R + 0.4f + H(i, seed, 944) * 1.4f;
            float s = 0.5f + H(i, seed, 945) * 1.0f;
            Box(center + new Vector3(Mathf.Cos(a) * r, 0.35f, Mathf.Sin(a) * r),
                new Vector3(s, s * 0.7f, s * 0.9f),
                Quaternion.Euler(H(i, seed, 946) * 26f, H(i, seed, 947) * 360f, 0f), Limestone, LimestoneDark);
        }
    }

    // ================= 可交互节点（独立 GameObject：单子网格 + 顶点色） =================

    static Mesh _lampLit, _lampDim, _drumLit, _drumDim;
    static readonly Mesh[] _glowLit = new Mesh[4];
    static readonly Mesh[] _glowDim = new Mesh[4];

    static readonly List<Vector3> _zv = new List<Vector3>();
    static readonly List<Color>   _zc = new List<Color>();
    static readonly List<int>     _zt = new List<int>();

    /// <summary>节点本体网格：石灯（高，灯罩看得见）或石鼓（矮，鼓面印记看得见）。
    /// 亮 / 暗各只建一次，全场共用</summary>
    public static Mesh PuzzleNodeMesh(PuzzleStyle style, bool lit)
    {
        bool drum = style == PuzzleStyle.LiyueDrum;
        if (drum && lit && _drumLit != null) return _drumLit;
        if (drum && !lit && _drumDim != null) return _drumDim;
        if (!drum && lit && _lampLit != null) return _lampLit;
        if (!drum && !lit && _lampDim != null) return _lampDim;

        _zv.Clear(); _zc.Clear(); _zt.Clear();

        if (drum) BuildDrumBody(lit); else BuildLampBody(lit);

        var mesh = LowPoly.BuildMesh((drum ? "PuzzleDrum" : "PuzzleLamp") + (lit ? "Lit" : "Dim"), _zv, _zc, _zt);

        if (drum) { if (lit) _drumLit = mesh; else _drumDim = mesh; }
        else      { if (lit) _lampLit = mesh; else _lampDim = mesh; }

        return mesh;
    }

    /// <summary>石灯：须弥座 + 柱身 + 四柱撑开的灯罩（灯罩是"那点身份色"，四面都看得见）+ 攒尖顶。
    /// 亮的时候柱身刻纹和顶珠转成金色</summary>
    static void BuildLampBody(bool lit)
    {
        Color body = lit ? Limestone : LimestoneDark;
        Color trim = lit ? Gold : LimestoneDark;
        Color roof = lit ? Tile : TileDark;

        // 须弥座 + 柱身
        LowPoly.Box(_zv, _zc, _zt, new Vector3(0f, 0.22f, 0f), new Vector3(1.5f, 0.44f, 1.5f),
                    Quaternion.identity, LimestoneDark, LimestoneDark);
        LowPoly.Cylinder(_zv, _zc, _zt, Vector3.up * 0.44f, 1.5f, 0.34f, 0.3f, 8, body);

        // 柱身刻纹（亮起来是金箍）
        for (int i = 0; i < 2; i++)
            LowPoly.Box(_zv, _zc, _zt, new Vector3(0f, 0.85f + i * 0.45f, 0f), new Vector3(0.72f, 0.09f, 0.72f),
                        Quaternion.identity, trim, trim);

        // 灯罩：四根小柱撑开（中间空着，灯色从四面都看得到）
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                LowPoly.Box(_zv, _zc, _zt, new Vector3(sx * 0.42f, 2.42f, sz * 0.42f),
                            new Vector3(0.16f, 1.0f, 0.16f), Quaternion.identity, Limestone, LimestoneDark);

        // 灯罩底盘 + 腰檐 + 攒尖顶 + 顶珠
        LowPoly.Box(_zv, _zc, _zt, new Vector3(0f, 1.94f, 0f), new Vector3(1.06f, 0.12f, 1.06f),
                    Quaternion.identity, LimestoneDark, LimestoneDark);
        LowPoly.Box(_zv, _zc, _zt, new Vector3(0f, 2.94f, 0f), new Vector3(1.4f, 0.18f, 1.4f),
                    Quaternion.identity, trim, trim);
        LowPoly.Prism(_zv, _zc, _zt, Vector3.up * 3.03f, 0.82f, 0.88f, 0.12f, 6, roof);
        LowPoly.Sphere(_zv, _zc, _zt, Vector3.up * 4.02f, 0.17f, trim, 6);
    }

    /// <summary>石鼓：方座 + 鼓身（十二棱）+ 两道金箍 + 鼓面边箍。鼓面上的印记（身份色）另挂</summary>
    static void BuildDrumBody(bool lit)
    {
        Color body = lit ? Limestone : LimestoneDark;
        Color trim = lit ? Gold : LimestoneDark;

        // 方座
        LowPoly.Box(_zv, _zc, _zt, new Vector3(0f, 0.18f, 0f), new Vector3(2.4f, 0.36f, 2.4f),
                    Quaternion.identity, LimestoneDark, LimestoneDark);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            LowPoly.Box(_zv, _zc, _zt, new Vector3(Mathf.Cos(a) * 1.0f, 0.42f, Mathf.Sin(a) * 1.0f),
                        new Vector3(0.5f, 0.16f, 0.5f), Quaternion.identity, Gold, Gold);
        }

        // 鼓身 + 两道箍 + 鼓面边箍
        LowPoly.Cylinder(_zv, _zc, _zt, Vector3.up * 0.36f, 1.0f, 1.02f, 1.02f, 12, body);
        for (int i = 0; i < 2; i++)
            LowPoly.Cylinder(_zv, _zc, _zt, Vector3.up * (0.56f + i * 0.46f), 0.1f, 1.06f, 1.06f, 12, trim);
        LowPoly.Cylinder(_zv, _zc, _zt, Vector3.up * 1.36f, 0.16f, 1.08f, 1.04f, 12, trim, true);
    }

    /// <summary>节点上那点"身份色"：石灯的灯罩（一个椭球，挂在灯罩中间）或石鼓鼓面上的印记。
    /// 暗的时候压暗但仍然看得出是哪种颜色 —— 顺序模式全靠它</summary>
    public static Mesh PuzzleGlowMesh(PuzzleStyle style, int colorIndex, bool lit)
    {
        colorIndex = Mathf.Clamp(colorIndex, 0, WildPuzzles.GemColors.Length - 1);
        var cache = lit ? _glowLit : _glowDim;
        if (cache[colorIndex] != null) return cache[colorIndex];

        Color c = WildPuzzles.GemColors[colorIndex];
        if (!lit) c = Color.Lerp(c, new Color32(0x74, 0x6f, 0x66, 255), 0.6f);

        _zv.Clear(); _zc.Clear(); _zt.Clear();

        if (style == PuzzleStyle.LiyueDrum)
            LowPoly.Box(_zv, _zc, _zt, new Vector3(0f, 1.44f, 0f), new Vector3(1.06f, 0.26f, 1.06f),
                        Quaternion.identity, c, c);
        else
            LowPoly.Sphere(_zv, _zc, _zt, new Vector3(0f, 2.44f, 0f), 0.4f, c, 7, 1.15f, 0f);

        var mesh = LowPoly.BuildMesh("PuzzleGlow" + (int)style + colorIndex + (lit ? "Lit" : "Dim"), _zv, _zc, _zt);
        cache[colorIndex] = mesh;
        return mesh;
    }
}
