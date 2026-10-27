using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 野外怪（原创低模，风格对标那种"野外三三两两的小怪"）：
///  · 丘丘人 Hilichurl：矮壮的方块小人，抡木棍近战；暴徒 HilichurlBrute 体型更大、血更厚、棍更疼。
///  · 史莱姆 Slime：一坨果冻，蹦跳着挪动，撞到人就撞伤；分水 / 草 / 火三种颜色，按地形分布（水边多水史莱姆）。
/// 行为：窝点附近游荡 → 玩家进警戒范围就靠过去 → 贴身挥棍 / 撞人 → 玩家跑远或超出拴绳距离就回窝发呆。
/// 和 NPC 用的是同一套行路规则（避开水边、陡坡、走出活动范围），所以不会卡在湖里。
/// 被打会掉血（受击闪红）、死亡化烟并掉原石。
/// </summary>
public class VoxelMonster : MonoBehaviour
{
    public enum Kind { Hilichurl, HilichurlBrute, SlimeWater, SlimeGrass, SlimeFire }

    /// <summary>场上所有野外怪（玩家攻击、分布统计都用它）</summary>
    public static readonly List<VoxelMonster> All = new List<VoxelMonster>();

    enum State { Idle, Patrol, Chase, Attack, Return, Dead }

    // ---------- 数值 ----------
    public Kind kind = Kind.Hilichurl;
    string _name = "丘丘人";
    float _maxHealth = 90f;
    float _walkSpeed = 2.0f;
    float _chaseSpeed = 3.4f;
    float _noticeRange = 14f;
    float _attackRange = 2.0f;
    float _attackDamage = 12f;
    float _attackInterval = 1.9f;
    float _wanderRange = 9f;
    float _leashRange = 34f;
    bool  _isSlime;
    float _bodyScale = 1f;

    // ---------- 世界难度（跟着出战角色等级走，见 VoxelWorldLevel） ----------

    /// <summary>当前生效的难度倍率：新刷出来的怪按它放大血量与攻击力</summary>
    static float _difficulty = 1f;

    /// <summary>按世界等级重算场上所有野怪的血量与攻击力（当前血量按比例保留）。
    /// 由 VoxelWorldLevel 在世界等级变化时调用 —— 所以"人变强、怪也变强"，不会一路碾压</summary>
    public static void ApplyDifficulty(float scale)
    {
        scale = Mathf.Clamp(scale, 0.5f, 8f);
        if (Mathf.Approximately(scale, _difficulty)) return;

        float ratio = scale / _difficulty;
        _difficulty = scale;

        for (int i = 0; i < All.Count; i++)
        {
            var m = All[i];
            if (m == null) continue;

            m._maxHealth *= ratio;
            m._attackDamage *= ratio;
            m._health = Mathf.Min(m._maxHealth, m._health * ratio);
        }
    }

    /// <summary>现在的世界难度倍率（自检 / 界面显示用）</summary>
    public static float Difficulty => _difficulty;

    float _health;
    State _state;
    Vector3 _home;
    Vector3 _target;
    Vector3 _lastPos;
    float _verticalVelocity = -2f;
    float _idleTimer = 1.5f;
    float _walkPhase, _stuckTime, _attackCooldown, _hitFlash, _deathTimer, _swing, _hopPhase;
    bool  _walking, _swingHit;

    OpenWorld _world;
    Transform _player;
    VoxelStats _playerStats;
    VoxelInteraction _inventory;
    CharacterController _controller;
    Transform _body, _leftArm, _rightArm, _leftLeg, _rightLeg, _club, _labelTop;
    Material _material;                 // 每只怪自己的材质实例（受击闪红时不会全场一起闪）
    Camera _camera;
    static readonly Dictionary<string, Mesh> VisualMeshes = new Dictionary<string, Mesh>();
    GUIStyle _label;
    bool _announcedFight;

    /// <summary>血量</summary>
    public float Health => _health;
    /// <summary>血量上限（名字牌画血条用）</summary>
    public float MaxHealth => _maxHealth;
    /// <summary>死了（等它化烟播完就销毁）</summary>
    public bool Dead => _state == State.Dead;
    /// <summary>玩家攻击判定的瞄准高度（身体中段）</summary>
    public float HitHeight => (_isSlime ? 0.45f : 1.0f) * _bodyScale;
    /// <summary>显示名</summary>
    public string DisplayName => _name;

    // ---------- 初始化 ----------

    public void Init(Material material, Transform player, OpenWorld world, VoxelInteraction inventory, Kind k)
    {
        kind = k;
        _world = world;
        _player = player;
        _inventory = inventory;
        _camera = Camera.main;
        _home = transform.position;

        ApplyKind();

        // 世界难度：怪的基准数值按当前世界等级放大（血量与攻击力一起），所以养成的收益是"打得动"，而不是"无脑碾压"
        _maxHealth   *= _difficulty;
        _attackDamage *= _difficulty;

        _health = _maxHealth;

        var cc = gameObject.AddComponent<CharacterController>();
        cc.height     = (_isSlime ? 1.0f : 1.8f) * _bodyScale;
        cc.radius     = (_isSlime ? 0.52f : 0.36f) * _bodyScale;
        cc.center     = new Vector3(0f, cc.height * 0.5f, 0f);
        cc.slopeLimit = 90f;
        cc.stepOffset = 0.6f;
        cc.skinWidth  = 0.02f;
        _controller = cc;

        // 每只怪一份材质实例：受击染色要靠它，共用材质会把全场怪物一起染红
        _material = new Material(material) { name = "Monster" + _name };

        BuildBody();

        if (_player != null) _playerStats = _player.GetComponent<VoxelStats>();
    }

    /// <summary>按种类填数值（丘丘人 / 暴徒 / 三种史莱姆）</summary>
    void ApplyKind()
    {
        switch (kind)
        {
            case Kind.HilichurlBrute:
                _name = "丘丘暴徒"; _maxHealth = 260f; _walkSpeed = 1.8f; _chaseSpeed = 3.1f;
                _noticeRange = 15f; _attackRange = 2.4f; _attackDamage = 22f; _attackInterval = 2.4f;
                _wanderRange = 8f; _leashRange = 38f; _bodyScale = 1.35f; _isSlime = false;
                break;
            case Kind.SlimeWater:
                _name = "水史莱姆"; _maxHealth = 55f; _walkSpeed = 1.5f; _chaseSpeed = 2.6f;
                _noticeRange = 12f; _attackRange = 1.7f; _attackDamage = 9f; _attackInterval = 1.6f;
                _wanderRange = 7f; _leashRange = 26f; _isSlime = true;
                break;
            case Kind.SlimeGrass:
                _name = "草史莱姆"; _maxHealth = 62f; _walkSpeed = 1.4f; _chaseSpeed = 2.4f;
                _noticeRange = 12f; _attackRange = 1.7f; _attackDamage = 8f; _attackInterval = 1.7f;
                _wanderRange = 7f; _leashRange = 26f; _isSlime = true;
                break;
            case Kind.SlimeFire:
                _name = "火史莱姆"; _maxHealth = 48f; _walkSpeed = 1.7f; _chaseSpeed = 2.9f;
                _noticeRange = 13f; _attackRange = 1.7f; _attackDamage = 15f; _attackInterval = 1.5f;
                _wanderRange = 7f; _leashRange = 26f; _isSlime = true;
                break;
            default:
                _name = "丘丘人"; _maxHealth = 90f; _walkSpeed = 2.0f; _chaseSpeed = 3.4f;
                _noticeRange = 14f; _attackRange = 2.0f; _attackDamage = 12f; _attackInterval = 1.9f;
                _wanderRange = 9f; _leashRange = 34f; _isSlime = false;
                break;
        }
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // ---------- 主循环 ----------

    void Update()
    {
        if (_controller == null || _world == null) return;

        float dt = Time.deltaTime;

        if (_state == State.Dead) { UpdateDeath(dt); return; }

        ApplyGravity();
        UpdateTint(dt);
        _attackCooldown -= dt;
        if (_swing > 0f)
        {
            float before = _swing;
            _swing -= dt;
            if (!_swingHit && before > 0.22f && _swing <= 0.22f) SwingHit();   // 挥到中间那一下结算伤害
        }

        _state = DecideState();

        switch (_state)
        {
            case State.Attack: AttackStep(dt); break;
            case State.Chase:  ChaseStep(dt);  break;
            case State.Return: ReturnStep(dt); break;
            case State.Patrol: PatrolStep();   break;
            default:           IdleStep();     break;
        }

        Animate();
    }

    State DecideState()
    {
        if (_player == null) return State.Idle;

        // 站在水里 / 水边：先上岸，别的都往后放
        if (NearWater(transform.position.x, transform.position.z, 0.3f)) return State.Return;

        float toHome = FlatDistance(_home);
        float toPlayer = FlatDistance(_player.position);

        // 拴绳：跑太远就回窝（原神里怪也会脱战回家）
        if (toHome > _leashRange) { _announcedFight = false; return State.Return; }

        if (toPlayer <= _noticeRange) return toPlayer <= _attackRange ? State.Attack : State.Chase;

        _announcedFight = false;
        return _walking ? State.Patrol : State.Idle;
    }

    // ---------- 各状态 ----------

    void IdleStep()
    {
        _idleTimer -= Time.deltaTime;

        // 闲站时朝玩家那边看（没进警戒范围也看一眼）
        if (_player != null && FlatDistance(_player.position) < _noticeRange * 1.6f)
            TurnToward(_player.position - transform.position, 2.5f);

        if (_idleTimer <= 0f) DecideNext();
    }

    void PatrolStep()
    {
        Vector3 to = _target - transform.position;
        to.y = 0f;

        if (to.magnitude < 0.35f)
        {
            _walking = false;
            _idleTimer = Random.Range(1.5f, 4f);
            return;
        }

        Vector3 dir = to.normalized;
        Vector3 move = PickWalkableDirection(dir);
        if (move == Vector3.zero)
        {
            _walking = false;
            _idleTimer = Random.Range(0.5f, 1.5f);
            return;
        }

        Move(move, _walkSpeed);
        TurnToward(move, 6f);
        CheckStuck();
    }

    void ChaseStep(float dt)
    {
        Vector3 to = _player.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist < 0.0001f) return;

        if (!_announcedFight)
        {
            _announcedFight = true;
            VoxelChat.Announce(_name + " 发现了你");
        }

        Vector3 move = PickWalkableDirection(to / dist);
        if (move == Vector3.zero) return;

        Move(move, _chaseSpeed);
        TurnToward(move, 8f);
    }

    void AttackStep(float dt)
    {
        Vector3 to = _player.position - transform.position;
        to.y = 0f;
        TurnToward(to, 9f);

        if (_attackCooldown > 0f || _swing > 0f) return;

        _attackCooldown = _attackInterval;
        _swing = 0.5f;
        _swingHit = false;
    }

    /// <summary>挥棍 / 撞人真正结算伤害的那一下：玩家还在范围内才吃伤害</summary>
    void SwingHit()
    {
        _swingHit = true;
        if (_player == null || _playerStats == null) return;
        if (FlatDistance(_player.position) > _attackRange + 0.6f) return;      // 已经闪开了

        _playerStats.TakeDamage(_attackDamage, _name);
    }

    void ReturnStep(float dt)
    {
        Vector3 to = _home - transform.position;
        to.y = 0f;

        if (to.magnitude < 1.2f)
        {
            _walking = false;
            _idleTimer = Random.Range(1.5f, 4f);
            return;
        }

        // 在水里 / 水边就朝地势最高的方向爬，平时就朝窝点走
        Vector3 dir = NearWater(transform.position.x, transform.position.z, 0.35f)
            ? UphillDirection()
            : to.normalized;

        Vector3 move = PickWalkableDirection(dir);
        if (move == Vector3.zero) move = dir;      // 回窝路上被挡住就硬挤一下，别原地打转

        Move(move, _walkSpeed * 1.15f);
        TurnToward(move, 6f);
        CheckStuck();
    }

    void DecideNext()
    {
        if (Random.value < 0.45f)
        {
            _idleTimer = Random.Range(1.5f, 4f);
            return;
        }

        for (int i = 0; i < 12; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float r = Random.Range(_wanderRange * 0.35f, _wanderRange);
            float x = _home.x + Mathf.Cos(a) * r;
            float z = _home.z + Mathf.Sin(a) * r;

            if (!Walkable(Mathf.FloorToInt(x), Mathf.FloorToInt(z))) continue;

            _target = new Vector3(x, _world.SurfaceY(x, z), z);
            _walking = true;
            return;
        }

        _idleTimer = Random.Range(1f, 3f);
    }

    // ---------- 行路规则（和 NPC 同一套） ----------

    bool Walkable(int x, int z)
    {
        float fx = x + 0.5f, fz = z + 0.5f;
        if (!OpenWorld.InBounds(fx, fz)) return false;
        if (NearWater(fx, fz, 0.8f)) return false;
        if (OpenWorld.Slope(fx, fz) > 0.4f) return false;

        float gy = _world.SurfaceY(fx, fz);
        return gy - transform.position.y <= 1.2f && transform.position.y - gy <= 2.2f;
    }

    /// <summary>水边判定：自己在高处（桥面之类）不算，免得连桥都不敢过</summary>
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
        if (_isSlime)
        {
            // 史莱姆是蹦着走的：一个周期里前段往前窜、后段落地歇一下
            _hopPhase += Time.deltaTime * 2.6f;
            if (_hopPhase > 1f) _hopPhase -= 1f;
            if (_hopPhase > 0.55f) return;                       // 落地那半程不走
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
        _verticalVelocity -= 28f * Time.deltaTime;
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

    // ---------- 受伤 / 死亡 ----------

    /// <summary>玩家打中它：掉血 + 闪红 + 进入追击；血掉光就死</summary>
    public void TakeDamage(float amount, string cause)
    {
        if (_state == State.Dead || amount <= 0f) return;

        _health = Mathf.Max(0f, _health - amount);
        _hitFlash = 0.18f;

        if (_health <= 0f)
        {
            Die();
            return;
        }

        _announcedFight = true;                 // 挨打了就还手，不用等它自己发现
        _state = State.Chase;
    }

    /// <summary>被玩家攻击推开一点（打起来有点手感）</summary>
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
        _swing = 0f;
        _walking = false;

        VoxelChat.Announce("打倒了一只" + _name);

        // 掉原石（原神里小怪也会掉素材）
        if (_world != null && _player != null && _inventory != null)
        {
            int drops = _isSlime ? 1 : (_kindIsBrute ? 3 : 2);
            for (int i = 0; i < drops; i++)
            {
                Vector3 from = transform.position + new Vector3(Random.Range(-0.6f, 0.6f), 1.2f, Random.Range(-0.6f, 0.6f));
                VoxelGem.Spawn(_world, _player, _inventory, from, VoxelGem.Kind.Primogem);
            }
        }
    }

    bool _kindIsBrute => kind == Kind.HilichurlBrute;

    void UpdateDeath(float dt)
    {
        _deathTimer += dt;
        UpdateTint(dt);

        // 化烟：缩下去 + 往下沉，0.9 秒后销毁
        float t = Mathf.Clamp01(_deathTimer / 0.9f);
        if (_body != null)
        {
            _body.localScale = Vector3.one * (1f - t) * _bodyScale;
            _body.localPosition = new Vector3(0f, -t * 0.5f, 0f);
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

    // ---------- 动画 ----------

    void Animate()
    {
        if (_body == null) return;

        if (_isSlime)
        {
            // 果冻感：一压一张，配合蹦跳周期
            float s = Mathf.Sin(_hopPhase * Mathf.PI * 2f);
            _body.localScale = new Vector3((1f - s * 0.10f) * _bodyScale, (1f + s * 0.14f) * _bodyScale, (1f - s * 0.10f) * _bodyScale);
            _body.localPosition = new Vector3(0f, Mathf.Abs(s) * 0.10f, 0f);
            return;
        }

        bool moving = _state == State.Chase || _state == State.Patrol || _state == State.Return;

        if (moving)
        {
            _walkPhase += Time.deltaTime * _walkSpeed * 3.4f;
            float leg = Mathf.Sin(_walkPhase) * 30f;
            float arm = Mathf.Sin(_walkPhase) * 22f;
            _leftLeg.localRotation  = Quaternion.Euler( leg, 0f, 0f);
            _rightLeg.localRotation = Quaternion.Euler(-leg, 0f, 0f);
            _leftArm.localRotation  = Quaternion.Euler(-arm, 0f, 0f);
            if (_swing <= 0f) _rightArm.localRotation = Quaternion.Euler(arm, 0f, 0f);
        }
        else
        {
            _leftLeg.localRotation  = Quaternion.identity;
            _rightLeg.localRotation = Quaternion.identity;
            float arm = Mathf.Sin(Time.time * 1.5f) * 7f;
            _leftArm.localRotation  = Quaternion.Euler(arm, 0f, 0f);
            if (_swing <= 0f) _rightArm.localRotation = Quaternion.Euler(-arm, 0f, 0f);
        }

        // 挥棍：0.5 → 0 秒，前段抬起来、中段砸下去
        if (_swing > 0f)
        {
            float t = 1f - Mathf.Clamp01(_swing / 0.5f);        // 0 起手 → 1 收势
            float angle = t < 0.35f
                ? Mathf.Lerp(0f, -120f, t / 0.35f)              // 抬起
                : Mathf.Lerp(-120f, 35f, (t - 0.35f) / 0.65f);  // 砸下
            _rightArm.localRotation = Quaternion.Euler(angle, 0f, 0f);
            if (_club != null) _club.localRotation = Quaternion.Euler(-12f, 0f, 0f);
        }
    }

    // ---------- 外形（全部代码拼，不用外部资源） ----------

    void BuildBody()
    {
        _body = new GameObject("Body").transform;
        _body.SetParent(transform, false);
        _body.localScale = Vector3.one * _bodyScale;

        if (_isSlime) BuildSlimeBody();
        else BuildHilichurlBody();
        MergeStaticBodyParts();

        _labelTop = new GameObject("LabelTop").transform;
        _labelTop.SetParent(transform, false);
        _labelTop.localPosition = new Vector3(0f, (_isSlime ? 1.15f : 2.0f) * _bodyScale + 0.35f, 0f);
    }

    /// <summary>丘丘人：矮壮方块小人（短腿、长臂、大头）、兽皮遮羞布、木棍</summary>
    void BuildHilichurlBody()
    {
        Color skin  = new Color32(0x9b, 0x86, 0x63, 255);   // 土黄皮
        Color cloth = new Color32(0x6d, 0x4f, 0x33, 255);   // 兽皮
        Color mask  = new Color32(0xe8, 0xdf, 0xc8, 255);   // 骨面具
        Color wood  = new Color32(0x7a, 0x54, 0x33, 255);
        Color eye   = new Color32(0x22, 0x1a, 0x14, 255);

        // 腿（挂在髋部枢轴下，绕髋摆）
        _leftLeg  = Pivot("LeftLeg",  new Vector3(-0.15f, 0.55f, 0f));
        _rightLeg = Pivot("RightLeg", new Vector3( 0.15f, 0.55f, 0f));
        Part("LegL", new Vector3(0f, -0.28f, 0f), new Vector3(0.26f, 0.56f, 0.28f), skin, _leftLeg);
        Part("LegR", new Vector3(0f, -0.28f, 0f), new Vector3(0.26f, 0.56f, 0.28f), skin, _rightLeg);

        // 身体：肚子鼓、胸口窄
        Part("Belly", new Vector3(0f, 0.82f, 0f), new Vector3(0.62f, 0.56f, 0.46f), skin);
        Part("Cloth", new Vector3(0f, 0.60f, 0f), new Vector3(0.66f, 0.22f, 0.50f), cloth);
        Part("Chest", new Vector3(0f, 1.18f, 0f), new Vector3(0.54f, 0.24f, 0.40f), skin);

        // 手臂（挂在肩枢轴下，长且垂）
        _leftArm  = Pivot("LeftArm",  new Vector3(-0.36f, 1.24f, 0f));
        _rightArm = Pivot("RightArm", new Vector3( 0.36f, 1.24f, 0f));
        Part("ArmL", new Vector3(0f, -0.34f, 0f), new Vector3(0.20f, 0.68f, 0.22f), skin, _leftArm);
        Part("ArmR", new Vector3(0f, -0.34f, 0f), new Vector3(0.20f, 0.68f, 0.22f), skin, _rightArm);

        // 手里的木棍（挂在右手枢轴下跟着摆）
        _club = Pivot("Club", new Vector3(0f, -0.64f, 0.06f), _rightArm);
        Part("ClubBody", new Vector3(0f, -0.28f, 0f), new Vector3(0.13f, 0.9f, 0.13f), wood, _club);
        Part("ClubHead", new Vector3(0f, -0.78f, 0f), new Vector3(0.24f, 0.26f, 0.24f), wood, _club);

        // 头 + 骨面具 + 眼睛 + 大鼻子
        Part("Head", new Vector3(0f, 1.60f, 0f), new Vector3(0.56f, 0.52f, 0.52f), skin);
        Part("Mask", new Vector3(0f, 1.58f, 0.28f), new Vector3(0.50f, 0.40f, 0.06f), mask);
        Part("EyeL", new Vector3(-0.13f, 1.66f, 0.32f), new Vector3(0.13f, 0.055f, 0.025f), eye);
        Part("EyeR", new Vector3( 0.13f, 1.66f, 0.32f), new Vector3(0.13f, 0.055f, 0.025f), eye);
        Part("Nose", new Vector3(0f, 1.50f, 0.34f), new Vector3(0.13f, 0.16f, 0.12f), skin);
        Color paint = new Color32(161, 69, 40, 255);
        Color fur = kind == Kind.HilichurlBrute ? new Color32(77, 65, 52, 255) : new Color32(116, 100, 77, 255);
        for (int side = -1; side <= 1; side += 2)
        {
            var brow = Part("Mask paint", new Vector3(side * .14f, 1.74f, .311f), new Vector3(.17f, .042f, .025f), paint);
            brow.localRotation = Quaternion.Euler(0, 0, side * 24);
            Part("Mask cheek", new Vector3(side * .18f, 1.51f, .314f), new Vector3(.06f, .12f, .026f), paint);
        }
        Part("Mask crest", new Vector3(0, 1.75f, .31f), new Vector3(.045f, .14f, .03f), paint);
        Part("Belt", new Vector3(0, .67f, .015f), new Vector3(.67f, .075f, .51f), wood);
        for (int tuft = 0; tuft < 9; tuft++)
        {
            float a = tuft * Mathf.PI * 2 / 9;
            var hair = Part("Fur", new Vector3(Mathf.Cos(a) * .27f, 1.4f, Mathf.Sin(a) * .2f - .03f),
                new Vector3(.17f, .24f + (tuft % 3) * .04f, .16f), fur);
            hair.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 180);
        }
        Part("Wrist wrap", new Vector3(0, -.52f, 0), new Vector3(.23f, .11f, .24f), cloth, _leftArm);
        Part("Wrist wrap", new Vector3(0, -.52f, 0), new Vector3(.23f, .11f, .24f), cloth, _rightArm);
        for (int band = 0; band < 3; band++)
            Part("Grip binding", new Vector3(0, -.1f - band * .09f, 0), new Vector3(.155f, .045f, .155f), cloth, _club);

        // 暴徒多两只角，一眼能认出来
        if (kind == Kind.HilichurlBrute)
        {
            for (int s = -1; s <= 1; s += 2)
                Part("Horn", new Vector3(s * 0.2f, 1.88f, 0f), new Vector3(0.12f, 0.26f, 0.12f), mask);
        }
    }

    /// <summary>史莱姆：一坨果冻 + 两只眼睛 + 一张嘴，颜色按种类分</summary>
    void BuildSlimeBody()
    {
        Color body, core;
        switch (kind)
        {
            case Kind.SlimeGrass: body = new Color32(0x7d, 0xd0, 0x52, 255); core = new Color32(0xbc, 0xee, 0x8f, 255); break;
            case Kind.SlimeFire:  body = new Color32(0xe8, 0x70, 0x3c, 255); core = new Color32(0xff, 0xc0, 0x6e, 255); break;
            default:              body = new Color32(0x4f, 0xb4, 0xe8, 255); core = new Color32(0xa8, 0xe8, 0xff, 255); break;
        }

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Jelly";
        ball.transform.SetParent(_body, false);
        ball.transform.localPosition = new Vector3(0f, 0.42f, 0f);
        ball.transform.localScale = new Vector3(0.95f, 0.78f, 0.95f);
        StripAndShade(ball, body, true);

        var inner = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        inner.name = "Core";
        inner.transform.SetParent(_body, false);
        inner.transform.localPosition = new Vector3(0f, 0.30f, 0f);
        inner.transform.localScale = new Vector3(0.46f, 0.34f, 0.46f);
        StripAndShade(inner, core, true);

        for (int s = -1; s <= 1; s += 2)
        {
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = "Eye";
            eye.transform.SetParent(_body, false);
            eye.transform.localPosition = new Vector3(s * 0.19f, 0.56f, 0.40f);
            eye.transform.localScale = Vector3.one * 0.13f;
            StripAndShade(eye, new Color32(0x20, 0x1a, 0x18, 255), true);
        }

        var mouth = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        mouth.name = "Mouth";
        mouth.transform.SetParent(_body, false);
        mouth.transform.localPosition = new Vector3(0f, 0.40f, 0.44f);
        mouth.transform.localScale = new Vector3(0.16f, 0.05f, 0.05f);
        StripAndShade(mouth, new Color32(0x30, 0x24, 0x20, 255), true);
        Part("Glint", new Vector3(-.23f, .67f, .34f), new Vector3(.14f, .075f, .025f), new Color(.86f, .96f, 1));
        if (kind == Kind.SlimeGrass)
        {
            for (int leaf = -1; leaf <= 1; leaf++)
            {
                var blade = Part("Leaf", new Vector3(leaf * .15f, .87f, 0), new Vector3(.16f, .32f, .07f), core);
                blade.localRotation = Quaternion.Euler(0, leaf * 35, -leaf * 35);
            }
        }
        else if (kind == Kind.SlimeFire)
        {
            Part("Horn", new Vector3(0, .92f, 0), new Vector3(.25f, .4f, .25f), core);
            Part("Horn", new Vector3(-.22f, .8f, .02f), new Vector3(.16f, .23f, .16f), body);
        }
        else
            Part("Droplet", new Vector3(0, .83f, 0), new Vector3(.2f, .22f, .2f), core);
    }

    // ---------- 建模小工具 ----------

    Transform Pivot(string name, Vector3 localPos, Transform parent = null)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent != null ? parent : _body, false);
        t.localPosition = localPos;
        return t;
    }

    Transform Part(string name, Vector3 localPos, Vector3 size, Color color, Transform parent = null)
    {
        var go = new GameObject(name);
        go.name = name;
        go.transform.SetParent(parent != null ? parent : _body, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        string shape = name == "Horn" || name == "Fur" ? "cone" : name.StartsWith("Arm") || name.StartsWith("Leg") || name.StartsWith("Club") ? "capsule" : "round";
        string key = shape + ColorUtility.ToHtmlStringRGBA(color);
        if (!VisualMeshes.TryGetValue(key, out var mesh) || mesh == null)
        {
            var vertices = new List<Vector3>(); var colors = new List<Color>(); var triangles = new List<int>();
            if (shape == "cone")
                LowPoly.Cylinder(vertices, colors, triangles, Vector3.down * .5f, 1f, .5f, .025f, 12, color, true);
            else
            {
                if (shape == "capsule")
                {
                    float[] heights = { .5f, .4665f, .375f, .25f, -.25f, -.375f, -.4665f, -.5f };
                    float[] radii = { 0, .25f, .433f, .5f, .5f, .433f, .25f, 0 };
                    for (int ring = 0; ring < heights.Length; ring++)
                    for (int segment = 0; segment <= 12; segment++)
                    {
                        float angle = segment * Mathf.PI * 2 / 12;
                        vertices.Add(new Vector3(Mathf.Sin(angle) * radii[ring], heights[ring], Mathf.Cos(angle) * radii[ring]));
                        colors.Add(color.linear);
                    }
                    for (int ring = 0; ring < heights.Length - 1; ring++)
                    for (int segment = 0; segment < 12; segment++)
                    {
                        int a = ring * 13 + segment, b = a + 13;
                        triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                        triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
                    }
                }
                else LowPoly.Sphere(vertices, colors, triangles, Vector3.zero, .5f, color, 12);
            }
            mesh = LowPoly.BuildMesh("Monster " + key, vertices, colors, triangles);
            BakeShade(mesh, color, true);
            VisualMeshes[key] = mesh;
        }
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = _material;
        return go.transform;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetVisualMeshes()
    {
        foreach (var mesh in VisualMeshes.Values) if (mesh != null) Destroy(mesh);
        VisualMeshes.Clear();
    }

    /// <summary>拆掉自带碰撞体（统一用胶囊）、按法线烘顶点色、换成怪物自己的材质</summary>
    void StripAndShade(GameObject go, Color color, bool smooth)
    {
        var col = go.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Destroy(col); }
        var filter = go.GetComponent<MeshFilter>();
        string key = "primitive_" + filter.sharedMesh.name + ColorUtility.ToHtmlStringRGBA(color) + smooth;
        if (!VisualMeshes.TryGetValue(key, out var mesh) || mesh == null)
        {
            mesh = Instantiate(filter.sharedMesh);
            BakeShade(mesh, color, smooth);
            VisualMeshes[key] = mesh;
        }
        filter.sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = _material;
    }

    void OnDestroy()
    {
        if (_material != null) Destroy(_material);
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

    void MergeStaticBodyParts()
    {
        string key = "combined_" + kind;
        var sources = new List<MeshFilter>();
        foreach (Transform child in _body)
        {
            var filter = child.GetComponent<MeshFilter>();
            if (filter != null) sources.Add(filter);
        }
        if (sources.Count == 0) return;
        if (!VisualMeshes.TryGetValue(key, out var mesh) || mesh == null)
        {
            var parts = new CombineInstance[sources.Count];
            for (int i = 0; i < parts.Length; i++)
                parts[i] = new CombineInstance { mesh = sources[i].sharedMesh,
                    transform = _body.worldToLocalMatrix * sources[i].transform.localToWorldMatrix };
            mesh = new Mesh { name = "Combined monster torso " + kind };
            mesh.CombineMeshes(parts, true, true);
            VisualMeshes[key] = mesh;
        }
        _body.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        _body.gameObject.AddComponent<MeshRenderer>().sharedMaterial = _material;
        foreach (var source in sources)
        {
            source.GetComponent<Renderer>().enabled = false;
            Destroy(source.gameObject);
        }
    }

    // ---------- 名字牌 ----------

    void OnGUI()
    {
        VoxelTypography.Apply();
        if (_labelTop == null || _camera == null || _state == State.Dead) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;

        Vector3 top = _labelTop.position;
        float dist = Vector3.Distance(top, _camera.transform.position);
        if (dist > 26f) return;

        Vector3 screen = _camera.WorldToScreenPoint(top);
        if (screen.z <= 0f) return;

        if (_label == null)
        {
            _label = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
        }

        float alpha = Mathf.Clamp01((26f - dist) / 4f);
        _label.normal.textColor = new Color(1f, 1f, 1f, alpha);

        VoxelHud.LabelShadow(new Rect(screen.x - 80f, Screen.height - screen.y - 10f, 160f, 20f), _name, _label);
        VoxelHud.SegmentedBar(screen.x - 40f, Screen.height - screen.y + 10f, _health,
                              new Color(0.85f, 0.22f, 0.22f, alpha), 8f, 5f);
    }
}
