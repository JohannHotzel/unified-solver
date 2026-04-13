Shader "UnifiedSolver/RopeRenderer"
{
    Properties
    {
        _Color ("Color", Color) = (0.8, 0.6, 0.2, 1.0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #include "UnityCG.cginc"

            struct Particle
            {
                float3 position;
                float3 velocity;
                float3 prevPosition;
                float  invMass;
                int    phase;
                float3 color;
            };

            StructuredBuffer<Particle> _Particles;
            int   _ParticleOffset;
            int   _SegmentCount;
            float _RopeWidth;
            float4 _Color;

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 color    : TEXCOORD0;
                float2 uv       : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            float3 GetPos(int seg)
            {
                return _Particles[_ParticleOffset + seg].position;
            }

            v2f vert(uint vertexID : SV_VertexID, float2 uv : TEXCOORD0)
            {
                v2f o;

                int vid = (int)vertexID;
                int seg  = vid / 2;       // which particle
                int side = vid % 2;       // 0 = left, 1 = right

                seg = clamp(seg, 0, _SegmentCount - 1);

                float3 pos = GetPos(seg);

                // Tangent from neighbors.
                float3 tangent;
                if (_SegmentCount < 2)
                    tangent = float3(0, 1, 0);
                else if (seg == 0)
                    tangent = GetPos(1) - pos;
                else if (seg == _SegmentCount - 1)
                    tangent = pos - GetPos(seg - 1);
                else
                    tangent = GetPos(seg + 1) - GetPos(seg - 1);

                float tLen = length(tangent);
                tangent = (tLen > 1e-6) ? (tangent / tLen) : float3(0, 1, 0);

                // Billboard offset: perpendicular to both tangent and view direction.
                float3 viewDir = normalize(_WorldSpaceCameraPos - pos);
                float3 offset  = normalize(cross(tangent, viewDir)) * _RopeWidth;

                float3 worldPos = pos + offset * (side == 0 ? -1.0 : 1.0);

                o.pos      = UnityWorldToClipPos(float4(worldPos, 1.0));
                o.color    = _Particles[_ParticleOffset + seg].color;
                o.uv       = uv;
                o.worldPos = worldPos;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Simple shading based on the strip UV for a rounded look.
                // uv.x goes 0..1 across the width of the strip.
                float edge = abs(i.uv.x - 0.5) * 2.0; // 0 at center, 1 at edge
                float shade = 1.0 - edge * edge * 0.4;

                float3 col = _Color.rgb * shade;
                return float4(col, 1.0);
            }
            ENDCG
        }
    }
}
