// MamboDMA/Games/ABI/ABIGameConfig.cs
using System.Numerics;
using MamboDMA.Input;
using static MamboDMA.Games.ABI.ABIGame; // MakcuMouseButton + VK

namespace MamboDMA.Games.ABI
{
    /// <summary>
    /// Persistent config for Arena Breakout Infinite.
    /// </summary>
    public sealed class ABIGameConfig
    {
        // Process / attach
        public string AbiExe = "UAGame.exe";

        // Toggles
        public bool DrawBoxes = true;
        public bool DrawNames = true;
        public bool DrawDistance = true;
        public bool DrawSkeletons = false;
        public bool ShowDebug = false;

        // Ranges
        public float MaxDistance = 800f;          // meters
        public float MaxSkeletonDistance = 300f;  // meters

        // Death markers
        public bool  DrawDeathMarkers = true;
        public float DeathMarkerMaxDist = 1200f;  // meters
        public float DeathMarkerBaseSize = 10f;   // px

        // Label colors
        public Vector4 ColorPlayer = new(1f, 0.25f, 0.25f, 1f);
        public Vector4 ColorBot    = new(0f, 0.6f, 1f, 1f);

        // ESP colors
        public Vector4 ColorBoxVisible    = new(0.20f, 1.00f, 0.20f, 1f);
        public Vector4 ColorBoxInvisible  = new(1.00f, 0.50f, 0.00f, 1f);
        public Vector4 ColorSkelVisible   = new(1.00f, 1.00f, 1.00f, 1f);
        public Vector4 ColorSkelInvisible = new(0.70f, 0.70f, 0.70f, 1f);

        // Dead-marker colors
        public Vector4 DeadFill    = new(0, 0, 0, 1f);
        public Vector4 DeadOutline = new(1f, 0.84f, 0f, 1f);

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤ Aimbot ©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public bool AimbotEnabled = true;
        public bool AimbotRequireVisible = true;
        public bool AimbotTargetAIOnly = false;   // ¡°Target only AI¡±
        public bool AimbotTargetPMCOnly = false;  // ¡°Only PMC¡±
        public bool AimbotHeadshotAI = false;     // If true and IsBot => force head bone
        public bool AimbotRandomBone = false;

        public int  AimbotTargetBone = Skeleton.IDX_Head; // default bone
        public float AimbotMaxMeters = 400f;               // max distance to target
        public float AimbotFovPx = 90f;                   // max screen radius from crosshair
        public float AimbotSmoothSegments = 3f;            // Makcu move segments (2-10 is okay)
        public float AimbotPixelPower = 0.1f;              // scalar multiply on mouse pixels
        public float AimbotDeadzonePx = 0f;                // small deadzone so we don't jitter

        // Trigger (either VK or Makcu mouse button; both can work)
        public int AimbotKey = VK.RBUTTON;                 // hold this keyboard key via InputManager
        public MakcuMouseButton AimbotMakcuHoldButton = MakcuMouseButton.mouse4; // hold Mouse4 on Makcu
        public AimbotTargetMode AimbotTargetMode { get; set; } = AimbotTargetMode.ClosestToCrosshairInFov;

    }
}
