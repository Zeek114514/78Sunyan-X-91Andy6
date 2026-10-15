using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 简易控制输入台：按 T 唤出输入框（按 / 唤出并自动带出斜杠），Enter 发送、Esc 取消。
/// 以 / 开头的内容当指令处理（见 ExecuteCommand），普通文字当聊天回显。
/// 对话框打开时鼠标解锁，交互脚本会暂停响应，避免打字误触。
/// </summary>
public class VoxelChat : MonoBehaviour
{
    const float LineHeight     = 22f;    // 单条消息高度
    const float LogVisibleTime = 6f;     // 关闭对话框后消息继续停留的秒数
    const int   MaxShownLines  = 8;      // 同时最多显示的消息条数
    const string InputControl  = "VoxelChatInput";

    /// <summary>对话框是否打开（交互和 HUD 靠它避让）</summary>
    public static bool IsOpen { get; private set; }

    static VoxelChat _instance;      // 给其他系统发消息用（Announce）

    VoxelPlayer _player;
    VoxelStats _stats;
    string _input = "";
    GUIStyle _body;
    GUIStyle _inputStyle;
    readonly List<string> _log = new List<string>();

    float _closeTime = -999f;      // 让开局时“距上次关闭很久”成立，不显示残留消息
    bool _focusPending;

    public void Init(VoxelPlayer player)
    {
        _player = player;
        _stats = player.GetComponent<VoxelStats>();   // Bootstrap 保证 VoxelStats 已先挂上
    }

    /// <summary>给其他系统发消息（受伤、阈值提示等），对话框没开也能看到（随日志淡出）</summary>
    public static void Announce(string message)
    {
        if (_instance != null) _instance.Log(message);
    }

    void Awake() => _instance = this;

    void Update()
    {
        if (VoxelGacha.IsOpen || VoxelMenu.IsOpen) return;      // 祈愿 / 菜单开着时不抢按键

        if (IsOpen)
        {
            // 打包运行时的兜底；编辑器里输入框聚焦时事件被 IMGUI 吃掉，轮询收不到
            if (Input.GetKeyDown(KeyCode.Escape))
                Close();
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                Submit();
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.T))          Open("");
            else if (Input.GetKeyDown(KeyCode.Slash)) Open("/");
        }
    }

    void OnGUI()
    {
        if (VoxelGacha.IsOpen || TitleScreen.IsOpen || VoxelMenu.IsOpen) return;   // 祈愿 / 菜单 / 开幕动画占满屏幕时，聊天日志先收起来

        EnsureStyles();

        if (IsOpen && TryHandleChatKeys()) return;

        if (IsOpen)
        {
            DrawLog(1f, Screen.height - 40f);
            DrawInput();
        }
        else
        {
            // 关闭后消息先停在血条上方，随时间淡出
            float age = Time.time - _closeTime;
            if (age < LogVisibleTime)
                DrawLog(1f - age / LogVisibleTime, Screen.height - 118f);
        }
    }

    /// <summary>
    /// 回车发送、Esc 取消。必须在 OnGUI 里用 Event 拦截：
    /// 聚焦的输入框会先吃掉键盘事件，Input.GetKeyDown 在编辑器里收不到。
    /// </summary>
    bool TryHandleChatKeys()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown) return false;

        if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
        {
            e.Use();
            Submit();
            return true;
        }
        if (e.keyCode == KeyCode.Escape)
        {
            e.Use();
            Close();
            return true;
        }
        return false;
    }

    void Open(string prefill)
    {
        IsOpen = true;
        _input = prefill;
        _focusPending = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Close()
    {
        IsOpen = false;
        _input = "";
        _closeTime = Time.time;
        Cursor.lockState = CursorLockMode.Locked;   // 关闭即回到游戏，不用再点一次画面
        Cursor.visible = false;
    }

    void Submit()
    {
        string text = _input.Trim();
        Close();

        if (text.Length == 0) return;

        if (text.StartsWith("/")) ExecuteCommand(text);
        else Log("<你> " + text);
    }

    void ExecuteCommand(string text)
    {
        string[] parts = text.Substring(1).Trim().Split(' ');

        switch (parts[0].ToLower())
        {
            case "fly":
                if (_player == null) return;
                _player.isFlying = !_player.isFlying;
                Log(_player.isFlying
                    ? "飞行已开启：空格上升 · Ctrl 下降 · 再输入 /fly 关闭"
                    : "飞行已关闭");
                break;

            case "eat":
                if (_stats == null) return;
                _stats.Eat();
                break;

            case "status":
                if (_stats == null) return;
                Log("生命 " + _stats.Health.ToString("F0") + "/100    饱食度 " + _stats.Hunger.ToString("F0") + "/100");
                if (_player != null)
                {
                    Vector3 p = _player.transform.position;
                    Log("坐标 " + p.x.ToString("F0") + ", " + p.y.ToString("F0") + ", " + p.z.ToString("F0"));
                }
                break;

            case "help":
                Log("可用指令：/fly 切换飞行    /eat 恢复饱食度    /status 查看状态");
                break;

            default:
                Log("未知指令：" + text + "（输入 /help 查看可用指令）");
                break;
        }
    }

    void Log(string message)
    {
        _log.Add(message);
        if (_log.Count > 50) _log.RemoveAt(0);
    }

    void DrawInput()
    {
        var rect = new Rect(8f, Screen.height - 34f, 480f, 26f);

        GUI.SetNextControlName(InputControl);
        _input = GUI.TextField(rect, _input, 120, _inputStyle);

        if (_focusPending)
        {
            GUI.FocusControl(InputControl);   // 打开时自动聚焦，省一次点击
            _focusPending = false;
        }
    }

    /// <summary>把最近的消息自下而上叠在 anchorY 上方，alpha &lt; 1 时整体淡出</summary>
    void DrawLog(float alpha, float anchorY)
    {
        if (_log.Count == 0) return;

        int shown = Mathf.Min(_log.Count, MaxShownLines);
        float y = anchorY;
        var bg = new Color(0f, 0f, 0f, 0.45f * alpha);

        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);

        for (int i = _log.Count - shown; i < _log.Count; i++)
        {
            y -= LineHeight;
            var rect = new Rect(8f, y, 560f, LineHeight);
            VoxelHud.FillRect(rect, bg);
            VoxelHud.LabelShadow(rect, _log[i], _body);
        }

        GUI.color = old;
    }

    void EnsureStyles()
    {
        if (_body != null) return;

        _body = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        _body.normal.textColor = Color.white;

        _inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
    }

    void OnDestroy()
    {
        IsOpen = false;
        _instance = null;   // 停止播放时清掉静态状态，不影响下一局
    }
}
