using System.Runtime.InteropServices;
using Raylib_cs;

namespace MamboDMA
{
    public static class Misc
    {
        private const uint WDA_NONE = 0x0;                  // allow capture
        private const uint WDA_MONITOR = 0x1;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;   // blocks capture
        private const int  GWL_EXSTYLE = -20;
        private const int  GWL_STYLE   = -16;
        private const int  SWP_NOSIZE  = 0x0001;
        private const int  SWP_NOMOVE  = 0x0002;
        private const int  SWP_NOZORDER= 0x0004;
        private const int  SWP_FRAMECHANGED = 0x0020;

        private const long WS_EX_NOREDIRECTIONBITMAP = 0x00200000L;

        [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")] static extern bool   SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] static extern bool   GetWindowDisplayAffinity(nint hWnd, out uint dwAffinity);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        public static unsafe void AllowCapture()
        {
            try
            {
                nint hwnd = (nint)Raylib.GetWindowHandle(); // Raylib_cs exposes this on Windows
                if (hwnd != IntPtr.Zero)
                    SetWindowDisplayAffinity(hwnd, WDA_NONE);
            }
            catch { /* ignore */ }
        }  

        public static unsafe void ApplyAll()
        {
            nint hwnd = (nint)Raylib.GetWindowHandle();
            if (hwnd == IntPtr.Zero) return;

            try
            {
                // 1) Ensure display affinity allows capture
                SetWindowDisplayAffinity(hwnd, WDA_NONE);

                // 2) Clear NOREDIRECTIONBITMAP so DWM can redirect (otherwise RDP/AnyDesk/OBS may see black)
                var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
                long exVal = ex.ToInt64();
                if ((exVal & WS_EX_NOREDIRECTIONBITMAP) != 0)
                {
                    exVal &= ~WS_EX_NOREDIRECTIONBITMAP;
                    SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)exVal);
                    // Tell the window manager styles changed
                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
                }
            }
            catch { /* ignore */ }
        }

        public static unsafe bool IsExcludedFromCapture()
        {
            nint hwnd = (nint)Raylib.GetWindowHandle();
            if (hwnd == IntPtr.Zero) return false;
            return GetWindowDisplayAffinity(hwnd, out uint a) && a == WDA_EXCLUDEFROMCAPTURE;
        }         
    }
}