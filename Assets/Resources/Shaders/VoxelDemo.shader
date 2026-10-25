Shader "Voxel/Demo"
{
    Properties
    {
        _Tint     ("受击染色（默认白；怪物被打时打红）", Color) = (1,1,1,1)
        _FogColor ("Fog Color", Color) = (0.62, 0.82, 0.94, 1)
        _FogStart ("Fog Start", Float) = 34
        _FogEnd   ("Fog End",   Float) = 96
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        Pass
        {
            Name "VoxelUnlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "StylizedLighting.hlsl"

            float4 _VoxelAmbientTint;
            float _VoxelEnvironmentActive;

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float4 _FogColor;
                float  _FogStart;
                float  _FogEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 color      : COLOR;
            };

            struct Varyings
            {
                float3 world : TEXCOORD3;
                float3 normal : TEXCOORD4;
                float4 positionHCS : SV_POSITION;
                float3 color       : COLOR;
                float  fogFactor   : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.world = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color       = IN.color;

                // positionHCS.w 就是视图空间深度，用它做线性雾
                float viewDepth = OUT.positionHCS.w;
                OUT.fogFactor   = saturate((viewDepth - _FogStart) / max(1e-4, _FogEnd - _FogStart));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 顶点色先乘受击染色（默认白色 = 不变），再压雾
                float3 c = lerp(IN.color * _Tint.rgb * VoxelSoftLight(IN.world, IN.normal) * lerp(float3(1,1,1), _VoxelAmbientTint.rgb, _VoxelEnvironmentActive), _FogColor.rgb, IN.fogFactor);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
        UsePass "Hidden/Voxel/StylizedShadow/ShadowCaster"
        UsePass "Hidden/Voxel/StylizedShadow/DepthOnly"
    }

    Fallback Off
}
