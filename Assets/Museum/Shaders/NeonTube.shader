// Neon tube: white-hot where it faces the viewer, saturated colour toward the silhouette. Unlit, opaque.
// _Intensity scales the whole tube (pulse). Single-pass instanced stereo ready (Quest).
Shader "Museum/NeonTube"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.2, 0.75, 1)
        _CoreColor ("Core Color", Color) = (1, 0.92, 0.98, 1)
        _CorePower ("Core Sharpness", Range(0.5, 8)) = 2.5
        _Intensity ("Intensity", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _CoreColor;
            half _CorePower;
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
                half core = pow(facing, _CorePower);
                return fixed4(lerp(_Color.rgb, _CoreColor.rgb, core) * _Intensity, 1);
            }
            ENDCG
        }
    }
}
