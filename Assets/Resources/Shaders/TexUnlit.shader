// 单张贴图 × 顶点色（不参与光照，只用线性雾）：给城的墙体、屋顶用
// 和 Voxel/Demo 一样极简，尽量少依赖 URP 内部（降低版本差异风险）
Shader "Voxel/TexUnlit"
{
    Properties
    {
        _MainMap  ("贴图", 2D) = "white" {}
        _TexScale ("贴图密度（每米重复几次）", Float) = 0.25
        _TexGain  ("贴图亮度增益", Float) = 1.5
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
            Name "VoxelTexUnlit"
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float  _FogStart;
                float  _FogEnd;
                float  _TexScale;
                float  _TexGain;
            CBUFFER_END

            TEXTURE2D(_MainMap); SAMPLER(sampler_MainMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 color       : COLOR;
                float2 uvWorld     : TEXCOORD0;
                float  fogFactor   : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;

                // 用水平 + 高度做三维贴图坐标：墙面的砖缝不会被竖直拉伸
                OUT.uvWorld = float2(IN.positionOS.x + IN.positionOS.z, IN.positionOS.y) * _TexScale;

                float viewDepth = OUT.positionHCS.w;
                OUT.fogFactor = saturate((viewDepth - _FogStart) / max(1e-4, _FogEnd - _FogStart));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 tex = SAMPLE_TEXTURE2D(_MainMap, sampler_MainMap, IN.uvWorld).rgb;
                float3 c = IN.color * tex * _TexGain;
                c = lerp(c, _FogColor.rgb, IN.fogFactor);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
