using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace VoxelDemo.Codex.Editor
{
    [Serializable] public sealed class ImageAttachment
    {
        public string path, name;
        public ImageAttachment(string path, string name) { this.path = path; this.name = name; }
    }

    public static class CodexImageAttachments
    {
        public const int MaxImages = 8;
        public static bool IsSupported(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg";
        }
        public static string CacheDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/CodexPanel/Attachments"));

        public static ImageAttachment Import(string source)
        {
            source = Path.GetFullPath(source);
            if (!IsSupported(source) || !File.Exists(source)) throw new IOException("请选择 PNG 或 JPG 图片。");
            if (new FileInfo(source).Length > 20 * 1024 * 1024) throw new IOException("单张图片请小于 20 MB。");
            byte[] bytes = File.ReadAllBytes(source);
            var texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!texture.LoadImage(bytes) || texture.width > 8192 || texture.height > 8192 ||
                    (long)texture.width * texture.height > 32000000)
                    throw new IOException("图片无法读取或尺寸太大，请换成较小的 PNG/JPG。");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            Directory.CreateDirectory(CacheDirectory);
            string destination = Path.Combine(CacheDirectory, hash + Path.GetExtension(source).ToLowerInvariant());
            if (!File.Exists(destination)) File.WriteAllBytes(destination, bytes);
            return new ImageAttachment(destination, Path.GetFileName(source));
        }

        public static JArray BuildInput(string text, string[] images)
        {
            images = images ?? Array.Empty<string>();
            if (images.Length > MaxImages) throw new IOException("一次最多发送 8 张图片。");
            var input = new JArray();
            if (!string.IsNullOrWhiteSpace(text))
                input.Add(new JObject { ["type"] = "text", ["text"] = text, ["text_elements"] = new JArray() });
            foreach (string image in images)
            {
                if (!File.Exists(image) || !IsSupported(image)) throw new IOException("图片附件已丢失，请重新添加。");
                input.Add(new JObject { ["type"] = "localImage", ["path"] = Path.GetFullPath(image) });
            }
            return input;
        }

        public static bool ClipboardHasImage()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) return false;
            return IsClipboardFormatAvailable(8) || IsClipboardFormatAvailable(17) ||
                IsClipboardFormatAvailable(2) || IsClipboardFormatAvailable(15) ||
                IsClipboardFormatAvailable(RegisterClipboardFormat("PNG"));
        }

        // WinForms runs only in an isolated STA helper, avoiding Unity/Mono clipboard limitations.
        // Only an explicit paste action reads the clipboard. No credentials or image bytes go into logs.
        public static async Task<string[]> ReadClipboardAsync()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) throw new IOException("此版本的剪贴板图片粘贴目前支持 Windows。");
            string directory = CacheDirectory;
            Directory.CreateDirectory(directory);
            const string script = @"
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$paths = @()
$image = $null
try {
  if ([Windows.Forms.Clipboard]::ContainsImage()) {
    $image = [Windows.Forms.Clipboard]::GetImage()
    $file = [IO.Path]::Combine($env:VOXEL_CODEX_CLIPBOARD_DIR, 'paste-' + [Guid]::NewGuid().ToString('N') + '.png')
    $image.Save($file, [Drawing.Imaging.ImageFormat]::Png)
    $paths += $file
  } elseif ([Windows.Forms.Clipboard]::ContainsData('PNG')) {
    $stream = [Windows.Forms.Clipboard]::GetData('PNG')
    if ($stream -is [IO.Stream]) {
      $file = [IO.Path]::Combine($env:VOXEL_CODEX_CLIPBOARD_DIR, 'paste-' + [Guid]::NewGuid().ToString('N') + '.png')
      $output = [IO.File]::Create($file)
      try { $stream.Position=0; $stream.CopyTo($output) } finally { $output.Dispose() }
      $paths += $file
    }
  } elseif ([Windows.Forms.Clipboard]::ContainsFileDropList()) {
    $paths = @([Windows.Forms.Clipboard]::GetFileDropList() | Where-Object { $_ -match '\.(png|jpe?g)$' })
  }
  @{paths=@($paths)} | ConvertTo-Json -Compress
} finally { if ($image) { $image.Dispose() } }
";
            var start = new ProcessStartInfo {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -STA -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            start.EnvironmentVariables["VOXEL_CODEX_CLIPBOARD_DIR"] = directory;
            using (var process = Process.Start(start))
            {
                if (process == null) throw new IOException("无法读取剪贴板。");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                var exited = Task.Run(() => process.WaitForExit());
                var finished = Task.WhenAll(stdout, stderr, exited);
                if (await Task.WhenAny(finished, Task.Delay(12000)) != finished)
                { try { process.Kill(); } catch (InvalidOperationException) { } throw new IOException("剪贴板读取超时，请重试。"); }
                await finished;
                if (process.ExitCode != 0) throw new IOException("剪贴板暂时无法读取，请重新复制图片后再粘贴。");
                try { return JObject.Parse(stdout.Result)["paths"]?.ToObject<string[]>() ?? Array.Empty<string>(); }
                catch { throw new IOException("剪贴板里没有可读取的图片。"); }
            }
        }
        [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    }

    public sealed class CodexImagePreview : EditorWindow
    {
        [SerializeField] string imagePath;
        Texture2D texture;
        public static void Open(string path)
        {
            var window = CreateInstance<CodexImagePreview>();
            window.imagePath = path; window.titleContent = new GUIContent("图片预览");
            window.minSize = new Vector2(350, 250); window.ShowUtility();
        }
        void OnGUI()
        {
            if (texture == null && File.Exists(imagePath))
            { texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave }; texture.LoadImage(File.ReadAllBytes(imagePath)); }
            if (texture != null) GUI.DrawTexture(new Rect(8, 8, position.width - 16, position.height - 16), texture, ScaleMode.ScaleToFit);
            else EditorGUILayout.HelpBox("图片已丢失，请重新添加。", MessageType.Info);
        }
        void OnDisable() { if (texture != null) DestroyImmediate(texture); }
    }
}
