using UnityEngine;

/// <summary>
/// 用 IMGUI 画准星、物品栏和提示，省得搭 Canvas。
/// </summary>
public class VoxelHud : MonoBehaviour
{
    VoxelInteraction _interaction;
    VoxelPlayer _player;
    VoxelStats _stats;
    Texture2D _gemIcon;
    Texture2D _fateIcon;
    GUIStyle _body;
    GUIStyle _title;
    GUIStyle _number;

    public void Init(VoxelInteraction interaction, VoxelPlayer player, VoxelStats stats)
    {
        _interaction = interaction;
        _player = player;
        _stats = stats;
    }

    void OnGUI()
    {
        if (_interaction == null) return;
        EnsureStyles();

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            DrawCrosshair();
            DrawHotbar();
            DrawVitals();
            DrawStatus();
        }
        else if (!VoxelChat.IsOpen && !VoxelGacha.IsOpen && !WorldMap.IsOpen)   // 对话框 / 祈愿 / 大地图打开时把屏幕让出去
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

        // 原石图标（生成器下载进 Resources 的 PNG）；没有也不碍事，格子会画兜底色块
        _gemIcon  = Resources.Load<Texture2D>("PrimogemIcon");
        _fateIcon = Resources.Load<Texture2D>("IntertwinedFateIcon");
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

    /// <summary>底部物品栏：10 格，当前格子抬起并加白框，空手格只画暗底</summary>
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
            byte id = VoxelInteraction.Palette[i];

            float y = on ? y0 - 6f : y0;                       // 选中的抬起来
            var r = new Rect(x0 + i * (slot + gap), y, slot, slot);

            // 外框（选中的更白更亮）
            FillRect(r, on ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.42f));

            // 内块 = 方块代表色；空手格画暗底；原石 / 纠缠之缘画生成的图标 + 数量角标
            var inner = new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f);
            if (id == VoxelInteraction.Primogem)
            {
                if (_gemIcon != null)
                    GUI.DrawTexture(inner, _gemIcon, ScaleMode.ScaleAndCrop);
                else
                    FillRect(inner, new Color(0.36f, 0.84f, 1f));

                LabelShadow(new Rect(r.x + r.width - 34f, r.y + 2f, 32f, 16f),
                            "×" + _interaction.PrimogemCount, _number);
            }
            else if (id == VoxelInteraction.IntertwinedFate)
            {
                if (_fateIcon != null)
                    GUI.DrawTexture(inner, _fateIcon, ScaleMode.ScaleAndCrop);
                else
                    FillRect(inner, new Color(0.62f, 0.45f, 0.95f));

                LabelShadow(new Rect(r.x + r.width - 34f, r.y + 2f, 32f, 16f),
                            "×" + _interaction.FateCount, _number);
            }
            else if (id != VoxelWorld.Air)
            {
                FillRect(inner, VoxelWorld.UiColor(id));

                // 底部压一条暗色，做出方块侧面的厚度感
                FillRect(new Rect(inner.x, inner.y + inner.height - 11f, inner.width, 11f),
                         new Color(0f, 0f, 0f, 0.22f));
            }
            else
            {
                FillRect(inner, new Color(0f, 0f, 0f, 0.35f));
            }

            // 编号（带描边，压在色块上也看得清）；第 10 格对应按键 0
            string key = i == n - 1 ? "0" : (i + 1).ToString();
            var numRect = new Rect(r.x, r.y + r.height - 19f, r.width, 17f);
            LabelShadow(numRect, key, _number);
        }
    }

    /// <summary>生命/饱食度条：物品栏上方左右各一条，各 10 格代表 100 点</summary>
    void DrawVitals()
    {
        if (_stats == null) return;

        float cx = Screen.width * 0.5f;
        float y  = Screen.height - 92f;

        LabelShadow(new Rect(cx - 125f, y - 16f, 120f, 14f),
                    "生命 " + _stats.Health.ToString("F0"), _number);
        SegmentedBar(cx - 125f, y, _stats.Health, new Color(0.83f, 0.18f, 0.18f));

        // 饿到跑不动时颜色变暗，一眼能看出状态不对
        bool starving = _stats.Hunger < VoxelStats.NoSprintBelow;
        LabelShadow(new Rect(cx + 5f, y - 16f, 120f, 14f),
                    "饱食 " + _stats.Hunger.ToString("F0"), _number);
        SegmentedBar(cx + 5f, y, _stats.Hunger,
                     starving ? new Color(0.72f, 0.30f, 0.06f) : new Color(0.93f, 0.62f, 0.12f));
    }

    /// <summary>10 格分段条（值 0~100），最后一格按数值比例部分填充。
    /// 玩家 HUD 和 NPC 名牌下方的血条共用；cell / h 是格宽和条高，NPC 头顶用更小的尺寸</summary>
    public static void SegmentedBar(float x, float y, float value, Color fill, float cell = 11f, float h = 12f)
    {
        const float gap = 1f;

        for (int i = 0; i < 10; i++)
        {
            float frac = Mathf.Clamp01((value - i * 10f) / 10f);
            var r = new Rect(x + i * (cell + gap), y, cell, h);

            // 底色跟着 fill 的透明度走：NPC 名牌渐隐时不会剩下一排黑块
            FillRect(r, new Color(0f, 0f, 0f, 0.5f * fill.a));
            if (frac > 0f)
                FillRect(new Rect(r.x, r.y, cell * frac, h), fill);
        }
    }

    void DrawStatus()
    {
        var box = new Rect(12f, Screen.height - 74f, 520f, 62f);
        FillRect(box, new Color(0f, 0f, 0f, 0.42f));

        string line1, line2;
        if (_player != null && _player.isFlying)
        {
            line1 = "WASD 移动    Shift 加速    空格 上升    Ctrl 下降";
            line2 = "T 对话框    P 祈愿    M 世界地图    再输入 /fly 关闭飞行";
        }
        else
        {
            line1 = "WASD 移动    Shift 疾跑    空格 跳跃    左键 攻击    Q 丢掉手上的道具";
            line2 = "T 对话框    P 祈愿    M 世界地图    滚轮 换道具    Esc 释放鼠标";
        }

        GUI.Label(new Rect(box.x + 12f, box.y + 8f,  box.width, 24f), line1, _body);
        GUI.Label(new Rect(box.x + 12f, box.y + 30f, box.width, 24f), line2, _body);
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
        GUI.Label(new Rect(box.x, box.y + 128f, w, 24f), "左键挖 · 右键放 · 数字键/滚轮换方块 · Esc 释放鼠标", _body);
        GUI.Label(new Rect(box.x, box.y + 152f, w, 24f), "T 对话框 · P 祈愿 · M 世界地图 · 输入 /fly 切换飞行", _body);
    }

    public static void FillRect(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    /// <summary>先画一层深色偏移再画本色，保证在任何底色上都能看清</summary>
    public static void LabelShadow(Rect r, string text, GUIStyle style)
    {
        Color old = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.72f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
        style.normal.textColor = old;
        GUI.Label(r, text, style);
    }
}
