using UnityEngine;

/// <summary>
/// 生命 / 饱食度系统（数值与规则都在这；显示在 VoxelHud，指令在 VoxelChat）：
/// 饱食度低于 20% 无法奔跑；低于 50% 生命不再自动回复；
/// 摔落超过 4 格扣血，归零后在出生点复活并重置数值。
/// 补充饱食度的正经途径是**吃狩猎来的兽肉**（按 R，见 EatFood），/eat 那条指令只是调试用的。
///
/// 生命上限不是固定的 100 —— 它由出战角色的养成数值决定（见 VoxelCharacterSheet）：
/// 1 级、什么都没装的孙琰正好是 100，所以和没有养成系统时一模一样；
/// 升级 / 突破 / 装武器圣遗物之后上限变高，同样的野怪就打不动你了。
/// 回血与摔落伤害按上限的比例结算，所以这两条规则不会被升级冲淡。
/// </summary>
public class VoxelStats : MonoBehaviour
{
    public const float Max              = 100f;   // 生命的基准值（饱食度也拿它当满值）
    public const float NoSprintBelow    = 20f;    // 低于 20% 无法奔跑
    public const float NoRegenBelow     = 50f;    // 低于 50% 生命不再自动回复
    public const float RegenPerSecond   = 2f;     // 回血速度（按上限比例）
    public const float HungerPerSecond  = 0.25f;  // 饱食度基础消耗
    public const float SprintCost       = 1.0f;   // 奔跑时的额外消耗
    public const float SafeFallBlocks   = 4f;     // 4 格以内摔不伤
    public const float DamagePerBlock   = 5f;     // 每超出 1 格的伤害（按上限比例）

    public float Health { get; private set; } = Max;
    public float Hunger { get; private set; } = Max;

    /// <summary>生命上限：由出战角色的养成数值决定（没接角色系统时就是原来的 100）</summary>
    public float MaxHealth => VoxelCharacterSheet.ActiveMaxHealth;

    /// <summary>当前上限相对基准（100）的倍数：回血、摔落这些按比例结算的都跟着它走</summary>
    public float HealthScale => MaxHealth / Max;

    float _appliedMax = Max;      // 上次同步时的上限（升级 / 换出战角色之后要按比例挪血）

    /// <summary>场上那一份（七天神像走近回满生命 / 体力时要用）</summary>
    public static VoxelStats Instance { get; private set; }

    /// <summary>饱食度够不够奔跑（VoxelPlayer 每帧问它）</summary>
    public bool CanSprint => Hunger >= NoSprintBelow;

    VoxelPlayer _player;

    public void Init(VoxelPlayer player)
    {
        _player = player;
        Instance = this;
    }

    void Update()
    {
        SyncMaxHealth();

        // 开始面板 / 对话框打开时视为暂停：不消耗也不回复
        if (_player == null || Cursor.lockState != CursorLockMode.Locked) return;

        float drain = HungerPerSecond;
        if (_player.IsSprinting) drain += SprintCost;
        SetHunger(Hunger - drain * Time.deltaTime);

        // 饱食度不低于 50% 才缓慢回血（按上限比例，升级之后回血速度不会显得没用）
        if (Hunger >= NoRegenBelow && Health < MaxHealth)
            Health = Mathf.Min(MaxHealth, Health + RegenPerSecond * HealthScale * Time.deltaTime);
    }

    /// <summary>出战角色 / 等级 / 装备一变，上限就跟着变：血条按比例保留，
    /// 不会出现"刚升级反而只剩一丝血"这种事</summary>
    void SyncMaxHealth()
    {
        float max = MaxHealth;
        if (Mathf.Approximately(max, _appliedMax)) return;

        Health = _appliedMax > 0.01f ? Mathf.Clamp(Health / _appliedMax * max, 0f, max) : max;
        _appliedMax = max;
    }

    public void Eat()
    {
        SetHunger(Max);
        VoxelChat.Announce("吃饱了，饱食度已恢复到 100");
    }

    /// <summary>
    /// 吃一份食物：回 amount 点饱食度（狩猎来的兽肉走这里，见 VoxelInteraction.EatMeat）。
    /// 返回"到底吃下去没有" —— 已经吃饱就是 false，调用方据此**不**扣掉那块肉。
    /// </summary>
    public bool EatFood(float amount, string name)
    {
        if (amount <= 0f) return false;

        if (Hunger >= Max - 0.01f)
        {
            VoxelChat.Announce("还饱着，吃不下" + name + "（饱食度 " + Hunger.ToString("F0") + "/100）");
            return false;
        }

        SetHunger(Hunger + amount);
        VoxelChat.Announce("吃了一块" + name + "，饱食度 " + Hunger.ToString("F0") + "/100");
        return true;
    }

    /// <summary>七天神像的祝福：生命与体力（饱食度）一起回满。
    /// 返回"本来有缺"——缺了才播报，站在神像前不会每帧刷一句</summary>
    public bool Bless()
    {
        bool needed = Health < MaxHealth - 0.01f || Hunger < Max - 0.01f;
        Health = MaxHealth;
        SetHunger(Max);
        return needed;
    }

    public void ApplyFallDamage(float fallBlocks)
    {
        TakeDamage((fallBlocks - SafeFallBlocks) * DamagePerBlock * HealthScale, "摔落");
    }

    public void TakeDamage(float amount, string cause)
    {
        if (amount <= 0f || Health <= 0f) return;

        // 出战角色的防御力在这里生效（没接角色系统就是 0 减伤）
        amount *= 1f - VoxelCharacterSheet.ActiveDamageReduction;
        if (amount <= 0f) return;

        Health = Mathf.Max(0f, Health - amount);
        VoxelChat.Announce("受到 " + amount.ToString("F0") + " 点伤害（" + cause + "）");

        if (Health <= 0f) Die();
    }

    void Die()
    {
        VoxelChat.Announce("你死了！已在出生点复活，生命与饱食度已重置");
        Health = MaxHealth;
        Hunger = Max;
        _player.Respawn();
    }

    void SetHunger(float value)
    {
        bool couldSprint = CanSprint;
        bool couldRegen  = Hunger >= NoRegenBelow;

        Hunger = Mathf.Clamp(value, 0f, Max);

        // 只在跨过阈值的瞬间提示一次
        if (couldSprint && !CanSprint)
            VoxelChat.Announce("你太饿了，跑不动了（输入 /eat 恢复饱食度）");
        else if (couldRegen && Hunger < NoRegenBelow)
            VoxelChat.Announce("饱食度不足一半，生命不再自动回复");
    }
}
