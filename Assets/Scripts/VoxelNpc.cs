using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 场景 NPC：运行时用小方块拼一个方块小人，复用世界的顶点色着色器（雾效一致）。
/// 平时：在家附近游荡、闲站时看玩家、跑去捡掉在地上的原石（捡到的算存粮，数据里可关）。
/// 数据里填了追击目标就去追：目标进 chaseRange 走过去，进 sprintRange 冲刺，贴身出手；
/// 目标被打倒后先退到 retreatRange 以外，再回去过自己的日子；
/// 填了躲避目标就躲：对方靠近就让开，对方在追就快跑。
/// 生存：有自己的血量（掉到 0 播倒下动作、原地不动，血量回到 5 点以上才起身）和可选的饥饿条
/// （追击时大幅消耗；低于 20% 就退出追击、跑不动，也不再闲逛——身上有原石就吃一颗，没有就站着等回饱）。
/// Resources 里放了带骨骼动画的角色模型就换成模型外形，并用它自带的站立/走/跑动画；
/// 模型或动画控制器缺失时，仍然用下面拼的方块小人。
/// </summary>
public class VoxelNpc : MonoBehaviour
{
    /// <summary>场上所有 NPC（NPC 之间按名字互相找对方用，比如孙琰找 Andy）</summary>
    public static readonly List<VoxelNpc> All = new List<VoxelNpc>();

    /// <summary>这个 NPC 的数据：名字、行为参数、模型外观。生成时由 VoxelBootstrap 填好</summary>
    public VoxelNpcData data = VoxelNpcData.SunYan();

    // 下面都从数据里现取，实例里不留副本，改数据就是改这个 NPC
    string displayName            => data.displayName;
    float  noticeRange            => data.noticeRange;
    float  labelRange             => data.labelRange;
    float  walkSpeed              => data.walkSpeed;
    float  wanderRange            => data.wanderRange;
    float  gemSeekRange           => data.gemSeekRange;
    string modelResourcePath      => data.modelResourcePath;
    string controllerResourcePath => data.controllerResourcePath;
    float  modelHeight            => data.modelHeight;
    float  modelYawOffset         => data.modelYawOffset;
    bool   useUrpLitMaterial      => data.useUrpLitMaterial;
    float  modelSmoothness        => data.modelSmoothness;

    // 青布袍小人的配色（gamma 空间，烘焙时统一转 linear）
    static readonly Color Skin  = new Color32(0xe8, 0xc3, 0x9a, 0xff);
    static readonly Color Robe  = new Color32(0x4c, 0x7f, 0x72, 0xff);
    static readonly Color Pants = new Color32(0x39, 0x3a, 0x45, 0xff);
    static readonly Color Hair  = new Color32(0x2e, 0x24, 0x1e, 0xff);
    static readonly Color Eye   = new Color32(0x20, 0x18, 0x14, 0xff);

    const float Gravity = 28f;

    // 生存数值（照玩家那套改的）
    const float HungerPerSecond      = 0.25f;   // 平时饱食度消耗（和玩家一样）
    const float ChaseHungerPerSecond = 4f;      // 追击时的额外消耗（大幅：满饱食度够追二十来秒）
    const float LowHungerFloor       = 20f;     // 低于这个饱食度：退出追击、冲刺不动，也不再闲逛（翻存粮吃）
    const float GemHungerValue       = 30f;     // 回一次体力恢复多少饱食度
    const int   EatGemCost           = 160;     // 回一次体力花多少原石（和原神抽一次一个价）
    const float RegenDelay           = 10f;     // 受伤后 / 饿到原地不动后，多久才开始回复
    const float HealthRegenPerSecond = 2f;      // 回血速度（缓慢）
    const float HungerRegenPerSecond = 2f;      // 原地不动时的回饱食度速度（缓慢）
    const float SprintMultiplier     = 1.6f;    // 冲刺 = 走路速度 ×1.6（和追原石的小跑一样）
    const float GetUpHealth          = 5f;      // 血回到这个数以上就从倒地状态起来
    const float WaterAvoidMargin     = 0.8f;    // 离水面这么近就当作"水边"：NPC 不往里走，免得走进湖里出不来

    /// <summary>这一帧在干什么（每帧重算，见 DecideState）</summary>
    enum NpcState { Idle, Walk, Gem, Chase, Flee, Retreat, Starve, Ashore, Downed }

    OpenWorld _world;
    Transform _player;
    CharacterController _controller;
    Transform _voxelBody;              // 代码拼的方块身体，换成模型后整体隐藏
    Transform _labelTop;               // 名牌锚点（方块头顶 / 模型头顶）
    Transform _leftArm, _rightArm, _leftLeg, _rightLeg;
    Animator _animator;                // 模型自带动画；用方块外形时为空
    Camera _camera;
    GUIStyle _label;

    // 动画状态机参数：0 站立 / 1 走 / 2 跑 / 3 倒地
    // （控制器里还留着 4 挨打状态，但暂时不驱动：挨打动画会打断走 / 跑循环，看着像动作播到一半被掐断）
    static readonly int MotionParam = Animator.StringToHash("Motion");

    Vector2Int _home;              // 游荡中心（出生位置）
    Vector3 _target;
    Vector3 _lastPos;
    float _verticalVelocity = -2f;
    float _idleTimer = 1f;         // 开局先站一会儿
    float _walkPhase;
    float _stuckTime;
    bool _walking;

    VoxelGem _gem;                 // 正在追的原石
    float _gemCooldown;            // 追不到时的冷静期
    int _gems;                     // 手上存着的原石（出生自带一批，捡到的会累加；饿了花原石换体力）

    // 战斗与生存
    NpcState _state;
    VoxelNpc _chaseTarget;         // 数据里填了名字就在场上找它
    VoxelNpc _fleeFrom;
    float _health;
    float _hunger;
    float _noDamageTime;           // 距上次受伤过了多久
    float _attackCooldown;
    float _starveTime;             // 饿到原地不动站了多久
    string _lastClipName;          // 自检用：上一次看到的播放剪辑名
    bool _fleeing;                 // 躲避开关（两个阈值做迟滞，免得来回抖）
    bool _downAnnounced;           // 倒地播报只发一次，起身时清掉

    public void Init(Material material, Transform player, OpenWorld world)
    {
        _world = world;
        _player = player;
        _camera = Camera.main;
        _home = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.z));

        // 碰撞统一交给根上的胶囊体
        var cc = gameObject.AddComponent<CharacterController>();
        cc.height     = 1.9f;
        cc.radius     = 0.3f;
        cc.center     = new Vector3(0f, 0.95f, 0f);
        cc.slopeLimit = 90f;
        cc.stepOffset = 1.05f;      // 1 格台阶直接迈上去
        cc.skinWidth  = 0.02f;
        _controller = cc;

        _health = data.maxHealth;
        _hunger = data.maxHunger;
        _gems   = data.startGems;      // 出生时身上带的原石（孙琰默认 1600）

        BuildBody(material);
        TryUseModel();
    }

    void OnEnable()  => All.Add(this);
    void OnDisable() => All.Remove(this);

    // ---------- 给别的脚本 / 别的 NPC 用 ----------

    /// <summary>血量（0 ~ data.maxHealth）</summary>
    public float Health => _health;
    /// <summary>饱食度（0 ~ data.maxHunger）；没有饥饿条的 NPC 一直是满的</summary>
    public float Hunger => _hunger;
    /// <summary>正在追人（被追的人靠它决定是让开还是快跑）</summary>
    public bool Chasing => _state == NpcState.Chase;
    /// <summary>倒地了（血量掉到 0，回到 5 点以上才起身）</summary>
    public bool Downed => _health < GetUpHealth;

    /// <summary>按名字找场上的 NPC（NPC 之间互相找对方用）</summary>
    public static VoxelNpc Find(string displayName)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].data.displayName == displayName) return All[i];
        return null;
    }

    /// <summary>被打：扣血并重新开始算回血倒计时；打到 0 就倒地不动</summary>
    public void TakeDamage(float amount, string cause)
    {
        if (amount <= 0f || _health <= 0f) return;

        _health = Mathf.Max(0f, _health - amount);
        _noDamageTime = 0f;
        VoxelChat.Announce(cause + " 打了 " + displayName + " 一下（生命 -" + amount.ToString("F0") +
                           "，剩 " + _health.ToString("F0") + "）");

        if (_health <= 0f)
        {
            _downAnnounced = true;
            _walking = false;
            _fleeing = false;
            _gem = null;
            VoxelChat.Announce(displayName + " 被打倒了，倒地不动");
        }
    }

    void Update()
    {
        if (_controller == null || _world == null || _player == null) return;

        float dt = Time.deltaTime;

        ApplyGravity();
        TickHealth(dt);
        ResolveOthers();

        // 先定这一帧干什么（依赖当前血量和饱食度），再按状态行动
        NpcState previous = _state;
        _state = DecideState(dt);
        TickHunger(dt);

        switch (_state)
        {
            case NpcState.Downed:  DownedStep();    break;
            case NpcState.Ashore:  AshoreStep(dt);  break;
            case NpcState.Flee:    FleeStep(dt);    break;
            case NpcState.Retreat: RetreatStep(dt); break;
            case NpcState.Chase:   ChaseStep(dt);   break;
            case NpcState.Starve:  StarveStep(dt);  break;
            case NpcState.Gem:     GemStep();       break;
            case NpcState.Walk:    WalkStep();      break;
            default:               IdleStep();      break;
        }

        // 刚决定走开时说一声，方便一眼看出这是打倒之后的反应
        if (_state == NpcState.Retreat && previous != NpcState.Retreat)
            VoxelChat.Announce(displayName + " 转身走开了");

        Animate();
    }

    // ---------- 状态机 ----------

    /// <summary>这一帧干什么：倒地 &gt; 上岸 &gt; 走开 &gt; 躲避 &gt; 追击 &gt; 饱食度过低 &gt; 捡原石 &gt; 游荡 / 发呆</summary>
    NpcState DecideState(float dt)
    {
        // 血量没回到 5 点以上就一直躺着
        if (_health < GetUpHealth) return NpcState.Downed;

        // 站在水里 / 水边：先想办法上岸，别的事都往后放
        if (NearWater(transform.position.x, transform.position.z, 0.35f)) return NpcState.Ashore;

        // 追击目标被打倒了：先离他远一点（至少 retreatRange），别守在人家旁边
        if (_chaseTarget != null && _chaseTarget.Downed &&
            FlatDistance(_chaseTarget.transform.position) < data.retreatRange)
            return NpcState.Retreat;

        // 躲避：对方进 keepDistance 就让开，退到 fleeStopRange 外再停（两个阈值，免得来回抖）
        if (_fleeFrom != null)
        {
            float d = FlatDistance(_fleeFrom.transform.position);

            if (_fleeing)
            {
                if (d >= data.fleeStopRange) _fleeing = false;
            }
            else if (d <= data.keepDistance)
            {
                _fleeing = true;
            }

            if (_fleeing) return NpcState.Flee;
        }

        // 追击：目标在范围内就走过去；自己饿到 20% 以下不追，倒地的目标也不打
        if (_chaseTarget != null && !_chaseTarget.Downed && CanChase &&
            FlatDistance(_chaseTarget.transform.position) <= data.chaseRange)
            return NpcState.Chase;

        // 饱食度过低：不闲逛了，原地翻存粮吃；没存粮就站着等回饱
        if (data.usesHunger && _hunger < LowHungerFloor) return NpcState.Starve;

        // 捡原石（数据里关了就不捡）
        if (data.seekGems)
        {
            _gemCooldown -= dt;
            if (_gem == null && _gemCooldown <= 0f)
                _gem = VoxelGem.Nearest(transform.position, gemSeekRange);

            if (_gem != null) return NpcState.Gem;
        }

        return _walking ? NpcState.Walk : NpcState.Idle;
    }

    /// <summary>还追得动吗：有饥饿条的饿到 20% 以下就追不动了（也就没力气冲刺）</summary>
    bool CanChase => !data.usesHunger || _hunger >= LowHungerFloor;

    /// <summary>追到冲刺距离了没有（冲刺要饱食度够）</summary>
    bool Sprinting =>
        CanChase && _chaseTarget != null &&
        FlatDistance(_chaseTarget.transform.position) <= data.sprintRange;

    /// <summary>数据里写的追击 / 躲避目标，按名字在场上找（生成顺序不定，找到就一直用）</summary>
    void ResolveOthers()
    {
        if (_chaseTarget == null && !string.IsNullOrEmpty(data.chaseTargetName))
            _chaseTarget = Find(data.chaseTargetName);
        if (_fleeFrom == null && !string.IsNullOrEmpty(data.fleeFromName))
            _fleeFrom = Find(data.fleeFromName);
    }

    // ---------- 生存 ----------

    /// <summary>受伤 10 秒后开始缓慢回血；倒地时也靠它把血抬回 5 点以上好起身</summary>
    void TickHealth(float dt)
    {
        _noDamageTime += dt;
        if (_noDamageTime < RegenDelay || _health >= data.maxHealth) return;

        _health = Mathf.Min(data.maxHealth, _health + HealthRegenPerSecond * dt);

        if (_downAnnounced && _health >= GetUpHealth)
        {
            _downAnnounced = false;
            VoxelChat.Announce(displayName + " 缓过来了，又站了起来");
        }
    }

    /// <summary>按当前状态扣饱食度；饿到原地不动那个状态自己在 StarveStep 里往回加</summary>
    void TickHunger(float dt)
    {
        if (!data.usesHunger || _state == NpcState.Starve) return;

        _starveTime = 0f;
        float drain = HungerPerSecond;
        if (_state == NpcState.Chase) drain += ChaseHungerPerSecond;   // 追击时大幅消耗
        SetHunger(_hunger - drain * dt);
    }

    /// <summary>改饱食度；跨过 20% 阈值时提示一次（和玩家那边的做法一样）。
    /// 原石够换体力就不提示了——他会立刻花掉接着追，提示只会刷屏</summary>
    void SetHunger(float value)
    {
        bool couldChase = _hunger >= LowHungerFloor;
        _hunger = Mathf.Clamp(value, 0f, data.maxHunger);

        if (couldChase && _hunger < LowHungerFloor && _gems < EatGemCost)
            VoxelChat.Announce(displayName + " 饿得追不动了，原石不够换体力，先原地歇着");
    }

    // ---------- 追击 ----------

    /// <summary>追击：走过去 → 进冲刺距离就加速 → 贴身按间隔出手</summary>
    void ChaseStep(float dt)
    {
        Vector3 to = _chaseTarget.transform.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist < 0.0001f) return;

        Vector3 dir = to / dist;
        TurnToward(dir, 10f);

        // 边追边打：贴上了也不停下，否则对方一跑就脱开
        Vector3 move = PickWalkableDirection(dir);
        if (move != Vector3.zero)
            _controller.Move(move * (Sprinting ? walkSpeed * SprintMultiplier : walkSpeed) * dt);

        _attackCooldown -= dt;
        if (dist <= data.attackRange && _attackCooldown <= 0f)
        {
            _attackCooldown = data.attackInterval;
            _chaseTarget.TakeDamage(data.attackDamage, displayName);
        }
    }

    // ---------- 躲避 ----------

    /// <summary>逃跑：背离对方走；对方在追就用逃跑速度，只是靠近就正常走开</summary>
    void FleeStep(float dt)
    {
        Vector3 dir = transform.position - _fleeFrom.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward; else dir.Normalize();

        dir = PickWalkableDirection(dir);
        if (dir == Vector3.zero) return;                  // 四面都走不通，只能站住

        float speed = _fleeFrom.Chasing ? data.fleeSpeed : walkSpeed;
        _controller.Move(dir * speed * dt);
        TurnToward(dir, 8f);

        // 被地形 / 方块卡住超过一秒 → 当作躲够了，回去干别的
        if ((transform.position - _lastPos).sqrMagnitude < 0.0004f)
        {
            _stuckTime += dt;
            if (_stuckTime > 1f)
            {
                _stuckTime = 0f;
                _fleeing = false;
            }
        }
        else
        {
            _stuckTime = 0f;
            _lastPos = transform.position;
        }
    }

    /// <summary>从几个候选方向里挑第一个能走的：正前方不行就往两边偏，偏到 ±80 度还不行就返回零向量</summary>
    Vector3 PickWalkableDirection(Vector3 dir)
    {
        for (int i = 0; i < 5; i++)
        {
            float angle = (i + 1) / 2 * 40f * (i % 2 == 1 ? 1f : -1f);   // 0 / +40 / -40 / +80 / -80
            Vector3 d = i == 0 ? dir : Quaternion.Euler(0f, angle, 0f) * dir;

            Vector3 ahead = transform.position + d * 0.8f;
            if (Walkable(Mathf.FloorToInt(ahead.x), Mathf.FloorToInt(ahead.z))) return d;
        }
        return Vector3.zero;
    }

    // ---------- 目标倒地后走开 ----------

    /// <summary>走开：背离被打倒的目标走，退到 retreatRange 以外再回去过自己的日子</summary>
    void RetreatStep(float dt)
    {
        Vector3 dir = transform.position - _chaseTarget.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = -transform.forward; else dir.Normalize();

        Vector3 move = PickWalkableDirection(dir);
        if (move == Vector3.zero) return;         // 四面都走不通，只能先站着

        _controller.Move(move * walkSpeed * dt);
        TurnToward(move, 6f);
    }

    // ---------- 饿到极限 ----------

    /// <summary>饱食度低于 20%：原地不动，用原石换一次体力（花 EatGemCost、恢复 GemHungerValue）；
    /// 原石不够就站够 10 秒，再按 2 点/秒回饱</summary>
    void StarveStep(float dt)
    {
        if (_gems >= EatGemCost)
        {
            _gems -= EatGemCost;
            SetHunger(_hunger + GemHungerValue);
            VoxelChat.Announce(displayName + " 花 " + EatGemCost + " 原石换了点体力（饱食度 +" +
                               GemHungerValue.ToString("F0") + "，还剩 " + _gems + " 原石）");
            return;
        }

        _starveTime += dt;
        if (_starveTime >= RegenDelay)
            SetHunger(_hunger + HungerRegenPerSecond * dt);

        _walking = false;      // 原地不动
        _gem = null;
    }

    // ---------- 倒地 ----------

    /// <summary>倒地：躺在原地不动，播模型自带的倒地动画（defeat / fall，单次播完停在最后一帧），
    /// 血回到 5 点以上自动起身</summary>
    void DownedStep()
    {
        _walking = false;
        _fleeing = false;
        _gem = null;
    }

    /// <summary>两点之间的水平距离（追击 / 躲避都按这个算）</summary>
    float FlatDistance(Vector3 other)
    {
        Vector3 to = other - transform.position;
        to.y = 0f;
        return to.magnitude;
    }

    // ---------- 捡原石 ----------

    /// <summary>主动跑去捡掉落的原石，谁先碰到归谁</summary>
    void GemStep()
    {
        // 目标没了（被玩家抢先捡走）→ 回到日常生活
        if (_gem == null || !_gem.isActiveAndEnabled)
        {
            _gem = null;
            _walking = false;
            _idleTimer = Random.Range(0.5f, 1.5f);
            return;
        }

        Vector3 to = _gem.RestPosition - transform.position;
        to.y = 0f;

        if (to.magnitude < 1.05f)
        {
            _walking = false;
            if (_gem.Settled)
            {
                _gem.CollectByNpc();
                _gem = null;
                _gems++;
                _idleTimer = Random.Range(1f, 2.5f);
                VoxelChat.Announce(displayName + " 捡走了一颗原石");
            }
            else
            {
                TurnToward(to, 8f);       // 还在空中飘，原地等它落下来
            }
            return;
        }

        Vector3 dir = to.normalized;
        Vector3 ahead = transform.position + dir * 0.8f;
        if (!Walkable(Mathf.FloorToInt(ahead.x), Mathf.FloorToInt(ahead.z)))
        {
            _gem = null;                  // 隔着水/悬崖过不去，先不管这颗
            _gemCooldown = 6f;
            _walking = false;
            _idleTimer = Random.Range(0.5f, 1.5f);
            return;
        }

        _walking = true;
        _controller.Move(dir * (walkSpeed * SprintMultiplier) * Time.deltaTime);   // 追原石时小跑
        TurnToward(dir, 8f);
    }

    // ---------- 游荡 AI ----------

    void IdleStep()
    {
        _idleTimer -= Time.deltaTime;

        // 闲站时看着玩家
        Vector3 to = _player.position - transform.position;
        if (to.magnitude < noticeRange)
            TurnToward(to, 3f);

        if (_idleTimer <= 0f) DecideNext();
    }

    void WalkStep()
    {
        Vector3 to = _target - transform.position;
        to.y = 0f;

        if (to.magnitude < 0.2f)                    // 到达 → 歇一会
        {
            _walking = false;
            _idleTimer = Random.Range(1.5f, 4f);
            return;
        }

        Vector3 dir = to.normalized;
        Vector3 ahead = transform.position + dir * 0.8f;
        if (!Walkable(Mathf.FloorToInt(ahead.x), Mathf.FloorToInt(ahead.z)))
        {
            _walking = false;                       // 前方是水/高墙/悬崖，换地方去
            _idleTimer = Random.Range(0.3f, 1f);
            return;
        }

        _controller.Move(dir * walkSpeed * Time.deltaTime);
        TurnToward(dir, 6f);

        // 被方块或玩家卡住超过一秒 → 放弃这个目标
        if ((transform.position - _lastPos).sqrMagnitude < 0.0004f)
        {
            _stuckTime += Time.deltaTime;
            if (_stuckTime > 1f)
            {
                _stuckTime = 0f;
                _walking = false;
                _idleTimer = Random.Range(0.5f, 2f);
            }
        }
        else
        {
            _stuckTime = 0f;
            _lastPos = transform.position;
        }
    }

    /// <summary>决定下一步：四成概率发呆，否则去家附近挑个落脚点</summary>
    void DecideNext()
    {
        if (Random.value < 0.4f)
        {
            _idleTimer = Random.Range(1.5f, 4f);
            return;
        }

        for (int i = 0; i < 12; i++)
        {
            int x = _home.x + Mathf.RoundToInt(Random.Range(-wanderRange, wanderRange));
            int z = _home.y + Mathf.RoundToInt(Random.Range(-wanderRange, wanderRange));

            if (!OpenWorld.InBounds(x + 0.5f, z + 0.5f)) continue;

            float gy = _world.SurfaceY(x + 0.5f, z + 0.5f);
            if (NearWater(x + 0.5f, z + 0.5f, WaterAvoidMargin * 2f)) continue;   // 别往水边去
            if (OpenWorld.Slope(x + 0.5f, z + 0.5f) > 0.4f) continue;   // 太陡的坡上不去
            if (Mathf.Abs(gy - transform.position.y) > 2.5f) continue;  // 落差太大不去

            _target = new Vector3(x + 0.5f, gy, z + 0.5f);
            _walking = true;
            return;
        }

        _idleTimer = Random.Range(1f, 3f);   // 一时没找到合适的地方，歇会再想
    }

    /// <summary>这一点现在能不能走：不是水边、不算陡坡，上升不超过 1 米、下落不超过 2 米</summary>
    bool Walkable(int x, int z)
    {
        if (!OpenWorld.InBounds(x + 0.5f, z + 0.5f)) return false;
        if (NearWater(x + 0.5f, z + 0.5f, WaterAvoidMargin)) return false;
        if (OpenWorld.Slope(x + 0.5f, z + 0.5f) > 0.4f) return false;

        float gy = _world.SurfaceY(x + 0.5f, z + 0.5f);
        return gy - transform.position.y <= 1.2f && transform.position.y - gy <= 2.2f;
    }

    /// <summary>这一带算不算"水边"：水面 + margin 以内都算，四个方向各探一点，
    /// 免得出现"站在岸上、下一步就是水"的情况。
    /// 自己已经站在桥面这类高处（比水面高 1.5 米以上）时不算 —— 否则他们连桥都不敢过。</summary>
    bool NearWater(float x, float z, float margin)
    {
        if (transform.position.y > OpenWorld.WaterLevel + 1.5f) return false;

        if (OpenWorld.Height(x, z) < OpenWorld.WaterLevel + margin) return true;

        const float probe = 1.5f;
        return OpenWorld.Height(x + probe, z) < OpenWorld.WaterLevel + margin
            || OpenWorld.Height(x - probe, z) < OpenWorld.WaterLevel + margin
            || OpenWorld.Height(x, z + probe) < OpenWorld.WaterLevel + margin
            || OpenWorld.Height(x, z - probe) < OpenWorld.WaterLevel + margin;
    }

    /// <summary>站在水里 / 水边过不去了：朝"8 个方向里地势最高"的那个方向走出来（相当于爬上岸）。
    /// 八个方向都不比脚下高就朝城的方向走，那边一定是干的。</summary>
    void AshoreStep(float dt)
    {
        Vector3 pos = transform.position;
        Vector3 best = Vector3.zero;
        float bestH = OpenWorld.Height(pos.x, pos.z);

        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            float px = pos.x + Mathf.Cos(a) * 2.5f;
            float pz = pos.z + Mathf.Sin(a) * 2.5f;
            if (!OpenWorld.InBounds(px, pz)) continue;

            float hh = OpenWorld.Height(px, pz);
            if (hh > bestH + 0.05f)
            {
                bestH = hh;
                best = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
        }

        if (best == Vector3.zero)
        {
            best = new Vector3(MondstadtTown.CenterX - pos.x, 0f, MondstadtTown.CenterZ - pos.z);
            if (best.sqrMagnitude < 0.01f) best = Vector3.forward;
            best.Normalize();
        }

        _controller.Move(best * (walkSpeed * 1.3f) * dt);   // 上岸时走快一点，别在水边磨蹭
        TurnToward(best, 8f);

        _walking = false;
        _gem = null;
    }

    void TurnToward(Vector3 dir, float speed)
    {
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, yaw, 0f), Time.deltaTime * speed);
    }

    void ApplyGravity()
    {
        if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
        _verticalVelocity -= Gravity * Time.deltaTime;
        _controller.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
    }

    // ---------- 动画 ----------

    void Animate()
    {
        // 用上模型就交给模型自带的动画，方块外形才需要手动摆四肢
        if (_animator != null)
        {
            _animator.SetInteger(MotionParam, MotionState());
            LogClipIfChanged();
            return;
        }

        if (Moving)
        {
            _walkPhase += Time.deltaTime * walkSpeed * 3.2f;
            float leg = Mathf.Sin(_walkPhase) * 24f;
            float arm = Mathf.Sin(_walkPhase) * 18f;
            _leftLeg.localRotation  = Quaternion.Euler( leg, 0f, 0f);
            _rightLeg.localRotation = Quaternion.Euler(-leg, 0f, 0f);
            _leftArm.localRotation  = Quaternion.Euler(-arm, 0f, 0f);
            _rightArm.localRotation = Quaternion.Euler( arm, 0f, 0f);
        }
        else
        {
            _leftLeg.localRotation  = Quaternion.identity;
            _rightLeg.localRotation = Quaternion.identity;
            float arm = Mathf.Sin(Time.time * 1.6f) * 8f;    // 闲站时手臂轻摆
            _leftArm.localRotation  = Quaternion.Euler( arm, 0f, 0f);
            _rightArm.localRotation = Quaternion.Euler(-arm, 0f, 0f);
        }
    }

    /// <summary>自检用：实际播放的剪辑变了就写一条日志。
    /// 一眼就能看出"走 / 跑到底绑的是哪段剪辑"，出事时不用猜。</summary>
    void LogClipIfChanged()
    {
        if (Time.frameCount % 20 != 0) return;      // 每 0.3 秒查一次

        string clip = CurrentClipName();
        if (clip == _lastClipName) return;

        _lastClipName = clip;
        Debug.Log($"[VoxelNpc] {displayName} Motion={MotionState()} 实际播放剪辑「{clip}」" +
                  $"（该状态期望：{ExpectedClipHint(MotionState())}）");
    }

    /// <summary>当前正在播的剪辑名（拿不到就返回"(无)"）</summary>
    string CurrentClipName()
    {
        if (_animator == null) return "(无)";
        var infos = _animator.GetCurrentAnimatorClipInfo(0);
        if (infos.Length == 0 || infos[0].clip == null) return "(无)";
        return infos[0].clip.name;
    }

    static string ExpectedClipHint(int motion)
    {
        switch (motion)
        {
            case 1:  return "走路";
            case 2:  return "跑步";
            case 3:  return "倒地";
            case 4:  return "挨打";
            default: return "站立";
        }
    }

    /// <summary>这一帧腿脚在动吗（走 / 捡原石 / 追 / 逃 / 走开 / 上岸 都算）</summary>
    bool Moving =>
        _state == NpcState.Walk || _state == NpcState.Gem || _state == NpcState.Ashore ||
        _state == NpcState.Chase || _state == NpcState.Flee || _state == NpcState.Retreat;

    /// <summary>当前该播哪一段：0 站立 / 1 走 / 2 跑 / 3 倒地。
    /// 倒地播模型自带的倒地剪辑（单次播完停在最后一帧）；挨打动画暂时停用（会打断走 / 跑循环）</summary>
    int MotionState()
    {
        switch (_state)
        {
            case NpcState.Downed:  return 3;
            case NpcState.Ashore:  return 1;
            case NpcState.Chase:   return Sprinting ? 2 : 1;
            case NpcState.Flee:    return _fleeFrom != null && _fleeFrom.Chasing ? 2 : 1;
            case NpcState.Retreat: return 1;      // 走开着用走的
            case NpcState.Gem:     return 2;
            case NpcState.Walk:    return 1;
            default:               return 0;
        }
    }

    // ---------- 名牌 ----------

    void OnGUI()
    {
        if (_labelTop == null || _camera == null) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;   // 开始面板/对话框时先不画
        EnsureStyle();

        Vector3 top = _labelTop.position;
        float dist = Vector3.Distance(top, _camera.transform.position);
        if (dist > labelRange) return;

        Vector3 screen = _camera.WorldToScreenPoint(top);
        if (screen.z <= 0f) return;                             // 在相机背后

        // 到达显示距离边缘时渐隐
        float alpha = Mathf.Clamp01((labelRange - dist) / 3f);
        _label.normal.textColor = new Color(1f, 1f, 1f, alpha);

        // 手上还存着原石就在名字后面记账（饿了要拿去吃）
        string text = _gems > 0
            ? displayName + "（原石×" + _gems + "）"
            : displayName;
        var rect = new Rect(screen.x - 110f, Screen.height - screen.y - 12f, 220f, 24f);
        VoxelHud.LabelShadow(rect, text, _label);
        DrawVitals(screen, alpha);
    }

    /// <summary>名牌下面画血条；有饥饿条的（孙琰）再画一条，颜色和玩家 HUD 那套一致</summary>
    void DrawVitals(Vector3 screen, float alpha)
    {
        const float cell = 8f, gap = 1f, h = 6f;
        float width = 10f * cell + 9f * gap;

        float x = screen.x - width * 0.5f;
        float y = Screen.height - screen.y + 13f;      // 名牌下面一点

        VoxelHud.SegmentedBar(x, y, _health, new Color(0.83f, 0.18f, 0.18f, alpha), cell, h);

        if (!data.usesHunger) return;

        // 饿到跑不动的临界，颜色变暗（和玩家饱食条一个规则）
        bool starving = _hunger < LowHungerFloor;
        VoxelHud.SegmentedBar(x, y + h + 2f, _hunger,
                              starving ? new Color(0.72f, 0.30f, 0.06f, alpha)
                                       : new Color(0.93f, 0.62f, 0.12f, alpha), cell, h);
    }

    // ---------- 外形 ----------

    /// <summary>Minecraft 比例：腿 0.75 + 身体 0.75 + 头 0.5，共 2 格高，脚底在原点，面朝 +Z</summary>
    void BuildBody(Material material)
    {
        // 方块零件统一挂在这个节点下，换成模型时整体隐藏
        _voxelBody = new GameObject("VoxelBody").transform;
        _voxelBody.SetParent(transform, false);

        // 腿挂在髋部枢轴下，走路时绕髋摆
        _leftLeg = Pivot("LeftLeg", new Vector3(-0.13f, 0.75f, 0f));
        Part("LegL", new Vector3(0f, -0.375f, 0f), new Vector3(0.24f, 0.75f, 0.26f), Pants, material, _leftLeg);
        _rightLeg = Pivot("RightLeg", new Vector3(0.13f, 0.75f, 0f));
        Part("LegR", new Vector3(0f, -0.375f, 0f), new Vector3(0.24f, 0.75f, 0.26f), Pants, material, _rightLeg);

        // 身体
        Part("Body", new Vector3(0f, 1.125f, 0f), new Vector3(0.5f, 0.75f, 0.28f), Robe, material);

        // 手臂挂在肩膀枢轴下，摆动时绕肩膀转
        _leftArm = Pivot("LeftArm", new Vector3(-0.38f, 1.5f, 0f));
        Part("ArmL", new Vector3(0f, -0.36f, 0f), new Vector3(0.22f, 0.72f, 0.24f), Robe, material, _leftArm);
        _rightArm = Pivot("RightArm", new Vector3(0.38f, 1.5f, 0f));
        Part("ArmR", new Vector3(0f, -0.36f, 0f), new Vector3(0.22f, 0.72f, 0.24f), Robe, material, _rightArm);

        // 头 + 头发 + 眼睛
        Part("Head", new Vector3(0f, 1.75f, 0f), new Vector3(0.5f, 0.5f, 0.5f), Skin, material);
        Part("Hair", new Vector3(0f, 1.97f, 0f), new Vector3(0.54f, 0.14f, 0.54f), Hair, material);
        Part("EyeL", new Vector3(-0.11f, 1.80f, 0.26f), new Vector3(0.08f, 0.08f, 0.04f), Eye, material);
        Part("EyeR", new Vector3( 0.11f, 1.80f, 0.26f), new Vector3(0.08f, 0.08f, 0.04f), Eye, material);

        // 名牌落在头顶上方一点（换成模型时会挪到模型头顶）
        _labelTop = new GameObject("LabelTop").transform;
        _labelTop.SetParent(transform, false);
        _labelTop.localPosition = new Vector3(0f, 2.3f, 0f);
    }

    /// <summary>加载导入好的角色模型替换方块外形；模型或控制器缺失时保留方块小人</summary>
    void TryUseModel()
    {
        var prefab = Resources.Load<GameObject>(modelResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"[VoxelNpc] 没找到角色模型 Resources/{modelResourcePath}，{displayName} 继续用方块小人。");
            return;
        }

        var controller = Resources.Load<RuntimeAnimatorController>(controllerResourcePath);
        if (controller == null)
        {
            Debug.LogWarning($"[VoxelNpc] 没找到动画控制器 Resources/{controllerResourcePath}，{displayName} 继续用方块小人。" +
                             "（菜单 VoxelDemo → 生成孙琰动画控制器 可以生成）");
            return;
        }

        var model = Instantiate(prefab, transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, modelYawOffset, 0f);
        model.transform.localScale = Vector3.one;

        // 量出模型自己的高度：缩放到设定身高，再把脚底对到 NPC 原点（原点就在地面上）
        Bounds bounds;
        if (!TryGetBounds(model, out bounds))
        {
            Debug.LogWarning($"[VoxelNpc] Resources/{modelResourcePath} 里没有可渲染的网格，{displayName} 继续用方块小人。");
            Destroy(model);
            return;
        }

        float scale = modelHeight / Mathf.Max(bounds.size.y, 0.001f);
        model.transform.localScale = Vector3.one * scale;
        model.transform.localPosition = new Vector3(0f, (transform.position.y - bounds.min.y) * scale, 0f);
        float scaledHeight = bounds.size.y * scale;

        // 模型自带的 Animator 在根节点上（剪辑路径相对它算），控制器也挂在根上
        _animator = model.GetComponent<Animator>();
        if (_animator == null) _animator = model.AddComponent<Animator>();
        _animator.runtimeAnimatorController = controller;
        _animator.applyRootMotion = false;      // 位移交给角色控制器，动画只管姿势
        _animator.SetInteger(MotionParam, MotionState());

        Debug.Log($"[VoxelNpc] {displayName} 动画就绪：Animator={(_animator != null ? "有" : "无")}，" +
                  $"Avatar={(_animator != null && _animator.avatar != null ? "有" : "无")}，" +
                  $"控制器剪辑数={(controller.animationClips != null ? controller.animationClips.Length : 0)}，" +
                  $"当前剪辑={CurrentClipName()}");

        // glTFast 的 PBR 材质在团结引擎的 URP 上会渲染成金属感 + 破碎高光，
        // 换成引擎自带的 URP/Lit：保留模型的底色贴图，法线/金属度细节舍弃
        if (useUrpLitMaterial)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogWarning("[VoxelNpc] 找不到 Universal Render Pipeline/Lit 着色器，继续用模型自带材质。");
            }
            else
            {
                int swapped = 0;
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    var mat = new Material(lit);
                    var src = r.sharedMaterial;
                    if (src != null && src.mainTexture != null)
                        mat.SetTexture("_BaseMap", src.mainTexture);
                    mat.SetFloat("_Metallic", 0f);
                    mat.SetFloat("_Smoothness", modelSmoothness);
                    r.sharedMaterial = mat;
                    swapped++;
                }
                Debug.Log($"[VoxelNpc] 已把 {swapped} 个渲染器换成 URP/Lit 材质（金属度 0，光滑度 {modelSmoothness}）。");
            }
        }

        // 名牌挪到模型头顶（用缩放后的实际身高，别让血条压在胸口）
        if (_labelTop != null) Destroy(_labelTop.gameObject);
        _labelTop = new GameObject("LabelTop").transform;
        _labelTop.SetParent(transform, false);
        _labelTop.localPosition = new Vector3(0f, scaledHeight + 0.45f, 0f);

        _voxelBody.gameObject.SetActive(false);

        Debug.Log($"[VoxelNpc] {displayName} 已换用模型 Resources/{modelResourcePath}（缩放 ×{scale:0.###}，" +
                  $"身高 {scaledHeight:0.##} 米）。");
    }

    /// <summary>模型所有渲染器合起来的世界包围盒；没有可渲染内容时返回 false</summary>
    static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            bounds = new Bounds();
            return false;
        }

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    /// <summary>建一个普通子节点当枢轴（肩膀/髋部）</summary>
    Transform Pivot(string name, Vector3 localPos)
    {
        var pivot = new GameObject(name).transform;
        pivot.SetParent(_voxelBody, false);
        pivot.localPosition = localPos;
        return pivot;
    }

    /// <summary>一个方块零件：白色 Cube 网格 + 烘焙面色 + 换成世界的材质；自带碰撞体拆掉，统一用胶囊</summary>
    Transform Part(string name, Vector3 localPos, Vector3 size, Color color, Material material, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent != null ? parent : _voxelBody, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;

        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        BakeFaceShade(go.GetComponent<MeshFilter>().mesh, color);
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go.transform;
    }

    /// <summary>把颜色按面明暗烘焙进顶点色，和 VoxelWorld 的做法一致（gamma → linear）</summary>
    static void BakeFaceShade(Mesh mesh, Color baseColor)
    {
        var normals = mesh.normals;
        var colors = new Color[normals.Length];

        for (int i = 0; i < normals.Length; i++)
        {
            Vector3 n = normals[i];
            float shade = n.y > 0.5f  ? 1.00f
                        : n.y < -0.5f ? 0.55f
                        : n.x > 0.5f  ? 0.82f
                        : n.x < -0.5f ? 0.76f
                        : n.z > 0.5f  ? 0.88f
                        : 0.72f;
            Color c = baseColor * shade;
            c.a = 1f;
            colors[i] = c.linear;
        }

        mesh.SetColors(colors);
    }

    void EnsureStyle()
    {
        if (_label != null) return;
        _label = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
    }
}
