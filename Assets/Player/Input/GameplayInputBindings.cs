using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    /// <summary>
    /// UI와 무관한 게임플레이 키 설정 API.
    /// 설정 화면은 GetBindings/StartInteractiveRebind/Reset 메서드만 호출하면 된다.
    /// 기본 바인딩은 기존 조작을 그대로 유지하고, 저장된 override만 PlayerPrefs에서 덧씌운다.
    ///
    /// TODO(키 설정 UI 연결):
    /// 1. GetBindings() 결과를 목록으로 표시한다. IsComposite=true인 루트는 행에서 제외하고,
    ///    IsPartOfComposite=true인 Move의 up/down/left/right는 각각 독립된 키 설정 행으로 표시한다.
    /// 2. 키 변경 버튼 클릭 시 StartInteractiveRebind(ActionPath, BindingIndex, callback)를 호출한다.
    /// 3. callback success=false는 Escape 취소이므로 기존 표시를 유지한다.
    /// 4. '기본값' 버튼은 ResetBinding, '전체 기본값' 버튼은 ResetAll을 호출한다.
    /// 이 파일에서는 의도적으로 설정 UI나 프리팹을 생성하지 않는다.
    /// </summary>
    public static class GameplayInputBindings
    {
        public const string Move = "Player/Move";
        public const string Sprint = "Player/Sprint";
        public const string Jump = "Player/Jump";
        public const string Interact = "Player/Interact";
        public const string Process = "Player/Process";
        public const string Revert = "Player/Revert";
        public const string RotateHeld = "Player/RotateHeld";
        public const string Throw = "Player/Throw";
        public const string ToggleOrder = "Player/ToggleOrder";
        public const string EmoteWheel = "Player/EmoteWheel";
        public const string CameraRotate = "Camera/Rotate";
        public const string CameraZoom = "Camera/Zoom";

        private const string kOverridesKey = "GameplayInput.BindingOverrides.v2";
        private static InputActionRebindingExtensions.RebindingOperation s_Rebind;
        private static PlayerControls s_RebindControls;

        public static event Action OverridesChanged;

        public readonly struct BindingInfo
        {
            public readonly string ActionPath;
            public readonly string ActionName;
            public readonly int BindingIndex;
            public readonly string BindingName;
            public readonly string DisplayString;
            public readonly string DeviceLayout;
            public readonly bool IsComposite;
            public readonly bool IsPartOfComposite;

            public BindingInfo(string actionPath, string actionName, int bindingIndex, string bindingName,
                string displayString, string deviceLayout, bool isComposite, bool isPartOfComposite)
            {
                ActionPath = actionPath;
                ActionName = actionName;
                BindingIndex = bindingIndex;
                BindingName = bindingName;
                DisplayString = displayString;
                DeviceLayout = deviceLayout;
                IsComposite = isComposite;
                IsPartOfComposite = isPartOfComposite;
            }
        }

        [Serializable]
        private sealed class OverrideStore
        {
            public List<OverrideEntry> entries = new();
        }

        [Serializable]
        private sealed class OverrideEntry
        {
            public string actionPath;
            public int bindingIndex;
            public string overridePath;
        }

        public static PlayerControls CreateControls()
        {
            var controls = new PlayerControls();
            Configure(controls.asset);
            ApplySavedOverrides(controls.asset);
            return controls;
        }

        /// <summary>현재 저장된 키 목록. 반환값만으로 나중에 설정 UI를 구성할 수 있다.</summary>
        public static IReadOnlyList<BindingInfo> GetBindings()
        {
            using var controls = CreateControls();
            var result = new List<BindingInfo>();
            foreach (var map in controls.asset.actionMaps)
            foreach (var action in map.actions)
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                string display = action.GetBindingDisplayString(i, out string layout, out _);
                result.Add(new BindingInfo($"{map.name}/{action.name}", action.name, i,
                    binding.name, display, layout, binding.isComposite, binding.isPartOfComposite));
            }
            return result;
        }

        /// <summary>
        /// 다음에 누른 키/패드 버튼으로 교체한다. composite(Move)는 up/down/left/right 항목의 index를 넘긴다.
        /// UI를 닫거나 취소할 때 CancelInteractiveRebind를 호출할 수 있다.
        /// TODO: 실제 설정 팝업을 만들 때 중복 키 경고가 필요하면 완료 callback 직전에
        /// GetBindings()의 effective display/path를 비교하는 검증 단계를 추가한다.
        /// </summary>
        public static bool StartInteractiveRebind(string actionPath, int bindingIndex,
            Action<bool, string> completed = null)
        {
            CancelInteractiveRebind();
            s_RebindControls = CreateControls();
            var action = s_RebindControls.asset.FindAction(actionPath, false);
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count ||
                action.bindings[bindingIndex].isComposite)
            {
                s_RebindControls.Dispose();
                s_RebindControls = null;
                completed?.Invoke(false, null);
                return false;
            }

            action.Disable();
            s_Rebind = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Touchscreen>")
                .OnCancel(op => FinishRebind(false, action, bindingIndex, completed))
                .OnComplete(op => FinishRebind(true, action, bindingIndex, completed));
            s_Rebind.Start();
            return true;
        }

        public static void CancelInteractiveRebind() => s_Rebind?.Cancel();

        public static void ResetBinding(string actionPath, int bindingIndex)
        {
            using var controls = CreateControls();
            var action = controls.asset.FindAction(actionPath, false);
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) return;
            action.RemoveBindingOverride(bindingIndex);
            SaveOverrides(controls.asset);
        }

        public static void ResetAll()
        {
            CancelInteractiveRebind();
            PlayerPrefs.DeleteKey(kOverridesKey);
            PlayerPrefs.Save();
            OverridesChanged?.Invoke();
        }

        internal static void ApplySavedOverrides(InputActionAsset asset)
        {
            asset.RemoveAllBindingOverrides();
            string json = PlayerPrefs.GetString(kOverridesKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            var store = JsonUtility.FromJson<OverrideStore>(json);
            if (store?.entries == null) return;
            foreach (var entry in store.entries)
            {
                var action = asset.FindAction(entry.actionPath, false);
                if (action == null || entry.bindingIndex < 0 || entry.bindingIndex >= action.bindings.Count) continue;
                action.ApplyBindingOverride(entry.bindingIndex, entry.overridePath);
            }
        }

        private static void FinishRebind(bool success, InputAction action, int bindingIndex,
            Action<bool, string> completed)
        {
            string display = null;
            if (success)
            {
                display = action.GetBindingDisplayString(bindingIndex);
                SaveOverrides(s_RebindControls.asset);
            }
            s_Rebind?.Dispose();
            s_Rebind = null;
            s_RebindControls?.Dispose();
            s_RebindControls = null;
            completed?.Invoke(success, display);
        }

        private static void SaveOverrides(InputActionAsset asset)
        {
            var store = new OverrideStore();
            foreach (var map in asset.actionMaps)
            foreach (var action in map.actions)
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (string.IsNullOrEmpty(binding.overridePath)) continue;
                store.entries.Add(new OverrideEntry
                {
                    actionPath = $"{map.name}/{action.name}",
                    bindingIndex = i,
                    overridePath = binding.overridePath,
                });
            }
            PlayerPrefs.SetString(kOverridesKey, JsonUtility.ToJson(store));
            PlayerPrefs.Save();
            OverridesChanged?.Invoke();
        }

        private static void Configure(InputActionAsset asset)
        {
            var player = asset.FindActionMap("Player", true);
            var camera = asset.FindActionMap("Camera", true);

            EnsureBinding(player.FindAction("Move", true), "<Gamepad>/leftStick", "Gamepad");
            EnsureBinding(player.FindAction("Sprint", true), "<Gamepad>/leftStickPress", "Gamepad");
            EnsureButton(player, "Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            EnsureButton(player, "Interact", "<Mouse>/leftButton", "<Gamepad>/buttonWest");
            EnsureButton(player, "Process", "<Keyboard>/e", "<Gamepad>/buttonNorth");
            EnsureButton(player, "Revert", "<Keyboard>/z", "<Gamepad>/leftShoulder");
            EnsureButton(player, "RotateHeld", "<Keyboard>/r", "<Gamepad>/rightShoulder");
            EnsureButton(player, "Throw", "<Keyboard>/g", "<Gamepad>/rightTrigger");
            EnsureButton(player, "ToggleOrder", "<Keyboard>/tab", "<Gamepad>/select");
            EnsureButton(player, "EmoteWheel", "<Keyboard>/t", "<Gamepad>/start");
            for (int i = 0; i < 10; i++)
                EnsureKeyboardButton(player, $"Emote{i + 1}", $"<Keyboard>/f{i + 1}");

            EnsureBinding(camera.FindAction("Rotate", true), "<Gamepad>/rightStick", "Gamepad", "scaleVector2(x=12,y=12)");
            EnsureBinding(camera.FindAction("Zoom", true), "<Gamepad>/dpad/y", "Gamepad", "scale(factor=120)");
        }

        private static InputAction EnsureButton(InputActionMap map, string name, string keyboardPath, string gamepadPath)
        {
            var action = map.FindAction(name, false) ?? map.AddAction(name, InputActionType.Button, expectedControlLayout: "Button");
            EnsureBinding(action, keyboardPath, "Keyboard&Mouse");
            EnsureBinding(action, gamepadPath, "Gamepad");
            return action;
        }

        private static InputAction EnsureKeyboardButton(InputActionMap map, string name, string keyboardPath)
        {
            var action = map.FindAction(name, false) ?? map.AddAction(name, InputActionType.Button, expectedControlLayout: "Button");
            EnsureBinding(action, keyboardPath, "Keyboard&Mouse");
            return action;
        }

        private static void EnsureBinding(InputAction action, string path, string group, string processors = null)
        {
            foreach (var binding in action.bindings)
                if (binding.path == path) return;
            var syntax = action.AddBinding(path, groups: group);
            if (!string.IsNullOrEmpty(processors)) syntax.WithProcessor(processors);
        }
    }
}
