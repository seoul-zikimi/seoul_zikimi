Shader "Hidden/PickupOutline"
{
    // 인버티드 헐 아웃라인: 메쉬를 법선 방향으로 살짝 키운 뒤 앞면을 컬링 → 뒷면만 그려 실루엣 테두리.
    // 색은 _OutlineColor↔_GlowColor를 천천히 오가고(HDR — 밤 맵 블룸이 받아 은은히 빛난다),
    // 두께도 같은 위상으로 살짝 숨쉰다 — '지금 집을 수 있다'는 살아있는 느낌.
    Properties
    {
        _OutlineColor ("Color", Color) = (0.35, 1, 0.45, 1)
        [HDR] _GlowColor ("Glow Color", Color) = (0.6, 1.8, 0.8, 1)
        _OutlineWidth ("Width", Float) = 0.05
        _PulseSpeed ("Pulse Speed", Float) = 2.2
        _PulseWidth ("Width Pulse (0~1)", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float4 _GlowColor;
                float _OutlineWidth;
                float _PulseSpeed;
                float _PulseWidth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float pulse = 0.5 + 0.5 * sin(_Time.y * _PulseSpeed);
                float width = _OutlineWidth * (1.0 + (pulse - 0.5) * _PulseWidth);
                float3 posOS = IN.positionOS.xyz + normalize(IN.normalOS) * width;
                OUT.positionHCS = TransformObjectToHClip(posOS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float pulse = 0.5 + 0.5 * sin(_Time.y * _PulseSpeed);
                return lerp(_OutlineColor, _GlowColor, pulse);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
