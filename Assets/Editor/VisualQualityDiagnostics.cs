using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Read-only inspection of the existing editor; never starts another editor or Play session.
[InitializeOnLoad]
public static class VisualQualityDiagnostics
{
    static VisualQualityDiagnostics()
    {
        EditorApplication.delayCall += WriteReport;
    }

    [MenuItem("VoxelDemo/Diagnostics/Write visual quality report")]
    public static void WriteReport()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += WriteReport;
            return;
        }
        var report = new StringBuilder();
        report.AppendLine("utc=" + DateTime.UtcNow.ToString("O"));
        report.AppendLine("unity=" + Application.unityVersion);
        report.AppendLine("playing=" + EditorApplication.isPlaying);
        report.AppendLine("quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()]);
        report.AppendLine("screen=" + Screen.width + "x" + Screen.height);
        report.AppendLine("anisotropicFiltering=" + QualitySettings.anisotropicFiltering);
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline != null)
            report.AppendLine("urp=" + pipeline.name + "; renderScale=" + pipeline.renderScale + "; msaa=" + pipeline.msaaSampleCount);
        foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
        {
            if (window.GetType().Name != "GameView") continue;
            report.AppendLine("gameViewWindow=" + window.position);
            var type = window.GetType();
            var size = type.GetProperty("targetSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (size != null) report.AppendLine("gameViewTarget=" + size.GetValue(window));
            var zoom = type.GetField("m_ZoomArea", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window);
            var scale = zoom?.GetType().GetProperty("scale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (scale != null) report.AppendLine("gameViewZoom=" + scale.GetValue(zoom));
        }
        foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>())
        {
            report.AppendLine("camera=" + camera.name + "; enabled=" + camera.enabled + "; pixels=" + camera.pixelWidth + "x" + camera.pixelHeight
                + "; dynamicResolution=" + camera.allowDynamicResolution + "; target=" + camera.targetTexture);
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) report.AppendLine("cameraPostFx=" + data.renderPostProcessing + "; aa=" + data.antialiasing);
        }
        foreach (string name in new[] { "MatGrass", "MatStoneWall", "MatSand", "MatRoofTile" })
        {
            var texture = Resources.Load<Texture2D>(name);
            if (texture != null) report.AppendLine("texture=" + name + "; size=" + texture.width + "x" + texture.height
                + "; filter=" + texture.filterMode + "; aniso=" + texture.anisoLevel + "; format=" + texture.format);
        }
        var font = Resources.Load<Font>("Fonts/HYWenHei-85W");
        report.AppendLine("font=" + (font == null ? "MISSING" : font.name + "; dynamic=" + font.dynamic));
        if (font != null)
            foreach (char character in "新手教程开始游戏背包角色地图")
                if (!font.HasCharacter(character)) report.AppendLine("missingGlyph=" + character);
        var logEntries = typeof(Editor).Assembly.GetType("UnityEditor.LogEntries");
        var counts = logEntries?.GetMethod("GetCountsByType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (counts != null)
        {
            object[] args = { 0, 0, 0 };
            counts.Invoke(null, args);
            report.AppendLine("consoleErrors=" + args[0] + "; warnings=" + args[1] + "; logs=" + args[2]);
        }
        Directory.CreateDirectory("Artifacts/VisualQuality");
        File.WriteAllText("Artifacts/VisualQuality/editor-report.txt", report.ToString());
    }
}
