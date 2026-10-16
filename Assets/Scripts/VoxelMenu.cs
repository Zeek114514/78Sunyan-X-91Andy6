using UnityEngine;

/// <summary>
/// Esc 菜单（原神那种"完全暂停式的菜单"）：左边一列分页，右边是内容。
///
/// 分页：
///   · 成就：已解锁 / 未解锁都列出来（未解锁的压成灰的，但名字和描述看得见）
///   · 角色：生命 / 饱食 / 位置 / 所在区域 / 背包里的东西
///   · 地区：三个国家 + 几个主要地标，去过没有（去过 = 对应成就解锁）
///   · 操作：全部按键
///
/// 交互和祈愿 / 大地图同一套：打开时把鼠标放出来（玩家的移动 / 攻击自然就停了），
/// Esc 或右上角的 ✕ 关掉。分页可以点，也可以用 ↑↓ + 鼠标滚轮翻。
/// </summary>
public class VoxelMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    VoxelInteraction _inventory;
    VoxelPlayer _player;
    VoxelStats _stats;

    enum Page { Achievements = 0, Character, Regions, Controls, Save }
    Page _page = Page.Achievements;

    int _hoverTab = -1;          // 鼠标停在哪个分页上（-1 = 没停）
    int _scroll;                 // 成就页的滚动（滚轮一格 40 像素）
    bool _resetAsked;            // 重置存档的二次确认：第一次点只问一句

    GUIStyle _title, _tab, _tabOn, _head, _pageTitle, _body, _small, _gold;

    static readonly string[] Tabs = { "成就", "角色", "地区", "操作", "存档" };

    public void Init(VoxelInteraction inventory, VoxelPlayer player, VoxelStats stats)
    {
        _inventory = inventory;
        _player = player;
        _stats = stats;
    }

    void Update()
    {
        if (!IsOpen)
        {
            // 只在"游戏内"（鼠标是锁着的）才开菜单：别的面板开着时鼠标是放开的，
            // 它们在各自的 Update 里用 Esc 关闭 —— 这里不抢，免得同一帧关一个又开一个
            bool free = VoxelPanels.CanUseInput;

            // O 开菜单；Esc 也开（停在上次看的那一页）
            if (free && (Input.GetKeyDown(KeyCode.O) || Input.GetKeyDown(KeyCode.Escape))) Open();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (Input.GetKeyDown(KeyCode.O)) { Close(); return; }        // O 也是开关

        if (Input.GetKeyDown(KeyCode.UpArrow))
            _page = (Page)Mathf.Max(0, (int)_page - 1);
        if (Input.GetKeyDown(KeyCode.DownArrow))
            _page = (Page)Mathf.Min(Tabs.Length - 1, (int)_page + 1);

        // 滚轮翻页
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (wheel > 0.01f && _page == Page.Achievements && _scroll > 0) _scroll -= 40;
        else if (wheel < -0.01f)
        {
            if (_page == Page.Achievements) _scroll += 40;
            else _page = (Page)Mathf.Min(Tabs.Length - 1, (int)_page + 1);
        }
    }

    void Open()
    {
        IsOpen = true;
        Achievements.Load();
        Cursor.lockState = CursorLockMode.None;      // 放开鼠标才能点分页
        Cursor.visible = true;
    }

    void Close()
    {
        IsOpen = false;
        Cursor.lockState = CursorLockMode.Locked;    // 关掉直接回游戏
        Cursor.visible = false;
        _hoverTab = -1;
    }

    void OnDestroy() => IsOpen = false;              // 停播时清掉静态状态

    // ================= 画界面 =================

    void OnGUI()
    {
        if (!IsOpen) return;

        EnsureStyles();
        Layout(out Rect panel, out Rect side, out Rect content);

        DrawBackdrop();
        DrawSidebar(side);
        DrawContent(panel, content);
        DrawFooter(panel);
        HandleClicks(side, panel);
    }

    /// <summary>整块面板 / 左侧分页条 / 右侧内容区</summary>
    void Layout(out Rect panel, out Rect side, out Rect content)
    {
        float w = Mathf.Min(1060f, Screen.width * 0.86f);
        float h = Mathf.Min(600f, Screen.height * 0.86f);

        panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

        const float sideW = 190f;
        side    = new Rect(panel.x, panel.y, sideW, panel.height);
        content = new Rect(panel.x + sideW + 26f, panel.y + 26f,
                           panel.width - sideW - 52f, panel.height - 78f);
    }

    void DrawBackdrop()
    {
        VoxelHud.FillRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.04f, 0.05f, 0.08f, 0.72f));
    }

    void DrawSidebar(Rect side)
    {
        // 左侧一整条深色 + 右边一道金线
        VoxelHud.FillRect(side, new Color(0.07f, 0.08f, 0.12f, 0.92f));
        VoxelHud.FillRect(new Rect(side.xMax - 2f, side.y, 2f, side.height), new Color(0.93f, 0.76f, 0.35f, 0.55f));

        VoxelHud.LabelShadow(new Rect(side.x + 20f, side.y + 20f, side.width - 40f, 30f), "菜 单", _title);

        for (int i = 0; i < Tabs.Length; i++)
        {
            var r = TabRect(side, i);
            bool on = (int)_page == i;
            bool hot = r.Contains(Event.current.mousePosition);

            if (on) VoxelHud.FillRect(r, new Color(0.93f, 0.76f, 0.35f, 0.16f));
            else if (hot) VoxelHud.FillRect(r, new Color(1f, 1f, 1f, 0.06f));

            if (on) VoxelHud.FillRect(new Rect(r.x, r.y + 6f, 3f, r.height - 12f), new Color(0.93f, 0.76f, 0.35f, 0.95f));

            VoxelHud.LabelShadow(new Rect(r.x + 22f, r.y + 7f, r.width - 30f, 24f), Tabs[i], on ? _tabOn : _tab);
        }

        // 左上角那一列也顺带报一下成就进度
        VoxelHud.LabelShadow(new Rect(side.x + 20f, side.yMax - 40f, side.width - 40f, 22f),
                             $"成就 {Achievements.UnlockedCount} / {Achievements.Count}", _small);
    }

    Rect TabRect(Rect side, int i) => new Rect(side.x + 8f, side.y + 74f + i * 52f, side.width - 16f, 44f);

    void DrawFooter(Rect panel)
    {
        VoxelHud.LabelShadow(new Rect(panel.x + 26f, panel.yMax - 38f, panel.width - 120f, 22f),
                             "O 关闭菜单 · Esc 也行 · ↑↓ / 滚轮 翻页 · 分页可以直接点", _small);

        var close = CloseRect(panel);
        bool hot = close.Contains(Event.current.mousePosition);
        VoxelHud.FillRect(close, hot ? new Color(1f, 1f, 1f, 0.18f) : new Color(1f, 1f, 1f, 0.07f));
        VoxelHud.LabelShadow(close, "✕", _head);
    }

    Rect CloseRect(Rect panel) => new Rect(panel.xMax - 52f, panel.y + 18f, 34f, 34f);

    void DrawContent(Rect panel, Rect content)
    {
        // 分页标题 + 一条金线（标题左对齐；_head 是居中的，给星号和大字用）
        VoxelHud.LabelShadow(new Rect(content.x, content.y, content.width, 34f), Tabs[(int)_page], _pageTitle);
        VoxelHud.FillRect(new Rect(content.x, content.y + 34f, 96f, 2f), new Color(0.93f, 0.76f, 0.35f, 0.8f));

        switch (_page)
        {
            case Page.Achievements: DrawAchievements(content); break;
            case Page.Character:    DrawCharacter(content); break;
            case Page.Regions:      DrawRegions(content); break;
            case Page.Controls:     DrawControls(content); break;
            default:                DrawSave(content); break;
        }
    }

    // ---------- 存档页 ----------

    /// <summary>存档概览 + 重置存档（两步确认：第一次点只问一句，再点一次才真清）</summary>
    void DrawSave(Rect content)
    {
        float x = content.x, w = content.width;
        float y = content.y + 48f;

        VoxelHud.LabelShadow(new Rect(x, y, w, 26f), "存档概览", _gold);
        y += 32f;

        var active = VoxelCharacterSheet.Instance != null ? VoxelCharacterSheet.Instance.Active : null;
        var stats = _stats;

        VoxelHud.LabelShadow(new Rect(x + 14f, y, w - 20f, 22f),
                             $"成就　{Achievements.UnlockedCount} / {Achievements.Count}", _body);
        y += 24f;
        VoxelHud.LabelShadow(new Rect(x + 14f, y, w - 20f, 22f),
                             active != null ? $"出战角色　{active.Name}　Lv.{active.Level}　突破 {active.Phase} 阶" : "出战角色　—", _body);
        y += 24f;
        VoxelHud.LabelShadow(new Rect(x + 14f, y, w - 20f, 22f),
                             $"世界等级　{VoxelWorldLevel.Level}（野怪强度 ×{VoxelWorldLevel.Scale:0.00}）", _body);
        y += 24f;
        if (stats != null)
        {
            VoxelHud.LabelShadow(new Rect(x + 14f, y, w - 20f, 22f),
                                 $"生命 {stats.Health:0} / {stats.MaxHealth:0}　饱食度 {stats.Hunger:0} / 100", _body);
            y += 24f;
        }

        y += 16f;
        VoxelHud.LabelShadow(new Rect(x, y, w, 22f), "清掉的东西：成就解锁、祈愿保底与抽卡历史、全部角色养成", _small);
        y += 24f;
        VoxelHud.LabelShadow(new Rect(x, y, w, 22f), "物品栏里的东西（原石 / 纠缠之缘 / 素材 / 武器）不在存档里，不受影响", _small);
        y += 30f;

        // 重置按钮（两步确认）
        var button = new Rect(x, y, 320f, 40f);
        bool hot = button.Contains(Event.current.mousePosition);

        VoxelHud.FillRect(button, hot ? new Color(0.75f, 0.22f, 0.22f, 0.55f) : new Color(0.55f, 0.16f, 0.16f, 0.35f));
        VoxelHud.FillRect(new Rect(button.x, button.y, 3f, button.height), new Color(0.92f, 0.4f, 0.35f, 0.95f));

        _body.normal.textColor = hot ? Color.white : new Color(1f, 0.85f, 0.82f);
        VoxelHud.LabelShadow(button, _resetAsked ? "再点一次确认重置" : "重置存档", _head);

        y += 50f;
        if (_resetAsked)
        {
            _small.normal.textColor = new Color(1f, 0.72f, 0.62f);
            VoxelHud.LabelShadow(new Rect(x, y, w, 22f), "⚠ 这一步不可撤销（点别处取消）", _small);
        }

        if (!hot || Event.current.type != EventType.MouseUp || Event.current.button != 0) return;

        Event.current.Use();

        if (!_resetAsked) { _resetAsked = true; return; }      // 第一次点：先问一句

        Achievements.ClearAll();
        VoxelGacha.ClearProgress();
        if (VoxelCharacterSheet.Instance != null) VoxelCharacterSheet.Instance.ClearAll();

        _resetAsked = false;
        VoxelChat.Announce("存档已重置：成就 / 祈愿保底 / 角色养成 都回到初始状态");
    }

    // ---------- 成就页 ----------

    void DrawAchievements(Rect content)
    {
        float y = content.y + 46f;

        VoxelHud.LabelShadow(new Rect(content.x, y, content.width, 22f),
            $"已解锁 {Achievements.UnlockedCount} / {Achievements.Count}" +
            (Achievements.UnlockedCount == Achievements.Count ? "（全部达成！）" : ""), _gold);
        y += 30f;

        // 滚轮：整段列表跟着 offset 走，超出的部分裁掉
        float top = y, viewH = content.yMax - y;
        float offset = -Mathf.Clamp(_scroll, 0, Mathf.Max(0, Mathf.RoundToInt(Achievements.Count * 66f + 10f - viewH)));

        for (int i = 0; i < Achievements.Count; i++)
        {
            var e = Achievements.All[i];
            var row = new Rect(content.x, top + offset + i * 66f, content.width, 60f);

            if (row.yMax < top || row.y > content.yMax) continue;      // 视野外的跳过

            VoxelHud.FillRect(row, e.unlocked
                ? new Color(0.93f, 0.76f, 0.35f, 0.13f)
                : new Color(1f, 1f, 1f, 0.05f));
            if (e.unlocked)
                VoxelHud.FillRect(new Rect(row.x, row.y, 3f, row.height), new Color(0.93f, 0.76f, 0.35f, 0.9f));

            // 左边一个星：解锁了是金色实心，没解锁是灰的空心
            _head.normal.textColor = e.unlocked ? new Color(1f, 0.86f, 0.42f) : new Color(1f, 1f, 1f, 0.28f);
            VoxelHud.LabelShadow(new Rect(row.x + 12f, row.y + 14f, 32f, 30f), e.unlocked ? "★" : "☆", _head);

            _body.normal.textColor = e.unlocked ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            VoxelHud.LabelShadow(new Rect(row.x + 52f, row.y + 8f, row.width - 190f, 24f), e.name, _body);

            _small.normal.textColor = e.unlocked ? new Color(1f, 1f, 1f, 0.72f) : new Color(1f, 1f, 1f, 0.32f);
            VoxelHud.LabelShadow(new Rect(row.x + 52f, row.y + 32f, row.width - 190f, 20f), e.desc, _small);

            _small.normal.textColor = e.unlocked ? new Color(1f, 0.86f, 0.42f, 0.95f) : new Color(1f, 1f, 1f, 0.28f);
            VoxelHud.LabelShadow(new Rect(row.xMax - 132f, row.y + 20f, 118f, 22f),
                                 e.unlocked ? "已解锁" : "未解锁", _small);
        }
    }

    // ---------- 角色页 ----------

    void DrawCharacter(Rect content)
    {
        float y = content.y + 48f;
        float x = content.x;

        // 出战角色（养成的明细在 C 键角色界面里）
        var active = VoxelCharacterSheet.Instance != null ? VoxelCharacterSheet.Instance.Active : null;

        VoxelHud.LabelShadow(new Rect(x, y, content.width, 30f),
                             active != null ? $"{active.Name}　Lv.{active.Level}" : "Sunyan", _gold);
        y += 32f;

        if (active != null)
        {
            VoxelHud.LabelShadow(new Rect(x + 14f, y, content.width - 20f, 22f),
                                 $"攻击力 {active.Attack:0}　防御力 {active.Defense:0}　" +
                                 $"暴击 {active.CritRate * 100f:0.#}% / {active.CritDamage * 100f:0.#}%　" +
                                 $"每下普攻 {active.NormalAttackDamage:0}", _small);
            y += 22f;

            VoxelHud.LabelShadow(new Rect(x + 14f, y, content.width - 20f, 22f),
                                 $"武器 {(active.Weapon != null ? active.Weapon.Name : "空手")}　" +
                                 $"圣遗物 {active.EquippedCount}/5 件　按 C 看养成 / 升级 / 换装备", _small);
            y += 30f;
        }
        else
        {
            y += 8f;
        }

        if (_stats != null)
        {
            VoxelHud.LabelShadow(new Rect(x, y, 60f, 20f), "生命", _small);
            VoxelHud.SegmentedBar(x + 56f, y, _stats.Health, new Color(0.83f, 0.18f, 0.18f), 14f, 16f, _stats.MaxHealth);
            VoxelHud.LabelShadow(new Rect(x + 220f, y, 160f, 20f),
                                 $"{_stats.Health:0} / {_stats.MaxHealth:0}", _small);
            y += 30f;

            VoxelHud.LabelShadow(new Rect(x, y, 60f, 20f), "饱食", _small);
            VoxelHud.SegmentedBar(x + 56f, y, _stats.Hunger, new Color(0.93f, 0.62f, 0.12f), 14f, 16f);
            VoxelHud.LabelShadow(new Rect(x + 220f, y, 120f, 20f), $"{_stats.Hunger:0} / 100", _small);
            y += 38f;
        }

        if (_player != null)
        {
            Vector3 p = _player.transform.position;
            VoxelHud.LabelShadow(new Rect(x, y, 60f, 20f), "位置", _small);
            VoxelHud.LabelShadow(new Rect(x + 56f, y, content.width - 60f, 20f),
                                 $"X {p.x:0}   Y {p.y:0}   Z {p.z:0}   （海拔 {p.y:0} 米）", _body);
            y += 28f;

            VoxelHud.LabelShadow(new Rect(x, y, 60f, 20f), "区域", _small);
            VoxelHud.LabelShadow(new Rect(x + 56f, y, content.width - 60f, 20f),
                                 WorldMap.RegionName(p.x, p.z), _body);
            y += 28f;

            VoxelHud.LabelShadow(new Rect(x, y, 60f, 20f), "状态", _small);
            VoxelHud.LabelShadow(new Rect(x + 56f, y, content.width - 60f, 20f),
                                 _player.isFlying ? "飞行中（/fly 关闭）" : "步行 / 疾跑", _body);
            y += 38f;
        }

        if (_inventory != null)
        {
            VoxelHud.LabelShadow(new Rect(x, y, content.width, 22f), "背包", _gold);
            y += 28f;

            VoxelHud.LabelShadow(new Rect(x + 14f, y, content.width - 20f, 22f),
                                 $"原石 ×{_inventory.PrimogemCount}      纠缠之缘 ×{_inventory.FateCount}", _body);
            y += 26f;
            VoxelHud.LabelShadow(new Rect(x + 14f, y, content.width - 20f, 22f),
                                 $"Andy的币 ×{_inventory.AndyCoinCount}      手里拿着：{_inventory.SelectedName}", _body);
            y += 32f;

            VoxelHud.LabelShadow(new Rect(x, y, content.width, 22f),
                                 "按 B 打开背包看道具（分类储物）；角色养成与武器在 C 键界面", _small);
        }
    }

    // ---------- 地区页 ----------

    void DrawRegions(Rect content)
    {
        float y = content.y + 48f;

        // 地区名 ⇄ 成就 id：去过没有就直接看那个成就（一处记，两处用）
        (string region, string desc, string ach)[] rows =
        {
            ("湖心城 · 蒙德城", "环绕护城河的湖心城，蒙德的主城", "mondstadt"),
            ("璃月 · 喀斯特峰林", "细而高的石灰岩峰丛", "karst"),
            ("Andy的雪顶", "压在两国的分界线上的雪山", "snow"),
            ("Andy的港门", "依山面海的大港，璃月的主城", "harbor"),
            ("稻妻 · 将军府", "海外的岛国稻妻，天守立在高台上", "inazuma"),
        };

        foreach (var r in rows)
        {
            bool got = Achievements.IsUnlocked(r.ach);

            VoxelHud.FillRect(new Rect(content.x, y, content.width, 44f),
                              got ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.18f));

            _body.normal.textColor = got ? Color.white : new Color(1f, 1f, 1f, 0.42f);
            VoxelHud.LabelShadow(new Rect(content.x + 16f, y + 4f, content.width - 200f, 22f), r.region, _body);

            _small.normal.textColor = got ? new Color(1f, 1f, 1f, 0.6f) : new Color(1f, 1f, 1f, 0.3f);
            VoxelHud.LabelShadow(new Rect(content.x + 16f, y + 24f, content.width - 200f, 18f), r.desc, _small);

            _small.normal.textColor = got ? new Color(1f, 0.86f, 0.42f, 0.95f) : new Color(1f, 1f, 1f, 0.28f);
            VoxelHud.LabelShadow(new Rect(content.xMax - 130f, y + 12f, 116f, 22f),
                                 got ? "已探索" : "未探索", _small);

            y += 50f;
        }

        if (_player != null)
        {
            Vector3 p = _player.transform.position;
            y += 6f;
            VoxelHud.LabelShadow(new Rect(content.x, y, content.width, 22f),
                                 $"当前位置：{WorldMap.RegionName(p.x, p.z)}（{p.x:0}, {p.z:0}）", _gold);
        }
    }

    // ---------- 操作页 ----------

    void DrawControls(Rect content)
    {
        string[] lines =
        {
            "WASD          移动            Shift  疾跑            空格  跳跃",
            "Ctrl          下降（飞行时）",
            "鼠标左键      普通攻击            鼠标右键  元素战技（有冷却）",
            "F             元素爆发（要攒满元素能量）",
            "鼠标右键 / 数字键 / 滚轮  切换手持物",
            "E             开宝箱 / 交互     Q      丢掉手上的道具",
            "T             对话框（聊天 / 指令：/fly /eat /status）",
            "O             菜单（成就 / 角色 / 地区 / 操作），再按一次关闭",
            "B             背包（分类储物 / 看道具详情），再按一次关闭",
            "C             角色（养成：等级 / 天赋 / 技能 / 圣遗物 / 武器），再按一次关闭",
            "P             祈愿（抽卡）      M      世界地图 + 小地图",
            "Esc           也是菜单          点画面  锁定鼠标继续游戏",
        };

        float y = content.y + 50f;
        foreach (var line in lines)
        {
            VoxelHud.LabelShadow(new Rect(content.x, y, content.width, 24f), line, _body);
            y += 30f;
        }

        y += 10f;
        VoxelHud.LabelShadow(new Rect(content.x, y, content.width, 22f),
                             "走近传送锚点 / 七天神像会自动点亮，之后在大地图上点图钉就能传送；", _small);
        y += 22f;
        VoxelHud.LabelShadow(new Rect(content.x, y, content.width, 22f),
                             "走到七天神像脚边还能把生命和体力一起回满。", _small);
    }

    // ---------- 点击 ----------

    void HandleClicks(Rect side, Rect panel)
    {
        Event e = Event.current;
        if (e.type != EventType.MouseUp || e.button != 0) return;

        Vector2 m = e.mousePosition;

        if (CloseRect(panel).Contains(m)) { Close(); e.Use(); return; }

        for (int i = 0; i < Tabs.Length; i++)
        {
            if (!TabRect(side, i).Contains(m)) continue;
            _page = (Page)i;
            _scroll = 0;
            VoxelAudio.Click();
            e.Use();
            return;
        }
    }

    // ---------- 样式 ----------

    void EnsureStyles()
    {
        if (_title != null) return;

        _title = new GUIStyle(GUI.skin.label) { fontSize = 24 };
        _title.normal.textColor = new Color(1f, 0.9f, 0.6f);

        _tab = new GUIStyle(GUI.skin.label) { fontSize = 17 };
        _tab.normal.textColor = new Color(1f, 1f, 1f, 0.72f);

        _tabOn = new GUIStyle(GUI.skin.label) { fontSize = 18 };
        _tabOn.normal.textColor = new Color(1f, 0.9f, 0.58f);

        _head = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        _head.normal.textColor = Color.white;

        _pageTitle = new GUIStyle(GUI.skin.label) { fontSize = 22 };
        _pageTitle.normal.textColor = new Color(1f, 0.94f, 0.8f);

        _body = new GUIStyle(GUI.skin.label) { fontSize = 15 };
        _body.normal.textColor = Color.white;

        _small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        _small.normal.textColor = new Color(1f, 1f, 1f, 0.6f);

        _gold = new GUIStyle(GUI.skin.label) { fontSize = 16 };
        _gold.normal.textColor = new Color(1f, 0.88f, 0.52f);
    }
}
