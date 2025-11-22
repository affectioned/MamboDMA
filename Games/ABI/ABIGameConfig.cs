// MamboDMA/Games/ABI/ABIGameConfig.cs
using System.Numerics;
using MamboDMA.Input;
using static MamboDMA.Games.ABI.ABIGame;

namespace MamboDMA.Games.ABI
{
    /// <summary>
    /// Persistent config for Arena Breakout Infinite.
    /// </summary>
    public sealed class ABIGameConfig
    {
        // Process / attach
        public string AbiExe = "UAGame.exe";
        
        // Player ESP Toggles
        public bool DrawBoxes = true;
        public bool DrawNames = true;
        public bool DrawDistance = true;
        public bool DrawSkeletons = false;
        public bool ShowDebug = false;
        
        // Player ESP Ranges
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
        public bool AimbotTargetAIOnly = false;
        public bool AimbotTargetPMCOnly = false;
        public bool AimbotHeadshotAI = false;
        public bool AimbotRandomBone = false;
        public int  AimbotTargetBone = Skeleton.IDX_Head;
        public float AimbotMaxMeters = 400f;
        public float AimbotFovPx = 90f;
        public float AimbotSmoothSegments = 3f;
        public float AimbotPixelPower = 0.1f;
        public float AimbotDeadzonePx = 0f;
        
        public int AimbotKey = 0x02; // VK_RBUTTON
        public MakcuMouseButton AimbotMakcuHoldButton = MakcuMouseButton.mouse4;
        public AimbotTargetMode AimbotTargetMode { get; set; } = AimbotTargetMode.ClosestToCrosshairInFov;
        
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤ Loot Display ©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // Ground Loot
        public bool  DrawGroundLoot = true;
        public bool  DrawGroundLootNames = true;
        public bool  DrawGroundLootDistance = true;
        public float GroundLootMaxDistance = 150f;
        public float GroundLootMarkerSize = 6f;
        
        // Containers
        public bool  DrawContainers = true;
        public bool  DrawContainerNames = true;
        public bool  DrawContainerDistance = true;
        public bool  DrawEmptyContainers = true;  // Changed default to true
        public float ContainerMaxDistance = 200f;
        public float ContainerMarkerSize = 8f;
        public Vector4 ContainerFilledColor = new(1f, 0.84f, 0f, 1f);
        public Vector4 ContainerEmptyColor = new(0.5f, 0.5f, 0.5f, 1f);
        
        // Item Filtering
        public string LootFilterInclude = "";
        public string LootFilterExclude = "";
        public int    LootMinPrice = 0;  // Legacy - kept for compatibility
        
        // Loot Display Style - Price-based filtering & coloring
        public int  LootMinPriceRegular = 5000;      // Min price for regular loot to show
        public int  LootMinPriceImportant = 50000;   // Min price for "important" (highlighted) loot
        public bool LootShowPrice = true;
        public Vector4 LootRegularColor = new(0f, 1f, 0.5f, 1f);      // Green for regular loot
        public Vector4 LootImportantColor = new(1f, 0.1f, 1f, 1f);    // Magenta for valuable loot
        
        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        // Loot Widget (Real-time valuable loot display)
        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        public bool DrawLootWidget = true;
        public int LootWidgetMaxItems = 20;        // Show top 20 items
        public int LootWidgetMinPrice = 5000;      // Show items worth at least this much
        public bool LootWidgetShowDistance = true;
        public Vector4 LootWidgetBackground = new Vector4(0.08f, 0.08f, 0.08f, 0.92f);
        public Vector4 LootWidgetImportantColor = new Vector4(1f, 0.65f, 0.1f, 1f);  // Gold for expensive items
        public Vector2 LootWidgetPosition = new Vector2(20, 400);  // Top-left corner position
    }
}