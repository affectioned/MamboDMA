using Raylib_cs;
using System.Numerics;
using ImGuiNET;
using Svg.Skia;
using SkiaSharp;
using System.Text;

namespace MamboDMA.Gui
{
    public static class Assets
    {
        public static Texture2D Logo;

        public static void Load()
        {
            if (Logo.Id != 0) return; // already loaded
            var img = Raylib.LoadImage("Assets/Img/Logo.png");
            Logo = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
        }

        public static void Unload()
        {
            if (Logo.Id != 0)
            {
                Raylib.UnloadTexture(Logo);
                Logo = new Texture2D();
            }
        }
    }
    public static class SvgLoader
    {
        /// <summary>
        /// Load an SVG file and rasterize it into a Raylib Texture2D.
        /// </summary>
        /// <param name="path">Path to the .svg file</param>
        /// <param name="targetHeight">Desired height in pixels (e.g. top bar height)</param>
        public static Texture2D LoadSvg(string path, int targetHeight)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"SVG file not found: {path}");

            // Parse SVG
            var svg = new SKSvg();
            svg.Load(path);

            if (svg.Picture == null)
                throw new InvalidOperationException("Failed to load SVG: " + path);

            // Scale width based on aspect ratio
            float aspect = svg.Picture.CullRect.Width / svg.Picture.CullRect.Height;
            int width = (int)(targetHeight * aspect);
            int height = targetHeight;

            // Render into Skia surface
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            // Build scale matrix so the SVG fits our target size
            float scaleX = width / svg.Picture.CullRect.Width;
            float scaleY = height / svg.Picture.CullRect.Height;
            var scale = SKMatrix.CreateScale(scaleX, scaleY);
            
            canvas.DrawPicture(svg.Picture, ref scale); // ✅ correct overload
            canvas.Flush();

            // Export to PNG bytes
            using var img = surface.Snapshot();
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);

            // Load into Raylib
            using var ms = new MemoryStream();
            data.SaveTo(ms);
            ms.Seek(0, SeekOrigin.Begin);

            // Raylib needs Image.LoadFromMemory → Texture2D
            byte[] bytes = ms.ToArray();
            unsafe
            {
                fixed (byte* ptr = bytes)
                {
                    // convert ".png" to sbyte*
                    var fileType = Encoding.ASCII.GetBytes(".png\0");
                    fixed (byte* ft = fileType)
                    {
                        Image imgRay = Raylib.LoadImageFromMemory((sbyte*)ft, ptr, bytes.Length);
                        Texture2D tex = Raylib.LoadTextureFromImage(imgRay);
                        Raylib.UnloadImage(imgRay); // free CPU-side image
                        return tex;
                    }
                }
            }
        }
    }    
}
