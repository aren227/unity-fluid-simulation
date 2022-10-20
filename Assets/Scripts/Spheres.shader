Shader "Spheres"
{
    Properties
    {
        _PrimaryColor ("Primary Color", Color) = (1,1,1,1)
        _SecondaryColor ("Secondary Color", Color) = (1,1,1,1)
        _FoamColor ("Foam Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct Particle {
                float4 pos;
                float4 vel;
            };

            StructuredBuffer<Particle> particles;

            float radius;

            StructuredBuffer<float3> principle;

            int usePositionSmoothing;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            // https://www.iquilezles.org/www/articles/spherefunctions/spherefunctions.htm
            float sphIntersect( float3 ro, float3 rd, float4 sph )
            {
                float3 oc = ro - sph.xyz;
                float b = dot( oc, rd );
                float c = dot( oc, oc ) - sph.w*sph.w;
                float h = b*b - c;
                if( h<0.0 ) return -1.0;
                h = sqrt( h );
                return -b - h;
            }

            float invlerp(float a, float b, float t) {
                return (t-a)/(b-a);
            }

            v2f vert (appdata v, uint id : SV_InstanceID)
            {
                float3 spherePos = usePositionSmoothing ? principle[id*4+3] : particles[id].pos.xyz;
                float3 localPos = v.vertex.xyz * (radius * 2 * 2);

                float3x3 ellip = float3x3(principle[id*4+0], principle[id*4+1], principle[id*4+2]);

                float3 worldPos = mul(ellip, localPos) + spherePos;

                v2f o;
                o.vertex = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }

        Pass
        {
            ZTest Always

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D depthBuffer;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float4x4 inverseV, inverseP;

            float radius;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = v.vertex;
                o.vertex.z = 0.5;
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i, out float depth : SV_Depth) : SV_Target
            {
                float d = tex2D(depthBuffer, i.uv);

                // Add small bias to take advantage of early-z-discard in the next pass.
                // ??
                depth = d-0.001;

                // Calculate world-space position.
                float3 viewSpaceRayDir = normalize(mul(inverseP, float4(i.uv*2-1, 0, 1)).xyz);
                float viewSpaceDistance = LinearEyeDepth(d) / dot(viewSpaceRayDir, float3(0,0,-1));
                // Slightly push forward to screen.
                // viewSpaceDistance -= radius * 1;
                // viewSpaceDistance -= 0.1;

                float3 viewSpacePos = viewSpaceRayDir * viewSpaceDistance;
                float3 worldSpacePos = mul(inverseV, float4(viewSpacePos, 1)).xyz;

                return float4(worldSpacePos, 0);
            }

            ENDCG
        }

        Pass
        {
            ZTest Less
            ZWrite Off
            Blend One One

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct Particle {
                float4 pos;
                float4 vel;
            };

            StructuredBuffer<Particle> particles;

            float radius;

            StructuredBuffer<float3> principle;

            int usePositionSmoothing;

            sampler2D worldPosBuffer;

            float4 _PrimaryColor, _SecondaryColor, _FoamColor;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 rayDir : TEXCOORD0;
                float3 rayOrigin: TEXCOORD1;
                float4 spherePos : TEXCOORD2;
                float3 vel : TEXCOORD3;
                float3 m1 : TEXCOORD4;
                float3 m2 : TEXCOORD5;
                float3 m3 : TEXCOORD6;
            };

            struct normalAndColor
            {
                float4 normal : SV_Target0;
                float4 color : SV_Target1;
            };

            // https://www.iquilezles.org/www/articles/spherefunctions/spherefunctions.htm
            float sphIntersect( float3 ro, float3 rd, float4 sph )
            {
                float3 oc = ro - sph.xyz;
                float b = dot( oc, rd );
                float c = dot( oc, oc ) - sph.w*sph.w;
                float h = b*b - c;
                if( h<0.0 ) return -1.0;
                h = sqrt( h );
                return -b - h;
            }

            float invlerp(float a, float b, float t) {
                return (t-a)/(b-a);
            }

            float3x3 inverse(float3x3 m) {
                float a00 = m[0][0], a01 = m[0][1], a02 = m[0][2];
                float a10 = m[1][0], a11 = m[1][1], a12 = m[1][2];
                float a20 = m[2][0], a21 = m[2][1], a22 = m[2][2];

                float b01 = a22 * a11 - a12 * a21;
                float b11 = -a22 * a10 + a12 * a20;
                float b21 = a21 * a10 - a11 * a20;

                float det = a00 * b01 + a01 * b11 + a02 * b21;

                return float3x3(b01, (-a22 * a01 + a02 * a21), (a12 * a01 - a02 * a11),
                            b11, (a22 * a00 - a02 * a20), (-a12 * a00 + a02 * a10),
                            b21, (-a21 * a00 + a01 * a20), (a11 * a00 - a01 * a10)) / det;
            }

            v2f vert (appdata v, uint id : SV_InstanceID)
            {
                float3 spherePos = usePositionSmoothing ? principle[id*4+3] : particles[id].pos.xyz;
                float3 localPos = v.vertex.xyz * (radius * 2 * 3);

                // @Todo: Implement ellipsoid fitted quad.
                float3 forward = normalize(_WorldSpaceCameraPos.xyz - spherePos);
                float3 right = normalize(cross(forward, float3(0, 1, 0)));
                float3 up = normalize(cross(right, forward));

                float3x3 rotMat = float3x3(right, up, forward);
                float3 worldPos = mul(localPos, rotMat) + spherePos;

                float3x3 ellip = float3x3(principle[id*4+0], principle[id*4+1], principle[id*4+2]);
                ellip = inverse(ellip);

                float3 objectSpaceCamera = _WorldSpaceCameraPos.xyz - spherePos;
                objectSpaceCamera = mul(ellip, objectSpaceCamera);

                float3 objectSpaceDir = normalize(worldPos - _WorldSpaceCameraPos.xyz);
                objectSpaceDir = mul(ellip, objectSpaceDir);
                objectSpaceDir = normalize(objectSpaceDir);

                v2f o;
                o.vertex = mul(UNITY_MATRIX_VP, float4(worldPos, 1));

                // @Temp: Actually it's screen-space uv.
                o.rayDir = ComputeScreenPos(o.vertex);
                o.rayOrigin = objectSpaceCamera;
                o.spherePos = float4(spherePos, particles[id].pos.w); // Add density values.
                o.vel = particles[id].vel.xyz;

                o.m1 = ellip._11_12_13;
                o.m2 = ellip._21_22_23;
                o.m3 = ellip._31_32_33;
                return o;
            }

            normalAndColor frag (v2f i) : SV_Target
            {
                float3x3 mInv = float3x3(i.m1, i.m2, i.m3);

                float2 uv = i.rayDir.xy / i.rayDir.w;
                float3 worldPos = tex2D(worldPosBuffer, uv).xyz;
                float3 ellipPos = mul(mInv, worldPos - i.spherePos.xyz);

                float distSqr = dot(ellipPos, ellipPos);
                // @Todo: Maybe we need to give more radius.
                float radiusSqr = pow(radius*6, 2);
                if (distSqr >= radiusSqr) discard;

                float weight = pow(1 - distSqr / radiusSqr, 3);

                float3 centered = worldPos - i.spherePos.xyz;
                float3 normal = -6 * pow(1 - distSqr / radiusSqr, 2) / radiusSqr * centered;
                normal = mul(normal, mInv);

                // @Hardcoded: Density range
                float density = saturate(invlerp(0, 1, i.spherePos.w));
                float3 color = lerp(_PrimaryColor, _SecondaryColor, density);
                // @Hardcoded: Velocity range
                color = lerp(color, _FoamColor, saturate(invlerp(10, 30, length(i.vel))));

                normalAndColor o;
                o.normal = float4(normal, weight);
                o.color = float4(color, 1) * weight;

                return o;
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D depthBuffer;
            sampler2D normalBuffer;
            sampler2D colorBuffer;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = v.vertex;
                o.vertex.z = 0.5;
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i, out float depth : SV_Depth) : SV_Target
            {
                float d = tex2D(depthBuffer, i.uv);
                float4 normal = tex2D(normalBuffer, i.uv);
                float4 color = tex2D(colorBuffer, i.uv);

                if (d == 0) discard;

                if (normal.w > 0) {
                    normal.xyz = -normalize(normal.xyz);
                    color /= color.w;
                }

                depth = d;

                float light = max(dot(normal, _WorldSpaceLightPos0.xyz), 0);
                light = lerp(0.1, 1, light);

                return color * light;
            }

            ENDCG
        }
    }
}
