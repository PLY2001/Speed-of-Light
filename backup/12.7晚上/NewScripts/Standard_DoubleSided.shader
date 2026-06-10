//着色器路径，将显示在 Unity 材质检视面板的 Shader 下拉菜单中
Shader "KnowledgeAdvisor/Basic Lit (No Culling)" {

    // --- 属性块 ---
    // 定义所有可供用户在材质检视面板中调节的参数
    Properties {
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0.0, 1.0)) = 0.5
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0
    }

    // --- 子着色器 ---
    // 着色器的主要逻辑部分。Unity 会选择第一个它能在目标硬件上运行的 SubShader
    SubShader {
        // 标签：告诉 Unity 渲染引擎如何以及何时渲染这个着色器
        Tags { "RenderType"="Opaque" }
        LOD 200

        // --- 核心指令 ---
        // 关闭背面剔除。默认是 Cull Back，只渲染正面。
        // Cull Off 会渲染所有面（正面和背面）。
        Cull Off

        // --- Cg/HLSL 代码块 ---
        CGPROGRAM
        // 定义这是一个表面着色器，使用 'surf' 函数，并采用标准的 PBR 光照模型
        #pragma surface surf Standard fullforwardshadows

        // 目标着色器模型 3.0
        #pragma target 3.0

        // 声明与 Properties 中对应的变量
        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;

        // 输入结构体：定义了需要从网格顶点数据中获取哪些信息
        // 这里我们只需要纹理坐标 (uv)
        struct Input {
            float2 uv_MainTex;
        };

        // 表面着色器主函数
        // IN: 包含了输入结构体中的数据
        // o:  输出结构体，我们需要填充它的属性来描述表面材质
        void surf (Input IN, inout SurfaceOutputStandard o) {
            // 从 _MainTex 纹理的对应 uv 坐标处采样颜色，并与 _Color 属性相乘
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;

            // Albedo (反照率)：物体的基本颜色
            o.Albedo = c.rgb;
            
            // Metallic (金属度)：控制表面像金属还是电介质（非金属）
            o.Metallic = _Metallic;
            
            // Smoothness (平滑度)：控制表面的微观平滑程度，影响反射的清晰度
            o.Smoothness = _Glossiness;
            
            // Alpha (透明度)
            o.Alpha = c.a;
        }
        ENDCG
    }

    // 后备着色器：如果以上所有 SubShader 都在目标硬件上运行失败，则使用这个
    FallBack "Diffuse"
}