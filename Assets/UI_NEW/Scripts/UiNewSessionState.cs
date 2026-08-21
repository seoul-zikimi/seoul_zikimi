using System;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace SeoulZikimi.UI.New
{
    public sealed class UiNewSessionState : MonoBehaviour
    {
        public ISession ActiveSession { get; private set; }
        public event Action<ISession> Changed;

        public void Set(ISession session)
        {
            ActiveSession = session;
            Changed?.Invoke(session);
        }

        public void Clear() => Set(null);
    }
}
