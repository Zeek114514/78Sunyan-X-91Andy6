using UnityEngine;

/// <summary>
/// 生命 / 饱食度系统（数值与规则都在这；显示在 VoxelHud，指令在 VoxelChat）：
/// 饱食度低于 20% 无法奔跑；低于 50% 生命不再自动回复；
/// 摔落超过 4 格扣血，归零后在出生点复活并重置数值。
/// </summary>
public class VoxelStats : MonoBehaviour
{
    public const float Max              = 100f;
    public const float NoSprintBelow    = 20f;    // 低于 20% 无法奔跑
    public const float NoRegenBelow     = 50f;    // 低于 50% 无法自动回复生命
    public const float RegenPerSecond   = 2f;     // 回血速度
    public const float HungerPerSecond  = 0.25f;  // 饱食度基础消耗
    public const float SprintCost       = 1.0f;   // 奔跑时的额外消耗
    public const float SafeFallBlocks   = 4f;     // 4 格以内摔不伤
    public const float DamagePerBlock   = 5f;     // 每超出 1 格的伤害

    public float Health { get; private set; } = Max;
    public float Hunger { get; private set; } = Max;

    /// <summary>饱食度够不够奔跑（VoxelPlayer 每帧问它）</summary>
    public bool CanSprint => Hunger >= NoSprintBelow;

    VoxelPlayer _player;

    public void Init(VoxelPlayer player) => _player = player;

    void Update()
    {
        // 开始面板 / 对话框打开时视为暂停：不消耗也不回复
        if (_player == null || Cursor.lockState != CursorLockMode.Locked) return;

        float drain = HungerPerSecond;
        if (_player.IsSprinting) drain += SprintCost;
        SetHunger(Hunger - drain * Time.deltaTime);

        // 饱食度不低于 50% 才缓慢回血
        if (Hunger >= NoRegenBelow && Health < Max)
            Health = Mathf.Min(Max, Health + RegenPerSecond * Time.deltaTime);
    }

    public void Eat()
    {
        SetHunger(Max);
        VoxelChat.Announce("吃饱了，饱食度已恢复到 100");
    }

    public void ApplyFallDamage(float fallBlocks)
    {
        TakeDamage((fallBlocks - SafeFallBlocks) * DamagePerBlock, "摔落");
    }

    public void TakeDamage(float amount, string cause)
    {
        if (amount <= 0f || Health <= 0f) return;

        Health = Mathf.Max(0f, Health - amount);
        VoxelChat.Announce("受到 " + amount.ToString("F0") + " 点伤害（" + cause + "）");

        if (Health <= 0f) Die();
    }

    void Die()
    {
        VoxelChat.Announce("你死了！已在出生点复活，生命与饱食度已重置");
        Health = Max;
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
