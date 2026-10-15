using UnityEngine;

/// <summary>
/// 世界地图系统（原神式）：
///  · 左上角常驻圆形小地图：实时跟着玩家走、指北、显示附近地标与 NPC、显示当前区域名；
///  · 按 M 打开大地图：整张世界俯视图 + 各主要建筑的名字和位置 + 玩家位置 + 图例，再按 M / Esc 关闭。
///
/// 俯视图是启动时用同一套确定性地形函数烘焙出来的（高度 / 水深 / 坡度 / 森林密度，
/// 再加上城墙、街道、民居屋顶、桥和栈桥），所以地图和真实地形是同一份数据，不会"地图和地面不一致"。
/// </summary>
public class WorldMap : MonoBehaviour
{
    // ---------- 参数 ----------

    public const int   TexSize   = 1024;         // 俯视图分辨率（像素）—— 大地图是"看一片区域"，所以烘得细一点
    public const float MiniSrcPx = 240f;         // 小地图采样范围（俯视图像素，约 140 米见方）

    const float MiniR      = 88f;                // 小地图半径（像素）
    const float MiniMargin = 22f;                // 小地图离屏幕左上角多远

    /// <summary>大地图是不是开着（HUD / 输入系统靠它让位）</summary>
    public static bool IsOpen { get; private set; }

    OpenWorld _world;
    Transform _player;

    Texture2D _map;                 // 世界俯视图
    Texture2D _ring;                // 小地图圆环边框
    Texture2D _arrow;               // 玩家箭头

    GUIStyle _label, _small, _title, _hint;
    float _openTime;

    // ---------- 大地图视野 ----------
    // 打开时以角色为中心显示一片区域（不是整张图），按住左键拖拽可以看别的区域，滚轮缩放，右键回到角色。
    const float ViewMin = 140f;     // 拉到最近时屏幕纵向能看到多少米
    const float ViewMax = 2000f;    // 拉到最远时能看全图（地图 1800 米；比它大时下面的代码会把视野居中）
    float   _viewMeters = 260f;     // 当前视野跨度（世界米）
    Vector2 _viewCenter;            // 当前视野中心（世界坐标 XZ）
    float   _pxPerMeter = 3f;       // 上一帧的缩放（像素 / 米），拖拽换算用
    Vector2 _dragMouse;             // 上一帧鼠标位置（算拖拽增量）
    bool    _dragging;
    float   _dragMoved;             // 这一次按下累计拖了多远（小于阈值算"点了一下" → 点锚点传送）
    int     _selIdx = -1;           // 键盘选中的锚点在 TeleportAnchor.All 里的下标（-1 = 没选）

    // ---------- 传送黑场过渡 ----------
    // 落地那一瞬间把屏幕压黑（短暂停留）再淡出：一秒之内跨越几百米不至于"啪"地跳过去。
    const float FadeHold = 0.12f;   // 全黑停留
    const float FadeOut  = 0.5f;    // 淡出时长
    float _fade, _fadeHold;

    public void Init(OpenWorld world, Transform player)
    {
        _world  = world;
        _player = player;
        IsOpen  = false;

        float t0 = Time.realtimeSinceStartup;
        BuildMap();
        _ring  = BuildRingTexture(256, 122f, 3.5f);
        _arrow = BuildArrowTexture(64);

        Debug.Log($"[WorldMap] 世界俯视图烘焙完成：{TexSize}×{TexSize}，世界 {OpenWorld.Size}×{OpenWorld.Size} 米，" +
                  $"耗时 {(Time.realtimeSinceStartup - t0) * 1000f:0} 毫秒；地标 {MondstadtTown.Markers.Count} 个，" +
                  $"色块 {MondstadtTown.Blobs.Count} 个，线 {MondstadtTown.MapLines.Count} 条。");
    }

    void OnDisable() => IsOpen = false;

    void Update()
    {
        // 传送黑场：地图关掉之后还要继续淡出，所以放在最前面（不受下面那些早退影响）
        if (_fade > 0f)
        {
            if (_fadeHold > 0f) _fadeHold -= Time.unscaledDeltaTime;
            else _fade = Mathf.Max(0f, _fade - Time.unscaledDeltaTime / FadeOut);
        }

        if (_map == null || VoxelChat.IsOpen || VoxelGacha.IsOpen || TitleScreen.IsOpen || VoxelMenu.IsOpen) return;

        if (Input.GetKeyDown(KeyCode.M)) Toggle();
        else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Toggle();

        if (IsOpen)
        {
            // 键盘兜底：Tab 在已解锁的锚点之间循环、Enter 传送选中的那个（鼠标点不到图钉时用）
            if (Input.GetKeyDown(KeyCode.Tab)) CycleSelection();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) TeleportSelected();

            // 滚轮缩放：视野跨度越小越近
            float wheel = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.001f)
                _viewMeters = Mathf.Clamp(_viewMeters * (1f - wheel * 0.6f), ViewMin, ViewMax);

            // 按住左键拖动：把鼠标位移换算成世界米，拖着看别的区域
            if (Input.GetMouseButtonDown(0))
            {
                _dragging = true;
                _dragMoved = 0f;
            }

            if (_dragging)
            {
                Vector2 delta = (Vector2)Input.mousePosition - _dragMouse;
                _dragMoved += delta.magnitude;
                _viewCenter -= delta / Mathf.Max(0.001f, _pxPerMeter);
            }

            if (Input.GetMouseButtonUp(0))
            {
                _dragging = false;
                if (_dragMoved < 6f) TryClickAnchor();      // 基本没拖动 = 点了一下 → 试试点传送锚点
            }

            // 右键：视野拉回角色身上
            if (Input.GetMouseButtonDown(1)) CenterOnPlayer();
        }
        else
        {
            _dragging = false;
        }

        _dragMouse = Input.mousePosition;
    }

    /// <summary>把大地图视野对准角色（打开地图和右键都用它）</summary>
    void CenterOnPlayer()
    {
        _viewCenter = new Vector2(_player.position.x, _player.position.z);
    }

    void Toggle()
    {
        IsOpen = !IsOpen;

        if (IsOpen)
        {
            _openTime = Time.unscaledTime;
            _viewMeters = 260f;
            _selIdx = -1;                                // 每次打开都从"没选中"开始，Tab 从第一个已解锁的算起
            CenterOnPlayer();                            // 打开时以角色为中心的一片区域
            _dragMouse = Input.mousePosition;
            Cursor.lockState = CursorLockMode.None;      // 开地图时松开鼠标、角色自然停下（VoxelPlayer 看鼠标锁）
            Cursor.visible = true;
        }
        else
        {
            _dragging = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // ---------- 画界面 ----------

    void OnGUI()
    {
        if (_map == null) return;
        if (TitleScreen.IsOpen) return;                           // 开幕动画期间把屏幕让出去（小地图也不画）
        if (VoxelMenu.IsOpen) return;                             // 菜单是全屏的，小地图别压在它上面
        if (Event.current.type != EventType.Repaint) return;      // 每帧只画一次，别在事件阶段重复画
        EnsureStyles();

        DrawMinimap();
        if (IsOpen) DrawBigMap();

        // 传送黑场：压在所有东西最上面 —— 必须画在这里而不是大地图里，
        // 因为传送的第一件事就是把大地图关掉（画在 DrawBigMap 里就永远看不见）
        if (_fade > 0f)
            VoxelHud.FillRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, _fade));
    }

    /// <summary>左上角圆形小地图：跟人走、指北、显示附近地标与 NPC</summary>
    void DrawMinimap()
    {
        float cx = MiniMargin + MiniR, cy = MiniMargin + MiniR;

        float pxx = _player.position.x / OpenWorld.Size * TexSize;
        float pxy = _player.position.z / OpenWorld.Size * TexSize;
        var src = new Rect(pxx - MiniSrcPx * 0.5f, pxy - MiniSrcPx * 0.5f, MiniSrcPx, MiniSrcPx);

        DrawCircleMap(new Rect(cx - MiniR, cy - MiniR, MiniR * 2f, MiniR * 2f), src);

        // 圆环边框（略微画大一点，正好压在圆的边上）
        GUI.DrawTexture(new Rect(cx - MiniR - 5f, cy - MiniR - 5f, (MiniR + 5f) * 2f, (MiniR + 5f) * 2f), _ring);

        float pxPerMeter = MiniR * 2f / (MiniSrcPx * OpenWorld.Size / TexSize);
        float range = MiniR / pxPerMeter;

        // 附近地标
        for (int i = 0; i < MondstadtTown.Markers.Count; i++)
        {
            var m = MondstadtTown.Markers[i];
            if (Mathf.Abs(m.x - _player.position.x) > range || Mathf.Abs(m.z - _player.position.z) > range) continue;

            Vector2 sp = MiniPoint(cx, cy, m.x, m.z, pxPerMeter);
            if (!InMiniDisc(sp, cx, cy)) continue;
            DrawPin(sp, MarkerColor(m.kind), 9f);
        }

        // 附近 NPC
        for (int i = 0; i < VoxelNpc.All.Count; i++)
        {
            var npc = VoxelNpc.All[i];
            if (npc == null) continue;

            Vector2 sp = MiniPoint(cx, cy, npc.transform.position.x, npc.transform.position.z, pxPerMeter);
            if (!InMiniDisc(sp, cx, cy)) continue;
            DrawPin(sp, new Color(0.98f, 0.86f, 0.35f), 7f);
        }

        // 附近的已解锁锚点（金点：走远了也能一眼看见哪儿有传送点）
        for (int i = 0; i < TeleportAnchor.All.Count; i++)
        {
            var a = TeleportAnchor.All[i];
            if (a == null || !a.Unlocked) continue;

            Vector2 sp = MiniPoint(cx, cy, a.transform.position.x, a.transform.position.z, pxPerMeter);
            if (!InMiniDisc(sp, cx, cy)) continue;
            DrawPin(sp, new Color(1f, 0.92f, 0.55f), 10f);
        }

        // 玩家箭头（永远在正中间，按朝向旋转）
        DrawArrow(cx, cy, 26f, _player.eulerAngles.y, Color.white);

        // 指北 + 区域名
        VoxelHud.LabelShadow(new Rect(cx - 20f, cy - MiniR + 1f, 40f, 20f), "N", _small);

        VoxelHud.FillRect(new Rect(cx - 92f, cy + MiniR + 8f, 184f, 24f), new Color(0f, 0f, 0f, 0.45f));
        VoxelHud.LabelShadow(new Rect(cx - 92f, cy + MiniR + 9f, 184f, 22f),
                             RegionName(_player.position.x, _player.position.z), _small);
    }

    /// <summary>大地图：整张世界图 + 主要建筑的名字与位置 + 玩家位置 + 图例。
    /// 滚轮缩放、按住左键拖拽看别的区域、右键把视野拉回角色。</summary>
    void DrawBigMap()
    {
        VoxelHud.FillRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.04f, 0.06f, 0.10f, 0.86f));

        // 面板是屏幕中间一块方区；显示范围由 _viewMeters 决定（以角色为中心的一片，不是整张图）
        var panel = BigPanel();

        // 视野中心夹在地图内（视野比整张地图还大时直接居中）
        float halfView = _viewMeters * 0.5f;
        if (_viewMeters >= OpenWorld.Size)
        {
            _viewCenter = new Vector2(OpenWorld.Size * 0.5f, OpenWorld.Size * 0.5f);
        }
        else
        {
            _viewCenter.x = Mathf.Clamp(_viewCenter.x, halfView, OpenWorld.Size - halfView);
            _viewCenter.y = Mathf.Clamp(_viewCenter.y, halfView, OpenWorld.Size - halfView);
        }

        float pxPerMeter = panel.width / _viewMeters;
        _pxPerMeter = pxPerMeter;                    // 拖拽换算要用

        // 从俯视图里裁出当前视野那一片画上去（比把整张图缩放清晰得多）
        float u = (_viewCenter.x - halfView) / OpenWorld.Size;
        float v = (_viewCenter.y - halfView) / OpenWorld.Size;
        float w = _viewMeters / OpenWorld.Size;
        GUI.DrawTextureWithTexCoords(panel, _map, new Rect(u, v, w, w));

        var gold = new Color(0.85f, 0.78f, 0.55f, 0.85f);
        VoxelHud.FillRect(new Rect(panel.x - 3f, panel.y - 3f, panel.width + 6f, 3f), gold);
        VoxelHud.FillRect(new Rect(panel.x - 3f, panel.y + panel.height, panel.width + 6f, 3f), gold);
        VoxelHud.FillRect(new Rect(panel.x - 3f, panel.y, 3f, panel.height), gold);
        VoxelHud.FillRect(new Rect(panel.x + panel.width, panel.y, 3f, panel.height), gold);

        // 地标：只画在视野里的（图标 + 名字）
        for (int i = 0; i < MondstadtTown.Markers.Count; i++)
        {
            var m = MondstadtTown.Markers[i];
            if (Mathf.Abs(m.x - _viewCenter.x) > halfView || Mathf.Abs(m.z - _viewCenter.y) > halfView) continue;

            Vector2 sp = MapPoint(panel, pxPerMeter, m.x, m.z);
            DrawPin(sp, MarkerColor(m.kind), 13f);
            VoxelHud.LabelShadow(new Rect(sp.x + 11f, sp.y - 10f, 160f, 20f), m.name, _label);
        }

        // 传送锚点：已解锁的金色可点、未点亮的灰色（都在视野里才画）——
        // 画在地标之后，图钉压在建筑标记上面
        for (int i = 0; i < TeleportAnchor.All.Count; i++)
        {
            var a = TeleportAnchor.All[i];
            if (a == null) continue;

            Vector3 ap = a.transform.position;
            if (Mathf.Abs(ap.x - _viewCenter.x) > halfView || Mathf.Abs(ap.z - _viewCenter.y) > halfView) continue;

            Vector2 sp = MapPoint(panel, pxPerMeter, ap.x, ap.z);

            bool selected = i == _selIdx;
            bool statue = a.IsStatue;
            Color pin = statue ? new Color(0.86f, 0.95f, 1f)
                      : a.Unlocked ? new Color(1f, 0.92f, 0.55f)
                      : new Color(0.52f, 0.58f, 0.62f);
            float size = statue ? 22f
                       : a.Unlocked ? (a.Major ? 17f : 13f)
                       : (a.Major ? 12f : 10f);
            DrawPin(sp, selected ? new Color(1f, 1f, 0.85f) : pin, selected ? size + 3f : size);
            if (statue) DrawCircleOutline(sp, size * 0.78f, new Color(0.78f, 0.92f, 1f, 0.9f));   // 神像外面再描一圈
            if (selected) DrawCircleOutline(sp, 16f, new Color(1f, 0.98f, 0.72f, 0.95f));

            // 名字：七天神像一定写；关键锚点（城镇 / 地标 / 村口）和已解锁的也写；
            // 普通锚点要放大到 220 米以内才写（几十个普通锚点全写名字会糊成一片）
            bool showName = statue || a.Major || a.Unlocked || selected || _viewMeters <= 220f;
            if (showName)
                VoxelHud.LabelShadow(new Rect(sp.x + 12f, sp.y - 10f, 200f, 20f),
                                     statue ? a.SiteName
                                            : a.Unlocked ? a.SiteName : a.SiteName + "（未点亮）",
                                     statue || a.Major || a.Unlocked ? _label : _small);
        }

        // NPC 小点（同样只画视野内的）
        for (int i = 0; i < VoxelNpc.All.Count; i++)
        {
            var npc = VoxelNpc.All[i];
            if (npc == null) continue;
            Vector3 p = npc.transform.position;
            if (Mathf.Abs(p.x - _viewCenter.x) > halfView || Mathf.Abs(p.z - _viewCenter.y) > halfView) continue;

            DrawPin(MapPoint(panel, pxPerMeter, p.x, p.z), new Color(0.98f, 0.86f, 0.35f), 8f);
        }

        // 玩家：脉冲圈 + 箭头（拖远了也能一眼看到自己在哪一侧）
        Vector2 pp = MapPoint(panel, pxPerMeter, _player.position.x, _player.position.z);
        float pulse = 15f + Mathf.Sin((Time.unscaledTime - _openTime) * 3.4f) * 4f;
        DrawCircleOutline(pp, pulse, new Color(1f, 1f, 1f, 0.55f));
        DrawArrow(pp.x, pp.y, 26f, _player.eulerAngles.y, Color.white);

        // 标题 / 坐标 / 图例：固定在屏幕上，拖地图时不会跟着跑出去
        VoxelHud.LabelShadow(new Rect(0f, 8f, Screen.width, 30f), "蒙德 · 璃月 · 世界地图", _title);
        VoxelHud.LabelShadow(new Rect(0f, 38f, Screen.width, 22f),
                             $"当前位置 {RegionName(_player.position.x, _player.position.z)}    " +
                             $"X {_player.position.x:0}  Z {_player.position.z:0}    " +
                             $"视野 {_viewMeters:0} 米    视野中心 ({_viewCenter.x:0}, {_viewCenter.y:0})",
                             _small);

        VoxelHud.LabelShadow(new Rect(0f, Screen.height - 54f, Screen.width, 22f),
                             "红顶 = 民居    金色 = 城门 / 广场    红色 = 丘丘人营地    白蓝 = Andy的雪顶    黄点 = NPC    " +
                             "金色大图钉 = 已解锁传送锚点（灰的还没点亮）    " +
                             "浅蓝大图钉 = 七天神像（走近回满生命与体力，也能传送）",
                             _small);
        VoxelHud.LabelShadow(new Rect(0f, Screen.height - 30f, Screen.width, 22f),
                             "按住左键拖拽 查看其他区域 · 滚轮 缩放 · 右键 回到角色 · 点金色锚点 传送 · " +
                             $"Tab 选下一个锚点 / Enter 传送它（已解锁 {TeleportAnchor.UnlockedCount}/{TeleportAnchor.All.Count} 处）· M / Esc 关闭",
                             _hint);
    }

    Vector2 MapPoint(Rect panel, float pxPerMeter, float wx, float wz) => new Vector2(
        panel.center.x + (wx - _viewCenter.x) * pxPerMeter,
        panel.center.y - (wz - _viewCenter.y) * pxPerMeter);     // 北在上

    /// <summary>大地图面板的屏幕矩形。画图和"点锚点"必须用同一份，所以抽出来</summary>
    static Rect BigPanel()
    {
        float side = Mathf.Min(Screen.width, Screen.height) * 0.86f;
        return new Rect((Screen.width - side) * 0.5f, (Screen.height - side) * 0.5f + 14f, side, side);
    }

    // ---------- 点锚点传送 ----------

    /// <summary>在大地图上点一下：命中已解锁的传送锚点就传送过去。
    /// 未点亮的锚点不给传（和原神一样，得先走过去点亮）</summary>
    void TryClickAnchor()
    {
        var panel = BigPanel();

        Vector2 mouse = Input.mousePosition;
        mouse.y = Screen.height - mouse.y;                       // Input 是左下原点，GUI 是左上原点
        if (!panel.Contains(mouse)) return;

        float pxPerMeter = panel.width / Mathf.Max(0.001f, _viewMeters);

        TeleportAnchor best = null;
        int bestIdx = -1;
        float bestDist = 18f;                                    // 图钉的点击半径（像素）

        for (int i = 0; i < TeleportAnchor.All.Count; i++)
        {
            var a = TeleportAnchor.All[i];
            if (a == null || !a.Unlocked) continue;

            Vector3 ap = a.transform.position;
            float d = (MapPoint(panel, pxPerMeter, ap.x, ap.z) - mouse).magnitude;
            if (d < bestDist) { bestDist = d; bestIdx = i; best = a; }
        }

        if (best == null) return;

        _selIdx = bestIdx;                                       // 点中的那个也变成"键盘选中的"，高亮跟着走
        TeleportTo(best);
    }

    /// <summary>Tab：在已解锁的锚点之间循环选中；视野跟着挪过去，保证选中的那个一定看得见</summary>
    void CycleSelection()
    {
        if (TeleportAnchor.All.Count == 0) return;

        int start = _selIdx < 0 ? 0 : _selIdx + 1;
        for (int k = 0; k < TeleportAnchor.All.Count; k++)
        {
            int i = (start + k) % TeleportAnchor.All.Count;
            var a = TeleportAnchor.All[i];
            if (a == null || !a.Unlocked) continue;

            _selIdx = i;
            _viewCenter = new Vector2(a.transform.position.x, a.transform.position.z);
            return;
        }

        _selIdx = -1;                                            // 一个都还没解锁
    }

    /// <summary>Enter：传送去键盘选中的那个锚点</summary>
    void TeleportSelected()
    {
        if (_selIdx < 0 || _selIdx >= TeleportAnchor.All.Count) return;

        var a = TeleportAnchor.All[_selIdx];
        if (a != null && a.Unlocked) TeleportTo(a);
    }

    /// <summary>传送到某处锚点：先把地图关掉（顺带把鼠标锁回去），再挪人。
    /// 落地后面朝该去的地方（村口朝村里、码头朝海湾），并压一段黑场 —— 跨几百米的瞬移不至于太生硬</summary>
    void TeleportTo(TeleportAnchor anchor)
    {
        if (IsOpen) Toggle();
        else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        _selIdx = -1;

        var player = _player.GetComponent<VoxelPlayer>();
        if (player != null)
        {
            player.Teleport(anchor.LandPos + Vector3.up * 0.25f);
            player.SetYaw(anchor.FaceYaw);
        }
        else
        {
            _player.position = anchor.LandPos;
        }

        _fade = 1f;
        _fadeHold = FadeHold;

        VoxelChat.Announce($"传送至 {anchor.SiteName}");
    }

    // ---------- 小地图圆形裁剪 ----------

    /// <summary>把俯视图的一块区域画成圆：逐条横带裁剪 + 按 UV 采样，边缘平滑，不需要额外材质</summary>
    void DrawCircleMap(Rect dest, Rect src)
    {
        const float slice = 4f;
        float R = dest.width * 0.5f;
        float scale = dest.height / src.height;

        for (float y = 0f; y < dest.height; y += slice)
        {
            float dh = Mathf.Min(slice, dest.height - y);
            float dy = y + dh * 0.5f - R;
            float half = Mathf.Sqrt(Mathf.Max(0f, R * R - dy * dy));
            if (half <= 0.7f) continue;

            var d = new Rect(dest.x + R - half, dest.y + y, half * 2f, dh);

            float u0 = (src.x + (R - half) / scale) / TexSize;
            float uw = (half * 2f / scale) / TexSize;
            float v0 = (src.y + src.height - (y + dh) / scale) / TexSize;   // 屏幕从上往下 ↔ 贴图从北往南
            float vh = (dh / scale) / TexSize;

            GUI.DrawTextureWithTexCoords(d, _map, new Rect(u0, v0, uw, vh));
        }
    }

    /// <summary>世界坐标 → 小地图屏幕坐标（以玩家为中心，北在上）</summary>
    Vector2 MiniPoint(float cx, float cy, float wx, float wz, float pxPerMeter)
    {
        float dx = wx - _player.position.x;
        float dz = wz - _player.position.z;
        return new Vector2(cx + dx * pxPerMeter, cy - dz * pxPerMeter);
    }

    static bool InMiniDisc(Vector2 p, float cx, float cy) =>
        (p.x - cx) * (p.x - cx) + (p.y - cy) * (p.y - cy) <= (MiniR - 4f) * (MiniR - 4f);

    static Color MarkerColor(byte kind)
    {
        switch (kind)
        {
            case 0:  return new Color(0.95f, 0.80f, 0.35f);   // 城门
            case 1:  return new Color(0.86f, 0.82f, 0.70f);   // 桥
            case 2:  return new Color(1.00f, 0.92f, 0.62f);   // 大教堂
            case 3:  return new Color(0.90f, 0.65f, 0.32f);   // 风车
            case 4:  return new Color(0.60f, 0.88f, 1.00f);   // 广场
            case 5:  return new Color(0.95f, 0.58f, 0.38f);   // 酒馆
            case 6:  return new Color(0.78f, 0.52f, 0.32f);   // 铁匠铺
            case 7:  return new Color(0.72f, 0.76f, 0.84f);   // 瞭望塔
            case 9:  return new Color(0.92f, 0.42f, 0.36f);   // 丘丘人营地（红色 = 有怪）
            case 10: return new Color(0.86f, 0.90f, 0.96f);   // 雪山前哨营地
            case 11: return new Color(1.00f, 1.00f, 1.00f);   // 龙脊雪山主峰
            case 12: return new Color(0.66f, 0.88f, 0.98f);   // 冰湖
            case 13: return new Color(0.72f, 0.98f, 0.86f);   // 测试平台（100 个 NPC）
            // 14~23：野外建筑（村落 / 设施 / 遗迹），编号和 WildBuildings.MarkerKind 对齐
            case 14: return new Color(0.98f, 0.86f, 0.52f);   // 野外村落
            case 15: return new Color(0.94f, 0.72f, 0.40f);   // 瞭望塔
            case 16: return new Color(0.92f, 0.78f, 0.44f);   // 风车
            case 17: return new Color(0.72f, 0.80f, 0.58f);   // 水车磨坊
            case 18: return new Color(0.90f, 0.66f, 0.44f);   // 木桥
            case 19: return new Color(0.80f, 0.66f, 0.46f);   // 猎人小屋
            case 20: return new Color(0.84f, 0.70f, 0.48f);   // 伐木场
            case 21: return new Color(0.86f, 0.80f, 0.62f);   // 篝火休息点
            case 22: return new Color(0.78f, 0.80f, 0.86f);   // 遗迹（断柱石阵 / 残墙）
            case 23: return new Color(0.70f, 0.94f, 0.86f);   // 风神祭坛
            case 24: return new Color(0.88f, 0.72f, 0.99f);   // 解密场地（元素方碑）
            // 30~40：璃月野外建筑，编号和 LiyueBuildings.MarkerKind 对齐（30 + LiyueKind）
            case 30: return new Color(0.90f, 0.86f, 0.78f);   // 璃月村落（白墙黛瓦）
            case 31: return new Color(0.94f, 0.42f, 0.34f);   // 楼阁（朱红）
            case 32: return new Color(0.98f, 0.78f, 0.36f);   // 牌坊（金）
            case 33: return new Color(0.72f, 0.62f, 0.46f);   // 吊脚楼（木）
            case 34: return new Color(0.86f, 0.86f, 0.84f);   // 石拱桥
            case 35: return new Color(0.82f, 0.90f, 0.66f);   // 茶棚（竹）
            case 36: return new Color(0.60f, 0.74f, 0.92f);   // 水车（水）
            case 37: return new Color(0.96f, 0.84f, 0.52f);   // 岩神像（岩金）
            case 38: return new Color(0.74f, 0.72f, 0.68f);   // 石林遗迹（石灰岩）
            case 39: return new Color(0.78f, 0.88f, 0.52f);   // 梯田农舍（水田绿）
            case 40: return new Color(0.66f, 0.80f, 0.90f);   // 码头
            // 41~44：璃月港（阶段 E，见 LiyueHarborCity.HarborMarkers）
            case 41: return new Color(1.00f, 0.94f, 0.70f);   // 灯塔（灯）
            case 42: return new Color(0.92f, 0.62f, 0.34f);   // 钟楼（铜钟）
            case 43: return new Color(0.80f, 0.84f, 0.88f);   // 主码头
            case 44: return new Color(0.96f, 0.86f, 0.62f);   // 上城
            // 45~46：璃月解密场地（阶段 F，见 LiyuePuzzleSpawner.MarkerKind）
            case 45: return new Color(0.99f, 0.78f, 0.44f);   // 石灯阵（灯火）
            case 46: return new Color(0.90f, 0.62f, 0.50f);   // 石鼓
            default: return new Color(0.55f, 0.78f, 0.90f);   // 码头
        }
    }

    /// <summary>地图上的小图标：深色描边 + 彩色方块 + 中心亮点</summary>
    static void DrawPin(Vector2 p, Color color, float size)
    {
        float h = size * 0.5f;
        VoxelHud.FillRect(new Rect(p.x - h - 1.5f, p.y - h - 1.5f, size + 3f, size + 3f), new Color(0f, 0f, 0f, 0.72f));
        VoxelHud.FillRect(new Rect(p.x - h, p.y - h, size, size), color);
        VoxelHud.FillRect(new Rect(p.x - h * 0.35f, p.y - h * 0.35f, size * 0.35f, size * 0.35f), new Color(1f, 1f, 1f, 0.75f));
    }

    static void DrawCircleOutline(Vector2 center, float radius, Color color)
    {
        const int steps = 40;
        for (int i = 0; i < steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps;
            VoxelHud.FillRect(new Rect(center.x + Mathf.Cos(a) * radius - 1.5f,
                                       center.y + Mathf.Sin(a) * radius - 1.5f, 3f, 3f), color);
        }
    }

    /// <summary>玩家箭头（贴图 + 绕中心旋转；箭头贴图默认朝上 = 朝北）</summary>
    void DrawArrow(float cx, float cy, float size, float yaw, Color tint)
    {
        var m = GUI.matrix;
        GUIUtility.RotateAroundPivot(yaw, new Vector2(cx, cy));   // GUI 的 y 轴朝下，正角度 = 顺时针，正好对应 yaw
        Color old = GUI.color;
        GUI.color = tint;
        GUI.DrawTexture(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), _arrow);
        GUI.color = old;
        GUI.matrix = m;
    }

    void EnsureStyles()
    {
        if (_label != null) return;

        _label = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
        _small = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
        _title = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
        _hint  = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };

        _label.normal.textColor = Color.white;
        _small.normal.textColor = Color.white;
        _title.normal.textColor = Color.white;
        _hint.normal.textColor  = new Color(1f, 1f, 1f, 0.75f);
    }

    /// <summary>当前区域名（小地图 / 大地图都显示）</summary>
    public static string RegionName(float x, float z)
    {
        // ---------- 稻妻：东南海上的群岛，先报它（不然全被"外海 / 丘陵"抢走） ----------
        if (InazumaIsles.RegionWeight(x, z) > 0.15f)
        {
            if (Vector2.Distance(new Vector2(x, z), InazumaIsles.CastlePos) < 92f) return "稻妻 · 将军府";
            if (Vector2.Distance(new Vector2(x, z), InazumaIsles.PortPos) < 62f) return "稻妻 · 港口";
            if (Vector2.Distance(new Vector2(x, z), InazumaIsles.ShrineIsle) < 78f) return "稻妻 · 神社离岛";
            if (Vector2.Distance(new Vector2(x, z), InazumaIsles.SpireIsle) < 66f) return "稻妻 · 岩柱离岛";
            if (OpenWorld.IsInazumaLand(x, z)) return "稻妻 · 鸣神岛";
            return "稻妻 · 外海";
        }

        // ---------- 旧大陆之外的深海：不管离稻妻多远，这一圈都是海 ----------
        if (Mathf.Max(x, z) > 1430f && OpenWorld.Height(x, z) < OpenWorld.WaterLevel + 0.5f)
            return "外海";

        // ---------- 璃月港：人工台地 + 双湾，先报它（不然会被下面的"峰林 / 丘陵"抢走） ----------
        float harbor = Vector2.Distance(new Vector2(x, z), new Vector2(LiyueHarbor.HarborX, LiyueHarbor.HarborZ));
        if (harbor < LiyueHarbor.QuayR) return "Andy的港门 · 港内";
        if (harbor < LiyueHarbor.QuayR + LiyueHarbor.QuayFalloff + 20f) return "Andy的港门 · 城外码头";

        if (Vector2.Distance(new Vector2(x, z), new Vector2(LiyueHarbor.InnerBayX, LiyueHarbor.InnerBayZ))
            < LiyueHarbor.InnerBayR) return "Andy的港门 · 内湾";
        if (Vector2.Distance(new Vector2(x, z), new Vector2(LiyueHarbor.BayX, LiyueHarbor.BayZ))
            < LiyueHarbor.BayR) return "璃月 · 外海湾";

        // ---------- 蒙德 ----------
        float d = Vector2.Distance(new Vector2(x, z), new Vector2(MondstadtTown.CenterX, MondstadtTown.CenterZ));
        if (d < MondstadtTown.WallR) return "湖心城 · 城内";
        if (d < MondstadtTown.IslandR) return "湖心城 · 城墙外";
        if (d < MondstadtTown.MoatOuter + 6f) return "湖心城 · 护城河道";

        if (Vector2.Distance(new Vector2(x, z), new Vector2(MondstadtTown.CathedralX, MondstadtTown.CathedralZ))
            < MondstadtTown.CathedralR) return "大教堂高台";

        // 「Andy的雪顶」：按高度分层报区域名
        if (OpenWorld.IsFrozenLake(x, z)) return "Andy的雪顶 · 冰湖";
        float snowH = OpenWorld.Height(x, z);
        if (snowH > OpenWorld.SnowLine + 70f) return "Andy的雪顶 · 雪线以上";
        if (OpenWorld.SnowFactor(x, z) > 0.12f) return "Andy的雪顶 · 山腰";

        // ---------- 剩下的按国度分：分界带（权重 0.35~0.65）两边都不算 ----------
        float lw = OpenWorld.LiyueWeight(x, z);
        if (lw > 0.65f)
        {
            if (OpenWorld.TerraceWeight(x, z) > 0.3f) return "璃月 · 梯田";
            if (OpenWorld.InKarstRegion(x, z)) return "璃月 · 喀斯特峰林";
            if (OpenWorld.ForestDensity(x, z) > 0.62f) return "璃月 · 竹林";
            return "璃月 · 丘陵";
        }
        if (lw < 0.35f)
        {
            if (z < MondstadtTown.SouthBridgeZ - 6f) return "南岸草原";
            if (x < 120f) return "西岸湖区";
            return "湖畔草原";
        }

        return "雪顶山麓 · 分界带";
    }

    // ---------- 烘焙世界俯视图 ----------

    void BuildMap()
    {
        int n = TexSize + 1;
        var hf = new float[n * n];

        for (int iy = 0; iy < n; iy++)
        {
            float z = (float)iy / TexSize * OpenWorld.Size;
            for (int ix = 0; ix < n; ix++)
                hf[iy * n + ix] = OpenWorld.Height((float)ix / TexSize * OpenWorld.Size, z);
        }

        var px = new Color32[TexSize * TexSize];
        float cell = OpenWorld.Size / TexSize;
        float water = OpenWorld.WaterLevel;

        Color32 deep    = new Color32(0x3c, 0x86, 0xbe, 255);
        Color32 shallow = new Color32(0x6c, 0xc4, 0xe4, 255);
        Color32 sand    = new Color32(0xf0, 0xe4, 0xba, 255);
        Color32 grass   = new Color32(0x9d, 0xd9, 0x6d, 255);
        Color32 forest  = new Color32(0x71, 0xbe, 0x51, 255);
        Color32 rock    = new Color32(0xc4, 0xbe, 0xaa, 255);
        Color32 peak    = new Color32(0xdf, 0xe4, 0xe6, 255);
        Color32 pave    = new Color32(0xcf, 0xc7, 0xb2, 255);
        Color32 snow    = new Color32(0xf6, 0xfa, 0xff, 255);   // 龙脊雪山：雪
        Color32 ice     = new Color32(0xc6, 0xe6, 0xf4, 255);   // 冰湖
        Color32 scree   = new Color32(0xb4, 0xb0, 0xa8, 255);   // 雪线附近的碎石坡

        for (int iy = 0; iy < TexSize; iy++)
        {
            float z = (iy + 0.5f) / TexSize * OpenWorld.Size;

            for (int ix = 0; ix < TexSize; ix++)
            {
                float x = (ix + 0.5f) / TexSize * OpenWorld.Size;

                float h  = hf[iy * n + ix];
                float hl = hf[iy * n + Mathf.Max(0, ix - 1)];
                float hr = hf[iy * n + Mathf.Min(TexSize, ix + 1)];
                float hd = hf[Mathf.Max(0, iy - 1) * n + ix];
                float hu = hf[Mathf.Min(TexSize, iy + 1) * n + ix];
                float steep = 1f - new Vector3(hl - hr, 2f * cell, hd - hu).normalized.y;

                Color32 c;
                if (h < water - 1.6f)      c = deep;
                else if (h < water)        c = shallow;
                else if (OpenWorld.IsFrozenLake(x, z)) c = ice;                       // 冰湖（冻住的湖面）
                else if (h > OpenWorld.SnowLine + 14f) c = snow;                       // 雪线以上：大片白
                else if (h > OpenWorld.SnowLine - 16f)                                 // 雪线过渡：碎石夹雪
                    c = Lerp32(scree, snow, Mathf.SmoothStep(0f, 1f,
                              Mathf.InverseLerp(OpenWorld.SnowLine - 16f, OpenWorld.SnowLine + 14f, h)));
                else if (h < water + 0.8f) c = sand;
                else if (steep > 0.34f)    c = rock;
                else if (h > 24f)          c = peak;
                else                       c = Lerp32(grass, forest,
                                                      Mathf.SmoothStep(0.42f, 0.85f, OpenWorld.ForestDensity(x, z)));

                if (MondstadtTown.OnTownStreet(x, z)) c = pave;
                else if (OpenWorld.IsRoad(x, z))      c = Lerp32(pave, sand, 0.35f);

                px[iy * TexSize + ix] = c;
            }
        }

        float pxPerMeter = TexSize / OpenWorld.Size;

        // 城墙（一圈石色）+ 塔楼
        for (int i = 0; i < 720; i++)
        {
            float a = i * Mathf.PI * 2f / 720f;
            PaintDisc(px, MondstadtTown.CenterX + Mathf.Cos(a) * MondstadtTown.WallR,
                          MondstadtTown.CenterZ + Mathf.Sin(a) * MondstadtTown.WallR,
                          MondstadtTown.WallT * 0.8f, new Color32(0xe0, 0xd8, 0xc4, 255), pxPerMeter);
        }
        for (int i = 0; i < MondstadtTown.WallSides; i++)
        {
            float a = (i * 360f / MondstadtTown.WallSides + 180f / MondstadtTown.WallSides) * Mathf.Deg2Rad;
            PaintDisc(px, MondstadtTown.CenterX + Mathf.Cos(a) * MondstadtTown.WallR,
                          MondstadtTown.CenterZ + Mathf.Sin(a) * MondstadtTown.WallR,
                          4.6f, new Color32(0xc9, 0xc0, 0xa8, 255), pxPerMeter);
        }

        // 桥 / 栈桥
        for (int i = 0; i < MondstadtTown.MapLines.Count; i++)
        {
            var l = MondstadtTown.MapLines[i];
            PaintLine(px, l.x0, l.z0, l.x1, l.z1, l.kind == 0 ? 6.5f : 5.0f,
                      l.kind == 0 ? new Color32(0xd8, 0xcf, 0xb6, 255) : new Color32(0x9a, 0x6b, 0x44, 255),
                      pxPerMeter);
        }

        // 城里那些屋顶色块（民居 / 地标 / 广场 / 风车）
        for (int i = 0; i < MondstadtTown.Blobs.Count; i++)
        {
            var b = MondstadtTown.Blobs[i];
            Color32 col = b.color == 0 ? new Color32(0xc2, 0x55, 0x3c, 255)
                        : b.color == 2 ? new Color32(0xa9, 0x7d, 0x50, 255)
                                       : new Color32(0xdd, 0xd5, 0xc0, 255);
            PaintDisc(px, b.x, b.z, b.r * 0.8f, col, pxPerMeter);
        }

        _map = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false) { name = "WorldMapTexture" };
        _map.filterMode = FilterMode.Bilinear;
        _map.wrapMode = TextureWrapMode.Clamp;
        _map.SetPixels32(px);
        _map.Apply(false);
    }

    static void PaintDisc(Color32[] px, float wx, float wz, float radiusMeters, Color32 color, float pxPerMeter)
    {
        float cx = wx / OpenWorld.Size * TexSize, cy = wz / OpenWorld.Size * TexSize;
        float r = Mathf.Max(1f, radiusMeters * pxPerMeter);
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(TexSize - 1, Mathf.CeilToInt(cx + r));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(TexSize - 1, Mathf.CeilToInt(cy + r));

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                if (dx * dx + dy * dy <= r * r) px[y * TexSize + x] = color;
            }
        }
    }

    static void PaintLine(Color32[] px, float x0, float z0, float x1, float z1, float width, Color32 color, float pxPerMeter)
    {
        float dx = x1 - x0, dz = z1 - z0;
        float len = Mathf.Sqrt(dx * dx + dz * dz);
        int steps = Mathf.Max(2, Mathf.CeilToInt(len * pxPerMeter * 0.7f));

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            PaintDisc(px, x0 + dx * t, z0 + dz * t, width * 0.5f, color, pxPerMeter);
        }
    }

    static Color32 Lerp32(Color32 a, Color32 b, float t)
    {
        t = Mathf.Clamp01(t);
        return new Color32((byte)(a.r + (b.r - a.r) * t),
                           (byte)(a.g + (b.g - a.g) * t),
                           (byte)(a.b + (b.b - a.b) * t), 255);
    }

    // ---------- 程序化贴图：圆环 / 箭头 ----------

    static Texture2D BuildRingTexture(int size, float ringR, float thickness)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "MapRing" };
        var px = new Color32[size * size];
        float c = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - ringR) / thickness);   // 金色圆环
                float vig = Mathf.Clamp01((d - ringR + 16f) / 16f) * 0.28f;          // 靠边压一点暗
                byte a = (byte)(Mathf.Max(ring * 0.95f, vig) * 255f);
                px[y * size + x] = new Color32(0xf2, 0xe2, 0xa8, a);
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false);
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    static Texture2D BuildArrowTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "MapArrow" };
        var px = new Color32[size * size];
        float s = size;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 朝上的三角箭头 + 外圈深色描边
                float nx = (x + 0.5f) / s, ny = (y + 0.5f) / s;
                bool inside = ny > 0.12f && ny < 0.90f && Mathf.Abs(nx - 0.5f) < (0.90f - ny) * 0.52f;
                bool border = ny > 0.08f && ny < 0.94f && Mathf.Abs(nx - 0.5f) < (0.94f - ny) * 0.57f;

                if (inside)      px[y * size + x] = new Color32(0xff, 0xff, 0xff, 255);
                else if (border) px[y * size + x] = new Color32(0x1c, 0x22, 0x2e, 235);
                else             px[y * size + x] = new Color32(0, 0, 0, 0);
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false);
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }
}
