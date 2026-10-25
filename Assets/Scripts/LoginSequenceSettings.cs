using UnityEngine;

[CreateAssetMenu(menuName = "VoxelDemo/Login sequence settings")]
public sealed class LoginSequenceSettings : ScriptableObject
{
    [Header("Company splash")]
    public float companyFadeIn = 0.8f, companyHold = 1.3f, companyFadeOut = 0.8f;
    [Header("Game logo on white")]
    public float gameFadeIn = 0.8f, gameHold = 1.3f, gameFadeOut = 0.8f;
    [Header("Content notice on white")]
    public float warningFadeIn = .8f, warningHold = 8f, warningFadeOut = .8f;
    [Header("Reveal and entry")]
    public float sceneReveal = 1.5f, approachDuration = 8f;
    [Range(0, 1)] public float doorStart = 0.40f, doorEnd = 0.88f, whiteStart = 0.72f;
    public float loadingFade = 1f, cameraTimeout = 30f;
    [Header("Presentation protection")]
    [Range(0.016f, 0.1f)] public float maximumAnimationStep = 0.05f;
    [Header("Camera: all distances in metres")]
    public float cameraStartZ = -32f, cameraEndZ = 84f, cameraHeight = 3.2f, verticalFov = 48f;
    public float idleDistance = 3f, idleTimeConstant = 100f;
    [Header("Audio")]
    public string musicResource = "Audio/bgm/theme";
    [Range(0, 1)] public float musicVolume = 0.28f;
    public float musicFadeIn = 3f;
}
