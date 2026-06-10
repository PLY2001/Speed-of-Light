Shader "Unlit/DepthShader"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 vertex : SV_POSITION;
                // 我们添加一个变量来传递视空间深度
                float viewDepth : TEXCOORD0;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                
                // 1. 获取视空间中的位置 (View Space Position)
                // UnityObjectToViewPos 返回的是物体相对于相机的实际坐标（米为单位）
                float3 viewPos = UnityObjectToViewPos(v.vertex);
                
                // 2. 提取 Z 分量。
                // 在 Unity 视空间中，相机前方是 -Z 方向，所以取负值得到正的距离
                o.viewDepth = -viewPos.z;
                
                return o;
            }

            float frag (v2f i) : SV_Target
            {
                // 3. 获取相机的远平面距离
                // _ProjectionParams.z 存储了相机的 Far Clip Plane 值
                float farPlane = _ProjectionParams.z;

                // 4. 计算归一化的线性深度 (0.0 = 相机位置, 1.0 = 远平面)
                // saturate 确保值不会超过 0~1 的范围
                return saturate(i.viewDepth / farPlane);
            }
            ENDCG
        }
    }
}