namespace MamboDMA.Games.ABI
{
    // Bone/mesh offsets + core engine pointers
    public static class ABIOffsets
    {
        public const ulong GWorld    = 0xAB9F1A8;
        public const ulong GNames    = 0xB017140;
        public const ulong GObjects    = 0xb031548;
        public const ulong DecryuptKey = 0xB016D88;

        public const ulong UWorld_OwningGameInstance = 0x180;
        public const ulong UWorld_GameState          = 0x120;
        public const ulong UWorld_PersistentLevel    = 0x30;

        public const ulong UGameInstance_LocalPlayers = 0x38;
        public const ulong UPlayer_PlayerController   = 0x30;

        public const ulong APlayerController_AcknowledgedPawn    = 0x390;
        public const ulong APlayerController_PlayerCameraManager = 0x3A8;
        public const ulong AController_ControlRotation = 0x378;   // FRotator (Pitch,Yaw,Roll)
        public const ulong AController_Pawn            = 0x340;   // APawn*
        public const ulong AController_PlayerState     = 0x318;   // APlayerState*

        public const ulong AActor_RootComponent = 0x168;

        public const ulong ULevel_ActorArray = 0x98;
        public const ulong ULevel_ActorCount = 0xA0;

        public const ulong AGameStateBase_PlayerArray = 0x328;
        public const ulong AGameStateBase_PlayerCount = 0x330;

        public const ulong ACharacter_Mesh = 0x380;

        // Scene/Skeletal components
        public const ulong USceneComponent_RelativeLocation = 0x16C; // fallback only
        public const ulong USceneComponent_ComponentToWorld = 0x1C0; // inline FTransform (root)
        public const ulong USkeletalMeshComponent_ComponentToWorld = 0x220; // inline FTransform (mesh)
        public static ulong USceneComponent_ComponentToWorld_Ptr = 0x210;              // FTransform
        // Camera cache (still used for W2S projection)
        public const ulong APlayerCameraManager_CameraCachePrivate        = 0x2090;
        public const ulong APlayerCameraManager_LastFrameCameraCachePrivate = 0x27C0;

        // Skeletal mesh: TArray<FTransform> ComponentSpaceTransforms
        public const ulong USkeletalMeshComponent_CachedComponentSpaceTransforms = 0x0918; // TArray<FTransform>
        public const ulong USkeletalMeshComponent_ComponentSpaceTransforms       = 0x0918; // mirror
    }
}
