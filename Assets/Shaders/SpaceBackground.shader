// Static outer-space backdrop: faint colored nebula clouds, a soft Milky Way band, and three layers
// of stars (many dim, some medium, a few bright ones with a glow and cross flare).
// It is computed in SCREEN space and does not use time, so it never moves: not over time, and not
// when the camera zooms or pans. Put it on a large sprite/quad behind everything.
Shader "Custom/SpaceBackground"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        [Header(Sky)]
        _SpaceColor ("Deep Space", Color) = (0.010, 0.012, 0.030, 1)
        _Seed ("Seed", Float) = 7.0

        [Header(Nebula)]
        _NebulaColorA ("Nebula Color A", Color) = (0.35, 0.10, 0.55, 1)
        _NebulaColorB ("Nebula Color B", Color) = (0.05, 0.25, 0.55, 1)
        _NebulaStrength ("Nebula Strength", Range(0, 1)) = 0.35
        _NebulaScale ("Nebula Size (higher = smaller)", Float) = 1.6

        [Header(Milky Way)]
        _BandStrength ("Band Strength", Range(0, 1)) = 0.12
        _BandAngle ("Band Angle (degrees)", Range(-90, 90)) = -25

        [Header(Stars)]
        _StarDensity ("Star Density", Float) = 220
        _StarBrightness ("Star Brightness", Range(0, 3)) = 1.2
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Cull Off
        ZWrite Off
        Blend Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _SpaceColor;
                float _Seed;
                float4 _NebulaColorA, _NebulaColorB;
                float _NebulaStrength, _NebulaScale;
                float _BandStrength, _BandAngle;
                float _StarDensity, _StarBrightness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            // ---------- noise helpers ----------
            float Hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float2 Hash22(float2 p) { float n = Hash21(p); return float2(n, Hash21(p + n + 17.17)); }

            float3 Hash33(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }
            float Grad(float3 c, float3 o, float3 f) { return dot(Hash33(c + o), f - o); }
            float Perlin(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                return lerp(lerp(lerp(Grad(i, float3(0,0,0), f), Grad(i, float3(1,0,0), f), u.x),
                                 lerp(Grad(i, float3(0,1,0), f), Grad(i, float3(1,1,0), f), u.x), u.y),
                            lerp(lerp(Grad(i, float3(0,0,1), f), Grad(i, float3(1,0,1), f), u.x),
                                 lerp(Grad(i, float3(0,1,1), f), Grad(i, float3(1,1,1), f), u.x), u.y), u.z);
            }
            float Fbm(float2 p, float seed)
            {
                float v = 0.0, a = 0.5; float3 q = float3(p, seed);
                [unroll] for (int k = 0; k < 5; k++) { v += a * Perlin(q); q = q * 2.02 + 9.7; a *= 0.5; }
                return v;
            }

            // One layer of stars on a grid: at most one star per cell, at a random spot, with a random size.
            float3 StarLayer(float2 p, float cells, float chance, float sizePx, float pxPerUnit, float seed)
            {
                float2 g = p * cells;
                float2 id = floor(g);
                float2 f = frac(g);
                float h = Hash21(id + seed);
                if (h > chance) return 0;

                float2 pos = Hash22(id + seed * 3.1) * 0.8 + 0.1;
                float d = length(f - pos) / cells * pxPerUnit;          // distance in pixels
                float size = sizePx * (0.5 + Hash21(id + seed * 7.3));
                float core = saturate(1.0 - d / size);
                core *= core;

                // Slight color variety: bluish, white, warm.
                float tint = Hash21(id + seed * 11.7);
                float3 color = tint < 0.33 ? float3(0.75, 0.85, 1.0) : (tint < 0.8 ? float3(1, 1, 1) : float3(1.0, 0.9, 0.75));
                return color * core * (0.4 + 0.6 * Hash21(id + seed * 5.9));
            }

            half4 frag (Varyings i) : SV_Target
            {
                // Screen position normalized by screen height: independent of camera zoom/position.
                float2 screen = i.positionCS.xy;
                float pxPerUnit = _ScaledScreenParams.y;
                float2 p = screen / pxPerUnit;

                float3 col = _SpaceColor.rgb;

                // Milky Way: a soft, noisy diagonal band.
                float ang = radians(_BandAngle);
                float2 dir = float2(cos(ang), sin(ang));
                float2 centered = p - float2(0.5 * _ScaledScreenParams.x / pxPerUnit, 0.5);
                float across = dot(centered, float2(-dir.y, dir.x));
                float band = exp(-across * across * 18.0) * (0.6 + 0.8 * Fbm(p * 3.0, _Seed + 4.0));
                col += float3(0.30, 0.32, 0.45) * band * _BandStrength;

                // Nebula clouds in two colors.
                float n1 = saturate(Fbm(p * _NebulaScale, _Seed) * 1.6 + 0.2);
                float n2 = saturate(Fbm(p * _NebulaScale * 1.7 + 3.3, _Seed + 21.0) * 1.6 + 0.1);
                col += _NebulaColorA.rgb * n1 * n1 * _NebulaStrength;
                col += _NebulaColorB.rgb * n2 * n2 * _NebulaStrength;

                // Stars: many tiny, some medium, a few big (more of them inside the Milky Way band).
                float3 stars = 0;
                stars += StarLayer(p, _StarDensity,        0.18 + band * 0.25, 1.2, pxPerUnit, _Seed + 1.0);
                stars += StarLayer(p, _StarDensity * 0.45, 0.16, 1.8, pxPerUnit, _Seed + 2.0) * 1.2;
                stars += StarLayer(p, _StarDensity * 0.12, 0.20, 2.8, pxPerUnit, _Seed + 3.0) * 1.6;

                // A handful of bright stars with a glow and a small cross flare.
                float2 g = p * 6.0;
                float2 id = floor(g);
                if (Hash21(id + _Seed * 13.0) < 0.12)
                {
                    float2 sp = Hash22(id + _Seed * 17.0) * 0.7 + 0.15;
                    float2 dv = (frac(g) - sp) / 6.0 * pxPerUnit;   // pixels
                    float d = length(dv);
                    float glow = exp(-d * 0.35) * 0.9 + exp(-d * 0.08) * 0.12;
                    float flare = (exp(-abs(dv.x) * 0.9) * exp(-abs(dv.y) * 0.06) +
                                   exp(-abs(dv.y) * 0.9) * exp(-abs(dv.x) * 0.06)) * 0.35;
                    stars += float3(0.9, 0.95, 1.0) * (glow + flare);
                }

                col += stars * _StarBrightness;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
