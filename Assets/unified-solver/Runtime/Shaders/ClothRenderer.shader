Shader "UnifiedSolver/ClothRenderer"
{
    Properties
    {
        _Color ("Color", Color) = (0.9, 0.2, 0.3, 1.0)
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
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
            int _ParticleOffset;
            int _ResolutionX;
            int _ResolutionY;
            float4 _Color;
            float _Metallic;
            float _Smoothness;

            struct appdata
            {
                uint vertexID : SV_VertexID;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 normal   : TEXCOORD0;
                float2 uv       : TEXCOORD1;
                float3 color    : TEXCOORD2;
                float3 worldPos : TEXCOORD3;
            };

            // Fetch world position of a grid particle by its flat index.
            float3 GetPos(int flatIndex)
            {
                return _Particles[_ParticleOffset + flatIndex].position;
            }

            v2f vert(appdata v)
            {
                v2f o;

                int idx = (int)v.vertexID;
                float3 pos = GetPos(idx);

                // Compute normal from grid neighbors via finite differences.
                int x = idx % _ResolutionX;
                int z = idx / _ResolutionX;

                // Horizontal tangent
                float3 ddx;
                if (x > 0 && x < _ResolutionX - 1)
                    ddx = GetPos(idx + 1) - GetPos(idx - 1);
                else if (x > 0)
                    ddx = pos - GetPos(idx - 1);
                else
                    ddx = GetPos(idx + 1) - pos;

                // Vertical tangent
                float3 ddz;
                if (z > 0 && z < _ResolutionY - 1)
                    ddz = GetPos(idx + _ResolutionX) - GetPos(idx - _ResolutionX);
                else if (z > 0)
                    ddz = pos - GetPos(idx - _ResolutionX);
                else
                    ddz = GetPos(idx + _ResolutionX) - pos;

                float3 normal = normalize(cross(ddz, ddx));

                o.pos      = UnityWorldToClipPos(float4(pos, 1.0));
                o.normal   = normal;
                o.uv       = v.uv;
                o.color    = _Particles[_ParticleOffset + idx].color;
                o.worldPos = pos;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Two-sided lighting: flip normal for backfaces.
                float3 normal  = normalize(i.normal);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                if (dot(normal, viewDir) < 0) normal = -normal;

                float3 lightDir = normalize(float3(0.5, 1.0, 0.3));
                float  ndl      = saturate(dot(normal, lightDir));

                // Diffuse
                float3 albedo   = _Color.rgb;
                float3 diffuse  = albedo * (0.3 + 0.7 * ndl);

                // Specular (Blinn-Phong approximation for metallic/smoothness)
                float3 halfVec  = normalize(lightDir + viewDir);
                float  ndh      = saturate(dot(normal, halfVec));
                float  specPow  = exp2(10.0 * _Smoothness + 1.0);
                float  spec     = pow(ndh, specPow) * _Smoothness;

                // Metallic surfaces tint specular with albedo, non-metallic use white.
                float3 specColor = lerp(float3(0.04, 0.04, 0.04), albedo, _Metallic);
                // Metallic surfaces darken diffuse.
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
            Cull Off

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
            int _ParticleOffset;

            struct v2f { V2F_SHADOW_CASTER; };

            v2f vert(appdata_base v, uint vertexID : SV_VertexID)
            {
                float3 worldPos = _Particles[_ParticleOffset + (int)vertexID].position;
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
