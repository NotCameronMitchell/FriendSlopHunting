// Puffy cartoon clouds: lit from above (bright tops, soft shaded bellies) with a little rim light,
// tinted by the time of day. Opaque and fog-free so distant clouds stay visible against the sky.
Shader "FriendSlop/Cloud"
{
    Properties
    {
        _Color ("Lit color", Color) = (1, 1, 1, 1)
        _Shadow ("Shadow color", Color) = (0.72, 0.78, 0.88, 1)
        _Rim ("Rim color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+10" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _Shadow, _Rim;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(posWS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float up = saturate(n.y * 0.6 + 0.55);
                up = smoothstep(0.25, 0.85, up);                           // toon-ish band between belly and top
                half3 col = lerp(_Shadow.rgb, _Color.rgb, up);
                float rim = pow(1.0 - saturate(dot(n, normalize(i.viewWS))), 3.0);
                col += _Rim.rgb * rim * 0.25;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
