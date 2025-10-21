using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace MamboDMA.Games.ABI
{
    //����������������������������������������������������������������������������������������������������
    // Math for W2S
    //����������������������������������������������������������������������������������������������������
    public static class ABIMath
    {
        private static Matrix4x4 RotationMatrix(Vector3 rotDeg)
        {
            float radPitch = rotDeg.X * (MathF.PI / 180f);
            float radYaw   = rotDeg.Y * (MathF.PI / 180f);
            float radRoll  = rotDeg.Z * (MathF.PI / 180f);

            float SP = MathF.Sin(radPitch), CP = MathF.Cos(radPitch);
            float SY = MathF.Sin(radYaw),   CY = MathF.Cos(radYaw);
            float SR = MathF.Sin(radRoll),  CR = MathF.Cos(radRoll);

            return new Matrix4x4
            {
                M11 = CP * CY,
                M12 = CP * SY,
                M13 = SP,

                M21 = SR * SP * CY - CR * SY,
                M22 = SR * SP * SY + CR * CY,
                M23 = -SR * CP,

                M31 = -(CR * SP * CY + SR * SY),
                M32 = CY * SR - CR * SP * SY,
                M33 = CR * CP
            };
        }

        public static bool WorldToScreen(Vector3 pos, Players.FMinimalViewInfo cam, float width, float height, out Vector2 s)
        {
            var rot = new Vector3(cam.Rotation.Pitch, cam.Rotation.Yaw, cam.Rotation.Roll);
            Matrix4x4 m = RotationMatrix(rot);

            Vector3 axisX = new(m.M11, m.M12, m.M13);
            Vector3 axisY = new(m.M21, m.M22, m.M23);
            Vector3 axisZ = new(m.M31, m.M32, m.M33);

            Vector3 delta = pos - cam.Location;

            Vector3 t = new(
                Vector3.Dot(delta, axisY),
                Vector3.Dot(delta, axisZ),
                Vector3.Dot(delta, axisX)
            );

            if (t.Z < 1f) { s = default; return false; }

            float cx = width * 0.5f, cy = height * 0.5f;
            float fovRad = cam.Fov * (MathF.PI / 180f);
            float focal = cx / MathF.Tan(fovRad * 0.5f);

            s.X = cx + t.X * focal / t.Z;
            s.Y = cy - t.Y * focal / t.Z;

            return (s.X >= 0 && s.Y >= 0 && s.X <= width && s.Y <= height);
        }
    }

    //����������������������������������������������������������������������������������������������������
    // Math/Structs for bone transforms
    //����������������������������������������������������������������������������������������������������
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FQuat { public float X, Y, Z, W; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FTransform
    {
        public FQuat   Rotation;     // 0x00 (16)
        public Vector3 Translation;  // 0x10 (12)
        private float  _pad0;        // 0x1C (4)
        public Vector3 Scale3D;      // 0x20 (12)
        private float  _pad1;        // 0x2C (4)
    }
}
