Shader "UnifiedSolver/ParticleRenderer"
{
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

            // Must match ParticleGPU in SolverData.cs and Particle in
            // UnifiedSolver.compute byte-for-byte (60 bytes).
            struct Particle
            {
                float3 position;
                float3 velocity;
                float3 prevPosition;
                float  invMass;
                int    phase;
                float3 color;
                uint   visible;
            };

            StructuredBuffer<Particle> _Particles;
            float _ParticleRadius;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 color  : TEXCOORD1;
            };

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                Particle p = _Particles[instanceID];

                if (p.visible == 0)
                {
                    // Collapse to a degenerate clip-space position; rasterizer culls it.
                    o.pos    = float4(0, 0, 0, 0);
                    o.normal = float3(0, 1, 0);
                    o.color  = float3(0, 0, 0);
                    return o;
                }

                float3 worldPos = v.vertex.xyz * (_ParticleRadius * 2.0) + p.position;
                o.pos    = UnityWorldToClipPos(float4(worldPos, 1.0));
                o.normal = v.normal;
                o.color  = p.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 normal   = normalize(i.normal);
                float3 lightDir = normalize(float3(0.5, 1.0, 0.3));
                float  ndl      = saturate(dot(normal, lightDir));
                float  lighting = 0.3 + 0.7 * ndl;
                return float4(i.color * lighting, 1.0);
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
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   5.0
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
                uint   visible;
            };

            StructuredBuffer<Particle> _Particles;
            float _ParticleRadius;

            struct v2f { V2F_SHADOW_CASTER; };

            v2f vert(appdata_base v, uint instanceID : SV_InstanceID)
            {
                Particle p = _Particles[instanceID];
                v2f o;
                if (p.visible == 0)
                {
                    // Hidden particle: no shadow either.
                    v.vertex = float4(0, 0, 0, 1);
                    TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                    o.pos = float4(0, 0, 0, 0);
                    return o;
                }
                // DrawMeshInstancedProcedural has no model matrix, so world == object space.
                // Place world-space position directly into v.vertex and let the standard
                // shadow caster macros project it.
                v.vertex = float4(v.vertex.xyz * (_ParticleRadius * 2.0) + p.position, 1.0);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag(v2f i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
}
