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

        public event Action<string> PasswordSubmitted;

        private void Awake()
        {
            passwordInput.onValueChanged.AddListener(_ =>
            {
                submitButton.interactable = !string.IsNullOrWhiteSpace(passwordInput.text);
                errorMessage.SetActive(false);
            });
            submitButton.onClick.AddListener(Submit);
        }

        private void OnEnable()
        {
            if (passwordInput == null)
                return;
            passwordInput.text = string.Empty;
            errorMessage.SetActive(false);
            submitButton.interactable = false;
            passwordInput.ActivateInputField();
        }

        public void Close() => router.Show(UiNewScreen.RoomList);

        private void Submit() => PasswordSubmitted?.Invoke(passwordInput.text);

        public void CompleteJoin() => router.Show(UiNewScreen.Lobby);
        public void ShowError() => errorMessage.SetActive(true);
    }
}
