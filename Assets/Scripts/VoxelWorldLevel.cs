using UnityEngine;

/// <summary>
/// 世界等级：跟着**出战角色的等级**走（每 10 级一档，最高 8 档），
/// 档次一变就把场上和以后刷出来的野怪血量 / 攻击力一起放大（见 VoxelMonster.ApplyDifficulty）。
///
/// 为什么要这个：养成系统上线之后，90 级角色一刀就能把丘丘人秒了，"变强"就没有意义了。
/// 原神那一套是"世界等级随冒险等阶提高，怪也跟着变强"——这里用角色等级当那根尺子，
/// 1 级 1.0 倍、10 级 1.32 倍……每档 +32%，升级之后打同一只怪要出更多刀，但伤害也确实更高。
///
/// 挂载在玩家身上，每帧只看一个整数变没变，开销可以忽略。
/// </summary>
public class VoxelWorldLevel : MonoBehaviour
{
    /// <summary>现在该是几档（没有角色系统时是 0 档 = 原始难度）</summary>
    public static int Level
    {
        get
        {
            var state = VoxelCharacterSheet.ActiveState;
            return state != null ? Mathf.Clamp((state.Level - 1) / 10, 0, 8) : 0;
        }
    }

    /// <summary>野怪血量 / 攻击力的倍率</summary>
    public static float Scale => 1f + 0.32f * Level;

    int _applied = -1;

    void Start() => Apply(announce: false);

    void Update()
    {
        if (Level != _applied) Apply(announce: true);
    }

    void Apply(bool announce)
    {
        _applied = Level;
        VoxelMonster.ApplyDifficulty(Scale);

        if (!announce) return;      // 开局那次不播报，免得一进场就刷一行

        VoxelChat.Announce($"世界等级 {Level}（野怪强度 ×{Scale:0.00}）");
    }
}
