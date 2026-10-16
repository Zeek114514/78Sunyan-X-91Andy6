using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 背包（B 键开关）：原神那种分页式储物 —— 左边一列分类（全部 / 道具 / 材料 / 武器），
/// 中间一格一件、右下角标数量，右边是选中道具的详情（名字、星级、拥有数量和说明）。
///
/// 东西从哪儿来：战利品宝箱开出的 Andy的币 / 原石 / 素材、地上捡回的原石与纠缠之缘，
/// 以及祈愿抽到的武器。角色不在背包里 —— 他们是 C 键的角色界面（VoxelCharacterScreen）。
///
/// 交互和其它面板同一套：打开时放开鼠标（玩家的移动 / 攻击 / 开箱自然就停了），
/// B 或 Esc 关掉。分类可以点、也能用 ↑↓ 切；滚轮翻格子。
/// </summary>
public class VoxelInventory : MonoBehaviour
{
    /// <summary>背包是否打开（HUD / 交互 / 对话 / 祈愿 / 菜单 / 角色界面 / 大地图靠它避让）</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>分类。All 只是"一览"，其余三个才是真正的储物罐</summary>
    enum Cat { All = 0, Gadget, Material, Weapon }

    static readonly string[] Tabs = { "全部", "道具", "材料", "武器" };

    // ---------- 配色（和菜单 / 祈愿同一套：深底 + 香槟金） ----------

    static readonly Color SideBg   = new Color(0.07f, 0.08f, 0.12f, 0.96f);
    static readonly Color DetailBg = new Color(0.05f, 0.06f, 0.09f, 0.90f);
    static readonly Color Gold     = new Color(0.93f, 0.76f, 0.35f);
    static readonly Color Text     = new Color(1f, 1f, 1f, 0.95f);
    static readonly Color TextDim  = new Color(1f, 1f, 1f, 0.55f);

    /// <summary>格子的边长 / 格间距（滚轮一格的步长也用它）</summary>
    const float Cell = 92f, Gap = 12f, CellStep = Cell + Gap;

    // ---------- 道具资料 ----------

    /// <summary>一件东西的静态资料：分类、星级、说明、图标</summary>
    class Info
    {
        public readonly Cat    Cat;
        public readonly int    Rarity;
        public readonly string Desc;
        public readonly string Icon;      // Resources 根目录下的图标名；留空就画文字色块

        public Info(Cat cat, int rarity, string desc, string icon = null)
        {
            Cat = cat; Rarity = rarity; Desc = desc; Icon = icon;
        }
    }

    /// <summary>已知道具的资料表。宝箱素材和三种"不占手上格子"的道具都登记在这儿；
    /// 没登记的（以后新加的素材、奖池里的角色武器）由 InfoOf 兜底，不会画不出来。</summary>
    static readonly Dictionary<string, Info> Catalog = new Dictionary<string, Info>
    {
        { "原石",       new Info(Cat.Gadget,   5, "提瓦特通行的稀有结晶，可以当作祈愿的资费。", "PrimogemIcon") },
        { "纠缠之缘",   new Info(Cat.Gadget,   5, "祈愿时的信物，一个可以祈愿一次。", "IntertwinedFateIcon") },
        { "Andy的币",   new Info(Cat.Gadget,   3, "战利品宝箱里开出来的通用货币，Andy 本人也还没想好怎么花。") },

        { "史莱姆凝液", new Info(Cat.Material, 1, "史莱姆体内凝结的黏稠液体，摸上去滑滑的。") },
        { "风车菊",     new Info(Cat.Material, 1, "偏爱风的植物，花瓣转起来像小小的风车。") },
        { "蒲公英籽",   new Info(Cat.Material, 1, "随风飘散的种子，蒙德的孩子喜欢对着它许愿。") },
        { "丘丘人面具", new Info(Cat.Material, 2, "丘丘人戴的面具，做工粗糙却颇有威慑力。") },
        { "水晶矿",     new Info(Cat.Material, 3, "透着清光的矿石，能锻出不错的武器。") },
        { "精锻用魔矿", new Info(Cat.Material, 4, "精练过的魔矿，锻高级武器时的关键材料。") },
    };

    /// <summary>背包里的一格</summary>
    class Entry
    {
        public string    Name;
        public int       Count;
        public Info      Info;
        public Texture2D Icon;      // 没图标就是 null，格子里改画名字前两个字
    }

    // ---------- 运行时状态 ----------

    VoxelInteraction _inventory;

    readonly List<Entry> _owned = new List<Entry>();      // 全部持有物（打开背包时清点一次）
    readonly List<Entry> _items = new List<Entry>();      // 当前分类筛出来、真正要画的那些
    readonly int[] _counts = new int[Tabs.Length];        // 各分类各有多少件（分页上显示）

    string _selected;                                     // 选中道具的名字（跨分类记住）
    Cat    _cat = Cat.All;
    int    _scroll;

    readonly Rect[] _tabs = new Rect[Tabs.Length];
    Rect _panel, _side, _list, _grid, _detail, _close;

    GUIStyle _title, _tab, _tabOn, _count, _name, _star, _body, _small, _mark, _center;

    static readonly Dictionary<string, Texture2D> IconCache = new Dictionary<string, Texture2D>();

    public void Init(VoxelInteraction inventory)
    {
        _inventory = inventory;
    }

    // ---------- 开关 ----------

    void Update()
    {
        if (!IsOpen)
        {
            // 只在"游戏内"才开：别的面板开着时鼠标是放开的，它们各自用 Esc 关
            bool free = VoxelPanels.CanUseInput;

            if (free && Input.GetKeyDown(KeyCode.B)) Open();
            return;
        }

        if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

        if (Input.GetKeyDown(KeyCode.UpArrow))   SetCategory((Cat)Mathf.Max(0, (int)_cat - 1));
        if (Input.GetKeyDown(KeyCode.DownArrow)) SetCategory((Cat)Mathf.Min(Tabs.Length - 1, (int)_cat + 1));

        // 滚轮一格一行
        float wheel = Input.GetAxisRaw("Mouse ScrollWheel");
        if (wheel > 0.01f)       _scroll = Mathf.Max(0, _scroll - Mathf.RoundToInt(CellStep));
        else if (wheel < -0.01f) _scroll = Mathf.Min(MaxScroll, _scroll + Mathf.RoundToInt(CellStep));
    }

    void Open()
    {
        IsOpen = true;
        Rebuild();
        Cursor.lockState = CursorLockMode.None;      // 放开鼠标才能点分类和格子
        Cursor.visible = true;
    }

    void Close()
    {
        IsOpen = false;
        Cursor.lockState = CursorLockMode.Locked;    // 关掉直接回游戏
        Cursor.visible = false;
    }

    void OnDestroy() => IsOpen = false;              // 停播时清掉静态状态

    // ---------- 清点家当 ----------

    /// <summary>把 VoxelInteraction 里持有的东西整理成格子。只在打开时跑一次
    /// —— 开着的时候游戏是暂停的，数量不会变，不用每帧重算</summary>
    void Rebuild()
    {
        _owned.Clear();

        if (_inventory != null)
        {
            Add(_inventory.PrimogemCount, "原石");
            Add(_inventory.FateCount,     "纠缠之缘");
            Add(_inventory.AndyCoinCount, "Andy的币");

            var names = _inventory.MaterialNames;
            for (int i = 0; i < names.Count; i++)
                Add(_inventory.MaterialCount(names[i]), names[i]);
        }

        // 星级高的排前面，同星级按分类再按名字 —— 顺序稳定，重开背包格子不会跳
        _owned.Sort((a, b) =>
        {
            if (a.Info.Rarity != b.Info.Rarity) return b.Info.Rarity.CompareTo(a.Info.Rarity);
            if (a.Info.Cat    != b.Info.Cat)    return a.Info.Cat.CompareTo(b.Info.Cat);
            return string.CompareOrdinal(a.Name, b.Name);
        });

        for (int i = 0; i < _counts.Length; i++) _counts[i] = 0;
        for (int i = 0; i < _owned.Count; i++)
        {
            _counts[(int)Cat.All]++;
            _counts[(int)_owned[i].Info.Cat]++;
        }

        Refilter();
        if (!_items.Exists(e => e.Name == _selected))
            _selected = _items.Count > 0 ? _items[0].Name : null;
    }

    /// <summary>收到一格（数量 0 的东西不进背包；角色也不进 —— 他们在 C 键的角色界面里）</summary>
    void Add(int count, string name)
    {
        if (count <= 0) return;
        if (VoxelCharacterData.CharacterOf(name) != null) return;

        var info = InfoOf(name);
        _owned.Add(new Entry { Name = name, Count = count, Info = info, Icon = ResolveIcon(name, info) });
    }

    /// <summary>查一件东西的资料：先查表，表里没有就问武器表，再没有按一星素材算</summary>
    static Info InfoOf(string name)
    {
        if (Catalog.TryGetValue(name, out var known)) return known;

        var weapon = VoxelCharacterData.WeaponOf(name);
        if (weapon != null)
            return new Info(Cat.Weapon, weapon.Rarity,
                            "武器 · " + VoxelCharacterData.TypeName(weapon.Type) +
                            "。按 C 打开角色界面就能装备。");

        return new Info(Cat.Material, 1, "在提瓦特各处收集到的素材。");
    }

    /// <summary>取这一格的图标：资料表里的图标名，或者祈愿那套角色 / 武器图标；都没有就 null</summary>
    static Texture2D ResolveIcon(string name, Info info)
    {
        if (!string.IsNullOrEmpty(info.Icon)) return LoadIcon(info.Icon);

        var wish = VoxelGacha.Find(name);
        return wish != null ? VoxelGacha.IconOf(wish) : null;
    }

    /// <summary>按当前分类筛出要画的格子</summary>
    void Refilter()
    {
        _items.Clear();

        for (int i = 0; i < _owned.Count; i++)
            if (_cat == Cat.All || _owned[i].Info.Cat == _cat) _items.Add(_owned[i]);

        _scroll = 0;
    }

    void SetCategory(Cat cat)
    {
        if (_cat == cat) return;

        _cat = cat;
        Refilter();

        // 换了分类就选中这一类里的第一件（这一类是空的那就不选）
        _selected = _items.Count > 0 ? _items[0].Name : null;
    }

    Entry SelectedEntry()
    {
        if (string.IsNullOrEmpty(_selected)) return null;

        for (int i = 0; i < _owned.Count; i++)
            if (_owned[i].Name == _selected) return _owned[i];

        return null;
    }

    /// <summary>某个道具持有多少（顶上那三条货币条用）</summary>
    int OwnedCount(string name)
    {
        for (int i = 0; i < _owned.Count; i++)
            if (_owned[i].Name == name) return _owned[i].Count;

        return 0;
    }

    // ---------- 画界面 ----------

    void OnGUI()
    {
        if (!IsOpen) return;

        Layout();
        EnsureStyles();

        VoxelHud.FillRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.03f, 0.04f, 0.07f, 0.78f));

        DrawSidebar();
        DrawHeader();
        DrawGrid();
        DrawDetail();
        DrawFooter();
        HandleClicks();
    }

    /// <summary>整块面板 / 左侧分类条 / 中间格子区 / 右侧详情</summary>
    void Layout()
    {
        float w = Mathf.Min(1180f, Screen.width * 0.9f);
        float h = Mathf.Min(660f, Screen.height * 0.88f);
        _panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

        const float sideW = 168f, detailW = 322f, pad = 22f;

        _side  = new Rect(_panel.x, _panel.y, sideW, _panel.height);
        _close = new Rect(_panel.xMax - 52f, _panel.y + 14f, 34f, 34f);

        float top    = _panel.y + 78f;
        float bottom = _panel.yMax - 44f;

        _detail = new Rect(_panel.xMax - detailW - pad, top, detailW, bottom - top);

        float listX = _panel.x + sideW + pad;
        _list = new Rect(listX, top, _detail.x - Gap - listX, bottom - top);
        _grid = new Rect(_list.x, _list.y + 40f, _list.width, _list.height - 40f);

        for (int i = 0; i < _tabs.Length; i++)
            _tabs[i] = new Rect(_side.x + 8f, _side.y + 74f + i * 52f, _side.width - 16f, 44f);
    }

    void DrawSidebar()
    {
        VoxelHud.FillRect(_side, SideBg);
        VoxelHud.FillRect(new Rect(_side.xMax - 2f, _side.y, 2f, _side.height), new Color(Gold.r, Gold.g, Gold.b, 0.55f));

        VoxelHud.LabelShadow(new Rect(_side.x + 20f, _side.y + 20f, _side.width - 40f, 30f), "背 包", _title);

        for (int i = 0; i < Tabs.Length; i++)
        {
            var r = _tabs[i];
            bool on  = (int)_cat == i;
            bool hot = r.Contains(Event.current.mousePosition);

            if (on) VoxelHud.FillRect(r, new Color(Gold.r, Gold.g, Gold.b, 0.16f));
            else if (hot) VoxelHud.FillRect(r, new Color(1f, 1f, 1f, 0.06f));

            if (on) VoxelHud.FillRect(new Rect(r.x, r.y + 6f, 3f, r.height - 12f), Gold);

            VoxelHud.LabelShadow(new Rect(r.x + 22f, r.y + 8f, r.width - 92f, 26f), Tabs[i], on ? _tabOn : _tab);

            _count.normal.textColor = on ? Gold : TextDim;
            VoxelHud.LabelShadow(new Rect(r.xMax - 62f, r.y + 9f, 54f, 26f), _counts[i].ToString(), _count);
        }

        VoxelHud.LabelShadow(new Rect(_side.x + 20f, _side.yMax - 40f, _side.width - 40f, 22f),
                             "共 " + _owned.Count + " 件道具", _small);
    }

    /// <summary>面板顶上那条：三种货币 + 关闭按钮</summary>
    void DrawHeader()
    {
        VoxelHud.FillRect(new Rect(_side.xMax, _panel.y, _panel.width - _side.width, 62f), new Color(0f, 0f, 0f, 0.25f));

        float x = _side.xMax + 22f;
        DrawChip(new Rect(x,        _panel.y + 14f, 176f, 38f), "原石",     OwnedCount("原石"),
                 "PrimogemIcon", new Color(0.36f, 0.72f, 0.95f));
        DrawChip(new Rect(x + 188f, _panel.y + 14f, 176f, 38f), "纠缠之缘", OwnedCount("纠缠之缘"),
                 "IntertwinedFateIcon", new Color(0.62f, 0.45f, 0.95f));
        DrawChip(new Rect(x + 376f, _panel.y + 14f, 176f, 38f), "Andy的币", OwnedCount("Andy的币"),
                 null, new Color(0.95f, 0.80f, 0.38f));

        bool hot = _close.Contains(Event.current.mousePosition);
        VoxelHud.FillRect(_close, hot ? new Color(1f, 1f, 1f, 0.18f) : new Color(1f, 1f, 1f, 0.07f));
        VoxelHud.LabelShadow(_close, "✕", _center);
    }

    /// <summary>货币小条：左边图标（没图标就一块兜底色），右边名字和数量</summary>
    void DrawChip(Rect r, string name, int count, string iconKey, Color tint)
    {
        VoxelHud.FillRect(r, new Color(1f, 1f, 1f, 0.06f));
        VoxelHud.FillRect(new Rect(r.x, r.y, 3f, r.height), new Color(tint.r, tint.g, tint.b, 0.9f));

        var box  = new Rect(r.x + 12f, r.y + 8f, 22f, 22f);
        var icon = string.IsNullOrEmpty(iconKey) ? null : LoadIcon(iconKey);

        if (icon != null) GUI.DrawTexture(box, icon, ScaleMode.ScaleToFit);
        else VoxelHud.FillRect(box, new Color(tint.r, tint.g, tint.b, 0.85f));

        float textX = box.xMax + 8f;
        float textW = r.xMax - textX - 8f;

        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(textX, r.y + 2f, textW, 18f), name, _small);

        _body.normal.textColor = Text;
        VoxelHud.LabelShadow(new Rect(textX, r.y + 18f, textW, 18f), "×" + count, _body);
    }

    /// <summary>中间那片格子：一格一件，右下角标数量</summary>
    void DrawGrid()
    {
        _scroll = Mathf.Clamp(_scroll, 0, MaxScroll);

        _name.normal.textColor = new Color(1f, 0.94f, 0.8f);
        VoxelHud.LabelShadow(new Rect(_list.x, _list.y, _list.width, 28f),
                             Tabs[(int)_cat] + "　" + _items.Count + " 件", _name);
        VoxelHud.FillRect(new Rect(_list.x, _list.y + 32f, 96f, 2f), new Color(Gold.r, Gold.g, Gold.b, 0.8f));

        if (_items.Count == 0)
        {
            _small.normal.textColor = TextDim;
            VoxelHud.LabelShadow(new Rect(_grid.x, _grid.y + 30f, _grid.width, 24f),
                                 _owned.Count == 0
                                     ? "背包里还什么都没有 —— 去野外开宝箱，或者按 P 祈愿吧"
                                     : "这一类还没有东西", _small);
            return;
        }

        int cols = Columns;
        Vector2 mouse = Event.current.mousePosition;

        for (int i = 0; i < _items.Count; i++)
        {
            var r = CellRect(i, cols);
            if (r.yMax < _grid.y - 1f || r.y > _grid.yMax + 1f) continue;   // 卷出视野的跳过

            DrawCell(r, _items[i], mouse);
        }
    }

    int Columns => Mathf.Max(1, Mathf.FloorToInt((_grid.width + Gap) / CellStep));

    int Rows => _items.Count == 0 ? 0 : Mathf.CeilToInt(_items.Count / (float)Columns);

    int MaxScroll => Mathf.Max(0, Mathf.RoundToInt(Rows * CellStep - Gap - _grid.height));

    Rect CellRect(int index, int cols) =>
        new Rect(_grid.x + (index % cols) * CellStep, _grid.y + (index / cols) * CellStep - _scroll, Cell, Cell);

    void DrawCell(Rect r, Entry e, Vector2 mouse)
    {
        bool selected = e.Name == _selected;
        bool hot      = r.Contains(mouse);
        Color tint    = RarityColor(e.Info.Rarity);

        // 底板：底色往星级色上靠一点，高星一眼就看得出（选中 / 悬停更亮）
        VoxelHud.FillRect(r, Color.Lerp(new Color(0.12f, 0.13f, 0.19f, 0.95f), tint, selected || hot ? 0.34f : 0.22f));
        Frame(r, selected ? Gold : new Color(tint.r, tint.g, tint.b, 0.85f), selected ? 3f : 2f);

        // 图标：有图贴图，没图就把名字前两个字当图标画
        var slot = new Rect(r.x + 8f, r.y + 8f, r.width - 16f, r.height - 16f);
        if (e.Icon != null)
        {
            GUI.DrawTexture(slot, e.Icon, ScaleMode.ScaleToFit);
        }
        else
        {
            _mark.normal.textColor = tint;
            VoxelHud.LabelShadow(slot, e.Name.Length > 2 ? e.Name.Substring(0, 2) : e.Name, _mark);
        }

        _count.normal.textColor = Text;
        VoxelHud.LabelShadow(new Rect(r.x, r.yMax - 22f, r.width - 6f, 20f), "×" + e.Count, _count);
    }

    /// <summary>右侧详情：选中道具的名字 / 星级 / 分类 / 数量 / 说明</summary>
    void DrawDetail()
    {
        VoxelHud.FillRect(_detail, DetailBg);
        VoxelHud.FillRect(new Rect(_detail.x, _detail.y, 2f, _detail.height), new Color(Gold.r, Gold.g, Gold.b, 0.45f));

        float x = _detail.x + 22f, w = _detail.width - 44f;
        float y = _detail.y + 22f;

        var e = SelectedEntry();
        if (e == null)
        {
            _small.normal.textColor = TextDim;
            VoxelHud.LabelShadow(new Rect(x, y, w, 24f), "点一格道具看它的详情", _small);
            return;
        }

        Color tint = RarityColor(e.Info.Rarity);

        // 顶上一道星级色带
        VoxelHud.FillRect(new Rect(x, y, w, 3f), tint);
        y += 18f;

        // 有大图就摆一张（没图标就靠下面的名字认）
        if (e.Icon != null)
        {
            float side = Mathf.Min(w * 0.55f, 130f);
            GUI.DrawTexture(new Rect(x + (w - side) * 0.5f, y, side, side), e.Icon, ScaleMode.ScaleToFit);
            y += side + 14f;
        }

        _name.normal.textColor = tint;
        VoxelHud.LabelShadow(new Rect(x, y, w, 30f), e.Name, _name);
        y += 36f;

        _star.normal.textColor = tint;
        VoxelHud.LabelShadow(new Rect(x, y, w, 24f), Stars(e.Info.Rarity), _star);
        y += 30f;

        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(x, y, w, 22f), "分类　" + Tabs[(int)e.Info.Cat], _small);
        y += 24f;
        VoxelHud.LabelShadow(new Rect(x, y, w, 22f), "拥有　×" + e.Count, _small);
        y += 30f;

        VoxelHud.FillRect(new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.14f));
        y += 16f;

        _body.normal.textColor = Text;
        var lines = Wrap(e.Info.Desc, 15);
        for (int i = 0; i < lines.Count; i++)
        {
            VoxelHud.LabelShadow(new Rect(x, y, w, 22f), lines[i], _body);
            y += 24f;
        }
    }

    void DrawFooter()
    {
        _small.normal.textColor = TextDim;
        VoxelHud.LabelShadow(new Rect(_panel.x + 190f, _panel.yMax - 34f, _panel.width - 260f, 22f),
                             "B 关闭背包 · Esc 也行 · 滚轮翻格 · 左键点格子看详情 · 角色与武器养成按 C", _small);
    }

    // ---------- 点击 ----------

    void HandleClicks()
    {
        Event e = Event.current;
        if (e.type != EventType.MouseUp || e.button != 0) return;

        Vector2 m = e.mousePosition;

        if (_close.Contains(m)) { Close(); e.Use(); return; }

        for (int i = 0; i < _tabs.Length; i++)
        {
            if (!_tabs[i].Contains(m)) continue;

            SetCategory((Cat)i);
            VoxelAudio.Click();
            e.Use();
            return;
        }

        int cols = Columns;
        for (int i = 0; i < _items.Count; i++)
        {
            var r = CellRect(i, cols);
            if (r.yMax < _grid.y || r.y > _grid.yMax) continue;     // 卷出视野的格子点不到
            if (!r.Contains(m)) continue;

            _selected = _items[i].Name;
            VoxelAudio.Click();
            e.Use();
            return;
        }
    }

    // ---------- 样式与小工具 ----------

    void EnsureStyles()
    {
        if (_title != null) return;

        _title = new GUIStyle(GUI.skin.label) { fontSize = 24 };
        _title.normal.textColor = new Color(1f, 0.9f, 0.6f);

        _tab = new GUIStyle(GUI.skin.label) { fontSize = 17 };
        _tab.normal.textColor = new Color(1f, 1f, 1f, 0.72f);

        _tabOn = new GUIStyle(GUI.skin.label) { fontSize = 18 };
        _tabOn.normal.textColor = new Color(1f, 0.9f, 0.58f);

        _count = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.LowerRight };
        _count.normal.textColor = TextDim;

        _name = new GUIStyle(GUI.skin.label) { fontSize = 20 };
        _name.normal.textColor = Color.white;

        _star = new GUIStyle(GUI.skin.label) { fontSize = 17 };
        _star.normal.textColor = Gold;

        _body = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        _body.normal.textColor = Text;

        _small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        _small.normal.textColor = TextDim;

        _mark = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
        _mark.normal.textColor = Color.white;

        _center = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        _center.normal.textColor = Color.white;
    }

    static string Stars(int rarity)
    {
        if (rarity >= 5) return "★★★★★";
        if (rarity == 4) return "★★★★";
        if (rarity == 3) return "★★★";
        if (rarity == 2) return "★★";
        return "★";
    }

    /// <summary>星级配色：金 / 紫 / 蓝 / 绿 / 灰（和祈愿那一套一致）</summary>
    static Color RarityColor(int rarity)
    {
        switch (rarity)
        {
            case 5:  return new Color(1f, 0.79f, 0.35f);
            case 4:  return new Color(0.75f, 0.56f, 0.95f);
            case 3:  return new Color(0.42f, 0.65f, 0.93f);
            case 2:  return new Color(0.48f, 0.78f, 0.52f);
            default: return new Color(0.70f, 0.72f, 0.76f);
        }
    }

    /// <summary>描边方框（只画四条边，不填底）</summary>
    static void Frame(Rect r, Color line, float thickness)
    {
        VoxelHud.FillRect(new Rect(r.x, r.y, r.width, thickness), line);
        VoxelHud.FillRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), line);
        VoxelHud.FillRect(new Rect(r.x, r.y, thickness, r.height), line);
        VoxelHud.FillRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), line);
    }

    static Texture2D LoadIcon(string key)
    {
        if (IconCache.TryGetValue(key, out var cached)) return cached;

        var texture = Resources.Load<Texture2D>(key);
        IconCache[key] = texture;        // 确实没有也缓存，免得每帧重复查
        return texture;
    }

    /// <summary>中文没空格，按字数硬折行（IMGUI 的 wordWrap 对 CJK 不靠谱）</summary>
    static List<string> Wrap(string text, int perLine)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        for (int i = 0; i < text.Length; i += perLine)
            lines.Add(text.Substring(i, Mathf.Min(perLine, text.Length - i)));

        return lines;
    }
}
