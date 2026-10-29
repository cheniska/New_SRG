Shader "SRG/ShipModel"
{
    // Модель корабля под ортографической камерой «сверху» (ShipVisualController3D).
    // Освещение не зависит от источников света сцены: Ламберт от фиксированного направления
    // _LightDir + _Ambient, поэтому модель одинаково выглядит в 2D Renderer и в Forward.
    // _Alpha — растворение при посадке/взлёте/гиперпереходе (через MaterialPropertyBlock).
    // Очередь Transparent: модель сортируется со спрайтами по sortingOrder, а ZWrite/ZTest
    // дают правильное самоперекрытие граней.
    Properties
    {
        _Color    ("Color", Color) = (0.7, 0.7, 0.72, 1)
        _LightDir ("Direction to light (world)", Vector) = (-0.35, 0.55, -0.75, 0)
        _Ambient  ("Ambient", Range(0, 1)) = 0.35
        _Alpha    ("Alpha", Range(0, 1)) = 1
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
            half4  _Color;
            float4 _LightDir;
            half   _Ambient;
            half   _Alpha;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS   : TEXCOORD0;
        };

        Varyings vert(Attributes IN)
        {
            Varyings OUT;
            OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
            OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
            return OUT;
        }

        half4 frag(Varyings IN) : SV_Target
        {
            float3 n = normalize(IN.normalWS);
            float3 l = normalize(_LightDir.xyz);
            half lambert = saturate(dot(n, l));
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
