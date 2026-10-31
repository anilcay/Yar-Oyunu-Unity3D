using System.Collections.Generic;
using LugeRush.AI;
using LugeRush.Input;
using LugeRush.Vehicle;
using UnityEngine;

namespace LugeRush.Race
{
    /// <summary>
    /// Applies RaceSession (menu selections) to the sleds placed in a track scene before the race starts:
    /// player sled type/colour, bot count, bot difficulty, input sources and the post-finish autopilot.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class RaceBootstrap : MonoBehaviour
    {
        public RaceManager Manager;
        public SledCatalog Catalog;
        [Tooltip("Indexed by BotDifficulty (Easy, Normal, Hard).")]
        public AIDifficulty[] Difficulties = new AIDifficulty[3];
        [Tooltip("Difficulty used when the player is driven by AI (tests / after finishing).")]
        public AIDifficulty AutopilotDifficulty;
        [Tooltip("Roster in grid order; element 0 is the local player.")]
        public List<RaceParticipant> Roster = new List<RaceParticipant>();
        [Tooltip("Random bot sled types and colours (otherwise the prefab values are kept).")]
        public bool RandomizeBots = true;
        public int Seed = 1234;

        private static readonly string[] BotNames = { "Ayşe", "Mert", "Zeynep", "Can", "Elif", "Burak", "Deniz" };

        private RaceParticipant player;

        private void Awake()
        {
            if (Manager == null) Manager = FindFirstObjectByType<RaceManager>();
            var cfg = RaceSession.Config.Clone();
            cfg.Laps = Mathf.Clamp(cfg.Laps, RaceConfig.MinLaps, RaceConfig.MaxLaps);
            cfg.BotCount = Mathf.Clamp(cfg.BotCount, 0, RaceConfig.MaxSleds - 1);
            Manager.Config = cfg;

            var rng = new System.Random(Seed);
            int botIndex = 0;
            for (int i = 0; i < Roster.Count; i++)
            {
                var p = Roster[i];
                if (p == null) continue;
                p.GridSlot = i;
                var sled = p.GetComponent<SledController>();
                var visuals = p.GetComponent<SledVisuals>();

                if (p.IsLocalPlayer)
                {
                    player = p;
                    p.DisplayName = RaceSession.PlayerName;
                    if (Catalog != null)
                    {
                        AssignStats(sled, Catalog.GetStats(RaceSession.PlayerSled));
                        visuals?.ApplyColors(Catalog.GetBodyColor(RaceSession.PlayerColor), Catalog.GetHelmetColor(0));
                    }
                    if (RaceSession.PlayerAutopilot) EnableAutopilot(p);
                    else UsePlayerInput(p);
                    continue;
                }

                if (botIndex >= cfg.BotCount)
                {
                    p.gameObject.SetActive(false);
                    continue;
                }

                p.IsBot = true;
                p.DisplayName = BotNames[botIndex % BotNames.Length];
                if (Catalog != null && RandomizeBots)
                {
                    var type = (SledType)rng.Next(0, 4);
                    AssignStats(sled, Catalog.GetStats(type));
                    visuals?.ApplyColors(Catalog.GetBodyColor((SledColor)(botIndex % 4)), Catalog.GetHelmetColor(botIndex + 1));
                }
                var ai = EnsureAI(p);
                ai.Difficulty = Difficulties[Mathf.Clamp((int)cfg.Difficulty, 0, Difficulties.Length - 1)];
                ai.SkillBias = ((float)rng.NextDouble() - 0.5f) * 0.06f;
                if (sled != null) sled.InputSource = ai;
                botIndex++;
            }

            // Rubber band reference = local player.
            if (player != null)
            {
                foreach (var p in Roster)
                {
                    if (p == null || p == player) continue;
                    var ai = p.GetComponent<AIDriver>();
                    if (ai != null)
                    {
                        var self = p;
                        ai.GapProvider = () => self.RaceDistance - player.RaceDistance;
                    }
                }
            }

            Manager.ParticipantFinished += OnParticipantFinished;
        }

        private void OnDestroy()
        {
            if (Manager != null) Manager.ParticipantFinished -= OnParticipantFinished;
        }

        private static void AssignStats(SledController sled, SledStats stats)
        {
            if (sled == null || stats == null) return;
            sled.Stats = stats;
            sled.ConfigureBody();
        }

        private AIDriver EnsureAI(RaceParticipant p)
        {
            var ai = p.GetComponent<AIDriver>();
            if (ai == null) ai = p.gameObject.AddComponent<AIDriver>();
            ai.enabled = true;
            return ai;
        }

        private void UsePlayerInput(RaceParticipant p)
        {
            var input = p.GetComponent<PlayerSledInput>();
            var sled = p.GetComponent<SledController>();
            if (input != null && sled != null) sled.InputSource = input;
        }

        private void EnableAutopilot(RaceParticipant p)
        {
            var ai = EnsureAI(p);
            if (ai.Difficulty == null) ai.Difficulty = AutopilotDifficulty != null ? AutopilotDifficulty : Difficulties[1];
            var sled = p.GetComponent<SledController>();
            if (sled != null) sled.InputSource = ai;
            ai.BuildProfiles();
        }

        private void OnParticipantFinished(RaceParticipant p)
        {
            var bridge = p.GetComponent<SledAnimatorBridge>();
            if (bridge != null)
            {
                if (p.FinishOrder <= 3) bridge.TriggerWin();
                else bridge.TriggerLose();
            }
            // After crossing the line the player's sled keeps rolling under AI control.
            if (p.IsLocalPlayer) EnableAutopilot(p);
        }
    }
}
