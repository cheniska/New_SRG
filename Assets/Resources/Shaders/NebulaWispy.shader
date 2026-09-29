Shader "Custom/NebulaWispy"
{
    // Туманность с несколькими отдельными сгустками и тонкими рукавами.
    // Техника: двойная деформация доменов (IQ, 2013) + высокий порог → пики шума
    // образуют изолированные облака, соединённые тонкими нитями.
    // Аддитивный блендинг — не влияет на освещение сцены.
    Properties
    {
        // Образец bg11: внешний cyan → синефиолетовый центр → яркие hotspot-ядра
        _ColorOuter ("Outer Color",   Color)           = (0.00, 0.72, 0.68, 1.0)
        _ColorMid   ("Mid Color",     Color)           = (0.28, 0.08, 0.78, 1.0)
        _ColorCore  ("Core Color",    Color)           = (0.60, 0.95, 1.00, 1.0)
        _Intensity  ("Intensity",     Range(0, 3))     = 1.0
        _Scale      ("Noise Scale",   Float)           = 3.5
        _Offset     ("Seed Offset",   Vector)          = (0, 0, 0, 0)
        // Высокое значение (>1) разрывает единый блоб на несколько сгустков с рукавами
        _WarpAmt    ("Warp Strength", Range(0, 2))     = 1.05
        // Высокий порог (~0.30) показывает только пики шума → отдельные облака
        _Threshold  ("Threshold",     Range(0, 0.5))   = 0.30
        _AspectX    ("Stretch X",     Float)           = 1.0
        _AspectY    ("Stretch Y",     Float)           = 1.0
        _Rotation   ("Rotation",      Float)           = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent-10"
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
                half4  _ColorOuter;
                half4  _ColorMid;
                half4  _ColorCore;
                half   _Intensity;
                float  _Scale;
                float4 _Offset;
                float  _WarpAmt;
                float  _Threshold;
                float  _AspectX;
                float  _AspectY;
                float  _Rotation;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            // ---- gradient noise ----

            float2 _Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453);
            }

            float _GNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(dot(_Hash2(i),               f),
                         dot(_Hash2(i + float2(1,0)), f - float2(1,0)), u.x),
                    lerp(dot(_Hash2(i + float2(0,1)), f - float2(0,1)),
                         dot(_Hash2(i + float2(1,1)), f - float2(1,1)), u.x),
                    u.y);
            }

            // 5-октавный FBM
            float _Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll]
                for (int k = 0; k < 5; k++)
                {
                    v += a * _GNoise(p);
                    p  = p * 2.03 + float2(1.7, 9.2);
                    a *= 0.5;
                }
                return v;
            }

            // 3-октавный FBM → 2D вектор деформации (дешевле основного)
            float2 _FbmWarp(float2 p)
            {
                float2 v = float2(0, 0);
                float  a = 0.5;
                [unroll]
                for (int k = 0; k < 3; k++)
                {
                    v += a * float2(_GNoise(p),
                                    _GNoise(p + float2(5.2, 1.3)));
                    p  = p * 2.03 + float2(1.7, 9.2);
                    a *= 0.5;
                }
                return v;
            }

            float2 _Rotate(float2 v, float a)
            {
                float s, c;
                sincos(a, s, c);
                return float2(v.x * c - v.y * s, v.x * s + v.y * c);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv * 2.0 - 1.0;
                uv = _Rotate(uv, _Rotation);
                uv *= float2(_AspectX, _AspectY);

                float2 p = uv * _Scale + _Offset.xy;

                // ================================================================
                //  ДВОЙНАЯ ДЕФОРМАЦИЯ ДОМЕНОВ
                //  При _WarpAmt > 1 единый шум расслаивается на отдельные пики —
                //  это даёт несколько облаков с тонкими соединительными нитями.
                // ================================================================
                float2 q = _FbmWarp(p);
                float2 r = _FbmWarp(p + _WarpAmt * q + float2(1.7, 9.2));
                float  f = _Fbm(p + _WarpAmt * r);
                f = saturate(f * 0.5 + 0.5);

                // ================================================================
                //  НЕРЕГУЛЯРНАЯ ОБОЛОЧКА
                //  Верхняя граница smoothstep 0.68 (вместо 1.45) → туманность
                //  занимает ~50% радиуса UV и гарантированно не доходит до краёв.
                // ================================================================
                float2 envWarp = _FbmWarp(uv * 1.0 + _Offset.xy * 0.35);
                float2 shifted = uv + float2(0.05, -0.08) + 0.85 * envWarp;
                float  envelope = 1.0 - smoothstep(0.10, 0.68, length(shifted));
                envelope = pow(saturate(envelope), 0.40);

                // ================================================================
                //  ПЛОТНОСТЬ — широкий диапазон (0.55) даёт плавный переход
                //  от прозрачного к плотному: нет резких обрезающих границ.
                // ================================================================
                float raw     = f * envelope;
                float density = smoothstep(_Threshold, _Threshold + 0.55, raw);

                // ================================================================
                //  BORDER FADE — начинается от 0.45 квада (не от 0.76),
                //  чтобы все края гарантированно уходили плавно, без прямых линий.
                // ================================================================
                float2 bUV   = abs(IN.uv * 2.0 - 1.0);
                float  bFade = 1.0 - smoothstep(0.45, 1.0, max(bUV.x, bUV.y));
                density *= bFade;

                if (density < 0.001) return half4(0, 0, 0, 0);

                // ================================================================
                //  ЦВЕТ: cyan на периферии → синефиолетовый в плотном теле → яркий
                //  Образец: внешние вихри — чистый teal, плотный центр — blue-violet,
                //  точки звёздообразования — почти белый cyan.
                // ================================================================
                half3 col = lerp(_ColorOuter.rgb, _ColorMid.rgb,  smoothstep(0.0,  0.60, density));
                col        = lerp(col,             _ColorCore.rgb, smoothstep(0.48, 0.92, density));

                // Небольшая примесь cyan к фиолетовому — body не уходит в чистый пурпур
                float cyanBoost = (1.0 - smoothstep(0.25, 0.70, density)) * 0.30;
                col += _ColorOuter.rgb * cyanBoost * density;

                // ================================================================
                //  HOTSPOTS — яркие точечные сгущения внутри облака
                // ================================================================
                float hs = _Fbm(p * 3.0 + float2(3.3, 7.1));
                hs = pow(saturate(hs * 0.5 + 0.5), 5.5) * step(0.28, density);
                col = lerp(col, _ColorCore.rgb * 1.5, hs * 0.55);

                return half4(col * density * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
}
