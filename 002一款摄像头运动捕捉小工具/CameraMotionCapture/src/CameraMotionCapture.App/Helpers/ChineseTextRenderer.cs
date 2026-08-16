using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace CameraMotionCapture.App.Helpers;

/// <summary>
/// 在 OpenCV Mat 上绘制中文文本（使用 GDI+ 渲染，支持宋体等中文字体）
/// 纯 ASCII 文本自动回退到 OpenCV PutText 以提升性能
/// </summary>
public static class ChineseTextRenderer
{
    private static readonly FontFamily _simSun;
    private static readonly object _lock = new();

    static ChineseTextRenderer()
    {
        try
        {
            _simSun = new FontFamily("SimSun");
        }
        catch
        {
            _simSun = FontFamily.GenericSansSerif;
        }
    }

    /// <summary>
    /// 在帧上绘制文本（支持中文）
    /// </summary>
    public static void DrawText(Mat frame, string text, int x, int y, Scalar color,
        string fontFamily = "SimSun", float fontSize = 14, int thickness = 2)
    {
        if (string.IsNullOrEmpty(text)) return;

        // 纯 ASCII 用 OpenCV 原生渲染（高性能）
        if (text.All(c => c < 128))
        {
            Cv2.PutText(frame, text, new OpenCvSharp.Point(x, y), HersheyFonts.HersheySimplex, fontSize / 20.0, color, thickness);
            return;
        }

        // 中文用 GDI+ 渲染
        try
        {
            Serilog.Log.Debug("ChineseTextRenderer: 渲染中文文本 '{Text}' 字体={Font} 大小={Size}",
                text.Length > 20 ? text[..20] + "..." : text, fontFamily, fontSize);
            lock (_lock)
            {
                using var font = new Font(fontFamily, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
                using var tempBmp = new Bitmap(1, 1);
                using var tempG = Graphics.FromImage(tempBmp);
                var size = tempG.MeasureString(text, font);

                int w = (int)size.Width + 6;
                int h = (int)size.Height + 6;
                if (w <= 0 || h <= 0) return;

                // 绘制文本到 Bitmap
                using var textBmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                using var g = Graphics.FromImage(textBmp);
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.Clear(Color.Black);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var brush = new SolidBrush(Color.FromArgb(
                    (byte)color.Val2, (byte)color.Val1, (byte)color.Val0)); // OpenCV BGR → GDI+ RGB
                g.DrawString(text, font, brush, 2, 2);

                // Bitmap -> Mat (BGR) 手动转换
                using var textMat = BitmapToMat(textBmp);

                // 创建掩码（非黑色像素）
                using var grayMat = new Mat();
                Cv2.CvtColor(textMat, grayMat, ColorConversionCodes.BGR2GRAY);
                using var mask = new Mat();
                Cv2.Threshold(grayMat, mask, 10, 255, ThresholdTypes.Binary);

                // 计算 ROI
                var roi = new Rect(x, y,
                    Math.Min(textMat.Width, frame.Width - x),
                    Math.Min(textMat.Height, frame.Height - y));
                if (roi.Width <= 0 || roi.Height <= 0) return;

                // 将文本复制到帧上（掩码区域）
                // textMat 的 ROI 应从 (0,0) 开始，frame 的 ROI 从 (x,y) 开始
                var textRoi = new Rect(0, 0, roi.Width, roi.Height);
                textMat[textRoi].CopyTo(frame[roi], mask[textRoi]);
            }
        }
        catch (Exception ex)
        {
            // 回退到 OpenCV 渲染
            Serilog.Log.Warning(ex, "中文文本渲染回退到 OpenCV");
            Cv2.PutText(frame, text, new OpenCvSharp.Point(x, y), HersheyFonts.HersheySimplex, 0.6, color, thickness);
        }
    }

    /// <summary>将 System.Drawing.Bitmap 转换为 OpenCV Mat (BGR格式)</summary>
    private static Mat BitmapToMat(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            int width = bmp.Width;
            int height = bmp.Height;
            int stride = Math.Abs(data.Stride);
            int rowBytes = width * 3; // 24bpp

            var mat = new Mat(height, width, MatType.CV_8UC3);
            int matStep = (int)mat.Step();

            byte[] rowData = new byte[rowBytes];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * stride), rowData, 0, rowBytes);
                Marshal.Copy(rowData, 0, IntPtr.Add(mat.Data, y * matStep), rowBytes);
            }

            return mat;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}