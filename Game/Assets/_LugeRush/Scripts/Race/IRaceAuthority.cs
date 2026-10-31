namespace LugeRush.Race
{
    /// <summary>
    /// Single entry point for every race state change. RaceManager evaluates the rules only when
    /// <see cref="HasAuthority"/> is true and submits results here; the authority applies them
    /// (locally, or host-side + replicated to clients in the NGO implementation).
    /// </summary>
    public interface IRaceAuthority
    {
        bool HasAuthority { get; }
        void Bind(RaceManager manager);
        void CommitState(RaceState state, float raceStartTime);
        void CommitCountdown(int tick);
        void CommitLap(RaceParticipant participant, int newLap, float lapTime);
        void CommitFinish(RaceParticipant participant, float totalTime);
    }

    /// <summary>Single player / offline authority: applies everything immediately.</summary>
    public class LocalRaceAuthority : IRaceAuthority
    {
        private RaceManager manager;

        public bool HasAuthority => true;
        public void Bind(RaceManager m) => manager = m;
        public void CommitState(RaceState state, float raceStartTime) => manager.ApplyState(state, raceStartTime);
        public void CommitCountdown(int tick) => manager.ApplyCountdown(tick);
        public void CommitLap(RaceParticipant p, int newLap, float lapTime) => manager.ApplyLap(p, newLap, lapTime);
        public void CommitFinish(RaceParticipant p, float totalTime) => manager.ApplyFinish(p, totalTime);
    }
}
