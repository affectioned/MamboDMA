using System;
using System.Numerics;

namespace MamboDMA.Games.ABI
{
    /// <summary>Math helpers ported from ABI C++.</summary>
    public static class ABIMath
    {
        public static Vector3 Sub(Vector3 a, Vector3 b) =>
            new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static float Dot(Vector3 a, Vector3 b) =>
            a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static void GetAxes(Vector3 angles, out Vector3 x, out Vector3 y, out Vector3 z)
        {
            double pitch = angles.X * Math.PI / 180.0;
            double yaw   = angles.Y * Math.PI / 180.0;
            double roll  = angles.Z * Math.PI / 180.0;

            float sp = (float)Math.Sin(pitch);
            float cp = (float)Math.Cos(pitch);
            float sy = (float)Math.Sin(yaw);
            float cy = (float)Math.Cos(yaw);
            float sr = (float)Math.Sin(roll);
            float cr = (float)Math.Cos(roll);

            x = new Vector3(cp * cy, cp * sy, sp);
            y = new Vector3(sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, -sr * cp);
            z = new Vector3(-(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp);
        }

        public static bool WorldToScreen(Vector3 pos, out Vector2 screen, CameraInfo cam, float width, float height)
        {
            screen = Vector2.Zero;
            Vector3 delta = Sub(pos, cam.Position);
            Vector3 transformed = new(
                Dot(delta, cam.AxisY),
                Dot(delta, cam.AxisZ),
                Dot(delta, cam.AxisX)
            );

            if (transformed.Z < 1f) return false;

            float cx = width / 2f, cy = height / 2f;
            float fov = cam.Fov;
            screen.X = cx + transformed.X * cx / (float)Math.Tan(fov * Math.PI / 360f) / transformed.Z;
            screen.Y = cy - transformed.Y * cx / (float)Math.Tan(fov * Math.PI / 360f) / transformed.Z;

            return screen.X > 0 && screen.Y > 0 && screen.X <= width && screen.Y <= height;
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
