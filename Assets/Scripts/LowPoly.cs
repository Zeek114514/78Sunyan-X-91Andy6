using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 低模零件库：城（MondstadtTown）、雪山、野外建筑（WildBuildings）、解密场地（WildPuzzles）
/// 拼几何都用这一套零件，保证绕序、上色、法线朝向处处一致。
///
/// 每个函数都往调用方传进来的三个列表里写：顶点 / 颜色 / 三角形。
/// 目标子网格由调用方决定 —— 城和野外建筑都按颜色路由到自己的三个子网格（石墙贴图 / 瓦片贴图 / 顶点色）。
///
/// 【绕序警告】这里的面索引是踩过坑调出来的，不要"顺手重写成更整齐的写法"：
/// AppendGable 那组曾经是反的，结果屋顶正面被背面剔除掉 —— 站在外面看不见屋顶，
/// 绕到另一边却能看见屋顶内表面，人一走进屋顶体积就"卡"在屋顶里。
/// 任何改动都要实机站在建筑外面确认屋顶、墙面两侧都正常。
/// </summary>
public static class LowPoly
{
    /// <summary>一个盒子（八顶点，顶面 / 侧面分开上色，法线朝外）</summary>
    public static void Box(List<Vector3> verts, List<Color> cols, List<int> tris,
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

    /// <summary>双坡屋顶（屋脊沿局部 Z 轴）</summary>
    public static void Gable(List<Vector3> verts, List<Color> cols, List<int> tris,
                             Vector3 baseCenter, float width, float depth, float height, Quaternion rot, Color color)
    {
        int baseIndex = verts.Count;
        float hw = width * 0.5f, hd = depth * 0.5f;
        Color c = color.linear;

        Vector3[] v =
        {
            new Vector3(-hw, 0f, -hd), new Vector3( hw, 0f, -hd), new Vector3( hw, 0f,  hd), new Vector3(-hw, 0f,  hd),
            new Vector3(  0f, height, -hd), new Vector3(  0f, height,  hd),
        };
        foreach (var p in v) { verts.Add(baseCenter + rot * p); cols.Add(c); }

        int[] faces = { 0, 4, 1, 2, 5, 3, 0, 5, 4, 0, 3, 5, 1, 5, 2, 1, 4, 5 };
        for (int i = 0; i < faces.Length; i++) tris.Add(baseIndex + faces[i]);
    }

    /// <summary>圆柱 / 圆台（只有侧面）。capTop = true 时补一个顶盖：台阶、基座、平台这类
    /// "看得见顶面"的零件必须带盖子，否则顶面是空的、从上往下能直接看穿。</summary>
    public static void Cylinder(List<Vector3> verts, List<Color> cols, List<int> tris,
                                Vector3 basePos, float height, float bottomR, float topR, int sides, Color color,
                                bool capTop = false, bool capBottom = true)
    {
        int baseIndex = verts.Count;
        Color c = color.linear;

        for (int i = 0; i <= sides; i++)
        {
            float a = (float)i / sides * Mathf.PI * 2f;
            verts.Add(basePos + new Vector3(Mathf.Cos(a) * bottomR, 0f, Mathf.Sin(a) * bottomR));
            cols.Add(c);
        }
        for (int i = 0; i <= sides; i++)
        {
            float a = (float)i / sides * Mathf.PI * 2f;
            verts.Add(basePos + new Vector3(Mathf.Cos(a) * topR, height, Mathf.Sin(a) * topR));
            cols.Add(c);
        }

        for (int i = 0; i < sides; i++)
        {
            int lo0 = baseIndex + i, lo1 = lo0 + 1;
            int hi0 = baseIndex + sides + 1 + i, hi1 = hi0 + 1;
            tris.Add(lo0); tris.Add(hi0); tris.Add(lo1);
            tris.Add(lo1); tris.Add(hi0); tris.Add(hi1);
        }

        // Separate rim vertices keep caps flat after RecalculateNormals.
        void Cap(float y, float radius, bool up)
        {
            if (radius <= 0) return;
            int center = verts.Count;
            verts.Add(basePos + Vector3.up * y); cols.Add(c);
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                verts.Add(basePos + new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius));
                cols.Add(c);
            }
            for (int i = 0; i < sides; i++)
            {
                tris.Add(center);
                tris.Add(center + 1 + i + (up ? 1 : 0));
                tris.Add(center + 1 + i + (up ? 0 : 1));
            }
        }
        if (capTop) Cap(height, topR, true);
        if (capBottom) Cap(0, bottomR, false);
    }

    /// <summary>棱柱（树干 / 塔身）：下粗上细，只有侧面</summary>
    public static void Prism(List<Vector3> verts, List<Color> cols, List<int> tris,
                             Vector3 basePos, float height, float bottomR, float topR, int sides, Color color)
    {
        Cylinder(verts, cols, tris, basePos, height, bottomR, topR, sides, color, true, true);
    }

    /// <summary>低模球（经纬度球）。jitter 是每个顶点上的随机缩放幅度，避免看起来太圆太规整</summary>
    public static void Sphere(List<Vector3> verts, List<Color> cols, List<int> tris,
                              Vector3 center, float radius, Color color, int segments,
                              float squash = 1f, float jitter = 0f, float seed = 0f)
    {
        int rings = Mathf.Max(3, segments / 2);
        int baseIndex = verts.Count;
        Color c = color.linear;

        for (int r = 0; r <= rings; r++)
        {
            float phi = (float)r / rings * Mathf.PI;
            float y = Mathf.Cos(phi) * radius * squash;
            float ring = Mathf.Sin(phi) * radius;

            for (int s = 0; s <= segments; s++)
            {
                float theta = (float)s / segments * Mathf.PI * 2f;
                float k = jitter <= 0f ? 1f
                                       : 0.88f + 0.22f * Hash01(r * 37 + s, Mathf.RoundToInt(seed * 997f), 21);

                verts.Add(center + new Vector3(Mathf.Sin(theta) * ring, y, Mathf.Cos(theta) * ring) * k);
                cols.Add(c);
            }
        }

        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < segments; s++)
            {
                int i0 = baseIndex + r * (segments + 1) + s;
                int i1 = i0 + 1;
                int i2 = i0 + segments + 1;
                int i3 = i2 + 1;

                tris.Add(i0); tris.Add(i2); tris.Add(i1);
                tris.Add(i1); tris.Add(i2); tris.Add(i3);
            }
        }
    }

    /// <summary>圆环 / 圆盘（广场水池、花圃顶、教堂铺地、圆窗都靠它；innerR = 0 就是一个实心圆盘）。
    /// rot 用来把圆盘立起来（法线朝水平方向），比如教堂正面的圆窗。</summary>
    public static void Ring(List<Vector3> verts, List<Color> cols, List<int> tris,
                            Vector3 center, float innerR, float outerR, int sides, Quaternion rot, Color color)
    {
        int baseIndex = verts.Count;
        Color c = color.linear;

        for (int i = 0; i <= sides; i++)
        {
            float a = (float)i / sides * Mathf.PI * 2f;
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            verts.Add(center + rot * new Vector3(ca * innerR, 0f, sa * innerR));
            cols.Add(c);
            verts.Add(center + rot * new Vector3(ca * outerR, 0f, sa * outerR));
            cols.Add(c);
        }

        for (int i = 0; i < sides; i++)
        {
            int i0 = baseIndex + i * 2;
            int i1 = i0 + 1;
            int i2 = i0 + 2;
            int i3 = i0 + 3;
            tris.Add(i0); tris.Add(i2); tris.Add(i1);
            tris.Add(i1); tris.Add(i2); tris.Add(i3);
        }
    }

    /// <summary>把上面这些零件合成一个网格（单子网格，带顶点色的用法）</summary>
    public static Mesh BuildMesh(string name, List<Vector3> verts, List<Color> cols, List<int> tris)
    {
        var mesh = new Mesh { name = name };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>0~1 的确定性哈希（整张地图共用一套，保证每次进游戏布局一样）</summary>
    public static float Hash01(int a, int b, int salt)
    {
        unchecked
        {
            int n = a * 73856093 ^ b * 19349663 ^ salt * 83492791;
            n = (n ^ (n >> 13)) * 1274126177;
            uint u = (uint)(n ^ (n >> 16));
            return u / 4294967295f;
        }
    }
}
