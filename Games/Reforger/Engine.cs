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
    /// <summary>
    /// Camera management and world-to-screen projection.
    /// Updated with new camera offsets.
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
            int vtype = 0;
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

            float blend = 0.3f;
            Camera.Position = Lerp(Camera.Position, pos, blend);
            Camera.Fwd = Lerp(Camera.Fwd, Normalize(fwd), blend);
            Camera.InvUp = Lerp(Camera.InvUp, Normalize(invU), blend);
            Camera.InvRight = Lerp(Camera.InvRight, Normalize(invR), blend);
            Camera.CameraZoom = zoom;
            Camera.ZoomIncreaseFactor = zf;
            Camera.Fov = (vtype == 2) ? f1 : f3;

            Camera.Aspect = (Screen.H > 0f) ? (Screen.W / Screen.H) : 1.0f;
            return true;
        }

        private static Vector3f Lerp(in Vector3f a, in Vector3f b, float t)
        {
            return new Vector3f(a.X + (b.X - a.X) * t,
                                a.Y + (b.Y - a.Y) * t,
                                a.Z + (b.Z - a.Z) * t);
        }

        private static void UpdateProjection()
        {
            float fov = 75f;
            if (Camera.CameraZoom > 0f) fov = 90f;

            double f = 1.0 / Math.Tan((fov * Math.PI / 180.0) * 0.5);
            float hf = (float)f;
            float vf = (float)f;

            float aspect = Camera.Aspect <= 0f ? 1f : Camera.Aspect;

            float fxH = hf;
            float fyH = hf * aspect;

            float fyV = vf;
            float fxV = vf / MathF.Max(1e-6f, aspect);

            if (Camera.CameraZoom > 0f)
            {
                float zx = (Camera.ZoomIncreaseFactor.X == 0f) ? 1f : Camera.ZoomIncreaseFactor.X;
                float zy = (Camera.ZoomIncreaseFactor.Y == 0f) ? 1f : Camera.ZoomIncreaseFactor.Y;

                fxH /= zx;
                fyH /= zy;
                fxV /= zx;
                fyV /= zy;
            }

            Camera.FxH = fxH;
            Camera.FyH = fyH;
            Camera.FxV = fxV;
            Camera.FyV = fyV;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WorldToScreen(in Vector3f p, out float sx, out float sy, bool? treatFovAsHorizontal = null)
        {
            sx = sy = 0f;

            var d = new Vector3f(p.X - Camera.Position.X, p.Y - Camera.Position.Y, p.Z - Camera.Position.Z);
            float tx = Vector3f.Dot(d, Camera.InvRight);
            float ty = Vector3f.Dot(d, Camera.InvUp);
            float tz = Vector3f.Dot(d, Camera.Fwd);
            if (tz <= 1e-4f) return false;

            bool horiz = treatFovAsHorizontal ?? FovIsHorizontal;
            float fx = horiz ? Camera.FxH : Camera.FxV;
            float fy = horiz ? Camera.FyH : Camera.FyV;

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