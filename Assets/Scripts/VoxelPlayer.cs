using UnityEngine;

/// <summary>
/// 第一人称移动。碰撞直接交给 CharacterController，比自己写 AABB 更稳。
///
/// 另外带一层"被卡住自救"：项目的建筑 / 神像都是低模薄壳（没有底的），
/// 万一被关在屋顶、屋子或神像体积里，这里会隔一会儿探一次四周，
/// 确认怎么走都出不去就把人挪到最近站得下人的地方（见 <see cref="VoxelSpace"/>）。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class VoxelPlayer : MonoBehaviour
{
    public float walkSpeed       = 5.0f;
    public float sprintSpeed     = 8.2f;
    public float jumpSpeed       = 10.0f;
    public float gravity         = 28.0f;
    public float mouseSensitivity = 2.4f;
    public float eyeHeight       = 1.62f;
    public float flySpeed        = 7.0f;      // 飞行时的垂直速度

    /// <summary>是否处于飞行状态（由对话框的 /fly 指令切换）</summary>
    public bool isFlying;

    CharacterController _controller;
    Transform _camera;
    OpenWorld _world;
    VoxelStats _stats;
    Vector3 _spawnPoint;
    float _pitch;
    float _verticalVelocity = -2f;
    float _fallDistance;

    /// <summary>本帧是否真的在奔跑（供饱食度消耗使用）</summary>
    public bool IsSprinting { get; private set; }

    static bool HasControl => VoxelPanels.CanUseInput;

    public void Init(Transform cameraTransform, OpenWorld world)
    {
        _controller = GetComponent<CharacterController>();
        _camera = cameraTransform;
        _world = world;

        _camera.localPosition = new Vector3(0f, eyeHeight, 0f);
        _camera.localRotation = Quaternion.identity;
    }

    /// <summary>脚是不是泡在水里（跳进湖里不摔伤）</summary>
    bool FeetInWater()
    {
        if (_world == null) return false;
        return OpenWorld.IsDeepWater(transform.position.x, transform.position.z);
    }

    void Start()
    {
        // Bootstrap 在场景加载时已把 VoxelStats 挂在同一物体上
        _stats = GetComponent<VoxelStats>();
        _spawnPoint = transform.position;
    }

    /// <summary>死亡重生：瞬移回出生点并清空运动状态</summary>
    public void Respawn()
    {
        _controller.enabled = false;     // 先关掉再瞬移，避免 CharacterController 卡住
        transform.position = _spawnPoint;
        _verticalVelocity = -2f;
        _fallDistance = 0f;
        _pitch = 0f;
        _controller.enabled = true;
    }

    /// <summary>传送到某处（大地图的传送锚点用）。和 Respawn 一个道理：CharacterController 开着
    /// 直接改 position 会卡住，所以先关、挪完再开</summary>
    public void Teleport(Vector3 position)
    {
        if (_controller == null)
        {
            transform.position = position;
            return;
        }

        _controller.enabled = false;
        transform.position = position;
        _verticalVelocity = -2f;         // 别把上一段的坠落速度带过去
        _fallDistance = 0f;
        _controller.enabled = true;
    }

    /// <summary>直接把朝向定成某个 yaw（度）。大地图传送落地时用：落在锚点旁边就面朝该去的地方，
    /// 不用自己先转半圈找方向</summary>
    public void SetYaw(float yaw)
    {
        if (float.IsNaN(yaw)) return;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    void Update()
    {
        if (_camera == null) return;

        // 掉出世界底部 = 死亡重生（保险网；正常情况下有边界墙根本出不去）
        if (transform.position.y < -8f && _stats != null)
            _stats.TakeDamage(9999f, "坠入虚空");

        if (HasControl)
        {
            Look();
            Move();
            EscapeIfTrapped();
        }
        else IsSprinting = false;

        // 俯仰始终生效，这样松开鼠标时视角不会跳
        _camera.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    void Look()
    {
        float mx = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float my = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        transform.Rotate(0f, mx, 0f, Space.Self);
        _pitch = Mathf.Clamp(_pitch - my, -89f, 89f);
    }

    void Move()
    {
        float strafe = 0f, forward = 0f;
        if (Input.GetKey(KeyCode.W)) forward += 1f;
        if (Input.GetKey(KeyCode.S)) forward -= 1f;
        if (Input.GetKey(KeyCode.D)) strafe  += 1f;
        if (Input.GetKey(KeyCode.A)) strafe  -= 1f;

        Vector3 dir = transform.forward * forward + transform.right * strafe;
        if (dir.sqrMagnitude > 1f) dir.Normalize();      // 斜向不加速

        bool sprinting = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (sprinting && _stats != null && !_stats.CanSprint) sprinting = false;   // 饿到跑不动
        IsSprinting = sprinting && (forward != 0f || strafe != 0f);

        float speed = sprinting ? sprintSpeed : walkSpeed;

        if (isFlying)
        {
            // 飞行：没有重力，空格上升、Ctrl 下降，WASD 照常水平移动
            float upDown = 0f;
            if (Input.GetKey(KeyCode.Space))       upDown += 1f;
            if (Input.GetKey(KeyCode.LeftControl)) upDown -= 1f;

            Vector3 flyMotion = dir * speed;
            flyMotion.y = upDown * flySpeed;
            _verticalVelocity = -2f;               // 关掉飞行时从静止开始下落，不积累速度
            _fallDistance = 0f;                    // 飞行中不累计摔落
            _controller.Move(flyMotion * Time.deltaTime);
            return;
        }

        if (_controller.isGrounded)
        {
            // 落地才结算摔落伤害（4 格内免伤，落水也不伤）
            if (_fallDistance > VoxelStats.SafeFallBlocks && _stats != null && !FeetInWater())
                _stats.ApplyFallDamage(_fallDistance);
            _fallDistance = 0f;

            if (_verticalVelocity < 0f) _verticalVelocity = -2f;   // 贴地，避免下坡抖动
            if (Input.GetKeyDown(KeyCode.Space)) _verticalVelocity = jumpSpeed;
        }
        else if (_verticalVelocity < 0f)
        {
            _fallDistance -= _verticalVelocity * Time.deltaTime;   // 只累计下落经过的距离
        }

        _verticalVelocity -= gravity * Time.deltaTime;

        Vector3 motion = dir * speed;
        motion.y = _verticalVelocity;
        _controller.Move(motion * Time.deltaTime);
    }

    // ---------- 被卡住自救 ----------

    const float TrapCheckInterval = 0.5f;    // 隔多久看一眼"四周还走不走得通"
    const float TrapEscapeAfter   = 1.5f;    // 一直走不通这么久，就认为是被关在壳子里了

    float _trapCheck;
    float _trapTime;

    /// <summary>
    /// 被关在屋顶 / 屋子 / 神像这种薄壳里时，把自己挪到最近站得下人的地方。
    /// 判定用 <see cref="VoxelSpace.Enclosed"/>（近处 + 远处两圈方向全堵住才算），
    /// 所以在开阔地形上不会误触发；飞行和坠落的途中也不管（那时候不叫"卡住"）。
    /// 实在找不到空位就贴回脚下的地形表面 —— 世界是连续地形，那儿一定站得住。
    /// </summary>
    void EscapeIfTrapped()
    {
        if (isFlying || !_controller.isGrounded)
        {
            _trapTime = 0f;
            return;
        }

        _trapCheck -= Time.deltaTime;
        if (_trapCheck > 0f) return;

        _trapCheck = TrapCheckInterval;

        if (!VoxelSpace.Enclosed(transform.position, transform))
        {
            _trapTime = 0f;
            return;
        }

        _trapTime += TrapCheckInterval;
        if (_trapTime < TrapEscapeAfter) return;

        _trapTime = 0f;

        Vector3 target;
        if (!VoxelSpace.TryFindSpot(transform.position, transform, out target))
            target = new Vector3(transform.position.x, GroundY(transform.position.x, transform.position.z) + 0.2f,
                                 transform.position.z);
        else
            target.y = Mathf.Max(target.y, GroundY(target.x, target.z) + 0.05f);

        Teleport(target);
        VoxelChat.Announce("你被卡在建筑里了，已经把你挪到外面");
    }

    /// <summary>脚下那块地形的高度（找不到世界时就用当前位置，权当兜底）</summary>
    float GroundY(float x, float z) => _world != null ? _world.SurfaceY(x, z) : transform.position.y;
}
