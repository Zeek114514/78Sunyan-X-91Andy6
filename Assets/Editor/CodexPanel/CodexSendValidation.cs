using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace VoxelDemo.Codex.Editor
{
    // Runs only when explicitly launched with -codexSendValidation in the isolated test project.
    public static class CodexSendValidation
    {
        const string Key = "VoxelDemo.Codex.SendValidation.";
        static BindingFlags Private => BindingFlags.Instance | BindingFlags.NonPublic;
        [InitializeOnLoadMethod]
        static void Initialize()
        {
            if (!Environment.GetCommandLineArgs().Contains("-codexSendValidation")) return;
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }
        public static void Run()
        {
            SessionState.SetString(Key + "Deadline", DateTime.UtcNow.AddMinutes(4).Ticks.ToString());
            SessionState.SetInt(Key + "Stage", 1);
            var window = ScriptableObject.CreateInstance<CodexChatWindow>();
            window.titleContent = new GUIContent("发送流程自检"); window.ShowUtility();
            window.Session.NewConversation();
            SessionState.SetInt(Key + "Window", window.GetInstanceID());
            var texture = new Texture2D(4, 4);
            texture.SetPixels(Enumerable.Repeat(Color.cyan, 16).ToArray()); texture.Apply();
            string image = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/CodexSendValidation.png"));
            File.WriteAllBytes(image, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            SessionState.SetString(Key + "Image", image);
            Initialize();
            EditorApplication.isPlaying = true;
        }
        static void Poll()
        {
            int stage = SessionState.GetInt(Key + "Stage", 0);
            if (stage <= 0 || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Key + "Deadline", "0")))
                    throw new TimeoutException("发送流程测试超时。");
                var window = EditorUtility.InstanceIDToObject(SessionState.GetInt(Key + "Window", 0)) as CodexChatWindow;
                if (window == null) throw new InvalidOperationException("重载后聊天窗口丢失。");
                if (stage == 1)
                {
                    if (!EditorApplication.isPlaying) return;
                    SetDraft(window, "只回复 READONLY_PLAY_OK，不使用工具，不修改文件。", false);
                    Check(CanSend(window), "Play 中的讨论发送仍然被禁用。");
                    SessionState.SetInt(Key + "Stage", 2); Submit(window);
                }
                else if (stage == 2)
                {
                    if (!HasReply(window, "READONLY_PLAY_OK")) return;
                    Check(EditorApplication.isPlaying, "只读聊天不应停止 Play。");
                    window.AddImageFile(SessionState.GetString(Key + "Image", ""));
                    SetDraft(window, "只回复 EDIT_SEND_OK，不使用工具，不修改文件。", true);
                    Check(CanSend(window), "Play 中停止后发送的入口仍然被禁用。");
                    SessionState.SetInt(Key + "Stage", 3); Submit(window);
                }
                else if (stage == 3)
                {
                    if (!HasReply(window, "EDIT_SEND_OK")) return;
                    Check(!EditorApplication.isPlaying, "写入模式发送前没有停止 Play。");
                    var users = window.Session.Messages.Where(m => m.role == "user").ToArray();
                    Check(users.Length == 2 && users.Last().imagePaths?.Length == 1, "重载后消息重复发送或图片丢失。");
                    window.Session.Disconnect(false);
                    window.Session.Executable = Path.Combine(window.Session.Project, "does-not-exist.exe");
                    SetDraft(window, "DRAFT_MUST_BE_RETAINED", true);
                    window.AddImageFile(SessionState.GetString(Key + "Image", ""));
                    SessionState.SetInt(Key + "Stage", 4); Submit(window);
                }
                else if (stage == 4)
                {
                    if ((bool)typeof(CodexChatWindow).GetField("queuedSend", Private).GetValue(window) || window.Session.Connecting) return;
                    var data = new SerializedObject(window);
                    Check(data.FindProperty("draft").stringValue == "DRAFT_MUST_BE_RETAINED" &&
                        data.FindProperty("imageDraft").arraySize == 1, "连接失败后草稿或图片丢失。");
                    Check(window.Session.Messages.Count(m => m.role == "user") == 2, "失败请求不应被重复提交。");
                    Finish(new JObject { ["passed"] = true, ["readOnlyChatDuringPlay"] = true,
                        ["disconnectedSendAutoConnects"] = true, ["editSendStopsPlay"] = true,
                        ["queuedTextAndImageSurviveDomainReload"] = true, ["noDuplicateSubmission"] = true,
                        ["failedConnectionRetainsDraft"] = true });
                }
            }
            catch (Exception e) { Finish(new JObject { ["passed"] = false, ["stage"] = stage, ["error"] = e.Message }); }
        }
        static void SetDraft(CodexChatWindow window, string message, bool edits)
        {
            var data = new SerializedObject(window);
            data.FindProperty("draft").stringValue = message; data.FindProperty("editMode").boolValue = edits;
            data.ApplyModifiedPropertiesWithoutUndo(); window.Session.AllowEdits = edits;
        }
        static bool CanSend(CodexChatWindow window) => (bool)typeof(CodexChatWindow).GetMethod("CanSend", Private).Invoke(window, null);
        static void Submit(CodexChatWindow window) => typeof(CodexChatWindow).GetMethod("QueueSend", Private).Invoke(window, null);
        static bool HasReply(CodexChatWindow w, string reply) => !w.Session.Busy &&
            w.Session.Messages.Any(m => m.role == "assistant" && m.text.Contains(reply));
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Finish(JObject report)
        {
            SessionState.SetInt(Key + "Stage", 0); EditorApplication.update -= Poll;
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts/CodexPanel"));
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "send-validation.json"), report.ToString());
            Debug.Log("[CodexSendValidation] " + ((bool?)report["passed"] == true ? "PASS" : "FAIL: " + (string)report["error"]));
            EditorApplication.Exit((bool?)report["passed"] == true ? 0 : 1);
        }
    }
}

