using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class ConnectionUI : MonoBehaviour
{
    private bool m_ShowLegacyOnGUI;

    private void Start()
    {
        JobsnailMainMenu.Show();
    }

    void OnGUI()
    {
        if (!m_ShowLegacyOnGUI)
            return;

        var style = new GUIStyle(GUI.skin.button);
        style.fontSize = 36;

        if (GUILayout.Button("Host", style, GUILayout.Height(120)))
        {
            NetworkManager.Singleton.StartHost();
            // NGO SceneManager 사용 — 모든 클라이언트 동시 씬 전환
            NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.AnswerAuthoring, LoadSceneMode.Single);
        }

        if (GUILayout.Button("Client", style, GUILayout.Height(120)))
            NetworkManager.Singleton.StartClient();
    }
}
