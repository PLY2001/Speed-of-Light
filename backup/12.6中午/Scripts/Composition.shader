Shader "Hidden/Composition_FinalSolution"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            v2f vert (appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            
            sampler2D _SourceTex;     
            sampler2D _IdMapCurrent;  
            sampler2D _PosMapCurrent; 
            
            Texture2DArray<float4> _ColorMapsArray;
            SamplerState sampler_point_clamp; // 确保 Unity 绑定了 Point Sampler
            
            StructuredBuffer<float4x4> _CameraVPMatricesBuffer;
            
            struct ObjectTransformData {
                float3 position;
                float padding; 
                float4 rotation; 
            };
            StructuredBuffer<ObjectTransformData> _ObjectHistoryBuffer;
            StructuredBuffer<int> _IdToIndexMap;
            int _MaxTrackableCount;

            float _LightSpeed, _CurrentTime, _FrameInterval, _MaxDistance;
            int _FrameCount;
            float3 _CameraPosCurrent;

            float3 RotateVector(float4 q, float3 v) {
                float3 t = 2.0 * cross(q.xyz, v);
                return v + q.w * t + cross(q.xyz, t);
            }
            float4 InverseQuaternion(float4 q) { return float4(-q.xyz, q.w); }

            float3 GetPastWorldPos(float3 currentWorldPos, int id, int currentFrame, int pastFrame)
            {
                int bufferIndex = _IdToIndexMap[id];
                if (bufferIndex < 0) return currentWorldPos; 
                int idxNow = currentFrame * _MaxTrackableCount + bufferIndex;
                int idxPast = pastFrame * _MaxTrackableCount + bufferIndex;
                if (idxNow >= _MaxTrackableCount * _FrameCount || idxPast < 0) return currentWorldPos;
                
                ObjectTransformData dataNow = _ObjectHistoryBuffer[idxNow];
                ObjectTransformData dataPast = _ObjectHistoryBuffer[idxPast];

                float3 vec = currentWorldPos - dataNow.position;
                float4 invRotNow = InverseQuaternion(dataNow.rotation);
                float3 localPos = RotateVector(invRotNow, vec);
                return dataPast.position + RotateVector(dataPast.rotation, localPos);
            }

            fixed4 LookUpHistoricalColor(float3 worldPos, int frameIndex)
            {
                if (frameIndex < 0 || frameIndex >= _FrameCount) return fixed4(0,0,0,0);
                float4x4 vp = _CameraVPMatricesBuffer[frameIndex];
                float4 clipPos = mul(vp, float4(worldPos, 1.0));

                if (clipPos.w <= 0) return fixed4(0, 0, 0, 0);

                float2 screenUV = (clipPos.xy / clipPos.w) * 0.5 + 0.5;

                // 严格的 UV 保护
                if (screenUV.x <= 0.001 || screenUV.x >= 0.999 || screenUV.y <= 0.001 || screenUV.y >= 0.999) 
                    return fixed4(0, 0, 0, 0);

                return _ColorMapsArray.Sample(sampler_point_clamp, float3(screenUV, frameIndex));
            }

            fixed4 SampleInterpolatedColor(float targetTime, float3 worldPosNow, int objectId, int frameNow)
            {
                float frameFloat = targetTime / _FrameInterval;
                int frameFloor = floor(frameFloat);
                int frameCeil = ceil(frameFloat);
                float lerpFactor = frac(frameFloat);

                float3 posFloor = GetPastWorldPos(worldPosNow, objectId, frameNow, frameFloor);
                float3 posCeil = GetPastWorldPos(worldPosNow, objectId, frameNow, frameCeil);

                fixed4 colFloor = LookUpHistoricalColor(posFloor, frameFloor);
                fixed4 colCeil = LookUpHistoricalColor(posCeil, frameCeil);

                bool validFloor = colFloor.a > 0.1;
                bool validCeil = colCeil.a > 0.1;

                if (validFloor && validCeil) return lerp(colFloor, colCeil, lerpFactor);
                if (validFloor) return colFloor;
                if (validCeil) return colCeil;
                return fixed4(0,0,0,0);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 使用 Point 采样读取 G-Buffer (防止边缘插值导致 ID 错误)
                float idVal = tex2D(_IdMapCurrent, i.uv).r;
                int objectId = round(idVal * 255.0);
                fixed4 currentSkybox = tex2D(_SourceTex, i.uv);
                
                if (objectId <= 0) return currentSkybox;

                float3 worldPosNow = tex2D(_PosMapCurrent, i.uv).rgb;
                float dist = distance(worldPosNow, _CameraPosCurrent);
                int frameNow = round(_CurrentTime / _FrameInterval);
                
                float T_emission = _CurrentTime - dist / _LightSpeed;
                if (T_emission < 0) T_emission = 0; 

                fixed4 finalColor = SampleInterpolatedColor(T_emission, worldPosNow, objectId, frameNow);

                // 回退逻辑 (假设光速无限)
                //if (finalColor.a < 0.1)
               // {
               ///     finalColor = SampleInterpolatedColor(_CurrentTime, worldPosNow, objectId, frameNow);
                //}

                return lerp(currentSkybox, finalColor, finalColor.a);
            }
            ENDCG
        }
    }
}