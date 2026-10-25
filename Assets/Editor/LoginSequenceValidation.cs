using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Local commands to the already-open editor; no editor processes are started or terminated.</summary>
[InitializeOnLoad]
public static class LoginSequenceValidation
{
    const string Folder="Artifacts/LoginValidation/";
    static bool _testing;
    static double _deadline,_stageTime;
    static int _stage,_minCameras;
    static float _idleZ;
    static Quaternion _idleRotation;
    static TitleScreen _title;
    static readonly List<string> Errors=new List<string>();
    static StringBuilder _report;
    const string PendingPlay="VoxelDemo.LoginValidation.ManualPlay.v2";
    static LoginSequenceValidation()
    {
        SessionState.EraseBool("VoxelDemo.LoginValidation.PendingPlay");
        EditorApplication.update+=Tick;Application.logMessageReceived+=OnLog;
        EditorApplication.playModeStateChanged+=state=>
        {
            if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(PendingPlay,false))
            { SessionState.EraseBool(PendingPlay);EditorApplication.delayCall+=Start; }
        };
    }
    static void OnLog(string message,string stack,LogType type){if(_testing&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert))Errors.Add(message);}
    static void Tick()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(File.Exists(Folder+"command.request"))
        {
            string command=File.ReadAllText(Folder+"command.request").Trim();File.Delete(Folder+"command.request");
            try
            {
                if(command=="stop")EditorApplication.isPlaying=false;
                else if(command=="play")EditorApplication.isPlaying=true;
                else if(command=="refresh")AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                else if(command=="validate")
                    File.WriteAllText(Folder+"command-error.txt","Automatic validation is disabled. Use the explicit VoxelDemo/Diagnostics/Validate login sequence menu.");
                Status(command);
            }
            catch(Exception error){File.WriteAllText(Folder+"command-error.txt",error.ToString());}
        }
        if(!_testing)return;
        try{Check();}catch(Exception error){Errors.Add(error.ToString());Finish(false);}
    }
    static void Status(string command)
    {
        var title=UnityEngine.Object.FindObjectOfType<TitleScreen>();
        File.WriteAllText(Folder+"editor-status.txt","utc="+DateTime.UtcNow.ToString("O")+"\ncommand="+command+"\nplaying="+EditorApplication.isPlaying+"\nunity="+Application.unityVersion+"\ntitle="+(title!=null?title.CurrentPhase.ToString():"absent")+"\ncompleted="+VoxelTutorial.Completed);
    }
    [MenuItem("VoxelDemo/Diagnostics/Validate login sequence")]
    public static void Start()
    {
        Directory.CreateDirectory(Folder);
        if(!EditorApplication.isPlaying)
        {
            SessionState.SetBool(PendingPlay,true);EditorApplication.isPlaying=true;return;
        }
        Errors.Clear();_stage=0;_minCameras=int.MaxValue;_stageTime=EditorApplication.timeSinceStartup;
        _deadline=_stageTime+180;_report=new StringBuilder("utc="+DateTime.UtcNow.ToString("O")+"\nunity="+Application.unityVersion+"\ncompletionBefore="+VoxelTutorial.Completed+"\n");
        _report.AppendLine("routingPolicy=always tutorial; no PlayerPrefs modified by validation");
        foreach(string name in new[]{"LoginCloudVolume","BrandingLogo","DynamicSky"})
        {
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/Shaders/"+name+".shader");
            if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Shader import failed: "+name);
        }
        _testing=true;
        EditorApplication.isPaused=false;
        if(!EditorApplication.isPlaying)EditorApplication.isPlaying=true;
    }
    static void Check()
    {
        if(EditorApplication.timeSinceStartup>_deadline)throw new Exception("Validation timeout at stage "+_stage);
        if(!EditorApplication.isPlaying)return;
        int cameras=UnityEngine.Object.FindObjectsOfType<Camera>().Count(c=>c.isActiveAndEnabled);
        _minCameras=Math.Min(_minCameras,cameras);
        if(cameras==0)throw new Exception("No enabled camera during login handover");
        if(_stage==0)
        {
            _title=UnityEngine.Object.FindObjectOfType<TitleScreen>();if(_title==null)return;
            if(_title.CurrentPhase==TitleScreen.Phase.Error)throw new Exception(_title.LastError);
            if(_title.CurrentPhase!=TitleScreen.Phase.Waiting)return;
            if(UnityEngine.Object.FindObjectOfType<OpenWorld>()!=null)throw new Exception("Open world generated before click");
            if(_title.gameObject.scene.name!="LoginSanctuary")throw new Exception("Not an independent login scene");
            _report.AppendLine("independentScene=LoginSanctuary; worldBeforeClick=absent");
            _idleZ=_title.LoginCamera.transform.position.z;_idleRotation=_title.LoginCamera.transform.rotation;
            _stageTime=EditorApplication.timeSinceStartup;_stage=1;
        }
        else if(_stage==1)
        {
            var position=_title.LoginCamera.transform.position;
            if(position.z<_idleZ||Mathf.Abs(position.x)>0.001f||Quaternion.Angle(_idleRotation,_title.LoginCamera.transform.rotation)>0.01f)throw new Exception("Idle camera moved off-axis or jumped");
            _idleZ=position.z;
            if(EditorApplication.timeSinceStartup-_stageTime<3)return;
            _report.AppendLine("idle=monotonic axis-aligned; observedSeconds=3");
            Capture(_title.LoginCamera,1280,720,"login-waiting.png");
            Capture(_title.LoginCamera,960,720,"login-4x3.png");
            Capture(_title.LoginCamera,1680,720,"login-21x9.png");
            Capture(_title.LoginCamera,720,1280,"login-portrait.png");
            ScreenCapture.CaptureScreenshot(Folder+"login-waiting-ui.png");
            _stageTime=EditorApplication.timeSinceStartup;_stage=2;
        }
        else if(_stage==2)
        {
            if(EditorApplication.timeSinceStartup-_stageTime<0.5)return;
            if(!_title.TryEnter()||_title.TryEnter())throw new Exception("Repeated click guard failed");
            _report.AppendLine("firstClick=accepted; duplicateClick=ignored");_stage=3;
        }
        else if(_stage==3)
        {
            float angle=Quaternion.Angle(Quaternion.identity,_title.Sanctuary.LeftDoor.localRotation);
            if(angle<45||_title.WhiteOpacity>0.5f)return;
            Capture(_title.LoginCamera,1280,720,"login-door-opening.png");
            ScreenCapture.CaptureScreenshot(Folder+"login-door-opening-ui.png");
            _report.AppendLine("openingAngle="+angle+"; whiteOpacity="+_title.WhiteOpacity);_stage=4;
        }
        else if(_stage==4)
        {
            if(_title!=null&&_title.CurrentPhase==TitleScreen.Phase.Error)throw new Exception(_title.LastError);
            if(_title!=null&&TitleScreen.IsOpen)return;
            bool tutorial=UnityEngine.Object.FindObjectOfType<VoxelTutorial>()!=null;
            bool world=UnityEngine.Object.FindObjectOfType<OpenWorld>()!=null;
            bool expectedTutorial=true;
            if(tutorial!=expectedTutorial||world==expectedTutorial)throw new Exception("Destination routing disagrees with completion flag");
            _report.AppendLine("destination="+(tutorial?"tutorial":"openWorld")+"; completionAfter="+VoxelTutorial.Completed);
            _report.AppendLine("minEnabledCameras="+_minCameras);
            Finish(Errors.Count==0);
        }
    }
    static void Capture(Camera camera,int width,int height,string name)
    {
        var texture=new RenderTexture(width,height,24);var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;float oldAspect=camera.aspect;
        try
        {
            camera.targetTexture=texture;camera.aspect=(float)width/height;camera.Render();RenderTexture.active=texture;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            File.WriteAllBytes(Folder+name,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }
        finally{camera.targetTexture=oldTarget;camera.aspect=oldAspect;RenderTexture.active=oldActive;texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
    }
    static void Finish(bool passed)
    {
        _testing=false;_report.AppendLine("runtimeErrors="+Errors.Count);foreach(string error in Errors)_report.AppendLine(error);
        _report.AppendLine("result="+(passed?"PASS":"FAIL"));File.WriteAllText(Folder+"runtime-validation.txt",_report.ToString());
    }
}
