using System;
using System.Numerics;

namespace MamboDMA.Games.ABI
{
    public static class ABIMath
    {
        public static Vector3 Sub(Vector3 a, Vector3 b) =>
            new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static float Dot(Vector3 a, Vector3 b) =>
            a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>
        /// Builds Unreal-style orientation axes from pitch/yaw/roll.
        /// </summary>
        public static void GetAxes(Vector3 rot, out Vector3 x, out Vector3 y, out Vector3 z)
        {
            float pitch = rot.X * (float)Math.PI / 180f;
            float yaw   = rot.Y * (float)Math.PI / 180f;
            float roll  = rot.Z * (float)Math.PI / 180f;

            float sp = MathF.Sin(pitch);
            float cp = MathF.Cos(pitch);
            float sy = MathF.Sin(yaw);
            float cy = MathF.Cos(yaw);
            float sr = MathF.Sin(roll);
            float cr = MathF.Cos(roll);

            // Unreal forward (X), right (Y), up (Z)
            x = new Vector3(cp * cy, cp * sy, sp);
            y = new Vector3(cy * sp * sr - cr * sy, sy * sp * sr + cr * cy, -sr * cp);
            z = new Vector3(-(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp);
        }

        /// <summary>
        /// Projects a 3D world position to 2D screen coordinates (Unreal left-handed).
        /// </summary>
        public static bool WorldToScreen(Vector3 world, out Vector2 screen, CameraInfo cam, float width, float height)
        {
            screen = Vector2.Zero;

            // World to camera space
            Vector3 delta = Sub(world, cam.Position);
            Vector3 transformed = new(
                Dot(delta, cam.AxisX),
                Dot(delta, cam.AxisY),
                Dot(delta, cam.AxisZ)
            );

            // Unreal's camera looks along +X (forward)
            if (transformed.X <= 1f) return false;

            float fovRad = cam.Fov * (float)Math.PI / 180f;
            float tanHalfFov = MathF.Tan(fovRad / 2f);
            float cx = width * 0.5f;
            float cy = height * 0.5f;

            screen.X = cx - (transformed.Y * cx) / (tanHalfFov * transformed.X);
            screen.Y = cy - (transformed.Z * cx) / (tanHalfFov * transformed.X);

            return screen.X >= 0 && screen.X <= width && screen.Y >= 0 && screen.Y <= height;
        }

        public static float Distance(Vector3 a, Vector3 b) =>
            MathF.Sqrt((a.X - b.X) * (a.X - b.X) +
                       (a.Y - b.Y) * (a.Y - b.Y) +
                       (a.Z - b.Z) * (a.Z - b.Z));

        public static float CrosshairDistance(Vector2 a, Vector2 b) =>
            MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    public struct CameraInfo
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 AxisX;
        public Vector3 AxisY;
        public Vector3 AxisZ;
        public float   Fov;
    }
}
