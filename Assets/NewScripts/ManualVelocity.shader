Shader "Custom/ManualVelocity"
{
    Properties { }
    SubShader
    {
        // 必须是不透明物体，且关闭剔除以防万一
        Tags { "RenderType"="Opaque" }
        Cull Off 
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // 变量定义
            float4x4 _PrevObjectToWorld; // 物体上一帧位置 (Per Object)
            float4x4 _PrevViewProj;      // 相机上一帧视角 (Global)

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION; // 当前裁剪空间坐标
                float4 screenPos : TEXCOORD0;
                float4 screenPosPrev : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                
                // 1. 计算当前帧的位置 (使用 Unity 内置矩阵)
                // unity_MatrixVP 是当前相机的 VP，unity_ObjectToWorld 是当前物体的 M
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.vertex);

                // 2. 计算上一帧的位置 (使用我们传入的矩阵)
                // World Pos (Past) = PrevModel * Vertex
                float4 worldPosPrev = mul(_PrevObjectToWorld, v.vertex);
                // Clip Pos (Past)  = PrevVP * World Pos (Past)
                float4 clipPosPrev = mul(_PrevViewProj, worldPosPrev);
                
                o.screenPosPrev = ComputeScreenPos(clipPosPrev);
                
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // 3. 透视除法拿到 UV (0~1)
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float2 uvPrev = i.screenPosPrev.xy / i.screenPosPrev.w;

                // 4. 计算速度
                float2 velocity = uv - uvPrev;

                // 【调试模式】如果不确定是否画出来，取消下面这行的注释
                 return float4(1, 0, 0, 1); // 应该输出纯红色

                // 5. 输出
                // 这里的速度可能非常小，也可能是负数。
                // 如果在屏幕上直接看，负数是黑色的，小正数接近黑色。
                // 建议: 在 Compute Shader 里使用时再乘以强度系数。
                return float4(velocity, 0, 1);
            }
            ENDCG
        }
    }
}