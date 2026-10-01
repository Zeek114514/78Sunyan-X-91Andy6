using UnityEngine;

/// <summary>
/// 用 IMGUI 画准星、物品栏和提示，省得搭 Canvas。
/// </summary>
public class VoxelHud : MonoBehaviour
{
    VoxelInteraction _interaction;
    GUIStyle _body;
    GUIStyle _title;
    GUIStyle _number;

    public void Init(VoxelInteraction interaction) => _interaction = interaction;

    void OnGUI()
    {
        if (_interaction == null || _interaction.World == null) return;
        EnsureStyles();

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            DrawCrosshair();
            DrawHotbar();
            DrawStatus();
        }
        else
        {
            DrawStartPanel();
        }
    }

    void EnsureStyles()
    {
        if (_body != null) return;

        _body = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        _body.normal.textColor = Color.white;

        _title = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
        _title.normal.textColor = Color.white;

        _number = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.LowerCenter };
        _number.normal.textColor = new Color(1f, 1f, 1f, 0.92f);
    }

    void DrawCrosshair()
    {
        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;

        var dark = new Color(0f, 0f, 0f, 0.55f);
        var light = new Color(1f, 1f, 1f, 0.9f);

        // 先画一层稍大的深色做描边，保证在任何背景上都看得见
        FillRect(new Rect(cx - 1.5f, cy - 11f, 3f, 22f), dark);
        FillRect(new Rect(cx - 11f, cy - 1.5f, 22f, 3f), dark);
        FillRect(new Rect(cx - 1f, cy - 10f, 2f, 20f), light);
        FillRect(new Rect(cx - 10f, cy - 1f, 20f, 2f), light);
    }

    /// <summary>底部物品栏：4 格，当前方块抬起并加白框</summary>
    void DrawHotbar()
    {
        const float slot = 54f, gap = 8f;
        int n = VoxelInteraction.Palette.Length;

        float total = n * slot + (n - 1) * gap;
        float x0 = (Screen.width - total) * 0.5f;
        float y0 = Screen.height - slot - 20f;

        for (int i = 0; i < n; i++)
        {
            bool on = i == _interaction.SelectedIndex;

            float y = on ? y0 - 6f : y0;                       // 选中的抬起来
            var r = new Rect(x0 + i * (slot + gap), y, slot, slot);

            // 外框（选中的更白更亮）
            FillRect(r, on ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.42f));

            // 内块 = 方块代表色
            var inner = new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f);
            FillRect(inner, VoxelWorld.UiColor(VoxelInteraction.Palette[i]));

            // 底部压一条暗色，做出方块侧面的厚度感
            FillRect(new Rect(inner.x, inner.y + inner.height - 11f, inner.width, 11f),
                     new Color(0f, 0f, 0f, 0.22f));

            // 编号（带描边，压在色块上也看得清）
            var numRect = new Rect(r.x, r.y + r.height - 19f, r.width, 17f);
            LabelShadow(numRect, (i + 1).ToString(), _number);
        }
    }

    void DrawStatus()
    {
        var box = new Rect(12f, Screen.height - 74f, 520f, 62f);
        FillRect(box, new Color(0f, 0f, 0f, 0.42f));

        GUI.Label(new Rect(box.x + 12f, box.y + 8f,  box.width, 24f), "WASD 移动    Shift 疾跑    空格 跳跃", _body);
        GUI.Label(new Rect(box.x + 12f, box.y + 30f, box.width, 24f), "左键 挖方块    右键 放方块    1-4 换方块    Esc 释放鼠标", _body);
    }

    void DrawStartPanel()
    {
        float w = 460f, h = 190f;
        var box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

        FillRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
        FillRect(box, new Color(0f, 0f, 0f, 0.72f));

        GUI.Label(new Rect(box.x, box.y + 18f, w, 34f), "VOXEL DEMO", _title);
        GUI.Label(new Rect(box.x, box.y + 70f,  w, 24f), "点击画面开始", _body);
        GUI.Label(new Rect(box.x, box.y + 104f, w, 24f), "WASD 移动 · Shift 疾跑 · 空格 跳跃", _body);
        GUI.Label(new Rect(box.x, box.y + 128f, w, 24f), "左键挖 · 右键放 · 1-4 换方块 · Esc 释放鼠标", _body);
    }

    static void FillRect(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    /// <summary>先画一层深色偏移再画本色，保证在任何底色上都能看清</summary>
    static void LabelShadow(Rect r, string text, GUIStyle style)
    {
        Color old = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.72f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
        style.normal.textColor = old;
        GUI.Label(r, text, style);
    }
}
