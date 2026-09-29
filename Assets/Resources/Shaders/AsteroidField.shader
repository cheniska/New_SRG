Shader "Custom/AsteroidField"
{
    // Процедурное астероидное поле фона с кластеризацией.
    // Двухуровневый подход: сначала noise-маска определяет регионы скопления,
    // затем клеточный хэш расставляет астероиды (SDF-окружности) только там.
    Properties
    {
        _Color            ("Color",             Color)           = (0.50, 0.45, 0.40, 1)
        _GridCells        ("Grid Cells",        Float)           = 500.0
        _Density          ("Density",           Range(0, 1))     = 0.30
        _SizeMin          ("Size Min",          Range(0.01,0.5)) = 0.12
        _SizeMax          ("Size Max",          Range(0.01,0.5)) = 0.30
        _Roughness        ("Roughness",         Range(0, 1))     = 0.50
        _Brightness       ("Brightness",        Range(0, 2))     = 1.0
        _ClusterScale     ("Cluster Scale",     Float)           = 50.0
        _ClusterThreshold ("Cluster Threshold", Range(0, 1))     = 0.52
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _GridCells;
                float  _Density;
                float  _SizeMin;
                float  _SizeMax;
                float  _Roughness;
                float  _Brightness;
                float  _ClusterScale;
                float  _ClusterThreshold;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float  _H21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float2 _H22(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // Интерполированный value noise
            float _VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(_H21(i),              _H21(i + float2(1,0)), u.x),
                            lerp(_H21(i+float2(0,1)),  _H21(i + float2(1,1)), u.x), u.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // ------------------------------------------------------------------
                //  Маска кластеров: 2-октавный noise → где asteroids разрешены
                // ------------------------------------------------------------------
                float2 cp = IN.uv * _ClusterScale;
                float  cn = _VNoise(cp) * 0.65 + _VNoise(cp * 2.1 + float2(3.7, 1.3)) * 0.35;
                float  clusterMask = smoothstep(_ClusterThreshold - 0.14, _ClusterThreshold + 0.14, cn);

                float effectiveDensity = _Density * clusterMask;
                if (effectiveDensity < 0.005) return half4(0, 0, 0, 0);

                // ------------------------------------------------------------------
                //  Клеточный хэш → индивидуальные астероиды
                // ------------------------------------------------------------------
                float2 gp    = IN.uv * _GridCells;
                float2 cell  = floor(gp);
                float2 local = frac(gp) - 0.5;

                float result   = 0.0;
                float colorVar = 0.5;

                [unroll]
                for (int ix = -1; ix <= 1; ix++)
                {
                    [unroll]
                    for (int iy = -1; iy <= 1; iy++)
                    {
                        float2 nc      = cell + float2(ix, iy);
                        float  present = step(_H21(nc + float2(0.37, 0.91)), effectiveDensity);

                        float2 h    = _H22(nc);
                        float2 pos  = float2(ix, iy) + h * 0.44 - local;
                        float  sizeN = _H21(nc + float2(0.52, 0.14));
                        float  size  = lerp(_SizeMin, _SizeMax, sizeN);
                        float  d    = length(pos);

                        // Синусоидальные неровности края
                        float2 rp    = pos * 4.2 + nc * 0.9;
                        float  rough = sin(rp.x * 5.3 + rp.y * 2.1) * 0.35
                                     + sin(rp.x * 2.8 - rp.y * 4.7) * 0.25;
                        d += rough * _Roughness * size * 0.40;

                        float a  = smoothstep(size, size * 0.5, d) * present;
                        float cv = _H21(nc + float2(0.71, 0.33));

                        float isNew = step(result + 0.001, a);
                        colorVar = lerp(colorVar, cv, isNew);
                        result   = max(result, a);
                    }
                }

                // Плавное затухание к краям квада
                float2 bUV   = abs(IN.uv * 2.0 - 1.0);
                float  bFade = 1.0 - smoothstep(0.80, 1.0, max(bUV.x, bUV.y));

                float3 col   = _Color.rgb * lerp(0.72, 1.18, colorVar) * _Brightness;
                float  alpha = result * bFade * _Color.a;

                if (alpha < 0.005) return half4(0, 0, 0, 0);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
