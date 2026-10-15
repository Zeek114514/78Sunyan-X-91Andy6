using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 用 IMGUI 画准星、物品栏和提示，省得搭 Canvas。
/// 右上角另外挂一路"钱袋"（Andy的币 + 素材），开宝箱时还会弹一张"获得"清单。
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
    GUIStyle _purse;
    GUIStyle _purseTitle;
    GUIStyle _lootTitle;
    GUIStyle _lootLine;
    GUIStyle _regionStyle;
    GUIStyle _achieveTitle, _achieveLine;

    // ---------- 宝箱奖励提示（静态：哪只宝箱开的都往这里塞，由 HUD 统一画） ----------

    const float LootDuration = 5f;      // 清单停留多久
    const float LootFade     = 0.9f;    // 最后这段时间淡出

    static string _lootTitleText;
    static readonly List<string> _lootLines = new List<string>();
    static float _lootTimer;

    /// <summary>开宝箱后弹一张"获得"清单（LootChest 调用）</summary>
    public static void ShowLoot(string title, List<string> lines)
    {
        _lootTitleText = title;

        _lootLines.Clear();
        if (lines != null) _lootLines.AddRange(lines);

        _lootTimer = LootDuration;
    }

    public void Init(VoxelInteraction interaction, VoxelPlayer player, VoxelStats stats)
    {
        _interaction = interaction;
        _player = player;
        _stats = stats;
    }

    void Update()
    {
        if (_lootTimer > 0f) _lootTimer -= Time.deltaTime;

        // 别的系统（神像祝福之类）解锁的成就从这里接手：取走待播报的那一条
        if (_pendingToast != null)
        {
            ShowAchievement(_pendingToast);
            _pendingToast = null;
        }

        TickRegionBanner();
    }

    void OnGUI()
    {
        if (_interaction == null) return;
        EnsureStyles();

        if (Cursor.lockState == CursorLockMode.Locked && !TitleScreen.IsOpen && !VoxelMenu.IsOpen)
        {
            DrawRegionBanner();          // 正上方那条"进入新地区"的横幅，压在准星之上
            DrawAchievement();           // 首次进 Andy的港门 的成就卡片
            DrawCrosshair();
            DrawHotbar();
            DrawVitals();
            DrawStatus();
            DrawPurse();
            DrawLootToast();
        }
        else if (!VoxelChat.IsOpen && !VoxelGacha.IsOpen && !WorldMap.IsOpen && !TitleScreen.IsOpen &&
                 !VoxelMenu.IsOpen)      // 对话框 / 祈愿 / 大地图 / 菜单 / 开幕动画打开时把屏幕让出去
        {
            DrawStartPanel();
        }
    }

    // ================= 进入新地区：正上方亮一下地区名 =================

    const float RegionHold  = 2.8f;      // 名字停多久
    const float RegionFade  = 1.4f;      // 之后淡出用多久

    string _region;
    float  _regionAt = -99f;

    /// <summary>每帧看一眼当前地区名，变了就重新计时（走回旧地区也会再亮一次 —— 和原神一样）</summary>
    void TickRegionBanner()
    {
        if (_player == null) return;

        string now = WorldMap.RegionName(_player.transform.position.x, _player.transform.position.z);
        if (now == _region) return;

        _region = now;
        _regionAt = Time.time;

        TryRegionAchievements(now);

        // 顺带看一眼海拔：站到 150 米以上解锁"一览众山"
        if (_player.transform.position.y >= 150f) UnlockAchievement("high");
    }

    // ================= 成就：解锁 + 弹卡片 =================

    /// <summary>解锁一个成就（第一次才弹提示）。触发点拿到的是 id，名字从成就表里取</summary>
    public static void UnlockAchievement(string id) => UnlockAchievement(id, Achievements.NameOf(id));

    /// <summary>解锁一个成就，卡片上写 <paramref name="label"/>；已经解锁过就什么都不做</summary>
    public static void UnlockAchievement(string id, string label)
    {
        if (!Achievements.Unlock(id)) return;

        _pendingToast = label;
        _pendingAt = Time.time;
    }

    static string _pendingToast;
    static float  _pendingAt;

    /// <summary>进 Andy的港门 / 稻妻 / 雪顶…… 的一次判定（和地区名横幅共用一次"看到新地区"）</summary>
    void TryRegionAchievements(string region)
    {
        if (region == null) return;

        if (region.StartsWith("Andy的港门")) UnlockAchievement("harbor", "Sunyan进入Andy港门！");
        else if (region.StartsWith("稻妻 · 将军府")) UnlockAchievement("inazuma", "Sunyan进入将军府！");
        else if (region.StartsWith("Andy的雪顶")) UnlockAchievement("snow", "Sunyan登上雪线！");
        else if (region.StartsWith("湖心城")) UnlockAchievement("mondstadt", "Sunyan进入湖心城！");
        else if (region == "璃月 · 喀斯特峰林") UnlockAchievement("karst", "Sunyan走进喀斯特峰林！");
    }

    /// <summary>成就提示：屏幕中上方一块金边卡片，停 AchieveHold 秒再淡出</summary>
    void ShowAchievement(string text)
    {
        _achievement = text;
        _achievementAt = Time.time;
        VoxelChat.Announce("解锁成就：" + text);
    }

    const float AchieveHold = 4.0f, AchieveFade = 1.2f;
    string _achievement;
    float  _achievementAt = -99f;

    void DrawAchievement()
    {
        if (string.IsNullOrEmpty(_achievement)) return;

        float age = Time.time - _achievementAt;
        if (age > AchieveHold + AchieveFade) return;

        float a = Mathf.Clamp01(age < AchieveHold ? 1f : 1f - (age - AchieveHold) / AchieveFade);

        float cx = Screen.width * 0.5f;
        float y = Screen.height * 0.13f + 74f;               // 地区名横幅下面一点
        float w = Mathf.Min(460f, Screen.width * 0.62f);
        var card = new Rect(cx - w * 0.5f, y, w, 62f);

        var old = GUI.color;
        GUI.color = new Color(0.06f, 0.07f, 0.10f, a * 0.72f);       // 卡片底
        GUI.DrawTexture(card, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.86f, 0.46f, a * 0.95f);           // 金边
        GUI.DrawTexture(new Rect(card.x, card.y, card.width, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(card.x, card.yMax - 2f, card.width, 2f), Texture2D.whiteTexture);
        GUI.color = old;

        _achieveTitle.normal.textColor = new Color(1f, 0.88f, 0.52f, a);
        LabelShadow(new Rect(card.x, card.y + 6f, card.width, 20f), "成就达成", _achieveTitle);

        _achieveLine.normal.textColor = new Color(1f, 1f, 1f, a);
        LabelShadow(new Rect(card.x, card.y + 28f, card.width, 26f), _achievement, _achieveLine);
    }

    /// <summary>正上方展示地区名：先停 RegionHold 秒，再在 RegionFade 秒里淡出。
    /// 标题底下一条细横线 + 四个小菱角，和原神那种"进新区域"的提示一个意思</summary>
    void DrawRegionBanner()
    {
        if (string.IsNullOrEmpty(_region)) return;

        float age = Time.time - _regionAt;
        if (age > RegionHold + RegionFade) return;

        float a = age < RegionHold ? 1f : 1f - (age - RegionHold) / RegionFade;
        a = Mathf.Clamp01(a);

        float cx = Screen.width * 0.5f;
        float y = Screen.height * 0.12f;                     // 屏幕上方约八分之一处
        float w = Mathf.Min(520f, Screen.width * 0.6f);

        var box = new Rect(cx - w * 0.5f, y - 8f, w, 50f);
        var old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, a * 0.18f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);        // 薄底：亮字压在任何背景上都读得清

        // 上下两条细线（跟着一起淡出）
        GUI.color = new Color(1f, 0.95f, 0.78f, a * 0.7f);
        GUI.DrawTexture(new Rect(cx - w * 0.30f, y + 36f, w * 0.60f, 1.6f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - w * 0.12f, y - 10f, w * 0.24f, 1.2f), Texture2D.whiteTexture);
        GUI.color = old;

        _regionStyle.normal.textColor = new Color(1f, 0.98f, 0.9f, a);
        LabelShadow(new Rect(cx - w * 0.5f, y - 2f, w, 38f), _region, _regionStyle);
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

        // 右上角钱袋 / 开箱清单
        _purseTitle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        _purseTitle.normal.textColor = new Color(1f, 0.88f, 0.55f);
        _purse = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        _purse.normal.textColor = new Color(1f, 1f, 1f, 0.92f);

        _lootTitle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
        _lootTitle.normal.textColor = new Color(1f, 0.88f, 0.55f);
        _lootLine = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        _lootLine.normal.textColor = Color.white;

        // 进入新地区时正上方那条名字 + 成就卡片
        _regionStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter };
        _regionStyle.normal.textColor = Color.white;

        _achieveTitle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        _achieveLine  = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };

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
            line2 = "T 对话框    P 祈愿    M 世界地图    E 开启宝箱    再输入 /fly 关闭飞行";
        }
        else
        {
            line1 = "WASD 移动    Shift 疾跑    空格 跳跃    左键 攻击    Q 丢掉手上的道具";
            line2 = "E 开启宝箱    T 对话框    P 祈愿    M 世界地图    滚轮 换道具    Esc 释放鼠标";
        }

        GUI.Label(new Rect(box.x + 12f, box.y + 8f,  box.width, 24f), line1, _body);
        GUI.Label(new Rect(box.x + 12f, box.y + 30f, box.width, 24f), line2, _body);
    }

    /// <summary>右上角钱袋：Andy的币 + 开宝箱攒下的素材（不占物品栏的格子，单独列在这儿）</summary>
    void DrawPurse()
    {
        if (_interaction == null) return;

        var names = _interaction.MaterialNames;

        const float w = 208f;
        float h = 28f + names.Count * 19f + 6f;
        float x = Screen.width - w - 24f;
        float y = 12f;

        FillRect(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.38f));
        FillRect(new Rect(x, y, 3f, h), new Color(0.93f, 0.76f, 0.35f, 0.85f));

        LabelShadow(new Rect(x + 10f, y + 4f, w - 20f, 20f), "Andy的币  ×" + _interaction.AndyCoinCount, _purseTitle);

        for (int i = 0; i < names.Count; i++)
            LabelShadow(new Rect(x + 16f, y + 26f + i * 19f, w - 26f, 18f),
                        names[i] + "  ×" + _interaction.MaterialCount(names[i]), _purse);
    }

    /// <summary>开箱"获得"清单：右侧滑一条淡出的小面板</summary>
    void DrawLootToast()
    {
        if (_lootTimer <= 0f || _lootTitleText == null) return;

        float a = Mathf.Clamp01(_lootTimer / LootFade);

        const float w = 224f, lineH = 22f;
        float h = 34f + _lootLines.Count * lineH + 8f;
        float x = Screen.width - w - 24f;
        float y = Screen.height * 0.34f;

        FillRect(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.55f * a));
        FillRect(new Rect(x, y, w, 3f), new Color(0.96f, 0.80f, 0.34f, 0.95f * a));

        _lootTitle.normal.textColor = new Color(1f, 0.88f, 0.55f, a);
        _lootLine.normal.textColor = new Color(1f, 1f, 1f, a);

        LabelShadow(new Rect(x + 12f, y + 6f, w - 24f, 22f), _lootTitleText, _lootTitle);

        for (int i = 0; i < _lootLines.Count; i++)
            LabelShadow(new Rect(x + 14f, y + 32f + i * lineH, w - 26f, 20f), "· " + _lootLines[i], _lootLine);
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
        GUI.Label(new Rect(box.x, box.y + 128f, w, 24f), "左键挖 · 右键放 · 数字键/滚轮换方块 · Esc 菜单（O 也行）", _body);
        GUI.Label(new Rect(box.x, box.y + 152f, w, 24f), "T 对话框 · P 祈愿 · M 世界地图 · E 开启宝箱 · 输入 /fly 切换飞行", _body);
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
