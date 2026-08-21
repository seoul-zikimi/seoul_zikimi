using System;
using SeoulZikimi.Weather;
using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 로비에서 확정한 날씨 옵션을 서버에서 한 번 추첨하고 모든 클라이언트에 복제한다.
    /// 선택 규칙은 Weather 도메인에, 실제 네트워크/플레이어/그리드 적용은 이 어댑터에 둔다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GameLoopManager), typeof(GridNetwork))]
    public sealed class NetworkWeatherCoordinator : NetworkBehaviour
    {
        private readonly NetworkVariable<bool> m_WeatherEnabled = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> m_Season = new(
            (int)Season.Spring, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> m_Weather = new(
            (int)WeatherKind.Sunny, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GridNetwork m_GridNetwork;
        private GameLoopManager m_GameLoop;
        private float m_NextSlipCheck;
        private float m_NextWindDrop;

        public bool IsWeatherEnabled => m_WeatherEnabled.Value;
        public Season SelectedSeason => (Season)m_Season.Value;
        public WeatherKind SelectedWeather => (WeatherKind)m_Weather.Value;
        public event Action<WeatherSelection> SelectionChanged;

        private void Awake()
        {
            m_GridNetwork = GetComponent<GridNetwork>();
            m_GameLoop = GetComponent<GameLoopManager>();
        }

        public override void OnNetworkSpawn()
        {
            m_WeatherEnabled.OnValueChanged += OnEnvironmentChanged;
            m_Season.OnValueChanged += OnEnvironmentChanged;
            m_Weather.OnValueChanged += OnEnvironmentChanged;

            if (IsServer)
                SelectSessionWeather();

            ApplyVisualsAndNotify();
        }

        public override void OnNetworkDespawn()
        {
            m_WeatherEnabled.OnValueChanged -= OnEnvironmentChanged;
            m_Season.OnValueChanged -= OnEnvironmentChanged;
            m_Weather.OnValueChanged -= OnEnvironmentChanged;
            TeamWeatherFx.ClearBaseWeather();
        }

        private void SelectSessionWeather()
        {
            var selector = new WeightedWeatherSelector(
                SeasonWeatherTable.CreateDefault(), new SystemRandomSource());
            WeatherSelection selection = selector.Select(new WeatherSessionOptions(
                GameLoopManager.HostWeatherEnabled,
                GameLoopManager.HostSeasonSelectionMode,
                GameLoopManager.HostFixedSeason));

            m_WeatherEnabled.Value = selection.IsEnabled;
            m_Season.Value = (int)selection.Season;
            m_Weather.Value = selection.IsEnabled ? (int)selection.Weather : (int)WeatherKind.Sunny;
            m_NextSlipCheck = Time.time + 1f;
            m_NextWindDrop = Time.time + 15f;

            Debug.Log(selection.IsEnabled
                ? $"[Weather] 세션 날씨 확정: {selection.Season} / {selection.Weather}"
                : "[Weather] 세션 날씨 비활성화");
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || !m_WeatherEnabled.Value || m_GameLoop == null || !m_GameLoop.IsBuilding)
                return;

            WeatherKind weather = SelectedWeather;
            if (weather is WeatherKind.Rain or WeatherKind.Snow or WeatherKind.Typhoon)
                TickSlip(weather);
            if (weather is WeatherKind.StrongWind or WeatherKind.Typhoon)
                TickWind(weather);
        }

        private void TickSlip(WeatherKind weather)
        {
            if (Time.time < m_NextSlipCheck) return;
            m_NextSlipCheck = Time.time + 1f;
            if (NetworkManager.Singleton == null) return;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var player = client.PlayerObject;
                var body = player != null ? player.GetComponent<Rigidbody>() : null;
                if (body == null || body.linearVelocity.sqrMagnitude < 0.25f || UnityEngine.Random.value >= 0.1f)
                    continue;

                Vector2 random = UnityEngine.Random.insideUnitCircle.normalized;
                float strength = weather == WeatherKind.Typhoon ? 4.5f : 3.2f;
                SlipRpc(new Vector3(random.x, 1.8f, random.y) * strength,
                    RpcTarget.Single(client.ClientId, RpcTargetUse.Temp));
            }
        }

        private void TickWind(WeatherKind weather)
        {
            if (Time.time < m_NextWindDrop) return;
            m_NextWindDrop = Time.time + 15f;
            if (m_GridNetwork == null) return;

            int count = weather == WeatherKind.Typhoon ? 2 : 1;
            if (m_GameLoop != null && m_GameLoop.IsVersus)
            {
                int firstTeam = UnityEngine.Random.Range(0, 2);
                int collapsed = m_GridNetwork.ServerWindCollapse(firstTeam, count);
                if (collapsed == 0)
                    m_GridNetwork.ServerWindCollapse(1 - firstTeam, count);
            }
            else
            {
                m_GridNetwork.ServerWindCollapse(0, count);
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void SlipRpc(Vector3 impulse, RpcParams rpc = default)
        {
            var manager = NetworkManager.Singleton;
            var player = manager != null && manager.LocalClient != null
                ? manager.LocalClient.PlayerObject : null;
            if (player == null) return;

            var body = player.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic)
                body.AddForce(impulse, ForceMode.VelocityChange);
            // Player 어셈블리는 GridSystem을 참조하므로 역참조를 만들지 않는다.
            // 공개 Stun(float) 계약을 메시지 경계로 호출해 어셈블리 의존 방향을 유지한다.
            player.SendMessage("Stun", 0.8f, SendMessageOptions.DontRequireReceiver);
        }

        private void OnEnvironmentChanged(bool previousValue, bool newValue) => ApplyVisualsAndNotify();
        private void OnEnvironmentChanged(int previousValue, int newValue) => ApplyVisualsAndNotify();

        private void ApplyVisualsAndNotify()
        {
            WeatherKind weather = m_WeatherEnabled.Value ? SelectedWeather : WeatherKind.Sunny;
            TeamWeatherFx.Get().SetBaseWeather(weather);
            WeatherSelection selection = m_WeatherEnabled.Value
                ? WeatherSelection.Enabled(SelectedSeason, weather)
                : WeatherSelection.Disabled();
            SelectionChanged?.Invoke(selection);
        }
    }
}
