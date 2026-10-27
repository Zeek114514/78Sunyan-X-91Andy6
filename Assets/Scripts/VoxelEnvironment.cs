using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Shared tutorial/world clock, continuous sky lighting and local weather.</summary>
public sealed class VoxelEnvironment : MonoBehaviour
{
    public enum Weather { Clear, Cloudy, Rain, Storm, Fog }
    public const float DayDurationSeconds = 1200f;
    static float _hour = 8f, _weatherRemaining;
    static Weather _weather;
    static readonly string[] WeatherNames = { "晴", "多云", "雨", "雷雨", "雾" };
    public static float Hour => _hour;
    public static Weather CurrentWeather => _weather;
    Camera _camera;
    Light _sun;
    OpenWorld _world;
    Material _sky, _previousSky, _rainMaterial;
    ParticleSystem _rain;
    float _cloud, _wet, _fog, _lightningTimer = 15, _flash, _roofCheck;
    bool _sheltered;
    Color _oldAmbient, _oldFogColor;
    AmbientMode _oldAmbientMode;
    bool _oldFog;
    float _oldFogDensity;
    FogMode _oldFogMode;
    GUIStyle _clock;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() { _hour = 8; _weatherRemaining = 0; _weather = Weather.Clear; Shader.SetGlobalColor("_VoxelAmbientTint", Color.white); Shader.SetGlobalFloat("_VoxelEnvironmentActive", 0); }

    public void Init(Camera camera, Light sun, OpenWorld world)
    {
        _camera = camera; _sun = sun; _world = world;
        if (_sun == null)
        {
            foreach (var light in FindObjectsOfType<Light>())
                if (light.type == LightType.Directional && light.enabled) { _sun = light; break; }
        }
        if (_sun == null)
        {
            var go = new GameObject("Day night sunlight"); go.transform.SetParent(transform, false);
            _sun = go.AddComponent<Light>(); _sun.type = LightType.Directional;
        }
        _sun.shadows = LightShadows.Soft;
        _previousSky = RenderSettings.skybox;
        _oldAmbient = RenderSettings.ambientLight; _oldAmbientMode = RenderSettings.ambientMode;
        _oldFog = RenderSettings.fog; _oldFogColor = RenderSettings.fogColor; _oldFogDensity = RenderSettings.fogDensity;
        _oldFogMode = RenderSettings.fogMode;
        var shader = Shader.Find("Voxel/DynamicSky");
        if (shader != null) { _sky = new Material(shader); RenderSettings.skybox = _sky; }
        _camera.clearFlags = CameraClearFlags.Skybox;
        BuildRain();
        if (_weatherRemaining <= 0) ChooseWeather();
        _cloud = TargetCloud; _wet = TargetWet; _fog = TargetFog;
        UpdateSky(0);
    }

    float TargetCloud => _weather == Weather.Clear ? 0.12f : _weather == Weather.Cloudy ? 0.55f : 0.9f;
    float TargetWet => _weather == Weather.Rain ? 0.55f : _weather == Weather.Storm ? 1f : 0f;
    float TargetFog => _weather == Weather.Fog ? 1f : _weather == Weather.Storm ? 0.4f : _weather == Weather.Rain ? 0.2f : 0f;
    static void ChooseWeather()
    {
        float roll = Random.value;
        Weather next = roll < 0.38f ? Weather.Clear : roll < 0.65f ? Weather.Cloudy : roll < 0.83f ? Weather.Rain : roll < 0.91f ? Weather.Storm : Weather.Fog;
        if (next == _weather) next = (Weather)(((int)next + 1) % WeatherNames.Length);
        _weather = next; _weatherRemaining = Random.Range(120f, 240f);
    }

    void Update()
    {
        if (_camera == null || _sun == null) return;
        // Menus and the tutorial welcome/pause screen pause time and weather together.
        float dt = VoxelPanels.InputBlocked ? 0 : Time.deltaTime;
        _hour = Mathf.Repeat(_hour + dt * 24f / DayDurationSeconds, 24);
        _weatherRemaining -= dt;
        if (_weatherRemaining <= 0) ChooseWeather();
        _cloud = Mathf.MoveTowards(_cloud, TargetCloud, dt / 12f);
        _wet = Mathf.MoveTowards(_wet, TargetWet, dt / 8f);
        _fog = Mathf.MoveTowards(_fog, TargetFog, dt / 12f);
        _flash = Mathf.Max(0, _flash - dt * 6);
        if (_weather == Weather.Storm && dt > 0)
        {
            _lightningTimer -= dt;
            if (_lightningTimer <= 0) { _flash = 0.7f; _lightningTimer = Random.Range(14f, 28f); }
        }
        UpdateSky(dt);
        _rain.transform.position = _camera.transform.position + Vector3.up * 14;
        _roofCheck -= Time.deltaTime;
        if (_roofCheck <= 0) { _roofCheck = 0.5f; _sheltered = Physics.Raycast(_camera.transform.position, Vector3.up, 18f); }
        var emission = _rain.emission;
        emission.rateOverTime = _sheltered || dt == 0 ? 0 : _wet * 650;
        if (dt == 0 && !_rain.isPaused) _rain.Pause();
        else if (dt > 0 && !_rain.isPlaying) _rain.Play();
    }

    void UpdateSky(float dt)
    {
        float elevation = Mathf.Sin((_hour - 6) * Mathf.PI / 12);
        float day = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.16f, 0.3f, elevation));
        float dusk = Mathf.Clamp01(1 - Mathf.Abs(elevation) * 5) * day;
        Color top = Color.Lerp(new Color(0.012f, 0.022f, 0.07f), new Color(0.15f, 0.46f, 0.85f), day);
        Color horizon = Color.Lerp(new Color(0.055f, 0.075f, 0.16f), new Color(0.68f, 0.85f, 0.97f), day);
        horizon = Color.Lerp(horizon, new Color(1, 0.42f, 0.2f), dusk * 0.8f);
        Color overcast = Color.Lerp(new Color(0.035f, 0.045f, 0.07f), new Color(0.42f, 0.48f, 0.53f), day);
        top = Color.Lerp(top, overcast, _cloud * 0.65f); horizon = Color.Lerp(horizon, overcast, _cloud * 0.65f);
        float angle = _hour * 15 - 90;
        Quaternion sunRotation = Quaternion.Euler(angle, -35, 0);
        Vector3 sunDirection = -(sunRotation * Vector3.forward);
        _sun.transform.rotation = elevation >= 0 ? sunRotation : Quaternion.Euler(angle + 180, -35, 0);
        _sun.color = Color.Lerp(new Color(0.47f, 0.61f, 0.95f), Color.Lerp(Color.white, new Color(1, 0.58f, 0.3f), dusk), day);
        _sun.intensity = Mathf.Lerp(0.13f, 1.15f, day) * (1 - _cloud * 0.6f) + _flash;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.Lerp(new Color(0.16f, 0.2f, 0.32f), new Color(0.66f, 0.71f, 0.78f), day) * (1 - _cloud * 0.22f);
        Color fogColor = horizon;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fogColor; RenderSettings.fogDensity = Mathf.Lerp(0.0003f, 0.014f, _fog);
        if (_world != null) _world.SetFog(fogColor, Mathf.Lerp(600, 32, _fog), Mathf.Lerp(2200, 200, _fog));
        Color tint = Color.Lerp(new Color(0.24f, 0.3f, 0.48f), Color.white, day) * (1 - _cloud * 0.2f);
        Shader.SetGlobalColor("_VoxelAmbientTint", tint);
        Shader.SetGlobalFloat("_VoxelEnvironmentActive", 1);
        if (_sky != null)
        {
            _sky.SetColor("_TopColor", top); _sky.SetColor("_HorizonColor", horizon);
            _sky.SetVector("_SunDirection", sunDirection); _sky.SetFloat("_Day", day);
            _sky.SetFloat("_CloudCover", _cloud); _sky.SetFloat("_Flash", _flash);
        }
        _camera.backgroundColor = horizon;
    }

    void BuildRain()
    {
        var go = new GameObject("Local weather rain"); go.transform.SetParent(transform, false);
        _rain = go.AddComponent<ParticleSystem>(); _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = _rain.main; main.loop = true; main.startLifetime = 1.1f; main.startSpeed = 22;
        main.startSize = 0.035f; main.startColor = new Color(0.7f, 0.82f, 0.95f, 0.55f);
        main.maxParticles = 1000; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = _rain.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(32, 1, 32);
        shape.rotation = new Vector3(90, 0, 0);
        var emission = _rain.emission; emission.rateOverTime = 0;
        var renderer = _rain.GetComponent<ParticleSystemRenderer>();
        // A Resources material keeps the particle shader available in Windows builds too.
        _rainMaterial = new Material(Resources.Load<Material>("RuntimeShaderReferences/WeatherRain"));
        _rainMaterial.SetFloat("_Surface", 1); _rainMaterial.SetFloat("_Blend", 0);
        _rainMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); _rainMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        _rainMaterial.SetFloat("_ZWrite", 0); _rainMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _rainMaterial.renderQueue = 3000; renderer.sharedMaterial = _rainMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 1.5f; renderer.velocityScale = 0.015f;
        _rain.Play();
    }

    void OnGUI()
    {
        if (TitleScreen.IsOpen || VoxelPanels.ModalOpen || VoxelPanels.ChatOpen) return;
        VoxelTypography.Apply();
        if (_clock == null) _clock = new GUIStyle(GUI.skin.label) { fontSize = 19, alignment = TextAnchor.MiddleCenter };
        int minutes = Mathf.FloorToInt(_hour * 60) % 1440;
        string period = _hour < 5 ? "深夜" : _hour < 7 ? "清晨" : _hour < 12 ? "上午" : _hour < 17 ? "午后" : _hour < 19 ? "傍晚" : "夜晚";
        var box = new Rect(Mathf.Round((Screen.width - 280) / 2f), 12, 280, 34);
        VoxelHud.FillRect(box, new Color(0.03f, 0.06f, 0.12f, 0.7f));
        VoxelHud.LabelShadow(box, $"{minutes / 60:00}:{minutes % 60:00}  {period} · {WeatherNames[(int)_weather]}", _clock);
    }

    void OnDestroy()
    {
        if (RenderSettings.skybox == _sky) RenderSettings.skybox = _previousSky;
        RenderSettings.ambientMode = _oldAmbientMode; RenderSettings.ambientLight = _oldAmbient;
        RenderSettings.fog = _oldFog; RenderSettings.fogColor = _oldFogColor; RenderSettings.fogDensity = _oldFogDensity;
        RenderSettings.fogMode = _oldFogMode;
        Shader.SetGlobalColor("_VoxelAmbientTint", Color.white);
        Shader.SetGlobalFloat("_VoxelEnvironmentActive", 0);
        if (_sky != null) Destroy(_sky); if (_rainMaterial != null) Destroy(_rainMaterial);
    }
}
