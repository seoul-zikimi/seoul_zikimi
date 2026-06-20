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
        createButton.onClick.AddListener(OnCreateSessionUI);
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