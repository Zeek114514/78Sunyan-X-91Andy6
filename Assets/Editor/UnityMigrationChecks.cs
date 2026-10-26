using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Migration checks live in the Editor assembly and are excluded from players.
[InitializeOnLoad]
public static class UnityMigrationChecks
{
    const string Active = "VoxelDemo.MigrationSmoke.Active";
    const string Errors = "VoxelDemo.MigrationSmoke.Errors";
    const string Started = "VoxelDemo.MigrationSmoke.Started";
    const string ReportDir = "Artifacts/Migration";
    static int frames;
    static bool finishing;

    static UnityMigrationChecks()
    {
        Application.logMessageReceived += OnLog;
        EditorApplication.update += Tick;
    }

    static void OnLog(string text, string stack, LogType type)
    {
        if (SessionState.GetBool(Active, false) &&
            (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            SessionState.SetInt(Errors, SessionState.GetInt(Errors, 0) + 1);
    }

    [MenuItem("VoxelDemo/Migration/Validate and build Windows")]
    public static void ValidateAndBuild()
    {
        Directory.CreateDirectory(ReportDir);
        // Runtime-created materials use Shader.Find. Explicit material assets keep
        // those shader variants in players even though the startup scene is empty.
        if (!AssetDatabase.IsValidFolder("Assets/Resources/RuntimeShaderReferences"))
            AssetDatabase.CreateFolder("Assets/Resources", "RuntimeShaderReferences");
        foreach (var shaderName in new[] { "Universal Render Pipeline/Lit", "Skybox/Panoramic" })
        {
            string path = "Assets/Resources/RuntimeShaderReferences/" +
                shaderName.Substring(shaderName.LastIndexOf('/') + 1) + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) continue;
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new Exception("Missing runtime shader: " + shaderName);
            var material = new Material(shader);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
            if (material.HasProperty("_Mapping")) material.SetFloat("_Mapping", 1);
            AssetDatabase.CreateAsset(material, path);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        if (GraphicsSettings.defaultRenderPipeline == null)
            throw new Exception("The URP pipeline reference was lost.");
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                    throw new Exception("Missing scene script: " + t.name);
        foreach (var path in AssetDatabase.FindAssets("t:Shader", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new Exception("Shader import failure: " + path);
        }
        WildSiteCheck.Verify();
        AssetDatabase.SaveAssets();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = "Builds/Windows/VoxelDemo.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        File.WriteAllText(ReportDir + "/build.txt", "result=" + report.summary.result +
            "\nerrors=" + report.summary.totalErrors + "\nwarnings=" + report.summary.totalWarnings +
            "\nbytes=" + report.summary.totalSize + "\nduration=" + report.summary.totalTime);
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Windows build failed: " + report.summary.result);
        Debug.Log("MIGRATION_BUILD_PASS");
    }

    public static void StartSmoke()
    {
        Directory.CreateDirectory(ReportDir);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetInt(Errors, 0);
        SessionState.SetFloat(Started, (float)EditorApplication.timeSinceStartup);
        SessionState.SetBool(Active, true);
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Active, false) || finishing) return;
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Started, 0) > 240)
        {
            Finish("timeout", false);
            return;
        }
        if (!EditorApplication.isPlaying || !Application.isPlaying) return;
        frames++;
        if (frames < 120 || Time.realtimeSinceStartup < 12) return;
        var world = UnityEngine.Object.FindObjectOfType<OpenWorld>();
        var player = UnityEngine.Object.FindObjectOfType<VoxelPlayer>();
        var hud = UnityEngine.Object.FindObjectOfType<VoxelHud>();
        var camera = Camera.main;
        var controller = player == null ? null : player.GetComponent<CharacterController>();
        var meshes = UnityEngine.Object.FindObjectsOfType<MeshFilter>();
        bool valid = world != null && player != null && hud != null && camera != null && controller != null &&
            meshes.Length > 0 && meshes.Any(m => m.sharedMesh != null && m.sharedMesh.vertexCount > 0) &&
            SessionState.GetInt(Errors, 0) == 0;
        if (valid && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            var rt = new RenderTexture(1280, 720, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(ReportDir + "/world-camera.png", image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }
        valid = valid && SessionState.GetInt(Errors, 0) == 0;
        Finish("world=" + (world != null) + "\nplayer=" + (player != null) +
            "\nhud=" + (hud != null) + "\ncamera=" + (camera != null) +
            "\ncontroller=" + (controller != null) + "\nmeshFilters=" + meshes.Length +
            "\nobservedEditorUpdates=" + frames + "\nruntimeErrors=" + SessionState.GetInt(Errors, 0), valid);
    }

    static void Finish(string details, bool valid)
    {
        finishing = true;
        SessionState.SetBool(Active, false);
        File.WriteAllText(ReportDir + "/smoke.txt", "passed=" + valid + "\n" + details);
        Debug.Log(valid ? "MIGRATION_SMOKE_PASS" : "MIGRATION_SMOKE_FAIL: " + details);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(valid ? 0 : 1);
    }
}
