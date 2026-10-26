using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VoxelDemo.Codex.Editor
{
    public sealed class CodexChatWindow : EditorWindow
    {
        public CodexPanelSession Session { get; private set; }
        Vector2 scroll;
        [SerializeField] string draft = "";
        [SerializeField] List<ImageAttachment> imageDraft = new List<ImageAttachment>();
        [SerializeField] bool editMode, queuedSend, queuedAllowEdits;
        [SerializeField] string queuedToken;
        [SerializeField] long queuedUntil;
        bool processingQueue;
        readonly Dictionary<string, Texture2D> imageTextures = new Dictionary<string, Texture2D>();
        Vector2 imageScroll;
        bool pastingImage;
        string imageNotice = "";
        bool showSettings, follow = true;
        GUIStyle bodyStyle, inputStyle, titleStyle;

        [MenuItem("VoxelDemo/Codex 聊天", priority = 0)]
        public static CodexChatWindow Open()
        {
            var window = GetWindow<CodexChatWindow>("Codex · 小猫助手");
            window.minSize = new Vector2(360, 480);
            window.Show();
            return window;
        }
        void OnEnable()
        {
            minSize = new Vector2(360, 480);
            Session = new CodexPanelSession();
            Session.AllowEdits = queuedSend ? queuedAllowEdits : editMode;
            // SessionState survives assembly reloads, but not a new Unity launch.
            if (queuedSend && !SessionState.GetBool("VoxelDemo.Codex.Send." + queuedToken, false)) queuedSend = false;
            Session.Changed += OnChanged;
            EditorApplication.update += ContinueQueuedSend;
        }
        void OnDisable()
        {
            EditorApplication.update -= ContinueQueuedSend;
            foreach (var texture in imageTextures.Values) if (texture != null) DestroyImmediate(texture);
            imageTextures.Clear();
            if (Session != null) { Session.Changed -= OnChanged; Session.Dispose(); Session = null; }
        }
        void OnDestroy() { CancelQueuedSend(); }
        void OnChanged() { if (follow) scroll.y = float.MaxValue; Repaint(); }

        void OnGUI()
        {
            if (Session == null) return;
            EnsureStyles();
            HandleImageEvents();
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return &&
                (Event.current.control || Event.current.command) && CanSend())
            { Event.current.Use(); QueueSend(); }
            GUILayout.Space(7);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                GUILayout.Label("Codex · 小猫开发助手", titleStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("设置", EditorStyles.miniButton, GUILayout.Width(42))) showSettings = !showSettings;
                GUILayout.Space(8);
            }
            GUILayout.Space(5);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(Session.Connecting))
                    if (GUILayout.Button(Session.Connected ? "断开" : "连接", EditorStyles.toolbarButton, GUILayout.Width(48)))
                    { if (Session.Connected) Session.Disconnect(); else Run(Session.ConnectAsync()); }
                using (new EditorGUI.DisabledScope(!Session.Connected || Session.Authenticated || Session.Busy))
                    if (GUILayout.Button("登录 ChatGPT", EditorStyles.toolbarButton, GUILayout.Width(95))) Run(Session.LoginAsync());
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(Session.Busy || Session.Connecting))
                    if (GUILayout.Button("新对话", EditorStyles.toolbarButton, GUILayout.Width(55))) Session.NewConversation();
            }
            if (showSettings) DrawSettings();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(Session.Status, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("项目：" + System.IO.Path.GetFileName(Session.Project), EditorStyles.miniLabel);
                if (Session.Connected) EditorGUILayout.LabelField(Session.Model, EditorStyles.miniLabel);
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("正在试玩：可以直接讨论。需要修改代码时，发送会先停止试玩，草稿和图片会保留。", MessageType.Info);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (Session.Messages.Count == 0) DrawWelcome();
            foreach (var message in Session.Messages)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    string who = message.role == "user" ? "你" : message.role == "assistant" ? "小猫助手" :
                        message.role == "tool" ? "项目操作" : "提示";
                    EditorGUILayout.LabelField(who, EditorStyles.boldLabel);
                    if (!string.IsNullOrEmpty(message.text)) DrawSelectable(message.text);
                    if (message.imagePaths != null && message.imagePaths.Length > 0)
                    {
                        foreach (string path in message.imagePaths)
                        {
                            var texture = GetImageTexture(path);
                            if (texture != null)
                            {
                                var rect = GUILayoutUtility.GetRect(80, 135, GUILayout.ExpandWidth(true));
                                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
                                if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) CodexImagePreview.Open(path);
                            }
                            else EditorGUILayout.LabelField("图片缓存已丢失", EditorStyles.miniLabel);
                        }
                    }
                }
                GUILayout.Space(4);
            }
            foreach (var request in Session.Requests.ToArray()) DrawRequest(request);
            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(queuedSend))
            {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(Session.Busy))
                { editMode = EditorGUILayout.ToggleLeft("允许修改当前项目", Session.AllowEdits, GUILayout.Width(155)); Session.AllowEdits = editMode; }
                GUILayout.FlexibleSpace();
                follow = GUILayout.Toggle(follow, "跟随回复", EditorStyles.miniButton, GUILayout.Width(65));
            }
            EditorGUILayout.LabelField(Session.AllowEdits ? "可修改项目文件；额外权限会先询问。" : "当前仅讨论和读取项目，不自动修改文件。", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("附上选中对象", EditorStyles.miniButton)) AttachSelection();
                using (new EditorGUI.DisabledScope(Session.RecentErrors.Count == 0))
                    if (GUILayout.Button("附上最近报错", EditorStyles.miniButton))
                        draft += "\n\nUnity 最近报错：\n" + string.Join("\n\n", Session.RecentErrors);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("添加图片", EditorStyles.miniButton, GUILayout.Width(75)))
                {
                    string path = EditorUtility.OpenFilePanelWithFilters("选择图片", "", new[] { "图片", "png,jpg,jpeg" });
                    if (!string.IsNullOrEmpty(path)) AddImageFile(path);
                }
                using (new EditorGUI.DisabledScope(pastingImage))
                    if (GUILayout.Button(pastingImage ? "正在粘贴…" : "粘贴图片", EditorStyles.miniButton, GUILayout.Width(80))) PasteImage();
                GUILayout.Label("拖入图片 / Ctrl+V", EditorStyles.miniLabel);
            }
            DrawImageDraft();
            if (!string.IsNullOrEmpty(imageNotice)) EditorGUILayout.LabelField(imageNotice, EditorStyles.wordWrappedMiniLabel);
            GUI.SetNextControlName("CodexMessageInput");
            draft = EditorGUILayout.TextArea(draft, inputStyle, GUILayout.Height(80));
            }
            EditorGUILayout.LabelField(SendHint(), EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Ctrl + Enter 发送 · 对话保存在本机", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (queuedSend)
                { if (GUILayout.Button("取消待发送", GUILayout.Width(100), GUILayout.Height(26))) CancelQueuedSend(); }
                else if (Session.Busy)
                { if (GUILayout.Button("停止", GUILayout.Width(65), GUILayout.Height(26))) Run(Session.StopAsync()); }
                else using (new EditorGUI.DisabledScope(!CanSend()))
                    if (GUILayout.Button(SendLabel(), GUILayout.Width(130), GUILayout.Height(26))) QueueSend();
            }
            GUILayout.Space(5);
        }

        void DrawSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            using (new EditorGUI.DisabledScope(Session.Connected || Session.Connecting))
            {
                EditorGUILayout.LabelField("Codex 程序位置", EditorStyles.boldLabel);
                Session.Executable = EditorGUILayout.TextField(Session.Executable);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("自动查找")) Session.Executable = CodexPanelSession.FindExecutable();
                    if (GUILayout.Button("选择程序…"))
                    {
                        string path = EditorUtility.OpenFilePanel("选择 codex.exe", "", "exe");
                        if (!string.IsNullOrEmpty(path)) Session.Executable = path;
                    }
                }
                EditorGUILayout.HelpBox("复用本机 Codex 登录。程序更新后若连接失败，可以重新自动查找。这里不保存账号密钥。", MessageType.None);
            }
        }

        void DrawWelcome()
        {
            GUILayout.Space(20);
            EditorGUILayout.LabelField("在 Unity 里直接说出你的想法，喵。", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("先点击「连接」。登录成功后，就可以讨论游戏、分析报错；勾选「允许修改当前项目」后，可以交给 Codex 修改代码。", bodyStyle);
            GUILayout.Space(10);
            EditorGUILayout.LabelField("例如：\n• 解释一下世界是怎么生成的\n• 分析我附上的报错\n• 把玩家跳跃高度稍微调高", bodyStyle);
            GUILayout.Space(10);
            EditorGUILayout.LabelField("这是编辑器内的独立对话，沿用当前项目资料；不会自动导入桌面聊天的全部记录。", EditorStyles.wordWrappedMiniLabel);
        }

        void DrawRequest(PanelRequest request)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool question = request.Method.EndsWith("requestUserInput", StringComparison.Ordinal);
                EditorGUILayout.LabelField(question ? "助手需要你的回答" : "需要你的确认", EditorStyles.boldLabel);
                bool allAnswered = true;
                if (question)
                {
                    foreach (var q in request.Payload["questions"] as JArray ?? new JArray())
                    {
                        string id = (string)q["id"];
                        EditorGUILayout.LabelField((string)q["question"] ?? (string)q["header"] ?? "问题", bodyStyle);
                        request.Answers.TryGetValue(id, out var answer);
                        foreach (var option in q["options"] as JArray ?? new JArray())
                        {
                            string label = (string)option["label"];
                            if (GUILayout.Button(label + "  " + (string)option["description"], bodyStyle)) answer = label;
                        }
                        answer = EditorGUILayout.TextField("你的回答", answer ?? "");
                        request.Answers[id] = answer;
                        allAnswered &= !string.IsNullOrWhiteSpace(answer);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("请查看本次操作内容，再决定是否允许。", MessageType.Info);
                    DrawSelectable(request.Details);
                }
                // Read-only mode never silently gains write or additional filesystem permissions.
                bool writableRequest = request.Method == "item/fileChange/requestApproval" ||
                    request.Method == "item/permissions/requestApproval";
                bool canAccept = allAnswered && (!writableRequest || Session.AllowEdits);
                if (!canAccept && !question)
                    EditorGUILayout.HelpBox("当前为仅讨论模式。拒绝本次请求后，可勾选允许修改并重新提交任务。", MessageType.None);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!canAccept))
                        if (GUILayout.Button(question ? "提交回答" : "允许这一次")) Session.Answer(request, true);
                    if (GUILayout.Button("拒绝")) Session.Answer(request, false);
                }
            }
        }

        void AttachSelection()
        {
            var selected = Selection.activeObject;
            var scene = SceneManager.GetActiveScene();
            draft += "\n\nUnity 上下文：\n场景：" + scene.path + (scene.isDirty ? "（有未保存修改）" : "");
            if (selected == null) { draft += "\n未选中对象。"; return; }
            draft += "\n选中对象：" + selected.name + "\n资源路径：" + AssetDatabase.GetAssetPath(selected);
            if (selected is GameObject go)
                draft += "\n组件：" + string.Join(", ", go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name));
        }
        bool CanSend() { return Session != null && !queuedSend && !Session.Busy &&
            !Session.Connecting && !pastingImage && !EditorApplication.isCompiling &&
            (!string.IsNullOrWhiteSpace(draft) || imageDraft.Count > 0); }
        string SendLabel()
        {
            if (Session.AllowEdits && EditorApplication.isPlayingOrWillChangePlaymode) return "停止试玩并发送";
            if (!Session.Connected) return "连接并发送";
            if (!Session.Authenticated) return "登录 ChatGPT";
            return "发送";
        }
        string SendHint()
        {
            if (queuedSend) return "正在准备发送；草稿和图片已保留，可取消。";
            if (EditorApplication.isCompiling) return "Unity 正在编译，完成后即可发送。";
            if (pastingImage) return "正在读取剪贴板图片，请稍候。";
            if (Session.Busy) return "助手正在处理上一条消息，可点击停止。";
            if (!Session.Connected) return "连接已断开，点击右侧按钮可重连并发送。";
            if (!Session.Authenticated) return "需要登录 ChatGPT；登录期间会保留草稿和图片。";
            if (Session.AllowEdits && EditorApplication.isPlayingOrWillChangePlaymode) return "当前在 Play 模式，发送时会先停止试玩再修改代码。";
            return "";
        }
        void QueueSend()
        {
            if (!CanSend()) return;
            if (Session.Connected && !Session.Authenticated) { Run(Session.LoginAsync()); return; }
            queuedSend = true; queuedAllowEdits = Session.AllowEdits;
            queuedToken = Guid.NewGuid().ToString("N"); queuedUntil = DateTime.UtcNow.AddMinutes(2).Ticks;
            SessionState.SetBool("VoxelDemo.Codex.Send." + queuedToken, true);
            if (queuedAllowEdits && EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.isPlaying = false;
            Repaint();
        }
        void CancelQueuedSend()
        {
            if (!string.IsNullOrEmpty(queuedToken)) SessionState.EraseBool("VoxelDemo.Codex.Send." + queuedToken);
            queuedSend = false; queuedToken = null;
            if (this != null) Repaint();
        }
        void ContinueQueuedSend()
        {
            if (!queuedSend || processingQueue || Session == null) return;
            if (DateTime.UtcNow.Ticks > queuedUntil)
            { CancelQueuedSend(); Session.Status = "准备发送超时，草稿和图片仍保留，请重试。"; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || Session.Connecting || Session.Busy) return;
            if (queuedAllowEdits && EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorApplication.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode) return;
            RunQueuedSend();
        }
        async void RunQueuedSend()
        {
            processingQueue = true;
            var session = Session;
            try
            {
                if (!session.Connected) await session.ConnectAsync();
                if (this == null || Session != session || !queuedSend) return;
                if (!session.Connected || !session.Authenticated)
                {
                    CancelQueuedSend();
                    if (session.Connected) session.Status = "请先登录 ChatGPT，然后再次发送。草稿和图片仍保留。";
                    return;
                }
                if (queuedAllowEdits && EditorApplication.isPlayingOrWillChangePlaymode) return;
                session.AllowEdits = queuedAllowEdits;
                CancelQueuedSend(); // Clear intent before submitting, so reloads cannot submit twice.
                await DispatchDraftAsync();
            }
            catch (Exception e)
            { if (this != null && Session == session) { CancelQueuedSend(); session.Status = e.Message; } }
            finally { processingQueue = false; if (this != null) Repaint(); }
        }
        async Task DispatchDraftAsync()
        {
            string original = draft;
            string text = draft.Trim();
            int sent = Session.Messages.Count(m => m.role == "user");
            var session = Session;
            var images = imageDraft.ToArray();
            imageDraft.Clear();
            draft = ""; follow = true;
            imageNotice = "";
            try { await session.SendAsync(text, images.Select(a => a.path).ToArray()); }
            finally
            {
                if (Session == session && session.Messages.Count(m => m.role == "user") == sent)
                {
                    if (draft.Length == 0) draft = original;
                    foreach (var image in images) if (!imageDraft.Any(i => i.path == image.path)) imageDraft.Insert(0, image);
                }
                if (this != null) Repaint();
            }
        }

        public void AddImageFile(string path)
        {
            try
            {
                if (imageDraft.Count >= CodexImageAttachments.MaxImages) throw new IOException("一次最多添加 8 张图片。");
                var image = CodexImageAttachments.Import(path);
                if (imageDraft.Any(i => i.path == image.path)) { imageNotice = "这张图片已经附上了。"; return; }
                imageDraft.Add(image); imageNotice = "图片已附上，点击缩略图可预览。发送后助手才能看到。";
            }
            catch (Exception e) { imageNotice = e.Message; }
            Repaint();
        }

        async void PasteImage()
        {
            if (pastingImage) return;
            pastingImage = true; imageNotice = "正在读取剪贴板图片…"; Repaint();
            try
            {
                string[] files = await CodexImageAttachments.ReadClipboardAsync();
                if (this == null) return;
                if (files.Length == 0) imageNotice = "剪贴板里没有图片。请先截图或复制图片，再按 Ctrl+V。";
                foreach (string file in files) AddImageFile(file);
            }
            catch (Exception e) { if (this != null) imageNotice = e.Message; }
            finally { pastingImage = false; if (this != null) Repaint(); }
        }

        void HandleImageEvents()
        {
            if (queuedSend) return;
            var e = Event.current;
            bool pasteKey = e.type == EventType.KeyDown && e.keyCode == KeyCode.V && (e.control || e.command);
            bool pasteCommand = (e.type == EventType.ExecuteCommand || e.type == EventType.ValidateCommand) && e.commandName == "Paste";
            if ((pasteKey || pasteCommand) && CodexImageAttachments.ClipboardHasImage())
            {
                bool validate = e.type == EventType.ValidateCommand;
                e.Use();
                if (!validate) PasteImage();
                return;
            }
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            var paths = DragAndDrop.paths.Concat(DragAndDrop.objectReferences.Select(AssetDatabase.GetAssetPath))
                .Where(p => !string.IsNullOrEmpty(p) && CodexImageAttachments.IsSupported(p)).Distinct().ToArray();
            if (paths.Length == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (string path in paths) AddImageFile(path);
            }
            e.Use();
        }

        void DrawImageDraft()
        {
            if (imageDraft.Count == 0) return;
            imageScroll = EditorGUILayout.BeginScrollView(imageScroll, GUILayout.Height(112));
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var image in imageDraft.ToArray())
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(100)))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(new GUIContent(image.name, image.name), EditorStyles.miniLabel, GUILayout.Width(68));
                            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(18))) imageDraft.Remove(image);
                        }
                        var texture = GetImageTexture(image.path);
                        if (texture != null)
                        {
                            var rect = GUILayoutUtility.GetRect(88, 58);
                            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
                            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) CodexImagePreview.Open(image.path);
                        }
                        else GUILayout.Label("图片已丢失", EditorStyles.miniLabel);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        Texture2D GetImageTexture(string path)
        {
            if (imageTextures.TryGetValue(path, out var cached)) return cached;
            if (!File.Exists(path)) return null;
            var texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            if (!texture.LoadImage(File.ReadAllBytes(path))) { DestroyImmediate(texture); return null; }
            imageTextures[path] = texture;
            return texture;
        }
        async void Run(Task task)
        {
            try { await task; }
            catch (Exception e) { Session?.Add("system", e.Message); }
            Repaint();
        }
        void EnsureStyles()
        {
            if (bodyStyle != null) return;
            bodyStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false, fontSize = 13,
                padding = new RectOffset(5, 5, 5, 5) };
            inputStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true, fontSize = 13,
                padding = new RectOffset(7, 7, 7, 7) };
            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
        }
        void DrawSelectable(string text)
        {
            float width = Mathf.Max(230, position.width - 45);
            float height = Mathf.Max(25, bodyStyle.CalcHeight(new GUIContent(text), width));
            EditorGUILayout.SelectableLabel(text, bodyStyle, GUILayout.Height(height));
        }
    }
}
