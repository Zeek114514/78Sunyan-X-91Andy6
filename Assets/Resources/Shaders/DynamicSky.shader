Shader "Voxel/DynamicSky"
{
    Properties { _TopColor("Zenith", Color) = (0.15,0.46,0.85,1) _HorizonColor("Horizon", Color) = (0.68,0.85,0.97,1) }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _TopColor, _HorizonColor;
            float3 _SunDirection;
            float _Day, _CloudCover, _Flash;
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.direction = i.positionOS.xyz; return o; }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p) {
                float2 a=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);
            }
            half4 frag(V i) : SV_Target
            {
                float3 d=normalize(i.direction);
                float3 c=lerp(_HorizonColor.rgb,_TopColor.rgb,pow(saturate(d.y),0.5));
                float sun=dot(d,normalize(_SunDirection));
                float moon=dot(d,-normalize(_SunDirection));
                c += float3(1,0.83,0.48)*(smoothstep(0.9993,0.9998,sun)+pow(saturate(sun),180)*0.18)*(1-_CloudCover*0.85);
                c += float3(0.65,0.76,1)*smoothstep(0.9992,0.9997,moon)*(1-_Day)*(1-_CloudCover);
                float2 starGrid=d.xz/(abs(d.y)+0.15)*180;
                float stars=step(0.996,hash(floor(starGrid)))*pow(saturate(1-length(frac(starGrid)-0.5)*2),6);
                c += stars*(1-_Day)*(1-_CloudCover)*smoothstep(0,0.25,d.y);
                float2 p=d.xz/(max(d.y,0)+0.18)*3+_Time.y*float2(0.009,0.004);
                float n=noise(p)*0.65+noise(p*2.1)*0.35;
                float clouds=smoothstep(1-_CloudCover,1.15-_CloudCover,n)*smoothstep(0,0.22,d.y);
                float3 cloudColor=lerp(float3(0.075,0.09,0.14),float3(0.92,0.94,0.96),_Day)*(1-_CloudCover*0.38);
                c=lerp(c,cloudColor,clouds*0.92)+_Flash*0.5;
                return half4(c,1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
