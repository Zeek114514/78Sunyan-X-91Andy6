using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for BGM: every clip under Assets/Resources/Audio/bgm/ is loaded as **Streaming**.
///
/// Why: Unity's default is "Decompress On Load", which decodes the whole clip into memory --
/// the 4-minute theme decodes to roughly 80 MB of PCM, and the overworld track is even longer.
/// Streaming decodes on demand, so BGM costs almost no memory.
///
/// Two API traps (both hit once):
///   - AudioClipLoadType lives in UnityEngine (not UnityEditor): without "using UnityEngine" you get CS0103.
///   - AudioImporter.preloadAudioData is obsolete (CS0619); it now belongs to defaultSampleSettings.
///
/// Editor-only (Assets/Editor, not part of the runtime assembly). The rule only applies while
/// importing, so clips that are already imported must be reimported once to pick it up.
/// </summary>
class AudioImportSettings : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        string path = assetPath.Replace('\\', '/');
        if (!path.Contains("/Audio/bgm/")) return;

        var importer = assetImporter as AudioImporter;
        if (importer == null) return;

        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.Streaming;
        settings.preloadAudioData = false;      // streaming should not preload the whole clip
        importer.defaultSampleSettings = settings;
    }
}
