using System;
using UnityEngine;
using UnityEngine.UI;

public class StartUGUI : MonoBehaviour
{
    public Button createButton;
    public Button joinButton;

    public event Action<bool> CreateBtnOnClick;
    public event Action<bool> JoinBtnOnClick;
    
    void Start()
    {
        if (createButton != null)
            createButton.onClick.AddListener(OnCreateSessionUI);
        if (joinButton != null)
            joinButton.onClick.AddListener(OnJoinByCodeUI);
    }

    private void OnCreateSessionUI()
    {
        CreateBtnOnClick?.Invoke(true);
    }

    private void OnJoinByCodeUI()
    {
        JoinBtnOnClick?.Invoke(true);
    }
}
