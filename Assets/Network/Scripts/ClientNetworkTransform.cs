using UnityEngine;
using Unity.Netcode.Components;

/// <summary>
/// Owner-authoritative NetworkTransform.
/// 기본 NetworkTransform은 서버 권한 → 클라이언트 이동 덮어씀.
/// 이걸로 교체하면 Owner가 자신의 Transform을 직접 씀.
/// </summary>
[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
