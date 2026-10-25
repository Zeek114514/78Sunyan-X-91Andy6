using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

// PMX 2.0 -> native Unity skinned asset. This local-test path deliberately does
// not interpret MMD IK, physics, sphere maps or animation files as Unity features.
[ScriptedImporter(4, "pmx")]
public sealed class LocalPmxImporter : ScriptedImporter
{
    public float targetHeight = 1.72f;
    public bool hideWings = true;

    sealed class Reader : IDisposable
    {
        readonly BinaryReader r;
        public readonly byte[] settings;
        readonly Encoding encoding;
        public Reader(string path)
        {
            r = new BinaryReader(File.OpenRead(path));
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "PMX " || Math.Abs(r.ReadSingle() - 2f) > .01f)
                throw new InvalidDataException("Only PMX 2.0 is supported by the local test importer.");
            settings = r.ReadBytes(r.ReadByte());
            if (settings.Length < 8) throw new InvalidDataException("Invalid PMX settings.");
            encoding = settings[0] == 0 ? Encoding.Unicode : Encoding.UTF8;
        }
        public int Int() => r.ReadInt32();
        public float Float() => r.ReadSingle();
        public byte Byte() => r.ReadByte();
        public ushort Flags() => r.ReadUInt16();
        public Vector3 Vec() => new Vector3(Float(), Float(), Float());
        public Vector2 UV() => new Vector2(Float(), 1f - Float());
        public Color Color() => new Color(Float(), Float(), Float(), Float());
        public string Text()
        {
            int n = Int();
            if (n < 0 || n > 16000000) throw new InvalidDataException("Invalid PMX string length.");
            return encoding.GetString(r.ReadBytes(n));
        }
        public void Skip(int n) { r.BaseStream.Seek(n, SeekOrigin.Current); }
        public int Index(int slot, bool unsigned = false)
        {
            switch (settings[slot])
            {
                case 1: return unsigned ? r.ReadByte() : r.ReadSByte();
                case 2: return unsigned ? r.ReadUInt16() : r.ReadInt16();
                case 4: return r.ReadInt32();
                default: throw new InvalidDataException("Invalid PMX index size.");
            }
        }
        public void Dispose() { r.Dispose(); }
    }

    public override void OnImportAsset(AssetImportContext ctx)
    {
        using (var r = new Reader(ctx.assetPath))
        {
            string modelName = r.Text(); r.Text(); r.Text(); r.Text();
            int count = r.Int();
            if (count < 1 || count > 1000000) throw new InvalidDataException("Invalid vertex count.");
            var positions = new Vector3[count]; var normals = new Vector3[count]; var uv = new Vector2[count];
            var weights = new BoneWeight[count];
            var used = new SortedSet<int>();
            for (int i = 0; i < count; i++)
            {
                positions[i] = r.Vec(); normals[i] = r.Vec(); uv[i] = r.UV();
                r.Skip(r.settings[1] * 16);
                byte deform = r.Byte();
                var b = new int[4]; var w = new float[4];
                if (deform == 0) { b[0] = r.Index(5); w[0] = 1; }
                else if (deform == 1 || deform == 3)
                {
                    b[0] = r.Index(5); b[1] = r.Index(5); w[0] = r.Float(); w[1] = 1 - w[0];
                    if (deform == 3) r.Skip(36); // SDEF uses ordinary linear skinning in this test importer.
                }
                else if (deform == 2 || deform == 4)
                {
                    for (int k = 0; k < 4; k++) b[k] = r.Index(5);
                    for (int k = 0; k < 4; k++) w[k] = r.Float();
                }
                else throw new InvalidDataException("Unknown PMX deform mode.");
                float total = 0;
                for (int k = 0; k < 4; k++) { if (b[k] < 0) { b[k] = 0; w[k] = 0; } w[k] = Mathf.Max(0, w[k]); total += w[k]; }
                if (total <= 0) { b[0] = 0; w[0] = total = 1; }
                for (int k = 0; k < 4; k++) { w[k] /= total; if (w[k] > 0) used.Add(b[k]); }
                weights[i] = new BoneWeight { boneIndex0 = b[0], boneIndex1 = b[1], boneIndex2 = b[2], boneIndex3 = b[3],
                    weight0 = w[0], weight1 = w[1], weight2 = w[2], weight3 = w[3] };
                r.Skip(4);
            }
            int indexCount = r.Int();
            if (indexCount < 3 || indexCount % 3 != 0 || indexCount > 6000000) throw new InvalidDataException("Invalid index count.");
            var triangles = new int[indexCount];
            for (int i = 0; i < indexCount; i++)
            {
                triangles[i] = r.Index(2, true);
                if (triangles[i] < 0 || triangles[i] >= count) throw new InvalidDataException("Invalid vertex index.");
            }
            // Match Unity's triangle winding to the supplied exterior vertex normals.
            float winding = 0;
            for (int i = 0; i < indexCount; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                winding += Vector3.Dot(Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]), normals[a]);
            }
            if (winding < 0) for (int i = 0; i < indexCount; i += 3) { int v = triangles[i]; triangles[i] = triangles[i + 2]; triangles[i + 2] = v; }

            var texturePaths = new string[r.Int()];
            for (int i = 0; i < texturePaths.Length; i++) texturePaths[i] = r.Text();
            int materialCount = r.Int();
            var materials = new Material[materialCount]; var submeshes = new int[materialCount][];
            var texturedMaterials = new List<int>();
            int offset = 0;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidDataException("URP Unlit shader is unavailable.");
            for (int i = 0; i < materialCount; i++)
            {
                string name = r.Text(); r.Text(); Color diffuse = r.Color();
                r.Skip(28); r.Byte(); r.Skip(20);
                int textureIndex = r.Index(3); r.Index(3); r.Byte();
                bool sharedToon = r.Byte() != 0;
                if (sharedToon) r.Byte(); else r.Index(3);
                r.Text(); int n = r.Int();
                if (n < 0 || offset + n > triangles.Length) throw new InvalidDataException("Invalid material range.");
                submeshes[i] = new int[n]; Array.Copy(triangles, offset, submeshes[i], 0, n); offset += n;
                var material = new Material(shader) { name = name };
                material.SetColor("_BaseColor", diffuse);
                material.SetFloat("_Cull", 0); // Thin hair, veil and skirt surfaces must be visible from both sides.
                material.SetFloat("_AlphaClip", 1); material.SetFloat("_Cutoff", .12f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
                if (textureIndex >= 0 && textureIndex < texturePaths.Length)
                {
                    texturedMaterials.Add(i);
                    string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ctx.assetPath), texturePaths[textureIndex].Replace('\\', '/')));
                    string project = Path.GetFullPath(".") + Path.DirectorySeparatorChar;
                    if (!path.StartsWith(project, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Texture escapes project.");
                    string assetPath = path.Substring(project.Length).Replace('\\', '/');
                    ctx.DependsOnArtifact(assetPath);
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (texture == null) ctx.LogImportWarning("Missing texture: " + assetPath);
                    material.SetTexture("_BaseMap", texture);
                }
                else material.SetTexture("_BaseMap", Texture2D.whiteTexture);
                if (diffuse.a < .99f || name.IndexOf("alpha", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    material.SetFloat("_Surface", 1); material.SetFloat("_ZWrite", 0);
                    material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = (int)RenderQueue.Transparent;
                }
                materials[i] = material; ctx.AddObjectToAsset("material" + i, material);
            }
            if (offset != indexCount) throw new InvalidDataException("Incomplete material ranges.");

            int boneCount = r.Int();
            var boneNames = new string[boneCount]; var bonePositions = new Vector3[boneCount]; var parents = new int[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                boneNames[i] = r.Text(); r.Text(); bonePositions[i] = r.Vec(); parents[i] = r.Index(5); r.Int();
                ushort flags = r.Flags();
                if ((flags & 1) != 0) r.Index(5); else r.Skip(12);
                if ((flags & 0x300) != 0) { r.Index(5); r.Float(); }
                if ((flags & 0x400) != 0) r.Skip(12);
                if ((flags & 0x800) != 0) r.Skip(24);
                if ((flags & 0x2000) != 0) r.Skip(4);
                if ((flags & 0x20) != 0)
                {
                    r.Index(5); r.Int(); r.Float(); int links = r.Int();
                    for (int k = 0; k < links; k++) { r.Index(5); if (r.Byte() != 0) r.Skip(24); }
                }
            }
            if (used.Any(index => index >= boneCount)) throw new InvalidDataException("Skin references a missing bone.");
            var bounds = new Bounds(positions[0], Vector3.zero);
            foreach (var p in positions) bounds.Encapsulate(p);
            float scale = targetHeight / Mathf.Max(.01f, bounds.size.y);
            Vector3 origin = new Vector3(0, bounds.min.y, 0);
            for (int i = 0; i < count; i++) positions[i] = (positions[i] - origin) * scale;
            var root = new GameObject(modelName);
            var rig = new GameObject("Skeleton").transform; rig.SetParent(root.transform, false);
            var bones = new Transform[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                bones[i] = new GameObject(boneNames[i]).transform;
                bones[i].SetParent(rig, false); bones[i].position = (bonePositions[i] - origin) * scale;
            }
            for (int i = 0; i < boneCount; i++)
                if (parents[i] >= 0 && parents[i] != i) bones[i].SetParent(bones[parents[i]], true);
            var skinBones = used.Select(index => bones[index]).ToArray();
            var remap = used.Select((index, i) => new { index, i }).ToDictionary(p => p.index, p => p.i);
            for (int i = 0; i < count; i++)
            {
                var w = weights[i];
                w.boneIndex0 = w.weight0 > 0 ? remap[w.boneIndex0] : 0; w.boneIndex1 = w.weight1 > 0 ? remap[w.boneIndex1] : 0;
                w.boneIndex2 = w.weight2 > 0 ? remap[w.boneIndex2] : 0; w.boneIndex3 = w.weight3 > 0 ? remap[w.boneIndex3] : 0;
                weights[i] = w;
            }
            var mesh = new Mesh { name = "Remielle mesh", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = positions; mesh.normals = normals; mesh.uv = uv; mesh.boneWeights = weights;
            mesh.bindposes = skinBones.Select(b => b.worldToLocalMatrix * root.transform.localToWorldMatrix).ToArray();
            mesh.subMeshCount = materialCount;
            for (int i = 0; i < materialCount; i++)
            {
                // Remove the dedicated wing surface, not its skeleton: collapsing
                // wing bones would drag connected skin and leave shading artifacts.
                string part = materials[i].name.Trim();
                bool wing = part == "翅膀" || part == "翼" || string.Equals(part, "wings", StringComparison.OrdinalIgnoreCase);
                mesh.SetTriangles(hideWings && wing ? new int[0] : submeshes[i], i, false);
            }
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            // PMX vertex morph offsets use model units, so scale them with the skin.
            int morphCount = r.Int();
            if (morphCount < 0 || morphCount > 10000) throw new InvalidDataException("Invalid morph count.");
            var morphNames = new List<string>();
            for (int m = 0; m < morphCount; m++)
            {
                string name = r.Text(); string english = r.Text(); r.Byte();
                byte type = r.Byte(); int entries = r.Int();
                if (entries < 0 || entries > 6000000) throw new InvalidDataException("Invalid morph entries.");
                Vector3[] delta = type == 1 ? new Vector3[count] : null;
                for (int e = 0; e < entries; e++)
                {
                    switch (type)
                    {
                        case 0: r.Index(7); r.Float(); break;
                        case 1:
                            int vertex = r.Index(2, true);
                            Vector3 offsetDelta = r.Vec() * scale;
                            if (vertex < 0 || vertex >= count) throw new InvalidDataException("Invalid morph vertex.");
                            delta[vertex] += offsetDelta;
                            break;
                        case 2: r.Index(5); r.Skip(28); break;
                        case 3: case 4: case 5: case 6: case 7: r.Index(2, true); r.Skip(16); break;
                        case 8: r.Index(4); r.Byte(); r.Skip(112); break;
                        default: throw new InvalidDataException("Unsupported PMX morph type: " + type);
                    }
                }
                // Group, bone, UV and material morphs are parsed, but not approximated as facial shapes.
                if (delta == null || entries == 0) continue;
                string shapeName = m + ":" + name + "|" + english;
                mesh.AddBlendShapeFrame(shapeName, 100, delta, null, null);
                morphNames.Add(shapeName);
            }
            ctx.AddObjectToAsset("expressions", new TextAsset(string.Join("\n", morphNames)) { name = "Imported vertex expressions" });
            ctx.AddObjectToAsset("mesh", mesh);
            ctx.AddObjectToAsset("texturedMaterials", new TextAsset(string.Join(",", texturedMaterials)) { name = "Textured material indices" });
            var renderer = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
            renderer.transform.SetParent(root.transform, false); renderer.sharedMesh = mesh;
            renderer.bones = skinBones; renderer.rootBone = rig; renderer.sharedMaterials = materials;
            renderer.localBounds = new Bounds(new Vector3(0, targetHeight * .5f, 0), new Vector3(3, targetHeight + .5f, 2));
            renderer.quality = SkinQuality.Bone4; renderer.updateWhenOffscreen = true;
            ctx.AddObjectToAsset("model", root); ctx.SetMainObject(root);
        }
    }
}
