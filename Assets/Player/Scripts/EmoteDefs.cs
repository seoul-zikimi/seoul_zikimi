using System.Collections.Generic;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// 감정표현 대사 정의 — 기획서 '인게임 소통 수단 시스템'(07/24)의 감정표현 종류 11종.
    /// 대사 추가/변경은 이 배열만 수정하면 휠 UI(EmoteWheelUI)·발동(PlayerEmote)에 그대로 반영된다.
    ///
    /// [보이스 넣는 법] 캐릭터별로 폴더가 나뉜다 — 맵별 BGM(MapDef.MapBgm)과 같은 "빈 칸은 공용 폴백" 방식.
    ///   Assets/Resources/Voices/Emotes/default/     ← 달팽이
    ///   Assets/Resources/Voices/Emotes/char_turtle/ ← 거북이
    ///   Assets/Resources/Voices/Emotes/char_crab/   ← 소라게
    ///   Assets/Resources/Voices/Emotes/            ← 캐릭터 폴더가 비었을 때 쓰는 공용(선택)
    /// 각 폴더에 VoiceName과 같은 이름의 오디오 파일(wav/mp3/ogg)을 넣으면 그 캐릭터가 그 대사를 낼 때 재생된다.
    /// 셋 다 없으면 무음 — 에러 아님. mp4는 Unity가 오디오로 임포트하지 못하므로 mp3 등으로 변환할 것.
    ///
    /// [아이콘 넣는 법] Assets/Resources/UI_pngs/Emotes/ 에 IconName과 같은 이름의 PNG를 넣으면
    /// 말풍선 왼쪽에 붙는다(없으면 EmoteIconArt의 임시 아트, 그것도 없으면 대사만 — 에러 아님).
    /// </summary>
    public static class EmoteDefs
    {
        public readonly struct Def
        {
            public readonly string Line;        // 화면에 뜨는 대사(기획서 원문 그대로)
            public readonly string VoiceName;   // Resources/Voices/Emotes/<캐릭터>/ 밑 파일 이름(캐릭터 3종 공통)
            public readonly string IconName;    // Resources/UI_pngs/Emotes/ 밑 파일 이름(없으면 null)

            public Def(string line, string voiceName, string iconName = null)
            {
                Line = line; VoiceName = voiceName; IconName = iconName;
            }
        }

        // 순서 = 휠 12시부터 시계방향. F1~F10 단축키는 앞 10개에 대응.
        public static readonly Def[] All =
        {
            new Def("망치 갖다줘!",    "Voice_Emote_00_HammerBring", "Emote_Hammer"),
            new Def("페인트 갖다줘!",  "Voice_Emote_01_PaintBring"),
            new Def("망치질 필요해!",  "Voice_Emote_02_HammerNeed",  "Emote_Hammer"),
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

        /// <summary>캐릭터 id → 보이스 폴더 이름. 빈 id(달팽이)는 "default"(썸네일 Thumb_default와 같은 컨벤션).
        /// 캐릭터를 추가하면 CharacterCatalog에 한 줄 + 같은 이름의 폴더만 만들면 된다.</summary>
        public static string VoiceFolder(string characterId)
            => string.IsNullOrEmpty(characterId) ? "default" : characterId;

        // 보이스는 (캐릭터 폴더 × 대사)마다 첫 요청 때 1회 로드 후 캐시(없는 파일도 null로 캐시해 반복 Load 방지)
        private sealed class VoiceSet
        {
            public readonly AudioClip[] Clips = new AudioClip[All.Length];
            public readonly bool[] Loaded = new bool[All.Length];
        }

        private static readonly Dictionary<string, VoiceSet> s_Voices = new();

        /// <summary>index 대사를 characterId 캐릭터가 낼 때의 보이스 클립.
        /// 캐릭터 전용 > 공용(Voices/Emotes 바로 밑) 순으로 찾고, 둘 다 없으면 null(무음).</summary>
        public static AudioClip Voice(int index, string characterId = null)
        {
            if (index < 0 || index >= All.Length) return null;

            string folder = VoiceFolder(characterId);
            if (!s_Voices.TryGetValue(folder, out var set)) s_Voices[folder] = set = new VoiceSet();

            if (!set.Loaded[index])
            {
                string file = All[index].VoiceName;
                set.Clips[index] = Resources.Load<AudioClip>($"Voices/Emotes/{folder}/{file}")
                                ?? Resources.Load<AudioClip>("Voices/Emotes/" + file);   // 맵별 BGM처럼 빈 칸은 공용 폴백
                set.Loaded[index] = true;
            }
            return set.Clips[index];
        }

        // 아이콘도 첫 요청 때 1회 로드 후 캐시(PNG 없으면 임시 아트, 그것도 없으면 null 캐시)
        private static readonly Texture2D[] s_IconCache = new Texture2D[All.Length];
        private static readonly bool[] s_IconLoaded = new bool[All.Length];

        /// <summary>index 대사의 아이콘(PNG > 임시 아트 > null 순서).</summary>
        public static Texture2D Icon(int index)
        {
            if (index < 0 || index >= All.Length) return null;
            if (!s_IconLoaded[index])
            {
                string icon = All[index].IconName;
                if (!string.IsNullOrEmpty(icon))
                {
                    var tex = Resources.Load<Texture2D>("UI_pngs/Emotes/" + icon);
                    s_IconCache[index] = tex != null ? tex : EmoteIconArt.Placeholder(icon);
                }
                s_IconLoaded[index] = true;
            }
            return s_IconCache[index];
        }
    }
}
