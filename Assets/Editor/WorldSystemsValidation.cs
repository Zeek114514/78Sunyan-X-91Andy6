using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// One-shot validation in the editor already running. Does not change Play state, scenes or saves.
[InitializeOnLoad]
public static class WorldSystemsValidation
{
    const string Request = "Artifacts/WorldSystems/validate.request";
    static double _nextCheck;
    static WorldSystemsValidation() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < _nextCheck) return;
        _nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Request);
        Run();
    }

    [MenuItem("VoxelDemo/Diagnostics/Validate chest models and world shaders")]
    public static void Run()
    {
        Directory.CreateDirectory("Artifacts/WorldSystems");
        var report = new StringBuilder("utc=" + DateTime.UtcNow.ToString("O") + "\nunity=" + Application.unityVersion + "\n");
        try
        {
            foreach (string name in new[] { "DynamicSky", "VoxelDemo", "VoxelWater", "TerrainBlend", "TexUnlit", "StylizedShadow", "MeadowGrass" })
            {
                string path = "Assets/Resources/Shaders/" + name + ".shader";
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Shader error: " + path);
                report.AppendLine("shader=" + name + "; importErrors=0");
            }
            // The runtime opening component uses normal delayed destruction, so exercise it only during existing Play.
            if (!EditorApplication.isPlaying) { report.AppendLine("chestPreview=SKIPPED (editor is not in Play; Play state was preserved)"); return; }
            foreach (LootChest.Tier tier in Enum.GetValues(typeof(LootChest.Tier)))
            {
                var preview = new PreviewRenderUtility();
                GameObject owner = null;
                Material fallback = null;
                try
                {
                    owner = new GameObject("Chest preview " + tier);
                    preview.AddSingleGO(owner);
                    fallback = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    var visual = LootChest.CreateVisual(owner.transform, tier, fallback);
                    var renderers = visual.GetComponentsInChildren<Renderer>();
                    if (renderers.Length == 0) throw new Exception("No renderers: " + tier);
                    Bounds bounds = renderers[0].bounds;
                    foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                    report.AppendLine("chest=" + tier + "; closedBounds=" + bounds.size.ToString("F3"));
                    Capture(preview, bounds, tier + "-closed.png");
                    LootChest.OpenVisual(visual);
                    var hinge = visual.Find("Opened lid hinge");
                    if (hinge == null || hinge.childCount == 0) throw new Exception("Missing moving lid: " + tier);
                    hinge.localRotation = Quaternion.Euler(105, 0, 0);
                    bool hasBody = false;
                    foreach (var mesh in visual.GetComponentsInChildren<MeshFilter>())
                    {
                        if (mesh.name == "Open chest body") hasBody = true;
                        if (mesh.sharedMesh == null) throw new Exception("Missing mesh: " + mesh.name);
                        foreach (var vertex in mesh.sharedMesh.vertices)
                            if (float.IsNaN(vertex.x) || float.IsNaN(vertex.y) || float.IsNaN(vertex.z)) throw new Exception("Non-finite mesh vertex");
                    }
                    if (!hasBody) throw new Exception("Missing open chest body: " + tier);
                    Capture(preview, bounds, tier + "-open.png");
                    report.AppendLine("opening=" + tier + "; body=present; lid=105deg; vertices=finite");
                }
                finally
                {
                    if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
                    if (fallback != null) UnityEngine.Object.DestroyImmediate(fallback);
                    preview.Cleanup();
                }
            }
            report.AppendLine("result=PASS");
        }
        catch (Exception error) { report.AppendLine("result=FAIL\n" + error); Debug.LogException(error); }
        finally { File.WriteAllText("Artifacts/WorldSystems/editor-validation.txt", report.ToString()); }
    }

    static void Capture(PreviewRenderUtility preview, Bounds bounds, string filename)
    {
        preview.camera.fieldOfView = 35;
        preview.camera.nearClipPlane = 0.01f; preview.camera.farClipPlane = 100;
        preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(0.24f, 0.3f, 0.36f);
        preview.camera.transform.position = bounds.center + new Vector3(1.6f, 1.15f, -2) * Mathf.Max(1, bounds.size.magnitude * 0.7f);
        preview.camera.transform.LookAt(bounds.center + Vector3.up * bounds.size.y * 0.15f);
        preview.lights[0].intensity = 1.2f; preview.lights[0].transform.rotation = Quaternion.Euler(45, 35, 0);
        preview.lights[1].intensity = 0.7f;
        preview.ambientColor = new Color(0.6f, 0.6f, 0.6f);
        preview.BeginStaticPreview(new Rect(0, 0, 640, 480));
        preview.Render(true);
        var texture = preview.EndStaticPreview();
        File.WriteAllBytes("Artifacts/WorldSystems/" + filename, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
    }
}
