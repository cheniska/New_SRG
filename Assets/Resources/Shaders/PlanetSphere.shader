Shader "Custom/PlanetRotation"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Rotation ("Rotation", Range(0, 1)) = 0
        _AxialTilt ("Axial Tilt", Range(0, 90)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha 
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Rotation;
            float _AxialTilt;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 centeredUV = i.uv * 2.0 - 1.0;
                float r = length(centeredUV);
                if (r > 1.0) discard;
                float z = sqrt(1.0 - r * r);
                // Axial tilt: rotate sample point in XY plane so texture bands appear tilted
                float tiltRad = _AxialTilt * 3.14159265 / 180.0;
                float cosT = cos(tiltRad);
                float sinT = sin(tiltRad);
                float sx = centeredUV.x * cosT - centeredUV.y * sinT;
                float sy = centeredUV.x * sinT + centeredUV.y * cosT;
                float phi = atan2(sx, z);
                float theta = asin(clamp(sy, -1.0, 1.0));
                float2 sphereUV;
                sphereUV.x = phi / (2.0 * 3.14159) + 0.5;
                sphereUV.y = theta / 3.14159 + 0.5;
                sphereUV.x -= _Rotation; 

                fixed4 col = tex2D(_MainTex, sphereUV);
                col *= i.color;
                float alpha = smoothstep(1.0, 0.98, r);
                col.a *= alpha;

                return col;
            }
            ENDCG
        }
    }
}