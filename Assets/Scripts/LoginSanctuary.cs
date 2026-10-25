using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Project-authored 3D architecture. No world terrain or character controller is created here.</summary>
public sealed class LoginSanctuary : MonoBehaviour
{
    public const int LoginLayer = 30;
    public Transform LeftDoor { get; private set; }
    public Transform RightDoor { get; private set; }
    readonly List<Object> _owned = new List<Object>();
    Material _stone, _edge, _gold, _dark, _light, _cloud;
    Mesh _columnShaft, _columnFoot, _columnCrown, _tile;
    public Material Sky { get; private set; }
    public const float DoorZ = 80;

    Material Lit(string name, Color color, float metallic = 0)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", 0.26f); m.SetFloat("_Metallic", metallic);
        _owned.Add(m); return m;
    }

    public void Build(Camera camera)
    {
        _stone = Lit("Login pearl limestone", new Color(0.87f, 0.88f, 0.84f));
        _edge = Lit("Login carved ivory", new Color(0.97f, 0.95f, 0.86f));
        _gold = Lit("Login pale gold inlay", new Color(0.7f, 0.57f, 0.32f), 0.5f);
        _dark = Lit("Login recessed stone", new Color(0.53f, 0.62f, 0.65f));
        _light = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        _light.SetColor("_BaseColor", new Color(4, 3.9f, 3.5f)); _owned.Add(_light);
        _cloud = new Material(Shader.Find("Voxel/LoginCloudVolume")); _owned.Add(_cloud);
        Sky = new Material(Shader.Find("Voxel/DynamicSky")); _owned.Add(Sky);
        Sky.SetColor("_TopColor", new Color(0.32f, 0.57f, 0.8f)); Sky.SetColor("_HorizonColor", new Color(0.86f, 0.91f, 0.96f));
        Sky.SetVector("_SunDirection", new Vector3(-0.35f, 0.65f, 0.4f)); Sky.SetFloat("_Day", 1); Sky.SetFloat("_CloudCover", 0.28f);

        var lightGo = new GameObject("Login soft sunlight"); lightGo.transform.SetParent(transform, false); lightGo.layer = LoginLayer;
        var sun = lightGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.color = new Color(1, 0.95f, 0.84f);
        sun.intensity = 1.1f; sun.shadows = LightShadows.Soft; sun.cullingMask = 1 << LoginLayer;
        sun.transform.rotation = Quaternion.Euler(38, -28, 0);
        var fx = new GameObject("Login restrained bloom"); fx.transform.SetParent(transform, false); fx.layer = LoginLayer;
        var volume = fx.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 10;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>(); _owned.Add(profile); volume.sharedProfile = profile;
        var bloom = profile.Add<Bloom>(true); _owned.Add(bloom);
        bloom.intensity.Override(0.18f); bloom.threshold.Override(1.3f); bloom.scatter.Override(0.55f);
        var cameraData = camera.GetUniversalAdditionalCameraData(); cameraData.renderPostProcessing = true;
        cameraData.volumeLayerMask = 1 << LoginLayer; cameraData.antialiasing = AntialiasingMode.None;

        _tile = BevelledBox();
        _columnShaft = Lathe(new[] { new Vector2(0.74f,0),new Vector2(0.72f,0.3f),new Vector2(0.59f,0.6f),new Vector2(0.52f,10.9f),new Vector2(0.58f,11.2f) }, true);
        _columnFoot = Lathe(new[] { new Vector2(1.15f,0),new Vector2(1.15f,0.15f),new Vector2(0.97f,0.3f),new Vector2(0.97f,0.5f),new Vector2(0.8f,0.65f),new Vector2(0.8f,0.8f) }, false);
        _columnCrown = Lathe(new[] { new Vector2(0.55f,0),new Vector2(0.67f,0.25f),new Vector2(0.72f,0.38f),new Vector2(0.98f,0.7f),new Vector2(1.1f,0.83f),new Vector2(1.1f,1) }, false);

        Box("Floating foundation, chamfered edge", new Vector3(0,-1.3f,15),new Vector3(17,2,154),_stone);
        Box("Foundation gold stringcourse",new Vector3(0,-0.43f,15),new Vector3(17.15f,0.12f,154.15f),_gold);
        Box("Walkway upper moulding",new Vector3(0,-0.22f,15),new Vector3(17.4f,0.3f,154.4f),_edge);
        for(int z=-60;z<91;z+=3)
        {
            for(int x=-6;x<=6;x+=3) Box("Paved limestone course",new Vector3(x,0,z),new Vector3(2.96f,0.16f,2.96f),((z/3+x)%3==0)?_edge:_stone);
            foreach(float side in new[]{-1f,1f})
            {
                Box("Long gold guide inlay",new Vector3(side*5,0.09f,z),new Vector3(0.045f,0.02f,3),_gold);
                Box("Edge balustrade plinth",new Vector3(side*7.65f,0.22f,z),new Vector3(1.15f,0.5f,2.96f),_stone);
            }
        }
        for(int z=-48;z<=72;z+=12) foreach(float side in new[]{-1f,1f}) Column(new Vector3(side*7.25f,0.5f,z),1);
        // Repeated overhead entablature and distinct capitals establish a real vanishing point.
        foreach(float side in new[]{-1f,1f}) {
            Box("Continuous cornice",new Vector3(side*7.25f,12.7f,12),new Vector3(2.35f,0.65f,142),_edge);
            Box("Cornice gilt recess",new Vector3(side*7.25f,12.35f,12),new Vector3(2.15f,0.12f,142),_gold);
        }
        for(int i=0;i<5;i++) Box("Sanctuary broad stair",new Vector3(0,0.13f+i*0.1f,71+i*1.1f),new Vector3(13-i*0.25f,0.22f+i*0.2f,3.4f),_edge);
        BuildGate();
        // Several banks occupy different heights and depths, all ray-marched as 3D density fields.
        CloudBank(new Vector3(0,-32,180),new Vector3(1100,58,1500),0);
        CloudBank(new Vector3(-115,-13,190),new Vector3(150,28,650),7);
        CloudBank(new Vector3(130,-17,300),new Vector3(175,32,800),19);
        // Silhouettes beyond the main gate: smaller temples fade naturally with aerial perspective.
        for(int i=0;i<7;i++)
        {
            float side=i%2==0?-1:1, x=side*(65+i*14),z=185+i*39;
            Box("Distant temple terrace",new Vector3(x,-3,z),new Vector3(27,2,22),_stone);
            for(int k=0;k<4;k++) Column(new Vector3(x-9+k*6,-2,z),0.8f);
            Box("Distant temple pediment",new Vector3(x,8.7f,z),new Vector3(28,1,7),_edge);
        }
    }

    void Column(Vector3 position,float scale)
    {
        var root=new GameObject("Fluted temple column").transform; root.SetParent(transform,false);root.localPosition=position;root.localScale=Vector3.one*scale;root.gameObject.layer=LoginLayer;
        Part("Turned base",_columnFoot,_edge,root,Vector3.zero,Vector3.one);
        Part("Fluted tapered shaft",_columnShaft,_stone,root,Vector3.up*0.8f,Vector3.one);
        Part("Lotus moulded capital",_columnCrown,_edge,root,Vector3.up*12,Vector3.one);
        Part("Capital gilt ring",_columnFoot,_gold,root,Vector3.up*11.85f,new Vector3(0.68f,0.12f,0.68f));
    }

    void BuildGate()
    {
        foreach(float side in new[]{-1f,1f})
        {
            Column(new Vector3(side*5.7f,0.5f,DoorZ),1.15f);
            Box("Door jamb",new Vector3(side*4.75f,7.1f,DoorZ),new Vector3(1.1f,14.2f,1.5f),_edge);
            Box("Jamb gold fillet",new Vector3(side*4.25f,7.1f,DoorZ-0.8f),new Vector3(0.12f,14.2f,0.08f),_gold);
        }
        Part("True stone archivolt",ArchBand(4.15f,5.4f,10.5f,1.2f),_edge,transform,new Vector3(0,0.5f,DoorZ),Vector3.one);
        Part("Gold arch inlay",ArchBand(4.35f,4.46f,10.5f,0.05f),_gold,transform,new Vector3(0,0.5f,DoorZ-0.66f),Vector3.one);
        // A luminous surface actually sits behind the door leaves, not as a full-screen flash.
        var glow=new List<Vector2>{new Vector2(-4.05f,0),new Vector2(4.05f,0)};
        for(int i=0;i<=40;i++){float a=i*Mathf.PI/40;glow.Add(new Vector2(Mathf.Cos(a)*4.05f,10.5f+Mathf.Sin(a)*4));}
        Part("Arched inner radiance",Extrude(glow,0.08f),_light,transform,new Vector3(0,0.5f,DoorZ+2),Vector3.one);
        LeftDoor=DoorLeaf(-1); RightDoor=DoorLeaf(1);
    }

    Transform DoorLeaf(int side)
    {
        var hinge=new GameObject(side<0?"Left door hinge":"Right door hinge").transform;
        hinge.SetParent(transform,false);hinge.localPosition=new Vector3(side*4.05f,0.5f,DoorZ-0.06f);hinge.gameObject.layer=LoginLayer;
        // Each polygon follows its half of the arch and is extruded to a real, closed solid.
        var points=new List<Vector2>{new Vector2(0,0),new Vector2(-side*4.03f,0),new Vector2(-side*4.03f,14.5f)};
        for(int i=1;i<=16;i++) {float a=Mathf.Lerp(Mathf.PI/2,side<0?Mathf.PI:0,i/16f);points.Add(new Vector2(Mathf.Cos(a)*4.03f-side*4.05f,10.5f+Mathf.Sin(a)*4));}
        Part("Carved arched door leaf",Extrude(points,0.52f),_stone,hinge,Vector3.zero,Vector3.one);
        foreach(float y in new[]{1.0f,6f,10f}) Box("Leaf gilt crosspiece",new Vector3(-side*2.02f,y,-0.32f),new Vector3(3.6f,0.12f,0.1f),_gold,hinge);
        foreach(float x in new[]{-side*0.4f,-side*3.6f}) Box("Leaf raised border",new Vector3(x,5.4f,-0.32f),new Vector3(0.12f,9.5f,0.14f),_gold,hinge);
        Box("Recessed central panel",new Vector3(-side*2.03f,5.5f,-0.285f),new Vector3(2.7f,7.6f,0.08f),_dark,hinge);
        var diamond=Box("Door star emblem",new Vector3(-side*2.03f,6,-0.39f),new Vector3(1,1,0.13f),_gold,hinge);diamond.localRotation=Quaternion.Euler(0,0,45);
        Box("Door vertical handle",new Vector3(-side*3.45f,5.2f,-0.52f),new Vector3(0.14f,1.25f,0.18f),_edge,hinge);
        return hinge;
    }

    public void SetDoor(float t) { LeftDoor.localRotation=Quaternion.Euler(0,-108*t,0);RightDoor.localRotation=Quaternion.Euler(0,108*t,0); }
    void CloudBank(Vector3 position,Vector3 scale,float seed)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Volumetric cloud bank";go.layer=LoginLayer;go.transform.SetParent(transform,false);go.transform.localPosition=position;go.transform.localScale=scale;
        var collider=go.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
        var m=new Material(_cloud);m.SetFloat("_Seed",seed);_owned.Add(m);go.GetComponent<Renderer>().sharedMaterial=m;
    }
    Transform Box(string name,Vector3 position,Vector3 scale,Material material,Transform parent=null) => Part(name,_tile,material,parent==null?transform:parent,position,scale);
    Transform Part(string name,Mesh mesh,Material material,Transform parent,Vector3 position,Vector3 scale)
    {
        var go=new GameObject(name);go.layer=LoginLayer;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;return go.transform;
    }
    Mesh Finish(string name,List<Vector3> vertices,List<int> triangles)
    {
        var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();_owned.Add(mesh);return mesh;
    }
    Mesh Lathe(Vector2[] profile,bool fluted)
    {
        const int count=64;var v=new List<Vector3>();var t=new List<int>();
        for(int j=0;j<profile.Length;j++) for(int i=0;i<=count;i++){float a=i*Mathf.PI*2/count,r=profile[j].x*(fluted?1+0.035f*Mathf.Cos(a*16):1);v.Add(new Vector3(Mathf.Cos(a)*r,profile[j].y,Mathf.Sin(a)*r));}
        for(int j=0;j<profile.Length-1;j++)for(int i=0;i<count;i++){int a=j*(count+1)+i,b=a+count+1;t.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
        return Finish("Turned and fluted stone",v,t);
    }
    Mesh BevelledBox()
    {
        var p=new List<Vector2>{new Vector2(-0.46f,-0.5f),new Vector2(0.46f,-0.5f),new Vector2(0.5f,-0.46f),new Vector2(0.5f,0.46f),new Vector2(0.46f,0.5f),new Vector2(-0.46f,0.5f),new Vector2(-0.5f,0.46f),new Vector2(-0.5f,-0.46f)};
        return Extrude(p,1);
    }
    Mesh Extrude(List<Vector2> polygon,float depth)
    {
        // Convex shapes only (the chamfered blocks and each half-arch leaf).
        float area=0;for(int i=0;i<polygon.Count;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Count];area+=a.x*b.y-b.x*a.y;}if(area<0)polygon.Reverse();
        var v=new List<Vector3>();var t=new List<int>();int n=polygon.Count;
        foreach(float z in new[]{-depth/2,depth/2})foreach(var p in polygon)v.Add(new Vector3(p.x,p.y,z));
        for(int i=1;i<n-1;i++)t.AddRange(new[]{0,i+1,i,n,n+i,n+i+1});
        for(int i=0;i<n;i++){int j=(i+1)%n;t.AddRange(new[]{i,j,n+i,j,n+j,n+i});}
        return Finish("Extruded carved solid",v,t);
    }
    Mesh ArchBand(float inner,float outer,float spring,float depth)
    {
        var v=new List<Vector3>();var t=new List<int>();const int segments=40;
        for(int i=0;i<=segments;i++){float a=i*Mathf.PI/segments;foreach(float z in new[]{-depth/2,depth/2})foreach(float r in new[]{inner,outer})v.Add(new Vector3(Mathf.Cos(a)*r,spring+Mathf.Sin(a)*r,z));}
        for(int i=0;i<segments;i++){int a=i*4,b=a+4;t.AddRange(new[]{a,b+1,b,a,a+1,b+1,a+2,b+2,b+3,a+2,b+3,a+3,a,b,b+2,a,b+2,a+2,a+1,a+3,b+3,a+1,b+3,b+1});}
        for(int i=0;i<t.Count;i+=3){int swap=t[i+1];t[i+1]=t[i+2];t[i+2]=swap;}
        return Finish("Solid arched archivolt",v,t);
    }
    void OnDestroy(){foreach(var asset in _owned)if(asset!=null)Destroy(asset);}
}
