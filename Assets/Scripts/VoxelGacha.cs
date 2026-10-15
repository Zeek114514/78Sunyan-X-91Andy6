using UnityEngine;

/// <summary>
/// 抽卡页面：按 P 开关，照着原神祈愿页的排版做——左边一排卡池页签、中间主卡池展位、
/// 右上角货币（原石 / 纠缠之缘）、右下角「祈愿 ×1 / ×10」按钮，背景是暗色星空。
/// 祈愿一次花 1 个纠缠之缘（十连花 10 个），扣完只在页面底部提示一句——抽出什么内容还没做。
/// 打开时解锁鼠标（和对话框一样），移动、挖掘、转视角都会暂停。
/// </summary>
public class VoxelGacha : MonoBehaviour
{
    /// <summary>界面是否打开（HUD / 交互 / 对话框靠它避让）</summary>
    public static bool IsOpen { get; private set; }

    // 排版按这个设计尺寸算好，再整体缩放到实际分辨率
    const float RefWidth  = 1280f;
    const float RefHeight = 720f;

    const int SingleFate = 1;        // 祈愿一次花几个纠缠之缘
    const int TenFate    = 10;

    static readonly string[] BannerNames = { "角色活动祈愿", "武器活动祈愿", "常驻祈愿" };
    static readonly string[] BannerSubs  = { "余数之章", "神铸赋形", "奔行世间" };
    static readonly string[] BannerTips  =
    {
        "本期五星角色概率 UP（占位，卡池内容待定）",
        "本期五星武器概率 UP（占位，卡池内容待定）",
        "常驻卡池（占位，卡池内容待定）",
    };

    // 原神那套配色：深蓝底 + 香槟金描边
    static readonly Color Backdrop = new Color(0.03f, 0.04f, 0.09f, 0.95f);
    static readonly Color Panel    = new Color(0.07f, 0.09f, 0.17f, 0.88f);
    static readonly Color PanelLit = new Color(0.13f, 0.16f, 0.27f, 0.95f);
    static readonly Color ArtBox   = new Color(0.05f, 0.07f, 0.14f, 0.95f);
    static readonly Color Gold     = new Color(0.92f, 0.84f, 0.64f);
    static readonly Color GoldDeep = new Color(0.52f, 0.43f, 0.25f);
    static readonly Color Text     = new Color(0.93f, 0.95f, 0.99f);
    static readonly Color TextDim  = new Color(0.62f, 0.67f, 0.79f);
    static readonly Color Star5    = new Color(0.99f, 0.79f, 0.35f);
    static readonly Color Star4    = new Color(0.80f, 0.62f, 0.98f);
    static readonly Color Hover    = new Color(0.22f, 0.27f, 0.42f, 0.96f);

    VoxelInteraction _inventory;
    Texture2D _gemIcon;
    Texture2D _fateIcon;
    Vector3[] _stars;                       // 星空：x/y 是归一化坐标，z 是大小
    float _scale = 1f, _ox, _oy;            // 设计坐标 → 屏幕坐标
    float _styledScale = -1f;
    int _banner;
    string _toast = "";
    float _toastUntil;

    readonly Rect[] _tabs = new Rect[BannerNames.Length];
    Rect _single, _ten, _detail, _close;

    GUIStyle _title, _sub, _body, _small, _button;

    public void Init(VoxelInteraction inventory)
    {
        _inventory = inventory;
        _gemIcon  = Resources.Load<Texture2D>("PrimogemIcon");
        _fateIcon = Resources.Load<Texture2D>("IntertwinedFateIcon");   // 生成器出的纠缠之缘图标
        BuildStars();
    }

    // ---------- 开关 ----------

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            if (IsOpen) Close();
            else if (!VoxelChat.IsOpen && !VoxelMenu.IsOpen) Open();      // 对话框 / 菜单开着的时候不抢
            return;
        }

        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void Open()
    {
        IsOpen = true;
        Cursor.lockState = CursorLockMode.None;      // 解锁鼠标才能点按钮
        Cursor.visible = true;
    }

    void Close()
    {
        IsOpen = false;
        Cursor.lockState = CursorLockMode.Locked;    // 关掉就直接回到游戏
        Cursor.visible = false;
    }

    void OnDestroy() => IsOpen = false;              // 停播放时清掉静态状态

    /// <summary>开一次就定下来的星空：每帧随机会闪得很乱</summary>
    void BuildStars()
    {
        const int count = 160;
        _stars = new Vector3[count];

        var rng = new System.Random(20261003);
        for (int i = 0; i < count; i++)
            _stars[i] = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(),
                                    0.6f + (float)rng.NextDouble() * 2.0f);
    }

    // ---------- 画界面 ----------

    void OnGUI()
    {
        if (!IsOpen) return;

        Layout();
        EnsureStyles();

        DrawBackdrop();
        DrawHeader();
        DrawTabs();
        DrawBanner();
        DrawWishButtons();
        DrawFooter();

        HandleClicks();
    }

    /// <summary>按屏幕算一次缩放和居中偏移，后面所有坐标都按 1280×720 的设计尺寸写</summary>
    void Layout()
    {
        _scale = Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight);
        _ox = (Screen.width  - RefWidth  * _scale) * 0.5f;
        _oy = (Screen.height - RefHeight * _scale) * 0.5f;

        for (int i = 0; i < _tabs.Length; i++)
            _tabs[i] = new Rect(38f, 190f + i * 56f, 158f, 48f);

        _detail = new Rect(1122f, 584f, 120f, 28f);
        _single = new Rect(742f,  626f, 220f, 62f);
        _ten    = new Rect(974f,  620f, 268f, 74f);
        _close  = new Rect(1206f, 24f,  40f,  40f);
    }

    Rect S(Rect design) => new Rect(_ox + design.x * _scale, _oy + design.y * _scale,
                                    design.width * _scale, design.height * _scale);

    float Px(float value) => value * _scale;

    void DrawBackdrop()
    {
        VoxelHud.FillRect(new Rect(0f, 0f, Screen.width, Screen.height), Backdrop);

        // 星空：每颗亮度按自己的相位慢慢明灭
        for (int i = 0; i < _stars.Length; i++)
        {
            Vector3 star = _stars[i];
            float twinkle = 0.55f + 0.45f * Mathf.Sin(Time.time * 0.8f + i * 1.7f);
            float size = Px(star.z);

            VoxelHud.FillRect(new Rect(_ox + star.x * RefWidth * _scale,
                                       _oy + star.y * RefHeight * _scale, size, size),
                              new Color(0.85f, 0.90f, 1f, 0.15f + 0.5f * twinkle));
        }

        // 卡池后面的柔光：没有圆形可画，用几层渐小的方框叠出朦胧感
        Glow(760f, 320f, 360f, new Color(0.35f, 0.55f, 0.95f, 0.18f));
    }

    void Glow(float cx, float cy, float radius, Color color, int steps = 12)
    {
        for (int i = steps; i >= 1; i--)
        {
            float r = radius * i / steps;
            float alpha = color.a * (1f - (float)i / steps);
            VoxelHud.FillRect(S(new Rect(cx - r, cy - r, r * 2f, r * 2f)),
                              new Color(color.r, color.g, color.b, alpha));
        }
    }

    void DrawHeader()
    {
        LabelD(new Rect(38f, 26f, 320f, 40f), "祈愿", _title, Gold);
        LabelD(new Rect(40f, 66f, 420f, 20f), "WISH · 按 P / Esc 关闭", _small);
        VoxelHud.FillRect(S(new Rect(38f, 90f, 120f, 2f)), Gold);

        DrawChip(new Rect(806f, 24f, 190f, 42f), "原石", PrimogemCount.ToString(), _gemIcon,
                 new Color(0.36f, 0.72f, 0.95f));
        DrawChip(new Rect(1006f, 24f, 190f, 42f), "纠缠之缘", FateCount.ToString(), _fateIcon,
                 new Color(0.62f, 0.45f, 0.95f));

        Rect close = S(_close);
        bool hot = close.Contains(Event.current.mousePosition);
        FrameD(_close, hot ? Hover : Panel, hot ? Gold : GoldDeep, Px(1f));
        LabelD(_close, "✕", _button, hot ? Gold : TextDim);
    }

    /// <summary>右上角那种货币小条：左边图标（没有图标就画一块兜底色），右边名字和数量</summary>
    void DrawChip(Rect d, string name, string amount, Texture2D icon, Color fallback)
    {
        FrameD(d, Panel, GoldDeep, Px(1f));

        Rect r = S(d);
        var box = new Rect(r.x + Px(6f), r.y + Px(6f), r.height - Px(12f), r.height - Px(12f));

        if (icon != null) GUI.DrawTexture(box, icon, ScaleMode.ScaleAndCrop);
        else VoxelHud.FillRect(box, fallback);

        float textX = box.xMax + Px(10f);
        float textW = r.xMax - textX - Px(8f);
        VoxelHud.LabelShadow(new Rect(textX, r.y + Px(4f), textW, Px(16f)), name, _small);
        VoxelHud.LabelShadow(new Rect(textX, r.y + Px(20f), textW, Px(20f)), "× " + amount, _body);
    }

    void DrawTabs()
    {
        for (int i = 0; i < _tabs.Length; i++)
        {
            bool on  = i == _banner;
            bool hot = S(_tabs[i]).Contains(Event.current.mousePosition);

            FrameD(_tabs[i], on || hot ? PanelLit : Panel, on ? Gold : GoldDeep, Px(1f));
            if (on) VoxelHud.FillRect(S(new Rect(_tabs[i].x, _tabs[i].y, 4f, _tabs[i].height)), Gold);

            LabelD(_tabs[i], BannerNames[i], _button, on ? Gold : TextDim);
        }
    }

    void DrawBanner()
    {
        Rect frame = new Rect(212f, 122f, 1030f, 458f);
        FrameD(frame, Panel, GoldDeep, Px(2f));
        CornersD(frame, Gold, 18f, 3f);

        Rect art = new Rect(232f, 142f, 990f, 330f);
        FrameD(art, ArtBox, new Color(GoldDeep.r, GoldDeep.g, GoldDeep.b, 0.7f), Px(1f));

        LabelD(new Rect(art.x, art.y + 8f, art.width, 30f),
               BannerNames[_banner] + " · " + BannerSubs[_banner], _body, Gold);

        Rect tag = new Rect(art.x + 12f, art.y + 12f, 96f, 24f);
        FrameD(tag, new Color(0.35f, 0.29f, 0.16f, 0.9f), Gold, Px(1f));
        LabelD(tag, "限时祈愿", _sub, Gold);

        DrawFeatured();
        DrawFourStars();

        LabelD(new Rect(frame.x, 484f, frame.width, 22f), BannerTips[_banner], _sub);
        LabelD(new Rect(frame.x, 512f, frame.width, 22f),
               "保底进度 0 / 90 · 五星基础概率 0.600% · 单次祈愿 " + SingleFate + " 个纠缠之缘", _sub);
    }

    /// <summary>中间那个大展位（占位）：原神是角色立绘，这里先用文字框撑着</summary>
    void DrawFeatured()
    {
        Rect card = new Rect(472f, 178f, 506f, 202f);
        FrameD(card, new Color(0.10f, 0.12f, 0.22f, 0.92f), GoldDeep, Px(1f));
        CornersD(card, Gold, 14f, 2f);

        LabelD(new Rect(card.x, card.y + 14f, card.width, 28f), "★★★★★", _button, Star5);
        LabelD(new Rect(card.x, card.y + 54f, card.width, 34f), "占位 · 五星角色", _button, Text);
        LabelD(new Rect(card.x, card.y + 96f, card.width, 24f), "UP！", _sub, Gold);
        LabelD(new Rect(card.x, card.y + 126f, card.width, 24f), "卡池内容待定，这里先留位置", _sub);
    }

    void DrawFourStars()
    {
        const float w = 130f, h = 58f, gap = 16f;
        float total = w * 3f + gap * 2f;
        float x = 640f - total * 0.5f;

        for (int i = 0; i < 3; i++)
        {
            Rect card = new Rect(x + i * (w + gap), 404f, w, h);
            FrameD(card, new Color(0.09f, 0.10f, 0.19f, 0.92f), GoldDeep, Px(1f));
            LabelD(new Rect(card.x, card.y + 6f, card.width, 22f), "★★★★", _sub, Star4);
            LabelD(new Rect(card.x, card.y + 28f, card.width, 22f), "四星占位", _sub);
        }
    }

    void DrawWishButtons()
    {
        if (DrawButton(_detail, "详情", "", Panel, _sub)) Toast("卡池详情也还没做");

        if (DrawButton(_single, "祈愿 ×1", "◆ " + SingleFate + " 个纠缠之缘", Panel, _button) &&
            TrySpendFate(SingleFate))
            Toast("抽卡内容待定：先做了个界面（花掉 " + SingleFate + " 个纠缠之缘）");

        if (DrawButton(_ten, "祈愿 ×10", "◆ " + TenFate + " 个纠缠之缘", PanelLit, _button) &&
            TrySpendFate(TenFate))
            Toast("抽卡内容待定：先做了个界面（花掉 " + TenFate + " 个纠缠之缘）");
    }

    /// <summary>画一个按钮：悬停变亮，松开左键返回 true</summary>
    bool DrawButton(Rect d, string label, string cost, Color fill, GUIStyle style)
    {
        bool hot = S(d).Contains(Event.current.mousePosition);

        FrameD(d, hot ? Hover : fill, hot ? Gold : GoldDeep, Px(2f));
        CornersD(d, hot ? Gold : GoldDeep, 12f, 3f);

        if (string.IsNullOrEmpty(cost))
        {
            LabelD(d, label, style, hot ? Gold : Text);
        }
        else
        {
            LabelD(new Rect(d.x, d.y + d.height * 0.14f, d.width, d.height * 0.46f), label, style, hot ? Gold : Text);
            LabelD(new Rect(d.x, d.y + d.height * 0.56f, d.width, d.height * 0.36f), cost, _sub);
        }

        return hot && Event.current.type == EventType.MouseUp && Event.current.button == 0;
    }

    void DrawFooter()
    {
        LabelD(new Rect(38f, 640f, 620f, 22f), "按 P / Esc 关闭 · 抽卡内容待定，先把界面搭出来", _small);

        if (Time.time >= _toastUntil) return;

        Rect box = S(new Rect(430f, 590f, 420f, 34f));
        VoxelHud.FillRect(box, new Color(0f, 0f, 0f, 0.62f));
        VoxelHud.LabelShadow(box, _toast, _sub);
    }

    void HandleClicks()
    {
        Event e = Event.current;
        if (e.type != EventType.MouseUp || e.button != 0) return;

        if (S(_close).Contains(e.mousePosition))
        {
            Close();
            e.Use();
            return;
        }

        for (int i = 0; i < _tabs.Length; i++)
        {
            if (!S(_tabs[i]).Contains(e.mousePosition)) continue;

            _banner = i;
            e.Use();
            return;
        }
    }

    void Toast(string message)
    {
        _toast = message;
        _toastUntil = Time.time + 2.5f;
    }

    int PrimogemCount => _inventory != null ? _inventory.PrimogemCount : 0;
    int FateCount     => _inventory != null ? _inventory.FateCount : 0;

    /// <summary>花掉纠缠之缘（一次祈愿花 1 个）；不够就提示一句，返回有没有扣成功</summary>
    bool TrySpendFate(int count)
    {
        if (_inventory == null) return false;

        if (_inventory.FateCount < count)
        {
            Toast("纠缠之缘不够（现在 " + _inventory.FateCount + " 个，需要 " + count + " 个）");
            return false;
        }

        _inventory.FateCount -= count;
        return true;
    }

    // ---------- 画方框 / 写字的小工具 ----------

    /// <summary>带描边的方框：先铺底色，再压四条边。thickness 按屏幕像素给（调用处记得 Px）</summary>
    void FrameD(Rect d, Color fill, Color line, float thickness)
    {
        Rect r = S(d);
        VoxelHud.FillRect(r, fill);
        VoxelHud.FillRect(new Rect(r.x, r.y, r.width, thickness), line);
        VoxelHud.FillRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), line);
        VoxelHud.FillRect(new Rect(r.x, r.y, thickness, r.height), line);
        VoxelHud.FillRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), line);
    }

    /// <summary>四角的小折线：原神那种框角装饰。length / thickness 按设计尺寸给（内部会缩放）</summary>
    void CornersD(Rect d, Color line, float length, float thickness)
    {
        Rect r = S(d);
        float len = Px(length), t = Px(thickness);

        VoxelHud.FillRect(new Rect(r.x, r.y, len, t), line);
        VoxelHud.FillRect(new Rect(r.x, r.y, t, len), line);
        VoxelHud.FillRect(new Rect(r.xMax - len, r.y, len, t), line);
        VoxelHud.FillRect(new Rect(r.xMax - t, r.y, t, len), line);
        VoxelHud.FillRect(new Rect(r.x, r.yMax - t, len, t), line);
        VoxelHud.FillRect(new Rect(r.x, r.yMax - len, t, len), line);
        VoxelHud.FillRect(new Rect(r.xMax - len, r.yMax - t, len, t), line);
        VoxelHud.FillRect(new Rect(r.xMax - t, r.yMax - len, t, len), line);
    }

    /// <summary>在设计坐标里写字（居中/居左由样式决定）</summary>
    void LabelD(Rect d, string text, GUIStyle style, Color? color = null)
    {
        Color old = style.normal.textColor;
        if (color.HasValue) style.normal.textColor = color.Value;

        VoxelHud.LabelShadow(S(d), text, style);

        style.normal.textColor = old;
    }

    void EnsureStyles()
    {
        if (_title != null && Mathf.Approximately(_styledScale, _scale)) return;
        _styledScale = _scale;

        _title = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(26f * _scale), alignment = TextAnchor.MiddleLeft,
        };
        _title.normal.textColor = Gold;

        _sub = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(13f * _scale), alignment = TextAnchor.MiddleCenter,
        };
        _sub.normal.textColor = TextDim;

        _body = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(15f * _scale), alignment = TextAnchor.MiddleCenter,
        };
        _body.normal.textColor = Text;

        _small = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(12f * _scale), alignment = TextAnchor.MiddleLeft,
        };
        _small.normal.textColor = TextDim;

        _button = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(18f * _scale), alignment = TextAnchor.MiddleCenter,
        };
        _button.normal.textColor = Text;
    }
}
