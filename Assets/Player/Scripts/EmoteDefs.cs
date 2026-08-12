using UnityEngine;

namespace Player
{
    /// <summary>
    /// 감정표현 대사 정의 — 기획서 '인게임 소통 수단 시스템'(07/24)의 감정표현 종류 11종.
    /// 대사 추가/변경은 이 배열만 수정하면 휠 UI(EmoteWheelUI)·발동(PlayerEmote)에 그대로 반영된다.
    ///
    /// [보이스 넣는 법] Assets/Resources/Voices/Emotes/ 에 VoiceName과 같은 이름의
    /// 오디오 파일(wav/mp3/ogg)을 넣으면 대사 발동 시 자동 재생(없으면 무음 — 에러 아님).
    /// mp4는 Unity가 오디오로 임포트하지 못하므로 mp3 등으로 변환해서 넣을 것.
    /// </summary>
    public static class EmoteDefs
    {
        public readonly struct Def
        {
            public readonly string Line;        // 화면에 뜨는 대사(기획서 원문 그대로)
            public readonly string VoiceName;   // Resources/Voices/Emotes/ 밑 파일 이름

            public Def(string line, string voiceName) { Line = line; VoiceName = voiceName; }
        }

        // 순서 = 휠 12시부터 시계방향. F1~F10 단축키는 앞 10개에 대응.
        public static readonly Def[] All =
        {
            new Def("망치 갖다줘!",    "Voice_Emote_00_HammerBring"),
            new Def("페인트 갖다줘!",  "Voice_Emote_01_PaintBring"),
            new Def("망치질 필요해!",  "Voice_Emote_02_HammerNeed"),
            new Def("페인트칠 필요해!", "Voice_Emote_03_PaintNeed"),
            new Def("고정 안됐어!",    "Voice_Emote_04_NotFixed"),
            new Def("여기 좀 지어줘!", "Voice_Emote_05_BuildHere"),
            new Def("오지 마!",        "Voice_Emote_06_DontCome"),
            new Def("잘했어!",         "Voice_Emote_07_GoodJob"),
            new Def("뭐해!!",          "Voice_Emote_08_WhatDoing"),
            new Def("좋았어!",         "Voice_Emote_09_Nice"),
            new Def("완성했어!",       "Voice_Emote_10_Complete"),
        };

        public static int Count => All.Length;

        // 보이스는 첫 요청 때 1회 로드 후 캐시(없는 파일도 null로 캐시해 반복 Load 방지)
        private static readonly AudioClip[] s_VoiceCache = new AudioClip[All.Length];
        private static readonly bool[] s_VoiceLoaded = new bool[All.Length];

        /// <summary>index 대사의 보이스 클립(파일이 아직 없으면 null).</summary>
        public static AudioClip Voice(int index)
        {
            if (index < 0 || index >= All.Length) return null;
            if (!s_VoiceLoaded[index])
            {
                s_VoiceCache[index] = Resources.Load<AudioClip>("Voices/Emotes/" + All[index].VoiceName);
                s_VoiceLoaded[index] = true;
            }
            return s_VoiceCache[index];
        }
    }
}
