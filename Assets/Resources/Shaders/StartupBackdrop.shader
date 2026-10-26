Shader "Voxel/StartupBackdrop"
{
    Properties { _MainTex("Texture", 2D) = "white" {} _Clock("Clock", Float) = 0 _Reveal("Reveal", Range(0,1)) = 0 }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Clock, _Reveal;
            struct V { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(appdata_img i) { V o; o.position = UnityObjectToClipPos(i.vertex); o.uv = i.texcoord; return o; }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p) { float2 a=floor(p), f=frac(p); f=f*f*(3-2*f); return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y); }
            float4 frag(V i) : SV_Target
            {
                float2 uv = i.uv, p = uv * float2(5,3) + _Clock * float2(0.018,0.006);
                float cloud = noise(p)*0.65+noise(p*2.2)*0.25+noise(p*4.1)*0.1;
                float3 sky = lerp(float3(0.76,0.86,0.95),float3(0.97,0.985,1),saturate(1-uv.y));
                sky = lerp(sky,float3(1,1,1),smoothstep(0.34,0.72,cloud)*0.9);
                float horizon = exp(-abs(uv.y-0.38)*12);
                sky = lerp(sky,float3(1,0.97,0.9),horizon*0.3);
                float2 star = frac(uv*float2(80,45)+float2(0,_Clock*0.018));
                float mote = pow(saturate(1-length(star-0.5)*2),12)*step(0.97,hash(floor(uv*float2(80,45))));
                sky += mote*0.16;
                return float4(lerp(float3(1,1,1),sky,0.045+_Reveal*0.955),1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
