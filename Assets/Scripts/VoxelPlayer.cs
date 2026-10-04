using UnityEngine;

/// <summary>
/// 第一人称移动。碰撞直接交给 CharacterController，比自己写 AABB 更稳。
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

    static bool HasControl => Cursor.lockState == CursorLockMode.Locked;

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
        }

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
}
