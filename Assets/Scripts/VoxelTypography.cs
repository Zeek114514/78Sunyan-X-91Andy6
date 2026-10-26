using UnityEngine;

/// <summary>Bundled Chinese typography shared by every runtime IMGUI screen.</summary>
public static class VoxelTypography
{
    const string FontPath = "Fonts/HYWenHei-85W";
    const string FallbackFontPath = "Fonts/NotoSansSC-Medium";
    static Font _font;
    static bool _fontRequested;
    static GUISkin _sourceSkin, _gameSkin;

    public static Font GameFont
    {
        get
        {
            if (!_fontRequested)
            {
                _fontRequested = true;
                _font = Resources.Load<Font>(FontPath);
                if (_font == null)
                {
                    _font = Resources.Load<Font>(FallbackFontPath);
                    Debug.LogWarning("[VoxelDemo] Missing HYWenHei-85W font at Resources/" + FontPath
                        + "; using Noto Sans SC fallback.");
                }
            }
            return _font;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        if (_gameSkin != null) Object.Destroy(_gameSkin);
        _font = null;
        _fontRequested = false;
        _sourceSkin = null;
        _gameSkin = null;
    }

    // Call inside OnGUI, before any cached GUIStyle is created. Clone the skin so
    // runtime typography never changes the editor's shared default skin.
    public static void Apply()
    {
        var font = GameFont;
        if (font == null || GUI.skin == _gameSkin) return;
        if (_gameSkin == null || _sourceSkin != GUI.skin)
        {
            if (_gameSkin != null) Object.Destroy(_gameSkin);
            _sourceSkin = GUI.skin;
            _gameSkin = Object.Instantiate(_sourceSkin);
            _gameSkin.name = "VoxelDemo Typography";
            _gameSkin.hideFlags = HideFlags.HideAndDontSave;
            _gameSkin.font = font;
            var styles = new[] {
                _gameSkin.box, _gameSkin.label, _gameSkin.button, _gameSkin.toggle,
                _gameSkin.textField, _gameSkin.textArea, _gameSkin.window,
                _gameSkin.horizontalSlider, _gameSkin.horizontalSliderThumb,
                _gameSkin.verticalSlider, _gameSkin.verticalSliderThumb,
                _gameSkin.horizontalScrollbar, _gameSkin.horizontalScrollbarThumb,
                _gameSkin.horizontalScrollbarLeftButton, _gameSkin.horizontalScrollbarRightButton,
                _gameSkin.verticalScrollbar, _gameSkin.verticalScrollbarThumb,
                _gameSkin.verticalScrollbarUpButton, _gameSkin.verticalScrollbarDownButton,
                _gameSkin.scrollView
            };
            foreach (var style in styles) style.font = font;
            foreach (var style in _gameSkin.customStyles) style.font = font;
        }
        GUI.skin = _gameSkin;
    }
}
