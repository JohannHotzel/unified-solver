Shader "UnifiedSolver/ConstraintRenderer"
{
    Properties
    {
        [Header(Strain Coloring)]
        _StrainScale  ("Strain Scale",  Float)        = 10.0
        _ColorCompr   ("Compressed",    Color)        = (0.2, 0.5, 1.0, 1.0)
        _ColorRest    ("Rest",          Color)        = (0.3, 1.0, 0.3, 1.0)
        _ColorStretch ("Stretched",     Color)        = (1.0, 0.2, 0.1, 1.0)

        [Header(Display)]
        _Alpha        ("Alpha",         Range(0,1))   = 0.8
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   5.0
            #include "UnityCG.cginc"

            // ---- GPU structs (must match SolverData.cs byte-for-byte) ----

            struct Particle                  // 56 bytes
            {
                float3 position;            // offset  0
                float3 velocity;            // offset 12
                float3 prevPosition;        // offset 24
                float  invMass;             // offset 36
                int    phase;               // offset 40
                float3 color;               // offset 44
            };

            struct DistanceConstraint       // 24 bytes
            {
                int   particleA;            // offset  0
                int   particleB;            // offset  4
                float restLength;           // offset  8  (-1 = broken)
                float compliance;           // offset 12
                float breakForce;           // offset 16
                float damping;              // offset 20
            };

            // ---- Buffers ----
            StructuredBuffer<Particle>           _Particles;
            StructuredBuffer<DistanceConstraint> _Constraints;

            // ---- Uniforms ----
            float  _StrainScale;
            float4 _ColorCompr;
            float4 _ColorRest;
            float4 _ColorStretch;
            float  _Alpha;

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
            };

            // DrawProcedural(MeshTopology.Lines, constraintCount * 2)
            //   vertexID / 2 -> constraint index
            //   vertexID % 2 -> 0 = particleA endpoint, 1 = particleB endpoint
            v2f vert(uint vertexID : SV_VertexID)
            {
                v2f o;

                uint cIdx     = vertexID >> 1u;   // / 2
                uint endpoint = vertexID &  1u;   // % 2

                DistanceConstraint c = _Constraints[cIdx];

                // Broken constraints: push beyond the far plane so the
                // hardware clips the line away.
                if (c.restLength < 0.0)
                {
                    o.pos   = float4(0, 0, 2, 1);
                    o.color = float4(0, 0, 0, 0);
                    return o;
                }

                int    pIdx     = (endpoint == 0u) ? c.particleA : c.particleB;
                float3 worldPos = _Particles[pIdx].position;
                o.pos = UnityWorldToClipPos(float4(worldPos, 1.0));

                // ---- Strain colour ----
                float3 posA       = _Particles[c.particleA].position;
                float3 posB       = _Particles[c.particleB].position;
                float  currentLen = length(posB - posA);
                float  strain     = (currentLen - c.restLength) / (c.restLength + 1e-6);
                float  t          = strain * _StrainScale;  // [-1..0] compressed, [0..1] stretched

                float3 col;
                if (t < 0.0)
                    col = lerp(_ColorCompr.rgb, _ColorRest.rgb,    saturate(1.0 + t));
                else
                    col = lerp(_ColorRest.rgb,  _ColorStretch.rgb, saturate(t));

                o.color = float4(col, _Alpha);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
