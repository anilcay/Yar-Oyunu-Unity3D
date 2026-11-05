using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using LugeRush.Race;
using LugeRush.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LugeRush.Network
{
    public enum SessionMode { None, SinglePlayer, DirectHost, DirectClient, OnlineHost, OnlineClient }
    public enum ServicesState { NotStarted, Initializing, Ready, Unavailable }

    /// <summary>Open room from the session browser (quick join list).</summary>
    public struct OpenRoom
    {
        public string Id;
        public string Name;
        public int Players;
        public int MaxPlayers;
    }

    /// <summary>
    /// Owns the NGO session lifecycle: Unity Gaming Services (Sessions + Relay) when available, direct IP
    /// otherwise, and single player as a local-only host. Spawns the lobby, starts races through
    /// NetworkManager.SceneManager and handles disconnects. Lives in Boot (DontDestroyOnLoad).
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class NetworkSessionService : MonoBehaviour, IRaceFlow
    {
        public const int DefaultPort = 7777;
        public const int MaxPlayers = LobbyRules.Capacity;

        public static NetworkSessionService Instance { get; private set; }

        [SerializeField] private NetworkObject lobbyPrefab;
        [SerializeField] private NetworkObject raceAuthorityPrefab;
        [SerializeField] private NetworkObject sledPrefab;
        [SerializeField] private float servicesTimeout = 10f;

        public SessionMode Mode { get; private set; }
        public ServicesState Services { get; private set; }
        public string ServicesMessage { get; private set; } = "";
        /// <summary>Room code (Sessions) shown in the lobby; null in direct IP mode.</summary>
        public string SessionCode => session?.Code;
        /// <summary>"192.168.1.20:7777" style hint for direct IP hosts.</summary>
        public string DirectAddressInfo { get; private set; } = "";
        public bool IsBusy { get; private set; }
        public bool IsHost => net != null && net.IsHost;
        public bool IsConnected => net != null && net.IsListening && (net.IsServer || net.IsConnectedClient);
        public bool InSession => Mode != SessionMode.None;
        public bool IsOnline => Mode == SessionMode.OnlineHost || Mode == SessionMode.OnlineClient;
        public string CurrentRaceScene { get; private set; }
        public IReadOnlyList<RosterEntry> Roster => roster;
        /// <summary>Message for the main menu after an involuntary return (host left, kicked...).</summary>
        public string PendingMenuMessage { get; set; }

        /// <summary>User facing status / error messages (Turkish).</summary>
        public event Action<string> Notice;
        public event Action StateChanged;

        private NetworkManager net;
        private UnityTransport transport;
        private ISession session;
        private readonly List<RosterEntry> roster = new List<RosterEntry>();
        private bool leaving;
        private Task servicesTask;

        public NetworkObject SledPrefab => sledPrefab;
        public NetworkObject RaceAuthorityPrefab => raceAuthorityPrefab;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LaunchArgs.Parse();

            if (!TryGetComponent(out net)) net = FindFirstObjectByType<NetworkManager>();
            transport = null;
            if (net != null) net.TryGetComponent(out transport);
            if (net == null)
            {
                Debug.LogError("[LR] NetworkSessionService needs a NetworkManager.");
                return;
            }
            net.NetworkConfig.ConnectionApproval = true;
            net.NetworkConfig.EnableSceneManagement = true;
            // A crashed peer is noticed within seconds (default is 30 s): dropped client -> bot, lost host -> menu.
            if (transport != null) transport.DisconnectTimeoutMS = 8000;
            net.ConnectionApprovalCallback = ApproveConnection;
            net.OnServerStarted += OnServerStarted;
            net.OnClientStarted += OnClientStarted;
            net.OnClientStopped += OnClientStopped;
            net.OnClientDisconnectCallback += OnClientDisconnect;
            net.OnTransportFailure += OnTransportFailure;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (net == null) return;
            net.OnServerStarted -= OnServerStarted;
            net.OnClientStarted -= OnClientStarted;
            net.OnClientStopped -= OnClientStopped;
            net.OnClientDisconnectCallback -= OnClientDisconnect;
            net.OnTransportFailure -= OnTransportFailure;
        }

        // ------------------------------------------------------------------ Services

        /// <summary>Initialises Unity Gaming Services + anonymous sign-in once; never throws.</summary>
        public Task InitializeServicesAsync()
        {
            if (servicesTask == null) servicesTask = InitializeServicesInternal();
            return servicesTask;
        }

        private async Task InitializeServicesInternal()
        {
            if (LaunchArgs.Offline || LaunchArgs.IsAutomation)
            {
                SetServices(ServicesState.Unavailable, "Çevrimdışı mod: doğrudan IP kullanılıyor.");
                return;
            }
            SetServices(ServicesState.Initializing, "Çevrimiçi servisler başlatılıyor...");
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await WithTimeout(UnityServices.InitializeAsync(), servicesTimeout, "UnityServices.InitializeAsync");
                if (!AuthenticationService.Instance.IsSignedIn)
                    await WithTimeout(AuthenticationService.Instance.SignInAnonymouslyAsync(), servicesTimeout, "SignInAnonymously");
                if (MultiplayerService.Instance == null) throw new InvalidOperationException("MultiplayerService unavailable");
                SetServices(ServicesState.Ready, "Çevrimiçi servisler hazır (Relay).");
                Debug.Log($"[LR] Unity Gaming Services ready, player {AuthenticationService.Instance.PlayerId}");
            }
            catch (Exception e)
            {
                SetServices(ServicesState.Unavailable,
                    "Çevrimiçi servisler kullanılamıyor (proje Unity Cloud'a bağlı değil ya da internet yok). Doğrudan IP ile oynayabilirsiniz.");
                Debug.LogWarning($"[LR] Unity Gaming Services unavailable, falling back to direct IP: {e.GetType().Name}: {e.Message}");
            }
        }

        private static async Task WithTimeout(Task task, float seconds, string what)
        {
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds)));
            if (done != task) throw new TimeoutException($"{what} timed out");
            await task;
        }

        private void SetServices(ServicesState state, string message)
        {
            Services = state;
            ServicesMessage = message;
            StateChanged?.Invoke();
        }

        // ------------------------------------------------------------------ Start / join

        /// <summary>Single player = local host on loopback that refuses every connection (same code path as online).</summary>
        public bool StartSinglePlayer()
        {
            if (!PrepareStart()) return false;
            var rng = new System.Random();
            for (int attempt = 0; attempt < 4; attempt++)
            {
                ushort port = (ushort)rng.Next(40000, 60000);
                transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
                Mode = SessionMode.SinglePlayer;
                if (net.StartHost())
                {
                    Debug.Log($"[LR] Single player host started (loopback port {port}).");
                    return true;
                }
                Mode = SessionMode.None;
            }
            RaiseNotice("Yerel oyun başlatılamadı.");
            return false;
        }

        public bool HostDirect(int port)
        {
            if (!PrepareStart()) return false;
            transport.SetConnectionData("127.0.0.1", (ushort)port, "0.0.0.0");
            Mode = SessionMode.DirectHost;
            if (!net.StartHost())
            {
                Mode = SessionMode.None;
                RaiseNotice($"Oda kurulamadı: {port} portu kullanımda olabilir.");
                return false;
            }
            DirectAddressInfo = string.Join("  ", LocalIPv4().Select(ip => $"{ip}:{port}"));
            if (string.IsNullOrEmpty(DirectAddressInfo)) DirectAddressInfo = $"127.0.0.1:{port}";
            Debug.Log($"[LR] Direct host started on port {port} ({DirectAddressInfo}).");
            StateChanged?.Invoke();
            return true;
        }

        public bool JoinDirect(string ip, int port)
        {
            if (!PrepareStart()) return false;
            transport.SetConnectionData(ip, (ushort)port);
            Mode = SessionMode.DirectClient;
            if (!net.StartClient())
            {
                Mode = SessionMode.None;
                RaiseNotice("Bağlantı başlatılamadı.");
                return false;
            }
            DirectAddressInfo = $"{ip}:{port}";
            Debug.Log($"[LR] Direct client connecting to {ip}:{port}.");
            RaiseNotice($"{ip}:{port} adresine bağlanılıyor...");
            StateChanged?.Invoke();
            return true;
        }

        /// <summary>Creates a Sessions room over Relay; falls back to a direct IP host when services are unavailable.</summary>
        public async Task<bool> HostOnlineAsync(string roomName)
        {
            await InitializeServicesAsync();
            if (Services != ServicesState.Ready)
            {
                RaiseNotice("Çevrimiçi servisler yok: doğrudan IP odası kuruluyor.");
                return HostDirect(DefaultPort);
            }
            if (!PrepareStart()) return false;
            IsBusy = true;
            StateChanged?.Invoke();
            try
            {
                Mode = SessionMode.OnlineHost;
                var options = new SessionOptions
                {
                    MaxPlayers = MaxPlayers,
                    IsPrivate = false,
                    Name = string.IsNullOrWhiteSpace(roomName) ? "Luge Rush odası" : roomName
                }.WithRelayNetwork();
                session = await MultiplayerService.Instance.CreateSessionAsync(options);
                HookSession();
                Debug.Log($"[LR] Online session created: code {session.Code}, id {session.Id}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LR] CreateSession failed: {e.Message}");
                Mode = SessionMode.None;
                session = null;
                RaiseNotice("Çevrimiçi oda kurulamadı. Doğrudan IP ile deneyin.");
                return false;
            }
            finally
            {
                IsBusy = false;
                StateChanged?.Invoke();
            }
        }

        public async Task<bool> JoinByCodeAsync(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0) { RaiseNotice("Oda kodunu girin."); return false; }
            await InitializeServicesAsync();
            if (Services != ServicesState.Ready)
            {
                RaiseNotice("Çevrimiçi servisler yok: kodla katılım kullanılamıyor. Host'un IP adresiyle katılın.");
                return false;
            }
            return await JoinSessionAsync(() => MultiplayerService.Instance.JoinSessionByCodeAsync(code), $"kod {code}");
        }

        public async Task<bool> JoinByIdAsync(string id)
        {
            await InitializeServicesAsync();
            if (Services != ServicesState.Ready) return false;
            return await JoinSessionAsync(() => MultiplayerService.Instance.JoinSessionByIdAsync(id), $"oda {id}");
        }

        private async Task<bool> JoinSessionAsync(Func<Task<ISession>> join, string what)
        {
            if (!PrepareStart()) return false;
            IsBusy = true;
            StateChanged?.Invoke();
            try
            {
                Mode = SessionMode.OnlineClient;
                session = await join();
                HookSession();
                Debug.Log($"[LR] Joined online session {session.Code}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LR] Join session ({what}) failed: {e.Message}");
                Mode = SessionMode.None;
                session = null;
                RaiseNotice("Odaya katılınamadı: oda bulunamadı, dolu ya da yarış başlamış olabilir.");
                return false;
            }
            finally
            {
                IsBusy = false;
                StateChanged?.Invoke();
            }
        }

        public async Task<List<OpenRoom>> QueryOpenRoomsAsync()
        {
            var list = new List<OpenRoom>();
            await InitializeServicesAsync();
            if (Services != ServicesState.Ready) return list;
            try
            {
                var result = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions { Count = 20 });
                foreach (var s in result.Sessions)
                {
                    if (s.IsLocked || s.AvailableSlots <= 0) continue;
                    list.Add(new OpenRoom { Id = s.Id, Name = s.Name, MaxPlayers = s.MaxPlayers, Players = s.MaxPlayers - s.AvailableSlots });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LR] QuerySessions failed: {e.Message}");
                RaiseNotice("Oda listesi alınamadı.");
            }
            return list;
        }

        private void HookSession()
        {
            if (session == null) return;
            session.RemovedFromSession += () => OnSessionLost("Odadan çıkarıldınız.");
            session.Deleted += () => OnSessionLost("Oda kapatıldı.");
        }

        private void OnSessionLost(string message)
        {
            if (leaving || Mode == SessionMode.None) return;
            Leave(message);
        }

        private bool PrepareStart()
        {
            if (net == null || transport == null)
            {
                RaiseNotice("Ağ bileşenleri eksik (Boot sahnesi yüklenmedi).");
                return false;
            }
            if (net.ShutdownInProgress || net.IsListening)
            {
                RaiseNotice("Önceki bağlantı kapanıyor, birazdan tekrar deneyin.");
                return false;
            }
            leaving = false;
            roster.Clear();
            CurrentRaceScene = null;
            DirectAddressInfo = "";
            return true;
        }

        // ------------------------------------------------------------------ Callbacks

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            if (request.ClientNetworkId == NetworkManager.ServerClientId)
            {
                response.Approved = true;
                return;
            }
            var lobby = NetworkLobby.Instance;
            int humans = net.ConnectedClientsIds.Count;
            var result = LobbyRules.CanJoin(humans, lobby != null && lobby.InRace.Value, Mode == SessionMode.SinglePlayer);
            response.Approved = result == LobbyRules.JoinResult.Ok;
            response.Reason = LobbyRules.JoinResultMessage(result);
            if (!response.Approved) Debug.Log($"[LR] Rejected client {request.ClientNetworkId}: {result}");
        }

        private void OnServerStarted()
        {
            RaceSession.NetworkRoster = true;
            RaceFlow.Handler = this;
            if (lobbyPrefab != null && NetworkLobby.Instance == null)
            {
                var lobby = Instantiate(lobbyPrefab);
                lobby.Spawn(false);
            }
            net.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            StateChanged?.Invoke();
        }

        private void OnClientStarted()
        {
            RaceSession.NetworkRoster = true;
            RaceFlow.Handler = this;
            StateChanged?.Invoke();
        }

        private void OnClientStopped(bool wasHost)
        {
            if (net.SceneManager != null) net.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
            RaceSession.NetworkRoster = false;
            if (ReferenceEquals(RaceFlow.Handler, this)) RaceFlow.Handler = null;
            var wasMode = Mode;
            Mode = SessionMode.None;
            Time.timeScale = 1f;
            StateChanged?.Invoke();
            if (!leaving && wasMode != SessionMode.None)
            {
                string reason = DisconnectMessage();
                Debug.Log($"[LR] Network stopped unexpectedly: {reason}");
                Leave(reason);
            }
        }

        private void OnClientDisconnect(ulong clientId)
        {
            if (net.IsServer)
            {
                if (clientId != NetworkManager.ServerClientId) Debug.Log($"[LR] Client {clientId} disconnected.");
                return;
            }
            // Local client lost the connection (host quit, rejected, network).
            if (leaving) return;
            string reason = DisconnectMessage();
            Debug.Log($"[LR] Disconnected from host: {reason}");
            Leave(reason);
        }

        /// <summary>Our own rejection reason, or a friendly text instead of NGO's "[Disconnect Event]..." diagnostics.</summary>
        private string DisconnectMessage()
        {
            string r = net.DisconnectReason;
            return string.IsNullOrEmpty(r) || r.StartsWith("[") ? "Host ile bağlantı koptu." : r;
        }

        private void OnTransportFailure()
        {
            Debug.LogWarning("[LR] Transport failure.");
            if (!leaving) Leave("Ağ hatası: bağlantı kesildi.");
        }

        // ------------------------------------------------------------------ Race flow (host)

        public bool CanStartRace(out string reason)
        {
            reason = "";
            var lobby = NetworkLobby.Instance;
            if (!IsHost || lobby == null) { reason = "Yalnızca host başlatabilir."; return false; }
            var content = GameContent.Instance;
            var entry = content != null && content.Tracks != null ? content.Tracks.Get(lobby.Settings.Value.TrackIndex) : null;
            if (!TrackCatalog.IsAvailable(entry)) { reason = "Bu harita henüz hazır değil (yakında)."; return false; }
            if (!LobbyRules.CanStart(lobby.Members())) { reason = "Herkes hazır olmalı."; return false; }
            return true;
        }

        /// <summary>Host: freezes the roster, closes the room and loads the race scene on every peer.</summary>
        public bool StartRace()
        {
            if (!CanStartRace(out string reason))
            {
                RaiseNotice(reason);
                return false;
            }
            var lobby = NetworkLobby.Instance;
            var content = GameContent.Instance;
            var settings = lobby.Settings.Value;
            var entry = content.Tracks.Get(settings.TrackIndex);

            roster.Clear();
            roster.AddRange(LobbyRules.BuildRoster(lobby.Members(), settings.Bots, NetworkManager.ServerClientId));
            lobby.ServerSetInRace(true);
            SetSessionLocked(true);
            CurrentRaceScene = entry.SceneName;
            lobby.ApplyToRaceSession();
            Debug.Log($"[LR] Starting race on {entry.TrackId} ({entry.SceneName}): laps={settings.Laps} sleds={roster.Count} " +
                      $"(humans {roster.Count(r => !r.IsBot)}, bots {roster.Count(r => r.IsBot)}) difficulty={(BotDifficulty)settings.Difficulty}");
            var status = net.SceneManager.LoadScene(entry.SceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogWarning($"[LR] LoadScene failed: {status}");
                lobby.ServerSetInRace(false);
                SetSessionLocked(false);
                RaiseNotice("Yarış sahnesi yüklenemedi.");
                return false;
            }
            return true;
        }

        private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            if (!net.IsServer || sceneName != CurrentRaceScene) return;
            if (timedOut != null && timedOut.Count > 0) Debug.LogWarning($"[LR] Clients timed out loading {sceneName}: {string.Join(",", timedOut)}");
            NetworkRaceAuthority.ServerSpawnRace(raceAuthorityPrefab, sledPrefab, roster, timedOut);
        }

        /// <summary>Host: same lobby, same track, new race (players who left are dropped).</summary>
        public void Rematch()
        {
            if (!IsHost || NetworkLobby.Instance == null) return;
            var lobby = NetworkLobby.Instance;
            var settings = lobby.Settings.Value;
            roster.Clear();
            roster.AddRange(LobbyRules.BuildRoster(lobby.Members(), settings.Bots, NetworkManager.ServerClientId));
            Debug.Log($"[LR] Rematch on {CurrentRaceScene} with {roster.Count} sleds.");
            net.SceneManager.LoadScene(CurrentRaceScene, LoadSceneMode.Single);
        }

        /// <summary>Host: everybody back to the lobby (main menu scene), room opens again.</summary>
        public void ReturnToLobby()
        {
            if (!IsHost) return;
            var lobby = NetworkLobby.Instance;
            if (lobby != null)
            {
                lobby.ServerSetInRace(false);
                lobby.ServerResetReady();
            }
            SetSessionLocked(false);
            CurrentRaceScene = null;
            var content = GameContent.Instance;
            net.SceneManager.LoadScene(content != null ? content.MainMenuScene : "MainMenu", LoadSceneMode.Single);
        }

        private async void SetSessionLocked(bool locked)
        {
            if (session == null || !session.IsHost) return;
            try
            {
                var host = session.AsHost();
                host.IsLocked = locked;
                await host.SavePropertiesAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LR] Could not {(locked ? "lock" : "unlock")} session: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ Leave

        /// <summary>Leaves the session (host: closes it) and returns to the main menu, optionally with a message.</summary>
        public void Leave(string message = null)
        {
            if (leaving) return;
            leaving = true;
            if (!string.IsNullOrEmpty(message)) PendingMenuMessage = message;
            Debug.Log($"[LR] Leaving session ({Mode}){(string.IsNullOrEmpty(message) ? "" : ": " + message)}");
            var s = session;
            session = null;
            if (s != null) LeaveSessionQuietly(s);
            if (net != null && (net.IsListening || net.IsClient || net.IsServer)) net.Shutdown();
            Mode = SessionMode.None;
            roster.Clear();
            CurrentRaceScene = null;
            RaceSession.NetworkRoster = false;
            if (ReferenceEquals(RaceFlow.Handler, this)) RaceFlow.Handler = null;
            Time.timeScale = 1f;
            StateChanged?.Invoke();
            StartCoroutine(ReturnToMenuWhenShutDown());
        }

        private static async void LeaveSessionQuietly(ISession s)
        {
            try { await s.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning($"[LR] Session leave: {e.Message}"); }
        }

        private IEnumerator ReturnToMenuWhenShutDown()
        {
            while (net != null && net.ShutdownInProgress) yield return null;
            var content = GameContent.Instance;
            string menu = content != null ? content.MainMenuScene : "MainMenu";
            if (SceneManager.GetActiveScene().name != menu) SceneManager.LoadScene(menu);
            yield return null;
            leaving = false;
            StateChanged?.Invoke(); // the main menu shows (and clears) PendingMenuMessage
        }

        private void RaiseNotice(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Debug.Log($"[LR] Notice: {message}");
            Notice?.Invoke(message);
        }

        public static List<string> LocalIPv4()
        {
            var list = new List<string>();
            try
            {
                foreach (var addr in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr)) list.Add(addr.ToString());
            }
            catch (Exception) { /* no DNS: hint stays empty */ }
            return list;
        }

        // ------------------------------------------------------------------ IRaceFlow

        public bool IsMultiplayer => Mode != SessionMode.SinglePlayer && Mode != SessionMode.None;
        public bool CanRematch => IsHost;
        public string ExitLabel => IsMultiplayer && IsHost ? "Lobiye Dön" : IsMultiplayer ? "Odadan Ayrıl" : "Ana Menü";

        void IRaceFlow.Rematch() => Rematch();

        void IRaceFlow.Exit()
        {
            if (IsMultiplayer && IsHost) ReturnToLobby();
            else Leave();
        }
    }
}
