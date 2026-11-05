using System.Collections.Generic;
using LugeRush.AI;
using LugeRush.CameraSystem;
using LugeRush.Input;
using LugeRush.Race;
using LugeRush.UI;
using LugeRush.Vehicle;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace LugeRush.Network
{
    /// <summary>
    /// Network carrier of one race sled. The NetworkObject root holds only network components
    /// (NetworkObject + owner-authoritative NetworkTransform + this); on spawn every peer instantiates the
    /// gameplay sled prefab of the chosen type (SledCatalog.GetPrefab) as a separate, non-networked object:
    /// <list type="bullet">
    /// <item>Owner: the sled simulates normally (player input or AI); this root copies its pose every physics
    /// step and NetworkTransform replicates it. Race progress and steering input go out in owner NetworkVariables.</item>
    /// <item>Everyone else: the sled is a kinematic proxy (Frozen) moved to the interpolated root pose with
    /// MovePosition, so local sleds collide with it and get pushed (kinematic push).</item>
    /// </list>
    /// Bots are owned by the host. If a client drops, ownership returns to the host and the sled becomes a bot.
    /// </summary>
    [DefaultExecutionOrder(15)] // after RaceManager (10): proxies overwrite the odometer with the owner's value
    public class NetworkSled : NetworkBehaviour, ISledInputSource
    {
        public static readonly List<NetworkSled> All = new List<NetworkSled>();

        public readonly NetworkVariable<SledSpawnInfo> Info = new NetworkVariable<SledSpawnInfo>();
        private readonly NetworkVariable<float> ownerRaceDistance =
            new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<int> ownerInput =
            new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [Tooltip("Overrides GameContent.SledPrefab (gameplay sled: physics + visuals).")]
        [SerializeField] private GameObject sledPrefabOverride;
        [Tooltip("Jumps larger than this (respawn) are sent as teleports instead of being interpolated.")]
        [SerializeField] private float teleportDistance = 8f;

        public SledController Sled { get; private set; }
        public RaceParticipant Participant { get; private set; }
        /// <summary>True when this peer simulates the sled (owner: player, autopilot or bot).</summary>
        public bool IsDrivenLocally { get; private set; }

        private NetworkTransform netTransform;
        private Vector3 lastProxyPos;
        private float proxySpeed;
        private bool proxyHasPose;
        private RaceManager manager;

        public static NetworkSled Find(RaceParticipant p)
        {
            foreach (var s in All) if (s != null && s.Participant == p) return s;
            return null;
        }

        private void Awake()
        {
            netTransform = GetComponent<NetworkTransform>();
        }

        public override void OnNetworkSpawn()
        {
            BuildSled();
            All.Add(this);
            NetworkRaceAuthority.Instance?.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Unsubscribe();
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            Unsubscribe();
            if (Sled != null) Destroy(Sled.gameObject);
            base.OnDestroy();
        }

        public override void OnGainedOwnership()
        {
            if (IsServer && Sled != null && !IsDrivenLocally) BecomeBot();
        }

        // ------------------------------------------------------------------ Build

        private void BuildSled()
        {
            var content = GameContent.Instance;
            var info = Info.Value;
            var type = (SledType)(info.Sled % 4);
            var color = (SledColor)(info.Color % 4);
            var catalog = content != null ? content.Sleds : null;
            var prefab = sledPrefabOverride != null ? sledPrefabOverride : catalog != null ? catalog.GetPrefab(type) : null;
            if (prefab == null && content != null) prefab = content.SledPrefab;
            if (prefab == null)
            {
                Debug.LogError("[LR] NetworkSled: no gameplay sled prefab (SledCatalog / GameContent.SledPrefab).");
                return;
            }
            IsDrivenLocally = IsOwner;
            bool isLocalHuman = IsOwner && !info.IsBot;

            // Instantiate under an inactive holder so components can be configured before Awake.
            var holder = new GameObject("_SledSpawn");
            holder.SetActive(false);
            var go = Instantiate(prefab, transform.position, transform.rotation, holder.transform);
            go.name = $"Sled_{info.Slot}_{info.Name}";
            Sled = go.GetComponent<SledController>();
            Participant = go.GetComponent<RaceParticipant>();
            var stats = catalog != null ? catalog.GetStats(type) : null;
            if (stats != null) Sled.Stats = stats;
            Participant.DisplayName = info.Name.ToString();
            Participant.GridSlot = info.Slot;
            Participant.IsBot = info.IsBot;
            Participant.IsLocalPlayer = isLocalHuman;

            if (IsDrivenLocally)
            {
                if (isLocalHuman && !RaceSession.PlayerAutopilot && !LaunchArgs.Autopilot)
                {
                    var input = LugeRush.UI.UIFactory.GetOrAdd<PlayerSledInput>(go);
                    input.Actions = content != null ? content.InputActions : null;
                    Sled.InputSource = input;
                }
                else
                {
                    var difficulty = info.IsBot ? content.GetDifficulty(RaceSession.Config.Difficulty) : content.AutopilotDifficulty;
                    Sled.InputSource = AddAI(go, difficulty, info.IsBot);
                }
            }
            else
            {
                var recovery = go.GetComponent<SledRecovery>();
                if (recovery != null) recovery.enabled = false;
                Sled.InputSource = this;
            }

            go.transform.SetParent(null, true);
            Destroy(holder);

            var visuals = go.GetComponent<SledVisuals>();
            if (visuals != null && catalog != null)
                visuals.ApplyColors(catalog.GetBodyColor(color), catalog.GetHelmetColor(info.Helmet));
            if (!IsDrivenLocally) Sled.Frozen = true;
            lastProxyPos = transform.position;

            manager = RaceManager.Instance;
            if (manager != null)
            {
                manager.StateChanged += OnRaceStateChanged;
                manager.ParticipantFinished += OnParticipantFinished;
            }

            if (isLocalHuman)
            {
                var director = FindFirstObjectByType<SledCameraDirector>();
                if (director != null)
                {
                    director.PlayerInput = go.GetComponent<PlayerSledInput>();
                    director.Bind(Sled);
                }
            }
        }

        private AIDriver AddAI(GameObject go, AIDifficulty difficulty, bool rubberBand)
        {
            var ai = LugeRush.UI.UIFactory.GetOrAdd<AIDriver>(go);
            ai.enabled = true;
            ai.Difficulty = difficulty;
            ai.SkillBias = ((Info.Value.Slot * 37 % 11) / 10f - 0.5f) * 0.06f;
            if (rubberBand)
            {
                var self = Participant;
                ai.GapProvider = () =>
                {
                    var reference = ReferencePlayer();
                    return reference != null ? self.RaceDistance - reference.RaceDistance : 0f;
                };
            }
            return ai;
        }

        /// <summary>Rubber band reference for host bots: the host's own player (else the leading human).</summary>
        private static RaceParticipant ReferencePlayer()
        {
            RaceParticipant best = null;
            foreach (var s in All)
            {
                var p = s != null ? s.Participant : null;
                if (p == null || p.IsBot) continue;
                if (p.IsLocalPlayer) return p;
                if (best == null || p.RaceDistance > best.RaceDistance) best = p;
            }
            return best;
        }

        private void Unsubscribe()
        {
            if (manager == null) return;
            manager.StateChanged -= OnRaceStateChanged;
            manager.ParticipantFinished -= OnParticipantFinished;
            manager = null;
        }

        // ------------------------------------------------------------------ Runtime

        private void OnRaceStateChanged(RaceState s)
        {
            // RaceManager unfreezes every participant at GO; proxies must stay kinematic.
            if (!IsDrivenLocally && Sled != null) Sled.Frozen = true;
        }

        private void OnParticipantFinished(RaceParticipant p)
        {
            if (p != Participant || Sled == null) return;
            var bridge = Sled.GetComponent<SledAnimatorBridge>();
            if (bridge != null)
            {
                if (p.FinishOrder <= 3) bridge.TriggerWin();
                else bridge.TriggerLose();
            }
            // Keep rolling under AI after the line (like the offline RaceBootstrap).
            if (IsDrivenLocally && !(Sled.InputSource is AIDriver))
            {
                var content = GameContent.Instance;
                var ai = AddAI(Sled.gameObject, content != null ? content.AutopilotDifficulty : null, false);
                ai.BuildProfiles();
                Sled.InputSource = ai;
            }
        }

        private void FixedUpdate()
        {
            if (Sled == null || !IsSpawned) return;
            if (IsDrivenLocally)
            {
                var body = Sled.Body;
                Vector3 pos = body.position;
                Quaternion rot = body.rotation;
                if ((pos - transform.position).sqrMagnitude > teleportDistance * teleportDistance && netTransform != null && IsOwner)
                    netTransform.Teleport(pos, rot, transform.localScale);
                else
                    transform.SetPositionAndRotation(pos, rot);
                if (IsOwner)
                {
                    if (!Mathf.Approximately(ownerRaceDistance.Value, Participant.RaceDistance)) ownerRaceDistance.Value = Participant.RaceDistance;
                    int packed = PackInput(Sled.LastInput);
                    if (ownerInput.Value != packed) ownerInput.Value = packed;
                }
            }
            else
            {
                if (!Sled.Frozen) Sled.Frozen = true;
                Vector3 pos = transform.position;
                float dt = Time.fixedDeltaTime;
                float measured = proxyHasPose ? (pos - lastProxyPos).magnitude / dt : 0f;
                if (measured > 80f) measured = proxySpeed; // teleport
                proxySpeed = Mathf.Lerp(proxySpeed, measured, 0.25f);
                lastProxyPos = pos;
                proxyHasPose = true;
                Sled.MoveOnLift(pos, transform.rotation, proxySpeed);
                // Race progress comes from the owner (it alone knows its respawns).
                if (manager != null && (manager.State == RaceState.Racing || manager.State == RaceState.Finished))
                    Participant.RaceDistance = ownerRaceDistance.Value;
            }
        }

        private void Update()
        {
            if (IsServer && IsOwner && !IsDrivenLocally && Sled != null) BecomeBot();
        }

        /// <summary>Host: the owning client left. Keep the sled in the race, driven by AI.</summary>
        private void BecomeBot()
        {
            IsDrivenLocally = true;
            var content = GameContent.Instance;
            Participant.IsBot = true;
            Participant.DisplayName = LobbyRules.SanitizeName(Participant.DisplayName + " (bot)");
            var ai = AddAI(Sled.gameObject, content != null ? content.GetDifficulty(RaceSession.Config.Difficulty) : null, true);
            ai.BuildProfiles();
            Sled.InputSource = ai;
            var recovery = Sled.GetComponent<SledRecovery>();
            if (recovery != null) recovery.enabled = true;
            bool racing = manager != null && (manager.State == RaceState.Racing || manager.State == RaceState.Finished);
            if (racing)
            {
                Sled.Frozen = false;
                Sled.ControlsLocked = false;
                Sled.SetVelocity(transform.forward * proxySpeed);
            }
            Debug.Log($"[LR] Owner of {Participant.DisplayName} left: sled continues as a host bot.");
        }

        // ------------------------------------------------------------------ Proxy input (handlebar/animation)

        private static int PackInput(SledInput i)
        {
            int steer = Mathf.Clamp(Mathf.RoundToInt(i.Steer * 127f), -127, 127) + 127; // 0..254
            int brake = Mathf.Clamp(Mathf.RoundToInt(i.Brake * 255f), 0, 255);
            return steer | (brake << 8) | (i.Drift ? 1 << 16 : 0);
        }

        public SledInput ReadInput()
        {
            int v = ownerInput.Value;
            return new SledInput
            {
                Steer = ((v & 0xFF) - 127) / 127f,
                Brake = ((v >> 8) & 0xFF) / 255f,
                Drift = (v & (1 << 16)) != 0
            };
        }
    }
}
