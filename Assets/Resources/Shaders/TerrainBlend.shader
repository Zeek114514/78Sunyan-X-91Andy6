// 地形着色器：顶点色（二次元平涂色 + 大块明暗）叠加三张无缝贴图，用 UV2 当混合权重
// 贴图只提供很轻的颗粒感（_Detail 控制），画面上主要是平涂色 —— 非写实的动画风
// 和 Voxel/Demo 一样不参与光照、只用线性雾，尽量少依赖 URP 内部的东西（降低版本差异风险）
Shader "Voxel/TerrainBlend"
{
    Properties
    {
        _GrassMap  ("Grass", 2D) = "white" {}
        _RockMap   ("Rock",  2D) = "white" {}
        _SandMap   ("Sand",  2D) = "white" {}
        _TexScale  ("贴图密度（每米重复几次）", Float) = 0.35
        _TexGain   ("贴图亮度增益", Float) = 1.12
        _Detail    ("贴图细节权重（0 = 纯平涂，1 = 全贴图）", Range(0, 1)) = 0.22
        _FogColor  ("Fog Color", Color) = (0.62, 0.82, 0.94, 1)
        _FogStart  ("Fog Start", Float) = 34
        _FogEnd    ("Fog End",   Float) = 96
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
            Name "TerrainBlend"
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
                float  _Detail;
            CBUFFER_END

            TEXTURE2D(_GrassMap); SAMPLER(sampler_GrassMap);
            TEXTURE2D(_RockMap);  SAMPLER(sampler_RockMap);
            TEXTURE2D(_SandMap);  SAMPLER(sampler_SandMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 color      : COLOR;
                float2 blend      : TEXCOORD1;    // x = 岩石权重，y = 沙滩权重
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 color       : COLOR;
                float2 blend       : TEXCOORD0;
                float2 uvWorld     : TEXCOORD1;
                float  fogFactor   : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.blend = IN.blend;

                // 用水平坐标当贴图坐标：一格的贴图大小恒定，不会被陡坡拉长
                OUT.uvWorld = IN.positionOS.xz * _TexScale;

                float viewDepth = OUT.positionHCS.w;
                OUT.fogFactor = saturate((viewDepth - _FogStart) / max(1e-4, _FogEnd - _FogStart));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 grass = SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, IN.uvWorld).rgb;
                half3 rock  = SAMPLE_TEXTURE2D(_RockMap,  sampler_RockMap,  IN.uvWorld).rgb;
                half3 sand  = SAMPLE_TEXTURE2D(_SandMap,  sampler_SandMap,  IN.uvWorld).rgb;

                // 先按权重把三张贴图混起来
                half3 tex = lerp(grass, rock, IN.blend.x);
                tex = lerp(tex, sand, IN.blend.y);

                // 二次元风：贴图只留一点点颗粒，主要靠顶点色的平涂色块，所以把贴图往白色拉平
                half3 flat = lerp(half3(1.0, 1.0, 1.0), tex, _Detail) * _TexGain;

                float3 c = IN.color * flat;
                c = lerp(c, _FogColor.rgb, IN.fogFactor);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
