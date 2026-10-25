Shader "Voxel/MeadowGrass"
{
    Properties
    {
        _FogColor("Fog",Color)=(0.76,0.89,0.98,1)
        _FogStart("Fog start",Float)=600
        _FogEnd("Fog end",Float)=2200
        _WindStrength("Wind metres",Range(0,0.3))=0.1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "StylizedLighting.hlsl"
            float4 _VoxelAmbientTint;float _VoxelEnvironmentActive;
            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;float _FogStart,_FogEnd,_WindStrength;
            CBUFFER_END
            struct A {float4 positionOS:POSITION;float4 color:COLOR;};
            struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;half3 color:COLOR;float fog:TEXCOORD1;};
            V vert(A i)
            {
                V o;float3 p=TransformObjectToWorld(i.positionOS.xyz);
                float wave=sin(p.x*0.45+p.z*0.31+_Time.y*1.6)+sin(p.z*0.67-_Time.y*1.1)*0.35;
                p.xz+=float2(1,0.45)*wave*_WindStrength*i.color.a;
                o.world=p;o.positionCS=TransformWorldToHClip(p);o.color=i.color.rgb;
                o.fog=saturate((o.positionCS.w-_FogStart)/max(0.01,_FogEnd-_FogStart));return o;
            }
            half4 frag(V i):SV_Target
            {
                half3 c=i.color*VoxelSoftLight(i.world,half3(0,1,0));
                c*=lerp(half3(1,1,1),_VoxelAmbientTint.rgb,_VoxelEnvironmentActive);
                return half4(lerp(c,_FogColor.rgb,i.fog),1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
