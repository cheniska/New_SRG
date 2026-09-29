Shader "Custom/NebulaURP"
{
    Properties
    {
        _ColorA        ("Core Color",   Color)           = (0.3, 1.0, 1.0, 1.0)
        _ColorB        ("Mid Color",    Color)           = (0.0, 0.5, 0.7, 1.0)
        _Intensity     ("Intensity",    Range(0, 3))     = 1.0
        _Scale         ("Noise Zoom",   Float)           = 2.5
        _Offset        ("Seed Offset",  Vector)          = (0, 0, 0, 0)
        _SwirlStrength ("Swirl",        Range(0, 3))     = 1.3
        _Sharpness     ("Sharpness",    Range(1, 6))     = 2.0
        _EdgeFade      ("Edge Fade",    Range(0.1, 0.9)) = 0.35
        _AspectX       ("Aspect X",     Float)           = 1.0
        _AspectY       ("Aspect Y",     Float)           = 1.0
        _Rotation      ("Rotation",     Float)           = 0.0
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
                half4  _ColorA;
                half4  _ColorB;
                half   _Intensity;
                float  _Scale;
                float4 _Offset;
                float  _SwirlStrength;
                float  _Sharpness;
                float  _EdgeFade;
                float  _AspectX;
                float  _AspectY;
                float  _Rotation;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;      // SpriteRenderer vertex color (не используется)
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            // ---- noise ----

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

            float _Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    v += a * _GNoise(p);
                    p *= 2.03;
                    a *= 0.5;
                }
                return v;
            }

            float _Nebula(float2 p, float swirl)
            {
                float2 q = float2(_Fbm(p), _Fbm(p + float2(3.7, 1.9)));
                return _Fbm(p + swirl * q);
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
                float2 centered = IN.uv * 2.0 - 1.0;

                // вращение → растяжение → органический FBM-контур
                float2 rotated  = _Rotate(centered, _Rotation);
                float2 elliptic = rotated * float2(_AspectX, _AspectY);

                float  distort  = _Fbm(elliptic * 1.7 + _Offset.xy * 0.29) * 0.28;
                float  r        = length(elliptic) - distort;
                float  mask     = 1.0 - smoothstep(_EdgeFade, _EdgeFade + 0.45, r);

                float2 noiseUV  = centered * _Scale + _Offset.xy;
                float  n        = _Nebula(noiseUV, _SwirlStrength);
                n = saturate(n * 0.5 + 0.5);
                n = pow(n, _Sharpness);
                n *= mask;

                half3 col = lerp(half3(0, 0, 0), _ColorB.rgb, saturate(n * 2.0));
                col        = lerp(col,            _ColorA.rgb, saturate(n * 2.0 - 1.0));

                return half4(col * n * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
}
