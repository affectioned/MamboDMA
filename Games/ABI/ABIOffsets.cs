namespace MamboDMA.Games.ABI
{
    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    // Bone/mesh offsets (updated with root CTW)
    //©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    public static class ABIOffsets
    {
        public const ulong GWorld = 0xAB9A098;
        public const ulong GNames = 0xB012000;
        public const ulong DecryuptKey = 0xB011C48;

        public const ulong UWorld_OwningGameInstance = 0x180;
        public const ulong UWorld_GameState = 0x120;
        public const ulong UWorld_PersistentLevel = 0x30;

        public const ulong UGameInstance_LocalPlayers = 0x38;
        public const ulong UPlayer_PlayerController = 0x30;
        public const ulong APlayerController_AcknowledgedPawn = 0x390;
        public const ulong APlayerController_PlayerCameraManager = 0x3A8;

        public const ulong USceneComponent_RelativeLocation = 0x16C;
        public const ulong USceneComponent_ComponentToWorld = 0x1C0; // FTransform (root SceneComponent)

        public const ulong AActor_RootComponent = 0x168;

        public const ulong AGameStateBase_PlayerArray = 0x328;
        public const ulong AGameStateBase_PlayerCount = 0x330;

        public const ulong ACharacter_Mesh = 0x380;
        public const ulong APlayerCameraManager_CameraCachePrivate = 0x2090;

        public const ulong ULevel_ActorArray = 0x98;
        public const ulong ULevel_ActorCount = 0xA0;

        // Skeletal mesh component
        public const ulong USkeletalMeshComponent_ComponentToWorld = 0x220;            // fallback CTW
        public static ulong USceneComponent_ComponentToWorld_Ptr = 0x210;              // FTransform
        public const ulong USkeletalMeshComponent_CachedComponentSpaceTransforms = 0x0918; // TArray<FTransform>
        public const ulong USkeletalMeshComponent_ComponentSpaceTransforms = 0x0918; // TArray<FTransform>
    }
}
