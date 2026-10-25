// 地形着色器：顶点色（二次元平涂色 + 大块明暗）叠加三张无缝贴图，用 UV2 当混合权重
// 贴图只提供很轻的颗粒感（_Detail 控制），画面上主要是平涂色 —— 非写实的动画风
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
        _PaintStrength ("Painterly surface strength", Range(0, 1)) = 0.7
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
                float4 _FogColor;
                float  _FogStart;
                float  _FogEnd;
                float  _TexScale;
                float  _TexGain;
                float  _Detail;
                float  _PaintStrength;
            CBUFFER_END

            TEXTURE2D(_GrassMap); SAMPLER(sampler_GrassMap);
            TEXTURE2D(_RockMap);  SAMPLER(sampler_RockMap);
            TEXTURE2D(_SandMap);  SAMPLER(sampler_SandMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 color      : COLOR;
                float2 blend      : TEXCOORD1;    // x = 岩石权重，y = 沙滩权重
            };

            struct Varyings
            {
                float3 world : TEXCOORD3;
                float3 normal : TEXCOORD4;
                float4 positionHCS : SV_POSITION;
                float3 color       : COLOR;
                float2 blend       : TEXCOORD0;
                float2 uvWorld     : TEXCOORD1;
                float  fogFactor   : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.world = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.blend = IN.blend;

                // 用水平坐标当贴图坐标：一格的贴图大小恒定，不会被陡坡拉长
                OUT.uvWorld = IN.positionOS.xz * _TexScale;

                float viewDepth = OUT.positionHCS.w;
                OUT.fogFactor = saturate((viewDepth - _FogStart) / max(1e-4, _FogEnd - _FogStart));
                return OUT;
            }

            float PaintHash(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }
            float PaintNoise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(PaintHash(cell), PaintHash(cell + float2(1,0)), f.x),
                    lerp(PaintHash(cell + float2(0,1)), PaintHash(cell + 1), f.x), f.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 grass = SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, IN.uvWorld).rgb;
                half3 weights = pow(abs(normalize(IN.normal)), 4);
                weights /= max(dot(weights, half3(1,1,1)), 0.001);
                // Natural strata replace the regular joints of the town brick atlas.
                float strata = PaintNoise(float2(IN.world.x * .12 + IN.world.z * .09, IN.world.y * .8));
                float stonePatch = PaintNoise(IN.world.xz * .16 + IN.world.y * .05);
                half rockTone = .91 + .13 * smoothstep(.25, .75, strata) + .06 * stonePatch;
                half3 rock = half3(rockTone, rockTone, rockTone);
                half3 sand  = SAMPLE_TEXTURE2D(_SandMap,  sampler_SandMap,  IN.uvWorld).rgb;
                // Preserve biome colours; source images contribute restrained value only.
                half grassValue = dot(grass, half3(.2126,.7152,.0722));
                half sandValue = dot(sand, half3(.2126,.7152,.0722));
                grass = lerp(grass, half3(.96,.99,.94) * (.94 + grassValue * .12), _PaintStrength);
                sand = lerp(sand, half3(1.02,1.0,.96) * (.96 + sandValue * .07), _PaintStrength);

                // 先按权重把三张贴图混起来
                half3 tex = lerp(grass, rock, IN.blend.x);
                tex = lerp(tex, sand, IN.blend.y);

                // 二次元风：贴图只留一点点颗粒，主要靠顶点色的平涂色块，所以把贴图往白色拉平
                half3 flat = lerp(half3(1.0, 1.0, 1.0), tex, _Detail) * _TexGain;

                float3 c = IN.color * flat * lerp(float3(1,1,1), _VoxelAmbientTint.rgb, _VoxelEnvironmentActive);
                float broad = PaintNoise(IN.world.xz * .075);
                float brush = PaintNoise(IN.world.xz * float2(.7, 1.8));
                float patches = lerp(.94, 1.055, smoothstep(.22, .78, broad)) + (brush - .5) * .035;
                float green = saturate((IN.color.g - max(IN.color.r, IN.color.b)) * 6);
                float nearDetail = 1 - smoothstep(12, 42, distance(_WorldSpaceCameraPos, IN.world));
                float2 cell = floor(IN.world.xz * 1.7);
                float2 stroke = frac(IN.world.xz * 1.7) - .5;
                stroke.x += (PaintHash(cell) - .5) * .45 + stroke.y * .3;
                float blade = (1 - smoothstep(.025, .065, abs(stroke.x)))
                    * (1 - smoothstep(.12, .32, abs(stroke.y))) * step(.6, PaintHash(cell + 19));
                c *= lerp(1, patches + blade * green * .045 * nearDetail, _PaintStrength);
                c *= VoxelSoftLight(IN.world, IN.normal);
                c = lerp(c, _FogColor.rgb, IN.fogFactor);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
        UsePass "Hidden/Voxel/StylizedShadow/ShadowCaster"
        UsePass "Hidden/Voxel/StylizedShadow/DepthOnly"
    }

    Fallback Off
}
