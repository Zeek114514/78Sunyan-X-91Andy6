using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

/// <summary>An additive, independent 3D login scene. Destination creation starts only behind white loading.</summary>
public sealed class TitleScreen : MonoBehaviour
{
    public enum Phase { Company, GameLogo, ContentWarning, Reveal, Waiting, Approach, Loading, Uncover, Error, Complete }
    public static bool IsOpen { get; private set; }
    public Phase CurrentPhase { get; private set; }
    public Camera LoginCamera { get; private set; }
    public Camera DestinationCamera { get; private set; }
    public LoginSanctuary Sanctuary { get; private set; }
    public float WhiteOpacity { get; private set; } = 1;
    public int EntryRequestCount { get; private set; }
    public bool SceneReady { get; private set; }
    public string LastError { get; private set; }
    public bool RoutedToTutorial { get; private set; }
    LoginSequenceSettings _settings;
    Func<Camera> _enterGame;
    Scene _loginScene, _destinationScene;
    Texture2D _company, _game, _contentWarning;
    Material _logoMaterial;
    AudioSource _music;
    float _phaseTime, _idleTime, _approachStartZ;
    float _animationDelta, _prepareStartedAt;
    int _lastPresentedFrame = -1000, _warmupFrames, _phaseRepaints;
    int _consumedPresentationFrame = -1000;
    bool _waitingInputArmed;
    GUIStyle _hint, _status;
    Material _savedSky;
    Color _savedAmbient;
    AmbientMode _savedAmbientMode;
    bool _savedFog, _restored;
    readonly List<Light> _lights = new List<Light>();
    readonly List<AudioListener> _listeners = new List<AudioListener>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { IsOpen = false; }

    public static TitleScreen Begin(Func<Camera> enterGame)
    {
        var existing = FindObjectOfType<TitleScreen>(); if (existing != null) return existing;
        var original = SceneManager.GetActiveScene();
        var scene = SceneManager.CreateScene("LoginSanctuary");
        var go = new GameObject("LoginSequence"); SceneManager.MoveGameObjectToScene(go, scene);
        var title = go.AddComponent<TitleScreen>(); title._loginScene = scene; title._destinationScene = original;
        title.Init(enterGame); return title;
    }

    void Init(Func<Camera> enterGame)
    {
        IsOpen = true; _enterGame = enterGame;
        _settings = Resources.Load<LoginSequenceSettings>("LoginSequenceSettings");
        if (_settings == null) { Fail("缺少登录动画配置 LoginSequenceSettings。"); return; }
        _savedSky = RenderSettings.skybox; _savedAmbient = RenderSettings.ambientLight;
        _savedAmbientMode = RenderSettings.ambientMode; _savedFog = RenderSettings.fog;
        foreach (var light in FindObjectsOfType<Light>())
            if (light.enabled && light.type == LightType.Directional) { _lights.Add(light); light.enabled = false; }
        foreach (var listener in FindObjectsOfType<AudioListener>())
            if (listener.enabled) { _listeners.Add(listener); listener.enabled = false; }
        Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
        var cameraGo = new GameObject("Login camera"); cameraGo.layer = LoginSanctuary.LoginLayer;
        cameraGo.transform.SetParent(transform, false);
        LoginCamera = cameraGo.AddComponent<Camera>(); LoginCamera.depth = 100;
        LoginCamera.cullingMask = 1 << LoginSanctuary.LoginLayer; LoginCamera.clearFlags = CameraClearFlags.SolidColor;
        LoginCamera.backgroundColor = Color.white; LoginCamera.nearClipPlane = 0.1f; LoginCamera.farClipPlane = 1600;
        cameraGo.AddComponent<AudioListener>();
        LoginCamera.transform.position = new Vector3(0, _settings.cameraHeight, _settings.cameraStartZ);
        LoginCamera.transform.rotation = Quaternion.identity;
        _music = gameObject.AddComponent<AudioSource>(); _music.playOnAwake = false; _music.loop = true; _music.spatialBlend = 0; _music.volume = 0;
        _prepareStartedAt = Time.realtimeSinceStartup;
        StartCoroutine(Prepare());
    }

    IEnumerator Prepare()
    {
        var company = Resources.LoadAsync<Texture2D>("Branding/CompanyLogo");
        var game = Resources.LoadAsync<Texture2D>("Branding/GameLogo");
        var warning = Resources.LoadAsync<Texture2D>("Branding/ContentWarning");
        var music = Resources.LoadAsync<AudioClip>(_settings.musicResource);
        yield return company; yield return game; yield return music;
        yield return warning;
        try
        {
            _company = company.asset as Texture2D; _game = game.asset as Texture2D;
            _contentWarning = warning.asset as Texture2D;
            if (_contentWarning == null) throw new InvalidOperationException("内容提示图片未能加载。");
            if (_company == null || _game == null) throw new InvalidOperationException("公司或游戏 Logo 图片未能加载。");
            var shader = Shader.Find("Voxel/BrandingLogo");
            if (shader == null) throw new InvalidOperationException("Logo 显示材质不可用。");
            _logoMaterial = new Material(shader);
            Sanctuary = gameObject.AddComponent<LoginSanctuary>(); Sanctuary.Build(LoginCamera);
            RenderSettings.skybox = Sanctuary.Sky; RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.72f, 0.78f, 0.84f);
            LoginCamera.clearFlags = CameraClearFlags.Skybox;
            _music.clip = music.asset as AudioClip;
            if (_music.clip == null) Debug.LogWarning("[Login] No login music at Resources/" + _settings.musicResource + "; audio interface remains available.");
            SceneReady = true;
        }
        catch (Exception error) { Fail(error.Message); Debug.LogException(error); }
    }

    void Update()
    {
        if (_settings == null || LoginCamera == null) return;
        // Consume each visible repaint once. A repaint may arrive more than one
        // frame after Update in an editor Game view; that must not stop the clock.
        bool presented = _lastPresentedFrame > _consumedPresentationFrame;
        _animationDelta = LoginAnimationClock.Step(Time.unscaledDeltaTime, presented, Application.isFocused,
            SceneReady && _warmupFrames >= 2, _settings.maximumAnimationStep);
        _phaseTime += _animationDelta;
        if (_animationDelta > 0) _consumedPresentationFrame = _lastPresentedFrame;
        LoginCamera.fieldOfView = Mathf.Max(_settings.verticalFov, LoginCamera.aspect < 1.1f ? 68 : _settings.verticalFov);
        if (_music != null)
        {
            float target = PlayerPrefs.GetInt("CodelyDemo.Audio.Mute", 0) != 0 ? 0 : _settings.musicVolume;
            if (CurrentPhase < Phase.Reveal) target = 0;
            else if (CurrentPhase == Phase.Approach) target *= 1 - Mathf.Clamp01(_phaseTime / Mathf.Max(0.1f,_settings.approachDuration));
            else if (CurrentPhase >= Phase.Loading) target = 0;
            _music.volume = Mathf.MoveTowards(_music.volume,target,_animationDelta * _settings.musicVolume / Mathf.Max(0.1f,_settings.musicFadeIn));
        }
        if (CurrentPhase == Phase.Company)
        {
            if (!SceneReady && Time.realtimeSinceStartup - _prepareStartedAt > Mathf.Max(10,_settings.cameraTimeout))
            {
                StopAllCoroutines(); Fail("登录资源准备超时，请重新启动；已有游戏进度未重置。"); return;
            }
            if (SceneReady && _phaseTime >= _settings.companyFadeIn + _settings.companyHold + _settings.companyFadeOut) Change(Phase.GameLogo);
        }
        else if (CurrentPhase == Phase.GameLogo)
        {
            if (_phaseTime >= _settings.gameFadeIn + _settings.gameHold + _settings.gameFadeOut) Change(Phase.ContentWarning);
        }
        else if (CurrentPhase == Phase.ContentWarning)
        {
            if (_phaseTime >= _settings.warningFadeIn + _settings.warningHold + _settings.warningFadeOut) Change(Phase.Reveal);
        }
        else if (CurrentPhase == Phase.Reveal)
        {
            WhiteOpacity = 1 - Ease(_phaseTime / Mathf.Max(0.1f,_settings.sceneReveal));
            if (WhiteOpacity <= 0) Change(Phase.Waiting);
        }
        else if (CurrentPhase == Phase.Waiting)
        {
            _idleTime += _animationDelta;
            // Monotonic, bounded drift: no loop reset, jump, lateral sway or player input.
            float drift = _settings.idleDistance * (1 - Mathf.Exp(-_idleTime / Mathf.Max(1,_settings.idleTimeConstant)));
            LoginCamera.transform.position = new Vector3(0,_settings.cameraHeight,_settings.cameraStartZ + drift);
            // The click that restores focus (or was held during loading) only arms input.
            if (!_waitingInputArmed)
            {
                if (Application.isFocused && presented && _phaseRepaints >= 2 &&
                    !Input.GetMouseButton(0) && !Input.GetKey(KeyCode.Return) && !Input.GetKey(KeyCode.KeypadEnter))
                    _waitingInputArmed = true;
            }
            else if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) TryEnter();
        }
        else if (CurrentPhase == Phase.Approach)
        {
            float t = Mathf.Clamp01(_phaseTime / Mathf.Max(0.1f,_settings.approachDuration));
            LoginCamera.transform.position = new Vector3(0,_settings.cameraHeight,Mathf.Lerp(_approachStartZ,_settings.cameraEndZ,Ease(t)));
            Sanctuary.SetDoor(Ease(Mathf.InverseLerp(_settings.doorStart,_settings.doorEnd,t)));
            WhiteOpacity = Ease(Mathf.InverseLerp(_settings.whiteStart,1,t));
            if (t >= 1) { Change(Phase.Loading); StartCoroutine(LoadDestination()); }
        }
    }

    public bool TryEnter()
    {
        if (!SceneReady || CurrentPhase != Phase.Waiting || !_waitingInputArmed || !Application.isFocused ||
            Time.frameCount - _lastPresentedFrame > 1) return false;
        EntryRequestCount++; _approachStartZ = LoginCamera.transform.position.z;
        Change(Phase.Approach); return true;
    }
    static float Ease(float t) { t=Mathf.Clamp01(t);return t*t*t*(t*(t*6-15)+10); }
    void Change(Phase phase)
    {
        CurrentPhase=phase;_phaseTime=0;_phaseRepaints=0;_waitingInputArmed=false;
        Cursor.visible = phase == Phase.Waiting || phase == Phase.Error;
        Debug.Log("[Login] Phase=" + phase);
        if (phase==Phase.Reveal && _music!=null && _music.clip!=null) _music.Play();
    }

    void OnApplicationFocus(bool focused)
    {
        _lastPresentedFrame=-1000;_phaseRepaints=0;_waitingInputArmed=false;
        if (CurrentPhase==Phase.Company) _warmupFrames=0;
    }

    IEnumerator LoadDestination()
    {
        WhiteOpacity = 1;
        // Let a complete loading frame render before any synchronous runtime world generation.
        yield return new WaitForEndOfFrame(); yield return null;
        LoginCamera.clearFlags = CameraClearFlags.SolidColor; LoginCamera.backgroundColor = Color.white;
        LoginCamera.cullingMask = 0;
        RestoreLightingAndListeners();
        var listener = LoginCamera.GetComponent<AudioListener>(); if (listener != null) listener.enabled=false;
        try
        {
            SceneManager.SetActiveScene(_destinationScene);
            RoutedToTutorial = true;
            DestinationCamera = _enterGame();
        }
        catch (Exception error) { Fail("目标场景加载失败："+error.Message);Debug.LogException(error); }
        if (CurrentPhase == Phase.Error) yield break;
        float deadline = Time.realtimeSinceStartup + Mathf.Max(1,_settings.cameraTimeout);
        while (DestinationCamera == null || !DestinationCamera.isActiveAndEnabled)
        {
            if (Time.realtimeSinceStartup >= deadline) { Fail("目标相机未能在规定时间内就绪，请重新启动游戏。已有进度未重置。"); yield break; }
            yield return null;
        }
        // Keep the login camera rendering until the destination has completed two frames.
        yield return new WaitForEndOfFrame(); yield return new WaitForEndOfFrame();
        if (DestinationCamera == null || !DestinationCamera.isActiveAndEnabled)
        {
            Fail("目标相机在交接时停止工作，已保留加载相机。请重新启动。"); yield break;
        }
        LoginCamera.enabled=false; Change(Phase.Uncover);
        float duration=Mathf.Max(0.1f,_settings.loadingFade);
        for(float time=0;time<duration;time+=_animationDelta)
        {
            if (DestinationCamera == null || !DestinationCamera.isActiveAndEnabled)
            {
                LoginCamera.enabled=true; Fail("目标相机意外停止，已恢复加载相机。请重新启动。"); yield break;
            }
            WhiteOpacity=1-Ease(time/duration);yield return null;
        }
        WhiteOpacity=0; Change(Phase.Complete); IsOpen=false;
        Cursor.lockState=VoxelTutorial.PanelOpen?CursorLockMode.None:CursorLockMode.Locked;
        Cursor.visible=VoxelTutorial.PanelOpen;
        SceneManager.UnloadSceneAsync(_loginScene);
    }

    void RestoreLightingAndListeners()
    {
        if (_restored) return; _restored=true;
        RenderSettings.skybox=_savedSky;RenderSettings.ambientMode=_savedAmbientMode;RenderSettings.ambientLight=_savedAmbient;RenderSettings.fog=_savedFog;
        foreach(var light in _lights)if(light!=null)light.enabled=true;
        foreach(var listener in _listeners)if(listener!=null)listener.enabled=true;
    }
    void Fail(string error) { LastError=error;Change(Phase.Error);WhiteOpacity=1;Debug.LogError("[Login] "+error); }

    void OnGUI()
    {
        if (!IsOpen) return;
        if (CurrentPhase < Phase.Waiting && Event.current.isMouse)
        {
            Event.current.Use();
            return;
        }
        VoxelTypography.Apply();
        if (_hint==null) { _hint=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter};_hint.normal.textColor=new Color(0.2f,0.28f,0.36f);_status=new GUIStyle(_hint){wordWrap=true}; }
        int depth=GUI.depth;var matrix=GUI.matrix;var color=GUI.color;GUI.depth=-2000;GUI.matrix=Matrix4x4.identity;GUI.color=Color.white;
        try
        {
            float w=Screen.width,h=Screen.height;
            _hint.fontSize=Mathf.Clamp(Mathf.RoundToInt(h*0.027f),18,30);_status.fontSize=Mathf.Clamp(Mathf.RoundToInt(h*0.021f),15,23);
            if (WhiteOpacity>0) VoxelHud.FillRect(new Rect(0,0,w,h),new Color(1,1,1,WhiteOpacity));
            if(CurrentPhase==Phase.Company)
            {
                float a=Mathf.SmoothStep(0,1,Mathf.Clamp01(_phaseTime/Mathf.Max(0.1f,_settings.companyFadeIn)));
                float end=_settings.companyFadeIn+_settings.companyHold+_settings.companyFadeOut;
                a*=Mathf.SmoothStep(0,1,Mathf.Clamp01((end-_phaseTime)/Mathf.Max(0.1f,_settings.companyFadeOut)));
                DrawLogo(_company,new Vector2(w/2,h/2),Mathf.Min(w*0.8f,h*1.6f),a);
                if(_phaseTime>end&&!SceneReady)PassiveLabel(new Rect(20,h*0.8f,w-40,40),"正在准备登录场景…",_status);
            }
            if(CurrentPhase==Phase.GameLogo)
            {
                float a=Mathf.SmoothStep(0,1,Mathf.Clamp01(_phaseTime/Mathf.Max(0.1f,_settings.gameFadeIn)));
                float end=_settings.gameFadeIn+_settings.gameHold+_settings.gameFadeOut;
                a*=Mathf.SmoothStep(0,1,Mathf.Clamp01((end-_phaseTime)/Mathf.Max(0.1f,_settings.gameFadeOut)));
                DrawLogo(_game,new Vector2(w/2,h/2),Mathf.Min(w*0.75f,h*1.25f),a);
            }
            if(CurrentPhase==Phase.ContentWarning)
            {
                float duration = _settings.warningFadeIn + _settings.warningHold + _settings.warningFadeOut;
                float alpha = Ease(_phaseTime / Mathf.Max(.1f, _settings.warningFadeIn))
                    * Ease((duration - _phaseTime) / Mathf.Max(.1f, _settings.warningFadeOut));
                float scale = Mathf.Min(w / 960f, h / 720f);
                GUI.matrix = Matrix4x4.TRS(new Vector3((w - 960 * scale) / 2, (h - 720 * scale) / 2, 0), Quaternion.identity, Vector3.one * scale);
                GUI.color = new Color(1, 1, 1, alpha);
                GUI.DrawTexture(new Rect(400, 26, 160, 190), _contentWarning, ScaleMode.ScaleToFit, true);
                var notice = new GUIStyle(_status) { fontSize = 23, alignment = TextAnchor.UpperLeft, wordWrap = true };
                const string text = "警告，本游戏包含限制級內容，可能涉及下列要素：\n\n・成人向描寫（R18+）\n・心理或情感衝擊性描寫\n・爭議性或不適宜未成年之元素\n・游戏画面包含Sunyan和Andy进行CCB的细节描画\n\n（其中一部分可以通过安全模式的选择避免观看到画面细节）";
                PassiveLabel(new Rect(90, 242, 780, 380), text, notice);
                notice.fontSize = 17;
                PassiveLabel(new Rect(90, 640, 780, 36), "功能状态：安全模式尚未实装。", notice);
                GUI.matrix = Matrix4x4.identity;
                GUI.color = Color.white;
            }
            if(CurrentPhase==Phase.Waiting)PassiveLabel(new Rect(20,h*0.84f,w-40,50),"点击进入",_hint);
            if(CurrentPhase==Phase.Loading)PassiveLabel(new Rect(24,h*0.76f,w-48,50),"正在准备旅程…",_status);
            if(CurrentPhase==Phase.Error)PassiveLabel(new Rect(40,h*0.4f,w-80,h*0.3f),LastError+"\n请停止运行后重新进入；游戏进度没有重置。",_status);
        }
        finally
        {
            if (Event.current.type==EventType.Repaint && SceneReady)
            {
                if (_lastPresentedFrame!=Time.frameCount)
                {
                    _phaseRepaints++;
                    if (Application.isFocused) _warmupFrames++;
                }
                _lastPresentedFrame=Time.frameCount;
            }
            GUI.depth=depth;GUI.matrix=matrix;GUI.color=color;
        }
    }
    // Draw without an IMGUI control: mouse position cannot select a hover state.
    static void PassiveLabel(Rect rect, string text, GUIStyle style)
    {
        if (Event.current.type == EventType.Repaint)
            style.Draw(rect, new GUIContent(text), false, false, false, false);
    }

    void DrawLogo(Texture2D texture,Vector2 center,float width,float alpha)
    {
        if(texture==null||_logoMaterial==null||Event.current.type!=EventType.Repaint)return;
        float height=width*texture.height/texture.width;
        _logoMaterial.SetFloat("_Opacity",alpha);
        Graphics.DrawTexture(new Rect(Mathf.Round(center.x-width/2),Mathf.Round(center.y-height/2),width,height),texture,_logoMaterial);
    }
    void OnDestroy() { IsOpen=false;RestoreLightingAndListeners();if(_logoMaterial!=null)Destroy(_logoMaterial); }
}
