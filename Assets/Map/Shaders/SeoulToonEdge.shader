Shader "Hidden/SeoulToonEdge"
{
    // 전맵 카툰 외곽선 — URP FullScreenPassRendererFeature용 풀스크린 패스.
    // 깊이의 2차 미분(라플라시안)으로 실루엣 단차에만 잉크 라인을 긋는다:
    //  · 기울어진 바닥·벽 같은 '평면'에선 0에 수렴해 줄무늬가 안 생기고,
    //  · 블록·건물·소품의 윤곽에서만 선이 나온다 → 민짜 폴리곤 세상이 만화 그림처럼.
    // 원경(_FadeStart~_FadeEnd)은 라인을 걷어 실루엣 카드·안개 지평선을 지저분하게 하지 않는다.
    //
    // ⚠ 화면 색(_BlitTexture)은 절대 읽지 않는다 — 09/03에 fetchColorBuffer 경로가 빈 텍스처를 물어
    // 화면 전체가 마젠타 민판이 된 사고. 라인은 알파 블렌드 오버레이로만 얹는다(edge=0이면 완전 투명).
    // 적용/해제: Tools ▸ Map ▸ ★ 비주얼 스타일. 세기·색은 Assets/Map/Materials/Mat_ToonEdge에서 조절.
    Properties
    {
        _EdgeColor ("Edge Color", Color) = (0.09, 0.10, 0.16, 1)
        _Strength ("Strength", Range(0, 1)) = 0.55
        _Threshold ("Depth Threshold", Float) = 1
        _FadeStart ("Fade Start (m)", Float) = 40
        _FadeEnd ("Fade End (m)", Float) = 85
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "SeoulToonEdge"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // 순서 중요(09/03 진단: 'TEXTURE2D_X 미정의' 컴파일 에러) — Blit.hlsl이 쓰는 TEXTURE2D_X는
            // core TextureXR.hlsl에 있는데 core Common.hlsl만으론 안 들어온다. URP Core → TextureXR → Blit 순.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureXR.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _EdgeColor;
            float _Strength;
            float _Threshold;
            float _FadeStart;
            float _FadeEnd;

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float2 t = 1.0 / _ScreenParams.xy;
                float dC = EyeDepth(uv);
                float dN = EyeDepth(uv + float2(0.0,  t.y));
                float dS = EyeDepth(uv + float2(0.0, -t.y));
                float dE = EyeDepth(uv + float2( t.x, 0.0));
                float dW = EyeDepth(uv + float2(-t.x, 0.0));

                // 라플라시안 — 평면(1차 기울기)은 상쇄되고 단차(불연속)만 남는다
                float lap = abs(dN + dS - 2.0 * dC) + abs(dE + dW - 2.0 * dC);
                // 문턱은 거리 비례(원근에서 픽셀당 깊이차가 커지는 것 보정)
                float thr = _Threshold * (0.03 + dC * 0.015);
                float edge = smoothstep(thr, thr * 2.0, lap);

                // 원경 페이드 — 지평선/실루엣 카드엔 라인 금지 (하늘·로비 등 깊이 없는 화면도 자동 투명)
                edge *= 1.0 - smoothstep(_FadeStart, _FadeEnd, dC);

                return half4(_EdgeColor.rgb, edge * _Strength);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
