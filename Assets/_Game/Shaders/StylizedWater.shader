// Stylized water for lakes and streams (research: Unity URP water tutorials on depth, foam, refraction and waves).
// - Water thickness = scene depth behind the surface minus the surface's own depth (needs the URP Depth Texture).
//   It drives the colour (turquoise shallows to deep blue), how clear the water is, and a cartoon foam band at the shore.
// - Two scrolling normal layers in world space (or along the stream's UVs, so streams visibly flow).
// - Refraction from the URP Opaque Texture, with a depth check so things in front of the water don't smear into it.
// - Sun glints, a fresnel sky tint, gentle sine swells on lakes, and fog.
Shader "FriendSlop/StylizedWater"
{
    Properties
    {
        _Shallow ("Shallow color", Color) = (0.32, 0.78, 0.78, 1)
        _Deep ("Deep color", Color) = (0.06, 0.24, 0.4, 1)
        _DepthDistance ("Depth for full color (m)", Float) = 2.2
        _Clarity ("Clarity in the shallows", Range(0, 1)) = 0.65
        _NormalTex ("Ripple normals", 2D) = "bump" {}
        _FoamTex ("Foam noise", 2D) = "white" {}
        _RippleScale ("Ripple tiling (per m)", Float) = 0.18
        _RippleStrength ("Ripple strength", Range(0, 2)) = 0.7
        _Flow ("Flow (xy world m/s)", Vector) = (0.05, 0.08, 0, 0)
        _UVFlow ("Flow along UVs (streams)", Range(0, 1)) = 0
        _Refraction ("Refraction strength", Range(0, 0.1)) = 0.025
        _FoamDistance ("Shore foam width (m)", Float) = 0.45
        _FoamColor ("Foam color", Color) = (0.95, 0.98, 1, 1)
        _WaveAmp ("Swell height (m)", Float) = 0.05
        _Gloss ("Sun glint sharpness", Float) = 180
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _NormalTex_ST, _FoamTex_ST;
                half4 _Shallow, _Deep, _FoamColor;
                float4 _Flow;
                float _DepthDistance, _Clarity, _RippleScale, _RippleStrength, _UVFlow, _Refraction, _FoamDistance, _WaveAmp, _Gloss;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            float Swell(float3 p)
            {
                float t = _Time.y;
                return (sin(p.x * 0.35 + t * 1.1) + sin(p.z * 0.29 - t * 0.9) * 0.8 + sin((p.x + p.z) * 0.71 + t * 1.7) * 0.35) * _WaveAmp;
            }

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                p.y += Swell(p);
                o.posWS = p;
                o.uv = v.uv;
                o.positionCS = TransformWorldToHClip(p);
                o.eyeDepth = -TransformWorldToView(p).z;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float t = _Time.y;
                // ---- ripples: two normal layers drifting against each other
                float2 baseUV = lerp(i.posWS.xz * _RippleScale, float2(i.uv.x * 0.5, i.uv.y), _UVFlow);
                float2 flow = lerp(_Flow.xy * _RippleScale, float2(0, -_Flow.y), _UVFlow) * t;
                half3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, baseUV + flow));
                half3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, baseUV * 1.73 - flow * 0.8 + 0.37));
                half2 nxy = (n1.xy + n2.xy) * _RippleStrength;
                float3 nWS = normalize(float3(nxy.x, 1.0, nxy.y));
                if (!front) nWS.y = -nWS.y;

                // ---- thickness from the depth texture
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float rawD = SampleSceneDepth(suv);
                float sceneEye = LinearEyeDepth(rawD, _ZBufferParams);
                float viewThick = max(0.0, sceneEye - i.eyeDepth);
                // Vertical depth (surface height minus the ground below) so gentle banks don't read as "shallow" for metres.
                float3 groundWS = ComputeWorldSpacePosition(suv, rawD, UNITY_MATRIX_I_VP);
                float thick = min(viewThick, max(0.0, i.posWS.y - groundWS.y));

                // ---- refraction (fall back to the straight view where the offset lands on something in front)
                float2 ruv = suv + nxy * _Refraction * saturate(thick * 0.6);
                float rEye = LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams);
                if (rEye < i.eyeDepth) { ruv = suv; rEye = sceneEye; }
                float rThick = min(max(0.0, rEye - i.eyeDepth), thick * 1.5 + 0.2);
                half3 below = SampleSceneColor(ruv);

                // ---- colour: clear turquoise shallows into deep blue
                float d = saturate(rThick / _DepthDistance);
                half3 waterCol = lerp(_Shallow.rgb, _Deep.rgb, d);
                half absorb = lerp(1.0 - _Clarity, 1.0, smoothstep(0.0, 1.0, d));
                half3 col = lerp(below * _Shallow.rgb * 1.15, waterCol, absorb);

                // ---- lighting: ambient + sun glint + fresnel sky tint
                Light light = GetMainLight(TransformWorldToShadowCoord(i.posWS));
                float3 v = normalize(GetWorldSpaceViewDir(i.posWS));
                half3 sky = SampleSH(float3(0, 1, 0));
                half fres = pow(1.0 - saturate(dot(nWS, v)), 4.0);
                col = col * (0.55 + 0.45 * saturate(light.color.g + sky.g)) ;
                col = lerp(col, sky * 1.1, fres * 0.45);
                float3 h = normalize(light.direction + v);
                half spec = pow(saturate(dot(nWS, h)), _Gloss) * light.shadowAttenuation;
                col += light.color * spec * 2.2;

                // ---- foam: a wobbly cartoon band where the water meets the shore or rocks
                half noise = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, i.posWS.xz * 0.35 + t * 0.03).r;
                half edge = 1.0 - saturate(thick / _FoamDistance);
                half foam = step(0.62, edge * (0.45 + noise * 0.9));
                half ring = step(0.82, frac(edge * 1.5 - t * 0.3)) * step(0.2, edge) * step(edge, 0.85) * 0.25; // faint lapping lines
                col = lerp(col, _FoamColor.rgb * (0.6 + 0.4 * saturate(light.color.g + sky.g)), saturate(foam * 0.85 + ring));

                col = MixFog(col, i.fog);
                // Soften the very edge so the water's outline never shows as a hard line.
                half alpha = saturate(thick * 6.0 + foam);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
