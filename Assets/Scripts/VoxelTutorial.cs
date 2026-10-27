using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Training repeated on every login. The original world is built only after the exit is unlocked.</summary>
[DefaultExecutionOrder(-200)]
public sealed class VoxelTutorial : MonoBehaviour
{
    const string CompletionKey = "VoxelDemo.Tutorial.Completed.v1";
    public static bool Completed => PlayerPrefs.GetInt(CompletionKey, 0) == 1;
    public static bool PanelOpen { get; private set; }
    enum Panel { None, Welcome, Pause }
    static readonly string[] Titles = {
        "环顾训练场", "移动", "疾跑", "跳过矮栏", "普通攻击", "元素战技",
        "元素爆发", "开启补给箱", "背包", "角色", "地图", "暂停菜单", "前往原世界"
    };
    static readonly string[] Hints = {
        "移动鼠标，向左右看看，累计转动视角 25 度。",
        "按 WASD 在训练场行走，累计移动 5 米。",
        "按住 Shift 配合 WASD 疾跑，累计跑动 5 米。",
        "向前走，按空格跳过金色矮栏，在另一侧落地。",
        "走近前方红色木桩，对准木桩按鼠标左键，命中 3 次。",
        "站在木桩附近按鼠标右键释放战技。战技有冷却时间。",
        "训练已补满能量。站在木桩附近按 F 释放元素爆发。",
        "沿步道走近紫色史诗补给箱，对准箱子按 E。",
        "按 B 打开真实背包查看补给，再按 B 或 Esc 关闭。",
        "按 C 打开角色界面，查看属性和技能，再按 C 或 Esc 关闭。",
        "按 M 查看训练场地图，再按 M 或 Esc 关闭。原世界也用 M 打开地图。",
        "按 Esc 打开暂停菜单，再按 Esc 返回训练。",
        "全部练习完成！沿步道走到蓝色出口附近，按 E 进入原本的世界。"
    };

    readonly List<Material> _materials = new List<Material>();
    readonly List<Camera> _cameras = new List<Camera>();
    readonly List<AudioListener> _listeners = new List<AudioListener>();
    readonly List<Light> _sceneLights = new List<Light>();
    Action _enterWorld;
    bool _returnToExistingWorld;
    GameObject _mapRoot, _playerGo, _exitBarrier;
    VoxelPlayer _player;
    CharacterController _controller;
    Camera _camera;
    VoxelInteraction _inventory;
    VoxelStats _stats;
    VoxelCombat _combat;
    Transform _dummy, _chest, _chestVisual;
    Renderer _dummyRenderer;
    Material _dummyMaterial, _combatMaterial;
    GUIStyle _heading, _body, _small;
    readonly Dictionary<GUIStyle, GUIStyle> _pixelStyles = new Dictionary<GUIStyle, GUIStyle>();
    Panel _panel;
    int _step, _hits;
    float _distance, _look, _flash;
    Vector3 _lastPosition;
    Quaternion _lastRotation;
    bool _jumped, _sawPanel, _started, _leaving;
    string _transitionError;
    public bool TrainingChestOpened => _step > 7;
    public bool ExitUnlocked => _step >= 12;

    public Camera ViewCamera => _camera;

    public static VoxelTutorial Begin(Action enterWorld, bool returnToExistingWorld = false)
    {
        var existing = FindObjectOfType<VoxelTutorial>();
        if (existing != null) return existing;
        var tutorial = new GameObject("NewPlayerTutorial").AddComponent<VoxelTutorial>();
        tutorial._enterWorld = enterWorld;
        tutorial._returnToExistingWorld = returnToExistingWorld;
        tutorial.Build();
        return tutorial;
    }

    void Build()
    {
        // Preserve scene cameras/listeners; restore them before constructing the original world.
        foreach (var cam in FindObjectsOfType<Camera>())
            if (cam.enabled && cam.gameObject.layer != LoginSanctuary.LoginLayer) { _cameras.Add(cam); cam.enabled = false; }
        foreach (var listener in FindObjectsOfType<AudioListener>())
            if (listener.enabled) { _listeners.Add(listener); listener.enabled = false; }
        foreach (var sceneLight in FindObjectsOfType<Light>())
            if (sceneLight.enabled && sceneLight.type == LightType.Directional) { _sceneLights.Add(sceneLight); sceneLight.enabled = false; }

        _mapRoot = new GameObject("TutorialMap");
        _mapRoot.transform.SetParent(transform);
        Cube("Grass island", new Vector3(0, -0.5f, 20), new Vector3(24, 1, 48), new Color(0.28f, 0.52f, 0.29f));
        Cube("Training path", new Vector3(0, 0.025f, 20), new Vector3(5, 0.05f, 44), new Color(0.7f, 0.65f, 0.5f));
        Cube("West wall", new Vector3(-12, 2, 20), new Vector3(1, 4, 48), new Color(0.35f, 0.42f, 0.45f));
        Cube("East wall", new Vector3(12, 2, 20), new Vector3(1, 4, 48), new Color(0.35f, 0.42f, 0.45f));
        Cube("Entrance wall", new Vector3(0, 2, -4), new Vector3(24, 4, 1), new Color(0.35f, 0.42f, 0.45f));
        Cube("Exit wall", new Vector3(0, 2, 44), new Vector3(24, 4, 1), new Color(0.35f, 0.42f, 0.45f));
        Cube("Jump hurdle", new Vector3(0, 0.5f, 12), new Vector3(6, 1, 0.5f), new Color(0.95f, 0.7f, 0.22f));
        _dummy = Cube("Training dummy", new Vector3(0, 1, 22), new Vector3(0.9f, 2, 0.9f), new Color(0.7f, 0.23f, 0.17f)).transform;
        _dummyRenderer = _dummy.GetComponent<Renderer>();
        _dummyMaterial = _dummyRenderer.sharedMaterial;
        Cube("Dummy arms", new Vector3(0, 1.4f, 22), new Vector3(2.2f, 0.25f, 0.3f), new Color(0.45f, 0.28f, 0.16f));
        Cube("Exit left pillar", new Vector3(-2, 2, 39), new Vector3(0.5f, 4, 0.7f), Color.cyan);
        Cube("Exit right pillar", new Vector3(2, 2, 39), new Vector3(0.5f, 4, 0.7f), Color.cyan);
        Cube("Exit lintel", new Vector3(0, 4, 39), new Vector3(4.5f, 0.5f, 0.7f), Color.cyan);
        _exitBarrier = Cube("Locked tutorial exit", new Vector3(0, 1.6f, 39), new Vector3(3.5f, 3.2f, 0.3f), new Color(0.65f, 0.2f, 0.2f));
        var lightGo = new GameObject("Tutorial sunlight");
        lightGo.transform.SetParent(_mapRoot.transform);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.transform.rotation = Quaternion.Euler(45, -35, 0);

        _playerGo = new GameObject("TutorialPlayer");
        _playerGo.transform.SetParent(transform);
        _playerGo.transform.position = new Vector3(0, 0.15f, 2);
        _controller = _playerGo.AddComponent<CharacterController>();
        _controller.height = 1.8f;
        _controller.radius = 0.3f;
        _controller.center = new Vector3(0, 0.9f, 0);
        _controller.stepOffset = 0.4f;
        _controller.skinWidth = 0.02f;
        _controller.slopeLimit = 50;
        var cameraGo = new GameObject("Tutorial Camera");
        cameraGo.transform.SetParent(_playerGo.transform);
        _camera = cameraGo.AddComponent<Camera>();
        _camera.fieldOfView = 72;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0.56f, 0.8f, 0.95f);
        cameraGo.AddComponent<AudioListener>();
        _player = _playerGo.AddComponent<VoxelPlayer>();
        _player.Init(_camera.transform, null);
        _stats = _playerGo.AddComponent<VoxelStats>();
        _stats.Init(_player);
        _inventory = _playerGo.AddComponent<VoxelInteraction>();
        _inventory.Init(null, _camera.transform);
        _inventory.PrimogemCount = 0;
        _inventory.FateCount = 0;
        var sheet = _playerGo.AddComponent<VoxelCharacterSheet>();
        sheet.InitTraining(_inventory);
        _playerGo.AddComponent<VoxelInventory>().Init(_inventory);
        _playerGo.AddComponent<VoxelCharacterScreen>().Init(sheet);
        _combatMaterial = new Material(Shader.Find("Voxel/Demo"));
        _materials.Add(_combatMaterial);
        _combatMaterial.SetFloat("_FogStart", 1000);
        _combatMaterial.SetFloat("_FogEnd", 2000);
        BuildTrainingChest();
        LocalRemielle.SpawnDisplay(_mapRoot.transform, new Vector3(7, .08f, 22), _camera);
        _combat = _playerGo.AddComponent<VoxelCombat>();
        _combat.Init(_camera.transform, _combatMaterial);
        _combat.EffectParent = _mapRoot.transform;
        _combat.ActionPerformed += OnAction;
        _playerGo.AddComponent<WorldMap>().InitTraining(_playerGo.transform);
        gameObject.AddComponent<VoxelEnvironment>().Init(_camera, light, null);
        SetPanel(Panel.Welcome);
        ResetMeasurements();
    }

    GameObject Cube(string name, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(_mapRoot.transform);
        go.transform.position = position;
        go.transform.localScale = scale;
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = color;
        _materials.Add(material);
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    void BuildTrainingChest()
    {
        _chest = new GameObject("TutorialEpicChest").transform;
        _chest.SetParent(_mapRoot.transform, false);
        _chest.localPosition = new Vector3(0, 0.05f, 29);
        var visual = LootChest.CreateVisual(_chest, LootChest.Tier.Epic, _combatMaterial);
        _chestVisual = visual;
        // Use the visible model bounds for interaction instead of the old placeholder cube.
        var renderers = visual.GetComponentsInChildren<Renderer>();
        var bounds = new Bounds(_chest.position, Vector3.zero);
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        }
        foreach (var collider in visual.GetComponentsInChildren<Collider>()) collider.enabled = false;
        var box = _chest.gameObject.AddComponent<BoxCollider>();
        box.center = _chest.InverseTransformPoint(bounds.center);
        box.size = Vector3.Max(bounds.size, Vector3.one * 0.1f);
    }

    void MarkTrainingChestOpened()
    {
        LootChest.OpenVisual(_chestVisual);
    }

    void SetPanel(Panel panel)
    {
        _panel = panel;
        PanelOpen = panel != Panel.None;
        Cursor.lockState = PanelOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = PanelOpen;
        ResetMeasurements();
    }

    void ResetMeasurements()
    {
        _lastPosition = _player.transform.position;
        _lastRotation = _camera.transform.rotation;
    }

    void Update()
    {
        if (_leaving || !_started) return;
        if (_panel != Panel.None)
        {
            bool close = Input.GetKeyDown(KeyCode.Escape);
            if (close)
            {
                var closed = _panel;
                SetPanel(Panel.None);
                if (_step == 11 && closed == Panel.Pause) Advance();
            }
            return;
        }
        if (VoxelPanels.InputBlocked) return;
        if (Input.GetKeyDown(KeyCode.Escape)) SetPanel(Panel.Pause);

    }

    void LateUpdate()
    {
        if (_leaving || _player == null) return;
        // A fall or external teleport cannot bypass the gate; no original world exists yet.
        var p = _player.transform.position;
        if (p.y < -2 || Mathf.Abs(p.x) > 12 || p.z < -4 || p.z > 44)
        {
            _player.Teleport(new Vector3(0, 0.15f, _step < 4 ? 2 : 18));
            ResetMeasurements();
        }
        if (_flash > 0)
        {
            _flash -= Time.deltaTime;
            _dummyMaterial.color = Color.Lerp(new Color(0.7f, 0.23f, 0.17f), Color.white, Mathf.Clamp01(_flash * 5));
        }
        if (_step == 8 || _step == 9 || _step == 10)
        {
            bool open = _step == 8 ? VoxelInventory.IsOpen : _step == 9 ? VoxelCharacterScreen.IsOpen : WorldMap.IsOpen;
            if (open) _sawPanel = true;
            else if (_sawPanel && !VoxelPanels.InputBlocked) Advance();
        }
        if (!_started || !VoxelPanels.CanUseInput) { ResetMeasurements(); return; }
        // Long training sessions must not leave a beginner unable to sprint.
        if (_stats.Hunger < 70) _stats.EatFood(30, "训练补给");
        p = _player.transform.position;
        float moved = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(_lastPosition.x, _lastPosition.z));
        if (_step == 0)
        {
            _look += Quaternion.Angle(_lastRotation, _camera.transform.rotation);
            if (_look >= 25) Advance();
        }
        else if (_step == 1 || _step == 2)
        {
            if (_step == 1 || _player.IsSprinting) _distance += moved;
            if (_distance >= 5) Advance();
        }
        else if (_step == 3)
        {
            if (!_controller.isGrounded && p.y > 1 && p.z > 11.6f && p.z < 12.7f && Mathf.Abs(p.x) < 3) _jumped = true;
            if (_jumped && _controller.isGrounded && p.z > 13 && Mathf.Abs(p.x) < 3) Advance();
        }
        else if (_step == 7 && Input.GetKeyDown(KeyCode.E) && AimingAt(_chest, 3))
        {
            _inventory.AddMaterial(VoxelInteraction.MeatName, 3);
            _inventory.AddAndyCoin(100);
            MarkTrainingChestOpened();
            Advance();
        }
        else if (_step == 12 && Input.GetKeyDown(KeyCode.E) && Vector3.Distance(p, new Vector3(0, p.y, 39)) < 2.5f)
            StartCoroutine(EnterWorld());
        ResetMeasurements();
    }

    bool AimingAt(Transform target, float range)
    {
        return Physics.Raycast(_camera.transform.position, _camera.transform.forward, out var hit, range)
            && (hit.transform == target || hit.transform.IsChildOf(target));
    }

    void OnAction(string action)
    {
        if (!_started || _leaving || !VoxelPanels.CanUseInput) return;
        bool nearby = Vector3.Distance(_player.transform.position, _dummy.position) <= 4.5f;
        if (action == "attack" && AimingAt(_dummy, _combat.range))
        {
            _flash = 0.2f;
            VoxelCharacterSheet.ActiveState?.GainEnergy(VoxelCharacterSheet.CharacterState.EnergyPerHit);
            // The dummy must keep granting energy after the attack lesson too:
            // an out-of-range burst otherwise spends all energy and blocks training.
            if (_step == 4 && ++_hits >= 3) Advance();
        }
        else if (_step == 5 && action == "skill" && nearby) { _flash = 0.2f; Advance(); }
        else if (_step == 6 && action == "burst" && nearby) { _flash = 0.2f; Advance(); }
    }

    void Advance()
    {
        _step++;
        _distance = 0;
        _sawPanel = false;
        if (_step == 3) { _player.Teleport(new Vector3(0, 0.15f, 9)); _player.SetYaw(0); }
        if (_step == 4) { _player.Teleport(new Vector3(0, 0.15f, 18)); _player.SetYaw(0); }
        if (_step == 6)
        {
            var state = VoxelCharacterSheet.ActiveState;
            if (state != null) { state.Energy = state.MaxEnergy; state.BurstCooldown = 0; }
        }
        if (_step == 12) _exitBarrier.SetActive(false);
        ResetMeasurements();
    }

    IEnumerator EnterWorld(bool completed = true)
    {
        _leaving = true;
        PanelOpen = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = false;
        // Keep a rendering camera alive while the old player and its singletons are removed.
        // It stays untagged so Bootstrap cannot adopt it as the new world's main camera.
        _camera.transform.SetParent(transform, true);
        _camera.tag = "Untagged";
        _camera.cullingMask = 0;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0.06f, 0.1f, 0.15f);
        _camera.depth = 100;
        // Destroy training singletons before constructing their world equivalents.
        _playerGo.SetActive(false);
        Destroy(_playerGo);
        Destroy(_mapRoot);
        yield return null;
        _camera.GetComponent<AudioListener>().enabled = false;
        RestoreCameras();
        try
        {
            _enterWorld();
            if (Camera.main == null || !Camera.main.isActiveAndEnabled)
                throw new InvalidOperationException("原世界主相机未就绪，请查看 Console。");
        }
        catch (Exception error)
        {
            _transitionError = error.Message;
            Debug.LogException(error);
        }
        if (_transitionError != null) yield break;
        // Observe actual destination render frames before removing the loading camera.
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        if (Camera.main == null || !Camera.main.isActiveAndEnabled)
        {
            _transitionError = "原世界相机在交接时停止工作，请停止 Play 后重试。";
            _camera.GetComponent<AudioListener>().enabled = true;
            Debug.LogError("[Tutorial] " + _transitionError);
            yield break;
        }
        _camera.enabled = false;
        PanelOpen = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (completed)
        {
            PlayerPrefs.SetInt(CompletionKey, 1);
            PlayerPrefs.Save();
        }
        Debug.Log("[VoxelDemo] Tutorial completed; entered original world.");
        Destroy(gameObject);
    }

    void RestoreCameras()
    {
        foreach (var cam in _cameras) if (cam != null) cam.enabled = true;
        foreach (var listener in _listeners) if (listener != null) listener.enabled = true;
        foreach (var light in _sceneLights) if (light != null) light.enabled = true;
        _cameras.Clear();
        _listeners.Clear();
        _sceneLights.Clear();
    }

    void OnDestroy()
    {
        PanelOpen = false;
        RestoreCameras();
        foreach (var material in _materials) if (material != null) Destroy(material);
    }

    Rect NativeRect(Rect rect, out Matrix4x4 matrix)
    {
        matrix = GUI.matrix;
        Vector3 min = matrix.MultiplyPoint3x4(new Vector3(rect.x, rect.y, 0));
        Vector3 max = matrix.MultiplyPoint3x4(new Vector3(rect.xMax, rect.yMax, 0));
        return Rect.MinMaxRect(Mathf.Round(min.x), Mathf.Round(min.y), Mathf.Round(max.x), Mathf.Round(max.y));
    }

    GUIStyle NativeStyle(GUIStyle source, float scale)
    {
        if (!_pixelStyles.TryGetValue(source, out var style))
        {
            style = new GUIStyle(source);
            _pixelStyles.Add(source, style);
        }
        style.fontSize = Mathf.Max(1, Mathf.RoundToInt((source.fontSize > 0 ? source.fontSize : 16) * scale));
        return style;
    }

    void NativeLabel(Rect rect, string text, GUIStyle style)
    {
        rect = NativeRect(rect, out var matrix);
        var pixels = NativeStyle(style, matrix.m00);
        GUI.matrix = Matrix4x4.identity;
        try { GUI.Label(rect, text, pixels); }
        finally { GUI.matrix = matrix; }
    }

    bool NativeButton(Rect rect, string text)
    {
        rect = NativeRect(rect, out var matrix);
        var pixels = NativeStyle(GUI.skin.button, matrix.m00);
        GUI.matrix = Matrix4x4.identity;
        try { return GUI.Button(rect, text, pixels); }
        finally { GUI.matrix = matrix; }
    }

    void OnGUI()
    {
        VoxelTypography.Apply();
        if (_body == null)
        {
            _body = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, alignment = TextAnchor.UpperLeft };
            _body.normal.textColor = Color.white;
            _heading = new GUIStyle(_body) { fontSize = 24, fontStyle = FontStyle.Bold };
            _small = new GUIStyle(_body) { fontSize = 15 };
        }
        // A stable logical canvas keeps instructions inside small Game views.
        var oldMatrix = GUI.matrix;
        int oldDepth = GUI.depth;
        float scale = Mathf.Min(Screen.width / 960f, Screen.height / 640f);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
        float width = Screen.width / scale, height = Screen.height / scale;
        GUI.depth = -100;
        if (_leaving)
        {
            VoxelHud.FillRect(new Rect(0, 0, width, height), new Color(0.06f, 0.1f, 0.15f));
            NativeLabel(new Rect(60, height / 2, width - 120, 120), _transitionError == null
                ? "教程完成，正在生成原世界……"
                : "原世界生成失败：" + _transitionError + "\n请停止 Play 后重试，并查看 Console。", _heading);
        }
        else if (_panel != Panel.None)
        {
            VoxelHud.FillRect(new Rect(0, 0, width, height), new Color(0.03f, 0.06f, 0.1f, 0.85f));
            var box = new Rect((width - 700) / 2, (height - 440) / 2, 700, 440);
            string title = _panel == Panel.Welcome ? "欢迎来到新手训练场" : "训练已暂停 · Esc";
            NativeLabel(new Rect(box.x + 24, box.y + 20, 652, 50), title, _heading);
            string text = _panel == Panel.Welcome
                ? "沿训练步道学习基础操作。完成全部练习后，蓝色出口才会开放。\n\n这里沿用原来的移动、跳跃与技能规则。训练补给和角色调整只用于练习。\n\n教程现在为可选内容。可从出生点附近的传送门进入，也可在暂停界面返回原世界。"
                : "关闭界面后继续当前步骤，已经完成的练习会保留。\n\n原世界常用功能：\nB 背包　C 角色　M 地图　Esc 菜单\nE 开箱　R 吃烤肉　T 对话　P 祈愿\n\n技能条会显示冷却与能量；能量不足时不能释放爆发。";
            NativeLabel(new Rect(box.x + 24, box.y + 85, 652, 275), text, _body);
            if (NativeButton(new Rect(box.x + 200, box.y + 375, 300, 44), _panel == Panel.Welcome ? "开始基础操作教程" : "返回训练"))
            {
                var closed = _panel;
                _started = true;
                SetPanel(Panel.None);
                if (_step == 11 && closed == Panel.Pause) Advance();
            }
            if (_returnToExistingWorld && NativeButton(new Rect(box.x + 200, box.y + 426, 300, 36), "退出训练，返回原世界"))
                StartCoroutine(EnterWorld(false));
        }
        else if (!VoxelPanels.ModalOpen)
        {
            var hudMatrix = GUI.matrix;
            // Compact native-pixel card; independent of the modal panel canvas.
            GUI.matrix = Matrix4x4.identity;
            float cardScale = Mathf.Clamp(Screen.height / 1080f, 0.78f, 1f);
            float cardWidth = Mathf.Min(340f * cardScale, Screen.width - 24f);
            float cardY = 244f * Mathf.Clamp(Screen.height / 1080f, 0.68f, 1f);
            var titleStyle = new GUIStyle(_heading) { fontSize = Mathf.RoundToInt(18 * cardScale), wordWrap = true };
            var hintStyle = new GUIStyle(_body) { fontSize = Mathf.RoundToInt(15 * cardScale), wordWrap = true };
            var progressStyle = new GUIStyle(_small) { fontSize = Mathf.RoundToInt(13 * cardScale) };
            VoxelHud.FillRect(new Rect(14, cardY, cardWidth, 126 * cardScale), new Color(0.04f, 0.08f, 0.1f, 0.78f));
            GUI.Label(new Rect(26, cardY + 8, cardWidth - 24, 30 * cardScale), "新手教程 " + (_step + 1) + "/" + Titles.Length + " · " + Titles[_step], titleStyle);
            GUI.Label(new Rect(26, cardY + 39 * cardScale, cardWidth - 24, 54 * cardScale), Hints[_step], hintStyle);
            string progress = _step == 0 ? _look.ToString("0") + " / 25°" : _step == 1 || _step == 2
                ? _distance.ToString("0.0") + " / 5 米" : _step == 4 ? _hits + " / 3 次命中" : "完成当前练习，解锁下一步";
            GUI.Label(new Rect(26, cardY + 101 * cardScale, cardWidth - 24, 24 * cardScale), progress, progressStyle);
            GUI.matrix = hudMatrix;
            Vector3 target = _step < 4 ? new Vector3(0, 1.5f, 12) : _step < 7 ? _dummy.position + Vector3.up * 1.5f
                : _step < 12 ? _chest.position + Vector3.up * 1.2f : new Vector3(0, 4.5f, 39);
            Vector3 projected = _camera.WorldToScreenPoint(target);
            if (projected.z > 0)
            {
                string marker = _step < 4 ? "跳跃练习" : _step < 7 ? "训练木桩" : _step < 12 ? "补给箱" : "出口已开放 · E";
                float markerX = Mathf.Clamp(projected.x / scale - 75, 20, width - 170);
                float markerY = Mathf.Clamp((Screen.height - projected.y) / scale, 195, height - 130);
                VoxelHud.FillRect(new Rect(markerX - 6, markerY, 170, 30), new Color(0, 0, 0, 0.7f));
                NativeLabel(new Rect(markerX, markerY + 3, 158, 26), marker, _small);
            }
            NativeLabel(new Rect(width / 2 - 8, height / 2 - 16, 24, 32), "+", _heading);
            var state = VoxelCharacterSheet.ActiveState;
            string energy = state != null ? state.Energy.ToString("0") + " / " + state.MaxEnergy.ToString("0") : "--";
            NativeLabel(new Rect(24, height - 90, 850, 70), "右键战技：" + (state != null && state.SkillReady ? "就绪" : state != null ? state.SkillCooldown.ToString("0.0") + " 秒" : "--")
                + "    F 爆发能量：" + energy + "\n生命 " + _stats.Health.ToString("0") + "    饱食 " + _stats.Hunger.ToString("0") + "    Esc 暂停", _small);
        }
        GUI.matrix = oldMatrix;
        GUI.depth = oldDepth;
    }
}
