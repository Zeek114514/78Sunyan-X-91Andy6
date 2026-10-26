using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 角色界面（C 键开关，照原神那套排版做）：
///   左边一列角色卡（主角 + 祈愿抽到的，带等级与"出战"标记），
///   中间是选中角色的立绘位（元素色 + 图标）、称号、元素、武器类型、星级和「设为出战」，
///   右边五个页签：
///     · 属性：等级 / 突破阶级、升级与突破按钮、六项数值和构成明细
///     · 天赋：普通攻击 / 元素战技 / 元素爆发，等级上限跟着突破阶级走
///     · 技能：战技与爆发的名称、CD、能量、倍率与说明
///     · 圣遗物：套装效果（2 件 / 4 件），五个槽位的装备 / 卸下 / 强化
///     · 武器：当前武器（等级 / 精炼 / 基础攻击力 / 被动）、强化、能装哪些武器
///
/// 养成改动立刻写存档（见 VoxelCharacterSheet），出战角色的数值立刻影响战斗与生命上限。
/// 打开时放开鼠标（和菜单 / 背包 / 祈愿同一套互斥），C 或 Esc 关掉。
/// </summary>
public class VoxelCharacterScreen : MonoBehaviour
{
    /// <summary>界面是否打开（HUD / 交互 / 对话 / 祈愿 / 菜单 / 背包 / 大地图靠它避让）</summary>
    public static bool IsOpen { get; private set; }

    enum Page { Stats = 0, Talents, Skills, Artifacts, Weapon }
    static readonly string[] Tabs = { "属性", "天赋", "技能", "圣遗物", "武器" };

    // ---------- 配色（和菜单 / 背包 / 祈愿同一套：深底 + 香槟金） ----------

    static readonly Color Gold    = new Color(0.93f, 0.76f, 0.35f);
    static readonly Color SideBg  = new Color(0.07f, 0.08f, 0.12f, 0.96f);
    static readonly Color CardBg  = new Color(0.05f, 0.06f, 0.10f, 0.92f);
    static readonly Color PanelBg = new Color(0.09f, 0.10f, 0.15f, 0.92f);
    static readonly Color Text    = new Color(1f, 1f, 1f, 0.95f);
    static readonly Color TextDim = new Color(1f, 1f, 1f, 0.55f);
    static readonly Color BtnBg   = new Color(1f, 1f, 1f, 0.08f);

    VoxelCharacterSheet _sheet;
    VoxelCharacterSheet.CharacterState _sel;

    Page _page = Page.Stats;

    readonly List<VoxelCharacterData.WeaponInfo> _weapons = new List<VoxelCharacterData.WeaponInfo>();

    float _listScroll, _contentScroll;
    float _measuredHeight;                 // 上一帧右侧内容的总高（算滚动上限用）

    Rect _panel, _side, _mid, _right, _content, _close;
    readonly Rect[] _tabs = new Rect[Tabs.Length];

    GUIStyle _title, _name, _big, _body, _small, _gold, _btn, _tag, _footer;

    static readonly Dictionary<string, Texture2D> IconCache = new Dictionary<string, Texture2D>();

    public void Init(VoxelCharacterSheet sheet)
    {
        _sheet = sheet;
    }

    // ---------- 开关 ----------

    void Update()
    {
        if (!IsOpen)
        {
            // 只在"游戏内"才开：别的面板开着时鼠标是放开的，它们各自用 Esc 关
            bool free = VoxelPanels.CanUseInput;

            if (free && Input.GetKeyDown(KeyCode.C)) Open();
            return;
        }

        if (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

        if (Input.GetKeyDown(KeyCode.LeftArrow))  SetPage((Page)Mathf.Max(0, (int)_page - 1));
        if (Input.GetKeyDown(KeyCode.RightArrow)) SetPage((Page)Mathf.Min(Tabs.Length - 1, (int)_page + 1));

        HandleWheel();
    }

    void Open()
    {
        IsOpen = true;

        if (_sheet != null)
        {
            _sheet.Rebuild();                       // 可能刚祈愿抽到新角色
            if (_sel == null || !InRoster(_sel))
                _sel = _sheet.Roster.Count > 0 ? _sheet.Roster[0] : null;
        }

        RefreshWeapons();
        _listScroll = 0f;
        _contentScroll = 0f;

        Cursor.lockState = CursorLockMode.None;     // 放开鼠标才能点按钮
        Cursor.visible = true;
    }

    void Close()
    {
        IsOpen = false;
        Cursor.lockState = CursorLockMode.Locked;   // 关掉直接回游戏
        Cursor.visible = false;
    }

    void OnDestroy() => IsOpen = false;             // 停播时清掉静态状态

    void SetPage(Page page)
    {
        _page = page;
        _contentScroll = 0f;
    }

    bool InRoster(VoxelCharacterSheet.CharacterState state)
    {
        if (_sheet == null || state == null) return false;

        for (int i = 0; i < _sheet.Roster.Count; i++)
            if (_sheet.Roster[i] == state) return true;

        return false;
    }

    void RefreshWeapons()
    {
        _weapons.Clear();
        if (_sheet != null) _weapons.AddRange(_sheet.EquippableWeapons(_sel));
    }

    /// <summary>滚轮：鼠标在左边翻角色列表，在右边翻内容</summary>
    void HandleWheel()
    {
        float wheel = Input.GetAxisRaw("Mouse ScrollWheel");
        if (Mathf.Abs(wheel) < 0.01f) return;

        var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        float step = Mathf.Sign(wheel) * 60f;

        if (_side.Contains(mouse))       _listScroll = Mathf.Clamp(_listScroll - step, 0f, MaxListScroll);
        else if (_right.Contains(mouse)) _contentScroll = Mathf.Clamp(_contentScroll - step, 0f, MaxContentScroll);
    }

    float MaxListScroll
    {
        get
        {
            int count = _sheet != null ? _sheet.Roster.Count : 0;
            return Mathf.Max(0f, count * RowStep + 46f - _side.height);
        }
    }

    float MaxContentScroll => Mathf.Max(0f, _measuredHeight - _content.height);

    // ---------- 画界面 ----------

    void OnGUI()
    {
        VoxelTypography.Apply();
        if (!IsOpen) return;

        Layout();
        EnsureStyles();

        VoxelHud.FillRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.03f, 0.04f, 0.07f, 0.80f));

        DrawHeader();
        DrawSide();
        DrawCard();
        DrawTabs();
        DrawContent();

        _footer.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(_panel.x + 240f, _panel.yMax - 34f, _panel.width - 320f, 22f),
                             "C 关闭 · Esc 也行 · ←→ 换页签 · 滚轮翻列表 · 左键点按钮 · 养成会立刻存档", _footer);

        bool hot = _close.Contains(Event.current.mousePosition);
        VoxelHud.FillRect(_close, hot ? new Color(1f, 1f, 1f, 0.18f) : new Color(1f, 1f, 1f, 0.07f));
        VoxelHud.LabelShadow(_close, "✕", _btn);

        if (hot && Event.current.type == EventType.MouseUp && Event.current.button == 0)
        {
            Event.current.Use();
            Close();
        }
    }

    void Layout()
    {
        float w = Mathf.Min(1200f, Screen.width * 0.92f);
        float h = Mathf.Min(680f, Screen.height * 0.9f);
        _panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

        const float sideW = 236f, midW = 286f, pad = 16f;

        _close = new Rect(_panel.xMax - 52f, _panel.y + 14f, 34f, 34f);

        _side  = new Rect(_panel.x, _panel.y + 62f, sideW, _panel.height - 62f - 42f);
        _mid   = new Rect(_side.xMax + pad, _panel.y + 76f, midW, _panel.height - 76f - 42f);
        _right = new Rect(_mid.xMax + pad, _panel.y + 76f,
                          _panel.xMax - pad - (_mid.xMax + pad), _panel.height - 76f - 42f);
        _content = new Rect(_right.x, _right.y + 46f, _right.width, _right.height - 46f);

        const float tabW = 92f, tabGap = 8f;
        for (int i = 0; i < Tabs.Length; i++)
            _tabs[i] = new Rect(_right.x + i * (tabW + tabGap), _right.y, tabW, 34f);
    }

    void DrawHeader()
    {
        VoxelHud.FillRect(new Rect(_side.xMax, _panel.y, _panel.width - _side.width, 62f), new Color(0f, 0f, 0f, 0.25f));

        _title.normal.textColor = new Color(1f, 0.9f, 0.6f);
        VoxelHud.LabelShadow(new Rect(_panel.x + 24f, _panel.y + 16f, 240f, 30f), "角 色", _title);

        // 右上角报一下出战角色，方便对着改
        var active = _sheet != null ? _sheet.Active : null;
        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(_panel.xMax - 640f, _panel.y + 22f, 570f, 22f),
                             active != null
                                 ? $"出战：{active.Name}　Lv.{active.Level}　生命上限 {active.MaxHealth:0}　攻击力 {active.Attack:0}"
                                 : "还没接上角色系统", _small);
    }

    // ---------- 左边：角色列表 ----------

    const float RowH = 54f, RowGap = 6f, RowStep = RowH + RowGap;

    void DrawSide()
    {
        VoxelHud.FillRect(_side, SideBg);
        VoxelHud.FillRect(new Rect(_side.xMax - 2f, _side.y, 2f, _side.height), new Color(Gold.r, Gold.g, Gold.b, 0.5f));

        _gold.normal.textColor = Gold;
        VoxelHud.LabelShadow(new Rect(_side.x + 18f, _side.y + 12f, _side.width - 36f, 26f),
                             _sheet != null ? $"角色　{_sheet.Roster.Count}" : "角色", _gold);

        if (_sheet == null || _sheet.Roster.Count == 0)
        {
            _small.normal.textColor = TextDim;
            VoxelHud.LabelShadow(new Rect(_side.x + 18f, _side.y + 52f, _side.width - 36f, 22f),
                                 "按 P 祈愿抽几个角色吧", _small);
            return;
        }

        float top = _side.y + 44f;

        for (int i = 0; i < _sheet.Roster.Count; i++)
        {
            var state = _sheet.Roster[i];
            var row = new Rect(_side.x + 8f, top + i * RowStep - _listScroll, _side.width - 16f, RowH);

            if (row.yMax < top || row.y > _side.yMax) continue;      // 卷出视野的跳过

            DrawRosterRow(row, state);
        }
    }

    void DrawRosterRow(Rect row, VoxelCharacterSheet.CharacterState state)
    {
        var info = state.Info;
        Color tint = info.ElementColor;

        bool selected = state == _sel;
        bool active   = _sheet.Active == state;
        bool hot      = row.Contains(Event.current.mousePosition) && row.y >= _side.y + 44f && row.yMax <= _side.yMax;

        VoxelHud.FillRect(row, selected ? new Color(tint.r, tint.g, tint.b, 0.20f)
                                        : new Color(1f, 1f, 1f, hot ? 0.10f : 0.05f));
        if (selected) VoxelHud.FillRect(new Rect(row.x, row.y, 3f, row.height), tint);

        // 小头像：有图标贴图，没有就一块元素色
        var box = new Rect(row.x + 8f, row.y + 9f, 36f, 36f);
        var icon = IconOf(info.Name);
        if (icon != null) GUI.DrawTexture(box, icon, ScaleMode.ScaleToFit);
        else VoxelHud.FillRect(box, new Color(tint.r, tint.g, tint.b, 0.85f));

        _body.normal.textColor = selected ? Color.white : new Color(1f, 1f, 1f, 0.85f);
        VoxelHud.LabelShadow(new Rect(box.xMax + 8f, row.y + 6f, row.width - 96f, 22f), info.Name, _body);

        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(box.xMax + 8f, row.y + 27f, row.width - 96f, 20f),
                             $"Lv.{state.Level}　{info.Element}", _small);

        if (active)
        {
            _tag.normal.textColor = Gold;
            VoxelHud.LabelShadow(new Rect(row.xMax - 46f, row.y + 16f, 42f, 20f), "出战", _tag);
        }

        if (!hot || Event.current.type != EventType.MouseUp || Event.current.button != 0) return;

        Event.current.Use();
        _sel = state;
        _contentScroll = 0f;
        RefreshWeapons();
    }

    // ---------- 中间：角色卡 ----------

    void DrawCard()
    {
        VoxelHud.FillRect(_mid, CardBg);
        VoxelHud.FillRect(new Rect(_mid.x, _mid.y, _mid.width, 2f), new Color(Gold.r, Gold.g, Gold.b, 0.35f));

        if (_sel == null)
        {
            _small.normal.textColor = TextDim;
            VoxelHud.LabelShadow(new Rect(_mid.x + 18f, _mid.y + 20f, _mid.width - 36f, 22f), "左边选一个角色", _small);
            return;
        }

        var info = _sel.Info;
        Color tint = info.ElementColor;

        // 立绘位
        var art = new Rect(_mid.x + 18f, _mid.y + 18f, _mid.width - 36f, 146f);
        VoxelHud.FillRect(art, new Color(tint.r * 0.32f, tint.g * 0.32f, tint.b * 0.42f, 0.95f));
        Frame(art, new Color(tint.r, tint.g, tint.b, 0.75f), 2f);

        var icon = IconOf(info.Name);
        if (info.Id == LocalRemielle.CharacterId && LocalRemielle.DrawPreview(art))
        {
            _small.normal.textColor = TextDim;
            VoxelHud.LabelShadow(new Rect(art.x + 5, art.yMax - 20, art.width - 10, 18), "拖动旋转 · 本地模型预览", _small);
        }
        else if (icon != null)
        {
            GUI.DrawTexture(new Rect(art.center.x - 62f, art.y + 13f, 124f, 120f), icon, ScaleMode.ScaleToFit);
        }
        else
        {
            _big.normal.textColor = tint;
            VoxelHud.LabelShadow(new Rect(art.x, art.y + 44f, art.width, 60f),
                                 info.Name.Length > 2 ? info.Name.Substring(0, 2) : info.Name, _big);
        }

        float x = _mid.x + 18f, w = _mid.width - 36f;
        float y = art.yMax + 12f;

        _name.normal.textColor = tint;
        VoxelHud.LabelShadow(new Rect(x, y, w, 28f), info.Name, _name);
        y += 30f;

        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f), info.Title, _small);
        y += 22f;

        _gold.normal.textColor = StarColor(info.Rarity);
        VoxelHud.LabelShadow(new Rect(x, y, w, 22f), Stars(info.Rarity), _gold);
        y += 26f;

        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f), $"元素　{info.Element}", _small);
        y += 22f;
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f), $"武器　{VoxelCharacterData.TypeName(info.Weapon)}", _small);
        y += 28f;

        // 三行速览，改完装备一眼就能看到变化
        _body.normal.textColor = Text;
        VoxelHud.LabelShadow(new Rect(x, y, w, 22f), $"生命上限　{_sel.MaxHealth:0}", _body);
        y += 24f;
        VoxelHud.LabelShadow(new Rect(x, y, w, 22f), $"攻击力　　{_sel.Attack:0}", _body);
        y += 24f;
        VoxelHud.LabelShadow(new Rect(x, y, w, 22f), $"普攻伤害　{_sel.NormalAttackDamage:0}", _body);
        y += 34f;

        bool isActive = _sheet != null && _sheet.Active == _sel;
        var button = new Rect(x, y, w, 34f);

        if (Button(button, isActive ? "正在出战" : "设为出战", _btn, !isActive, false))
        {
            _sheet.SetActive(_sel);
        }

        y += 42f;
        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f), "出战角色的生命 / 攻击 / 减伤立刻生效", _small);
    }

    // ---------- 右边：页签 + 内容 ----------

    void DrawTabs()
    {
        for (int i = 0; i < Tabs.Length; i++)
        {
            bool on  = (int)_page == i;
            bool hot = _tabs[i].Contains(Event.current.mousePosition);

            VoxelHud.FillRect(_tabs[i], on ? new Color(Gold.r, Gold.g, Gold.b, 0.18f) : new Color(1f, 1f, 1f, hot ? 0.10f : 0.05f));
            if (on) VoxelHud.FillRect(new Rect(_tabs[i].x, _tabs[i].yMax - 2f, _tabs[i].width, 2f), Gold);

            _btn.normal.textColor = on ? Gold : TextDim;
            VoxelHud.LabelShadow(_tabs[i], Tabs[i], _btn);

            if (!hot || Event.current.type != EventType.MouseUp || Event.current.button != 0) continue;

            Event.current.Use();
            SetPage((Page)i);
        }
    }

    void DrawContent()
    {
        _contentScroll = Mathf.Clamp(_contentScroll, 0f, MaxContentScroll);

        VoxelHud.FillRect(_content, PanelBg);

        float startY = _content.y - _contentScroll;
        float endY;

        switch (_page)
        {
            case Page.Stats:     endY = DrawStatsPage(startY);     break;
            case Page.Talents:   endY = DrawTalentsPage(startY);   break;
            case Page.Skills:    endY = DrawSkillsPage(startY);    break;
            case Page.Artifacts: endY = DrawArtifactsPage(startY); break;
            default:             endY = DrawWeaponPage(startY);    break;
        }

        _measuredHeight = endY - startY;
    }

    /// <summary>这一行在不在内容区里（滚动后判断，免得点了看不见的按钮）</summary>
    bool Visible(Rect r) => r.yMax > _content.y + 2f && r.y < _content.yMax - 2f;

    // ---------- 属性页 ----------

    float DrawStatsPage(float y0)
    {
        var s = _sel;
        float x = _content.x + 18f, w = _content.width - 36f;
        float y = y0 + 16f;

        if (s == null) { Hint(x, y, w, "左边先选一个角色"); return y + 30f; }

        _name.normal.textColor = Text;
        VoxelHud.LabelShadow(new Rect(x, y, w, 28f), $"Lv.{s.Level} / {s.LevelCap}　　突破 {s.Phase} 阶", _name);
        y += 36f;

        bool up   = s.CanLevelUp;
        bool asc  = s.CanAscend;

        if (Button(new Rect(x, y, 104f, 32f), "升级 +1", _btn, up)) s.LevelUp(1);
        if (Button(new Rect(x + 112f, y, 104f, 32f), "升级 +10", _btn, up)) s.LevelUp(10);
        if (Button(new Rect(x + 224f, y, 132f, 32f), asc ? "突破（+上限）" : "突破（先到上限）", _btn, asc)) s.Ascend();
        y += 44f;

        // 主数值
        y = StatRow(x, y, w, "生命上限", s.MaxHealth.ToString("0"), new Color(0.86f, 0.36f, 0.36f));
        y = StatRow(x, y, w, "攻击力", s.Attack.ToString("0"), new Color(0.95f, 0.72f, 0.34f));
        y = StatRow(x, y, w, "防御力", s.Defense.ToString("0"), new Color(0.55f, 0.72f, 0.92f));
        y = StatRow(x, y, w, "暴击率", (s.CritRate * 100f).ToString("0.#") + "%", Text);
        y = StatRow(x, y, w, "暴击伤害", (s.CritDamage * 100f).ToString("0.#") + "%", Text);
        y = StatRow(x, y, w, "伤害减免", (s.DamageReduction * 100f).ToString("0.#") + "%", Text);
        y = StatRow(x, y, w, "每下普攻", s.NormalAttackDamage.ToString("0"), new Color(0.92f, 0.55f, 0.45f));
        y += 6f;

        // 构成明细：知道数值是从哪儿来的
        VoxelHud.FillRect(new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.12f));
        y += 12f;

        _gold.normal.textColor = Gold;
        VoxelHud.LabelShadow(new Rect(x, y, w, 22f), "数值构成", _gold);
        y += 26f;

        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f),
                             $"基础生命 {s.BaseHp:0}　基础攻击 {s.BaseAtk:0}　基础防御 {s.BaseDef:0}", _small);
        y += 22f;
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f),
                             s.Weapon != null
                                 ? $"武器 {s.Weapon.Name}（Lv.{s.WeaponLevel} 精炼 {s.Refine}）+攻击 {s.WeaponAttack:0}"
                                 : "武器 空手（去「武器」页装一把）", _small);
        y += 22f;
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f),
                             $"圣遗物加成　生命+{s.BonusOf(VoxelCharacterData.Stat.HpFlat):0}　" +
                             $"攻击+{s.BonusOf(VoxelCharacterData.Stat.AtkFlat):0}　" +
                             $"攻击+{s.BonusOf(VoxelCharacterData.Stat.AtkPercent) * 100f:0.#}%　" +
                             $"生命+{s.BonusOf(VoxelCharacterData.Stat.HpPercent) * 100f:0.#}%", _small);
        y += 22f;
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f),
                             $"套装 {s.Info.SetName}　2 件套 {(s.EquippedCount >= 2 ? "生效" : "未生效")}　" +
                             $"4 件套 {(s.EquippedCount >= 4 ? "生效" : "未生效")}", _small);
        y += 26f;

        _small.normal.textColor = new Color(1f, 0.86f, 0.55f, 0.9f);
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f),
                             "升级不消耗材料；装上武器和圣遗物数值会立刻跳到战斗里", _small);

        return y + 24f;
    }

    // ---------- 天赋页 ----------

    float DrawTalentsPage(float y0)
    {
        var s = _sel;
        float x = _content.x + 18f, w = _content.width - 36f;
        float y = y0 + 16f;

        if (s == null) { Hint(x, y, w, "左边先选一个角色"); return y + 30f; }

        int cap = VoxelCharacterData.TalentCap(s.Phase);

        _gold.normal.textColor = Gold;
        VoxelHud.LabelShadow(new Rect(x, y, w, 22f), $"天赋等级上限 {cap} 级（突破到 {s.Phase} 阶）", _gold);
        y += 30f;

        string[] names =
        {
            "普通攻击 · " + s.Info.NormalName,
            "元素战技 · " + s.Info.SkillName,
            "元素爆发 · " + s.Info.BurstName,
        };

        string[] effects =
        {
            $"每下普攻伤害 {s.NormalAttackDamage:0}（倍率 {VoxelCharacterData.NormalAttackMultiplier(s.Talents[0]) * 100f:0}% × 攻击力 {s.Attack:0}）",
            $"战技倍率 {VoxelCharacterData.SkillMultiplier(s.Talents[1]) * 100f:0}%（参考伤害 {s.SkillDamage:0}）",
            $"爆发倍率 {VoxelCharacterData.BurstMultiplier(s.Talents[2]) * 100f:0}%（参考伤害 {s.BurstDamage:0}）",
        };

        for (int i = 0; i < 3; i++)
        {
            var row = new Rect(x, y, w, 76f);
            if (!Visible(row)) { y += 82f; continue; }

            VoxelHud.FillRect(row, new Color(1f, 1f, 1f, 0.05f));

            _body.normal.textColor = Text;
            VoxelHud.LabelShadow(new Rect(row.x + 12f, row.y + 8f, row.width - 140f, 22f), names[i], _body);

            _small.normal.textColor = TextDim;
            VoxelHud.LabelShadow(new Rect(row.x + 12f, row.y + 32f, row.width - 140f, 20f), effects[i], _small);

            _gold.normal.textColor = s.Talents[i] >= cap ? TextDim : Gold;
            VoxelHud.LabelShadow(new Rect(row.x + 12f, row.y + 52f, row.width - 140f, 20f),
                                 $"Lv.{s.Talents[i]} / {cap}" + (i == 0 ? "　（这一项真的算进伤害）" : ""), _small);

            bool canUp = s.Talents[i] < cap;
            if (Button(new Rect(row.xMax - 116f, row.y + 22f, 104f, 32f), canUp ? "升级" : "已满", _btn, canUp))
                s.UpgradeTalent(i);

            y += 82f;
        }

        y += 4f;
        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f), "天赋等级跟着突破阶级解锁：0 阶 2 级、1 阶 4 级…… 4 阶起 10 级封顶", _small);

        return y + 24f;
    }

    // ---------- 技能页 ----------

    float DrawSkillsPage(float y0)
    {
        var s = _sel;
        float x = _content.x + 18f, w = _content.width - 36f;
        float y = y0 + 16f;

        if (s == null) { Hint(x, y, w, "左边先选一个角色"); return y + 30f; }

        y = SkillCard(x, y, w, "元素战技", s.Info.SkillName, s.Info.SkillDesc,
                      $"冷却 {s.Info.SkillCd:0.#} 秒　鼠标右键释放", VoxelCharacterData.SkillMultiplier(s.Talents[1]), s.SkillDamage, s.Info.ElementColor);

        y += 8f;

        y = SkillCard(x, y, w, "元素爆发", s.Info.BurstName, s.Info.BurstDesc,
                      $"元素能量 {s.Info.SkillEnergy:0}　F 键释放", VoxelCharacterData.BurstMultiplier(s.Talents[2]), s.BurstDamage, s.Info.ElementColor);

        y += 12f;
        _small.normal.textColor = new Color(1f, 0.86f, 0.55f, 0.9f);
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f),
                             "战技与爆发都已接进战斗（范围伤害 + 暴击）；元素能量靠普通攻击命中积累（每下 +" +
                             VoxelCharacterSheet.CharacterState.EnergyPerHit.ToString("0") + "）", _small);

        return y + 24f;
    }

    float SkillCard(float x, float y, float w, string kind, string name, string desc, string meta,
                    float multiplier, float damage, Color tint)
    {
        var card = new Rect(x, y, w, 116f);
        if (!Visible(card)) return y + 124f;

        VoxelHud.FillRect(card, new Color(tint.r, tint.g, tint.b, 0.12f));
        VoxelHud.FillRect(new Rect(card.x, card.y, 3f, card.height), tint);

        _tag.normal.textColor = tint;
        VoxelHud.LabelShadow(new Rect(card.x + 12f, card.y + 8f, 120f, 20f), kind, _tag);

        _body.normal.textColor = Text;
        VoxelHud.LabelShadow(new Rect(card.x + 12f, card.y + 28f, card.width - 24f, 24f), name, _body);

        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(card.x + 12f, card.y + 54f, card.width - 24f, 20f), meta, _small);
        VoxelHud.LabelShadow(new Rect(card.x + 150f, card.y + 54f, card.width - 160f, 20f),
                             $"倍率 {multiplier * 100f:0}%　参考伤害 {damage:0}", _small);
        VoxelHud.LabelShadow(new Rect(card.x + 12f, card.y + 78f, card.width - 24f, 20f), desc, _small);

        return card.yMax;
    }

    // ---------- 圣遗物页 ----------

    float DrawArtifactsPage(float y0)
    {
        var s = _sel;
        float x = _content.x + 18f, w = _content.width - 36f;
        float y = y0 + 16f;

        if (s == null) { Hint(x, y, w, "左边先选一个角色"); return y + 30f; }

        int worn = s.EquippedCount;

        _gold.normal.textColor = Gold;
        VoxelHud.LabelShadow(new Rect(x, y, w, 22f), $"{s.Info.SetName}（已装 {worn} / 5 件）", _gold);
        y += 26f;

        _small.normal.textColor = worn >= 2 ? new Color(0.62f, 0.92f, 0.62f) : TextDim;
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f), $"2 件套：{s.Info.Set2Text}" + (worn >= 2 ? "（生效）" : "（未生效）"), _small);
        y += 22f;

        _small.normal.textColor = worn >= 4 ? new Color(0.62f, 0.92f, 0.62f) : TextDim;
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f), $"4 件套：{s.Info.Set4Text}" + (worn >= 4 ? "（生效）" : "（未生效）"), _small);
        y += 30f;

        for (int i = 0; i < VoxelCharacterData.Slots.Length; i++)
        {
            var slot = VoxelCharacterData.Slots[i];
            var row = new Rect(x, y, w, 70f);
            if (!Visible(row)) { y += 76f; continue; }

            bool on = s.Equipped[i];
            int level = s.ArtifactLevels[i];

            VoxelHud.FillRect(row, new Color(1f, 1f, 1f, on ? 0.08f : 0.03f));
            if (on) VoxelHud.FillRect(new Rect(row.x, row.y, 3f, row.height), Gold);

            _body.normal.textColor = on ? Text : TextDim;
            VoxelHud.LabelShadow(new Rect(row.x + 12f, row.y + 6f, row.width - 300f, 22f),
                                 $"{slot.SlotName}　{StatLineOf(slot.MainStat, slot.MainBase + slot.MainPerLevel * level)}" +
                                 (level > 0 ? $"　+{level}" : ""), _body);

            _small.normal.textColor = TextDim;
            VoxelHud.LabelShadow(new Rect(row.x + 12f, row.y + 30f, row.width - 300f, 20f),
                                 $"{s.Info.SetName}　" +
                                 $"{VoxelCharacterData.StatLine(slot.Sub1, slot.Sub1Base + slot.Sub1PerLevel * level)}　" +
                                 $"{VoxelCharacterData.StatLine(slot.Sub2, slot.Sub2Base + slot.Sub2PerLevel * level)}", _small);

            VoxelHud.LabelShadow(new Rect(row.x + 12f, row.y + 50f, row.width - 300f, 20f),
                                 $"强化 Lv.{level} / {VoxelCharacterData.MaxArtifactLevel}" + (on ? "" : "　（未装备）"), _small);

            // 按钮：装备 / 卸下、强化 +1、强化满
            if (Button(new Rect(row.xMax - 284f, row.y + 19f, 88f, 32f), on ? "卸下" : "装备", _btn, true))
                s.SetArtifactEquipped(i, !on);

            bool canUp = level < VoxelCharacterData.MaxArtifactLevel;
            if (Button(new Rect(row.xMax - 188f, row.y + 19f, 72f, 32f), "+1", _btn, canUp))
                s.UpgradeArtifact(i, 1);

            if (Button(new Rect(row.xMax - 108f, row.y + 19f, 96f, 32f), "强化满", _btn, canUp))
                s.UpgradeArtifact(i, VoxelCharacterData.MaxArtifactLevel);

            y += 76f;
        }

        y += 4f;
        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(x, y, w, 20f), "装上的圣遗物词条会真的加到生命 / 攻击 / 暴击上（卸下就失效）", _small);

        return y + 24f;
    }

    /// <summary>主词条那一行的文字（"生命值 200"）</summary>
    static string StatLineOf(VoxelCharacterData.Stat stat, float value) =>
        VoxelCharacterData.StatName(stat) + " " + VoxelCharacterData.StatText(stat, value);

    // ---------- 武器页 ----------

    float DrawWeaponPage(float y0)
    {
        var s = _sel;
        float x = _content.x + 18f, w = _content.width - 36f;
        float y = y0 + 16f;

        if (s == null) { Hint(x, y, w, "左边先选一个角色"); return y + 30f; }

        var weapon = s.Weapon;

        if (weapon == null)
        {
            _gold.normal.textColor = TextDim;
            VoxelHud.LabelShadow(new Rect(x, y, w, 22f), "当前武器：空手", _gold);
            y += 30f;
        }
        else
        {
            var card = new Rect(x, y, w, 120f);
            Color tint = StarColor(weapon.Rarity);

            VoxelHud.FillRect(card, new Color(tint.r, tint.g, tint.b, 0.13f));
            VoxelHud.FillRect(new Rect(card.x, card.y, 3f, card.height), tint);

            _body.normal.textColor = Text;
            VoxelHud.LabelShadow(new Rect(card.x + 12f, card.y + 8f, card.width - 24f, 24f),
                                 $"{weapon.Name}　{Stars(weapon.Rarity)}", _body);

            _small.normal.textColor = TextDim;
            VoxelHud.LabelShadow(new Rect(card.x + 12f, card.y + 34f, card.width - 24f, 20f),
                                 $"{VoxelCharacterData.TypeName(weapon.Type)}　Lv.{s.WeaponLevel} / {VoxelCharacterData.MaxWeaponLevel}" +
                                 $"　精炼 {s.Refine} 阶（同名抽到 {_sheet.OwnedCount(weapon.Name)} 把）" +
                                 $"　基础攻击力 {s.WeaponAttack:0}", _small);

            VoxelHud.LabelShadow(new Rect(card.x + 12f, card.y + 58f, card.width - 24f, 20f),
                                 $"被动 · {weapon.Passive}", _gold);
            VoxelHud.LabelShadow(new Rect(card.x + 12f, card.y + 80f, card.width - 24f, 20f),
                                 weapon.PassiveDesc + "　（被动为说明文字，暂未参与计算）", _small);

            bool canUp = s.WeaponLevel < VoxelCharacterData.MaxWeaponLevel;
            if (Button(new Rect(card.xMax - 268f, card.yMax - 40f, 76f, 30f), "+1", _btn, canUp)) s.WeaponLevelUp(1);
            if (Button(new Rect(card.xMax - 186f, card.yMax - 40f, 76f, 30f), "+10", _btn, canUp)) s.WeaponLevelUp(10);
            if (Button(new Rect(card.xMax - 104f, card.yMax - 40f, 92f, 30f), "卸下", _btn, true))
            {
                s.UnequipWeapon();
                RefreshWeapons();
            }

            y = card.yMax + 12f;
        }

        _gold.normal.textColor = Gold;
        VoxelHud.LabelShadow(new Rect(x, y, w, 22f),
                             $"可装备的武器（{VoxelCharacterData.TypeName(s.Info.Weapon)}，已抽到的）", _gold);
        y += 28f;

        if (_weapons.Count == 0)
        {
            _small.normal.textColor = TextDim;
            VoxelHud.LabelShadow(new Rect(x, y, w, 20f),
                                 $"身上还没有 {VoxelCharacterData.TypeName(s.Info.Weapon)} —— 按 P 祈愿抽武器吧", _small);
            return y + 24f;
        }

        foreach (var candidate in _weapons)
        {
            var row = new Rect(x, y, w, 44f);
            if (!Visible(row)) { y += 50f; continue; }

            bool equipped = s.WeaponName == candidate.Name;

            VoxelHud.FillRect(row, new Color(1f, 1f, 1f, equipped ? 0.10f : 0.05f));

            _body.normal.textColor = equipped ? Gold : Text;
            VoxelHud.LabelShadow(new Rect(row.x + 12f, row.y + 10f, row.width - 260f, 24f),
                                 $"{candidate.Name}　{Stars(candidate.Rarity)}　" +
                                 $"基础攻击力 {candidate.BaseAtk:0}　拥有 {_sheet.OwnedCount(candidate.Name)}", _body);

            if (Button(new Rect(row.xMax - 104f, row.y + 6f, 92f, 32f), equipped ? "已装备" : "装备", _btn, !equipped))
            {
                s.EquipWeapon(candidate.Name);
                RefreshWeapons();
            }

            y += 50f;
        }

        return y + 16f;
    }

    // ---------- 小工具 ----------

    void Hint(float x, float y, float w, string text)
    {
        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(x, y, w, 22f), text, _small);
    }

    /// <summary>一行"名字 …… 数值"，返回下一行的 y</summary>
    float StatRow(float x, float y, float w, string label, string value, Color color)
    {
        var row = new Rect(x, y, w, 26f);
        if (!Visible(row)) return y + 28f;

        VoxelHud.FillRect(row, new Color(1f, 1f, 1f, 0.04f));

        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(row.x + 10f, row.y + 3f, row.width - 20f, 20f), label, _small);

        _body.normal.textColor = color;
        VoxelHud.LabelShadow(new Rect(row.x, row.y + 3f, row.width - 12f, 20f), value, _rightBody);

        return y + 28f;
    }

    /// <summary>画一个按钮；返回是不是被点了（点完把事件吃掉，一次点击只触发一个按钮）。
    /// clipped = 这个按钮在会滚动的右侧内容区里，翻出视野之后就不该再点得到</summary>
    bool Button(Rect r, string label, GUIStyle style, bool enabled, bool clipped = true)
    {
        bool hot = enabled && (!clipped || Visible(r)) && r.Contains(Event.current.mousePosition);

        VoxelHud.FillRect(r, hot ? new Color(1f, 1f, 1f, 0.20f) : BtnBg);
        Frame(r, enabled ? (hot ? Gold : new Color(1f, 1f, 1f, 0.22f)) : new Color(1f, 1f, 1f, 0.10f), 1f);

        style.normal.textColor = enabled ? (hot ? Gold : Text) : new Color(1f, 1f, 1f, 0.28f);
        VoxelHud.LabelShadow(r, label, style);

        if (!hot || Event.current.type != EventType.MouseUp || Event.current.button != 0) return false;

        VoxelAudio.Click();
        Event.current.Use();
        return true;
    }

    /// <summary>描边方框（只画四条边，不填底）</summary>
    static void Frame(Rect r, Color line, float thickness)
    {
        VoxelHud.FillRect(new Rect(r.x, r.y, r.width, thickness), line);
        VoxelHud.FillRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), line);
        VoxelHud.FillRect(new Rect(r.x, r.y, thickness, r.height), line);
        VoxelHud.FillRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), line);
    }

    static string Stars(int rarity)
    {
        if (rarity >= 5) return "★★★★★";
        if (rarity == 4) return "★★★★";
        if (rarity == 3) return "★★★";
        if (rarity == 2) return "★★";
        return "★";
    }

    static Color StarColor(int rarity)
    {
        switch (rarity)
        {
            case 5:  return new Color(1f, 0.79f, 0.35f);
            case 4:  return new Color(0.75f, 0.56f, 0.95f);
            case 3:  return new Color(0.42f, 0.65f, 0.93f);
            default: return new Color(0.70f, 0.72f, 0.76f);
        }
    }

    /// <summary>角色 / 武器的图标（祈愿那套 GachaIcons 里有什么就用什么，没有就返回 null 走色块）</summary>
    static Texture2D IconOf(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        if (IconCache.TryGetValue(name, out var cached)) return cached;

        var wish = VoxelGacha.Find(name);
        var icon = wish != null ? VoxelGacha.IconOf(wish) : null;

        IconCache[name] = icon;      // 没有也缓存，免得每帧重复查
        return icon;
    }

    void EnsureStyles()
    {
        if (_title != null) return;

        _title = new GUIStyle(GUI.skin.label) { fontSize = 24 };
        _title.normal.textColor = new Color(1f, 0.9f, 0.6f);

        _name = new GUIStyle(GUI.skin.label) { fontSize = 19 };
        _name.normal.textColor = Text;

        _big = new GUIStyle(GUI.skin.label) { fontSize = 42, alignment = TextAnchor.MiddleCenter };
        _big.normal.textColor = Text;

        _body = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        _body.normal.textColor = Text;

        _small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        _small.normal.textColor = TextDim;

        _gold = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        _gold.normal.textColor = Gold;

        _btn = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
        _btn.normal.textColor = Text;

        _tag = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
        _tag.normal.textColor = Gold;

        _footer = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        _footer.normal.textColor = TextDim;
    }

    /// <summary>数值那一列右对齐用的样式（和 _body 同尺寸，只在右侧对齐）</summary>
    GUIStyle _rightBody
    {
        get
        {
            if (_rightBodyStyle == null)
            {
                _rightBodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperRight };
                _rightBodyStyle.normal.textColor = Text;
            }
            return _rightBodyStyle;
        }
    }

    GUIStyle _rightBodyStyle;
}
