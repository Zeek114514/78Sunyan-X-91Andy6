using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VoxelDemo.Codex.Editor;

// A one-shot local request lets an in-editor chat finish saving its response
// before the editor saves the project and exits through Unity's own API.
[InitializeOnLoad]
public static class SaveAndCloseProject
{
    const string RequestPath = "Artifacts/EditorControl/save-and-close.request";
    const string ResultPath = "Artifacts/EditorControl/save-and-close-result.txt";
    static double nextCheck;

    static SaveAndCloseProject()
    {
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        foreach (var window in Resources.FindObjectsOfTypeAll<CodexChatWindow>())
            if (window.Session != null && (window.Session.Busy || window.Session.Connecting)) return;

        string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
        string requestedProject = File.ReadAllText(RequestPath).Trim().TrimEnd('\\', '/');
        if (!string.Equals(project, requestedProject, StringComparison.OrdinalIgnoreCase)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;

        // Consume once: a cancelled save or an error must never close the editor later.
        File.Delete(RequestPath);
        try
        {
            if (!EditorSceneManager.SaveOpenScenes())
            {
                File.WriteAllText(ResultPath, "cancelled: scene save did not complete; editor remains open.");
                return;
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(ResultPath, "saved: scenes and assets; graceful Unity exit requested at " + DateTime.UtcNow.ToString("O"));
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(ResultPath, "failed: " + error);
            Debug.LogException(error);
        }
    }
}
