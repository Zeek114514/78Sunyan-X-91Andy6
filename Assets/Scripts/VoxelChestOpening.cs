using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Opens the existing textured mesh, including generated meshes without separate lid bones.</summary>
public sealed class VoxelChestOpening : MonoBehaviour
{
    struct Vertex
    {
        public Vector3 position, normal;
        public Vector2 uv;
        public Color color;
        public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex {
            position = Vector3.Lerp(a.position, b.position, t), normal = Vector3.Lerp(a.normal, b.normal, t).normalized,
            uv = Vector2.Lerp(a.uv, b.uv, t), color = Color.Lerp(a.color, b.color, t)
        };
    }
    readonly List<Mesh> _meshes = new List<Mesh>();
    Transform _lid;
    Material _interior;
    float _elapsed;
    bool _opened;

    public void Open()
    {
        if (_opened) return;
        _opened = true;
        var filters = GetComponentsInChildren<MeshFilter>();
        var bounds = new Bounds();
        bool first = true;
        foreach (var filter in filters)
        {
            if (filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
            foreach (var point in filter.sharedMesh.vertices)
            {
                Vector3 p = transform.InverseTransformPoint(filter.transform.TransformPoint(point));
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                else bounds.Encapsulate(p);
            }
        }
        if (first) { Debug.LogWarning("[Chest] No readable chest mesh; opening could not be built."); return; }
        float cut = bounds.min.y + bounds.size.y * 0.65f;
        Vector3 hinge = new Vector3(bounds.center.x, cut, bounds.max.z);
        _lid = new GameObject("Opened lid hinge").transform;
        _lid.SetParent(transform, false);
        _lid.localPosition = hinge;
        foreach (var filter in filters)
        {
            var mesh = filter.sharedMesh;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (mesh == null || !mesh.isReadable || renderer == null) continue;
            var positions = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            var colors = mesh.colors;
            var vertices = new Vertex[positions.Length];
            Matrix4x4 matrix = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vertex {
                position = matrix.MultiplyPoint3x4(positions[i]),
                normal = normals.Length == positions.Length ? normalMatrix.MultiplyVector(normals[i]).normalized : Vector3.up,
                uv = uvs.Length == positions.Length ? uvs[i] : Vector2.zero,
                color = colors.Length == positions.Length ? colors[i] : Color.white
            };
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var lower = new List<Vertex>();
                var upper = new List<Vertex>();
                var indices = mesh.GetTriangles(sub);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var triangle = new[] { vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]] };
                    Clip(triangle, cut, false, lower);
                    Clip(triangle, cut, true, upper);
                }
                var materials = renderer.sharedMaterials;
                Material material = materials[Mathf.Min(sub, materials.Length - 1)];
                BuildPart("Open chest body", transform, lower, Vector3.zero, material);
                BuildPart("Open chest lid", _lid, upper, hinge, material);
            }
            renderer.enabled = false;
        }
        // A dark recessed interior makes the open cavity visible; do not merely tint a closed chest.
        _interior = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        _interior.color = new Color(0.095f, 0.055f, 0.035f);
        var underside = GameObject.CreatePrimitive(PrimitiveType.Cube);
        underside.name = "Lid wooden underside"; underside.transform.SetParent(_lid, false);
        underside.transform.localPosition = new Vector3(0, 0.006f, -bounds.extents.z);
        underside.transform.localScale = new Vector3(bounds.size.x * 0.9f, bounds.size.y * 0.025f, bounds.size.z * 0.9f);
        underside.GetComponent<Renderer>().sharedMaterial = _interior;
        underside.GetComponent<Collider>().enabled = false; Destroy(underside.GetComponent<Collider>());
        InteriorPart("Chest interior floor", new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.12f, bounds.center.z),
            new Vector3(bounds.size.x * 0.89f, bounds.size.y * 0.06f, bounds.size.z * 0.89f));
        float wallHeight = cut - bounds.min.y;
        foreach (float side in new[] { -1f, 1f })
        {
            InteriorPart("Chest inner side", new Vector3(bounds.center.x + side * bounds.extents.x * 0.9f, bounds.min.y + wallHeight / 2, bounds.center.z),
                new Vector3(bounds.size.x * 0.035f, wallHeight, bounds.size.z * 0.9f));
            InteriorPart("Chest inner end", new Vector3(bounds.center.x, bounds.min.y + wallHeight / 2, bounds.center.z + side * bounds.extents.z * 0.9f),
                new Vector3(bounds.size.x * 0.9f, wallHeight, bounds.size.z * 0.035f));
        }
    }

    static void Clip(Vertex[] input, float cut, bool upper, List<Vertex> result)
    {
        var polygon = new List<Vertex>(4);
        Vertex previous = input[input.Length - 1];
        bool wasInside = upper ? previous.position.y >= cut : previous.position.y <= cut;
        foreach (var current in input)
        {
            bool inside = upper ? current.position.y >= cut : current.position.y <= cut;
            if (inside != wasInside)
                polygon.Add(Vertex.Lerp(previous, current, (cut - previous.position.y) / (current.position.y - previous.position.y)));
            if (inside) polygon.Add(current);
            previous = current; wasInside = inside;
        }
        for (int i = 1; i + 1 < polygon.Count; i++) { result.Add(polygon[0]); result.Add(polygon[i]); result.Add(polygon[i + 1]); }
    }

    void BuildPart(string name, Transform parent, List<Vertex> source, Vector3 offset, Material material)
    {
        if (source.Count == 0) return;
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        var positions = new Vector3[source.Count]; var normals = new Vector3[source.Count];
        var uv = new Vector2[source.Count]; var colors = new Color[source.Count]; var triangles = new int[source.Count];
        for (int i = 0; i < source.Count; i++) {
            positions[i] = source[i].position - offset; normals[i] = source[i].normal;
            uv[i] = source[i].uv; colors[i] = source[i].color; triangles[i] = i;
        }
        mesh.vertices = positions; mesh.normals = normals; mesh.uv = uv; mesh.colors = colors; mesh.triangles = triangles;
        mesh.RecalculateBounds(); _meshes.Add(mesh);
        var part = new GameObject(name); part.transform.SetParent(parent, false);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        part.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    void InteriorPart(string name, Vector3 position, Vector3 scale)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = name;
        part.transform.SetParent(transform, false); part.transform.localPosition = position; part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = _interior;
        var collider = part.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
    }

    void Update()
    {
        if (_lid == null) return;
        _elapsed = Mathf.Min(1, _elapsed + Time.deltaTime / 0.65f);
        _lid.localRotation = Quaternion.Euler(Mathf.SmoothStep(0, 105, _elapsed), 0, 0);
    }
    void OnDestroy() { foreach (var mesh in _meshes) if (mesh != null) Destroy(mesh); if (_interior != null) Destroy(_interior); }
}
