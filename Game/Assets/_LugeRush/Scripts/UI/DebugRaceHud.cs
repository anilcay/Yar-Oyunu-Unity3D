using LugeRush.Race;
using UnityEngine;

namespace LugeRush.UI
{
    /// <summary>
    /// Legacy placeholder kept so scenes built before the real HUD keep working: it swaps itself for
    /// <see cref="RaceHud"/> at startup. New scenes get RaceHud directly from TrackSceneBuilder.
    /// </summary>
    public class DebugRaceHud : MonoBehaviour
    {
        public RaceManager Manager;
        public RaceParticipant Player;

        private void Awake()
        {
            var hud = UIFactory.GetOrAdd<RaceHud>(gameObject);
            if (hud.Manager == null) hud.Manager = Manager;
            if (hud.Player == null) hud.Player = Player;
            Destroy(this);
        }

        public static string FormatTime(float seconds) => UIStyle.FormatTime(seconds);
    }
}
