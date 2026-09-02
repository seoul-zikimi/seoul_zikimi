using System;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulZikimi.UI.New
{
    public sealed class PasswordPopupPanel : MonoBehaviour, IPasswordEntryActions
    {
        [SerializeField] private UiNewScreenRouter router;
        [SerializeField] private InputField passwordInput;
        [SerializeField] private Button submitButton;
        [SerializeField] private GameObject errorMessage;

        private UiLoadingSpinner joiningSpinner;   // 입장 요청 중 표시(성공·실패·팝업 닫힘에 정리)

        public event Action<string> PasswordSubmitted;

        private void Awake()
        {
            var passwordHint = PasswordPolicyHint.Attach(passwordInput);
            passwordInput.onValueChanged.AddListener(_ =>
            {
                // 생성 쪽과 같은 문자 정책으로 걸러 '입력 자체가 안 되는' 문자로 헤매지 않게 한다
                string clean = SessionPasswordPolicy.Sanitize(passwordInput.text);
                if (clean != passwordInput.text)
                {
                    passwordInput.text = clean;
                    passwordInput.caretPosition = clean.Length;
                    passwordHint?.Show();
                    return;   // 재설정이 이 콜백을 다시 부른다
                }
                RefreshSubmitInteractable();
                errorMessage.SetActive(false);
            });
            submitButton.onClick.AddListener(Submit);
        }

        private void OnEnable()
        {
            if (passwordInput == null)
                return;
            ClearJoiningSpinner();
            passwordInput.text = string.Empty;
            errorMessage.SetActive(false);
            RefreshSubmitInteractable();
            passwordInput.ActivateInputField();
        }

        private void OnDisable() => ClearJoiningSpinner();

        public void Close() => router.Show(UiNewScreen.RoomList);

        private void Submit()
        {
            if (joiningSpinner != null)
                return;   // 이미 입장 요청 중 — 중복 발사 방지
            PasswordSubmitted?.Invoke(passwordInput.text);
        }

        /// <summary>실제 입장 요청이 시작될 때 컨트롤러가 호출 — 방 목록에서 바로 입장할 때(카드 위 스피너)와
        /// 같은 대기 표시를 이 팝업에도 띄우고 버튼을 잠근다. 비밀번호가 틀린 경우엔 호출되지 않으므로
        /// 오답 시 스피너가 잠깐 번쩍이지 않는다.</summary>
        public void BeginJoining()
        {
            if (joiningSpinner != null || submitButton == null)
                return;
            joiningSpinner = UiLoadingSpinner.AttachBeside((RectTransform)submitButton.transform, Vector2.zero);
            RefreshSubmitInteractable();
        }

        public void CompleteJoin()
        {
            ClearJoiningSpinner();
            router.Show(UiNewScreen.Lobby);
        }

        /// <summary>비밀번호 불일치 또는 입장 실패 — 컨트롤러가 호출. 스피너를 걷고 다시 시도할 수 있게 한다.</summary>
        public void ShowError()
        {
            ClearJoiningSpinner();
            errorMessage.SetActive(true);
        }

        private void ClearJoiningSpinner()
        {
            if (joiningSpinner != null)
            {
                joiningSpinner.Detach();
                joiningSpinner = null;
            }
            RefreshSubmitInteractable();
        }

        // 입장 요청 중엔 잠그고, 그 외엔 비밀번호가 채워졌을 때만 누를 수 있다.
        private void RefreshSubmitInteractable()
        {
            if (submitButton == null || passwordInput == null)
                return;
            submitButton.interactable = joiningSpinner == null && !string.IsNullOrWhiteSpace(passwordInput.text);
        }
    }
}
