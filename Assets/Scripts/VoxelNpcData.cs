using UnityEngine;

/// <summary>
/// 一个 NPC 的全部数据：名字、行为参数、战斗与生存数值、模型外观。
/// 场景里的 NPC 由 VoxelBootstrap 按数据运行时生成，加人改参数都改这里，VoxelNpc 只负责按数据行动。
/// </summary>
[System.Serializable]
public class VoxelNpcData
{
    // ---------- 基本信息 ----------
    public string displayName = "孙琰";

    // ---------- 行为参数 ----------
    public float noticeRange  = 8f;     // 玩家进这个范围，闲站时就看你
    public float labelRange   = 16f;    // 名牌的显示距离
    public float walkSpeed    = 2.2f;  // 走路速度（比玩家慢）
    public float wanderRange  = 10f;   // 离“家”最远的游荡半径
    public float gemSeekRange = 14f;   // 这个范围内的掉落原石会吸引他（seekGems 打开时才管用）

    // ---------- 战斗与生存 ----------
    public float maxHealth  = 100f;     // 血量上限
    public float maxHunger  = 100f;     // 饱食度上限
    public bool  usesHunger = false;    // 有没有饥饿条（像玩家那样：会消耗、饿了花原石换体力、饿到 20% 追不动）
    public bool  seekGems   = true;     // 会不会主动跑去捡原石（捡到的算存粮）
    public int   startGems  = 0;        // 出生时身上带多少原石（花原石换体力，见 VoxelNpc.EatGemCost）

    // 追击：填了 chaseTargetName 才会追人
    public string chaseTargetName = "";    // 追谁（按名字找场上的 NPC，留空就不追）
    public float  chaseRange      = 10f;   // 目标进这个半径就开始靠近（走路）
    public float  sprintRange     = 5f;    // 直线距离进这个数就冲刺
    public float  attackRange     = 1.6f;  // 冲到这个距离就出手
    public float  attackDamage    = 10f;   // 每下伤害
    public float  attackInterval  = 1.5f;  // 两下攻击之间的间隔
    public float  retreatRange    = 20f;   // 目标被打倒后退到多远：至少离他这么远再回去过自己的日子

    // 躲避：填了 fleeFromName 才会躲人
    public string fleeFromName  = "";      // 躲谁（按名字找场上的 NPC，留空就不躲）
    public float  keepDistance  = 4f;      // 对方进这个半径就主动让开
    public float  fleeStopRange = 9f;      // 退到这个距离就停下（留出被追上的余地，否则永远追不上）
    public float  fleeSpeed     = 2.6f;    // 逃跑速度（比追击者走路快、比冲刺慢）

    // ---------- 外形（角色模型）----------
    // 有导入好的模型就用模型外形 + 自带动画，找不到就回退到 VoxelNpc 里拼的方块小人
    public string modelResourcePath      = "sy/tripo_convert_b7aafa07-ebc1-458c-af70-3fe007883286";  // Resources 下的模型（不带扩展名）
    public string controllerResourcePath = "NpcSunYan/SunYanController";  // Resources 下的动画控制器
    public float  modelHeight    = 1.85f;  // 模型缩放后的身高，对齐 1.9 的胶囊
    public float  modelYawOffset = 0f;     // 模型朝向修正：模型若背对前方就填 180
    public bool   useUrpLitMaterial = false;  // 勾上：放弃模型自带 PBR 材质，用 URP/Lit 只保留底色贴图（材质再发金属色时的保险开关）
    public float  modelSmoothness  = 0.35f;  // useUrpLitMaterial 打开时的光滑度（0 哑光 ~ 1 镜面）

    /// <summary>（测试用）站着不动：不游荡、不追不躲、也不转身，只播站立动画。
    /// 浮空测试平台上那 100 个阵型 NPC 用它保持队形。</summary>
    public bool holdPosition = false;

    /// <summary>孙琰：默认值就是他本人的数据；有饥饿条、会捡原石，身上带着 1600 原石，看见 Andy 就追</summary>
    public static VoxelNpcData SunYan()
    {
        var data = new VoxelNpcData();
        data.usesHunger      = true;
        data.chaseTargetName = "Andy";
        data.startGems       = 1600;
        return data;
    }

    /// <summary>Andy：用自己的模型和动作（Resources/NpcAndy，带 idle / walk / run / box / hit / defeat 等剪辑）。
    /// 他不捡原石、没有饥饿条；孙琰靠近就让开，孙琰追上来就跑</summary>
    public static VoxelNpcData Andy()
    {
        var data = new VoxelNpcData();
        data.displayName    = "Andy";
        data.seekGems       = false;
        data.usesHunger     = false;
        data.fleeFromName   = "孙琰";

        // Andy 自己的模型与动画控制器（控制器由菜单 VoxelDemo → 生成 NPC 动画控制器 产出）
        data.modelResourcePath      = "NpcAndy/Andy";
        data.controllerResourcePath = "NpcAndy/AndyController";

        // Tripo 的 PBR 在 URP 下会渲染成抛光金属感，换成 URP/Lit + 底色贴图（想看他原本的 PBR 就改回 false）
        data.useUrpLitMaterial = true;
        return data;
    }
}
