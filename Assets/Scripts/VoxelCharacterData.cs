using System.Collections.Generic;
using UnityEngine;

/// <summary>武器类型（决定谁能装哪把武器）</summary>
public enum WeaponType { Sword = 0, Claymore, Polearm, Bow, Catalyst }

/// <summary>
/// 角色 / 武器 / 圣遗物的静态资料表（只读；运行时的等级、突破、装备在 VoxelCharacterSheet）。
///
/// 角色就是祈愿奖池里那十个：Id 用来存 PlayerPrefs（不用中文名当键，重命名不会丢档），
/// 数值按"1 级基础值 × 等级成长 × 突破成长"算，公式都在这个文件末尾。
/// 基准是主角孙琰：1 级 100 生命 / 25 攻击 —— 和没有养成系统时的手感完全一致，
/// 所以 1 级新号打起来和以前一模一样，越养越强。
/// </summary>
public static class VoxelCharacterData
{
    /// <summary>一条属性加成（圣遗物词条、套装效果、武器都用它描述）</summary>
    public enum Stat
    {
        HpFlat = 0, AtkFlat, DefFlat, HpPercent, AtkPercent, DefPercent, CritRate, CritDamage,
    }

    public static string TypeName(WeaponType type)
    {
        switch (type)
        {
            case WeaponType.Claymore: return "双手剑";
            case WeaponType.Polearm:  return "长柄武器";
            case WeaponType.Bow:      return "弓";
            case WeaponType.Catalyst: return "法器";
            default:                  return "单手剑";
        }
    }

    /// <summary>属性名（界面显示用）</summary>
    public static string StatName(Stat stat)
    {
        switch (stat)
        {
            case Stat.HpFlat:      return "生命值";
            case Stat.AtkFlat:     return "攻击力";
            case Stat.DefFlat:     return "防御力";
            case Stat.HpPercent:   return "生命值";
            case Stat.AtkPercent:  return "攻击力";
            case Stat.DefPercent:  return "防御力";
            case Stat.CritRate:    return "暴击率";
            default:               return "暴击伤害";
        }
    }

    /// <summary>百分比词条（暴击、生命/攻击/防御加成）要按百分比显示</summary>
    public static bool IsPercent(Stat stat) => stat >= Stat.HpPercent;

    /// <summary>词条数值 → 显示文字（百分比词条内部存的是 0.05 这种小数）</summary>
    public static string StatText(Stat stat, float value) =>
        IsPercent(stat) ? (value * 100f).ToString("0.#") + "%" : value.ToString("0.#");

    /// <summary>一条词条连名字带数值（"攻击力+18%"）</summary>
    public static string StatLine(Stat stat, float value) =>
        IsPercent(stat) ? StatName(stat) + "+" + (value * 100f).ToString("0.#") + "%"
                        : StatName(stat) + "+" + value.ToString("0.#");

    // ---------- 角色 ----------

    /// <summary>一个角色的资料</summary>
    public class CharacterInfo
    {
        public string     Id, Name, Title, Element;   // Id 存作用户存档键
        public WeaponType Weapon;
        public int        Rarity;
        public float      BaseHp, BaseAtk, BaseDef;   // 1 级基础值
        public string     NormalName;                 // 普通攻击天赋名
        public string     SkillName, SkillDesc;       // 元素战技
        public string     BurstName, BurstDesc;       // 元素爆发
        public float      SkillCd, SkillEnergy;
        public string     SetName, Set2Text, Set4Text;
        public Stat       Set2Stat = Stat.AtkPercent, Set4Stat = Stat.CritRate;
        public float      Set2Value, Set4Value;
        public Color      ElementColor;
    }

    static readonly CharacterInfo[] CharacterTable =
    {
        new CharacterInfo
        {
            Id = "sunyan", Name = "孙琰", Title = "余数之章", Element = "余数",
            Weapon = WeaponType.Sword, Rarity = 5, BaseHp = 100f, BaseAtk = 25f, BaseDef = 30f,
            NormalName = "余数之刃",
            SkillName = "余数刻印", SkillDesc = "向前划出一道余数刻印，对路径上的敌人造成范围伤害。",
            BurstName = "余数终章", BurstDesc = "写下这一页的最后一笔：把周围的时间都往后挪一格。",
            SkillCd = 6f, SkillEnergy = 60f,
            SetName = "余数之律", Set2Text = "攻击力提高 18%", Set4Text = "暴击率提高 12%",
            Set2Stat = Stat.AtkPercent, Set2Value = 0.18f, Set4Stat = Stat.CritRate, Set4Value = 0.12f,
            ElementColor = new Color(0.42f, 0.78f, 0.92f),
        },
        new CharacterInfo
        {
            Id = "andy", Name = "Andy", Title = "港门之主", Element = "风",
            Weapon = WeaponType.Bow, Rarity = 5, BaseHp = 98f, BaseAtk = 24f, BaseDef = 29f,
            NormalName = "蒲公英箭术",
            SkillName = "港门信风", SkillDesc = "唤来一阵海风，把面前的敌人推开并造成风元素伤害。",
            BurstName = "蒲公英之约", BurstDesc = "随风散开的蒲公英，让全队行动得更轻快。",
            SkillCd = 5f, SkillEnergy = 60f,
            SetName = "蒲公英之风", Set2Text = "攻击力提高 18%", Set4Text = "暴击伤害提高 24%",
            Set2Stat = Stat.AtkPercent, Set2Value = 0.18f, Set4Stat = Stat.CritDamage, Set4Value = 0.24f,
            ElementColor = new Color(0.45f, 0.85f, 0.68f),
        },
        new CharacterInfo
        {
            Id = "lakecity", Name = "湖心城主", Title = "湖心磐岩", Element = "岩",
            Weapon = WeaponType.Claymore, Rarity = 5, BaseHp = 104f, BaseAtk = 25f, BaseDef = 33f,
            NormalName = "磐岩重击",
            SkillName = "湖心垒石", SkillDesc = "在脚边立起一块岩障，并砸出碎石造成岩元素伤害。",
            BurstName = "湖心城垣", BurstDesc = "整座湖心城的影子压下来，把小范围内的敌人钉在原地。",
            SkillCd = 7f, SkillEnergy = 70f,
            SetName = "湖心磐岩", Set2Text = "防御力提高 20%", Set4Text = "生命值提高 20%",
            Set2Stat = Stat.DefPercent, Set2Value = 0.20f, Set4Stat = Stat.HpPercent, Set4Value = 0.20f,
            ElementColor = new Color(0.93f, 0.78f, 0.38f),
        },
        new CharacterInfo
        {
            Id = "tenshu", Name = "将军府雷使", Title = "鸣雷代行", Element = "雷",
            Weapon = WeaponType.Polearm, Rarity = 5, BaseHp = 96f, BaseAtk = 26f, BaseDef = 28f,
            NormalName = "雷光枪术",
            SkillName = "雷罚一闪", SkillDesc = "向前突进一段距离，落点留下一道雷元素刀痕。",
            BurstName = "将军府敕令", BurstDesc = "唤起天边的雷云，连续落雷打击范围内的敌人。",
            SkillCd = 8f, SkillEnergy = 80f,
            SetName = "鸣雷之律", Set2Text = "攻击力提高 18%", Set4Text = "暴击率提高 12%",
            Set2Stat = Stat.AtkPercent, Set2Value = 0.18f, Set4Stat = Stat.CritRate, Set4Value = 0.12f,
            ElementColor = new Color(0.79f, 0.60f, 0.98f),
        },
        new CharacterInfo
        {
            Id = "harbor_merchant", Name = "港门商会长", Title = "港门商契", Element = "岩",
            Weapon = WeaponType.Catalyst, Rarity = 4, BaseHp = 90f, BaseAtk = 23f, BaseDef = 27f,
            NormalName = "商契秘法",
            SkillName = "港门契书", SkillDesc = "摊开一卷契书，为全队附上一层护盾。",
            BurstName = "三日之约", BurstDesc = "三日内到货：把账本上欠的东西一次结清。",
            SkillCd = 9f, SkillEnergy = 60f,
            SetName = "港门商契", Set2Text = "生命值提高 20%", Set4Text = "攻击力提高 18%",
            Set2Stat = Stat.HpPercent, Set2Value = 0.20f, Set4Stat = Stat.AtkPercent, Set4Value = 0.18f,
            ElementColor = new Color(0.93f, 0.78f, 0.38f),
        },
        new CharacterInfo
        {
            Id = "snow_hunter", Name = "雪顶猎手", Title = "雪线之上", Element = "冰",
            Weapon = WeaponType.Bow, Rarity = 4, BaseHp = 88f, BaseAtk = 24f, BaseDef = 26f,
            NormalName = "雪顶箭术",
            SkillName = "碎冰一箭", SkillDesc = "射出一支结霜的箭，命中处炸开一片冰屑。",
            BurstName = "雪线封缄", BurstDesc = "让风雪把整片场地盖住，被冻住的敌人寸步难行。",
            SkillCd = 6f, SkillEnergy = 60f,
            SetName = "雪顶猎装", Set2Text = "攻击力提高 18%", Set4Text = "暴击伤害提高 24%",
            Set2Stat = Stat.AtkPercent, Set2Value = 0.18f, Set4Stat = Stat.CritDamage, Set4Value = 0.24f,
            ElementColor = new Color(0.62f, 0.88f, 0.98f),
        },
        new CharacterInfo
        {
            Id = "miko", Name = "鸣神巫女", Title = "鸣神祝词", Element = "雷",
            Weapon = WeaponType.Catalyst, Rarity = 4, BaseHp = 86f, BaseAtk = 23f, BaseDef = 26f,
            NormalName = "鸣神法仪",
            SkillName = "御雷之符", SkillDesc = "在身前悬起三张雷符，依次落下打击敌人。",
            BurstName = "鸣神大祓", BurstDesc = "一场把污秽连同敌人一起扫净的神事。",
            SkillCd = 7f, SkillEnergy = 70f,
            SetName = "鸣神祝词", Set2Text = "攻击力提高 18%", Set4Text = "暴击率提高 12%",
            Set2Stat = Stat.AtkPercent, Set2Value = 0.18f, Set4Stat = Stat.CritRate, Set4Value = 0.12f,
            ElementColor = new Color(0.79f, 0.60f, 0.98f),
        },
        new CharacterInfo
        {
            Id = "lakeside_guard", Name = "湖畔卫兵", Title = "湖畔守望", Element = "风",
            Weapon = WeaponType.Sword, Rarity = 4, BaseHp = 92f, BaseAtk = 22f, BaseDef = 30f,
            NormalName = "湖畔剑式",
            SkillName = "守望之风", SkillDesc = "一圈护身的风，替身边的同伴挡下这一次攻击。",
            BurstName = "湖畔长哨", BurstDesc = "一声长哨，把湖畔的同伴都喊过来协防。",
            SkillCd = 5f, SkillEnergy = 60f,
            SetName = "湖畔守望", Set2Text = "防御力提高 20%", Set4Text = "攻击力提高 18%",
            Set2Stat = Stat.DefPercent, Set2Value = 0.20f, Set4Stat = Stat.AtkPercent, Set4Value = 0.18f,
            ElementColor = new Color(0.45f, 0.85f, 0.68f),
        },
        new CharacterInfo
        {
            Id = "karst_miner", Name = "喀斯特矿工", Title = "峰林之下", Element = "岩",
            Weapon = WeaponType.Claymore, Rarity = 4, BaseHp = 95f, BaseAtk = 23f, BaseDef = 29f,
            NormalName = "岩心巨刃",
            SkillName = "开山一锄", SkillDesc = "照着脚下凿开一道石缝，把敌人震得站不稳。",
            BurstName = "峰林回声", BurstDesc = "整片峰林一起回响，碎石雨落下来砸满场地。",
            SkillCd = 8f, SkillEnergy = 80f,
            SetName = "喀斯特岩心", Set2Text = "生命值提高 20%", Set4Text = "防御力提高 20%",
            Set2Stat = Stat.HpPercent, Set2Value = 0.20f, Set4Stat = Stat.DefPercent, Set4Value = 0.20f,
            ElementColor = new Color(0.93f, 0.78f, 0.38f),
        },
        new CharacterInfo
        {
            Id = "statue_priest", Name = "神像祭司", Title = "神像礼赞", Element = "草",
            Weapon = WeaponType.Catalyst, Rarity = 4, BaseHp = 90f, BaseAtk = 22f, BaseDef = 28f,
            NormalName = "神像祷言",
            SkillName = "七天之祈", SkillDesc = "向着七天神像祷告，为全队缓慢回复生命。",
            BurstName = "神像垂怜", BurstDesc = "神像低眉看下来，把这片土地上的伤口都抚平。",
            SkillCd = 10f, SkillEnergy = 80f,
            SetName = "神像礼赞", Set2Text = "生命值提高 20%", Set4Text = "暴击率提高 12%",
            Set2Stat = Stat.HpPercent, Set2Value = 0.20f, Set4Stat = Stat.CritRate, Set4Value = 0.12f,
            ElementColor = new Color(0.58f, 0.86f, 0.47f),
        },
        // Local model evaluation, available only when its separately downloaded asset exists.
        new CharacterInfo
        {
            Id = LocalRemielle.CharacterId, Name = "蕾米埃尔", Title = "本地模型测试角色", Element = "光（测试）",
            Weapon = WeaponType.Catalyst, Rarity = 5, BaseHp = 100f, BaseAtk = 25f, BaseDef = 30f,
            NormalName = "微光试射", SkillName = "星芒脉冲", BurstName = "流光环",
            SkillDesc = "本地测试：沿用现有范围战技，检查角色切换与战斗接口。",
            BurstDesc = "本地测试：沿用现有范围爆发，能量和冷却遵循本游戏规则。",
            SkillCd = 6f, SkillEnergy = 60f,
            SetName = "流光测试", Set2Text = "攻击力提高 18%", Set4Text = "暴击率提高 12%",
            Set2Stat = Stat.AtkPercent, Set2Value = .18f, Set4Stat = Stat.CritRate, Set4Value = .12f,
            ElementColor = new Color(.83f, .79f, .98f)
        },
    };

    static readonly Dictionary<string, CharacterInfo> CharacterByName = BuildCharacterIndex();

    static Dictionary<string, CharacterInfo> BuildCharacterIndex()
    {
        var map = new Dictionary<string, CharacterInfo>();
        foreach (var info in CharacterTable) map[info.Name] = info;
        return map;
    }

    /// <summary>全部角色（顺序即界面里的顺序）</summary>
    public static IReadOnlyList<CharacterInfo> Characters => CharacterTable;

    /// <summary>主角：默认就有，不用抽</summary>
    public const string PlayerName = "孙琰";

    /// <summary>按名字查角色；不是角色（比如素材）就返回 null</summary>
    public static CharacterInfo CharacterOf(string name) =>
        !string.IsNullOrEmpty(name) && CharacterByName.TryGetValue(name, out var info) ? info : null;

    /// <summary>按 Id 查角色（读存档用）</summary>
    public static CharacterInfo CharacterById(string id)
    {
        foreach (var info in CharacterTable)
            if (info.Id == id) return info;
        return null;
    }

    // ---------- 武器 ----------

    /// <summary>一把武器的资料（基础攻击力是 1 级的，等级成长见 WeaponAttack）</summary>
    public class WeaponInfo
    {
        public string     Name;
        public WeaponType Type;
        public int        Rarity;
        public float      BaseAtk;
        public string     Passive, PassiveDesc;
    }

    // 奖池里出现过的武器都在这里（十连抽到几把，精炼就是几 —— 见 VoxelCharacterSheet）
    static readonly WeaponInfo[] WeaponTable =
    {
        new WeaponInfo { Name = "新手长剑", Type = WeaponType.Sword,    Rarity = 3, BaseAtk = 39f,
                         Passive = "初出茅庐", PassiveDesc = "刚上手的那把剑，握着还算顺手。" },
        new WeaponInfo { Name = "新手猎弓", Type = WeaponType.Bow,      Rarity = 3, BaseAtk = 38f,
                         Passive = "初出茅庐", PassiveDesc = "弦有点松，但拉得开。" },
        new WeaponInfo { Name = "新手法器", Type = WeaponType.Catalyst, Rarity = 3, BaseAtk = 40f,
                         Passive = "初出茅庐", PassiveDesc = "书页上还留着前一位主人的批注。" },
        new WeaponInfo { Name = "新手长柄", Type = WeaponType.Polearm,  Rarity = 3, BaseAtk = 39f,
                         Passive = "初出茅庐", PassiveDesc = "枪头磨得发亮，看得出常被擦拭。" },
        new WeaponInfo { Name = "新手大剑", Type = WeaponType.Claymore, Rarity = 3, BaseAtk = 41f,
                         Passive = "初出茅庐", PassiveDesc = "分量十足，挥两下就喘。" },

        new WeaponInfo { Name = "喀斯特金石", Type = WeaponType.Catalyst, Rarity = 4, BaseAtk = 42f,
                         Passive = "峰林之重", PassiveDesc = "攻击力提高 8%。" },
        new WeaponInfo { Name = "鸣神短刀", Type = WeaponType.Sword,    Rarity = 4, BaseAtk = 44f,
                         Passive = "御雷之刃", PassiveDesc = "普通攻击命中后，攻击力提高 6%，最多叠 3 层。" },
        new WeaponInfo { Name = "雪顶长弓", Type = WeaponType.Bow,      Rarity = 4, BaseAtk = 42f,
                         Passive = "雪线之息", PassiveDesc = "暴击率提高 6%。" },
        new WeaponInfo { Name = "港门巨斧", Type = WeaponType.Claymore, Rarity = 4, BaseAtk = 43f,
                         Passive = "港门之力", PassiveDesc = "攻击力提高 9%，受到的伤害提高 3%。" },
        new WeaponInfo { Name = "神像法典", Type = WeaponType.Catalyst, Rarity = 4, BaseAtk = 41f,
                         Passive = "神像之悯", PassiveDesc = "生命值提高 10%。" },
        new WeaponInfo { Name = "湖畔猎弓", Type = WeaponType.Bow,      Rarity = 4, BaseAtk = 42f,
                         Passive = "湖畔之息", PassiveDesc = "移动之后的下一次攻击伤害提高 12%。" },
        new WeaponInfo { Name = "峰林重剑", Type = WeaponType.Claymore, Rarity = 4, BaseAtk = 44f,
                         Passive = "峰林之压", PassiveDesc = "攻击命中时，防御力提高 6%，最多叠 3 层。" },
        new WeaponInfo { Name = "鸣神法杖", Type = WeaponType.Catalyst, Rarity = 4, BaseAtk = 43f,
                         Passive = "鸣神之仪", PassiveDesc = "元素爆发造成的伤害提高 12%。" },

        new WeaponInfo { Name = "雪顶之刃", Type = WeaponType.Sword,    Rarity = 5, BaseAtk = 46f,
                         Passive = "雪顶之誓", PassiveDesc = "攻击命中后攻击力提升 8%，最多叠 3 层。" },
        new WeaponInfo { Name = "港门长枪", Type = WeaponType.Polearm,  Rarity = 5, BaseAtk = 47f,
                         Passive = "港门之潮", PassiveDesc = "普通攻击与元素战技造成的伤害提高 10%。" },
        new WeaponInfo { Name = "将军府太刀", Type = WeaponType.Sword,  Rarity = 5, BaseAtk = 46f,
                         Passive = "敕令之雷", PassiveDesc = "暴击伤害提高 20%。" },
        new WeaponInfo { Name = "湖心城法典", Type = WeaponType.Catalyst, Rarity = 5, BaseAtk = 45f,
                         Passive = "湖心之静", PassiveDesc = "生命值提高 16%，受到的伤害降低 6%。" },
    };

    static readonly Dictionary<string, WeaponInfo> WeaponByName = BuildWeaponIndex();

    static Dictionary<string, WeaponInfo> BuildWeaponIndex()
    {
        var map = new Dictionary<string, WeaponInfo>();
        foreach (var info in WeaponTable) map[info.Name] = info;
        return map;
    }

    public static IReadOnlyList<WeaponInfo> Weapons => WeaponTable;

    /// <summary>按名字查武器；不是武器（比如素材）就返回 null</summary>
    public static WeaponInfo WeaponOf(string name) =>
        !string.IsNullOrEmpty(name) && WeaponByName.TryGetValue(name, out var info) ? info : null;

    /// <summary>武器在某个等级的基础攻击力（90 级约 4.45 倍）</summary>
    public static float WeaponAttack(WeaponInfo weapon, int level) =>
        weapon.BaseAtk * (1f + 0.039f * (Mathf.Clamp(level, 1, MaxLevel) - 1));

    /// <summary>武器的精炼上限（同名抽到几把就是几级）</summary>
    public const int MaxRefine = 5;
    public const int MaxWeaponLevel = 90;

    // ---------- 圣遗物 ----------

    /// <summary>一个圣遗物槽位的定义：主词条 + 两条副词条。
    /// 五个槽位的主词条和原神一致（花固定生命、羽固定攻击），值随强化等级线性增长</summary>
    public class ArtifactSlotInfo
    {
        public string SlotName;        // 生之花 / 死之羽 / 时之沙 / 空之杯 / 理之冠
        public Stat   MainStat;
        public float  MainBase, MainPerLevel;
        public Stat   Sub1; public float Sub1Base, Sub1PerLevel;
        public Stat   Sub2; public float Sub2Base, Sub2PerLevel;
    }

    public static readonly ArtifactSlotInfo[] Slots =
    {
        new ArtifactSlotInfo
        {
            SlotName = "生之花", MainStat = Stat.HpFlat, MainBase = 200f, MainPerLevel = 40f,
            Sub1 = Stat.AtkPercent, Sub1Base = 0.041f, Sub1PerLevel = 0.0022f,
            Sub2 = Stat.CritRate,   Sub2Base = 0.027f, Sub2PerLevel = 0.0014f,
        },
        new ArtifactSlotInfo
        {
            SlotName = "死之羽", MainStat = Stat.AtkFlat, MainBase = 12f, MainPerLevel = 2.4f,
            Sub1 = Stat.HpPercent,  Sub1Base = 0.041f, Sub1PerLevel = 0.0022f,
            Sub2 = Stat.CritDamage, Sub2Base = 0.054f, Sub2PerLevel = 0.0028f,
        },
        new ArtifactSlotInfo
        {
            SlotName = "时之沙", MainStat = Stat.AtkPercent, MainBase = 0.07f, MainPerLevel = 0.005f,
            Sub1 = Stat.HpFlat,     Sub1Base = 150f, Sub1PerLevel = 12f,
            Sub2 = Stat.DefFlat,    Sub2Base = 12f, Sub2PerLevel = 1.2f,
        },
        new ArtifactSlotInfo
        {
            SlotName = "空之杯", MainStat = Stat.HpPercent, MainBase = 0.07f, MainPerLevel = 0.005f,
            Sub1 = Stat.AtkFlat,    Sub1Base = 8f, Sub1PerLevel = 0.8f,
            Sub2 = Stat.CritRate,   Sub2Base = 0.027f, Sub2PerLevel = 0.0014f,
        },
        new ArtifactSlotInfo
        {
            SlotName = "理之冠", MainStat = Stat.CritRate, MainBase = 0.046f, MainPerLevel = 0.0035f,
            Sub1 = Stat.AtkPercent, Sub1Base = 0.041f, Sub1PerLevel = 0.0022f,
            Sub2 = Stat.CritDamage, Sub2Base = 0.054f, Sub2PerLevel = 0.0028f,
        },
    };

    public const int MaxArtifactLevel = 20;

    // ---------- 成长公式 ----------

    public const int MaxLevel = 90;

    /// <summary>突破阶级（0~6）对应的等级上限</summary>
    static readonly int[] LevelCapByPhase = { 20, 40, 50, 60, 70, 80, 90 };

    public static int LevelCap(int phase) => LevelCapByPhase[Mathf.Clamp(phase, 0, LevelCapByPhase.Length - 1)];

    /// <summary>等级成长：每级 +5%（1 级 = 1.0；90 级 ≈ 5.45 倍）</summary>
    public static float LevelScale(int level) => 1f + 0.05f * (Mathf.Clamp(level, 1, MaxLevel) - 1);

    /// <summary>突破成长：每一阶 +8%</summary>
    public static float PhaseScale(int phase) => 1f + 0.08f * Mathf.Clamp(phase, 0, 6);

    /// <summary>普通攻击倍率 —— 这是真的加到伤害上的那个数（1 级 130%，10 级 202%）</summary>
    public static float NormalAttackMultiplier(int talentLevel) => 1.30f + 0.08f * (Mathf.Clamp(talentLevel, 1, 10) - 1);

    /// <summary>元素战技倍率（界面上展示；战技本身还没接进战斗）</summary>
    public static float SkillMultiplier(int talentLevel) => 2.40f + 0.20f * (Mathf.Clamp(talentLevel, 1, 10) - 1);

    /// <summary>元素爆发倍率（同上，展示用）</summary>
    public static float BurstMultiplier(int talentLevel) => 6.00f + 0.50f * (Mathf.Clamp(talentLevel, 1, 10) - 1);

    /// <summary>天赋等级上限：跟着突破阶级走（0 阶 2 级、1 阶 4 级……4 阶起 10 级）</summary>
    public static int TalentCap(int phase) => Mathf.Min(10, 2 + Mathf.Clamp(phase, 0, 6) * 2);

    public const float BaseCritRate   = 0.05f;    // 自带 5% 暴击率
    public const float BaseCritDamage = 0.50f;    // 自带 50% 暴击伤害

    /// <summary>防御力换算的伤害减免：def / (def + 400)，最多减 70%</summary>
    public static float DamageReduction(float defense) =>
        Mathf.Min(0.70f, Mathf.Max(0f, defense) / (Mathf.Max(0f, defense) + 400f));
}
