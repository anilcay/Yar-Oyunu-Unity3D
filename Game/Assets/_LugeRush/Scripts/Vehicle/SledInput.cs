namespace LugeRush.Vehicle
{
    /// <summary>One frame of driver intent. Read by <see cref="SledController"/> every FixedUpdate.</summary>
    public struct SledInput
    {
        public float Steer;   // -1..1 (right positive)
        public float Brake;   // 0..1
        public bool Drift;    // held
        public bool Push;     // start push, pressed since last read
        public bool Reset;    // pressed since last read

        public static readonly SledInput None = default;
    }

    /// <summary>Anything that can drive a sled: local player, AI, network proxy, replay...</summary>
    public interface ISledInputSource
    {
        SledInput ReadInput();
    }
}
