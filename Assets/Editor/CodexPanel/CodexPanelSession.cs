using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace VoxelDemo.Codex.Editor
{
    [Serializable] public sealed class ChatMessage
    {
        public string role, text, itemId;
        public string[] imagePaths;
        public ChatMessage(string role, string text, string itemId = null, string[] imagePaths = null)
        { this.role = role; this.text = text; this.itemId = itemId; this.imagePaths = imagePaths; }
    }
    public sealed class PanelRequest
    {
        public JToken Id;
        public string Method;
        public JObject Payload;
        public string Details;
        public readonly Dictionary<string, string> Answers = new Dictionary<string, string>();
    }

    public sealed class CodexPanelSession : IDisposable
    {
        const string Instructions = "你是 VoxelDemo 的 Unity 开发助手。用简洁中文交流，可以自然地称呼自己小猫娘，但技术结论要准确。" +
            "当前工作目录就是唯一开发项目。先遵循 AGENTS.md，保持游戏玩法；不操作旧团结项目。" +
            "不要删除 .meta、升级引擎、卸载软件、发布代码或更改账号设置，除非用户明确要求。" +
            "Unity 正开着，不要启动第二个编辑器访问同一项目，不要结束用户的编辑器进程。" +
            "修改完说明改了什么、实际做了哪些验证。不要声称已经编译或试玩，除非有真实结果。" +
            "本面板的图片入口是‘添加图片’、‘粘贴图片’、Ctrl+V 或拖入 PNG/JPG，发送前有缩略图，可点×移除。" +
            "不要编造未实现的界面按钮或操作。图片是随消息发送的真实附件，不必要求用户放进 Assets。";
        public readonly List<ChatMessage> Messages = new List<ChatMessage>();
        public readonly List<PanelRequest> Requests = new List<PanelRequest>();
        public readonly List<string> RecentErrors = new List<string>();
        public string Executable;
        public string Status = "未连接";
        public string Model = "Codex 默认模型";
        public string ThreadId { get; private set; }
        public string ActiveTurn { get; private set; }
        public bool Connected { get; private set; }
        public bool Authenticated { get; private set; }
        public bool Busy { get; private set; }
        public bool Connecting { get; private set; }
        public bool AllowEdits;
        public string Project { get; }
        public event Action Changed;
        CodexAppServerClient client;
        bool disposed, locked, filesChanged, threadLoaded;
        string loginId;
        readonly string statePath;
        readonly string executablePref;
        readonly bool ephemeral;
        readonly Dictionary<string, JObject> items = new Dictionary<string, JObject>();

        public CodexPanelSession(string stateName = "session", bool ephemeral = false)
        {
            this.ephemeral = ephemeral;
            Project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            statePath = Path.Combine(Project, "Library", "CodexPanel", stateName + ".json");
            executablePref = "VoxelDemo.Codex.Executable";
            Executable = EditorPrefs.GetString(executablePref, "");
            if (!File.Exists(Executable)) Executable = FindExecutable();
            Load();
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            Application.logMessageReceived += CaptureError;
        }

        public static string FindExecutable()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = Path.Combine(local, "OpenAI", "Codex", "bin");
            if (Directory.Exists(root))
            {
                var found = Directory.GetDirectories(root).Select(d => Path.Combine(d, "codex.exe"))
                    .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (found != null) return found;
            }
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { var p = Path.Combine(dir.Trim('"'), "codex.exe"); if (File.Exists(p)) return p; }
                catch (ArgumentException) { }
            }
            return "";
        }

        public async Task ConnectAsync()
        {
            if (Connecting || disposed) return;
            Disconnect(false);
            Connecting = true; Status = "正在连接…"; Touch();
            var connection = new CodexAppServerClient(); client = connection;
            connection.Notification += OnNotification;
            connection.ServerRequest += OnServerRequest;
            connection.Disconnected += text => { if (client == connection) { Disconnect(false); Status = text; Touch(); } };
            try
            {
                connection.Start(Executable, Project);
                await connection.RequestAsync("initialize", new JObject { ["clientInfo"] = new JObject {
                    ["name"] = "voxel_demo_unity", ["title"] = "VoxelDemo Unity Codex", ["version"] = "0.1.0" } });
                if (!IsCurrent(connection)) return;
                connection.Notify("initialized");
                Connected = true;
                EditorPrefs.SetString(executablePref, Executable);
                await RefreshAccountAsync(connection);
                if (!IsCurrent(connection)) return;
                Status = Authenticated ? "已连接 · 可以开始对话" : "已连接 · 请登录 ChatGPT";
            }
            catch (Exception e) { if (IsCurrent(connection)) { Disconnect(false); Status = e.Message; } }
            finally { if (IsCurrent(connection) || client == null) { Connecting = false; Touch(); } }
        }

        bool IsCurrent(CodexAppServerClient connection) { return !disposed && client == connection; }

        async Task RefreshAccountAsync(CodexAppServerClient connection)
        {
            var account = await connection.RequestAsync("account/read", new JObject { ["refreshToken"] = false });
            if (IsCurrent(connection)) Authenticated = account["account"] is JObject;
        }

        public async Task LoginAsync()
        {
            var connection = client;
            if (!Connected || connection == null || loginId != null) return;
            try
            {
                Status = "正在打开 ChatGPT 登录…"; Touch();
                var result = await connection.RequestAsync("account/login/start", new JObject { ["type"] = "chatgpt" });
                if (!IsCurrent(connection)) return;
                loginId = (string)result["loginId"];
                if (Uri.TryCreate((string)result["authUrl"], UriKind.Absolute, out var uri) && uri.Scheme == "https")
                { Application.OpenURL(uri.AbsoluteUri); Status = "请在浏览器完成登录，完成后自动连接"; }
                else throw new IOException("未收到有效的登录链接，请重新连接。");
            }
            catch (Exception e) { if (IsCurrent(connection)) { loginId = null; Status = e.Message; } }
            Touch();
        }

        public async Task SendAsync(string text, string[] imagePaths = null)
        {
            if (Busy || !Connected || !Authenticated || (string.IsNullOrWhiteSpace(text) && (imagePaths == null || imagePaths.Length == 0)) || disposed) return;
            if (AllowEdits && EditorApplication.isPlayingOrWillChangePlaymode)
            { Status = "请先停止 Play，再发送开发任务。"; Touch(); return; }
            JArray input;
            try { input = CodexImageAttachments.BuildInput(text, imagePaths); }
            catch (IOException e) { Status = e.Message; Touch(); return; }
            var connection = client;
            Busy = true; filesChanged = false; items.Clear();
            Status = "正在准备对话…"; Touch();
            try
            {
                if (!threadLoaded)
                {
                    var settings = new JObject { ["cwd"] = Project, ["approvalPolicy"] = "on-request",
                        ["sandbox"] = AllowEdits ? "workspace-write" : "read-only", ["developerInstructions"] = Instructions };
                    JObject response;
                    if (string.IsNullOrEmpty(ThreadId))
                    { settings["ephemeral"] = ephemeral; response = await connection.RequestAsync("thread/start", settings); }
                    else
                    {
                        settings["threadId"] = ThreadId;
                        settings["excludeTurns"] = true;
                        response = await connection.RequestAsync("thread/resume", settings);
                    }
                    if (!IsCurrent(connection)) return;
                    ThreadId = (string)response["thread"]?["id"] ?? ThreadId;
                    Model = (string)response["model"] ?? Model;
                    if (string.IsNullOrEmpty(ThreadId)) throw new IOException("未能创建对话。");
                    threadLoaded = true;
                }
                Messages.Add(new ChatMessage("user", text ?? "", null, imagePaths?.ToArray()));
                Save(); Touch();
                LockRefresh();
                Status = "正在思考…"; Touch();
                var turn = await connection.RequestAsync("turn/start", new JObject {
                    ["threadId"] = ThreadId, ["approvalPolicy"] = "on-request",
                    ["sandboxPolicy"] = AllowEdits ? new JObject {
                        ["type"] = "workspaceWrite", ["writableRoots"] = new JArray(Project),
                        ["networkAccess"] = false, ["excludeSlashTmp"] = true, ["excludeTmpdirEnvVar"] = true
                    } : new JObject { ["type"] = "readOnly", ["networkAccess"] = false },
                    ["input"] = input });
                if (!IsCurrent(connection)) return;
                var state = (string)turn["turn"]?["status"];
                // Completion can arrive before the request continuation on fast turns.
                if (Busy && state == "inProgress") ActiveTurn = (string)turn["turn"]?["id"];
                else if (state != "inProgress" && Busy) Complete(turn["turn"] as JObject);
            }
            catch (Exception e)
            {
                if (IsCurrent(connection))
                {
                    Add("system", "请求未完成：" + e.Message);
                    // After an ambiguous timeout, disconnect before accepting another turn.
                    Disconnect(false); Status = "请重新连接后查看对话，避免重复提交任务。"; Touch();
                }
            }
        }

        public async Task StopAsync()
        {
            var connection = client;
            if (connection == null || !Busy) return;
            if (string.IsNullOrEmpty(ActiveTurn)) { Disconnect(); return; }
            try
            {
                Status = "正在停止…"; Touch();
                await connection.RequestAsync("turn/interrupt", new JObject { ["threadId"] = ThreadId, ["turnId"] = ActiveTurn }, 15);
            }
            catch { if (IsCurrent(connection)) Disconnect(); }
        }

        void OnNotification(string method, JObject p)
        {
            if (disposed) return;
            if (method == "account/login/completed")
            {
                loginId = null;
                if ((bool?)p["success"] == true) FinishLogin();
                else { Status = "登录未完成，请重试。"; Touch(); }
                return;
            }
            if (method == "serverRequest/resolved")
            { Requests.RemoveAll(r => JToken.DeepEquals(r.Id, p["requestId"])); Touch(); return; }
            if ((string)p["threadId"] != ThreadId) return;
            switch (method)
            {
                case "turn/started":
                    Busy = true; ActiveTurn = (string)p["turn"]?["id"]; Status = "正在思考…"; break;
                case "item/agentMessage/delta":
                    Append((string)p["itemId"], (string)p["delta"] ?? ""); Status = "正在回复…"; break;
                case "item/started":
                    var item = p["item"] as JObject;
                    if (item != null)
                    {
                        items[(string)item["id"] ?? ""] = item;
                        if ((string)item["type"] == "commandExecution") Status = "正在处理项目命令…";
                        if ((string)item["type"] == "fileChange") { filesChanged = true; Status = "正在修改项目文件…"; }
                    }
                    break;
                case "item/completed":
                    var done = p["item"] as JObject;
                    if (done == null) break;
                    var id = (string)done["id"];
                    if ((string)done["type"] == "agentMessage")
                    {
                        var existing = Messages.LastOrDefault(m => m.itemId == id);
                        if (existing != null) existing.text = (string)done["text"] ?? existing.text;
                        else Add("assistant", (string)done["text"] ?? "", id);
                    }
                    if ((string)done["type"] == "fileChange")
                    {
                        filesChanged = true;
                        var paths = (done["changes"] as JArray)?.Select(c => (string)c["path"]).Where(s => s != null);
                        Add("tool", "文件修改 · " + (string)done["status"] + "\n" + string.Join("\n", paths ?? Enumerable.Empty<string>()));
                    }
                    if ((string)done["type"] == "commandExecution")
                        Add("tool", "命令 · " + (string)done["status"] + "\n" + (string)done["command"] +
                            "\n" + Truncate((string)done["aggregatedOutput"] ?? "", 6000));
                    Save(); break;
                case "turn/completed": Complete(p["turn"] as JObject); break;
                case "error":
                    var error = (string)p["error"]?["message"] ?? "Codex 请求遇到错误。";
                    Add("system", error); break;
            }
            Touch();
        }

        async void FinishLogin()
        {
            var connection = client;
            if (connection == null) return;
            try { await RefreshAccountAsync(connection); if (IsCurrent(connection)) { Status = Authenticated ? "登录成功 · 可以开始对话" : "请重新登录"; Touch(); } }
            catch (Exception e) { if (IsCurrent(connection)) { Status = e.Message; Touch(); } }
        }

        void OnServerRequest(JToken id, string method, JObject p)
        {
            if ((string)p["threadId"] != ThreadId)
            { client.ReplyUnsupported(id); return; }
            bool supported = method == "item/commandExecution/requestApproval" || method == "item/fileChange/requestApproval" ||
                method == "item/permissions/requestApproval" || method == "item/tool/requestUserInput" || method == "tool/requestUserInput";
            if (!supported) { client.ReplyUnsupported(id); Add("system", "当前面板不支持此工具请求，已拒绝：" + method); return; }
            var details = new JObject();
            foreach (var field in new[] { "reason", "command", "cwd", "grantRoot", "permissions", "networkApprovalContext", "questions" })
                if (p[field] != null) details[field] = p[field].DeepClone();
            if (items.TryGetValue((string)p["itemId"] ?? "", out var item) && item["changes"] != null)
                details["changes"] = item["changes"].DeepClone();
            Requests.Add(new PanelRequest { Id = id.DeepClone(), Method = method, Payload = p, Details = details.ToString(Formatting.Indented) });
            Status = "等待你的回答或确认"; Touch();
        }

        public void Answer(PanelRequest request, bool accept)
        {
            if (client == null || !Requests.Contains(request)) return;
            JObject result;
            if (request.Method.EndsWith("requestUserInput", StringComparison.Ordinal))
            {
                var answers = new JObject();
                foreach (var pair in request.Answers) answers[pair.Key] = new JObject { ["answers"] = new JArray(pair.Value) };
                result = new JObject { ["answers"] = accept ? answers : new JObject() };
            }
            else if (request.Method == "item/permissions/requestApproval")
                result = new JObject { ["permissions"] = accept ? request.Payload["permissions"]?.DeepClone() ?? new JObject() : new JObject(), ["scope"] = "turn" };
            else result = new JObject { ["decision"] = accept ? "accept" : "decline" };
            try
            {
                client.Reply(request.Id, result);
                Requests.Remove(request);
                Add("system", accept ? "已发送你的确认或回答。" : "已拒绝该请求。");
                Status = "正在继续…"; Touch();
            }
            catch (Exception e) { Status = e.Message; Touch(); }
        }

        void Append(string id, string delta)
        {
            var message = Messages.LastOrDefault(m => m.itemId == id && m.role == "assistant");
            if (message == null) { message = new ChatMessage("assistant", "", id); Messages.Add(message); }
            message.text += delta;
        }

        void Complete(JObject turn)
        {
            Busy = false; ActiveTurn = null; Requests.Clear();
            var state = (string)turn?["status"] ?? "completed";
            Status = state == "completed" ? "已完成" : state == "interrupted" ? "已停止" : "任务未完成";
            if (turn?["error"] != null && turn["error"].Type != JTokenType.Null)
                Add("system", (string)turn["error"]["message"] ?? "任务失败。");
            UnlockRefresh(); Save(); Touch();
        }

        void LockRefresh()
        {
            if (locked) return;
            EditorApplication.LockReloadAssemblies();
            AssetDatabase.DisallowAutoRefresh();
            locked = true;
        }
        void UnlockRefresh()
        {
            if (!locked) return;
            locked = false;
            AssetDatabase.AllowAutoRefresh();
            EditorApplication.UnlockReloadAssemblies();
            if (filesChanged || AllowEdits) EditorApplication.delayCall += () => AssetDatabase.Refresh();
        }

        public void NewConversation()
        {
            if (Busy || Connecting) return;
            ThreadId = null; threadLoaded = false; Messages.Clear(); items.Clear(); Requests.Clear();
            Status = Connected ? "新对话已就绪" : "未连接"; Save(); Touch();
        }

        internal async Task ArchiveSelfTestAsync()
        {
            if (Path.GetFileName(statePath) != "validation.json" || string.IsNullOrEmpty(ThreadId) || Busy)
                throw new InvalidOperationException("Only the idle self-test conversation may be archived here.");
            await client.RequestAsync("thread/archive", new JObject { ["threadId"] = ThreadId });
            ThreadId = null; threadLoaded = false; Save();
        }

        public void Disconnect(bool notify = true)
        {
            bool interrupted = Busy;
            var old = client; client = null;
            old?.Dispose();
            Connected = false; Authenticated = false; Busy = false; Connecting = false;
            ActiveTurn = null; loginId = null; threadLoaded = false; Requests.Clear(); UnlockRefresh();
            if (notify)
            {
                Status = "已断开";
                if (interrupted) Add("system", "连接已断开，本面板的本地任务已停止。重新连接可继续此对话；已写入的文件不会自动撤销。");
            }
            Save(); Touch();
        }

        void CaptureError(string message, string stack, LogType kind)
        {
            if (kind != LogType.Error && kind != LogType.Exception && kind != LogType.Assert) return;
            if (RecentErrors.Count == 8) RecentErrors.RemoveAt(0);
            RecentErrors.Add(Truncate(message + "\n" + stack, 2500));
        }
        void Update() { client?.Pump(); }
        void BeforeReload() { Disconnect(); }
        void PlayStateChanged(PlayModeStateChange state)
        { if (state == PlayModeStateChange.ExitingEditMode) Disconnect(); }
        public void Add(string role, string text, string id = null)
        { Messages.Add(new ChatMessage(role, text, id)); Save(); Touch(); }
        void Touch() { Changed?.Invoke(); }
        public static string Truncate(string text, int limit) { return text.Length <= limit ? text : text.Substring(0, limit) + "\n…"; }

        void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(statePath));
                var data = new JObject { ["threadId"] = ThreadId, ["model"] = Model,
                    ["messages"] = JArray.FromObject(Messages.Skip(Math.Max(0, Messages.Count - 200))) };
                var temporary = statePath + ".tmp";
                File.WriteAllText(temporary, data.ToString(), new System.Text.UTF8Encoding(false));
                if (File.Exists(statePath)) File.Replace(temporary, statePath, null);
                else File.Move(temporary, statePath);
            }
            catch (IOException) { Status = "对话暂未保存到本机，请检查磁盘空间。"; }
        }
        void Load()
        {
            try
            {
                if (!File.Exists(statePath)) return;
                var data = JObject.Parse(File.ReadAllText(statePath));
                ThreadId = (string)data["threadId"]; Model = (string)data["model"] ?? Model;
                var rows = data["messages"]?.ToObject<List<ChatMessage>>();
                if (rows != null) Messages.AddRange(rows);
            }
            catch (Exception e) when (e is IOException || e is JsonException)
            { Status = "本地对话记录未能读取；原文件已保留。"; }
        }
        public void Dispose()
        {
            if (disposed) return;
            Disconnect(false); disposed = true;
            EditorApplication.update -= Update;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            EditorApplication.playModeStateChanged -= PlayStateChanged;
            Application.logMessageReceived -= CaptureError;
        }
    }
}
