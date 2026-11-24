// Neon halo: an inflated shell around a NeonTube, added on top of the scene, brightest where the shell faces the
// viewer and fading to nothing at its silhouette, so it reads as light bleeding around the tube without bloom.
// _Intensity scales it (pulse). Single-pass instanced stereo ready (Quest).
Shader "Museum/NeonGlow"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.2, 0.75, 1)
        _Falloff ("Falloff", Range(0.5, 8)) = 2
        _Intensity ("Intensity", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Back
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            half _Falloff;
            half _Intensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 view : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = WorldSpaceViewDir(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half facing = saturate(dot(normalize(i.normal), normalize(i.view)));
                return fixed4(_Color.rgb * pow(facing, _Falloff) * _Intensity, 1);
            }
            ENDCG
        }
    }
}
