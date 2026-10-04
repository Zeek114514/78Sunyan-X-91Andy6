// 极简 URP 顶点色水体着色器：半透明、双面渲染、不写深度
// 和 Voxel/Demo 同构，只是 alpha 来自顶点色，远处同样融进雾里
Shader "Voxel/Water"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.62, 0.82, 0.94, 1)
        _FogStart ("Fog Start", Float) = 34
        _FogEnd   ("Fog End",   Float) = 96
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent"
        }

        Pass
        {
            Name "VoxelWater"
            Cull Off          // 水下抬头也要能看到水面
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float  _FogStart;
                float  _FogEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float  fogFactor   : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color       = IN.color;

                float viewDepth = OUT.positionHCS.w;
                OUT.fogFactor   = saturate((viewDepth - _FogStart) / max(1e-4, _FogEnd - _FogStart));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 c = lerp(IN.color.rgb, _FogColor.rgb, IN.fogFactor);
                return half4(c, IN.color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
