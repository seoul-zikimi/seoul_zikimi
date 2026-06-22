Shader "Custom/URP Vertex Color Lit"
{
    // 정점색(vertex color)을 알베도로 쓰는 URP 라이트 셰이더.
    // VARCO/AI 모델처럼 텍스처 없이 정점색으로 칠해진 메쉬를 색 나오게 한다.
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float3 normalWS : TEXCOORD0; float4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half3 albedo = (IN.color * _Tint).rgb;
                Light mainLight = GetMainLight();
                half ndl = saturate(dot(IN.normalWS, mainLight.direction)) * 0.5 + 0.5;   // half-lambert(부드럽게)
                half3 lit = albedo * (mainLight.color * ndl + half3(0.25, 0.25, 0.28));    // + 약한 환경광
                return half4(lit, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
