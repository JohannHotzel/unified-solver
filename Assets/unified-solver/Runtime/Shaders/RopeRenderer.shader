Shader "UnifiedSolver/RopeRenderer"
{
    Properties
    {
        _Color ("Color", Color) = (0.8, 0.6, 0.2, 1.0)
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #include "UnityCG.cginc"

            // Must match ParticleGPU / Particle struct byte-for-byte (56 bytes).
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
            int   _RadialSegments;
            float _TubeRadius;
            float4 _Color;
            float _Metallic;
            float _Smoothness;

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 normal   : TEXCOORD0;
                float2 uv       : TEXCOORD1;
                float3 color    : TEXCOORD2;
                float3 worldPos : TEXCOORD3;
            };

            float3 GetPos(int seg)
            {
                return _Particles[_ParticleOffset + seg].position;
            }

            // Compute a safe tangent for the given segment. Returns a
            // normalised direction; falls back to (0,1,0) when the rope
            // segment has zero length (overlapping particles).
            float3 SafeTangent(int seg)
            {
                float3 t;
                if (_SegmentCount < 2)
                    return float3(0, 1, 0);

                if (seg <= 0)
                    t = GetPos(1) - GetPos(0);
                else if (seg >= _SegmentCount - 1)
                    t = GetPos(_SegmentCount - 1) - GetPos(_SegmentCount - 2);
                else
                    t = GetPos(seg + 1) - GetPos(seg - 1);

                float len = length(t);
                return (len > 1e-6) ? (t / len) : float3(0, 1, 0);
            }

            // Build a stable orthonormal frame around `tangent`.
            // Picks the reference axis that is least parallel to tangent
            // to maximise numerical stability and avoid frame flipping.
            void BuildFrame(float3 tangent, out float3 normal, out float3 bitangent)
            {
                float3 absT = abs(tangent);
                // Pick the cardinal axis most perpendicular to tangent.
                float3 ref_;
                if (absT.x <= absT.y && absT.x <= absT.z)
                    ref_ = float3(1, 0, 0);
                else if (absT.y <= absT.z)
                    ref_ = float3(0, 1, 0);
                else
                    ref_ = float3(0, 0, 1);

                bitangent = normalize(cross(tangent, ref_));
                normal    = cross(bitangent, tangent);
            }

            v2f vert(uint vertexID : SV_VertexID, float2 uv : TEXCOORD0)
            {
                v2f o;

                int totalRingVerts = _SegmentCount * _RadialSegments;
                int vid = (int)vertexID;

                bool isCapCenter = vid >= totalRingVerts;
                int seg, radIdx;

                if (isCapCenter)
                {
                    int capIdx = vid - totalRingVerts;
                    seg = (capIdx == 0) ? 0 : _SegmentCount - 1;
                    radIdx = 0;
                }
                else
                {
                    seg    = vid / _RadialSegments;
                    radIdx = vid % _RadialSegments;
                }

                seg = clamp(seg, 0, _SegmentCount - 1);

                float3 pos     = GetPos(seg);
                float3 tangent = SafeTangent(seg);
                float3 normal, bitangent;
                BuildFrame(tangent, normal, bitangent);

                if (isCapCenter)
                {
                    o.pos      = UnityWorldToClipPos(float4(pos, 1.0));
                    int capIdx = vid - totalRingVerts;
                    o.normal   = (capIdx == 0) ? -tangent : tangent;
                    o.uv       = uv;
                    o.color    = _Particles[_ParticleOffset + seg].color;
                    o.worldPos = pos;
                    return o;
                }

                float angle = (float)radIdx / (float)_RadialSegments * 6.28318530718;
                float3 circleOffset = (cos(angle) * normal + sin(angle) * bitangent) * _TubeRadius;

                float3 worldPos = pos + circleOffset;
                float3 vertNormal = normalize(circleOffset);

                o.pos      = UnityWorldToClipPos(float4(worldPos, 1.0));
                o.normal   = vertNormal;
                o.uv       = uv;
                o.color    = _Particles[_ParticleOffset + seg].color;
                o.worldPos = worldPos;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 normal  = normalize(i.normal);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);

                float3 lightDir = normalize(float3(0.5, 1.0, 0.3));
                float  ndl      = saturate(dot(normal, lightDir));

                // Diffuse
                float3 albedo  = _Color.rgb;
                float3 diffuse = albedo * (0.3 + 0.7 * ndl);

                // Specular (Blinn-Phong approximation for metallic/smoothness)
                float3 halfVec  = normalize(lightDir + viewDir);
                float  ndh      = saturate(dot(normal, halfVec));
                float  specPow  = exp2(10.0 * _Smoothness + 1.0);
                float  spec     = pow(ndh, specPow) * _Smoothness;

                float3 specColor = lerp(float3(0.04, 0.04, 0.04), albedo, _Metallic);
                diffuse *= (1.0 - _Metallic);

                float3 col = diffuse + specColor * spec;
                return float4(col, 1.0);
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #pragma multi_compile_shadowcaster
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
            int   _RadialSegments;
            float _TubeRadius;

            float3 GetPos(int seg)
            {
                return _Particles[_ParticleOffset + seg].position;
            }

            float3 SafeTangent(int seg)
            {
                float3 t;
                if (_SegmentCount < 2)
                    return float3(0, 1, 0);

                if (seg <= 0)
                    t = GetPos(1) - GetPos(0);
                else if (seg >= _SegmentCount - 1)
                    t = GetPos(_SegmentCount - 1) - GetPos(_SegmentCount - 2);
                else
                    t = GetPos(seg + 1) - GetPos(seg - 1);

                float len = length(t);
                return (len > 1e-6) ? (t / len) : float3(0, 1, 0);
            }

            void BuildFrame(float3 tangent, out float3 normal, out float3 bitangent)
            {
                float3 absT = abs(tangent);
                float3 ref_;
                if (absT.x <= absT.y && absT.x <= absT.z)
                    ref_ = float3(1, 0, 0);
                else if (absT.y <= absT.z)
                    ref_ = float3(0, 1, 0);
                else
                    ref_ = float3(0, 0, 1);

                bitangent = normalize(cross(tangent, ref_));
                normal    = cross(bitangent, tangent);
            }

            struct v2f { V2F_SHADOW_CASTER; };

            v2f vert(appdata_base v, uint vertexID : SV_VertexID)
            {
                int totalRingVerts = _SegmentCount * _RadialSegments;
                int vid = (int)vertexID;

                bool isCapCenter = vid >= totalRingVerts;
                int seg, radIdx;

                if (isCapCenter)
                {
                    int capIdx = vid - totalRingVerts;
                    seg = (capIdx == 0) ? 0 : _SegmentCount - 1;
                    radIdx = 0;
                }
                else
                {
                    seg    = vid / _RadialSegments;
                    radIdx = vid % _RadialSegments;
                }

                seg = clamp(seg, 0, _SegmentCount - 1);

                float3 pos     = GetPos(seg);
                float3 tangent = SafeTangent(seg);
                float3 normal, bitangent;
                BuildFrame(tangent, normal, bitangent);

                float3 worldPos;
                if (isCapCenter)
                {
                    worldPos = pos;
                }
                else
                {
                    float angle = (float)radIdx / (float)_RadialSegments * 6.28318530718;
                    float3 circleOffset = (cos(angle) * normal + sin(angle) * bitangent) * _TubeRadius;
                    worldPos = pos + circleOffset;
                }

                v.vertex = float4(worldPos, 1.0);

                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag(v2f i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
}
