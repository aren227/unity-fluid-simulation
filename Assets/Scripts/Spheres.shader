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

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 rayDir : TEXCOORD0;
                float4 spherePos : TEXCOORD1;
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
                float3 spherePos = particles[id].pos.xyz;
                float3 localPos = v.vertex.xyz * (radius * 2);

                float3 forward = normalize(_WorldSpaceCameraPos.xyz - spherePos);
                float3 right = normalize(cross(forward, float3(0, 1, 0)));
                float3 up = normalize(cross(right, forward));

                float3x3 rotMat = float3x3(right, up, forward);
                float3 worldPos = mul(localPos, rotMat) + spherePos;

                v2f o;
                o.vertex = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
                o.rayDir = normalize(worldPos - _WorldSpaceCameraPos.xyz);
                o.spherePos = float4(spherePos, particles[id].pos.w); // Add density values.
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 rayOrigin = _WorldSpaceCameraPos.xyz;
                float3 rayDir = normalize(i.rayDir);
                float rayHit = sphIntersect(rayOrigin, rayDir, float4(i.spherePos.xyz, radius));
                clip(rayHit);

                float3 hitPos = rayOrigin + rayDir * rayHit;
                float3 normal = normalize(hitPos - i.spherePos.xyz);
                float light = max(dot(normal, _WorldSpaceLightPos0.xyz), 0);
                light = lerp(0.25, 1, light);

                float density = saturate(invlerp(0, 1, i.spherePos.w));


                float3 col = 0;
                if (density < 0.5) {
                    col = lerp(float3(0,0,1), float3(0,1,0), density*2);
                }
                else {
                    col = lerp(float3(0,1,0), float3(1,0,0), (density-0.5)*2);
                }

                return fixed4(col * light, 1);
            }
            ENDCG
        }
    }
}
