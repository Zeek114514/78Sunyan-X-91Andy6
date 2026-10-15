using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 抽卡页面：按 P 开关，照着原神祈愿页的排版做——左边一排卡池页签、中间主卡池展位、
/// 右上角货币（原石 / 纠缠之缘）、右下角「祈愿 ×1 / ×10」按钮，背景是暗色星空。
///
/// 奖池、概率、保底都是真的：五星基础 0.6%（第 74 抽起软保底，90 抽必出），四星基础 5.1%（10 抽必出），
/// 角色池 / 武器池还有「大小保底」——出五星时有 50%（武器池 75%）是本池 UP，歪了下次必 UP。
/// 抽出来的东西逐张翻牌展示，五星额外闪一层金光，同时记进抽卡历史、并收进背包。
/// 保底进度与总抽数写进 PlayerPrefs，重启游戏不重置。
///
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

    // ---------- 概率与保底 ----------
    const int   HardPity5 = 90;      // 第 90 抽必出五星
    const int   HardPity4 = 10;      // 第 10 抽必出四星
    const float Base5     = 0.006f;  // 五星基础概率
    const float Base4     = 0.051f;  // 四星基础概率
    const int   SoftStart = 74;      // 从第 74 抽起进软保底，每多一抽五星概率 +6%
    const float SoftStep  = 0.06f;
    const float Up4Chance = 0.5f;    // 四星有 50% 是本池 UP

    const int HistoryMax = 8;        // 抽卡历史最多留几条
    const string SavePrefix = "CodelyDemo.Wish.";   // 保底进度的 PlayerPrefs 前缀（和成就那套一致）

    static readonly string[] BannerNames = { "角色活动祈愿", "武器活动祈愿", "常驻祈愿" };
    static readonly string[] BannerSubs  = { "余数之章", "神铸赋形", "奔行世间" };
    static readonly string[] BannerTips  =
    {
        "五星角色「孙琰 · 余数」概率 UP",
        "五星武器「雪顶之刃」「港门长枪」概率 UP",
        "角色与武器同池 · 五星不分 UP",
    };

    // ---------- 奖品 ----------

    /// <summary>一件奖品：名字、星级（3 / 4 / 5）、是角色还是武器、备注（元素或武器类型）</summary>
    class Wish
    {
        public readonly string Name, Note;
        public readonly int Rarity;
        public readonly bool IsCharacter;

        public Wish(string name, int rarity, bool isCharacter, string note)
        {
            Name = name; Rarity = rarity; IsCharacter = isCharacter; Note = note;
        }
    }

    /// <summary>一个卡池的奖品构成。Up5Chance = 出五星时「是本池 UP」的概率（常驻池给 1，也就是不分 UP）</summary>
    class Banner
    {
        public Wish[]  Up5, Standard5, Up4, Standard4, Standard3;
        public float   Up5Chance;
    }

    /// <summary>三星奖池（三个池子共用）</summary>
    static readonly Wish[] Pool3 =
    {
        new Wish("新手长剑", 3, false, "单手剑"),
        new Wish("新手猎弓", 3, false, "弓"),
        new Wish("新手法器", 3, false, "法器"),
        new Wish("新手长柄", 3, false, "长柄武器"),
        new Wish("新手大剑", 3, false, "双手剑"),
    };

    static readonly Banner[] Banners =
    {
        // 角色活动祈愿「余数之章」：五星 UP 孙琰，四星 UP 三个；歪了出的是常驻五星角色
        new Banner
        {
            Up5       = new[] { new Wish("孙琰", 5, true, "余数") },
            Standard5 = new[]
            {
                new Wish("Andy",       5, true, "风"),
                new Wish("湖心城主",   5, true, "岩"),
                new Wish("将军府雷使", 5, true, "雷"),
            },
            Up4 = new[]
            {
                new Wish("港门商会长", 4, true, "璃月"),
                new Wish("雪顶猎手",   4, true, "冰"),
                new Wish("鸣神巫女",   4, true, "稻妻"),
            },
            Standard4 = new[]
            {
                new Wish("湖畔卫兵",   4, true, "蒙德"),
                new Wish("喀斯特矿工", 4, true, "璃月"),
                new Wish("神像祭司",   4, true, "七天神像"),
                new Wish("喀斯特金石", 4, false, "法器"),
                new Wish("鸣神短刀",   4, false, "单手剑"),
            },
            Standard3 = Pool3,
            Up5Chance = 0.5f,
        },

        // 武器活动祈愿「神铸赋形」：双五星 UP，五星 UP 概率 75%
        new Banner
        {
            Up5 = new[]
            {
                new Wish("雪顶之刃", 5, false, "单手剑"),
                new Wish("港门长枪", 5, false, "长柄武器"),
            },
            Standard5 = new[]
            {
                new Wish("将军府太刀", 5, false, "单手剑"),
                new Wish("湖心城法典", 5, false, "法器"),
            },
            Up4 = new[]
            {
                new Wish("湖畔猎弓", 4, false, "弓"),
                new Wish("峰林重剑", 4, false, "双手剑"),
                new Wish("鸣神法杖", 4, false, "法器"),
            },
            Standard4 = new[]
            {
                new Wish("喀斯特金石", 4, false, "法器"),
                new Wish("鸣神短刀",   4, false, "单手剑"),
                new Wish("雪顶长弓",   4, false, "弓"),
                new Wish("港门巨斧",   4, false, "双手剑"),
                new Wish("神像法典",   4, false, "法器"),
            },
            Standard3 = Pool3,
            Up5Chance = 0.75f,
        },

        // 常驻祈愿「奔行世间」：角色与武器同池，出五星时不再分 UP（Standard5 / Standard4 用不上）
        new Banner
        {
            Up5 = new[]
            {
                new Wish("Andy",       5, true, "风"),
                new Wish("湖心城主",   5, true, "岩"),
                new Wish("将军府雷使", 5, true, "雷"),
                new Wish("雪顶之刃",   5, false, "单手剑"),
                new Wish("港门长枪",   5, false, "长柄武器"),
                new Wish("将军府太刀", 5, false, "单手剑"),
                new Wish("湖心城法典", 5, false, "法器"),
            },
            Standard5 = null,
            Up4 = new[]
            {
                new Wish("湖畔卫兵",   4, true, "蒙德"),
                new Wish("喀斯特矿工", 4, true, "璃月"),
                new Wish("神像祭司",   4, true, "七天神像"),
                new Wish("喀斯特金石", 4, false, "法器"),
                new Wish("鸣神短刀",   4, false, "单手剑"),
            },
            Standard4 = null,
            Standard3 = Pool3,
            Up5Chance = 1f,
        },
    };

    // ---------- 图标 ----------
    // 图标放在 Assets/Resources/GachaIcons/ 下，按下表的键命名（不带扩展名）。
    // 角色按名字查表、武器按类型推 —— 加新奖品只要名字和类型没变就自动对上图，奖池数据一行都不用改。
    // 缺图不会出问题：IconOf 返回 null，界面自动退回原来的纯文字排版，可以一张一张慢慢补。
    // 三星会先找 "<键>_basic" 的朴素版，没有就退回同类型的通用图。

    const string IconFolder = "GachaIcons/";

    /// <summary>角色名 → 图标键</summary>
    static readonly Dictionary<string, string> CharacterIcons = new Dictionary<string, string>
    {
        { "孙琰",       "char_sunyan" },
        { "Andy",       "char_andy" },
        { "湖心城主",   "char_lake_city_lord" },
        { "将军府雷使", "char_tenshu_general" },
        { "港门商会长", "char_harbor_merchant" },
        { "雪顶猎手",   "char_snow_hunter" },
        { "鸣神巫女",   "char_narukami_miko" },
        { "湖畔卫兵",   "char_lakeside_guard" },
        { "喀斯特矿工", "char_karst_miner" },
        { "神像祭司",   "char_statue_priest" },
    };

    /// <summary>武器类型（Wish.Note）→ 图标键。武器按类型共用一张图，星级靠卡片描边颜色区分。</summary>
    static readonly Dictionary<string, string> WeaponIcons = new Dictionary<string, string>
    {
        { "单手剑",   "wpn_sword" },
        { "双手剑",   "wpn_claymore" },
        { "弓",       "wpn_bow" },
        { "法器",     "wpn_catalyst" },
        { "长柄武器", "wpn_polearm" },
    };

    // 查到的图缓存在这儿（"确实没有"也缓存），否则十连时每帧几十次 Resources.Load
    static readonly Dictionary<string, Texture2D> IconCache = new Dictionary<string, Texture2D>();

    /// <summary>这件奖品用哪张图</summary>
    static string IconKeyOf(Wish w)
    {
        if (w.IsCharacter)
            return CharacterIcons.TryGetValue(w.Name, out string character) ? character : null;

        return WeaponIcons.TryGetValue(w.Note, out string weapon) ? weapon : null;
    }

    /// <summary>取图标；没有就返回 null，交给调用处退回纯文字版</summary>
    Texture2D IconOf(Wish w)
    {
        string key = IconKeyOf(w);
        if (string.IsNullOrEmpty(key)) return null;

        if (w.Rarity <= 3)
        {
            Texture2D basic = LoadIcon(key + "_basic");
            if (basic != null) return basic;
        }

        return LoadIcon(key);
    }

    static Texture2D LoadIcon(string key)
    {
        if (IconCache.TryGetValue(key, out Texture2D cached)) return cached;

        var texture = Resources.Load<Texture2D>(IconFolder + key);
        IconCache[key] = texture;     // 没有也缓存，免得每帧重复查
        return texture;
    }

    // ---------- 运行时状态 ----------

    readonly int[]  _pity5      = new int[BannerNames.Length];       // 距离上次五星已经抽了多少次
    readonly int[]  _pity4      = new int[BannerNames.Length];
    readonly bool[] _guarantee5 = new bool[BannerNames.Length];      // 上次五星歪了 → 这次必 UP
    int _totalPulls;

    readonly List<Wish>   _results  = new List<Wish>();              // 本次出货（翻牌展示）
    readonly List<string> _history  = new List<string>();            // 最近出货，最新的在最前面
    float _resultAt;                                                 // 翻牌开始的时间
    bool  _showPool;                                                 // 「详情」展开奖池清单

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

    GUIStyle _title, _heading, _sub, _body, _small, _button;

    public void Init(VoxelInteraction inventory)
    {
        _inventory = inventory;
        _gemIcon  = Resources.Load<Texture2D>("PrimogemIcon");
        _fateIcon = Resources.Load<Texture2D>("IntertwinedFateIcon");   // 生成器出的纠缠之缘图标
        BuildStars();
        LoadProgress();
    }

    // ---------- 保底 / 历史存档 ----------
    // 每个池子各记各的保底（原神也是分开算的），总抽数全局共用

    void LoadProgress()
    {
        _totalPulls = PlayerPrefs.GetInt(SavePrefix + "Total", 0);
        _history.Clear();

        for (int i = 0; i < Banners.Length; i++)
        {
            _pity5[i]      = PlayerPrefs.GetInt(SavePrefix + "P5." + i, 0);
            _pity4[i]      = PlayerPrefs.GetInt(SavePrefix + "P4." + i, 0);
            _guarantee5[i] = PlayerPrefs.GetInt(SavePrefix + "G5." + i, 0) != 0;
        }

        int count = PlayerPrefs.GetInt(SavePrefix + "HistCount", 0);
        for (int i = 0; i < count && i < HistoryMax; i++)
        {
            string line = PlayerPrefs.GetString(SavePrefix + "Hist." + i, "");
            if (!string.IsNullOrEmpty(line)) _history.Add(line);
        }
    }

    void SaveProgress()
    {
        PlayerPrefs.SetInt(SavePrefix + "Total", _totalPulls);

        for (int i = 0; i < Banners.Length; i++)
        {
            PlayerPrefs.SetInt(SavePrefix + "P5." + i, _pity5[i]);
            PlayerPrefs.SetInt(SavePrefix + "P4." + i, _pity4[i]);
            PlayerPrefs.SetInt(SavePrefix + "G5." + i, _guarantee5[i] ? 1 : 0);
        }

        PlayerPrefs.SetInt(SavePrefix + "HistCount", _history.Count);
        for (int i = 0; i < _history.Count; i++)
            PlayerPrefs.SetString(SavePrefix + "Hist." + i, _history[i]);

        PlayerPrefs.Save();
    }

    // ---------- 开关 ----------

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            // 翻牌翻到一半按 P：先把牌收掉，不要整页关掉
            if (IsOpen && _results.Count > 0) { DismissResults(); return; }

            if (IsOpen) Close();
            else if (!VoxelChat.IsOpen && !VoxelMenu.IsOpen) Open();      // 对话框 / 菜单开着的时候不抢
            return;
        }

        if (!IsOpen) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_results.Count > 0) { DismissResults(); return; }     // 先收牌
            if (_showPool)          { _showPool = false; return; }    // 再收奖池清单
            Close();
            return;
        }

        // 空格也能确认（祈愿页开着时鼠标是解锁的，玩家不会跳起来）
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (_results.Count > 0) DismissResults();
            else if (_showPool)     _showPool = false;
        }
    }

    void DismissResults()
    {
        _results.Clear();
        _resultAt = 0f;
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
        _showPool = false;                            // 下次打开回到主卡池页
        _results.Clear();
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

        // 翻牌 / 奖池清单是模式层：这两层开着的时候，底下的按钮一律不响应，
        // 免得"点一下收牌"顺手把「祈愿 ×10」也点掉了
        if (_results.Count > 0) { DrawResults();    return; }
        if (_showPool)          { DrawPoolDetail(); return; }

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

        // 保底进度：每个池子各记各的，歪了下次必 UP 会直接标出来
        Banner b = Banners[_banner];
        string guar = b.Up5Chance >= 1f ? ""
                    : _guarantee5[_banner] ? " · 下次五星必为 UP"
                                           : " · 大保底未触发";

        LabelD(new Rect(frame.x, 510f, frame.width, 22f),
               "五星保底 " + _pity5[_banner] + " / " + HardPity5 +
               "（还差 " + (HardPity5 - _pity5[_banner]) + " 抽） · 四星 " +
               _pity4[_banner] + " / " + HardPity4 + guar, _sub);

        Rect bar = new Rect(frame.x + 2f, 538f, frame.width - 4f, 9f);
        FrameD(bar, new Color(0.05f, 0.06f, 0.12f, 1f), GoldDeep, Px(1f));
        VoxelHud.FillRect(S(new Rect(bar.x + 1f, bar.y + 1f,
                                     (bar.width - 2f) * Mathf.Clamp01(_pity5[_banner] / (float)HardPity5),
                                     bar.height - 2f)),
                          _pity5[_banner] >= SoftStart ? Star5 : Gold);
    }

    /// <summary>中间那个大展位：本池的五星。单 / 双 UP 左右并排，常驻池那种一堆五星的排成网格</summary>
    void DrawFeatured()
    {
        Rect card = new Rect(472f, 166f, 506f, 226f);
        FrameD(card, new Color(0.10f, 0.12f, 0.22f, 0.92f), GoldDeep, Px(1f));
        CornersD(card, Gold, 14f, 2f);

        Wish[] up = Banners[_banner].Up5;

        // 常驻池：七件五星挤不下，排成两行小格（格子太小，只画小图 + 名字）
        if (up.Length > 2)
        {
            LabelD(new Rect(card.x, card.y + 10f, card.width, 26f),
                   "常驻五星 · 共 " + up.Length + " 件", _sub, Gold);

            const int cols = 4;
            float cellW = (card.width - 32f) / cols;

            for (int i = 0; i < up.Length; i++)
            {
                Wish wish = up[i];
                Rect cell = new Rect(card.x + 16f + (i % cols) * cellW,
                                     card.y + 42f + (i / cols) * 72f, cellW, 68f);

                Texture2D icon = IconOf(wish);
                if (icon != null)
                {
                    const float side = 38f;
                    GUI.DrawTexture(S(new Rect(cell.center.x - side * 0.5f, cell.y, side, side)),
                                    icon, ScaleMode.ScaleToFit);
                    LabelD(new Rect(cell.x, cell.y + 40f, cell.width, 24f), wish.Name, _small, Text);
                }
                else
                {
                    LabelD(new Rect(cell.x, cell.y, cell.width, 22f), Stars(wish.Rarity), _sub, Star5);
                    LabelD(new Rect(cell.x, cell.y + 22f, cell.width, 26f), wish.Name, _sub, Text);
                    LabelD(new Rect(cell.x, cell.y + 46f, cell.width, 20f), wish.Note, _small, TextDim);
                }
            }
            return;
        }

        float half = card.width * (up.Length == 1 ? 1f : 0.5f);

        for (int i = 0; i < up.Length; i++)
            DrawFeaturedSlot(new Rect(card.x + i * half, card.y, half, card.height), up[i], i == 1);
    }

    /// <summary>展位里的一格：有图标就"上图下文"，没图标就整格居中放文字</summary>
    void DrawFeaturedSlot(Rect slot, Wish w, bool divider)
    {
        if (divider)   // 双 UP 中间的分隔线
            VoxelHud.FillRect(S(new Rect(slot.x, slot.y + 26f, Px(1f), slot.height - 52f)),
                              new Color(GoldDeep.r, GoldDeep.g, GoldDeep.b, 0.6f));

        Texture2D icon = IconOf(w);
        float top = slot.y + 10f;

        if (icon != null)
        {
            float side = Mathf.Min(slot.width * 0.34f, slot.height * 0.42f);
            GUI.DrawTexture(S(new Rect(slot.center.x - side * 0.5f, top, side, side)),
                            icon, ScaleMode.ScaleToFit);
            top += side + 4f;
        }

        LabelD(new Rect(slot.x, top, slot.width, 26f), Stars(w.Rarity), _sub, Star5);
        LabelD(new Rect(slot.x, top + 28f, slot.width, 30f), w.Name, _button, Text);
        LabelD(new Rect(slot.x, top + 60f, slot.width, 22f),
               (w.IsCharacter ? "角色 · " : "武器 · ") + w.Note, _sub, TextDim);
        LabelD(new Rect(slot.x, top + 84f, slot.width, 22f), "UP！", _sub, Gold);
    }

    /// <summary>星级对应的那串星星</summary>
    static string Stars(int rarity)
    {
        if (rarity >= 5) return "★★★★★";
        if (rarity == 4) return "★★★★";
        return "★★★";
    }

    /// <summary>星级对应的颜色（五星金 / 四星紫 / 三星灰）</summary>
    Color RarityColor(int rarity)
    {
        if (rarity >= 5) return Star5;
        if (rarity == 4) return Star4;
        return TextDim;
    }

    /// <summary>底下一排小卡：本池 UP 的四星，最多显示 3 张（常驻池四星多，其余走「详情」）</summary>
    void DrawFourStars()
    {
        Wish[] up = Banners[_banner].Up4;
        int count = Mathf.Min(3, up.Length);
        if (count <= 0) return;

        const float w = 130f, h = 58f, gap = 16f;
        float total = w * count + gap * (count - 1);
        float x = 640f - total * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Wish wish = up[i];
            Rect card = new Rect(x + i * (w + gap), 404f, w, h);

            FrameD(card, new Color(0.09f, 0.10f, 0.19f, 0.92f), GoldDeep, Px(1f));

            Texture2D icon = IconOf(wish);
            if (icon == null)
            {
                LabelD(new Rect(card.x, card.y + 5f, card.width, 20f), Stars(wish.Rarity), _sub, Star4);
                LabelD(new Rect(card.x, card.y + 25f, card.width, 26f), wish.Name, _sub, Text);
                continue;
            }

            // 卡片是横条，图标放左边、文字放右边
            float side = card.height - 10f;
            GUI.DrawTexture(S(new Rect(card.x + 5f, card.y + 5f, side, side)), icon, ScaleMode.ScaleToFit);

            float textX = card.x + 5f + side + 4f;
            float textW = card.xMax - textX - 5f;
            LabelD(new Rect(textX, card.y + 11f, textW, 20f), Stars(wish.Rarity), _small, Star4);
            LabelD(new Rect(textX, card.y + 29f, textW, 22f), wish.Name, _small, Text);
        }

        if (up.Length > count)
            LabelD(new Rect(232f, 462f, 990f, 20f),
                   "另有 " + (up.Length - count) + " 件四星 · 点「详情」看全表", _sub);
    }

    void DrawWishButtons()
    {
        if (DrawButton(_detail, _showPool ? "收起" : "详情", "", Panel, _sub))
            _showPool = !_showPool;

        if (DrawButton(_single, "祈愿 ×1", "◆ " + SingleFate + " 个纠缠之缘", Panel, _button) &&
            TrySpendFate(SingleFate))
            DoWish(1);

        if (DrawButton(_ten, "祈愿 ×10", "◆ " + TenFate + " 个纠缠之缘", PanelLit, _button) &&
            TrySpendFate(TenFate))
            DoWish(10);
    }

    // ---------- 抽卡本身 ----------

    /// <summary>抽 count 次：逐次走概率，结果收进背包并记进历史，然后进翻牌</summary>
    void DoWish(int count)
    {
        _results.Clear();

        for (int i = 0; i < count; i++)
        {
            Wish w = RollOne(Banners[_banner]);
            _results.Add(w);
            Grant(w);
        }

        _resultAt = Time.time;
        SaveProgress();
    }

    /// <summary>抽一次：先掷星级（软保底 / 硬保底），再掷具体是哪一件</summary>
    Wish RollOne(Banner b)
    {
        int i = _banner;
        _totalPulls++;

        // ---- 五星：第 90 抽必出；第 74 抽起每多一抽概率 +6% ----
        float p5 = Base5;
        if (_pity5[i] + 1 >= SoftStart)
            p5 = Mathf.Min(1f, Base5 + (_pity5[i] + 1 - SoftStart) * SoftStep);

        if (_pity5[i] + 1 >= HardPity5 || Random.value < p5)
        {
            bool up = b.Up5Chance >= 1f || _guarantee5[i] || Random.value < b.Up5Chance;
            _guarantee5[i] = !up && b.Up5Chance < 1f;      // 歪了记一笔，下次必 UP
            _pity5[i] = 0;
            _pity4[i]++;                                   // 出五星不清四星计数（和原神一致）
            return up || b.Standard5 == null ? Pick(b.Up5) : Pick(b.Standard5);
        }

        // ---- 四星：第 10 抽必出，所以十连至少出一个四星 ----
        if (_pity4[i] + 1 >= HardPity4 || Random.value < Base4)
        {
            _pity4[i] = 0;
            _pity5[i]++;
            return b.Standard4 == null || Random.value < Up4Chance ? Pick(b.Up4) : Pick(b.Standard4);
        }

        _pity5[i]++;
        _pity4[i]++;
        return Pick(b.Standard3);
    }

    static Wish Pick(Wish[] pool) => pool[Random.Range(0, pool.Length)];

    /// <summary>把抽到的东西收进背包，并往抽卡历史里插一条（最新的在最前）</summary>
    void Grant(Wish w)
    {
        if (_inventory != null) _inventory.AddMaterial(w.Name, 1);

        _history.Insert(0, Stars(w.Rarity) + " " + w.Name);
        while (_history.Count > HistoryMax)
            _history.RemoveAt(_history.Count - 1);
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
        LabelD(new Rect(38f, 640f, 760f, 22f),
               "按 P / Esc 关闭 · 点「详情」看奖池全表 · 已祈愿 " + _totalPulls + " 次", _small);

        DrawHistory();

        if (Time.time >= _toastUntil) return;

        Rect box = S(new Rect(430f, 592f, 420f, 34f));
        VoxelHud.FillRect(box, new Color(0f, 0f, 0f, 0.62f));
        VoxelHud.LabelShadow(box, _toast, _sub);
    }

    /// <summary>左侧最近出货列表（最新的在最上面），五星 / 四星按星级上色</summary>
    void DrawHistory()
    {
        if (_history.Count == 0) return;

        Rect panel = new Rect(38f, 384f, 168f, 244f);
        FrameD(panel, new Color(0.07f, 0.09f, 0.17f, 0.80f), GoldDeep, Px(1f));

        LabelD(new Rect(panel.x, panel.y + 8f, panel.width, 22f), "最近祈愿", _small, Gold);

        for (int i = 0; i < _history.Count && i < HistoryMax; i++)
        {
            string line = _history[i];
            Color tint = line.StartsWith("★★★★★") ? Star5
                       : line.StartsWith("★★★★")  ? Star4
                                                   : TextDim;

            LabelD(new Rect(panel.x + 10f, panel.y + 32f + i * 25f, panel.width - 20f, 23f),
                   line, _small, tint);
        }
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

    // ---------- 翻牌层 ----------

    /// <summary>本次出货的翻牌：逐张弹出，五星先闪一层金光。点一下 / 空格 / Esc 收起</summary>
    void DrawResults()
    {
        float elapsed = Time.time - _resultAt;
        int n = _results.Count;

        bool has5 = false;
        for (int i = 0; i < n; i++)
            if (_results[i].Rarity >= 5) { has5 = true; break; }

        // 五星：整屏先闪一下金光，然后很快褪掉
        if (has5 && elapsed < 0.9f)
        {
            float a = Mathf.Clamp01(1f - elapsed / 0.9f) * 0.42f;
            VoxelHud.FillRect(new Rect(0f, 0f, Screen.width, Screen.height),
                              new Color(Star5.r, Star5.g, Star5.b, a));
        }

        VoxelHud.FillRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.74f));

        LabelD(new Rect(0f, 68f, RefWidth, 40f), n > 1 ? "祈愿 ×" + n : "祈愿", _heading);

        if (n == 1)
        {
            DrawResultCard(new Rect(490f, 168f, 300f, 350f), _results[0], 0f, elapsed);
        }
        else
        {
            // 十连：5 列 × 2 行（卡片比单抽的矮，字号也小一号）
            const float cw = 208f, ch = 212f, gapX = 14f, gapY = 14f;
            float totalW = cw * 5f + gapX * 4f;
            float x0 = 640f - totalW * 0.5f;

            for (int i = 0; i < n && i < 10; i++)
                DrawResultCard(new Rect(x0 + (i % 5) * (cw + gapX),
                                        150f + (i / 5) * (ch + gapY), cw, ch),
                               _results[i], i * 0.09f, elapsed);
        }

        LabelD(new Rect(0f, 650f, RefWidth, 24f), "点击任意处 / 空格 / Esc 收起", _sub);

        // 等最后一张翻完再允许收起，免得手快连点把牌跳过去
        Event e = Event.current;
        if (e.type == EventType.MouseUp && e.button == 0 && elapsed >= n * 0.09f + 0.3f)
        {
            DismissResults();
            e.Use();
        }
    }

    /// <summary>一张牌：delay 秒之后才翻出来（从 0.86 倍放大到 1 倍）</summary>
    void DrawResultCard(Rect d, Wish w, float delay, float elapsed)
    {
        float t = Mathf.Clamp01((elapsed - delay) / 0.22f);
        if (t <= 0f) return;

        Color tint = RarityColor(w.Rarity);
        float scale = Mathf.Lerp(0.86f, 1f, t);

        Rect box = new Rect(d.center.x - d.width * scale * 0.5f,
                            d.center.y - d.height * scale * 0.5f,
                            d.width * scale, d.height * scale);

        // 五星先铺一层光晕，压在卡片后面
        if (w.Rarity >= 5)
            Glow(box.center.x, box.center.y, 170f, new Color(tint.r, tint.g, tint.b, 0.22f * t), 10);

        FrameD(box, new Color(0.08f, 0.10f, 0.19f, 0.95f), tint, Px(w.Rarity >= 5 ? 3f : 2f));
        CornersD(box, tint, 16f, 3f);

        Texture2D icon = IconOf(w);

        // 没图就退回原来那套纯文字竖排
        if (icon == null)
        {
            LabelD(new Rect(box.x, box.y + box.height * 0.12f, box.width, 30f), Stars(w.Rarity), _button, tint);
            LabelD(new Rect(box.x, box.y + box.height * 0.32f, box.width, 40f), w.Name, _button, Text);
            LabelD(new Rect(box.x, box.y + box.height * 0.54f, box.width, 24f),
                   (w.IsCharacter ? "角色" : "武器") + " · " + w.Note, _sub, TextDim);
            LabelD(new Rect(box.x, box.y + box.height * 0.68f, box.width, 24f), "已收进背包", _sub, TextDim);
            return;
        }

        // 十连的卡小，文字精简成"星级 + 名字"两行
        bool compact = box.height < 260f;

        // 小卡给图标留更大比例，否则十连里的图小得看不清
        float side = compact ? Mathf.Min(box.width * 0.72f, box.height * 0.52f)
                             : Mathf.Min(box.width * 0.62f, box.height * 0.40f);
        Rect slot = new Rect(box.center.x - side * 0.5f, box.y + box.height * 0.06f, side, side);
        GUI.DrawTexture(S(slot), icon, ScaleMode.ScaleToFit);

        float textTop = slot.yMax - box.y + box.height * 0.02f;   // 相对卡顶的偏移

        LabelD(new Rect(box.x, box.y + textTop, box.width, box.height * 0.13f), Stars(w.Rarity), _button, tint);

        if (compact)
        {
            LabelD(new Rect(box.x, box.y + textTop + box.height * 0.14f, box.width, box.height * 0.18f),
                   w.Name, _sub, Text);
            return;
        }

        LabelD(new Rect(box.x, box.y + textTop + box.height * 0.14f, box.width, box.height * 0.15f),
               w.Name, _button, Text);
        LabelD(new Rect(box.x, box.y + textTop + box.height * 0.29f, box.width, box.height * 0.10f),
               (w.IsCharacter ? "角色" : "武器") + " · " + w.Note, _sub, TextDim);
        LabelD(new Rect(box.x, box.y + textTop + box.height * 0.40f, box.width, box.height * 0.10f),
               "已收进背包", _sub, TextDim);
    }

    // ---------- 奖池详情层 ----------

    /// <summary>「详情」展开的奖池全表：五星 / 四星 / 三星分组列出来</summary>
    void DrawPoolDetail()
    {
        VoxelHud.FillRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.80f));

        Rect frame = new Rect(190f, 56f, 900f, 596f);
        FrameD(frame, Panel, GoldDeep, Px(2f));
        CornersD(frame, Gold, 20f, 3f);

        Banner b = Banners[_banner];

        LabelD(new Rect(frame.x, frame.y + 16f, frame.width, 34f),
               BannerNames[_banner] + " · " + BannerSubs[_banner], _title, Gold);
        LabelD(new Rect(frame.x, frame.y + 52f, frame.width, 22f), BannerTips[_banner], _sub);

        float y = frame.y + 88f;
        y = DrawPoolGroup(frame, y, "五星", b.Up5,       Star5);
        y = DrawPoolGroup(frame, y, "四星", b.Up4,       Star4);
        y = DrawPoolGroup(frame, y, "三星", b.Standard3, TextDim);

        LabelD(new Rect(frame.x + 26f, frame.yMax - 58f, frame.width - 52f, 22f),
               "五星基础概率 0.600% · " + HardPity5 + " 抽必出 · 四星 " + HardPity4 + " 抽必出", _small);
        LabelD(new Rect(frame.x + 26f, frame.yMax - 36f, frame.width - 52f, 22f),
               b.Up5Chance >= 1f
                   ? "本池五星不分 UP"
                   : "出五星时 " + Mathf.RoundToInt(b.Up5Chance * 100f) + "% 为本池 UP（歪了下次必 UP）", _small);

        LabelD(new Rect(frame.x, frame.yMax + 10f, frame.width, 22f), "点击任意处 / 空格 / Esc 收起", _sub);

        Event e = Event.current;
        if (e.type == EventType.MouseUp && e.button == 0)
        {
            _showPool = false;
            e.Use();
        }
    }

    /// <summary>画一组奖池（标题 + 名字，按宽度折行），返回下一组的起始 y</summary>
    float DrawPoolGroup(Rect frame, float y, string title, Wish[] pool, Color tint)
    {
        LabelD(new Rect(frame.x + 26f, y, 120f, 24f), title, _body, tint);
        if (title != "三星")
            LabelD(new Rect(frame.x + 150f, y, 260f, 24f), "UP", _small, Gold);

        const float cellW = 200f, cellH = 24f;
        float x = frame.x + 26f;
        int perRow = Mathf.Max(1, Mathf.FloorToInt((frame.width - 52f) / cellW));
        float rowY = y + 26f;

        for (int i = 0; i < pool.Length; i++)
        {
            if (i > 0 && i % perRow == 0) { rowY += cellH; x = frame.x + 26f; }

            LabelD(new Rect(x, rowY, cellW - 8f, cellH),
                   pool[i].Name + "（" + pool[i].Note + "）", _small, Text);
            x += cellW;
        }

        return rowY + cellH + 12f;
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

        _heading = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(26f * _scale), alignment = TextAnchor.MiddleCenter,
        };
        _heading.normal.textColor = Gold;

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
