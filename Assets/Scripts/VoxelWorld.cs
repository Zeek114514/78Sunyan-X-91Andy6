using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 体素世界：地形数据 + 只生成“裸露面”的网格 + 碰撞体。
/// 顶点色里同时烘焙了方块颜色、六个面的固定明暗、以及每格轻微色差，
/// 所以着色器不需要参与光照也能有体积感。
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class VoxelWorld : MonoBehaviour
{
    public const int SizeX = 56;
    public const int SizeY = 28;
    public const int SizeZ = 56;

    public const byte Air   = 0;
    public const byte Grass = 1;
    public const byte Dirt  = 2;
    public const byte Stone = 3;
    public const byte Sand  = 4;

    readonly byte[] _blocks = new byte[SizeX * SizeY * SizeZ];

    Mesh _mesh;
    Material _material;
    MeshFilter _filter;
    MeshRenderer _renderer;
    MeshCollider _collider;

    // 六个面：法线方向 / 四个角点（从外面看逆时针）/ 明暗系数
    static readonly Vector3Int[] FaceNormals =
    {
        new Vector3Int( 1,  0,  0), new Vector3Int(-1,  0,  0),
        new Vector3Int( 0,  1,  0), new Vector3Int( 0, -1,  0),
        new Vector3Int( 0,  0,  1), new Vector3Int( 0,  0, -1),
    };

    static readonly Vector3[][] FaceCorners =
    {
        new[] { new Vector3(1, 0, 1), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1) }, // +X
        new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0) }, // -X
        new[] { new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0) }, // +Y 顶
        new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) }, // -Y 底
        new[] { new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1) }, // +Z
        new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) }, // -Z
    };

    static readonly float[] FaceShade = { 0.82f, 0.76f, 1.00f, 0.55f, 0.88f, 0.72f };

    static readonly int[] QuadOrder = { 0, 1, 2, 0, 2, 3 };

    public Material VoxelMaterial => _material;

    /// <summary>出生点：世界中心地表往上 3 格</summary>
    public Vector3 SpawnPoint =>
        new Vector3(SizeX * 0.5f, SurfaceHeight(SizeX / 2, SizeZ / 2) + 3f, SizeZ * 0.5f);

    void Awake()
    {
        _filter   = GetComponent<MeshFilter>();
        _renderer = GetComponent<MeshRenderer>();
        _collider = GetComponent<MeshCollider>();

        var shader = Resources.Load<Shader>("Shaders/VoxelDemo");
        if (shader == null) shader = Shader.Find("Voxel/Demo");

        _material = new Material(shader) { name = "VoxelDemoMaterial" };
        _renderer.sharedMaterial = _material;

        GenerateTerrain();
        Rebuild();
    }

    public void SetFog(Color color, float start, float end)
    {
        if (_material == null) return;
        _material.SetColor("_FogColor", color);
        _material.SetFloat("_FogStart", start);
        _material.SetFloat("_FogEnd", end);
    }

    // ---------- 方块读写 ----------

    public byte GetBlock(int x, int y, int z)
    {
        if (x < 0 || y < 0 || z < 0 || x >= SizeX || y >= SizeY || z >= SizeZ) return Air;
        return _blocks[Index(x, y, z)];
    }

    public void SetBlock(int x, int y, int z, byte value)
    {
        if (x < 0 || y < 0 || z < 0 || x >= SizeX || y >= SizeY || z >= SizeZ) return;
        _blocks[Index(x, y, z)] = value;
    }

    static int Index(int x, int y, int z) => (y * SizeZ + z) * SizeX + x;

    // ---------- 地形 ----------

    int SurfaceHeight(int x, int z)
    {
        float h = 9f
                + 3.0f * Mathf.Sin(x * 0.17f) * Mathf.Cos(z * 0.15f)
                + 1.6f * Mathf.Sin(x * 0.39f + 1.3f) * Mathf.Sin(z * 0.33f + 0.7f)
                + 1.1f * Mathf.Cos(x * 0.09f + z * 0.11f);
        return Mathf.Clamp(Mathf.RoundToInt(h), 2, SizeY - 6);
    }

    void GenerateTerrain()
    {
        for (int x = 0; x < SizeX; x++)
        {
            for (int z = 0; z < SizeZ; z++)
            {
                int h = SurfaceHeight(x, z);
                for (int y = 0; y <= h; y++)
                {
                    byte t = Stone;
                    if (y == h) t = (h <= 6) ? Sand : Grass;   // 低处沙滩，高处草地
                    else if (y >= h - 3) t = Dirt;             // 表层下面是泥土
                    SetBlock(x, y, z, t);
                }
            }
        }
    }

    // ---------- 网格重建 ----------

    /// <summary>只在方块被挖掉/放下后调用，不要每帧调</summary>
    public void Rebuild()
    {
        var verts = new List<Vector3>();
        var cols  = new List<Color>();

        for (int y = 0; y < SizeY; y++)
        {
            for (int z = 0; z < SizeZ; z++)
            {
                for (int x = 0; x < SizeX; x++)
                {
                    byte t = GetBlock(x, y, z);
                    if (t == Air) continue;

                    float jitter = Jitter(x, y, z);

                    for (int f = 0; f < 6; f++)
                    {
                        Vector3Int n = FaceNormals[f];
                        // 邻居是实心就不生成这个面
                        if (GetBlock(x + n.x, y + n.y, z + n.z) != Air) continue;

                        // 颜色在 gamma 空间算好（和网页版一致），再转 linear 交给管线
                        Color c = FaceColor(t, f) * (FaceShade[f] * jitter);
                        c.a = 1f;
                        c = c.linear;

                        Vector3[] corners = FaceCorners[f];
                        for (int i = 0; i < 6; i++)
                        {
                            Vector3 p = corners[QuadOrder[i]];
                            verts.Add(new Vector3(x + p.x, y + p.y, z + p.z));
                            cols.Add(c);
                        }
                    }
                }
            }
        }

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "VoxelMesh" };
            _filter.sharedMesh = _mesh;
        }

        _mesh.Clear();
        // 顶点数可能超过 65535，必须用 32 位索引
        _mesh.indexFormat = IndexFormat.UInt32;
        _mesh.SetVertices(verts);
        _mesh.SetColors(cols);
        _mesh.RecalculateNormals();   // 非索引几何 → 逐面法线，正好是方块感
        _mesh.RecalculateBounds();

        // 先置空再赋值，强制 MeshCollider 重新烘焙
        _collider.sharedMesh = null;
        _collider.sharedMesh = _mesh;
    }

    // ---------- 颜色与色差 ----------

    static Color FaceColor(byte t, int f)
    {
        if (t == Grass)
        {
            if (f == 2) return new Color32(0x6f, 0xbf, 0x4a, 255);   // 顶：草绿
            if (f == 3) return new Color32(0x7a, 0x5a, 0x34, 255);   // 底：土
            return new Color32(0x5c, 0x94, 0x40, 255);               // 侧面：暗一点的绿
        }
        if (t == Dirt)  return new Color32(0x8b, 0x6b, 0x43, 255);
        if (t == Stone) return new Color32(0x8d, 0x8f, 0x92, 255);
        if (t == Sand)  return new Color32(0xd8, 0xc9, 0x8d, 255);
        return Color.magenta;
    }

    /// <summary>每格 ±6% 的确定性色差，避免大片死平</summary>
    static float Jitter(int x, int y, int z)
    {
        unchecked
        {
            int n = x * 374761393 + y * 668265263 + z * 1274126177;
            n = (n ^ (n >> 13)) * 1274126177;
            uint u = (uint)(n ^ (n >> 16));
            return 0.94f + (u / 4294967295f) * 0.12f;
        }
    }
}
