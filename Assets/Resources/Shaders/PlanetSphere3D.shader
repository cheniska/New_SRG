Shader "SRG/PlanetSphere3D"
{
    // Планета/спутник/облака 3D-сферой (PlanetVisualController3D, SatelliteVisualController3D).
    // Текстура натягивается так же, как в 2D-шейдере Custom/PlanetRotation: центр видимого
    // диска — середина развёртки, _Rotation сдвигает долготу, _AxialTilt поворачивает полосы в
    // плоскости экрана. Поэтому в 2D и 3D планета выглядит одинаково, а 3D добавляет освещение:
    // дневная сторона повёрнута к звезде системы (она в начале координат).
    // Сфера выпуклая — хватает Cull Back, глубину не пишем (не перекрываем спрайты).
    Properties
    {
        _MainTex   ("Texture (equirectangular)", 2D) = "white" {}
        _Tint      ("Tint", Color) = (1, 1, 1, 1)
        _Rotation  ("Rotation", Range(0, 1)) = 0
        _AxialTilt ("Axial Tilt", Range(0, 90)) = 0
        _SideLight ("Share of light from the star (vs from camera)", Range(0, 1)) = 0.75
        _Ambient   ("Ambient (night side)", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Transparent"
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Back

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4  _Tint;
            float  _Rotation;
            float  _AxialTilt;
            half   _SideLight;
            half   _Ambient;
        CBUFFER_END

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 dirOS      : TEXCOORD0;   // точка единичной сферы в осях объекта
            float3 normalWS   : TEXCOORD1;
            float3 lightWS    : TEXCOORD2;
        };

        Varyings vert(Attributes IN)
        {
            Varyings OUT;
            OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
            OUT.dirOS      = IN.positionOS.xyz;
            OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);

            float3 center = TransformObjectToWorld(float3(0, 0, 0));
            float2 toStar = -center.xy;
            float len = length(toStar);
            float3 side = len > 1e-4 ? float3(toStar / len, 0) : float3(0, 0, 0);
            OUT.lightWS = normalize(side * _SideLight + float3(0, 0, -1) * (1 - _SideLight));
            return OUT;
        }

        half4 frag(Varyings IN) : SV_Target
        {
            float3 p = normalize(IN.dirOS);
            // Оси «как в 2D»: x — вправо, y — вверх, z — к камере (у объекта камера по −Z).
            float vx = p.x, vy = p.y, vz = -p.z;
            float tiltRad = _AxialTilt * PI / 180.0;
            float cosT = cos(tiltRad), sinT = sin(tiltRad);
            float sx = vx * cosT - vy * sinT;
            float sy = vx * sinT + vy * cosT;
            float phi = atan2(sx, vz);
            float theta = asin(clamp(sy, -1.0, 1.0));
            float2 uv;
            uv.x = phi / (2.0 * PI) + 0.5 - _Rotation;
            uv.y = theta / PI + 0.5;

            half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * _Tint;
            half lambert = saturate(dot(normalize(IN.normalWS), normalize(IN.lightWS)));
            col.rgb *= _Ambient + (1 - _Ambient) * lambert;
            return col;
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
    }
}
