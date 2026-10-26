using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace VoxelDemo.Codex.Editor
{
    // Explicit self-test, using a separate conversation and ignored Temp files.
    public static class CodexPanelValidation
    {
        public static bool Running { get; private set; }
        [MenuItem("VoxelDemo/Codex 连接自检")]
        public static async void Run()
        {
            if (Running) return;
            Running = true;
            var report = new JObject();
            CodexPanelSession session = null;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("请先停止 Play，再运行连接自检。");
                var window = CodexChatWindow.Open();
                report["windowCreated"] = window != null;
                session = new CodexPanelSession("validation");
                session.NewConversation();
                session.AllowEdits = false;
                await session.ConnectAsync();
                Check(session.Connected && session.Authenticated, "未连接或尚未登录 ChatGPT。");
                report["authenticated"] = true;
                report["executableFound"] = File.Exists(session.Executable);
                await session.SendAsync("这是编辑器内连接自检。仅只读查看 Assets/Scripts/VoxelBootstrap.cs，回复该文件的 FogEnd 常量数值和 Boot 方法 RuntimeInitializeLoadType 的时机名。不要修改文件，也不要运行或关闭 Unity。");
                await Until(() => !session.Busy, 180);
                string reply = string.Join("\n", session.Messages.Where(m => m.role == "assistant").Select(m => m.text));
                Check(reply.Contains("2100") && reply.Contains("AfterSceneLoad"), "读取项目的回答未通过检查。");
                report["projectReadVerified"] = true;
                report["streamedReplyReceived"] = session.Messages.Any(m => m.role == "assistant" && !string.IsNullOrEmpty(m.itemId));
                session.AllowEdits = true;
                string marker = Path.Combine(session.Project, "Temp", "CodexPanel", "connection-check.txt");
                if (File.Exists(marker)) File.Delete(marker);
                await session.SendAsync("继续连接自检。仅在当前项目 Temp/CodexPanel/connection-check.txt 中写入一行 UNITY_PANEL_OK，可以创建该目录。不允许更改其他文件，不要操作 Unity 编辑器，不要运行测试或提交 Git。完成后简短回复。");
                await Until(() => !session.Busy, 180);
                Check(File.Exists(marker) && File.ReadAllText(marker).Trim() == "UNITY_PANEL_OK", "项目内写入自检未通过。");
                File.Delete(marker);
                report["projectWriteVerified"] = true;
                report["continuedConversationVerified"] = session.Messages.Count(m => m.role == "user") == 2;
                report["pendingApprovals"] = session.Requests.Count;
                report["passed"] = true;
                // Close the test process, then prove the production client can reconnect.
                session.Disconnect(false);
                await session.ConnectAsync();
                Check(session.Connected && session.Authenticated, "重新连接未通过。");
                report["reconnectionVerified"] = true;
                await session.SendAsync("只根据此对话前文回答：刚刚写入临时文件的那一行内容是什么？只回复那一行，不要使用工具或读取文件。");
                await Until(() => !session.Busy, 120);
                string resumed = session.Messages.LastOrDefault(m => m.role == "assistant")?.text ?? "";
                Check(resumed.Contains("UNITY_PANEL_OK"), "断开后续聊未通过。");
                report["persistentHistoryResumeVerified"] = true;
                await session.SendAsync("仅只读分析项目中全部界面脚本，逐个解释其行为和交互。不要修改任何文件，不要操作 Unity。");
                if (session.Busy) await session.StopAsync();
                await Until(() => !session.Busy, 30);
                Check(session.Status == "已停止", "停止任务未通过。");
                report["interruptionVerified"] = true;
                await session.ArchiveSelfTestAsync();
                report["testConversationArchived"] = true;
            }
            catch (Exception e) { report["passed"] = false; report["error"] = e.Message; }
            finally
            {
                session?.Dispose();
                string directory = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Artifacts", "CodexPanel"));
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "validation.json"), report.ToString());
                Running = false;
                UnityEngine.Debug.Log("[CodexPanelValidation] " + ((bool?)report["passed"] == true ? "PASS" : "FAIL: " + (string)report["error"]));
                if (Environment.GetCommandLineArgs().Contains("-codexPanelValidationExit"))
                    EditorApplication.Exit((bool?)report["passed"] == true ? 0 : 1);
            }
        }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        [MenuItem("VoxelDemo/Codex 图片自检")]
        public static async void RunImages()
        {
            if (Running) return;
            Running = true;
            var report = new JObject();
            CodexPanelSession session = null;
            CodexChatWindow window = null;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止 Play。");
                string directory = System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Temp/CodexImageValidation");
                Directory.CreateDirectory(directory);
                string first = MakeImage(Path.Combine(directory, "first.png"), UnityEngine.Color.red, UnityEngine.Color.blue);
                string second = MakeImage(Path.Combine(directory, "second.png"), UnityEngine.Color.yellow, UnityEngine.Color.green);
                var a = CodexImageAttachments.Import(first);
                var b = CodexImageAttachments.Import(second);
                Check(File.Exists(a.path) && File.Exists(b.path), "图片缓存未生成。");
                report["attachmentImportVerified"] = true;
                bool rejected = false;
                try { CodexImageAttachments.BuildInput("", new[] { Path.Combine(directory, "missing.png") }); }
                catch (IOException) { rejected = true; }
                Check(rejected, "丢失附件没有被拒绝。"); report["missingAttachmentRejected"] = true;
                window = UnityEngine.ScriptableObject.CreateInstance<CodexChatWindow>();
                window.titleContent = new UnityEngine.GUIContent("图片功能自检"); window.ShowUtility();
                window.AddImageFile(first); window.AddImageFile(first); window.AddImageFile(second);
                var serialized = new SerializedObject(window);
                var draft = serialized.FindProperty("imageDraft");
                Check(draft.arraySize == 2, "附件列表或去重未通过。");
                draft.DeleteArrayElementAtIndex(0); serialized.ApplyModifiedPropertiesWithoutUndo();
                Check(new SerializedObject(window).FindProperty("imageDraft").arraySize == 1 && File.Exists(first), "附件移除未通过。");
                report["previewDraftAndRemovalVerified"] = true;
                report["clipboardHasImageOrFile"] = CodexImageAttachments.ClipboardHasImage();
                // Read only; never replace the user's clipboard and never send clipboard contents in this test.
                if ((bool)report["clipboardHasImageOrFile"])
                {
                    string[] clipboard = await CodexImageAttachments.ReadClipboardAsync();
                    report["clipboardReadVerified"] = clipboard.Length > 0 && clipboard.All(File.Exists);
                    if (clipboard.Length > 0) Check(File.Exists(CodexImageAttachments.Import(clipboard[0]).path), "剪贴板图片无法导入。");
                }
                session = new CodexPanelSession("image-validation", true);
                session.NewConversation(); await session.ConnectAsync();
                Check(session.Connected && session.Authenticated, "请先登录 ChatGPT。");
                await session.SendAsync("这是图片识别自检。请直接看附件，分别说出第一张和第二张图片中左边、右边的颜色。只回复颜色，不要使用工具或读取文件。", new[] { a.path, b.path });
                await Until(() => !session.Busy, 150);
                var reply = session.Messages.LastOrDefault(m => m.role == "assistant")?.text ?? "";
                Check(reply.Contains("红") && reply.Contains("蓝") && reply.Contains("黄") && reply.Contains("绿"), "多图片识别自检未通过。");
                report["multipleImageVisionVerified"] = true;
                await session.SendAsync("", new[] { a.path });
                await Until(() => !session.Busy, 120);
                reply = session.Messages.LastOrDefault(m => m.role == "assistant")?.text ?? "";
                Check(!string.IsNullOrWhiteSpace(reply) && session.Status == "已完成" &&
                    string.IsNullOrWhiteSpace(session.Messages.Last(m => m.role == "user").text), "纯图片消息自检未通过。");
                report["imageOnlyMessageVerified"] = true;
                session.Dispose(); session = null;
                using (var reloaded = new CodexPanelSession("image-validation", true))
                    Check(reloaded.Messages.Where(m => m.role == "user").SelectMany(m => m.imagePaths ?? Array.Empty<string>()).Count() == 3,
                        "图片历史恢复未通过。");
                report["imageHistoryReloadVerified"] = true;
                report["passed"] = true;
            }
            catch (Exception e) { report["passed"] = false; report["error"] = e.Message; }
            finally
            {
                session?.Dispose(); if (window != null) window.Close();
                string output = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../Artifacts/CodexPanel"));
                Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "image-validation.json"), report.ToString());
                Running = false;
                UnityEngine.Debug.Log("[CodexImageValidation] " + ((bool?)report["passed"] == true ? "PASS" : "FAIL: " + (string)report["error"]));
                if (Environment.GetCommandLineArgs().Contains("-codexPanelValidationExit"))
                    EditorApplication.Exit((bool?)report["passed"] == true ? 0 : 1);
            }
        }
        static string MakeImage(string path, UnityEngine.Color left, UnityEngine.Color right)
        {
            var image = new UnityEngine.Texture2D(80, 40);
            try
            {
                for (int y = 0; y < 40; y++) for (int x = 0; x < 80; x++) image.SetPixel(x, y, x < 40 ? left : right);
                image.Apply(); File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(image));
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
            return path;
        }
        static async Task Until(Func<bool> predicate, int seconds)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (!predicate())
            {
                if (DateTime.UtcNow >= until) throw new TimeoutException("自检等待超时。请在面板中查看登录或权限状态。");
                await Task.Delay(100);
            }
        }
    }
}
