using System;
using TMPro;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

public class ShowJoinCode : MonoBehaviour
{
    public TextMeshProUGUI roomNameText;
    public TextMeshProUGUI joinCodeText;

    private static string _roomName;
    private static string _sessionCode;
    
    public event Action OnDisableJoinCode;

    private void OnEnable()
    {
        roomNameText.text = _roomName;
        joinCodeText.text = _sessionCode;
    }

    private void OnDisable()
    {
        if (gameObject.scene.isLoaded == false)
            return;
        
        // 생성한 방 삭제
        OnDisableJoinCode?.Invoke();
    }

    public static void SetData(IHostSession sessionInfo)
    {
        _roomName = sessionInfo.Name;
        _sessionCode = sessionInfo.Code;
    }
}