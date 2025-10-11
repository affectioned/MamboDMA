using System;

namespace MamboDMA.Games.ABI
{
    /// <summary>
    /// Offsets for Arena Breakout Infinite (ABI).
    /// </summary>
    public static class ABIOffsets
    {
        // Global objects
        public const ulong GWorld   = 0xAB9A038;
        public const ulong GNames   = 0xB011FC0;
        public const ulong GObjects = 0xB02C3C8;
        public const ulong DecBuffer = 0xB011C08;

        // World hierarchy
        public const ulong OwningGameInstance = 0x180;
        public const ulong PersistentLevel    = 0x30;
        public const ulong ActorsOffset       = 0x98;
        public const ulong ActorSize          = 0xA0;
        public const ulong BoneArrayOne = 0x658;
        public const ulong ComponentToWorld = 0x210;
        public const ulong ComponentToWorld_Translation = ComponentToWorld + 0x10;
        // Player hierarchy
        public const ulong LocalPlayers = 0x38;
        public const ulong PlayerController   = 0x30;
        public const ulong AcknowledgedPawn   = 0x390;
        public const ulong CameraManager        = 0x3A8;
        public const ulong CameraCachePrivate   = 0x2090;
        public const ulong CameraPOVLocation    = 0x10;
        public const ulong CameraPOVRotation    = 0x1C;
        public const ulong CameraCacheOffset  = 0x1E90 + 0x10;
        public const ulong PlayerState        = 0x340;
        public const ulong Controller         = 0x358;
        public const ulong CachedCharacterType = 0x152D;
        // Components
        public const ulong RootComponent = 0x168;
        public const ulong RelativeLocation   = 0x16C;
        public const ulong Mesh               = 0x380;
        public const ulong BoneArray          = 0x658;
        public const ulong PlayerNamePrivate  = 0x3F0;

        // Combat
        public const ulong DeathComponent     = 0x15B0;
        public const ulong DeathInfo          = 0x240;
        public const ulong TeamId             = 0x4D0;
        public const ulong WeaponManagerComponent = 0x15A8;
        public const ulong CurrentWeapon      = 0x150;
        public const ulong WeaponZoomComponent= 0xA38;
        public const ulong ZoomProgressRate   = 0x3C0;
        public const ulong ZoomProgressRateCheck = 0x3AC;

        // Visibility
        public const ulong LastSubmitTime     = 0x3DC;
        public const ulong LastRenderTime     = 0x3E0;
    }
}
