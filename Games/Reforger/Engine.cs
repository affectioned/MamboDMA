using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using MamboDMA;
using MamboDMA.Games;
using VmmSharpEx.Scatter;
using static MamboDMA.Misc;

namespace ArmaReforgerFeeder
{
    #region === DTOs / Math types =================================================================

    public struct ActorDto
    {
        // Pointers & identity
        public ulong Ptr;
        public string Entity;
        public string Faction;
        public string Name; 

        // State
        public int Health;
        public int Distance;
        public bool IsDead;

        // World/screen anchors
        public Vector3f Position;   // legacy anchor (fallback)
        public Vector2f Projected;

        // Precise head anchor
        public bool HasHead;
        public Vector3f HeadWorld;
        public Vector2f Head2D;

        // Skeleton / box data
        public bool HasBones;
        public Vector2f[]? Bones;
        public Vector2f BMin, BMax;
        public EntityStance Stance;
        public float BoxW;
        public float BoxH;
        public Vector3f Velocity;     // m/s
        public float    Speed;        // |Velocity|
        public Vector3f Velocity2D;   // (X,Z), Y=0
        public float    Speed2D;      // |Velocity2D|
        public Vector3f Dir;          // unit 3D direction (Velocity normalized)
        public Vector3f Dir2D;        // unit ground direction
        public float    YawRad;       // heading on ground (Y-up world)
        public float    YawDeg;       
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Vector2f
    {
        public float X, Y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2f(float x, float y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Vector3f
    {
        public float X, Y, Z;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3f(float x, float y, float z) { X = x; Y = y; Z = z; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(in Vector3f a, in Vector3f b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Matrix3x4
    {
        public float m00, m01, m02, m03, m04, m05, m06, m07, m08, m09, m10, m11;
    }

    public struct CameraModel
    {
        // Pose
        public Vector3f Position, InvRight, InvUp, Fwd;

        // Camera params (raw)
        public float Fov, CameraZoom;
        public Vector3f ZoomIncreaseFactor;
        public Vector3f Forward;
        public Vector3f Up;
        public Vector3f Right;
        public Matrix4x4 View;
        public Matrix4x4 Proj;
        public Matrix4x4 ViewProj;

        // Precomputed projection factors (avoid trig per WorldToScreen)
        // If FOV is treated as horizontal:  ndc = (x/z)*FxH, (y/z)*FyH
        // If FOV is treated as vertical:    ndc = (x/z)*FxV, (y/z)*FyV
        public float FxH, FyH, FxV, FyV;
        public float Aspect;              // Screen.W / Screen.H (float cache)
    }

    public enum EntityStance : int
    {
        STAND = 0,
        CROUCH = 1,
        PRONE = 2,
        UNKNOWN = -1,
    }

    #endregion

    #region === Offsets ============================================================================
    public static class Off
    {
        // Game / world
        public const ulong Game = 0x1D59488;
        public const ulong GameWorld = 0x130;
        public const ulong LocalPlayerController = 0x378;
        public const ulong LocalPlayer = 0x08;
        public const ulong PlayerManager  = 0x2D0;   // you already have this
        public static ulong PmPlayerArray = 0x18;    // vector<Player*> 
        public static ulong PmPlayerCount = 0x24;    // int
        public static ulong Player_Name   = 0x18;    // ptr to UTF8 name
        public static ulong Player_FirstLevelPtr = 0x48;
        public static ulong FirstLevel_ControlledEntity = 0x8;
        public static ulong ControlledEntity_Ptr2 = 0x10;
        // eastl::vector<PlayerController*> layout (begin,end) – confirm these:
        public const ulong PmControllersBegin = 0x1A8;   // TODO: verify
        public const ulong PmControllersEnd   = 0x1B0;   // TODO: verify
    
        public const ulong Controller_Player            = 0x08;  // you use this for LocalPlayer already
        public const ulong Controller_ControlledEntity  = 0x1F0; // TODO: verify

        // Camera manager + camera
        public const ulong AVCameraManagerWeak = 0x308;
        public const ulong AVCameraManager = 0x08;
        public const ulong FirstPersonFOV = 0x128;
        public const ulong ThirdPersonFOV = 0x12C;
        public const ulong PlayerCameraWeak = 0x108;
        public const ulong PlayerCamera = 0x08;
        public const ulong CameraPos = 0x58;
        public const ulong InvertedViewRight = 0x70;
        public const ulong InvertedViewUp = 0x7C;
        public const ulong VectorViewForward = 0x88;
        public const ulong CameraViewType = 0x1C8;
        public const ulong CameraZoom = 0x214;
        public const ulong ZoomIncreaseFactor = 0x18C;

        // Entities
        public const ulong EntityList = 0x118;
        public const ulong EntityCount = 0x124;
        public const ulong EntityPosition = 0x68;

        // Faction
        public const ulong FactionComponent = 0x188;
        public const ulong FactionComponentLocal = 0x178;
        public const ulong FactionComponentDataClass = 0x08;
        public const ulong FactionComponentDataType = 0x68;

        // Damage / HP
        public const ulong ExtDamageMgr = 0x148;
        public const ulong HitZone = 0xC8;
        public const ulong HitZoneMaxHP = 0x40;
        public const ulong HitZoneHP = 0x44;
        public const ulong Isdead = 0x4C;

        public const ulong PrefabMgr       = 0xE8;  // entity -> prefab data
        public const ulong PrefabDataClass = 0x8;  // <-- was 0x08; this matches your C++ sample
        public const ulong PrefabDataType  = 0x10;  // RTTI-ish string (often "VehicleClass")
        public const ulong PrefabModelNamePtr = 0x78; // char* -> "Vehicle/Car/HMMWV.et"
        // Prefab
        public const ulong PrefabMgrVic       = 0xE8;  // entity -> prefab data
        public const ulong PrefabDataClassVic = 0x30;  // <-- was 0x08; this matches your C++ sample
        public const ulong PrefabDataTypeVic  = 0x10;  // RTTI-ish string (often "VehicleClass")
        public const ulong PrefabModelNamePtrVic = 0x78; // char* -> "Vehicle/Car/HMMWV.et"

        // Common component pointers
        public const ulong EntityMatrix = 0x80;
        public const ulong MeshComponent = 0x50;             // MeshObjectComponent
        public const ulong MeshComponentData = 0x18;         // MeshObjectComponentData
        public const ulong MeshObject = 0x30;
        public const ulong MeshComponentBones = 0x40;
        public const ulong MeshComponentBonesMatrixSize = 0x30;
        public const ulong MeshObjectBonesCount = 0x100;
        public const ulong MeshObjectBonesList = 0xE0;
        public const ulong MeshObjectBonesSize = 0x18;

        // Animation
        public const ulong CharacterAnimationComponent = 0x138;
        public const uint CharacterStanceType = 0x1B0;
    }
    #endregion

    #region === Game: camera + projection + W2S ====================================================
    public static class Game
    {
        // Public state
        public static CameraModel Camera;
        public static ScreenSettings Screen = new(ScreenService.Current.W, ScreenService.Current.H);
        public static void Reset()
        {
            ResetCamera();
            if (ScreenService.Current.W <= 0 || ScreenService.Current.H <= 0)
                ScreenService.UpdateFromMonitor(GameSelector.SelectedMonitor);            
            _gamePtr = _camMgrWeak = _camMgr = _playerCamWeak = _playerCam = 0;
        }
        /// <summary>
        /// If true, treat FOV as horizontal. If false, treat FOV as vertical.
        /// You can override per-call in WorldToScreen with the nullable parameter.
        /// </summary>
        public static bool FovIsHorizontal = false;

        // Cached pointers for the 1-round scatter path
        static ulong _gamePtr, _camMgrWeak, _camMgr, _playerCamWeak, _playerCam;

        public static void ResetCamera()
        {
            Camera.Position = default;                 // (0,0,0)
            Camera.Forward  = new Vector3f(0, 0, -1);  // sane default
            Camera.Up       = new Vector3f(0, 1,  0);
            Camera.Right    = new Vector3f(1, 0,  0);

            Camera.CameraZoom        = 0f;             // hipfire
            Camera.ZoomIncreaseFactor = new Vector3f(1f, 1f, 1f); // no extra zoom

            Camera.View     = Matrix4x4.Identity;
            Camera.Proj     = Matrix4x4.Identity;
            Camera.ViewProj = Matrix4x4.Identity;

            _vpValid = false;
            _lastCamTick = 0;
        }        
        private static bool _vpValid;
        private static long _lastCamTick;        
        #region Camera update (fast scatter)
        public static void UpdateCamera()
        {
            // If we have valid cached pointers, try the fast path first.
            if (_camMgr != 0 && _playerCam != 0)
            {
                if (FastCameraScatter())
                {
                    UpdateProjection();  // <-- precompute Fx/Fy once per frame
                    return;
                }
                // Stale pointers, reset and resolve again.
                _camMgr = _playerCam = 0;
            }

            // Resolve pointers cheaply, then go through the fast path.
            if (!DmaMemory.Read(DmaMemory.Base + Off.Game, out _gamePtr) || _gamePtr == 0) return;
            DmaMemory.Read(_gamePtr + Off.AVCameraManagerWeak, out _camMgrWeak);
            DmaMemory.Read(_camMgrWeak + Off.AVCameraManager, out _camMgr);
            DmaMemory.Read(_camMgr + Off.PlayerCameraWeak, out _playerCamWeak);
            DmaMemory.Read(_playerCamWeak + Off.PlayerCamera, out _playerCam);
            if (_camMgr == 0 || _playerCam == 0) return;

            if (FastCameraScatter())
                UpdateProjection();
        }

        private static bool FastCameraScatter()
        {
            float f1 = 0, f3 = 0, zoom = 0;
            int vtype = 0;
            Vector3f pos = default, invR = default, invU = default, fwd = default, zf = default;
            bool okFov = false, okPose = false;

            using var map = DmaMemory.Scatter();
            var rd = map.AddRound(useCache: false);

            // Gather everything needed for pose + fov in one round.
            rd[0].AddValueEntry<float>(0, _camMgr + Off.FirstPersonFOV);
            rd[0].AddValueEntry<float>(1, _camMgr + Off.ThirdPersonFOV);
            rd[0].AddValueEntry<Vector3f>(2, _playerCam + Off.CameraPos);
            rd[0].AddValueEntry<Vector3f>(3, _playerCam + Off.InvertedViewRight);
            rd[0].AddValueEntry<Vector3f>(4, _playerCam + Off.InvertedViewUp);
            rd[0].AddValueEntry<Vector3f>(5, _playerCam + Off.VectorViewForward);
            rd[0].AddValueEntry<int>(6, _playerCam + Off.CameraViewType);
            rd[0].AddValueEntry<float>(7, _playerCam + Off.CameraZoom);
            rd[0].AddValueEntry<Vector3f>(8, _playerCam + Off.ZoomIncreaseFactor);

            rd[0].Completed += (_, cb) =>
            {
                okFov = cb.TryGetValue<float>(0, out f1) &&
                        cb.TryGetValue<float>(1, out f3);

                okPose = cb.TryGetValue<Vector3f>(2, out pos) &&
                         cb.TryGetValue<Vector3f>(3, out invR) &&
                         cb.TryGetValue<Vector3f>(4, out invU) &&
                         cb.TryGetValue<Vector3f>(5, out fwd) &&
                         cb.TryGetValue<int>(6, out vtype) &&
                         cb.TryGetValue<float>(7, out zoom) &&
                         cb.TryGetValue<Vector3f>(8, out zf);
            };

            map.Execute();
            if (!(okFov && okPose)) return false;

            float blend = 0.3f; // 0 = instant, 1 = no change
            Camera.Position = Lerp(Camera.Position, pos, blend);
            Camera.Fwd = Lerp(Camera.Fwd, Normalize(fwd), blend);
            Camera.InvUp = Lerp(Camera.InvUp, Normalize(invU), blend);
            Camera.InvRight = Lerp(Camera.InvRight, Normalize(invR), blend);
            Camera.CameraZoom = zoom;
            Camera.ZoomIncreaseFactor = zf;
            Camera.Fov = (vtype == 2) ? f1 : f3;  // 2 == first person

            // Aspect cached as float (updated in UpdateProjection each frame)
            Camera.Aspect = (Screen.H > 0f) ? (Screen.W / Screen.H) : 1.0f;

            return true;
        }
        private static Vector3f Lerp(in Vector3f a, in Vector3f b, float t)
        {
            return new Vector3f(a.X + (b.X - a.X) * t,
                                a.Y + (b.Y - a.Y) * t,
                                a.Z + (b.Z - a.Z) * t);
        }

        #endregion

        #region Projection precompute (per-frame)
        /// <summary>
        /// Precomputes projection factors Fx/Fy for both horizontal and vertical FOV interpretations.
        /// This removes all trig, division-by-aspect, and zoom divisions from every WorldToScreen call.
        /// </summary>
        private static void UpdateProjection()
        {
            // Effective FOV (zoom uses a fixed 90 in your code)
            float fov = Camera.Fov;
            if (Camera.CameraZoom > 0f) fov = 90f;

            // 1/tan(fov/2)
            double f = 1.0 / Math.Tan((fov * Math.PI / 180.0) * 0.5);
            float hf = (float)f;                 // horizontal reference factor
            float vf = (float)f;                 // vertical reference factor

            float aspect = Camera.Aspect <= 0f ? 1f : Camera.Aspect;

            // Horizontal-interpretation pair
            float fxH = hf;
            float fyH = hf * aspect;

            // Vertical-interpretation pair
            float fyV = vf;
            float fxV = vf / MathF.Max(1e-6f, aspect);

            // Apply zoom scaling (divide by per-axis factor if zoomed)
            if (Camera.CameraZoom > 0f)
            {
                float zx = (Camera.ZoomIncreaseFactor.X == 0f) ? 1f : Camera.ZoomIncreaseFactor.X;
                float zy = (Camera.ZoomIncreaseFactor.Y == 0f) ? 1f : Camera.ZoomIncreaseFactor.Y;

                fxH /= zx; fyH /= zy;
                fxV /= zx; fyV /= zy;
            }

            Camera.FxH = fxH; Camera.FyH = fyH;
            Camera.FxV = fxV; Camera.FyV = fyV;
        }
        #endregion

        #region WorldToScreen (hot path)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WorldToScreen(in Vector3f p, out float sx, out float sy, bool? treatFovAsHorizontal = null)
        {
            sx = sy = 0f;

            // Camera-space delta
            var d = new Vector3f(p.X - Camera.Position.X, p.Y - Camera.Position.Y, p.Z - Camera.Position.Z);
            float tx = Vector3f.Dot(d, Camera.InvRight);
            float ty = Vector3f.Dot(d, Camera.InvUp);
            float tz = Vector3f.Dot(d, Camera.Fwd);
            if (tz <= 1e-4f) return false;

            // Choose precomputed factors
            bool horiz = treatFovAsHorizontal ?? FovIsHorizontal;
            float fx = horiz ? Camera.FxH : Camera.FxV;
            float fy = horiz ? Camera.FyH : Camera.FyV;

            // NDC
            double ndcX = (tx / tz) * fx;
            double ndcY = (ty / tz) * fy;

            // Screen
            sx = (float)((ndcX + 1.0) * (Screen.W * 0.5));
            sy = (float)((1.0 - ndcY) * (Screen.H * 0.5));
            return !(float.IsNaN(sx) || float.IsNaN(sy) || float.IsInfinity(sx) || float.IsInfinity(sy));
        }
        #endregion

        #region Helpers
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3f Normalize(in Vector3f v)
        {
            float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (len <= 1e-6f) return v;
            float inv = 1f / len;
            return new Vector3f(v.X * inv, v.Y * inv, v.Z * inv);
        }
        #endregion
    }
    #endregion
}
