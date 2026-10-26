using UnityEngine;

/// <summary>Local-only character presentation; downloaded assets are never referenced by a player build.</summary>
public sealed class LocalRemielle : MonoBehaviour
{
    public const string CharacterId = "remielle_local";
    public const string ModelPath = "Assets/LocalCharacterTests/Remielle/Remielle.pmx";
    static GameObject _template;
    static LocalRemielle _preview;
    Camera _previewCamera, _labelCamera;
    RenderTexture _texture;
    Vector3 _anchor;
    OpenWorld _world;
    bool _demo, _motionStarted;
    float _yaw, _clock;
    int _lastPreviewFrame = -100;
    GUIStyle _label;
    RemielleMotion _motion;
    float _lastSpeed, _heading;
    public RemielleMotion Motion => _motion ?? (_motion = new RemielleMotion(transform));

    public static GameObject Template
    {
        get
        {
#if UNITY_EDITOR
            if (_template == null) _template = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
#endif
            return _template;
        }
    }
    public static bool Available => Template != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _template = null; _preview = null; }

    public static LocalRemielle SpawnDisplay(Transform parent, Vector3 position, Camera camera, OpenWorld world = null)
    {
        if (!Available) return null;
        var model = Instantiate(Template, position, Quaternion.identity, parent);
        model.name = "蕾米埃尔 · 本地模型测试";
        var actor = model.AddComponent<LocalRemielle>();
        actor._anchor = position; actor._demo = true; actor._world = world; actor._labelCamera = camera;
        var collider = model.AddComponent<CapsuleCollider>();
        collider.height = 1.72f; collider.radius = .22f; collider.center = Vector3.up * .86f;
        return actor;
    }

    public static bool DrawPreview(Rect rect)
    {
        if (!Available) return false;
        if (_preview == null)
        {
            var stage = new GameObject("Local character preview") { hideFlags = HideFlags.DontSave };
            stage.transform.position = new Vector3(0, -10000, 0);
            var model = Instantiate(Template, stage.transform, false);
            _preview = model.AddComponent<LocalRemielle>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            var camera = new GameObject("Character preview camera").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.transform.localPosition = new Vector3(0, .9f, -3);
            camera.transform.localRotation = Quaternion.identity;
            camera.orthographic = true; camera.orthographicSize = 1;
            camera.nearClipPlane = .1f; camera.farClipPlane = 10;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.14f, .18f, .24f, 1);
            camera.enabled = false;
            _preview._texture = new RenderTexture(512, 320, 24) { name = "Remielle preview", antiAliasing = 4 };
            _preview._texture.Create(); camera.targetTexture = _preview._texture;
            _preview._previewCamera = camera;
            _preview.transform.localRotation = Quaternion.Euler(0, 12, 0);
        }
        _preview._lastPreviewFrame = Time.frameCount;
        GUI.DrawTexture(rect, _preview._texture, ScaleMode.ScaleToFit, false);
        if (rect.Contains(Event.current.mousePosition) && Event.current.type == EventType.MouseDrag && Event.current.button == 0)
        {
            _preview._yaw -= Event.current.delta.x * .6f;
            _preview.transform.localRotation = Quaternion.Euler(0, 12 + _preview._yaw, 0);
            Event.current.Use();
        }
        return true;
    }

    void Awake() { _motion = new RemielleMotion(transform); }

    public void SamplePose(float time, bool walking)
        => Motion.Sample(time, walking ? 1f : 0f, time * 6f);

    /// <summary>Same deterministic path used by the live actor and editor motion preview.</summary>
    public void AdvanceDemo(float deltaTime)
    {
        if (deltaTime <= 0) return;
        // Bounded steps keep the foot solver and secondary response stable after a long frame.
        int steps = Mathf.CeilToInt(Mathf.Min(deltaTime, .25f) / (1f / 60));
        float dt = Mathf.Min(deltaTime, .25f) / steps;
        for (int i = 0; i < steps; i++) StepDemo(dt);
    }

    void StepDemo(float dt)
    {
        if (!_motionStarted)
        {
            _motionStarted = true;
            _anchor = transform.position;
        }
        _clock += dt;
        float cycle = _clock % 20f;
        float travel = cycle < 3 ? 0 : cycle < 8 ? RemielleMotion.Smooth((cycle - 3) / 5)
            : cycle < 12 ? 1 : cycle < 17 ? 1 - RemielleMotion.Smooth((cycle - 12) / 5) : 0;
        float heading = cycle < 8.5f ? 0 : cycle < 10.5f ? 180 * RemielleMotion.Smooth((cycle - 8.5f) / 2)
            : cycle < 17.5f ? 180 : cycle < 19.5f ? 180 * (1 - RemielleMotion.Smooth((cycle - 17.5f) / 2)) : 0;
        Vector3 target = _anchor + Vector3.back * (travel * 2.2f);
        if (_world != null) target.y = _world.SurfaceY(target.x, target.z);
        Vector3 delta = target - transform.position; delta.y = 0;
        float speed = delta.magnitude / dt;
        float turn = Mathf.Clamp(Mathf.DeltaAngle(_heading, heading) / dt / 120f, -1, 1);
        transform.SetPositionAndRotation(target, Quaternion.Euler(0, heading, 0));
        Motion.Advance(_clock, dt, speed, (speed - _lastSpeed) / dt, turn, _world);
        _lastSpeed = speed; _heading = heading;
    }

    void LateUpdate()
    {
        if (_previewCamera != null)
        {
            if (Time.frameCount - _lastPreviewFrame > 1) return;
            _clock += Time.unscaledDeltaTime;
            SamplePose(_clock, false);
            _previewCamera.Render();
            return;
        }
        if (!_demo || VoxelPanels.InputBlocked || Cursor.lockState != CursorLockMode.Locked) return;
        AdvanceDemo(Time.deltaTime);
    }

    void OnGUI()
    {
        if (!_demo || _labelCamera == null || VoxelPanels.InputBlocked) return;
        VoxelTypography.Apply();
        Vector3 p = _labelCamera.WorldToScreenPoint(transform.position + Vector3.up * 1.9f);
        if (p.z <= 0 || p.z > 20) return;
        if (_label == null) _label = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        VoxelHud.LabelShadow(new Rect(p.x - 120, Screen.height - p.y, 240, 24), "蕾米埃尔 · 本地测试", _label);
    }

    void OnDestroy()
    {
        if (_preview != this) return;
        _preview = null;
        if (_texture != null) { _texture.Release(); Destroy(_texture); }
        if (_previewCamera != null) Destroy(_previewCamera.transform.parent.gameObject);
    }
}
