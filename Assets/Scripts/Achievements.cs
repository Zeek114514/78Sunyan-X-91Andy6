using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 成就系统：一张固定的成就表 + 解锁状态（存 PlayerPrefs，重进游戏还在）。
///
/// 解锁条件目前是三类"看一眼就知道"的：
///   · 走到某个地区（和地区名横幅共用一次判定）
///   · 站到某个高度
///   · 在神像前恢复过几次
/// 都不需要给别的系统加钩子。以后要加新成就：在 <see cref="Load"/> 里加一条，
/// 再到触发点调 <see cref="Unlock"/>，菜单的成就页会自动列出来。
/// </summary>
public static class Achievements
{
    public struct Entry
    {
        public string id;
        public string name;
        public string desc;
        public bool   unlocked;
    }

    /// <summary>全部成就（顺序就是菜单里的顺序）</summary>
    public static readonly List<Entry> All = new List<Entry>();

    const string SaveKey    = "CodelyDemo.Ach.";        // + id
    const string BlessKey   = "CodelyDemo.BlessCount";  // 神像祝福的次数

    static bool _loaded;

    static void Need(string id, string name, string desc) =>
        All.Add(new Entry { id = id, name = name, desc = desc });

    /// <summary>建表 + 读存档（幂等，第一次问的时候执行一次）</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        All.Clear();
        Need("harbor",    "Andy的港门",   "首次进入 Andy的港门");
        Need("inazuma",   "将军府",       "首次进入稻妻的将军府");
        Need("snow",      "Andy的雪顶",   "首次抵达雪顶（雪线以上）");
        Need("mondstadt", "湖心城",       "首次进入蒙德城");
        Need("karst",     "喀斯特峰林",   "首次进入璃月的峰林");
        Need("high",      "一览众山",     "站到海拔 150 米以上");
        Need("blessing",  "七天神像",     "在七天神像前恢复过三次");

        for (int i = 0; i < All.Count; i++)
        {
            var e = All[i];
            e.unlocked = PlayerPrefs.GetInt(SaveKey + e.id, 0) != 0;
            All[i] = e;
        }
    }

    public static int Count
    {
        get { Load(); return All.Count; }
    }

    public static int UnlockedCount
    {
        get
        {
            Load();
            int n = 0;
            for (int i = 0; i < All.Count; i++) if (All[i].unlocked) n++;
            return n;
        }
    }

    public static bool IsUnlocked(string id)
    {
        Load();
        for (int i = 0; i < All.Count; i++) if (All[i].id == id) return All[i].unlocked;
        return false;
    }

    public static string NameOf(string id)
    {
        Load();
        for (int i = 0; i < All.Count; i++) if (All[i].id == id) return All[i].name;
        return id;
    }

    /// <summary>解锁一个成就；第一次解锁返回 true（调用方据此弹提示）</summary>
    public static bool Unlock(string id)
    {
        Load();

        for (int i = 0; i < All.Count; i++)
        {
            var e = All[i];
            if (e.id != id || e.unlocked) continue;

            e.unlocked = true;
            All[i] = e;

            PlayerPrefs.SetInt(SaveKey + e.id, 1);
            PlayerPrefs.Save();
            return true;
        }

        return false;
    }

    /// <summary>在神像前恢复一次；攒够三次解锁"七天神像"，返回 true 表示这一次刚好解锁</summary>
    public static bool MarkBlessing()
    {
        int n = PlayerPrefs.GetInt(BlessKey, 0) + 1;
        PlayerPrefs.SetInt(BlessKey, n);
        PlayerPrefs.Save();

        return n >= 3 && Unlock("blessing");
    }

    /// <summary>调试用：清掉全部成就（将来做"重置存档"会用）</summary>
    public static void ClearAll()
    {
        Load();
        for (int i = 0; i < All.Count; i++)
        {
            var e = All[i];
            e.unlocked = false;
            All[i] = e;
            PlayerPrefs.DeleteKey(SaveKey + e.id);
        }
        PlayerPrefs.DeleteKey(BlessKey);
        PlayerPrefs.Save();
    }
}
