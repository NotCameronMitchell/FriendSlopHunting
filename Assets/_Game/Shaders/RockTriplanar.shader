// Mountain rock for meshes without useful UVs (the Kenney Nature Kit outcrops): the terrain's rock texture
// projected from three axes, with moss creeping over the upward faces, so props blend into the slopes.
Shader "FriendSlop/RockTriplanar"
{
    Properties
    {
        _MainTex ("Rock", 2D) = "white" {}
        _MossTex ("Moss", 2D) = "white" {}
        _Tint ("Rock tint", Color) = (1, 1, 1, 1)
        _MossTint ("Moss tint", Color) = (1, 1, 1, 1)
        _Scale ("World tiling (per m)", Float) = 0.12
        _Moss ("Moss amount", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_MossTex); SAMPLER(sampler_MossTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _MossTex_ST;
                half4 _Tint, _MossTint;
                float _Scale, _Moss;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 posWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half3 Tri(TEXTURE2D_PARAM(tex, smp), float3 p, float3 w)
            {
                half3 x = SAMPLE_TEXTURE2D(tex, smp, p.zy).rgb;
                half3 y = SAMPLE_TEXTURE2D(tex, smp, p.xz).rgb;
                half3 z = SAMPLE_TEXTURE2D(tex, smp, p.xy).rgb;
                return x * w.x + y * w.y + z * w.z;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 w = pow(abs(n), 4.0); w /= (w.x + w.y + w.z);
                float3 p = i.posWS * _Scale;
                half3 rock = Tri(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), p, w) * _Tint.rgb;
                half3 moss = Tri(TEXTURE2D_ARGS(_MossTex, sampler_MossTex), p * 1.7, w) * _MossTint.rgb;
                float m = smoothstep(0.55, 0.85, n.y + (rock.g - 0.4) * 0.4) * _Moss;
                half3 albedo = lerp(rock, moss, m);

                Light light = GetMainLight(TransformWorldToShadowCoord(i.posWS));
                half ndl = saturate(dot(n, light.direction));
                half3 col = albedo * (light.color * ndl * light.shadowAttenuation + SampleSH(n));
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert (Attributes v) : SV_POSITION
            {
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 frag () : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert (float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half frag () : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
