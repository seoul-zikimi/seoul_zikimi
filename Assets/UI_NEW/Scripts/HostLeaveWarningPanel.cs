using System;
using UnityEngine;

namespace SeoulZikimi.UI.New
{
    public sealed class HostLeaveWarningPanel : MonoBehaviour
    {
        [SerializeField] private UiNewScreenRouter router;

        public event Action ConfirmRequested;

        public void Cancel() => router.Show(UiNewScreen.Lobby);
        public void Confirm() => ConfirmRequested?.Invoke();
    }
}
