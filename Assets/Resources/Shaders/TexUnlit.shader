Shader "Voxel/TexUnlit"
{
    Properties
    {
        _MainMap  ("贴图", 2D) = "white" {}
        _TexScale ("贴图密度（每米重复几次）", Float) = 0.25
        _TexGain  ("贴图亮度增益", Float) = 1.5
        _FogColor ("Fog Color", Color) = (0.62, 0.82, 0.94, 1)
        _TextureContrast ("Texture contrast", Range(0,1)) = 0.48
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
                float  _TextureContrast;
            CBUFFER_END

            TEXTURE2D(_MainMap); SAMPLER(sampler_MainMap);

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
                float  fogFactor   : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.world = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;


                float viewDepth = OUT.positionHCS.w;
                OUT.fogFactor = saturate((viewDepth - _FogStart) / max(1e-4, _FogEnd - _FogStart));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // x+z collapses on diagonally rotated walls. Project on three
                // world planes at a common metre scale instead of that single axis.
                float3 geometricNormal = normalize(cross(ddy(IN.world), ddx(IN.world)));
                float3 weights = pow(abs(geometricNormal), 8);
                weights /= max(weights.x + weights.y + weights.z, .0001);
                float3 p = IN.world * _TexScale;
                half3 tex = SAMPLE_TEXTURE2D(_MainMap, sampler_MainMap, p.zy).rgb * weights.x
                          + SAMPLE_TEXTURE2D(_MainMap, sampler_MainMap, p.xz).rgb * weights.y
                          + SAMPLE_TEXTURE2D(_MainMap, sampler_MainMap, p.xy).rgb * weights.z;
                // Preserve masonry and tile rhythm without dark photographic scratches.
                tex = lerp(half3(.72,.72,.72), tex, _TextureContrast);
                float3 c = IN.color * tex * _TexGain * lerp(float3(1,1,1), _VoxelAmbientTint.rgb, _VoxelEnvironmentActive);
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
