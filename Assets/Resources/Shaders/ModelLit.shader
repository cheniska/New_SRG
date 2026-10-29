Shader "SRG/ModelLit"
{
    // 3D-модели объектов системы (корабли, астероиды) под ортографической камерой «сверху».
    // Освещение не зависит от источников света сцены: Ламберт от звезды системы (она в начале
    // координат) с примесью света «от камеры», чтобы верх модели не уходил в тень, + _Ambient.
    // Поэтому модель одинаково выглядит в 2D Renderer и в Forward.
    // _Alpha — растворение при посадке/взлёте/гиперпереходе (через MaterialPropertyBlock).
    // Очередь Transparent: модель сортируется со спрайтами по sortingOrder, а ZWrite/ZTest
    // дают правильное самоперекрытие граней (модель лежит за плоскостью z = 0).
    Properties
    {
        _Color     ("Color", Color) = (0.7, 0.7, 0.72, 1)
        _SideLight ("Share of light from the star (vs from camera)", Range(0, 1)) = 0.55
        _Ambient   ("Ambient", Range(0, 1)) = 0.3
        _Alpha     ("Alpha", Range(0, 1)) = 1
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
        ZWrite On
        ZTest LEqual
        Cull Back

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half  _SideLight;
            half  _Ambient;
            half  _Alpha;
        CBUFFER_END

        // Направление на свет для объекта: к звезде в начале координат (в плоскости системы)
        // вперемешку с направлением к камере (−Z). Считается по центру объекта, а не по пикселю.
        float3 LightDirFor(float3 objectCenterWS, half sideLight)
        {
            float2 toStar = -objectCenterWS.xy;
            float len = length(toStar);
            float3 side = len > 1e-4 ? float3(toStar / len, 0) : float3(0, 0, 0);
            return normalize(side * sideLight + float3(0, 0, -1) * (1 - sideLight));
        }

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS   : TEXCOORD0;
            float3 lightWS    : TEXCOORD1;
        };

        Varyings vert(Attributes IN)
        {
            Varyings OUT;
            OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
            OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
            OUT.lightWS    = LightDirFor(TransformObjectToWorld(float3(0, 0, 0)), _SideLight);
            return OUT;
        }

        half4 frag(Varyings IN) : SV_Target
        {
            half lambert = saturate(dot(normalize(IN.normalWS), normalize(IN.lightWS)));
            half3 rgb = _Color.rgb * (_Ambient + (1 - _Ambient) * lambert);
            return half4(rgb, _Color.a * _Alpha);
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
