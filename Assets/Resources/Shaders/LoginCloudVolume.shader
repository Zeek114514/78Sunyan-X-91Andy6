Shader "Voxel/LoginCloudVolume"
{
    Properties { _Seed("Seed",Float)=0 }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Back ZWrite Off Blend One OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _Seed;
            struct A {float4 positionOS:POSITION;};
            struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;};
            V vert(A i){V o;o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);return o;}
            float hash(float3 p){return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);}
            float noise(float3 p){float3 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(lerp(hash(a),hash(a+float3(1,0,0)),f.x),lerp(hash(a+float3(0,1,0)),hash(a+float3(1,1,0)),f.x),f.y),lerp(lerp(hash(a+float3(0,0,1)),hash(a+float3(1,0,1)),f.x),lerp(hash(a+float3(0,1,1)),hash(a+1),f.x),f.y),f.z);}
            half4 frag(V i):SV_Target
            {
                float3 origin=TransformWorldToObject(_WorldSpaceCameraPos),direction=normalize(i.world-_WorldSpaceCameraPos);
                float3 ray=mul((float3x3)unity_WorldToObject,direction);
                float3 inv=rcp(sign(ray)*max(abs(ray),1e-7));
                float3 a=(-0.5-origin)*inv,b=(0.5-origin)*inv;
                float3 lo=min(a,b),hi=max(a,b);float begin=max(0,max(lo.x,max(lo.y,lo.z))),end=min(hi.x,min(hi.y,hi.z));
                float stepSize=max(0,end-begin)/32;
                float trans=1;float3 result=0;
                [loop] for(int s=0;s<32;s++)
                {
                    float distance=begin+(s+0.5)*stepSize;float3 local=origin+ray*distance;
                    float3 world=mul(unity_ObjectToWorld,float4(local,1)).xyz;
                    float3 p=world*0.023+float3(_Time.y*0.003,_Seed,_Seed*0.3);
                    float n=noise(p)*0.72+noise(p*2.4)*0.28;
                    float density=saturate((n-0.24)*2.4)*smoothstep(0,0.23,0.5-local.y)*smoothstep(0,0.15,local.y+0.5);
                    float opacity=1-exp(-density*stepSize*0.13);
                    float light=lerp(0.72,1,saturate(local.y+0.55));
                    result+=trans*opacity*lerp(float3(0.67,0.77,0.86),float3(1,0.99,0.95),light);
                    trans*=1-opacity;if(trans<0.015)break;
                }
                return half4(result,1-trans);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
