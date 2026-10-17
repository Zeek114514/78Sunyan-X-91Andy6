using UnityEngine;

/// <summary>
/// 开幕动画（标题画面）：黑场淡出 → 游戏名「琰神」浮出 → 公司名浮出 → 点击进入游戏。
///
/// 参考原神那套的做法：毛笔感的衬线字 + 金色 + 上下压暗 + 飘动的金色光点，
/// 背景就是真实生成的游戏世界（标题期间相机缓慢横移当运镜）。
///
/// 三条规则：
///   · **播放完才能点击**（需求原话"播放完点击进入游戏"）—— 动画没走完点击一律忽略；
///   · 标题期间**关掉玩家的三个输入组件**（移动 / 攻击 / 交互），
///     这样相机可以自由做运镜，玩家也不会在标题里乱跑；
///   · 通过 <see cref="IsOpen"/> 让 HUD / 小地图 / 宝箱 / 方碑一起闭嘴 —— 和项目里
///     VoxelChat / VoxelGacha / WorldMap 的用法一致，不加第二套机制。
///
/// 字体用系统里的中文楷体（STKaiti / 楷体 / SimSun 依次尝试）：楷体是毛笔笔锋，
/// 最接近原神那套标题字，而且字形一定正确 —— 用 AI 生图做 Logo 有把汉字画错的风险。
/// </summary>
public class TitleScreen : MonoBehaviour
{
    /// <summary>标题画面开着（其它系统照这个让出输入和屏幕）</summary>
    public static bool IsOpen { get; private set; }

    // ---------- 时间轴（秒） ----------

    const float FadeIn   = 0.85f;   // 黑场淡出
    const float LogoIn   = 1.30f;   // 游戏名浮出（从 FadeIn 开始）
    const float LineIn   = 0.55f;   // 金线展开
    const float CompIn   = 1.00f;   // 公司名浮出
    const float FadeOut  = 0.55f;   // 点击后收场

    float Ready => FadeIn + LogoIn + LineIn + CompIn;   // 到这之后才接受点击

    // ---------- 外观 ----------

    static readonly Color Gold      = new Color(0.98f, 0.85f, 0.52f);
    static readonly Color GoldDeep  = new Color(0.72f, 0.53f, 0.22f);
    static readonly Color GoldPale  = new Color(1.00f, 0.95f, 0.80f);

    const string GameName    = "琰神";
    const string CompanyName = "78Sunyan X 91Andy6 Prostitution Limit .inc";

    // ---------- 状态 ----------

    enum Phase { Intro, Waiting, Out }

    Phase _phase = Phase.Intro;
    float _t;                       // 从标题开始算的秒数（不受 timeScale 影响）

    VoxelPlayer _player;
    VoxelCombat _combat;
    VoxelInteraction _interaction;

    Camera _camRef;
    float _camStartYaw;

    GUIStyle _logoStyle, _companyStyle, _promptStyle, _taglineStyle;
    Font _titleFont;
    Texture2D _gradTop, _gradBottom;

    readonly System.Collections.Generic.List<Vector2> _motes = new System.Collections.Generic.List<Vector2>();

    // ---------- 初始化 ----------

    /// <summary>由 VoxelBootstrap 在最后调用。传进来的三个组件会在标题期间被关掉</summary>
    public void Init(Camera cam, VoxelPlayer player, VoxelCombat combat, VoxelInteraction interaction)
    {
        IsOpen = true;

        _camRef = cam;
        _player = player;
        _combat = combat;
        _interaction = interaction;

        Cursor.lockState = CursorLockMode.None;      // 标题期间不锁鼠标
        Cursor.visible = true;

        // 标题期间玩家不能动：关掉这三个组件（相机因此空出来做运镜）
        if (_player != null) _player.enabled = false;
        if (_combat != null) _combat.enabled = false;
        if (_interaction != null) _interaction.enabled = false;

        if (_camRef != null) _camStartYaw = _camRef.transform.eulerAngles.y;

        // 开幕主题曲：作者原创的《孙狗大冒险》（Resources/Audio/bgm/theme.mp3），
        // 点"进入游戏"后由 Exit() 切回开放世界的循环曲
        if (VoxelAudio.Instance != null) VoxelAudio.Instance.PlayBgm(VoxelAudio.BgmTheme, 1.45f);

        BuildFont();
        BuildGradients();
        BuildMotes();
    }

    void BuildFont()
    {
        // 楷体优先（毛笔笔锋，最像原神那套标题字），找不到再往回落
        _titleFont = Font.CreateDynamicFontFromOSFont(
            new[] { "STKaiti", "KaiTi", "楷体", "STZhongsong", "SimSun", "Microsoft YaHei", "Arial" }, 120);
    }

    /// <summary>做一个 1×64 的竖直渐变贴图（拉伸到全屏就是一个柔和的压暗），比画几百条横线便宜</summary>
    void BuildGradients()
    {
        _gradTop = MakeGradient(true);
        _gradBottom = MakeGradient(false);
    }

    static Texture2D MakeGradient(bool fromTop)
    {
        const int h = 64;
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
        {
            float t = (float)y / (h - 1);                 // 0 = 底, 1 = 顶
            if (!fromTop) t = 1f - t;
            float a = Mathf.Pow(t, 1.8f) * 0.82f;         // 越靠边越黑
            tex.SetPixel(0, y, new Color(0.03f, 0.035f, 0.05f, a));
        }
        tex.Apply();
        return tex;
    }

    void BuildMotes()
    {
        // 飘动的金色小光点：固定哈希出初始位置，运行时按正弦漂
        for (int i = 0; i < 46; i++)
        {
            float x = LowPoly.Hash01(i, 7, 2001);
            float y = LowPoly.Hash01(i, 11, 2002);
            _motes.Add(new Vector2(x, y));
        }
    }

    void OnDestroy()
    {
        IsOpen = false;
        if (_gradTop != null) Destroy(_gradTop);
        if (_gradBottom != null) Destroy(_gradBottom);
    }

    // ---------- 主循环 ----------

    void Update()
    {
        _t += Time.unscaledDeltaTime;

        if (_phase == Phase.Intro)
        {
            if (_t >= Ready) _phase = Phase.Waiting;
        }
        else if (_phase == Phase.Waiting)
        {
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) ||
                Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                _phase = Phase.Out;
                _t = 0f;
            }
        }
        else // Out
        {
            if (_t >= FadeOut)
            {
                Exit();
                return;
            }
        }

        DriftCamera();
    }

    /// <summary>背景运镜：相机极慢地横移 + 轻微起伏，让标题后面是"活的"世界</summary>
    void DriftCamera()
    {
        if (_camRef == null) return;

        float k = Mathf.Min(_t, Ready + 4f);
        float yaw = _camStartYaw + k * 1.5f;                       // 每秒 1.5 度，几乎是察觉不到的缓移
        float pitch = Mathf.Sin(k * 0.35f) * 1.2f;

        _camRef.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    /// <summary>收场：把相机还给玩家、重新锁鼠标、打开输入组件</summary>
    void Exit()
    {
        if (_camRef != null) _camRef.transform.localRotation = Quaternion.identity;

        if (_player != null) _player.enabled = true;
        if (_combat != null) _combat.enabled = true;
        if (_interaction != null) _interaction.enabled = true;

        IsOpen = false;
        Cursor.lockState = CursorLockMode.Locked;      // 点一下就进游戏并抓住鼠标（和原神一样）
        Cursor.visible = false;

        // 标题放完了：切回开放世界的环境 BGM
        if (VoxelAudio.Instance != null) VoxelAudio.Instance.PlayBgm(VoxelAudio.BgmWorld);

        Destroy(gameObject);
    }

    // ---------- 画面 ----------

    void EnsureStyles()
    {
        if (_logoStyle != null) return;

        _logoStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, font = _titleFont };
        _companyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, font = _titleFont };
        _promptStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, font = _titleFont };
        _taglineStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, font = _titleFont };
    }

    void OnGUI()
    {
        EnsureStyles();

        float w = Screen.width, h = Screen.height;
        float outFade = _phase == Phase.Out ? Mathf.Clamp01(_t / FadeOut) : 0f;

        // ---- 上下压暗（黑场淡出就是"上层黑色从 1 收到 0.55"） ----
        float topAlpha = 1f - Mathf.Clamp01(_t / FadeIn) * 0.45f;
        topAlpha = Mathf.Max(topAlpha, outFade);

        DrawGradients(w, h, topAlpha);

        // ---- 金色光点 ----
        DrawMotes(w, h, Mathf.Clamp01(_t / FadeIn));

        // ---- 游戏名 ----
        float logoA = Mathf.Clamp01((_t - FadeIn) / LogoIn);
        if (logoA > 0.001f)
        {
            float ease = 1f - Mathf.Pow(1f - logoA, 3f);                 // 快出慢收
            float glow = Mathf.Clamp01((_t - FadeIn - LogoIn) / 0.9f);   // 浮出之后再补一层辉光

            _logoStyle.fontSize = Mathf.RoundToInt(h * 0.165f);
            float scale = Mathf.Lerp(1.10f, 1.00f, ease);
            DrawTitle(GameName, w, h * 0.40f, scale, ease * (1f - outFade), glow);
        }

        // ---- 金线分隔 ----
        float lineA = Mathf.Clamp01((_t - FadeIn - LogoIn) / LineIn);
        if (lineA > 0.001f) DrawDivider(w, h * 0.505f, lineA * (1f - outFade), h);

        // ---- 公司名 ----
        float compA = Mathf.Clamp01((_t - FadeIn - LogoIn - LineIn) / CompIn);
        if (compA > 0.001f)
        {
            _companyStyle.fontSize = Mathf.RoundToInt(h * 0.0285f);
            float a = compA * (1f - outFade);

            Rect r = new Rect(0f, h * 0.545f, w, h * 0.04f);
            Outline(r, CompanyName, _companyStyle, 1.5f, a * 0.75f);
            GUI.color = new Color(0.86f, 0.90f, 0.94f, a);
            GUI.Label(r, CompanyName, _companyStyle);
            GUI.color = Color.white;

            // 副标题：一句小字，让标题画面不那么空
            _taglineStyle.fontSize = Mathf.RoundToInt(h * 0.019f);
            Rect t = new Rect(0f, h * 0.605f, w, h * 0.03f);
            Outline(t, "开放世界 · 元素方碑 · 雪山", _taglineStyle, 1.2f, a * 0.45f);
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, a * 0.55f);
            GUI.Label(t, "开放世界 · 元素方碑 · 雪山", _taglineStyle);
            GUI.color = Color.white;
        }

        // ---- 点击进入 ----
        if (_phase == Phase.Waiting)
        {
            float pulse = 0.62f + 0.38f * Mathf.Sin(Time.unscaledTime * 3.2f);
            float a = (1f - outFade) * pulse;

            _promptStyle.fontSize = Mathf.RoundToInt(h * 0.024f);
            Rect r = new Rect(0f, h * 0.855f, w, h * 0.035f);

            Outline(r, "点击进入游戏", _promptStyle, 1.5f, a * 0.8f);
            GUI.color = new Color(GoldPale.r, GoldPale.g, GoldPale.b, a);
            GUI.Label(r, "点击进入游戏", _promptStyle);
            GUI.color = Color.white;
        }

        // ---- 收场黑幕 ----
        if (outFade > 0f)
        {
            GUI.color = new Color(0f, 0f, 0f, outFade);
            GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }

    /// <summary>画标题字：先描边（八个方向的深色副本）再叠金色主体，最后盖一层上浅下深的渐变感</summary>
    void DrawTitle(string text, float w, float centerY, float scale, float alpha, float glow)
    {
        if (alpha <= 0.001f) return;

        _logoStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.165f * scale);
        Rect r = new Rect(0f, centerY - _logoStyle.fontSize * 0.75f, w, _logoStyle.fontSize * 1.5f);

        // 辉光：外圈几层低透明度的金色
        if (glow > 0.01f)
        {
            float g = glow * alpha * 0.20f;
            for (int i = 1; i <= 3; i++)
            {
                float o = i * 2.2f * scale;
                GUI.color = new Color(Gold.r, Gold.g, Gold.b, g / i);
                GUI.Label(new Rect(r.x, r.y - o, r.width, r.height), text, _logoStyle);
                GUI.Label(new Rect(r.x, r.y + o, r.width, r.height), text, _logoStyle);
                GUI.Label(new Rect(r.x - o, r.y, r.width, r.height), text, _logoStyle);
                GUI.Label(new Rect(r.x + o, r.y, r.width, r.height), text, _logoStyle);
            }
        }

        // 描边
        float s = 2.4f * scale;
        GUI.color = new Color(0.10f, 0.07f, 0.03f, alpha * 0.92f);
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (dx != 0 || dy != 0)
                    GUI.Label(new Rect(r.x + dx * s, r.y + dy * s, r.width, r.height), text, _logoStyle);

        // 主体：上浅下深，做出金属渐变的感觉
        GUI.color = new Color(Gold.r, Gold.g, Gold.b, alpha);
        GUI.Label(new Rect(r.x, r.y, r.width, r.height), text, _logoStyle);

        GUI.color = new Color(GoldPale.r, GoldPale.g, GoldPale.b, alpha * 0.45f);
        GUI.Label(new Rect(r.x, r.y - _logoStyle.fontSize * 0.035f, r.width, r.height), text, _logoStyle);

        GUI.color = new Color(GoldDeep.r, GoldDeep.g, GoldDeep.b, alpha * 0.5f);
        GUI.Label(new Rect(r.x, r.y + _logoStyle.fontSize * 0.045f, r.width, r.height), text, _logoStyle);

        GUI.color = Color.white;
    }

    /// <summary>标题下面那条金线：中间一个小菱形，两边细线往外展开</summary>
    void DrawDivider(float w, float y, float a, float h)
    {
        float cx = w * 0.5f;
        float half = Mathf.Lerp(0f, w * 0.19f, a);
        float th = Mathf.Max(1.5f, h * 0.0018f);

        GUI.color = new Color(Gold.r, Gold.g, Gold.b, a * 0.85f);
        GUI.DrawTexture(new Rect(cx - half, y, half, th), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx, y, half, th), Texture2D.whiteTexture);

        // 菱形（把方块转 45 度）
        float d = h * 0.011f;
        var m = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, new Vector2(cx, y + th * 0.5f));
        GUI.DrawTexture(new Rect(cx - d * 0.5f, y + th * 0.5f - d * 0.5f, d, d), Texture2D.whiteTexture);
        GUI.matrix = m;

        GUI.color = Color.white;
    }

    void DrawGradients(float w, float h, float alpha)
    {
        float g = h * 0.34f;

        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(new Rect(0f, 0f, w, g), _gradTop, ScaleMode.StretchToFill);
        GUI.DrawTexture(new Rect(0f, h - g, w, g), _gradBottom, ScaleMode.StretchToFill);
        GUI.color = Color.white;
    }

    void DrawMotes(float w, float h, float alpha)
    {
        if (alpha <= 0.01f) return;

        float size = Mathf.Max(2f, h * 0.0035f);

        for (int i = 0; i < _motes.Count; i++)
        {
            float bx = _motes[i].x, by = _motes[i].y;
            float t = Time.unscaledTime;

            // 缓慢上飘 + 左右轻摆
            float x = (bx + Mathf.Sin(t * 0.20f + i) * 0.012f) * w;
            float y = (1f - Mathf.Repeat(by + t * 0.006f + i * 0.013f, 1f)) * h;

            float twinkle = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(t * 1.1f + i * 1.7f));
            float a = alpha * twinkle * 0.42f;

            GUI.color = new Color(GoldPale.r, GoldPale.g, GoldPale.b, a);

            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f + t * 8f + i * 11f, new Vector2(x, y));
            GUI.DrawTexture(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), Texture2D.whiteTexture);
            GUI.matrix = m;
        }

        GUI.color = Color.white;
    }

    /// <summary>给一行字画一层深色描边：IMGUI 没有描边功能，就把同一行字画八个偏移副本垫在底下</summary>
    void Outline(Rect r, string text, GUIStyle style, float off, float alpha)
    {
        GUI.color = new Color(0f, 0f, 0f, alpha);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                GUI.Label(new Rect(r.x + dx * off, r.y + dy * off, r.width, r.height), text, style);
            }
        }
        GUI.color = Color.white;
    }
}
