Shader "Unlit/G-Buffer"
{
    Properties
    {
        _ObjectID ("Object ID", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 worldPos : TEXCOORD0;
            };

            float _ObjectID;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                return o;
            }
            
            // MRT (Multiple Render Targets) output
            struct FragOutput {
                float4 id : SV_Target0;
                float4 pos : SV_Target1;
                // SV_Target2 will be the normal color from another shader
            };

            FragOutput frag (v2f i)
            {
                FragOutput o;
                // 将ID编码到float中。这里直接用，但在实际应用中可能需要更复杂的编码
                o.id = _ObjectID / 255.0; 
                o.pos = i.worldPos;
                return o;
            }
            ENDCG
        }
    }
}