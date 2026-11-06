using LugeRush.AI;
using LugeRush.Race;
using LugeRush.Vehicle;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LugeRush.UI
{
    /// <summary>
    /// Shared runtime references for menus, HUD and network spawning (fonts, catalogs, gameplay sled prefab).
    /// Lives in a Resources folder so every scene (and play-in-editor from any scene) can reach it.
    /// </summary>
    [CreateAssetMenu(menuName = "Luge Rush/Game Content", fileName = ResourceName)]
    public class GameContent : ScriptableObject
    {
        public const string ResourceName = "LugeRushContent";

        [Header("Fonts (TextMeshPro, Turkish glyph coverage)")]
        public TMP_FontAsset BodyFont;
        public TMP_FontAsset DisplayFont;

        [Header("Gameplay")]
        public SledCatalog Sleds;
        public TrackCatalog Tracks;
        [Tooltip("Gameplay sled prefab (physics + visuals) instantiated by NetworkSled and the menu preview.")]
        public GameObject SledPrefab;
        public InputActionAsset InputActions;
        [Tooltip("Indexed by BotDifficulty (Easy, Normal, Hard).")]
        public AIDifficulty[] Difficulties = new AIDifficulty[3];
        public AIDifficulty AutopilotDifficulty;
        [Tooltip("SM_Track_Lift_Hook prefab shown between the cable and a sled on the lift.")]
        public GameObject LiftHookPrefab;

        [Header("Scenes")]
        public string BootScene = "Boot";
        public string MainMenuScene = "MainMenu";

        private static GameContent instance;

        public static GameContent Instance
        {
            get
            {
                if (instance == null) instance = Resources.Load<GameContent>(ResourceName);
                return instance;
            }
        }

        public AIDifficulty GetDifficulty(BotDifficulty d)
        {
            if (Difficulties == null || Difficulties.Length == 0) return AutopilotDifficulty;
            return Difficulties[Mathf.Clamp((int)d, 0, Difficulties.Length - 1)] ?? AutopilotDifficulty;
        }
    }
}
