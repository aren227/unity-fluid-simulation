Shader "Spheres"
{
    Properties
    {
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

            fixed4 primaryColor;
            fixed4 secondaryColor;

            StructuredBuffer<float3> principle;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 rayDir : TEXCOORD0;
                float3 rayOrigin: TEXCOORD1;
                float4 spherePos : TEXCOORD2;
                float3 vel : TEXCOORD3;
                float3 m1 : TEXCOORD4;
                float3 m2 : TEXCOORD5;
                float3 m3 : TEXCOORD6;
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
                float3 spherePos = particles[id].pos.xyz;
                // @Temp: Additional radius to make it larger.
                float3 localPos = v.vertex.xyz * (radius * 2.5);

                float3 forward = normalize(_WorldSpaceCameraPos.xyz - spherePos);
                float3 right = normalize(cross(forward, float3(0, 1, 0)));
                float3 up = normalize(cross(right, forward));

                float3x3 rotMat = float3x3(right, up, forward);
                float3 worldPos = mul(localPos, rotMat) + spherePos;

                float3 cov1 = principle[id*4+0];
                float3 cov2 = principle[id*4+1];

                float3x3 ellip = {
                    cov1.x, cov2.x, cov2.z,
                    cov2.x, cov1.y, cov2.y,
                    cov2.z, cov2.y, cov1.z
                };

                // ellip *= determinant(ellip);
                ellip = inverse(ellip);

                // ellip *= 5;

                ellip /= 20;

                float3 objectSpaceCamera = _WorldSpaceCameraPos.xyz - spherePos;
                objectSpaceCamera = mul(ellip, objectSpaceCamera);

                float3 objectSpaceDir = normalize(worldPos - _WorldSpaceCameraPos.xyz);
                objectSpaceDir = mul(ellip, objectSpaceDir);
                objectSpaceDir = normalize(objectSpaceDir);

                v2f o;
                o.vertex = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
                o.rayDir = objectSpaceDir;
                o.rayOrigin = objectSpaceCamera;
                o.spherePos = float4(spherePos, particles[id].pos.w); // Add density values.
                o.vel = particles[id].vel.xyz;

                o.m1 = ellip._11_12_13;
                o.m2 = ellip._21_22_23;
                o.m3 = ellip._31_32_33;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 rayOrigin = i.rayOrigin;
                float3 rayDir = normalize(i.rayDir);
                float rayHit = sphIntersect(rayOrigin, rayDir, float4(0,0,0, radius));
                clip(rayHit);

                float3 hitPos = rayOrigin + rayDir * rayHit;

                float3x3 mInv = float3x3(i.m1, i.m2, i.m3);

                // float3 normal = float3(
                //     dot(hitPos, mInv._11_21_31),
                //     dot(hitPos, mInv._12_22_32),
                //     dot(hitPos, mInv._13_23_33)
                // );

                // mInv^T * hitPos
                float3 normal = normalize(mul(mInv, hitPos));

                float light = max(dot(normal, _WorldSpaceLightPos0.xyz), 0);
                light = lerp(0.3, 1, light);

                float density = saturate(invlerp(0, 1, i.spherePos.w));


                // float3 col = 0;
                // if (density < 0.5) {
                //     col = lerp(float3(0,0,1), float3(0,1,0), density*2);
                // }
                // else {
                //     col = lerp(float3(0,1,0), float3(1,0,0), (density-0.5)*2);
                // }

                float3 col = lerp(primaryColor, secondaryColor, density);

                col = lerp(col, float3(1,1,1), saturate(invlerp(10, 30, length(i.vel))));

                return fixed4(col * light, 1);
            }
            ENDCG
        }
    }
}
