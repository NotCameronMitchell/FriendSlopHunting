// Sun and moon discs on the far sky: a crisp core plus a soft glow (sun), or a pale disc with
// soft crater shading (moon). Additive, no fog, hidden behind terrain and clouds by the depth test.
Shader "FriendSlop/SkyDisc"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Core ("Core radius (0-1)", Float) = 0.3
        _Glow ("Glow strength", Float) = 0.6
        _Craters ("Craters (0/1)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-60" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Core, _Glow, _Craters;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float Spot(float2 p, float2 c, float r) { return smoothstep(r, r * 0.4, length(p - c)); }

            half4 frag (Varyings i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;           // -1..1
                float d = length(p);
                float core = smoothstep(_Core + 0.015, _Core - 0.015, d);
                float glow = pow(saturate(1.0 - d), 3.0) * _Glow;
                float shade = 1.0;
                if (_Craters > 0.5)
                {
                    float2 q = p / max(_Core, 0.001);
                    shade = 1.0 - 0.28 * (Spot(q, float2(-0.3, 0.25), 0.32) + Spot(q, float2(0.35, -0.1), 0.22)
                                        + Spot(q, float2(0.05, -0.45), 0.18) + Spot(q, float2(0.45, 0.42), 0.12));
                    shade *= lerp(1.0, 0.82, saturate(dot(q, float2(0.6, -0.4)))); // a soft terminator
                }
                float a = saturate(core * shade + glow);
                return half4(_Color.rgb * (core > 0.0 ? shade : 1.0), _Color.a * a);
            }
            ENDHLSL
        }
    }
}
