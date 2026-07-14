using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Text2Cad.Addin.Infrastructure
{
    internal static class TaskPaneIconSet
    {
        private static readonly int[] Sizes = { 20, 32, 40, 64, 96, 128 };

        public static string[] EnsureCreated()
        {
            string directory = GetIconDirectory();
            Directory.CreateDirectory(directory);

            var paths = new string[Sizes.Length];
            for (int index = 0; index < Sizes.Length; index++)
            {
                int size = Sizes[index];
                string path = Path.Combine(directory, $"text2cad-{size}.png");
                paths[index] = path;

                if (!File.Exists(path))
                {
                    CreateIcon(path, size);
                }
            }

            return paths;
        }

        private static string GetIconDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.GetTempPath();
            }

            return Path.Combine(localAppData, "Text2CAD", "icons", "v1");
        }

        private static void CreateIcon(string path, int size)
        {
            using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.FromArgb(192, 192, 192));

            float margin = Math.Max(2f, size * 0.1f);
            using var background = new SolidBrush(Color.FromArgb(24, 105, 219));
            graphics.FillEllipse(background, margin, margin, size - (margin * 2), size - (margin * 2));

            using var font = new Font(
                "Segoe UI",
                Math.Max(9f, size * 0.5f),
                FontStyle.Bold,
                GraphicsUnit.Pixel);
            using var foreground = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString("T", font, foreground, new RectangleF(0, 0, size, size), format);

            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
