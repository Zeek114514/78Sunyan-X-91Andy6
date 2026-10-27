using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 角色养成状态（界面在 VoxelCharacterScreen，按 C 打开）：
/// 每个角色的等级 / 突破阶级 / 三个天赋等级 / 装着的武器与其等级 / 五件圣遗物的装备位与强化等级，
/// 存在 PlayerPrefs 里（和成就、祈愿保底一样，重进游戏还在）。
///
/// ★ 出战角色的数值是真的会算到战斗里的 —— 不是看一眼就完事的展示数据：
///   · 生命上限 → VoxelStats 的血条上限（升到 90 级比 1 级能扛十倍左右）
///   · 攻击力 × 普通攻击倍率 → 每一下普攻的伤害（VoxelCombat）
///   · 防御力 → 受到伤害的减免
///   · 暴击率 / 暴击伤害 → 暴击判定
///
/// 1 级、什么都没装的时候，主角就是 100 生命 / 25 攻击 —— 和加养成系统之前完全一样，
/// 所以不会把老存档的手感改掉。另外战技 / 爆发目前只有资料与倍率展示（还没接进战斗）。
/// </summary>
public class VoxelCharacterSheet : MonoBehaviour
{
    /// <summary>场上那一份（战斗、生命上限都问它）</summary>
    public static VoxelCharacterSheet Instance { get; private set; }

    /// <summary>主角：默认就有，不用抽</summary>
    public const string PlayerName = VoxelCharacterData.PlayerName;

    // ---------- 静态快捷取值：给战斗 / 生命系统用 ----------
    // 没有角色系统时（比如单独打开某个场景）就退回原来的手感：100 血、不减伤、不暴击

    public static float ActiveMaxHealth       => Current != null ? Current.MaxHealth       : VoxelStats.Max;
    public static float ActiveAttack          => Current != null ? Current.Attack          : 0f;
    public static float ActiveDamageReduction => Current != null ? Current.DamageReduction : 0f;
    public static float ActiveCritRate        => Current != null ? Current.CritRate        : 0f;
    public static float ActiveCritDamage      => Current != null ? Current.CritDamage      : 0f;

    /// <summary>出战角色每一下普攻的伤害（攻击力 × 普通攻击倍率）；没有角色系统就是 0</summary>
    public static float ActiveNormalAttackDamage => Current != null ? Current.NormalAttackDamage : 0f;

    /// <summary>出战角色的养成状态（战斗里读冷却 / 能量 / 战技名要用）；没有角色系统时是 null</summary>
    public static CharacterState ActiveState => Current;

    /// <summary>出战角色（没有角色系统、或者名册空的时候是 null）</summary>
    static CharacterState Current => Instance != null ? Instance._active : null;

    // ---------- 一个角色的养成状态 ----------

    public class CharacterState
    {
        public readonly VoxelCharacterData.CharacterInfo Info;

        readonly VoxelCharacterSheet _sheet;

        public int    Level = 1;
        public int    Phase;                                  // 突破阶级 0~6
        public readonly int[] Talents = { 1, 1, 1 };           // 普通攻击 / 元素战技 / 元素爆发
        public string WeaponName = "";                        // 装着的武器（"" = 空手）
        public int    WeaponLevel = 1;
        public readonly bool[] Equipped = new bool[VoxelCharacterData.Slots.Length];
        public readonly int[]  ArtifactLevels = new int[VoxelCharacterData.Slots.Length];

        public CharacterState(VoxelCharacterSheet sheet, VoxelCharacterData.CharacterInfo info)
        {
            _sheet = sheet;
            Info = info;
        }

        public string Name => Info.Name;

        public int  LevelCap   => VoxelCharacterData.LevelCap(Phase);
        public bool CanLevelUp => Level < LevelCap;
        public bool CanAscend  => Phase < 6 && Level >= LevelCap;

        // ---- 基础数值（等级 + 突破） ----

        public float BaseHp  => Info.BaseHp  * VoxelCharacterData.LevelScale(Level) * VoxelCharacterData.PhaseScale(Phase);
        public float BaseAtk => Info.BaseAtk * VoxelCharacterData.LevelScale(Level) * VoxelCharacterData.PhaseScale(Phase);
        public float BaseDef => Info.BaseDef * VoxelCharacterData.LevelScale(Level) * VoxelCharacterData.PhaseScale(Phase);

        // ---- 装备 ----

        public VoxelCharacterData.WeaponInfo Weapon => VoxelCharacterData.WeaponOf(WeaponName);
        public float WeaponAttack => Weapon != null ? VoxelCharacterData.WeaponAttack(Weapon, WeaponLevel) : 0f;

        /// <summary>精炼等级：同名武器抽到几把就是几级（原神那种"叠精炼"）</summary>
        public int Refine => Mathf.Clamp(_sheet.OwnedCount(WeaponName), 1, VoxelCharacterData.MaxRefine);

        /// <summary>装上了几件圣遗物 —— 2 件套 / 4 件套按这个数生效</summary>
        public int EquippedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Equipped.Length; i++) if (Equipped[i]) n++;
                return n;
            }
        }

        /// <summary>把装上的圣遗物词条和套装效果加起来（卸下的不算）</summary>
        public float BonusOf(VoxelCharacterData.Stat stat)
        {
            float total = 0f;

            for (int i = 0; i < VoxelCharacterData.Slots.Length; i++)
            {
                if (!Equipped[i]) continue;

                var slot = VoxelCharacterData.Slots[i];
                float level = ArtifactLevels[i];

                if (slot.MainStat == stat) total += slot.MainBase + slot.MainPerLevel * level;
                if (slot.Sub1     == stat) total += slot.Sub1Base + slot.Sub1PerLevel * level;
                if (slot.Sub2     == stat) total += slot.Sub2Base + slot.Sub2PerLevel * level;
            }

            int worn = EquippedCount;
            if (worn >= 2 && Info.Set2Stat == stat) total += Info.Set2Value;
            if (worn >= 4 && Info.Set4Stat == stat) total += Info.Set4Value;

            return total;
        }

        // ---- 总数值（界面和战斗都读这几个） ----

        public float MaxHealth => BaseHp * (1f + BonusOf(VoxelCharacterData.Stat.HpPercent))
                                       + BonusOf(VoxelCharacterData.Stat.HpFlat);

        public float Attack => (BaseAtk + WeaponAttack) * (1f + BonusOf(VoxelCharacterData.Stat.AtkPercent))
                                                        + BonusOf(VoxelCharacterData.Stat.AtkFlat);

        public float Defense => BaseDef * (1f + BonusOf(VoxelCharacterData.Stat.DefPercent))
                                       + BonusOf(VoxelCharacterData.Stat.DefFlat);

        public float CritRate   => VoxelCharacterData.BaseCritRate   + BonusOf(VoxelCharacterData.Stat.CritRate);
        public float CritDamage => VoxelCharacterData.BaseCritDamage + BonusOf(VoxelCharacterData.Stat.CritDamage);

        // ---- 战斗中的运行时状态（冷却与能量不存档：跟原神一样是"这一场"的状态） ----

        public float SkillCooldown;      // 元素战技剩余冷却（秒）
        public float BurstCooldown;      // 元素爆发剩余冷却（秒）
        public float Energy;             // 当前元素能量

        public float MaxEnergy  => Info.SkillEnergy;
        public bool  SkillReady => SkillCooldown <= 0f;
        public bool  BurstReady => BurstCooldown <= 0f && Energy >= MaxEnergy;

        /// <summary>每帧推进冷却与能量回复（普攻命中还会额外给能量，见 VoxelCombat）</summary>
        public void TickBattle(float dt)
        {
            if (SkillCooldown > 0f) SkillCooldown = Mathf.Max(0f, SkillCooldown - dt);
            if (BurstCooldown > 0f) BurstCooldown = Mathf.Max(0f, BurstCooldown - dt);
            if (Energy < MaxEnergy) Energy = Mathf.Min(MaxEnergy, Energy + EnergyPerSecond * dt);
        }

        /// <summary>元素能量自然回复速度（每秒）</summary>
        public const float EnergyPerSecond = 2.5f;

        /// <summary>普攻命中给多少元素能量（原神那种"打出来的能量"）</summary>
        public const float EnergyPerHit = 6f;

        public void GainEnergy(float amount) => Energy = Mathf.Min(MaxEnergy, Energy + amount);

        /// <summary>放了一次元素战技：进冷却</summary>
        public void StartSkillCooldown() => SkillCooldown = Info.SkillCd;

        /// <summary>放了一次元素爆发：能量清零 + 一点收招硬直（免得连按两次）</summary>
        public void ConsumeBurst()
        {
            Energy = 0f;
            BurstCooldown = 1.2f;
        }

        public float DamageReduction => VoxelCharacterData.DamageReduction(Defense);

        /// <summary>每一下普攻的伤害 = 攻击力 × 普通攻击倍率（天赋等级）</summary>
        public float NormalAttackDamage => Attack * VoxelCharacterData.NormalAttackMultiplier(Talents[0]);

        public float SkillDamage => Attack * VoxelCharacterData.SkillMultiplier(Talents[1]);
        public float BurstDamage => Attack * VoxelCharacterData.BurstMultiplier(Talents[2]);

        // ---- 养成操作（都会写回存档） ----

        /// <summary>升 count 级，到当前等级上限就停；返回真的升了几级</summary>
        public int LevelUp(int count)
        {
            int before = Level;
            Level = Mathf.Min(LevelCap, Level + Mathf.Max(1, count));

            if (Level != before) _sheet.Save(this);
            return Level - before;
        }

        /// <summary>突破一次（等级到上限才能突；暂时不消耗材料）：阶级 +1，数值和等级上限一起抬</summary>
        public bool Ascend()
        {
            if (!CanAscend) return false;

            Phase++;
            _sheet.Save(this);
            return true;
        }

        /// <summary>升一级天赋（上限跟着突破阶级走）</summary>
        public bool UpgradeTalent(int index)
        {
            if (index < 0 || index >= Talents.Length) return false;
            if (Talents[index] >= VoxelCharacterData.TalentCap(Phase)) return false;

            Talents[index]++;
            _sheet.Save(this);
            return true;
        }

        /// <summary>换武器（换的那把从 1 级重新养）</summary>
        public void EquipWeapon(string name)
        {
            WeaponName = name ?? "";
            WeaponLevel = 1;
            _sheet.Save(this);
        }

        public void UnequipWeapon() => EquipWeapon("");

        /// <summary>升 count 级武器，到 90 级封顶；返回真的升了几级</summary>
        public int WeaponLevelUp(int count)
        {
            if (Weapon == null) return 0;

            int before = WeaponLevel;
            WeaponLevel = Mathf.Min(VoxelCharacterData.MaxWeaponLevel, WeaponLevel + Mathf.Max(1, count));

            if (WeaponLevel != before) _sheet.Save(this);
            return WeaponLevel - before;
        }

        /// <summary>装上 / 卸下一件圣遗物（卸下会同时让 2 件套 / 4 件套失效）</summary>
        public void SetArtifactEquipped(int slot, bool on)
        {
            if (slot < 0 || slot >= Equipped.Length) return;

            Equipped[slot] = on;
            _sheet.Save(this);
        }

        /// <summary>强化一件圣遗物，+20 封顶；返回真的强化了几级</summary>
        public int UpgradeArtifact(int slot, int count)
        {
            if (slot < 0 || slot >= ArtifactLevels.Length) return 0;

            int before = ArtifactLevels[slot];
            ArtifactLevels[slot] = Mathf.Min(VoxelCharacterData.MaxArtifactLevel, ArtifactLevels[slot] + Mathf.Max(1, count));

            if (ArtifactLevels[slot] != before) _sheet.Save(this);
            return ArtifactLevels[slot] - before;
        }
    }

    // ---------- 名册与出战 ----------

    readonly Dictionary<string, CharacterState> _states = new Dictionary<string, CharacterState>();
    readonly List<CharacterState> _roster = new List<CharacterState>();

    VoxelInteraction _inventory;
    bool _training;
    CharacterState _active;

    const string KeyPrefix = "CodelyDemo.Char.";
    const string KeyActive = KeyPrefix + "Active";

    /// <summary>能看的角色：主角 + 祈愿抽到的（顺序按资料表）</summary>
    public IReadOnlyList<CharacterState> Roster => _roster;

    /// <summary>出战角色 —— 战斗中生效的就是这一位</summary>
    public CharacterState Active => _active;

    public void Init(VoxelInteraction inventory)
    {
        _inventory = inventory;
        Instance = this;

        Load();
        Rebuild();
    }

    public void InitTraining(VoxelInteraction inventory)
    {
        _training = true;
        Init(inventory);
    }

    // Reclaim the existing live roster after an optional, isolated training visit.
    public void ResumeAfterTraining() { Instance = this; }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;      // 停播时清掉静态状态
    }

    void Update()
    {
        // 冷却与元素能量只在"游戏内"推进（面板开着 / 鼠标松开时算暂停）
        if (!VoxelPanels.CanUseInput || _active == null) return;

        _active.TickBattle(Time.deltaTime);
    }

    /// <summary>某样东西持有多少（武器精炼按它算，所以得问物品栏）</summary>
    public int OwnedCount(string name) => _inventory != null && !string.IsNullOrEmpty(name)
                                        ? _inventory.MaterialCount(name) : 0;

    /// <summary>重新清点角色：主角永远有；其余看祈愿有没有抽到 ——
    /// 抽到的角色以名字记在物品栏的素材表里（见 VoxelGacha.Grant）</summary>
    public void Rebuild()
    {
        var previous = _active;
        _roster.Clear();

        foreach (var info in VoxelCharacterData.Characters)
        {
            if (!_states.TryGetValue(info.Id, out var state)) continue;
            bool localTest = info.Id == LocalRemielle.CharacterId && LocalRemielle.Available;
            if (info.Id == LocalRemielle.CharacterId && !localTest) continue;
            if (info.Name != PlayerName && !localTest && OwnedCount(info.Name) <= 0) continue;

            _roster.Add(state);
        }

        var hero = Find(VoxelCharacterData.CharacterOf(PlayerName));
        if (hero != null && !_roster.Contains(hero)) _roster.Insert(0, hero);      // 主角保证在第一位

        // 出战角色：存档里存的是 id；读不到（比如那个角色这局没有）就退回主角
        _active = Find(VoxelCharacterData.CharacterById(PlayerPrefs.GetString(KeyActive, "")))
                  ?? (hero != null ? hero : (_roster.Count > 0 ? _roster[0] : null));
        if (_active != null && !_roster.Contains(_active)) _active = hero;
        if (previous != null && previous.Info.Id == LocalRemielle.CharacterId && _roster.Contains(previous)) _active = previous;
    }

    CharacterState Find(VoxelCharacterData.CharacterInfo info) =>
        info != null && _states.TryGetValue(info.Id, out var state) ? state : null;

    /// <summary>换出战角色（生命上限、攻击力、减伤立刻跟着变；血条按比例保留）</summary>
    public void SetActive(CharacterState state)
    {
        if (state == null || !_roster.Contains(state)) return;

        _active = state;
        if (!_training && state.Info.Id != LocalRemielle.CharacterId)
        {
            PlayerPrefs.SetString(KeyActive, state.Info.Id);
            PlayerPrefs.Save();
        }
    }

    /// <summary>这个人现在能装哪些武器（同类型 + 已经抽到的），高星在前</summary>
    public List<VoxelCharacterData.WeaponInfo> EquippableWeapons(CharacterState state)
    {
        var list = new List<VoxelCharacterData.WeaponInfo>();
        if (state == null) return list;

        foreach (var weapon in VoxelCharacterData.Weapons)
            if (weapon.Type == state.Info.Weapon && OwnedCount(weapon.Name) > 0) list.Add(weapon);

        list.Sort((a, b) => a.Rarity != b.Rarity ? b.Rarity.CompareTo(a.Rarity)
                                                 : string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    // ---------- 存档 ----------

    void Load()
    {
        _states.Clear();

        foreach (var info in VoxelCharacterData.Characters)
        {
            string key = KeyPrefix + info.Id + ".";

            var state = new CharacterState(this, info)
            {
                Level       = Mathf.Clamp(PlayerPrefs.GetInt(key + "Lv", 1), 1, VoxelCharacterData.MaxLevel),
                Phase       = Mathf.Clamp(PlayerPrefs.GetInt(key + "Phase", 0), 0, 6),
                WeaponName  = PlayerPrefs.GetString(key + "Weapon", ""),
                WeaponLevel = Mathf.Clamp(PlayerPrefs.GetInt(key + "WLv", 1), 1, VoxelCharacterData.MaxWeaponLevel),
            };

            for (int i = 0; i < state.Talents.Length; i++)
                state.Talents[i] = Mathf.Clamp(PlayerPrefs.GetInt(key + "T" + i, 1), 1, 10);

            for (int i = 0; i < VoxelCharacterData.Slots.Length; i++)
            {
                state.Equipped[i] = PlayerPrefs.GetInt(key + "A" + i + "Eq", 0) != 0;
                state.ArtifactLevels[i] = Mathf.Clamp(PlayerPrefs.GetInt(key + "A" + i + "Lv", 0),
                                                      0, VoxelCharacterData.MaxArtifactLevel);
            }

            // 存档里记着的武器如果这局根本不在了（比如清过档），就当空手
            if (state.Weapon != null && OwnedCount(state.WeaponName) <= 0) state.WeaponName = "";

            _states[info.Id] = state;
        }
    }

    /// <summary>清掉角色养成的存档（等级 / 突破 / 天赋 / 武器 / 圣遗物 / 出战角色）——
    /// 菜单里的"重置存档"会调它；清完回到 1 级空装状态</summary>
    public void ClearAll()
    {
        foreach (var info in VoxelCharacterData.Characters)
        {
            string key = KeyPrefix + info.Id + ".";

            PlayerPrefs.DeleteKey(key + "Lv");
            PlayerPrefs.DeleteKey(key + "Phase");
            PlayerPrefs.DeleteKey(key + "Weapon");
            PlayerPrefs.DeleteKey(key + "WLv");

            for (int i = 0; i < 3; i++) PlayerPrefs.DeleteKey(key + "T" + i);

            for (int i = 0; i < VoxelCharacterData.Slots.Length; i++)
            {
                PlayerPrefs.DeleteKey(key + "A" + i + "Eq");
                PlayerPrefs.DeleteKey(key + "A" + i + "Lv");
            }
        }

        PlayerPrefs.DeleteKey(KeyActive);
        PlayerPrefs.Save();

        Load();         // 把内存里的那份也重置回初始（1 级、空手、圣遗物未装备）
        Rebuild();
    }

    /// <summary>把某个角色的养成状态写回存档（升级 / 突破 / 换装备之后调用）</summary>
    public void Save(CharacterState state)
    {        if (_training || state == null || state.Info.Id == LocalRemielle.CharacterId) return;

        string key = KeyPrefix + state.Info.Id + ".";
        PlayerPrefs.SetInt(key + "Lv", state.Level);
        PlayerPrefs.SetInt(key + "Phase", state.Phase);
        PlayerPrefs.SetString(key + "Weapon", state.WeaponName ?? "");
        PlayerPrefs.SetInt(key + "WLv", state.WeaponLevel);

        for (int i = 0; i < state.Talents.Length; i++)
            PlayerPrefs.SetInt(key + "T" + i, state.Talents[i]);

        for (int i = 0; i < state.Equipped.Length; i++)
        {
            PlayerPrefs.SetInt(key + "A" + i + "Eq", state.Equipped[i] ? 1 : 0);
            PlayerPrefs.SetInt(key + "A" + i + "Lv", state.ArtifactLevels[i]);
        }

        PlayerPrefs.Save();
    }
}
