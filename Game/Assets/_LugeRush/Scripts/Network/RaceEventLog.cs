using System.Text;
using LugeRush.Race;
using UnityEngine;

namespace LugeRush.Network
{
    /// <summary>
    /// Writes race events ("[LR] ...") to the player log; in automation mode also a periodic snapshot of every
    /// sled (position, speed, owner/proxy) so replication can be checked from the logs of both peers.
    /// </summary>
    [RequireComponent(typeof(RaceManager))]
    public class RaceEventLog : MonoBehaviour
    {
        public float SnapshotInterval = 5f;

        private RaceManager manager;
        private float nextSnapshot;
        private readonly StringBuilder sb = new StringBuilder(1024);

        private string Peer => Unity.Netcode.NetworkManager.Singleton == null ? "offline" :
            Unity.Netcode.NetworkManager.Singleton.IsHost ? "host" : Unity.Netcode.NetworkManager.Singleton.IsClient ? "client" : "offline";

        private void Awake()
        {
            manager = GetComponent<RaceManager>();
            manager.CountdownTick += t => Debug.Log($"[LR] ({Peer}) Countdown {(t > 0 ? t.ToString() : "GO")}");
            manager.RaceStarted += () =>
            {
                Debug.Log($"[LR] ({Peer}) RaceStarted participants={manager.Participants.Count} laps={manager.TotalLaps}");
                nextSnapshot = Time.time + 2f;
            };
            manager.LapCompleted += (p, lap) => Debug.Log($"[LR] ({Peer}) LapCompleted {p.DisplayName} lap={lap} time={p.LastLapTime:0.00}s");
            manager.ParticipantFinished += p => Debug.Log($"[LR] ({Peer}) ParticipantFinished P{p.FinishOrder} {p.DisplayName} total={p.TotalTime:0.00}s best={p.BestLapTime:0.00}s bot={p.IsBot}");
            manager.RaceFinished += OnRaceFinished;
        }

        private void OnRaceFinished()
        {
            sb.Clear();
            int finished = 0;
            foreach (var p in manager.Participants) if (p.Finished) finished++;
            sb.Append($"[LR] ({Peer}) RaceFinished finished={finished}/{manager.Participants.Count}");
            foreach (var p in manager.Standings)
                sb.Append($"\n[LR]   P{p.Position} {p.DisplayName} finished={p.Finished} total={p.TotalTime:0.00}s best={p.BestLapTime:0.00}s bot={p.IsBot}");
            Debug.Log(sb.ToString());
        }

        private void Update()
        {
            if (!LaunchArgs.IsAutomation || manager.State != RaceState.Racing || Time.time < nextSnapshot) return;
            nextSnapshot = Time.time + SnapshotInterval;
            sb.Clear();
            sb.Append($"[LR] ({Peer}) Snapshot t={manager.RaceTime:0.0}s");
            foreach (var p in manager.Standings)
            {
                var net = NetworkSled.Find(p);
                string role = net == null ? "local" : net.IsDrivenLocally ? (p.IsBot ? "bot" : "owner") : "proxy";
                Vector3 pos = p.transform.position;
                float speed = p.Controller != null ? p.Controller.SpeedKmh : 0f;
                sb.Append($"\n[LR]   P{p.Position} {p.DisplayName} [{role}] lap={p.Lap} dist={p.RaceDistance:0} pos=({pos.x:0.0},{pos.y:0.0},{pos.z:0.0}) v={speed:0}km/h");
            }
            Debug.Log(sb.ToString());
        }
    }
}
