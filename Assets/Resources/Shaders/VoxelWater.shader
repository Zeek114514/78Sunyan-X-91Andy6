Shader "Voxel/Water"
{
    Properties
    {
        _FogColor("Fog",Color)=(0.76,0.89,0.98,1)
        _FogStart("Fog start",Float)=600
        _FogEnd("Fog end",Float)=2200
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float4 _VoxelAmbientTint;float _VoxelEnvironmentActive;
            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;float _FogStart,_FogEnd;
            CBUFFER_END
            struct A {float4 positionOS:POSITION;float4 color:COLOR;};
            struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;half4 color:COLOR;};
            V vert(A i){V o;o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.color=i.color;return o;}
            half4 frag(V i):SV_Target
            {
                float2 uv=GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneDepth=LinearEyeDepth(SampleSceneDepth(uv),_ZBufferParams);
                float viewDepth=-TransformWorldToView(i.world).z;
                float depth=max(0,sceneDepth-viewDepth);
                float2 p=i.world.xz;
                float time=_Time.y;
                float2 wave=float2(cos(p.x*0.7+p.y*0.35+time*0.8),sin(p.y*0.9-p.x*0.2-time*0.65))*0.055;
                float3 normal=normalize(float3(wave.x,1,wave.y));
                float3 view=SafeNormalize(_WorldSpaceCameraPos-i.world);
                float fresnel=pow(1-saturate(dot(view,normal)),4);
                half3 shallow=half3(0.065,0.40,0.43),deep=half3(0.025,0.14,0.27);
                half3 c=lerp(shallow,deep,1-exp(-depth*0.15));
                c=lerp(c,_FogColor.rgb*0.75,fresnel*0.5);
                Light sun=GetMainLight();
                float sparkle=pow(saturate(dot(normal,SafeNormalize(sun.direction+view))),180);
                c+=sun.color*sparkle*0.35;
                float ripple=0.5+0.5*sin(p.x*2.4+p.y*1.7-time*1.8+sin(p.y*0.7+time));
                float foam=(1-smoothstep(0.05,0.65,depth))*smoothstep(0.25,0.75,ripple);
                c=lerp(c,half3(0.78,0.91,0.9),foam*0.72);
                c*=lerp(half3(1,1,1),_VoxelAmbientTint.rgb,_VoxelEnvironmentActive);
                float fog=saturate((viewDepth-_FogStart)/max(1,_FogEnd-_FogStart));
                c=lerp(c,_FogColor.rgb,fog);
                return half4(c,lerp(0.28,0.88,saturate(depth*0.18))+foam*0.08);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
