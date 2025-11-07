using System;

namespace ArmaReforgerFeeder
{
    /// <summary>
    /// Updated offsets for the latest game version.
    /// All offsets have been updated according to the new offset list.
    /// </summary>
    public static class Off
    {
        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        // Game / World
        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        public const ulong Game = 0x213A388;                    // ModuleBase + GameBase -> Game*
        public const ulong GameWorld = 0x130;                   // Game + World -> World*
        public const ulong LocalPlayerController = 0x378;       // World + LocalPlayerController -> WeakRef
        public const ulong LocalPlayer = 0x08;                  // WeakRef (+0x8) Instance
        public const ulong PlayerManager = 0x2C8;               // Game + PlayerManager (if used)

        // PlayerManager related (for name resolution)
        public const ulong PmPlayerArray = 0x18;
        public const ulong PmPlayerCount = 0x24;
        public const ulong Player_Name = 0x18;
        public const ulong Player_FirstLevelPtr = 0x48;
        public const ulong FirstLevel_ControlledEntity = 0x8;
        public const ulong ControlledEntity_Ptr2 = 0x10;

        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        // Entity Lists (PRIMARY - will be auto-discovered by resolver)
        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        public const ulong EntityList = 0x128;                  // World + EntityList -> Entity** (array)
        public const ulong EntityCount = 0x134;                 // World + EntityCount -> i32/i64 entity count

        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        // Camera Manager + Player Camera (UPDATED)
        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        public const ulong AVCameraManagerWeak = 0x318;         // Game + CameraManagerWeak -> WeakRef
        public const ulong AVCameraManager = 0x08;              // WeakRef (+0x8) CameraManager*
        public const ulong PlayerCameraWeak = 0x100;            // CameraManager + PlayerCameraWeak -> WeakRef
        public const ulong PlayerCamera = 0x08;                 // WeakRef (+0x8) PlayerCamera*

        // Camera pose
        public const ulong CameraPos = 0x58;                    // Camera + CamPosA -> Vec3
        public const ulong InvertedViewRight = 0x70;            // Camera + RightA -> Vec3
        public const ulong InvertedViewUp = 0x7C;               // Camera + UpA -> Vec3
        public const ulong VectorViewForward = 0x88;            // Camera + FwdA -> Vec3

        // Camera zoom / FOV
        public const ulong ZoomIncreaseFactor = 0x18C;          // Camera + ZoomA -> Vec3
        public const ulong CameraZoom = 0x20C;                  // Camera + CameraZoom (empirical)
        public const ulong FirstPersonFOV = 0x128;              // CameraManager + FirstPersonFOV
        public const ulong ThirdPersonFOV = 0x12C;              // CameraManager + ThirdPersonFOV
        public const ulong CameraViewType = 0x1C8;              // Camera + ViewType (2 = first person)

        // PIP/Scope
        public const ulong PipActiveFlag = 0x11C;               // CameraManager + PipActiveFlag
        public const ulong PipScopeCameraWeak = 0x118;          // CameraManager + PipScopeCameraWeak

        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        // Entity Base
        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        public const ulong EntityPosition = 0xA4;               // Entity + EntityPosition -> Vec3 world position
        public const ulong EntityMatrix = 0x80;                 // Entity + EntityMatrix -> Matrix3x4 (world transform)

        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        // Prefab / Class (UPDATED)
        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        public const ulong PrefabMgr = 0xE8;                    // Entity + PrefabData -> PrefabData*
        public const ulong PrefabDataClass = 0x08;              // PrefabData + PrefabDataClass -> DataClass*
        public const ulong PrefabDataType = 0x10;               // DataClass + ClassTypeString -> char* TypeName
        public const ulong PrefabDataClassAlt = 0x30;           // PrefabData + PrefabDataClassAlt -> AltDataClass*
        public const ulong PrefabModelNamePtr = 0x70;           // AltDataClass + VehicleModelName -> char* DisplayName

        // Vehicle-specific prefab offsets (same as above but kept for clarity)
        public const ulong PrefabMgrVic = 0xE8;
        public const ulong PrefabDataClassVic = 0x08;

        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        // Faction (UPDATED)
        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        public const ulong FactionComponent = 0x180;            // Entity + FactionWeak -> WeakRef/Ptr to Faction
        public const ulong FactionComponentDataClass = 0x08;    // Faction + FactionDataClass -> DataClass*
        public const ulong FactionComponentDataType = 0x68;     // DataClass + FactionTypeStr -> char* TypeName
        
        // Local faction (for filtering)
        public const ulong FactionComponentLocal = 0x180;       // Same as FactionComponent

        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        // Damage / HP / Hitzone (UPDATED)
        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        public const ulong DamageManagerWeak = 0x188;           // Entity + DmgManagerWeak -> WeakRef/Ptr to DamageManager
        public const ulong DamageComponent = 0x08;              // DamageManager + DmgComponent -> DamageComponent*
        public const ulong HealthContainer = 0xD0;              // DamageComponent + HealthContainer -> HealthContainer*
        public const ulong HealthCurrent = 0x18;                // HealthContainer + HealthCurrent -> float current HP
        public const ulong HealthMax = 0x1C;                    // HealthContainer + HealthMax -> float max HP

        public const ulong ExtDamageMgr = 0x130;                // Entity + ExtDmgManagerWeak -> WeakRef/Ptr to ExtendedDamageManager
        public const ulong HitZone = 0x70;                      // ExtendedDamageManager + Hitzone -> WeakRef/Ptr to Hitzone
        public const ulong HitZoneMaxHP = 0x38;                 // Hitzone + HitzoneMaxHp -> float
        public const ulong HitZoneHP = 0x3C;                    // Hitzone + HitzoneHp -> float
        public const ulong Isdead = 0x4C;                       // Hitzone + IsDead -> byte/bool

        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        // Mesh / Bones (UNCHANGED)
        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        public const ulong MeshComponent = 0x50;                // Entity + MeshObjectComponent -> MeshComponent*
        public const ulong MeshComponentData = 0x18;            // MeshComponent + MeshObjectComponentData -> MeshComponentData*
        public const ulong MeshObject = 0x30;                   // MeshComponentData + MeshObject -> MeshObject*
        public const ulong MeshComponentBones = 0x40;           // MeshComponent + MeshComponentBones -> BoneMatrices base
        public const ulong MeshComponentBonesMatrixSize = 0x30; // sizeof(Matrix3x4) per bone (48 bytes)
        public const ulong MeshObjectBonesCount = 0xF0;         // MeshObject + MeshObjectBonesCount -> u32 count
        public const ulong MeshObjectBonesList = 0xD0;          // MeshObject + MeshObjectBonesList -> Bone metadata array base
        public const ulong MeshObjectBonesSize = 0x18;          // sizeof(BoneMeta) stride in MeshObjectBonesList (24 bytes)

        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        // Animation / Stance
        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        public const ulong CharacterAnimationComponent = 0x138; // Entity + CharacterAnimationComponent
        public const uint CharacterStanceType = 0x1B0;          // CharacterAnimationComponent + StanceType -> int

        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        // Weapon / Hands (UPDATED from your list)
        // ¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T¨T
        public const ulong Weapon_CharController = 0xF8;        // Entity + CharacterController
        public const ulong Weapon_AnimComponent = 0x890;        // CharacterController + CharacterAnimationComponent
        public const ulong Weapon_HandsWeak = 0x8C0;            // CharacterAnimationComponent + HandsWeak -> Weak ptr to item
        public const ulong Weapon_ItemPrefabData = 0xD8;        // ItemInHandsComponent + ItemPrefabData
        public const ulong Weapon_Prefab = 0x30;                // ItemPrefabData + Prefab
        public const ulong Weapon_ItemNamePtr = 0x70;           // ItemPrefab + ItemNamePtr -> char*
    }
}