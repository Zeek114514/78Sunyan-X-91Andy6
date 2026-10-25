using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Bounded, non-colliding meadow detail. Never alters terrain, roads, save data or spawn rules.</summary>
public sealed class WorldArtDetails : MonoBehaviour
{
    readonly List<Object> _owned = new List<Object>();
    Material _surface;
    Material _grass;

    public Material CreateGrassMaterial(Material source)
    {
        _surface=source;
        var material=new Material(source){name="Wind meadow material",shader=Resources.Load<Shader>("Shaders/MeadowGrass")};
        material.SetFloat("_WindStrength",0.1f);_owned.Add(material);_grass=material;return material;
    }

    public void SetFog(Color color,float start,float end)
    {
        if(_grass==null)return;
        _grass.SetColor("_FogColor",color);_grass.SetFloat("_FogStart",start);_grass.SetFloat("_FogEnd",end);
    }

    public void BuildMeadowAccents(OpenWorld world)
    {
        var vertices=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();
        Vector3 spawn=world.SpawnPoint;int patches=0;
        for(int gx=-18;gx<=18;gx++)for(int gz=-18;gz<=18;gz++)
        {
            float x=spawn.x+gx*5+(LowPoly.Hash01(gx,gz,720)-0.5f)*4;
            float z=spawn.z+gz*5+(LowPoly.Hash01(gx,gz,721)-0.5f)*4;
            if(!OpenWorld.InBounds(x,z)||OpenWorld.IsRoad(x,z)||OpenWorld.OnTownStreet(x,z)||OpenWorld.InSiteKeepOut(x,z))continue;
            if(OpenWorld.Slope(x,z)>0.18f||OpenWorld.SnowFactor(x,z)>0.2f||OpenWorld.Height(x,z)<OpenWorld.WaterLevel+1)continue;
            if(Vector2.Distance(new Vector2(x,z),new Vector2(spawn.x,spawn.z))<5||LowPoly.Hash01(gx,gz,722)>0.15f||patches>=160)continue;
            patches++;
            Color petal=LowPoly.Hash01(gx,gz,723)<0.4f?new Color(0.94f,0.89f,0.68f):new Color(0.57f,0.75f,0.94f);
            if(OpenWorld.IsInazumaLand(x,z))petal=new Color(0.85f,0.63f,0.87f);
            for(int flower=0;flower<3;flower++)
            {
                float fx=x+(flower-1)*0.28f,fz=z+LowPoly.Hash01(gx,gz,724+flower)*0.5f;
                Vector3 root=new Vector3(fx,OpenWorld.Height(fx,fz),fz);
                float h=0.25f+LowPoly.Hash01(gx,gz,730+flower)*0.23f;
                LowPoly.Prism(vertices,colors,triangles,root,h,0.014f,0.009f,4,new Color(0.29f,0.46f,0.2f));
                Vector3 top=root+Vector3.up*h;
                for(int petalIndex=0;petalIndex<5;petalIndex++)
                {
                    float a=petalIndex*Mathf.PI*0.4f;
                    Vector3 direction=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                    Vector3 side=new Vector3(-direction.z,0,direction.x)*0.035f;
                    int first=vertices.Count;
                    vertices.Add(top);vertices.Add(top+direction*0.07f+side+Vector3.up*0.025f);
                    vertices.Add(top+direction*0.13f+Vector3.up*0.02f);vertices.Add(top+direction*0.07f-side+Vector3.up*0.025f);
                    for(int i=0;i<4;i++)colors.Add(petal.linear);
                    triangles.AddRange(new[]{first,first+1,first+2,first,first+2,first+3,first+2,first+1,first,first+3,first+2,first});
                }
            }
        }
        if(vertices.Count==0)return;
        var mesh=LowPoly.BuildMesh("Spawn meadow wildflowers",vertices,colors,triangles);_owned.Add(mesh);
        var go=new GameObject("Meadow wildflower accents");go.transform.SetParent(transform,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=_surface;renderer.shadowCastingMode=ShadowCastingMode.Off;
        Debug.Log("[WorldArt] Decorative flower patches="+patches+"; no colliders; original terrain retained.");
    }
    void OnDestroy(){foreach(var asset in _owned)if(asset!=null)Destroy(asset);}
}
