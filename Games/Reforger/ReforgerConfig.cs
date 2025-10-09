using System.Numerics;

namespace MamboDMA.Games.Reforger
{
    /// <summary>All per-game configurable settings for Reforger ESP/logic.</summary>
    public sealed class ReforgerConfig
    {
        // ───── ESP Toggles ─────
        public bool DrawBoxes = true;
        public bool HpBarEnabled = true;
        public bool HpTextEnabled = false;
        public bool ShowName = true;
        public bool ShowWeapon = true;
        public bool ShowDistance = true;

        // ───── ESP Layout ─────
        public float BoxWidthOffsetPx = 0f;
        public float BoxHeightOffsetPx = 0f;
        public float HeadTopOffsetPx = 0f;
        public float BoxOutlineThick = 1.6f;
        public float HpBarWidthPx = 4f;

        // ───── ESP Colors ─────
        public Vector4 BoxColor = new(212 / 255f, 175 / 255f, 55 / 255f, 1f);
        public Vector4 BoxShadowColor = new(0, 0, 0, 0.5f);
        public Vector4 LabelShadow = new(0, 0, 0, 0.62f);
        public Vector4 NameColor = new(1f, 1f, 1f, 1f);
        public Vector4 WeaponColor = new(200 / 255f, 230 / 255f, 255 / 255f, 1f);
        public Vector4 DistanceColor = new(180 / 255f, 255 / 255f, 190 / 255f, 1f);
        public Vector4 HpTextColor = new(1f, 0.86f, 0.63f, 1f);

        public Vector4 SkelColor = new(1f, 1f, 1f, 1f);
        public Vector4 SkelShadowColor = new(0, 0, 0, 0.55f);

        // ───── Player Filters ─────
        public bool IncludeFriendlies = false;
        public bool OnlyPlayersFromManager = false;
        public bool RequireHitZones = false;
        public bool IncludeRagdolls = false;
        public bool AnimatedOnly = true;

        // ───── Performance / Loops ─────
        public float MaxDrawDistance = 500f;
        public int FrameCap = 128;
        public int FastIntervalMs = 4;
        public int HpIntervalMs = 50;
        public int SlowIntervalMs = 200;

        // ───── Skeletons ─────
        public bool EnableSkeletons = true;
        public int SkeletonLevel = 10; // enum mapped
        public float SkeletonThickness = 1.2f;


        // ESP – Items
        public bool ShowItemsWeapons { get; set; } = true;
        public bool ShowItemsAmmo { get; set; } = true;
        public bool ShowItemsAttachments { get; set; } = true;
        public bool ShowItemsEquipment { get; set; } = true;
        public bool ShowItemsMisc { get; set; } = false;

        // ESP – Vehicles
        public bool ShowVehiclesCars { get; set; } = true;
        public bool ShowVehiclesHelis { get; set; } = true;
        public bool ShowVehiclesPlanes { get; set; } = false;
        public bool ShowVehiclesBoats { get; set; } = false;        
    }
}
