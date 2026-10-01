using UnityEngine;

/// <summary>
/// 第一人称移动。碰撞直接交给 CharacterController，比自己写 AABB 更稳。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class VoxelPlayer : MonoBehaviour
{
    public float walkSpeed       = 5.0f;
    public float sprintSpeed     = 8.2f;
    public float jumpSpeed       = 9.2f;
    public float gravity         = 28.0f;
    public float mouseSensitivity = 2.4f;
    public float eyeHeight       = 1.62f;

    CharacterController _controller;
    Transform _camera;
    float _pitch;
    float _verticalVelocity = -2f;

    static bool HasControl => Cursor.lockState == CursorLockMode.Locked;

    public void Init(Transform cameraTransform)
    {
        _controller = GetComponent<CharacterController>();
        _camera = cameraTransform;

        _camera.localPosition = new Vector3(0f, eyeHeight, 0f);
        _camera.localRotation = Quaternion.identity;
    }

    void Update()
    {
        if (_camera == null) return;

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
        float speed = sprinting ? sprintSpeed : walkSpeed;

        if (_controller.isGrounded)
        {
            if (_verticalVelocity < 0f) _verticalVelocity = -2f;   // 贴地，避免下坡抖动
            if (Input.GetKeyDown(KeyCode.Space)) _verticalVelocity = jumpSpeed;
        }

        _verticalVelocity -= gravity * Time.deltaTime;

        Vector3 motion = dir * speed;
        motion.y = _verticalVelocity;
        _controller.Move(motion * Time.deltaTime);
    }
}
