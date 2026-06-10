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
            
            // 最大搜索步数
            #define MAX_SEARCH_STEPS 200

            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            v2f vert (appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            
            sampler2D _SourceTex;     
            
            Texture2DArray<float4> _PosMapsArray;   
            Texture2DArray<float4> _ColorMapsArray; 
            SamplerState sampler_point_clamp;

            float _LightSpeed;     
            float _FrameInterval;  
            float _CurrentTime;    
            float3 _CameraPosCurrent; 
            int _FrameCount;
            float _MaxDistance; // 虽然保留变量，但在新逻辑中主要作为兜底

            fixed4 frag (v2f i) : SV_Target
            {
                int frameNow = round(_CurrentTime / _FrameInterval);
                int loops = min(MAX_SEARCH_STEPS, frameNow + 1);

                for (int step = 0; step < loops; step++)
                {
                    int pastFrame = frameNow - step;
                    
                    float flightTime = step * _FrameInterval;
                    float photonDist = _LightSpeed * flightTime;

                    // 1. 超过录制时间兜底：直接显示实时画面
                    if (flightTime > _CurrentTime) break;

                    // 2. 读取历史几何信息
                    float4 historyPosSample = _PosMapsArray.Sample(sampler_point_clamp, float3(i.uv, pastFrame));
                    
                    // >>> 核心修正：天空光子流逻辑 <<<
                    // 如果 Alpha < 0.1，说明该时刻该像素没有物体阻挡。
                    // 根据物理原理，这意味着背景光（天空）正在穿过这个位置。
                    // 既然我们的光子已经飞到了这里(photonDist)且没撞到任何前景，
                    // 那我们捕获到的就是这个背景光子。
                    if (historyPosSample.a < 0.1) 
                    {
                        return _ColorMapsArray.Sample(sampler_point_clamp, float3(i.uv, pastFrame));
                    }

                    // 3. 实体碰撞判定
                    // 只有当存在实体时，才需要比对距离
                    float geometryDist = distance(historyPosSample.rgb, _CameraPosCurrent);
                    
                    if (photonDist >= geometryDist)
                    {
                        return _ColorMapsArray.Sample(sampler_point_clamp, float3(i.uv, pastFrame));
                    }
                }

                // 兜底：光速无限大（显示当前帧）
                return tex2D(_SourceTex, i.uv);
            }
            ENDCG
        }
    }
}