using System.Numerics;

namespace MamboDMA.Games.CS2
{
    public static class CS2Math
    {
        public static bool WorldToScreen(Vector3 pos, Matrix4x4 m, float screenW, float screenH, out Vector2 screenPos)
        {
            screenPos = Vector2.Zero;

            float view = m.M41 * pos.X +
                         m.M42 * pos.Y +
                         m.M43 * pos.Z +
                         m.M44;

            if (view <= 0.01f)
                return false;

            float clipX = m.M11 * pos.X +
                          m.M12 * pos.Y +
                          m.M13 * pos.Z +
                          m.M14;

            float clipY = m.M21 * pos.X +
                          m.M22 * pos.Y +
                          m.M23 * pos.Z +
                          m.M24;

            float halfW = screenW * 0.5f;
            float halfH = screenH * 0.5f;

            screenPos.X = halfW + (clipX / view) * halfW;
            screenPos.Y = halfH - (clipY / view) * halfH;

            return true;
        }
    }
}
