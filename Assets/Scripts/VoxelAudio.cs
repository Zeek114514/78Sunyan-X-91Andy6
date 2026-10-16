using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局音频：BGM（循环）+ 音效。
///
///   · 音效按"组"放：整组放在 Resources/Audio/&lt;组名&gt;/ 里，每次随机挑一个来播 ——
///     以后加音效只要往对应文件夹里丢文件，代码一行都不用改（组里只有一个就用那一个）；
///   · 现在的组：attack（命中）、coin（拾取 / 开箱）、click（UI）、teleport（传送）、heal（神像祝福 / 解锁）；
///   · BGM：Resources/Audio/bgm/overworld，进游戏就循环播；以后要按地区换曲，
///     把曲子按地区放进 bgm/ 再调 <see cref="PlayBgm"/> 就行；
///   · 组是懒加载的（第一次用到才读盘），所以启动不会因为几十个音效卡一下；
///   · 音频文件缺了不会报错刷屏：只警告一次，然后静默降级（和缺图标、缺模型一个套路）。
///
/// 音量与静音存 PlayerPrefs（和成就 / 祈愿 / 角色养成一样），对话栏里用 /mute 切换静音。
/// </summary>
public class VoxelAudio : MonoBehaviour
{
    /// <summary>场上那一份（其它系统调静态方法就行）</summary>
    public static VoxelAudio Instance { get; private set; }

    const string SfxRoot   = "Audio/";                  // 音效组的根目录
    const string BgmFolder = "Audio/bgm/";
    const string BgmDefault = "overworld";              // 默认循环曲
    const string MuteKey   = "CodelyDemo.Audio.Mute";

    public const float SfxVolume = 0.75f;
    public const float BgmVolume = 0.34f;

    static readonly Dictionary<string, AudioClip[]> _groups = new Dictionary<string, AudioClip[]>();
    static readonly HashSet<string> _missing = new HashSet<string>();      // 确实没有的组（免得每次刷警告）

    AudioSource _bgm;
    AudioSource _sfx;      // 2D 音效专用（BGM 那条是循环的，别混着用）
    bool _muted;

    /// <summary>现在是不是静音</summary>
    public static bool Muted => Instance != null && Instance._muted;

    public void Init(Transform camera)
    {
        Instance = this;
        _muted = PlayerPrefs.GetInt(MuteKey, 0) != 0;

        // 场景相机上得有个 AudioListener，不然什么声音都听不到（场景里那台相机是复用的，可能没挂）
        if (camera != null && camera.GetComponent<AudioListener>() == null && FindObjectOfType<AudioListener>() == null)
            camera.gameObject.AddComponent<AudioListener>();

        _bgm = gameObject.AddComponent<AudioSource>();
        _bgm.loop = true;
        _bgm.playOnAwake = false;
        _bgm.spatialBlend = 0f;                 // BGM 是 2D 的，不随位置衰减
        _bgm.volume = _muted ? 0f : BgmVolume;

        _sfx = NewOneShot();
        _sfx.volume = SfxVolume;

        PlayBgm(BgmDefault);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _groups.Clear();                        // 停播时把缓存清掉，下一局重新读
    }

    // ---------- BGM ----------

    /// <summary>放一首 BGM（循环）。名字是 Resources/Audio/bgm/ 下的文件名，没有就退回默认曲</summary>
    public void PlayBgm(string name)
    {
        if (_bgm == null) return;

        var clip = Resources.Load<AudioClip>(BgmFolder + name) ?? Resources.Load<AudioClip>(BgmFolder + BgmDefault);
        if (clip == null)
        {
            if (_missing.Add(BgmFolder + name))
                Debug.LogWarning($"[VoxelAudio] 没找到 BGM Resources/{BgmFolder}{name}，这次不出声。");
            return;
        }

        if (_bgm.clip == clip && _bgm.isPlaying) return;

        _bgm.clip = clip;
        _bgm.volume = _muted ? 0f : BgmVolume;
        _bgm.Play();
    }

    // ---------- 音效 ----------

    /// <summary>放一个 2D 音效（UI / 拾取 / 祝福这类"贴耳朵"的声音）</summary>
    public static void PlaySfx(string group, float volume = 1f, float pitch = 1f)
    {
        if (Instance == null || Instance._muted) return;

        var clip = Pick(group);
        if (clip == null) return;

        Instance._sfx.pitch = pitch;
        Instance._sfx.PlayOneShot(clip, SfxVolume * volume);
    }

    /// <summary>放一个 3D 音效（开箱 / 命中这类"从世界里传来"的声音），会随距离衰减</summary>
    public static void PlaySfxAt(string group, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        if (Instance == null || Instance._muted) return;

        var clip = Pick(group);
        if (clip == null) return;

        AudioSource.PlayClipAtPoint(clip, position, SfxVolume * volume);
    }

    /// <summary>UI 点击（各面板按钮统一用它，省得每处自己挑音效）</summary>
    public static void Click() => PlaySfx("click", 0.7f, Random.Range(0.97f, 1.04f));

    /// <summary>切换静音（对话栏 /mute），返回切换后是不是静音</summary>
    public static bool ToggleMute()
    {
        if (Instance == null) return false;

        Instance._muted = !Instance._muted;
        PlayerPrefs.SetInt(MuteKey, Instance._muted ? 1 : 0);
        PlayerPrefs.Save();

        if (Instance._bgm != null) Instance._bgm.volume = Instance._muted ? 0f : BgmVolume;
        return Instance._muted;
    }

    /// <summary>从某一组里随机挑一个（第一次用到才读盘；组不存在就返回 null 并只警告一次）</summary>
    static AudioClip Pick(string group)
    {
        if (string.IsNullOrEmpty(group)) return null;

        if (!_groups.TryGetValue(group, out var clips))
        {
            clips = Resources.LoadAll<AudioClip>(SfxRoot + group);
            _groups[group] = clips;             // 空数组也缓存

            if (clips == null || clips.Length == 0) _groups[group] = new AudioClip[0];
            clips = _groups[group];

            if (clips.Length == 0 && _missing.Add(group))
                Debug.LogWarning($"[VoxelAudio] 没找到音效组 Resources/{SfxRoot}{group}/（丢 wav 进那个文件夹即可）。");
        }

        if (clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }

    /// <summary>身上再挂一个 AudioSource 用来放 2D 音效（BGM 那条是循环专用的，别混着用）</summary>
    AudioSource NewOneShot()
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        return source;
    }
}
