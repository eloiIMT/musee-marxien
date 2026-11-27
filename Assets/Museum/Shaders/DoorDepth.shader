// Inside of a door opening (DoorPortal): a flat quad that shows a passage going on behind the wall, fading into the
// dark ("interior mapping": each pixel's view ray is traced in a virtual box behind the quad, nothing is modelled).
// The box is the quad's own footprint (x, y in [-0.5, 0.5]) extended _Depth metres along +Z, away from the room:
// parquet floor, walls and ceiling like the room's at the entrance, then darkening quickly and smoothly to black at
// the far end (no sharp step at the threshold). The parquet is the room's own (same pattern, same place: _FloorFrame,
// set per door) and lit like it: the room floor (URP Lit, sun + ambient + sky reflection) was measured as
// albedo * _FloorDiffuse + _FloorSheen + _FloorFresnel * (1 - cos view angle)^5. Per-eye camera position, so the
// depth is right in stereo. Single-pass instanced ready.
Shader "Museum/DoorDepth"
{
    Properties
    {
        _WallColor ("Passage walls, lit (linear, as measured on the room walls)", Color) = (0.62, 0.53, 0.39, 1)
        _FloorTex ("Floor texture", 2D) = "white" {}
        _FloorTile ("Floor texture size (m)", Float) = 1.6
        [HDR] _FloorDiffuse ("Floor: albedo factor", Color) = (1.08, 1.075, 1.006, 1)
        _FloorSheen ("Floor: constant reflection", Color) = (0.059, 0.048, 0.047, 1)
        _FloorFresnel ("Floor: grazing reflection", Color) = (0.023, 0.12, 0.224, 1)
        _FloorFrame ("Room floor frame (origin x, z; right x, z), world", Vector) = (0, 0, 1, 0)
        _Depth ("Passage depth (m): black at the far end", Float) = 2.5
        _Falloff ("Darkening curve (1 linear, higher = darker sooner)", Float) = 2
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"

            half4 _WallColor, _FloorDiffuse, _FloorSheen, _FloorFresnel;
            sampler2D _FloorTex;
            float4 _FloorTex_TexelSize, _FloorFrame;
            float _FloorTile, _Depth, _Falloff;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 objPos : TEXCOORD0;
                float3 ray : TEXCOORD1;   // camera -> vertex, object space (linear: interpolates exactly)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.objPos = v.vertex.xyz;
                float3 cam = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz; // per eye
                o.ray = v.vertex.xyz - cam;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 p = i.objPos;
                float3 d = i.ray;
                d.z = max(d.z, 1e-4);
                // distance along the ray to each side of the box (x, y walls; far end)
                // Ray nearly parallel to a wall (looking straight along it): keep the direction's sign when avoiding
                // the division by zero, otherwise that wall is "hit" behind the quad and the pixel blows up to white
                // (a cross of white dots at the eye's height and in line with it).
                float dx = d.x >= 0 ? max(d.x, 1e-5) : min(d.x, -1e-5);
                float dy = d.y >= 0 ? max(d.y, 1e-5) : min(d.y, -1e-5);
                float tx = max(((dx > 0 ? 0.5 : -0.5) - p.x) / dx, 0);
                float ty = max(((dy > 0 ? 0.5 : -0.5) - p.y) / dy, 0);
                float tz = max((_Depth - p.z) / d.z, 0);
                // Which surface the ray meets first. Explicit comparisons (no float equality): stable along the
                // edges where two surfaces meet, so they do not shimmer as the head moves.
                int surface; // 0 far end, 1 floor / ceiling, 2 side wall
                float t;
                if (tz <= tx && tz <= ty) { surface = 0; t = tz; }
                else if (ty <= tx) { surface = 1; t = ty; }
                else { surface = 2; t = tx; }
                float3 h = p + d * t;

                half3 col;
                if (surface == 0) col = 0;                              // far end: black
                else if (surface == 1 && dy < 0)
                {
                    // Parquet continuing the room's. Mip level from the distance and the grazing angle (screen
                    // derivatives are meaningless here: neighbouring pixels may hit other surfaces), so the far
                    // floor is smoothed instead of sparkling.
                    float3 w = mul(unity_ObjectToWorld, float4(h, 1)).xyz;
                    float3 toHit = w - _WorldSpaceCameraPos;
                    float dist = length(toHit);
                    float grazing = max(abs(toHit.y) / max(dist, 1e-4), 0.08);
                    float texel = _FloorTile / _FloorTex_TexelSize.z;           // metres per texel
                    float lod = log2(max(dist * 0.001 / grazing / texel, 1));   // ~1 mrad per pixel
                    // room-local floor position, mapped like RoomView's floor (u = -x / tile, v = -z / tile)
                    float2 o = w.xz - _FloorFrame.xy;
                    float2 right = _FloorFrame.zw, forward = float2(-right.y, right.x);
                    float2 uv = -float2(dot(o, right), dot(o, forward)) / _FloorTile;
                    half3 albedo = tex2Dlod(_FloorTex, float4(uv, 0, lod)).rgb;
                    float fresnel = pow(1 - min(abs(toHit.y) / max(dist, 1e-4), 1), 5);
                    col = albedo * _FloorDiffuse.rgb + _FloorSheen.rgb + _FloorFresnel.rgb * fresnel;
                }
                else
                {
                    col = _WallColor.rgb;
                    if (surface == 1) col *= 0.8;                       // ceiling a little darker
                    // soft corners: darker near the edges of each surface
                    float e = surface == 2 ? h.y : h.x;
                    col *= 1 - 0.25 * smoothstep(0.3, 0.5, abs(e));
                }
                // Progressive darkening: full room brightness at the threshold, smoothly down to black at _Depth.
                col *= pow(saturate(1 - max(h.z, 0) / _Depth), _Falloff);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
