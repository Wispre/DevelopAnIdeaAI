// Sprite shader that makes a spacesuit part look old and worn:
// yellowed fabric, grime blotches, darker grime gathering toward the bottom, faded patches,
// fine fabric grain and thin scratches. All from 3D Perlin noise, so no texture is needed.
// The sprite's own color is the clean suit color; the sprite texture supplies the shape (alpha).
Shader "Custom/WornSuit"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        [Header(Age)]
        _AgedTint ("Yellowed Tint", Color) = (0.86, 0.80, 0.62, 1)
        _Yellowing ("Yellowing", Range(0, 1)) = 0.55
        _Fade ("Faded Patches", Range(0, 1)) = 0.35

        [Header(Grime)]
        _DirtColor ("Grime Color", Color) = (0.30, 0.24, 0.17, 1)
        _DirtAmount ("Grime Amount", Range(0, 1)) = 0.55
        _DirtScale ("Grime Size (higher = smaller)", Float) = 3.5
        _BottomGrime ("Extra Grime Toward Bottom", Range(0, 1)) = 0.35

        [Header(Detail)]
        _GrainStrength ("Fabric Grain", Range(0, 0.3)) = 0.08
        _ScratchStrength ("Scratches", Range(0, 1)) = 0.45
        _Seed ("Seed", Vector) = (3.1, 7.7, 1.3, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _AgedTint;
                float _Yellowing, _Fade;
                float4 _DirtColor;
                float _DirtAmount, _DirtScale, _BottomGrime;
                float _GrainStrength, _ScratchStrength;
                float4 _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float3 noisePos   : TEXCOORD1;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color * unity_SpriteColor; // Unity 6 sends the SpriteRenderer color here

                // Object-space position scaled to world size, so the wear has the same density on
                // big and small parts, and sticks to the part as the astronaut tumbles.
                float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20),
                                      length(UNITY_MATRIX_M._m01_m11_m21),
                                      length(UNITY_MATRIX_M._m02_m12_m22));
                o.noisePos = v.positionOS.xyz * scale + _Seed.xyz;
                return o;
            }

            // ---------- 3D Perlin noise ----------
            float3 Hash33(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }

            float Grad(float3 cell, float3 corner, float3 f) { return dot(Hash33(cell + corner), f - corner); }

            float Perlin(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float x00 = lerp(Grad(i, float3(0,0,0), f), Grad(i, float3(1,0,0), f), u.x);
                float x10 = lerp(Grad(i, float3(0,1,0), f), Grad(i, float3(1,1,0), f), u.x);
                float x01 = lerp(Grad(i, float3(0,0,1), f), Grad(i, float3(1,0,1), f), u.x);
                float x11 = lerp(Grad(i, float3(0,1,1), f), Grad(i, float3(1,1,1), f), u.x);
                return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
            }

            float Fbm(float3 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int k = 0; k < 4; k++) { v += a * Perlin(p); p = p * 2.07 + 11.3; a *= 0.5; }
                return v;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float alpha = tex.a * i.color.a;
                clip(alpha - 0.001);

                float3 p = i.noisePos;
                float3 col = tex.rgb * i.color.rgb;   // clean suit color

                // Yellowed with age, unevenly.
                float age = saturate(_Yellowing * (0.7 + 0.6 * (Fbm(p * 1.3 + 5.0) + 0.5)));
                col = lerp(col, col * _AgedTint.rgb, age);

                // Sun-faded, slightly washed-out patches.
                float fade = smoothstep(0.05, 0.35, Fbm(p * 2.2 + 19.0)) * _Fade;
                float grey = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, lerp(col, grey.xxx, 0.6) * 1.06, fade);

                // Grime blotches, heavier toward the bottom of each part.
                float bottom = saturate(0.5 - i.uv.y) * 2.0 * _BottomGrime;
                float grimeNoise = Fbm(p * _DirtScale) + bottom * 0.35;
                float grime = smoothstep(0.02, 0.32, grimeNoise) * _DirtAmount;
                col = lerp(col, col * _DirtColor.rgb * 1.6, grime);

                // Fine fabric grain.
                col *= 1.0 + Perlin(p * 45.0) * _GrainStrength;

                // Thin scratches: narrow ridges of stretched noise, some dark, some light.
                float ridge = 1.0 - smoothstep(0.0, 0.035, abs(Perlin(p * float3(28.0, 5.0, 9.0) + 2.0)));
                float mask = smoothstep(0.1, 0.35, Perlin(p * 3.0 + 41.0));
                float scratch = ridge * mask * _ScratchStrength;
                col = lerp(col, (Perlin(p * 7.0) > 0 ? col * 0.55 : min(col * 1.25 + 0.05, 1.0)), scratch);

                return half4(saturate(col), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
