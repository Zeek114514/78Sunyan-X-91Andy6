using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 野外动物（原创低模，和丘丘人 / 史莱姆用的是同一套"代码拼方块 + 顶点色"的做法）：
///   · 六种：野兔 / 鹿 / 野猪 / 岩羊 / 白鹭 / 狐狸，各住各的地方（草原、疏林、密林、岩坡雪线、水边浅滩、林缘）；
///   · 全是**和平**的：不会打你，只会低头啃草、溜达、抬头看你一眼，你靠太近就撒腿跑；
///   · 能猎：打死了掉兽肉（<see cref="VoxelGem.Kind.Meat"/>），捡回物品栏后按 R 吃一块回饱食度 ——
///     野外因此多了一条狩猎线，也让"生态"不只是风景。
///
/// 行为：吃草 / 溜达 → 玩家进警戒圈就抬头盯着 → 再靠近就逃（拉开距离后自己溜达回窝）。
/// 行路规则和 NPC / 野怪同一套（避水、避陡坡、拴绳回窝），但**不会**主动朝玩家走 ——
/// 野外怪是"发现了你就靠过来"，动物正相反，这是两套东西唯一要分清楚的地方。
///
/// 【远处的动物不跑 AI】野外要撒几十只，不能让它们每帧都算寻路 + 骨骼：
/// 离玩家超过 <see cref="SleepDistance"/> 米就直接跳出 Update（只保留一次"收回站姿"），
/// 回到附近再醒过来。真机掉帧时最先动这里。
/// </summary>
public class VoxelAnimal : MonoBehaviour
{
    public enum Kind { Rabbit, Deer, Boar, Goat, Crane, Fox }

    /// <summary>场上所有野生动物（玩家攻击、分布统计都用它）</summary>
    public static readonly List<VoxelAnimal> All = new List<VoxelAnimal>();

    enum State { Graze, Wander, Alert, Flee, Return, Dead }

    const float SleepDistance = 110f;   // 离玩家超过这么远就完全不动

    // ---------- 数值（按种类在 ApplyKind 里填）----------
    public Kind kind = Kind.Rabbit;
    string _name = "野兔";
    float _maxHealth  = 18f;
    float _walkSpeed  = 1.5f;    // 平时溜达
    float _fleeSpeed  = 6.0f;    // 撒腿跑
    float _alertRange = 12f;     // 进这个圈就抬头盯着你（但还不跑）
    float _fleeRange  = 8f;      // 再近就逃
    float _leashRange = 45f;     // 离窝这么远就往回走
    float _homeRange  = 8f;      // 平时在窝附近这个半径里溜达
    float _bodyScale  = 1f;      // 个体大小差异（各只不一样，看着不整齐划一）
    float _hitHeight  = 0.3f;    // 玩家攻击的瞄准高度（身体中段，米）
    float _neckDown   = 30f;     // 低头啃草时脖子 / 头压下去多少度
    int   _meatDrop   = 1;       // 掉几块兽肉
    bool  _waterBird;            // 白鹭：就该站在水边 / 浅水里
    bool  _snowy;                // 雪原上的个体（狐狸在雪线以北是白狐）

    float _health;

    State _state;
    Vector3 _home, _target, _lastPos;
    float _verticalVelocity = -2f;
    float _idleTimer = 1.2f;
    float _walkPhase, _hopPhase, _hopRate = 1f;
    float _stuckTime, _hitFlash, _deathTimer;
    float _fleeTimer;              // 挨打之后这段时间内一直逃（哪怕玩家站着不动）
    float _labelTimer;             // 名字牌还要显示多久
    bool  _walking, _awake = true;
    bool  _spooked;                // 被惊到过：逃到明显拉开距离为止（见 DecideState 的迟滞）
    float _lookSeed;               // 吃草时"抬头看一眼"的相位（每只不一样，不会一起抬头）

    OpenWorld _world;
    Transform _player;
    VoxelInteraction _inventory;
    CharacterController _controller;
    Transform _body, _neck, _tail, _leftWing, _rightWing, _earL, _earR, _labelTop;
    readonly Transform[] _legs = new Transform[4];      // 0 前左 / 1 前右 / 2 后左 / 3 后右（兔子用 0,1 当后腿）
    Material _material;                                 // 每只动物一份实例（受击闪红不能全场一起红）
    Camera _camera;
    GUIStyle _label;

    /// <summary>血量</summary>
    public float Health => _health;
    /// <summary>血量上限（名字牌画血条用）</summary>
    public float MaxHealth => _maxHealth;
    /// <summary>死了（等它缩完就销毁）</summary>
    public bool Dead => _state == State.Dead;
    /// <summary>玩家攻击判定的瞄准高度</summary>
    public float HitHeight => _hitHeight * _bodyScale;
    /// <summary>显示名</summary>
    public string DisplayName => _name;

    // ---------- 初始化 ----------

    public void Init(Material material, Transform player, OpenWorld world, VoxelInteraction inventory,
                     Kind k, bool snowy = false)
    {
        kind = k;
        _snowy = snowy;
        _world = world;
        _player = player;
        _inventory = inventory;
        _camera = Camera.main;
        _home = transform.position;
        _lastPos = transform.position;

        // 个体差异：大小 ±8%、吃草的抬头相位随机 —— 十几只鹿站一排也不会像复制出来的
        _lookSeed = Random.value * 6.28f;

        ApplyKind();

        _bodyScale *= Random.Range(0.92f, 1.08f);
        _health = _maxHealth;

        float radius = kind == Kind.Crane ? 0.22f : (kind == Kind.Rabbit ? 0.16f : 0.3f);
        float height = Mathf.Max(0.5f, _hitHeight * 2.0f);

        var cc = gameObject.AddComponent<CharacterController>();
        cc.height     = height * _bodyScale;
        cc.radius     = radius * _bodyScale;
        cc.center     = new Vector3(0f, cc.height * 0.5f, 0f);
        cc.slopeLimit = 90f;
        cc.stepOffset = 0.45f;
        cc.skinWidth  = 0.02f;
        _controller = cc;

        // 每只动物一份材质实例：挨打闪红只红它自己（和野怪同一个理由）
        _material = new Material(material) { name = "Animal" + _name };

        BuildBody();
    }

    /// <summary>按种类填数值</summary>
    void ApplyKind()
    {
        switch (kind)
        {
            case Kind.Deer:
                _name = "鹿"; _maxHealth = 70f; _walkSpeed = 1.8f; _fleeSpeed = 6.2f;
                _alertRange = 14f; _fleeRange = 10f; _leashRange = 55f; _homeRange = 11f;
                _hitHeight = 0.95f; _neckDown = 42f; _meatDrop = 3;
                break;

            case Kind.Boar:
                _name = "野猪"; _maxHealth = 120f; _walkSpeed = 1.5f; _fleeSpeed = 4.8f;
                _alertRange = 9f; _fleeRange = 7f; _leashRange = 45f; _homeRange = 9f;
                _hitHeight = 0.5f; _neckDown = 30f; _meatDrop = 4;
                break;

            case Kind.Goat:
                _name = "岩羊"; _maxHealth = 85f; _walkSpeed = 1.6f; _fleeSpeed = 5.0f;
                _alertRange = 13f; _fleeRange = 9f; _leashRange = 50f; _homeRange = 10f;
                _hitHeight = 0.7f; _neckDown = 34f; _meatDrop = 3;
                break;

            case Kind.Crane:
                _name = "白鹭"; _maxHealth = 40f; _walkSpeed = 1.3f; _fleeSpeed = 4.6f;
                _alertRange = 12f; _fleeRange = 8f; _leashRange = 45f; _homeRange = 8f;
                _hitHeight = 0.8f; _neckDown = 55f; _meatDrop = 2; _waterBird = true;
                break;

            case Kind.Fox:
                _name = "狐狸"; _maxHealth = 55f; _walkSpeed = 1.7f; _fleeSpeed = 6.8f;
                _alertRange = 13f; _fleeRange = 9f; _leashRange = 45f; _homeRange = 9f;
                _hitHeight = 0.35f; _neckDown = 24f; _meatDrop = 2;
                break;

            default:
                _name = "野兔"; _maxHealth = 18f; _walkSpeed = 1.5f; _fleeSpeed = 6.5f;
                _alertRange = 11f; _fleeRange = 8f; _leashRange = 40f; _homeRange = 8f;
                _hitHeight = 0.26f; _neckDown = 0f; _meatDrop = 1;
                break;
        }

        if (_snowy) _name = "雪狐";
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // ---------- 主循环 ----------

    void Update()
    {
        if (_controller == null || _world == null) return;

        float dt = Time.deltaTime;

        if (_state == State.Dead) { UpdateDeath(dt); return; }

        // 闪红 / 名字牌无论远近都要继续走完（挨打时玩家就在旁边，这里几乎不会真跑远）
        UpdateTint(dt);
        if (_labelTimer > 0f) _labelTimer -= dt;
        if (_fleeTimer > 0f) _fleeTimer -= dt;

        // 远处的动物完全不动：撒了几十只，不能让它们每帧都算寻路和骨骼
        if (_player != null && FlatDistance(_player.position) > SleepDistance)
        {
            if (_awake) { _awake = false; RestPose(); }
            return;
        }
        _awake = true;

        ApplyGravity();

        _state = DecideState();

        switch (_state)
        {
            case State.Alert:  AlertStep();      break;
            case State.Flee:   FleeStep();       break;
            case State.Return: ReturnStep();     break;
            case State.Wander: WanderStep();     break;
            default:           GrazeStep();      break;
        }

        Animate(dt);
    }

    /// <summary>这一刻该干什么（优先级从高到低：上岸 → 被吓到就逃 → 走太远回窝 → 警戒 → 吃草 / 溜达）</summary>
    State DecideState()
    {
        if (_player == null) return _walking ? State.Wander : State.Graze;

        // 站在水里（白鹭除外）先上岸：动物掉进湖里游不回来的样子很出戏
        if (!_waterBird && NearWater(transform.position.x, transform.position.z, 0.35f)) return State.Return;

        float toPlayer = FlatDistance(_player.position);

        // 逃就要逃到"明显拉开距离"为止（迟滞）：
        // 只在警戒圈边缘判一次是否逃的话，动物会在圈边上"一逃一停"地抽动；
        // 挨过打（_fleeTimer > 0）则这段时间内一直逃，玩家站着不动也不停。
        if (_fleeTimer > 0f || _spooked)
        {
            // 但也不能一路逃到天边：离开窝太远（超过拴绳的 1.6 倍）就放弃逃跑、往家走
            bool tooFarFromHome = FlatDistance(_home) > _leashRange * 1.6f;
            if (!tooFarFromHome && (_fleeTimer > 0f || toPlayer < _fleeRange * 1.9f)) return State.Flee;
            _spooked = false;
        }

        if (FlatDistance(_home) > _leashRange) return State.Return;   // 跑太远，回窝

        if (toPlayer < _alertRange)
        {
            // 警戒圈里：先抬头盯着；再近一步才真的跑
            if (toPlayer < _fleeRange) { _spooked = true; return State.Flee; }
            return State.Alert;
        }

        return _walking ? State.Wander : State.Graze;
    }

    // ---------- 各状态 ----------

    /// <summary>低头啃草：站着不动，偶尔抬头瞄一眼（吃草 / 抬头用同一条正弦切换，很便宜）</summary>
    void GrazeStep()
    {
        _idleTimer -= Time.deltaTime;

        if (_player != null && FlatDistance(_player.position) < _alertRange * 1.8f)
            TurnToward(_player.position - transform.position, 1.6f);      // 远处有人：慢慢转头看一眼

        if (_idleTimer <= 0f) DecideNext();
    }

    /// <summary>抬头盯着你：站定、身体朝向你 —— 这一步还没跑，所以你能靠到警戒圈边上观察它</summary>
    void AlertStep()
    {
        if (_player != null) TurnToward(_player.position - transform.position, 4f);
    }

    /// <summary>溜达到目标点</summary>
    void WanderStep()
    {
        Vector3 to = _target - transform.position;
        to.y = 0f;

        if (to.magnitude < 0.35f)
        {
            _walking = false;
            _idleTimer = Random.Range(1.5f, 4.5f);
            return;
        }

        Vector3 move = PickWalkableDirection(to.normalized);
        if (move == Vector3.zero)
        {
            _walking = false;
            _idleTimer = Random.Range(0.5f, 1.5f);
            return;
        }

        Move(move, _walkSpeed);
        TurnToward(move, 5f);
        CheckStuck();
    }

    /// <summary>逃：朝远离玩家的方向跑；路上避开水面和陡坡（逃跑路线也是走兽道，不会一头扎进湖里）</summary>
    void FleeStep()
    {
        if (_player == null) { _walking = false; return; }

        Vector3 away = transform.position - _player.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = transform.forward;
        away.Normalize();

        Vector3 move = PickWalkableDirection(away);
        if (move == Vector3.zero) move = away;      // 被逼到死角就硬挤（总比原地站着被抓强）

        Move(move, _fleeSpeed);
        TurnToward(move, 9f);
        CheckStuck();
    }

    /// <summary>回窝（也负责"从水里爬上岸"）</summary>
    void ReturnStep()
    {
        Vector3 to = _home - transform.position;
        to.y = 0f;

        if (to.magnitude < 1.5f)
        {
            _walking = false;
            _idleTimer = Random.Range(1.5f, 4f);
            return;
        }

        Vector3 dir = NearWater(transform.position.x, transform.position.z, 0.35f)
            ? UphillDirection()
            : to.normalized;

        Vector3 move = PickWalkableDirection(dir);
        if (move == Vector3.zero) move = dir;

        Move(move, _walkSpeed * 1.25f);
        TurnToward(move, 5f);
        CheckStuck();
    }

    /// <summary>吃草 / 站着的时候想下一件事：多半接着吃，偶尔挑个近处的地方走走</summary>
    void DecideNext()
    {
        if (Random.value < 0.5f)
        {
            _idleTimer = Random.Range(2f, 5f);
            return;
        }

        for (int i = 0; i < 10; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float r = Random.Range(_homeRange * 0.3f, _homeRange);
            float x = _home.x + Mathf.Cos(a) * r;
            float z = _home.z + Mathf.Sin(a) * r;

            if (!Walkable(Mathf.FloorToInt(x), Mathf.FloorToInt(z))) continue;

            _target = new Vector3(x, _world.SurfaceY(x, z), z);
            _walking = true;
            return;
        }

        _idleTimer = Random.Range(1f, 3f);
    }

    // ---------- 行路规则（和 NPC / 野怪同一套） ----------

    bool Walkable(int x, int z)
    {
        float fx = x + 0.5f, fz = z + 0.5f;
        if (!OpenWorld.InBounds(fx, fz)) return false;
        if (OpenWorld.Slope(fx, fz) > 0.5f) return false;      // 岩羊住在陡坡上，阈值比野怪宽一点

        float h = OpenWorld.Height(fx, fz);
        if (_waterBird)
        {
            // 白鹭本来就该站在水边 / 浅水里 —— 只有"水太深"才算不能走（不然它一步都迈不出去）
            if (h < OpenWorld.WaterLevel - 0.35f) return false;
        }
        else if (NearWater(fx, fz, 0.8f)) return false;

        float gy = _world.SurfaceY(fx, fz);
        return gy - transform.position.y <= 1.2f && transform.position.y - gy <= 2.2f;
    }

    /// <summary>水边判定：自己在高处（浅滩、石头台）不算</summary>
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

    Vector3 PickWalkableDirection(Vector3 dir)
    {
        for (int i = 0; i < 5; i++)
        {
            float angle = (i + 1) / 2 * 40f * (i % 2 == 1 ? 1f : -1f);   // 0 / +40 / -40 / +80 / -80
            Vector3 d = i == 0 ? dir : Quaternion.Euler(0f, angle, 0f) * dir;
            if (Walkable(Mathf.FloorToInt(transform.position.x + d.x * 0.9f),
                         Mathf.FloorToInt(transform.position.z + d.z * 0.9f)))
                return d;
        }
        return Vector3.zero;
    }

    /// <summary>8 个方向里挑地势最高的那个（从水边爬上岸）</summary>
    Vector3 UphillDirection()
    {
        Vector3 pos = transform.position;
        Vector3 best = Vector3.zero;
        float bestH = OpenWorld.Height(pos.x, pos.z);

        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            float px = pos.x + Mathf.Cos(a) * 2.5f, pz = pos.z + Mathf.Sin(a) * 2.5f;
            if (!OpenWorld.InBounds(px, pz)) continue;

            float hh = OpenWorld.Height(px, pz);
            if (hh > bestH + 0.05f) { bestH = hh; best = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); }
        }

        if (best == Vector3.zero) best = (_home - pos).normalized;
        return best;
    }

    void Move(Vector3 dir, float speed)
    {
        // 兔子是蹦着走的：一个周期里前半程窜出去，后半程落地（和史莱姆同一个套路，只是更快更碎）
        if (kind == Kind.Rabbit)
        {
            _hopPhase += Time.deltaTime * _hopRate;
            if (_hopPhase > 1f) _hopPhase -= 1f;
            if (_hopPhase > 0.58f) return;
            speed *= 1.7f;
        }

        _controller.Move(dir * speed * Time.deltaTime);
    }

    void CheckStuck()
    {
        if ((transform.position - _lastPos).sqrMagnitude < 0.0004f)
        {
            _stuckTime += Time.deltaTime;
            if (_stuckTime > 1.5f)
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

    void ApplyGravity()
    {
        if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
        _verticalVelocity -= 26f * Time.deltaTime;
        _controller.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
    }

    void TurnToward(Vector3 dir, float speed)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, yaw, 0f), Time.deltaTime * speed);
    }

    float FlatDistance(Vector3 other)
    {
        Vector3 to = other - transform.position;
        to.y = 0f;
        return to.magnitude;
    }

    // ---------- 被猎杀 ----------

    /// <summary>玩家打中它：掉血 + 闪红 + 撒腿跑；血掉光就倒</summary>
    public void TakeDamage(float amount, string cause)
    {
        if (_state == State.Dead || amount <= 0f) return;

        _health = Mathf.Max(0f, _health - amount);
        _hitFlash = 0.18f;
        _labelTimer = 4.5f;                 // 挨打了才飘名字牌（见 OnGUI 的说明）

        if (_health <= 0f) { Die(); return; }

        // 一枪没打死：这一只接下来几秒只会逃，不会站着让你打第二下
        _fleeTimer = Mathf.Max(_fleeTimer, 5f);
        _spooked = true;
        _state = State.Flee;
    }

    /// <summary>被打退一点（和野怪一样的手感；玩家真能把它推开）</summary>
    public void Knockback(Vector3 dir, float distance)
    {
        if (_state == State.Dead) return;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        _controller.Move(dir.normalized * distance);
    }

    void Die()
    {
        _state = State.Dead;
        _deathTimer = 0f;
        _walking = false;

        VoxelChat.Announce("打倒了一只" + _name + "（掉 " + _meatDrop + " 块兽肉）");

        // 掉兽肉：散在尸体周围，走过去捡就是素材（按 R 吃）
        if (_world != null && _player != null && _inventory != null)
        {
            for (int i = 0; i < _meatDrop; i++)
            {
                Vector3 from = transform.position + new Vector3(Random.Range(-0.5f, 0.5f),
                                                                _hitHeight * _bodyScale + 0.5f,
                                                                Random.Range(-0.5f, 0.5f));
                VoxelGem.Spawn(_world, _player, _inventory, from, VoxelGem.Kind.Meat);
            }
        }
    }

    void UpdateDeath(float dt)
    {
        _deathTimer += dt;
        UpdateTint(dt);

        // 倒下：往侧面一歪 + 沉一点，0.9 秒后销毁（比野怪"化烟"朴素，动物不该凭空消失）
        float t = Mathf.Clamp01(_deathTimer / 0.9f);
        if (_body != null)
        {
            _body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 82f, t));
            _body.localPosition = new Vector3(0f, -t * 0.12f, 0f);
        }

        if (_deathTimer > 0.95f) Destroy(gameObject);
    }

    // ---------- 受击闪红 ----------

    void UpdateTint(float dt)
    {
        if (_hitFlash > 0f) _hitFlash -= dt;
        if (_material == null) return;

        float t = Mathf.Clamp01(_hitFlash / 0.18f);
        _material.SetColor("_Tint", Color.Lerp(Color.white, new Color(1f, 0.35f, 0.32f), t));
    }

    // ---------- 动画（全部是摆姿态，没有任何外部动画资源） ----------

    void Animate(float dt)
    {
        if (_body == null) return;

        bool moving = _state == State.Wander || _state == State.Flee || _state == State.Return;
        float speed = _state == State.Flee ? _fleeSpeed : _walkSpeed;

        if (kind == Kind.Rabbit) { AnimateRabbit(dt, moving); return; }
        if (kind == Kind.Crane)  { AnimateCrane(dt, moving); return; }

        // 四条腿的：对角步 —— 前左 + 后右 一组，前右 + 后左 一组
        if (moving)
        {
            _walkPhase += dt * speed * 2.6f;
            float swing = Mathf.Sin(_walkPhase) * 26f;
            SetLeg(0,  swing);
            SetLeg(3,  swing);
            SetLeg(1, -swing);
            SetLeg(2, -swing);
            _body.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(_walkPhase)) * 0.05f, 0f);
        }
        else
        {
            RestLegs();
            _body.localPosition = new Vector3(0f, Mathf.Sin(Time.time * 1.2f) * 0.012f, 0f);   // 呼吸
        }

        // 脖子 / 头：吃草时压下去，警戒时抬起来盯着你，跑动时随步伐轻点
        float neck = 0f;
        if (_state == State.Graze)      neck = GrazingNeck();
        else if (_state == State.Alert) neck = -8f;
        else if (moving)                neck = Mathf.Sin(_walkPhase) * 7f;

        if (_neck != null)
            _neck.localRotation = Quaternion.Slerp(_neck.localRotation, Quaternion.Euler(neck, 0f, 0f), dt * 5f);

        // 尾巴：溜达时慢慢摆，逃跑时翘起来
        if (_tail != null)
        {
            float wag  = moving ? Mathf.Sin(_walkPhase * 1.6f) * 16f : Mathf.Sin(Time.time * 1.6f) * 7f;
            float lift = _state == State.Flee ? -20f : 0f;
            _tail.localRotation = Quaternion.Euler(lift, wag, 0f);
        }
    }

    /// <summary>吃草时低头，但隔一会儿抬起来看一眼（每只的相位不同，一群鹿不会整齐划一地点头）</summary>
    float GrazingNeck()
    {
        bool lookUp = Mathf.Sin(Time.time * 0.7f + _lookSeed) > 0.55f;
        return lookUp ? -6f : _neckDown;
    }

    void AnimateRabbit(float dt, bool moving)
    {
        _hopRate = _state == State.Flee ? 4.2f : 2.4f;

        float s = _hopPhase * Mathf.PI;
        float hop = Mathf.Max(0f, Mathf.Sin(s));

        _body.localPosition = new Vector3(0f, hop * 0.2f * _bodyScale, 0f);
        _body.localScale = new Vector3(_bodyScale * (1f - hop * 0.10f),
                                       _bodyScale * (1f + hop * 0.18f),
                                       _bodyScale * (1f - hop * 0.10f));

        // 后腿蹬直（起跳那一下）→ 收回来；前爪几乎不动
        float push = hop * 40f;
        SetLeg(0, -push);
        SetLeg(1, -push);
        SetLeg(2, push * 0.35f);
        SetLeg(3, push * 0.35f);

        // 耳朵：逃跑时往后贴，警戒时立起来，平时半垂着轻轻抖
        float ear = _state == State.Flee ? -38f : (_state == State.Alert ? 6f : -8f);
        float twitch = Mathf.Sin(Time.time * 9f) * (_state == State.Alert ? 6f : 2f);
        if (_earL != null) _earL.localRotation = Quaternion.Slerp(_earL.localRotation, Quaternion.Euler(ear + twitch, 0f,  9f), dt * 7f);
        if (_earR != null) _earR.localRotation = Quaternion.Slerp(_earR.localRotation, Quaternion.Euler(ear - twitch, 0f, -9f), dt * 7f);
    }

    void AnimateCrane(float dt, bool moving)
    {
        if (moving)
        {
            _walkPhase += dt * (_state == State.Flee ? 8f : 3.4f);
            float swing = Mathf.Sin(_walkPhase) * 28f;
            SetLeg(0,  swing);      // 两条腿交替，抬得高
            SetLeg(1, -swing);

            // 走一步点一下头 —— 鹤 / 鹭走路就是这个样子
            if (_neck != null)
                _neck.localRotation = Quaternion.Euler(16f + Mathf.Sin(_walkPhase * 2f) * 12f, 0f, 0f);

            _body.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(_walkPhase)) * 0.06f, 0f);
        }
        else
        {
            RestLegs();
            float neck = _state == State.Alert ? -10f : GrazingNeck();
            if (_neck != null)
                _neck.localRotation = Quaternion.Slerp(_neck.localRotation, Quaternion.Euler(neck, 0f, 0f), dt * 5f);
            _body.localPosition = new Vector3(0f, Mathf.Sin(Time.time * 1.4f) * 0.015f, 0f);
        }

        // 翅膀：受惊就扇起来，顺便离地一点 —— 读起来就是"扑腾着飞走了"
        bool fleeing = _state == State.Flee;
        float flap = fleeing ? Mathf.Sin(Time.time * 17f) * 64f : 0f;
        if (_leftWing  != null) _leftWing.localRotation  = Quaternion.Euler(0f, 0f, -flap);
        if (_rightWing != null) _rightWing.localRotation = Quaternion.Euler(0f, 0f,  flap);

        if (fleeing)
            _body.localPosition += Vector3.up * (Mathf.Max(0f, Mathf.Sin(Time.time * 9f)) * 0.3f);
    }

    void SetLeg(int i, float angle)
    {
        if (_legs[i] == null) return;
        _legs[i].localRotation = Quaternion.Euler(angle, 0f, 0f);
    }

    void RestLegs()
    {
        for (int i = 0; i < _legs.Length; i++) SetLeg(i, 0f);
    }

    /// <summary>走远 / 睡着之前把姿态收回站立：不然停在半空、劈着腿的样子会被看到</summary>
    void RestPose()
    {
        RestLegs();
        if (_body != null) { _body.localPosition = Vector3.zero; _body.localScale = Vector3.one * _bodyScale; }
        if (_neck != null) _neck.localRotation = Quaternion.identity;
        if (_tail != null) _tail.localRotation = Quaternion.identity;
    }

    // ---------- 外形（全部代码拼，和野外怪同一套零件） ----------

    void BuildBody()
    {
        _body = new GameObject("Body").transform;
        _body.SetParent(transform, false);
        _body.localScale = Vector3.one * _bodyScale;

        switch (kind)
        {
            case Kind.Deer:   BuildDeerBody();   break;
            case Kind.Boar:   BuildBoarBody();   break;
            case Kind.Goat:   BuildGoatBody();   break;
            case Kind.Crane:  BuildCraneBody();  break;
            case Kind.Fox:    BuildFoxBody();    break;
            default:          BuildRabbitBody(); break;
        }

        float top = kind == Kind.Deer ? 2.0f
                  : kind == Kind.Crane ? 1.25f
                  : kind == Kind.Boar ? 1.05f
                  : kind == Kind.Goat ? 1.25f
                  : kind == Kind.Fox ? 0.68f : 0.62f;

        _labelTop = new GameObject("LabelTop").transform;
        _labelTop.SetParent(transform, false);
        _labelTop.localPosition = new Vector3(0f, top * _bodyScale + 0.25f, 0f);
    }

    /// <summary>野兔：一大坨圆身子 + 长耳朵 + 大后腿 + 白尾巴</summary>
    void BuildRabbitBody()
    {
        Color coat  = new Color32(0xb0, 0x8c, 0x62, 255);
        Color belly = new Color32(0xec, 0xdf, 0xc8, 255);
        Color dark  = new Color32(0x3a, 0x2d, 0x22, 255);
        Color pink  = new Color32(0xd0, 0x8b, 0x86, 255);

        // 后腿（蹲着的大腿盘 + 蹬地的脚）、前爪（短）
        _legs[0] = Leg("LegBL", new Vector3(-0.11f, 0.20f, -0.12f), 0.20f, 0.11f, coat, coat);
        _legs[1] = Leg("LegBR", new Vector3( 0.11f, 0.20f, -0.12f), 0.20f, 0.11f, coat, coat);
        _legs[2] = Leg("LegFL", new Vector3(-0.09f, 0.16f,  0.16f), 0.16f, 0.07f, coat, coat);
        _legs[3] = Leg("LegFR", new Vector3( 0.09f, 0.16f,  0.16f), 0.16f, 0.07f, coat, coat);

        Ball("Body",   new Vector3(0f, 0.26f, -0.02f), new Vector3(0.34f, 0.30f, 0.46f), coat);
        Ball("Belly",  new Vector3(0f, 0.21f,  0.06f), new Vector3(0.28f, 0.22f, 0.36f), belly);
        Ball("Head",   new Vector3(0f, 0.40f,  0.24f), new Vector3(0.24f, 0.22f, 0.26f), coat);
        Ball("Muzzle", new Vector3(0f, 0.36f,  0.36f), new Vector3(0.14f, 0.12f, 0.12f), belly);
        Ball("Tail",   new Vector3(0f, 0.28f, -0.29f), new Vector3(0.13f, 0.13f, 0.10f), belly);

        for (int s = -1; s <= 1; s += 2)
            Ball("Eye", new Vector3(s * 0.11f, 0.43f, 0.34f), Vector3.one * 0.055f, dark);

        _earL = Pivot("EarL", new Vector3(-0.07f, 0.50f, 0.22f));
        _earR = Pivot("EarR", new Vector3( 0.07f, 0.50f, 0.22f));
        Part("EarLBody",  new Vector3(0f, 0.13f, 0f),    new Vector3(0.055f, 0.26f, 0.030f), coat, _earL);
        Part("EarRBody",  new Vector3(0f, 0.13f, 0f),    new Vector3(0.055f, 0.26f, 0.030f), coat, _earR);
        Part("EarLInner", new Vector3(0f, 0.13f, 0.02f), new Vector3(0.030f, 0.20f, 0.020f), pink, _earL);
        Part("EarRInner", new Vector3(0f, 0.13f, 0.02f), new Vector3(0.030f, 0.20f, 0.020f), pink, _earR);
    }

    /// <summary>鹿：细长的腿、结实的躯干、伸出来的脖子和一对树枝角</summary>
    void BuildDeerBody()
    {
        Color coat   = new Color32(0xa9, 0x74, 0x3f, 255);
        Color belly  = new Color32(0xe0, 0xcb, 0xa6, 255);
        Color hoof   = new Color32(0x3b, 0x2f, 0x26, 255);
        Color antler = new Color32(0xcb, 0xb8, 0x94, 255);
        Color dark   = new Color32(0x2a, 0x1f, 0x18, 255);
        Color patch  = new Color32(0xf0, 0xe4, 0xcd, 255);

        _legs[0] = Leg("LegFL", new Vector3(-0.15f, 0.72f,  0.36f), 0.72f, 0.11f, coat, hoof);
        _legs[1] = Leg("LegFR", new Vector3( 0.15f, 0.72f,  0.36f), 0.72f, 0.11f, coat, hoof);
        _legs[2] = Leg("LegBL", new Vector3(-0.16f, 0.72f, -0.38f), 0.72f, 0.12f, coat, hoof);
        _legs[3] = Leg("LegBR", new Vector3( 0.16f, 0.72f, -0.38f), 0.72f, 0.12f, coat, hoof);

        Part("Body",  new Vector3(0f, 0.90f,  0.00f), new Vector3(0.46f, 0.48f, 1.02f), coat);
        Part("Belly", new Vector3(0f, 0.66f,  0.00f), new Vector3(0.40f, 0.18f, 0.94f), belly);
        Part("Rump",  new Vector3(0f, 0.92f, -0.40f), new Vector3(0.42f, 0.44f, 0.30f), coat);

        _tail = Pivot("Tail", new Vector3(0f, 1.00f, -0.56f));
        Part("TailBody", new Vector3(0f, -0.09f, -0.03f), new Vector3(0.10f, 0.20f, 0.09f), patch, _tail);

        // 脖子 + 头 + 角，一起挂在 _neck 上（吃草 / 抬头都是它转）
        _neck = Pivot("Neck", new Vector3(0f, 1.06f, 0.46f));
        Part("NeckBody", new Vector3(0f, 0.17f, 0.03f), new Vector3(0.20f, 0.46f, 0.22f), coat, _neck);
        Part("Head",     new Vector3(0f, 0.48f, 0.14f), new Vector3(0.21f, 0.21f, 0.34f), coat, _neck);
        Part("Muzzle",   new Vector3(0f, 0.43f, 0.32f), new Vector3(0.14f, 0.14f, 0.16f), patch, _neck);
        Part("Nose",     new Vector3(0f, 0.43f, 0.42f), new Vector3(0.08f, 0.08f, 0.05f), dark, _neck);

        for (int s = -1; s <= 1; s += 2)
        {
            Ball("Eye", new Vector3(s * 0.10f, 0.53f, 0.22f), Vector3.one * 0.05f, dark, _neck);
            Part("Ear", new Vector3(s * 0.13f, 0.60f, 0.06f), new Vector3(0.07f, 0.15f, 0.10f), coat, _neck);

            // 树枝角：主干 + 两根分叉
            Part("Antler",   new Vector3(s * 0.11f, 0.72f, 0.05f), new Vector3(0.045f, 0.30f, 0.045f), antler, _neck);
            Part("AntlerT1", new Vector3(s * 0.16f, 0.82f, 0.14f), new Vector3(0.035f, 0.18f, 0.035f), antler, _neck);
            Part("AntlerT2", new Vector3(s * 0.19f, 0.68f, 0.02f), new Vector3(0.035f, 0.16f, 0.035f), antler, _neck);
        }
    }

    /// <summary>野猪：矮壮、脊背一溜鬃毛、拱嘴和两颗獠牙</summary>
    void BuildBoarBody()
    {
        Color coat  = new Color32(0x5b, 0x46, 0x32, 255);
        Color back  = new Color32(0x49, 0x38, 0x2a, 255);
        Color mane  = new Color32(0x33, 0x29, 0x1f, 255);
        Color snout = new Color32(0x8a, 0x75, 0x66, 255);
        Color tusk  = new Color32(0xec, 0xe4, 0xcf, 255);
        Color dark  = new Color32(0x22, 0x1a, 0x14, 255);

        _legs[0] = Leg("LegFL", new Vector3(-0.23f, 0.40f,  0.38f), 0.40f, 0.16f, coat, dark);
        _legs[1] = Leg("LegFR", new Vector3( 0.23f, 0.40f,  0.38f), 0.40f, 0.16f, coat, dark);
        _legs[2] = Leg("LegBL", new Vector3(-0.24f, 0.40f, -0.40f), 0.40f, 0.17f, coat, dark);
        _legs[3] = Leg("LegBR", new Vector3( 0.24f, 0.40f, -0.40f), 0.40f, 0.17f, coat, dark);

        Part("Body",  new Vector3(0f, 0.62f,  0.00f), new Vector3(0.70f, 0.58f, 1.18f), coat);
        Part("Belly", new Vector3(0f, 0.42f,  0.00f), new Vector3(0.60f, 0.22f, 1.02f), back);

        // 背上一溜鬃毛（野猪最好认的特征）
        for (int i = -1; i <= 1; i++)
            Part("Mane", new Vector3(0f, 0.96f, i * 0.22f), new Vector3(0.14f, 0.18f, 0.12f), mane);

        _tail = Pivot("Tail", new Vector3(0f, 0.80f, -0.60f));
        Part("TailBody", new Vector3(0f, -0.04f, -0.05f), new Vector3(0.09f, 0.22f, 0.09f), back, _tail);

        _neck = Pivot("Neck", new Vector3(0f, 0.66f, 0.56f));
        Part("Head",  new Vector3(0f, 0.04f, 0.14f), new Vector3(0.44f, 0.42f, 0.42f), coat, _neck);
        Part("Snout", new Vector3(0f, -0.02f, 0.38f), new Vector3(0.24f, 0.22f, 0.22f), snout, _neck);
        Part("TuskL", new Vector3(-0.11f, -0.08f, 0.44f), new Vector3(0.05f, 0.18f, 0.05f), tusk, _neck);
        Part("TuskR", new Vector3( 0.11f, -0.08f, 0.44f), new Vector3(0.05f, 0.18f, 0.05f), tusk, _neck);

        for (int s = -1; s <= 1; s += 2)
        {
            Ball("Eye", new Vector3(s * 0.13f, 0.16f, 0.30f), Vector3.one * 0.05f, dark, _neck);
            Part("Ear", new Vector3(s * 0.20f, 0.26f, 0.02f), new Vector3(0.12f, 0.14f, 0.05f), back, _neck);
        }
    }

    /// <summary>岩羊：山地体型、细腿、向后弯的短角、山羊胡</summary>
    void BuildGoatBody()
    {
        Color coat  = new Color32(0x9a, 0x8f, 0x7c, 255);
        Color belly = new Color32(0xd8, 0xd2, 0xc4, 255);
        Color horn  = new Color32(0x6c, 0x5b, 0x45, 255);
        Color hoof  = new Color32(0x4a, 0x42, 0x38, 255);
        Color dark  = new Color32(0x2c, 0x26, 0x1e, 255);

        _legs[0] = Leg("LegFL", new Vector3(-0.14f, 0.52f,  0.28f), 0.52f, 0.10f, coat, hoof);
        _legs[1] = Leg("LegFR", new Vector3( 0.14f, 0.52f,  0.28f), 0.52f, 0.10f, coat, hoof);
        _legs[2] = Leg("LegBL", new Vector3(-0.15f, 0.52f, -0.30f), 0.52f, 0.11f, coat, hoof);
        _legs[3] = Leg("LegBR", new Vector3( 0.15f, 0.52f, -0.30f), 0.52f, 0.11f, coat, hoof);

        Part("Body",  new Vector3(0f, 0.72f,  0.00f), new Vector3(0.42f, 0.44f, 0.84f), coat);
        Part("Belly", new Vector3(0f, 0.55f,  0.00f), new Vector3(0.36f, 0.18f, 0.76f), belly);

        _tail = Pivot("Tail", new Vector3(0f, 0.92f, -0.44f));
        Part("TailBody", new Vector3(0f, 0.06f, -0.03f), new Vector3(0.09f, 0.18f, 0.08f), belly, _tail);

        _neck = Pivot("Neck", new Vector3(0f, 0.92f, 0.34f));
        Part("NeckBody", new Vector3(0f, 0.13f, 0.03f), new Vector3(0.18f, 0.34f, 0.20f), coat, _neck);
        Part("Head",     new Vector3(0f, 0.34f, 0.10f), new Vector3(0.21f, 0.21f, 0.30f), coat, _neck);
        Part("Muzzle",   new Vector3(0f, 0.29f, 0.26f), new Vector3(0.13f, 0.13f, 0.14f), belly, _neck);
        Part("Beard",    new Vector3(0f, 0.20f, 0.22f), new Vector3(0.10f, 0.18f, 0.08f), belly, _neck);

        for (int s = -1; s <= 1; s += 2)
        {
            Ball("Eye", new Vector3(s * 0.10f, 0.40f, 0.20f), Vector3.one * 0.045f, dark, _neck);
            // 角：两段向后弯
            Part("Horn1", new Vector3(s * 0.08f, 0.50f, -0.02f), new Vector3(0.055f, 0.20f, 0.055f), horn, _neck);
            Part("Horn2", new Vector3(s * 0.08f, 0.66f, -0.12f), new Vector3(0.045f, 0.18f, 0.045f), horn, _neck);
        }
    }

    /// <summary>白鹭：两条细长腿、白身子、长脖子，翅膀受惊会扇</summary>
    void BuildCraneBody()
    {
        Color white = new Color32(0xf4, 0xf4, 0xf0, 255);
        Color gray  = new Color32(0xcd, 0xd3, 0xd6, 255);
        Color leg   = new Color32(0x2f, 0x2b, 0x28, 255);
        Color beak  = new Color32(0xe6, 0xb9, 0x3c, 255);
        Color dark  = new Color32(0x24, 0x21, 0x1e, 255);

        // 两条腿（白鹭站在浅水里，脚是深色的）
        _legs[0] = Leg("LegL", new Vector3(-0.07f, 0.52f, 0.00f), 0.52f, 0.045f, leg, leg);
        _legs[1] = Leg("LegR", new Vector3( 0.07f, 0.52f, 0.00f), 0.52f, 0.045f, leg, leg);

        Ball("Body", new Vector3(0f, 0.66f,  0.00f), new Vector3(0.32f, 0.26f, 0.50f), white);
        Ball("Rump", new Vector3(0f, 0.62f, -0.26f), new Vector3(0.26f, 0.22f, 0.28f), gray);

        _tail = Pivot("Tail", new Vector3(0f, 0.70f, -0.34f));
        Part("Feather", new Vector3(0f, -0.02f, -0.10f), new Vector3(0.16f, 0.08f, 0.24f), gray, _tail);

        _neck = Pivot("Neck", new Vector3(0f, 0.76f, 0.18f));
        Part("NeckBody", new Vector3(0f, 0.18f, 0.02f), new Vector3(0.075f, 0.36f, 0.075f), white, _neck);
        Ball("Head",     new Vector3(0f, 0.38f, 0.03f), new Vector3(0.14f, 0.13f, 0.15f), white, _neck);
        Part("Beak",     new Vector3(0f, 0.36f, 0.20f), new Vector3(0.045f, 0.045f, 0.28f), beak, _neck);
        Ball("Crest",    new Vector3(0f, 0.42f, -0.10f), new Vector3(0.07f, 0.05f, 0.16f), gray, _neck);

        for (int s = -1; s <= 1; s += 2)
            Ball("Eye", new Vector3(s * 0.07f, 0.40f, 0.08f), Vector3.one * 0.035f, dark, _neck);

        // 翅膀：挂在肩侧，向左 / 右伸出去，逃跑时扇起来
        _leftWing  = Pivot("WingL", new Vector3(-0.20f, 0.72f, 0.00f));
        _rightWing = Pivot("WingR", new Vector3( 0.20f, 0.72f, 0.00f));
        Part("WingLBody", new Vector3(-0.20f, 0.00f, 0.00f), new Vector3(0.42f, 0.06f, 0.32f), white, _leftWing);
        Part("WingRBody", new Vector3( 0.20f, 0.00f, 0.00f), new Vector3(0.42f, 0.06f, 0.32f), white, _rightWing);
        Part("WingLTip",  new Vector3(-0.38f, -0.01f, -0.10f), new Vector3(0.20f, 0.04f, 0.22f), gray, _leftWing);
        Part("WingRTip",  new Vector3( 0.38f, -0.01f, -0.10f), new Vector3(0.20f, 0.04f, 0.22f), gray, _rightWing);
    }

    /// <summary>狐狸：小、尖脸、大尾巴；雪原上的个体换成一身白（雪狐）</summary>
    void BuildFoxBody()
    {
        Color coat, belly, dark, tailTip;

        if (_snowy)
        {
            coat    = new Color32(0xee, 0xf1, 0xf3, 255);
            belly   = new Color32(0xff, 0xff, 0xff, 255);
            dark    = new Color32(0x4a, 0x4f, 0x55, 255);
            tailTip = new Color32(0xd7, 0xdf, 0xe4, 255);
        }
        else
        {
            coat    = new Color32(0xc9, 0x66, 0x2f, 255);
            belly   = new Color32(0xf0, 0xe0, 0xcd, 255);
            dark    = new Color32(0x3a, 0x2a, 0x20, 255);
            tailTip = new Color32(0xf4, 0xef, 0xe6, 255);
        }

        _legs[0] = Leg("LegFL", new Vector3(-0.09f, 0.24f,  0.20f), 0.24f, 0.06f, coat, dark);
        _legs[1] = Leg("LegFR", new Vector3( 0.09f, 0.24f,  0.20f), 0.24f, 0.06f, coat, dark);
        _legs[2] = Leg("LegBL", new Vector3(-0.10f, 0.24f, -0.22f), 0.24f, 0.07f, coat, dark);
        _legs[3] = Leg("LegBR", new Vector3( 0.10f, 0.24f, -0.22f), 0.24f, 0.07f, coat, dark);

        Part("Body",  new Vector3(0f, 0.36f,  0.00f), new Vector3(0.28f, 0.26f, 0.58f), coat);
        Part("Belly", new Vector3(0f, 0.25f,  0.02f), new Vector3(0.24f, 0.14f, 0.52f), belly);

        _tail = Pivot("Tail", new Vector3(0f, 0.40f, -0.30f));
        Part("TailBody", new Vector3(0f, -0.05f, -0.16f), new Vector3(0.17f, 0.17f, 0.32f), coat, _tail);
        Part("TailTip",  new Vector3(0f, -0.07f, -0.34f), new Vector3(0.14f, 0.14f, 0.16f), tailTip, _tail);

        _neck = Pivot("Neck", new Vector3(0f, 0.44f, 0.26f));
        Part("Head",  new Vector3(0f, 0.06f, 0.10f), new Vector3(0.23f, 0.21f, 0.25f), coat, _neck);
        Part("Snout", new Vector3(0f, 0.02f, 0.25f), new Vector3(0.11f, 0.10f, 0.16f), belly, _neck);
        Part("Nose",  new Vector3(0f, 0.03f, 0.34f), new Vector3(0.06f, 0.06f, 0.04f), dark, _neck);

        for (int s = -1; s <= 1; s += 2)
        {
            Ball("Eye", new Vector3(s * 0.09f, 0.10f, 0.20f), Vector3.one * 0.04f, dark, _neck);
            Part("Ear",     new Vector3(s * 0.08f, 0.22f, 0.02f), new Vector3(0.07f, 0.15f, 0.045f), coat, _neck);
            Part("EarTip",  new Vector3(s * 0.08f, 0.30f, 0.02f), new Vector3(0.05f, 0.06f, 0.035f), dark, _neck);
        }
    }

    // ---------- 建模小工具（和 VoxelMonster 同一套做法） ----------

    Transform Pivot(string name, Vector3 localPos, Transform parent = null)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent != null ? parent : _body, false);
        t.localPosition = localPos;
        return t;
    }

    Transform Part(string name, Vector3 localPos, Vector3 size, Color color, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent != null ? parent : _body, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        StripShade(go, color, false);
        return go.transform;
    }

    Transform Ball(string name, Vector3 localPos, Vector3 scale, Color color, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent != null ? parent : _body, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        StripShade(go, color, true);
        return go.transform;
    }

    /// <summary>一条腿：枢轴挂在髋部，腿和蹄子挂在枢轴下面（绕髋摆就是走路）</summary>
    Transform Leg(string name, Vector3 hip, float length, float thickness, Color coat, Color hoof)
    {
        var pivot = Pivot(name, hip);
        Part(name + "Upper", new Vector3(0f, -length * 0.5f, 0f), new Vector3(thickness, length, thickness), coat, pivot);
        Part(name + "Hoof",  new Vector3(0f, -length + 0.03f, 0f), new Vector3(thickness * 1.15f, 0.07f, thickness * 1.3f), hoof, pivot);
        return pivot;
    }

    /// <summary>拆掉自带碰撞体（统一用胶囊）、按法线烘顶点色、换成动物自己的材质</summary>
    void StripShade(GameObject go, Color color, bool smooth)
    {
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        BakeShade(go.GetComponent<MeshFilter>().mesh, color, smooth);
        go.GetComponent<MeshRenderer>().sharedMaterial = _material;
    }

    /// <summary>方块按面明暗分档、球面按法线做渐变（和世界里其它低模一个调子；着色器直接用顶点色，所以转 linear）</summary>
    static void BakeShade(Mesh mesh, Color baseColor, bool smooth)
    {
        var normals = mesh.normals;
        var colors = new Color[normals.Length];

        for (int i = 0; i < normals.Length; i++)
        {
            Vector3 n = normals[i];
            float shade;
            if (smooth) shade = 0.62f + 0.38f * (n.y * 0.5f + 0.5f);
            else shade = n.y > 0.5f  ? 1.00f
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

    // ---------- 名字牌 ----------

    /// <summary>
    /// 只有"挨过打"（4.5 秒内）才飘名字和血条。
    /// 这是故意的：野外撒了几十只动物，全给名字牌的话草原上一片"野兔""鹿"在飘，
    /// 生态看着就变成道具清单了；想猎它才需要看清是什么、还剩多少血。
    /// </summary>
    void OnGUI()
    {
        VoxelTypography.Apply();
        if (_labelTop == null || _camera == null) return;
        if (_state == State.Dead || _labelTimer <= 0f) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;

        Vector3 top = _labelTop.position;
        float dist = Vector3.Distance(top, _camera.transform.position);
        if (dist > 30f) return;

        Vector3 screen = _camera.WorldToScreenPoint(top);
        if (screen.z <= 0f) return;

        if (_label == null)
            _label = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };

        float alpha = Mathf.Clamp01(_labelTimer / 1.2f) * Mathf.Clamp01((30f - dist) / 4f);
        _label.normal.textColor = new Color(1f, 1f, 1f, alpha);

        VoxelHud.LabelShadow(new Rect(screen.x - 80f, Screen.height - screen.y - 10f, 160f, 20f), _name, _label);
        VoxelHud.SegmentedBar(screen.x - 40f, Screen.height - screen.y + 10f, _health,
                              new Color(0.85f, 0.22f, 0.22f, alpha), 8f, 5f, _maxHealth);
    }
}
