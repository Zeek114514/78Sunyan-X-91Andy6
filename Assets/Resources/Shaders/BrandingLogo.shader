Shader "Voxel/BrandingLogo"
{
    Properties { _MainTex("Logo", 2D) = "white" {} _Opacity("Opacity", Range(0,1)) = 1 }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Opacity;
            struct V { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(appdata_img i) { V o; o.position = UnityObjectToClipPos(i.vertex); o.uv = i.texcoord; return o; }
            fixed4 frag(V i) : SV_Target
            {
                float4 pixel = tex2D(_MainTex, i.uv);
                float paper = min(pixel.r, min(pixel.g, pixel.b));
                float alpha = 1 - smoothstep(0.82, 0.985, paper);
                float3 ink = saturate((pixel.rgb - (1 - alpha)) / max(alpha, 0.001));
                return float4(ink, pixel.a * alpha * _Opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
