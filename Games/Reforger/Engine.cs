using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MamboDMA;
using MamboDMA.Games;
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
        public int Seq; // even=stable, odd=writer in progress
        // Camera params (raw)
        public float Fov, CameraZoom;
        public Vector3f ZoomIncreaseFactor;
        public Vector3f Forward;
        public Vector3f Up;
        public Vector3f Right;
        public Matrix4x4 View;
        public Matrix4x4 Proj;
        public Matrix4x4 ViewProj;

        // Precomputed projection factors
        public float FxH, FyH, FxV, FyV;
        public float Aspect;
        
        // Scope detection
        public bool IsScoped;
        public bool IsPipActive;
    }

    public enum EntityStance : int
    {
        STAND = 0,
        CROUCH = 1,
        PRONE = 2,
        UNKNOWN = -1,
    }

    #endregion    
    
    /// <summary>
    /// Camera management and world-to-screen projection.
    /// FIXED: Proper scope detection and FOV handling
    /// </summary>
    public static class Game
    {
        public static CameraModel Camera;
        public static ScreenSettings Screen = new(ScreenService.Current.W, ScreenService.Current.H);
        public static bool FovIsHorizontal = false;

        private static ulong _gamePtr, _camMgrWeak, _camMgr, _playerCamWeak, _playerCam;
        private static bool _vpValid;
        private static long _lastCamTick;

        public static void Reset()
        {
            ResetCamera();
            if (ScreenService.Current.W <= 0 || ScreenService.Current.H <= 0)
                ScreenService.UpdateFromMonitor(GameSelector.SelectedMonitor);
            _gamePtr = _camMgrWeak = _camMgr = _playerCamWeak = _playerCam = 0;
        }

        public static void ResetCamera()
        {
            Camera.Position = default;
            Camera.Forward = new Vector3f(0, 0, -1);
            Camera.Up = new Vector3f(0, 1, 0);
            Camera.Right = new Vector3f(1, 0, 0);
            Camera.CameraZoom = 0f;
            Camera.ZoomIncreaseFactor = new Vector3f(1f, 1f, 1f);
            Camera.View = Matrix4x4.Identity;
            Camera.Proj = Matrix4x4.Identity;
            Camera.ViewProj = Matrix4x4.Identity;
            Camera.IsScoped = false;
            Camera.IsPipActive = false;
            _vpValid = false;
            _lastCamTick = 0;
        }

        public static void UpdateCamera()
        {
            if (_camMgr != 0 && _playerCam != 0)
            {
                if (FastCameraScatter())
                {
                    UpdateProjection();
                    return;
                }
                _camMgr = _playerCam = 0;
            }

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
            int vtype = 0, pipFlag = 0;
            Vector3f pos = default, invR = default, invU = default, fwd = default, zf = default;
            bool okFov = false, okPose = false;

            using var map = DmaMemory.Scatter();
            var rd = map.AddRound(useCache: false);

            rd[0].AddValueEntry<float>(0, _camMgr + Off.FirstPersonFOV);
            rd[0].AddValueEntry<float>(1, _camMgr + Off.ThirdPersonFOV);
            rd[0].AddValueEntry<Vector3f>(2, _playerCam + Off.CameraPos);
            rd[0].AddValueEntry<Vector3f>(3, _playerCam + Off.InvertedViewRight);
            rd[0].AddValueEntry<Vector3f>(4, _playerCam + Off.InvertedViewUp);
            rd[0].AddValueEntry<Vector3f>(5, _playerCam + Off.VectorViewForward);
            rd[0].AddValueEntry<int>(6, _playerCam + Off.CameraViewType);
            rd[0].AddValueEntry<float>(7, _playerCam + Off.CameraZoom);
            rd[0].AddValueEntry<Vector3f>(8, _playerCam + Off.ZoomIncreaseFactor);
            rd[0].AddValueEntry<int>(9, _camMgr + Off.PipActiveFlag);

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

                cb.TryGetValue<int>(9, out pipFlag);
            };

            map.Execute();
            if (!(okFov && okPose)) return false;

            // Prepare values
            var fwdN = Normalize(fwd);
            var invUN = Normalize(invU);
            var invRN = Normalize(invR);
            var aspect = (Screen.H > 0f) ? (Screen.W / Screen.H) : 1.0f;

            // Single writer section for all camera fields
            BeginCamWrite();

            // Light smoothing on position only (set to 0f if you want zero-latency)
            const float posBlend = 0.12f;
            Camera.Position = Lerp(Camera.Position, pos, posBlend);

            // NO smoothing on orientation to avoid edge drift/lag
            Camera.Fwd = fwdN;
            Camera.InvUp = invUN;
            Camera.InvRight = invRN;

            Camera.CameraZoom = zoom;
            Camera.ZoomIncreaseFactor = zf;
            Camera.Fov = (vtype == 2) ? f1 : f3;

            // Scope detection as you had
            Camera.IsPipActive = (pipFlag != 0);
            bool hasZoomFactor = (zf.X > 1.01f || zf.Y > 1.01f);
            Camera.IsScoped = Camera.IsPipActive || hasZoomFactor;

            Camera.Aspect = aspect;

            EndCamWrite();
            return true;
        }

        private static Vector3f Lerp(in Vector3f a, in Vector3f b, float t)
        {
            return new Vector3f(a.X + (b.X - a.X) * t,
                                a.Y + (b.Y - a.Y) * t,
                                a.Z + (b.Z - a.Z) * t);
        }

        private static float _baselineZx = 1f, _baselineZy = 1f;

        private static void UpdateProjection()
        {
            float fov;
            var camSnap = ReadCamStable(); // read once

            if (camSnap.IsScoped)
            {
                float zx = camSnap.ZoomIncreaseFactor.X > 0 ? camSnap.ZoomIncreaseFactor.X : 1f;
                float zy = camSnap.ZoomIncreaseFactor.Y > 0 ? camSnap.ZoomIncreaseFactor.Y : 1f;
                float maxZoom = Math.Max(zx, zy);
                fov = 75f / Math.Max(1f, maxZoom);
                fov = Math.Clamp(fov, 5f, 45f);
            }
            else if (camSnap.CameraZoom > 0f)
            {
                fov = 90f;
            }
            else
            {
                fov = 75f;
            }

            double f = 1.0 / Math.Tan((fov * Math.PI / 180.0) * 0.5);
            float vf = (float)f;

            float aspect = camSnap.Aspect > 0f ? camSnap.Aspect : 1f;

            // Base factors (vertical-FOV convention)
            float fxV = vf / MathF.Max(1e-6f, aspect);
            float fyV = vf;
            float fxH = vf;
            float fyH = vf * aspect;

            // --- Relative zoom scaling (uniform to avoid side skew) ---
            bool clearlyNoZoom = !camSnap.IsScoped && camSnap.CameraZoom <= 0.001f;

            float zNowX = (camSnap.ZoomIncreaseFactor.X > 0f && !float.IsNaN(camSnap.ZoomIncreaseFactor.X)) ? camSnap.ZoomIncreaseFactor.X : 1f;
            float zNowY = (camSnap.ZoomIncreaseFactor.Y > 0f && !float.IsNaN(camSnap.ZoomIncreaseFactor.Y)) ? camSnap.ZoomIncreaseFactor.Y : 1f;
            zNowX = Math.Clamp(zNowX, 0.05f, 5f);
            zNowY = Math.Clamp(zNowY, 0.05f, 5f);

            if (clearlyNoZoom)
            {
                const float a = 0.2f; // mild smoothing
                _baselineZx = (1f - a) * _baselineZx + a * zNowX;
                _baselineZy = (1f - a) * _baselineZy + a * zNowY;
            }
            else
            {
                if (_baselineZx <= 0f || float.IsNaN(_baselineZx)) _baselineZx = 1f;
                if (_baselineZy <= 0f || float.IsNaN(_baselineZy)) _baselineZy = 1f;
            }

            bool zooming = camSnap.IsScoped || camSnap.CameraZoom > 0.001f;
            float s = 1f;
            if (zooming)
            {
                float zRatioX = _baselineZx / zNowX;
                float zRatioY = _baselineZy / zNowY;
                s = MathF.Sqrt(MathF.Max(0.0001f, zRatioX * zRatioY)); // geometric mean keeps aspect true
                s = Math.Clamp(s, 0.25f, 4f);
            }

            BeginCamWrite();
            Camera.FxV = fxV * s;
            Camera.FyV = fyV * s;
            Camera.FxH = fxH * s;
            Camera.FyH = fyH * s;
            EndCamWrite();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BeginCamWrite() => System.Threading.Interlocked.Increment(ref Camera.Seq); // odd

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EndCamWrite()   => System.Threading.Interlocked.Increment(ref Camera.Seq); // even

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static CameraModel ReadCamStable()
        {
            while (true)
            {
                int s0 = Volatile.Read(ref Camera.Seq);
                if ((s0 & 1) != 0) continue;     // writer in progress
                var snap = Camera;               // struct copy
                int s1 = Volatile.Read(ref Camera.Seq);
                if (s0 == s1) return snap;       // stable snapshot
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WorldToScreen(in Vector3f p, out float sx, out float sy, bool? treatFovAsHorizontal = null)
        {
            sx = sy = 0f;
            var cam = ReadCamStable();
        
            var d = new Vector3f(p.X - cam.Position.X, p.Y - cam.Position.Y, p.Z - cam.Position.Z);
            float tx = Vector3f.Dot(d, cam.InvRight);
            float ty = Vector3f.Dot(d, cam.InvUp);
            float tz = Vector3f.Dot(d, cam.Fwd);
            if (tz <= 1e-4f) return false;
        
            bool horiz = treatFovAsHorizontal ?? FovIsHorizontal;
            float fx = horiz ? cam.FxH : cam.FxV;
            float fy = horiz ? cam.FyH : cam.FyV;
        
            double ndcX = (tx / tz) * fx;
            double ndcY = (ty / tz) * fy;
        
            sx = (float)((ndcX + 1.0) * (Screen.W * 0.5));
            sy = (float)((1.0 - ndcY) * (Screen.H * 0.5));
            return !(float.IsNaN(sx) || float.IsNaN(sy) || float.IsInfinity(sx) || float.IsInfinity(sy));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3f Normalize(in Vector3f v)
        {
            float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (len <= 1e-6f) return v;
            float inv = 1f / len;
            return new Vector3f(v.X * inv, v.Y * inv, v.Z * inv);
        }
    }
}