Shader "Custom/StarLarge"
{
    // Процедурный слой крупных фоновых звёзд с медленным мерцанием (~1 мин период).
    // Клеточный хэш размещает звёзды; у части из них синусоидальное мерцание
    // с индивидуальными фазой и периодом. Аддитивный блендинг.
    Properties
    {
        _ColorWarm       ("Color Warm",         Color)        = (1.00, 0.95, 0.82, 1)
        _ColorCool       ("Color Cool",         Color)        = (0.82, 0.90, 1.00, 1)
        _GridCells       ("Grid Cells",         Float)        = 550.0
        _Density         ("Density",            Range(0,1))   = 0.30
        _CoreSizeMin     ("Core Size Min",      Range(0,0.4)) = 0.06
        _CoreSizeMax     ("Core Size Max",      Range(0,0.4)) = 0.22
        _GlowFalloff     ("Glow Falloff",       Range(1,20))  = 6.0
        _GlowStrength    ("Glow Strength",      Range(0,1))   = 0.28
        _FlickerChance   ("Flicker Chance",     Range(0,1))   = 0.45
        _FlickerAmp      ("Flicker Amplitude",  Range(0,1))   = 0.38
        _FlickerSpeedMin ("Flicker Speed Min",  Float)        = 0.07
        _FlickerSpeedMax ("Flicker Speed Max",  Float)        = 0.16
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

        Blend One One
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
                float4 _ColorWarm;
                float4 _ColorCool;
                float  _GridCells;
                float  _Density;
                float  _CoreSizeMin;
                float  _CoreSizeMax;
                float  _GlowFalloff;
                float  _GlowStrength;
                float  _FlickerChance;
                float  _FlickerAmp;
                float  _FlickerSpeedMin;
                float  _FlickerSpeedMax;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float  _H21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float2 _H22(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
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
                float2 gp    = IN.uv * _GridCells;
                float2 cell  = floor(gp);
                float2 local = frac(gp) - 0.5;

                float3 result = float3(0, 0, 0);

                [unroll]
                for (int ix = -2; ix <= 2; ix++)
                {
                    [unroll]
                    for (int iy = -2; iy <= 2; iy++)
                    {
                        float2 nc      = cell + float2(ix, iy);
                        float  present = step(_H21(nc + float2(0.37, 0.91)), _Density);
                        if (present < 0.5) continue;

                        float2 h    = _H22(nc);
                        float2 pos  = float2(ix, iy) + h * 0.46 - local;
                        float  d    = length(pos);

                        float  sizeN = _H21(nc + float2(0.52, 0.14));
                        float  size  = lerp(_CoreSizeMin, _CoreSizeMax, sizeN);

                        // Ядро звезды + экспоненциальный ореол
                        float  core  = smoothstep(size, size * 0.35, d);
                        float  glow  = exp(-d * (_GlowFalloff / size)) * _GlowStrength * (1.0 - core);
                        float  starV = core + glow;

                        if (starV < 0.001) continue;

                        // Мерцание: случайная подмножество звёзд, медленный синус
                        float  hasFlicker  = step(_H21(nc + float2(0.15, 0.42)), _FlickerChance);
                        float  flickPhase  = _H21(nc + float2(0.83, 0.27)) * 6.2832;
                        float  flickSpeed  = lerp(_FlickerSpeedMin, _FlickerSpeedMax,
                                                   _H21(nc + float2(0.44, 0.61)));
                        float  flickValue  = (sin(_Time.y * flickSpeed + flickPhase) + 1.0) * 0.5;
                        float  brightness  = lerp(1.0, lerp(1.0 - _FlickerAmp, 1.0, flickValue), hasFlicker);

                        // Яркость звезды — крупные ярче
                        float  starBright = lerp(0.55, 1.0, sizeN) * brightness;

                        // Тёплый/холодный оттенок по хэшу
                        float  warmCool = _H21(nc + float2(0.71, 0.33));
                        float3 col      = lerp(_ColorWarm.rgb, _ColorCool.rgb, warmCool) * starBright;

                        result += col * starV;
                    }
                }

                // Плавное затухание к краям квада
                float2 bUV   = abs(IN.uv * 2.0 - 1.0);
                float  bFade = 1.0 - smoothstep(0.82, 1.0, max(bUV.x, bUV.y));

                result *= bFade;
                if (max(result.r, max(result.g, result.b)) < 0.002) return half4(0, 0, 0, 0);
                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }
}
