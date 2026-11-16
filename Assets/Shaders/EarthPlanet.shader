// A flat sprite circle shaded as a 3D, spinning, Earth-like planet.
// - The disc is turned into a sphere (per-pixel normal from the UV), so lighting, specular and the
//   atmosphere rim make it read as 3D.
// - Continents, ice and clouds come from 3D Perlin noise (fBm) sampled on the sphere's surface,
//   so there is no seam. The sample point is rotated over time, so the surface turns to the right.
Shader "Custom/EarthPlanet"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        [Header(Rotation)]
        _RotationSpeed ("Rotation Speed (rad/s)", Float) = 0.6
        _AxialTilt ("Axial Tilt (degrees)", Range(0, 45)) = 23.4

        [Header(Terrain)]
        _NoiseScale ("Continent Size (higher = smaller)", Float) = 1.8
        _SeaLevel ("Sea Level", Range(-0.4, 0.4)) = 0.02
        _Seed ("Seed", Vector) = (12.3, 4.56, 7.89, 0)
        _DeepOcean ("Deep Ocean", Color) = (0.02, 0.10, 0.35, 1)
        _ShallowOcean ("Shallow Ocean", Color) = (0.08, 0.40, 0.75, 1)
        _LandLow ("Lowland", Color) = (0.16, 0.55, 0.20, 1)
        _LandHigh ("Highland", Color) = (0.07, 0.32, 0.10, 1)
        _Ice ("Ice", Color) = (0.95, 0.97, 1.0, 1)
        _IceLatitude ("Ice Cap Start (0-1)", Range(0.5, 1)) = 0.82

        [Header(Clouds)]
        _CloudScale ("Cloud Size (higher = smaller)", Float) = 3.0
        _CloudCover ("Cloud Threshold", Range(-0.3, 0.5)) = 0.08
        _CloudOpacity ("Cloud Opacity", Range(0, 1)) = 0.85
        _CloudSpeed ("Cloud Speed (x rotation)", Float) = 1.35

        [Header(Lighting)]
        _LightDir ("Light Direction", Vector) = (-0.55, 0.45, 0.70, 0)
        _Ambient ("Night Side Brightness", Range(0, 1)) = 0.18
        _Specular ("Ocean Shine", Range(0, 2)) = 0.6
        _Atmosphere ("Atmosphere Color (A = strength)", Color) = (0.35, 0.65, 1.0, 0.9)
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
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _RotationSpeed, _AxialTilt;
                float _NoiseScale, _SeaLevel;
                float4 _Seed;
                float4 _DeepOcean, _ShallowOcean, _LandLow, _LandHigh, _Ice;
                float _IceLatitude;
                float _CloudScale, _CloudCover, _CloudOpacity, _CloudSpeed;
                float4 _LightDir;
                float _Ambient, _Specular;
                float4 _Atmosphere;
            CBUFFER_END

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color * unity_SpriteColor; // Unity 6 sends the SpriteRenderer color here
                return o;
            }

            // ---------- 3D Perlin (gradient) noise ----------
            float3 Hash33(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }

            float Grad(float3 cell, float3 corner, float3 f)
            {
                return dot(Hash33(cell + corner), f - corner);
            }

            float Perlin(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0); // quintic fade

                float x00 = lerp(Grad(i, float3(0,0,0), f), Grad(i, float3(1,0,0), f), u.x);
                float x10 = lerp(Grad(i, float3(0,1,0), f), Grad(i, float3(1,1,0), f), u.x);
                float x01 = lerp(Grad(i, float3(0,0,1), f), Grad(i, float3(1,0,1), f), u.x);
                float x11 = lerp(Grad(i, float3(0,1,1), f), Grad(i, float3(1,1,1), f), u.x);
                return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
            }

            // Fractal Brownian motion: several octaves of Perlin noise.
            float Fbm(float3 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int k = 0; k < 5; k++)
                {
                    v += a * Perlin(p);
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return v;
            }

            float3 RotateY(float3 p, float a) { float s = sin(a), c = cos(a); return float3(p.x * c + p.z * s, p.y, -p.x * s + p.z * c); }
            float3 RotateZ(float3 p, float a) { float s = sin(a), c = cos(a); return float3(p.x * c - p.y * s, p.x * s + p.y * c, p.z); }

            half4 frag (Varyings i) : SV_Target
            {
                // Disc -> sphere.
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float aa = max(fwidth(r), 1e-4);
                float alpha = 1.0 - smoothstep(1.0 - aa * 1.5, 1.0, r);
                clip(alpha - 0.001);

                float z = sqrt(saturate(1.0 - r * r));
                float3 n = float3(p, z); // surface normal, facing the viewer

                // Planet-space point: undo the axial tilt, then the spin. Spinning by -t when sampling
                // makes surface features travel toward +x, i.e. the planet rotates to the right.
                float tilt = radians(_AxialTilt);
                float t = _Time.y * _RotationSpeed;
                float3 planet = RotateZ(n, -tilt);
                float3 q = RotateY(planet, -t);

                // Terrain height.
                float h = Fbm(q * _NoiseScale + _Seed.xyz);
                float land = smoothstep(_SeaLevel - 0.012, _SeaLevel + 0.012, h);
                float3 ocean = lerp(_DeepOcean.rgb, _ShallowOcean.rgb, saturate((h - (_SeaLevel - 0.22)) / 0.22));
                float3 ground = lerp(_LandLow.rgb, _LandHigh.rgb, saturate((h - _SeaLevel) / 0.3));
                float3 surface = lerp(ocean, ground, land);

                // Polar ice caps with a ragged edge.
                float ice = smoothstep(_IceLatitude - 0.04, _IceLatitude + 0.04, abs(q.y) + Fbm(q * 5.0 + 3.7) * 0.18);
                surface = lerp(surface, _Ice.rgb, ice);

                // Clouds drift a little faster than the ground.
                float3 qc = RotateY(planet, -t * _CloudSpeed);
                float c = Fbm(qc * _CloudScale + 31.7);
                float clouds = smoothstep(_CloudCover, _CloudCover + 0.22, c) * _CloudOpacity;

                // Lighting: diffuse day/night, ocean glint, cloud layer, atmosphere rim.
                float3 L = normalize(_LightDir.xyz);
                float ndl = saturate(dot(n, L));
                float light = lerp(_Ambient, 1.0, ndl);

                float3 col = surface * light;
                float3 H = normalize(L + float3(0, 0, 1));
                col += pow(saturate(dot(n, H)), 48.0) * _Specular * (1.0 - land) * (1.0 - ice) * (1.0 - clouds);
                col = lerp(col, float3(1, 1, 1) * light, clouds);

                float fresnel = pow(1.0 - z, 3.0);
                col += _Atmosphere.rgb * fresnel * (0.35 + ndl) * _Atmosphere.a;

                col *= i.color.rgb;
                return half4(col, alpha * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
