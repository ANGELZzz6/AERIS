using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace IslasEscritorio.Core;

/// <summary>
/// Servicio de reconocimiento óptico de caracteres (OCR) nativo de Windows 10/11 sin dependencias externas.
/// </summary>
public static class OcrService
{
    /// <summary>
    /// Extrae el texto plano contenido en un System.Drawing.Bitmap.
    /// </summary>
    public static async Task<string> ExtractTextFromBitmapAsync(Bitmap bitmap)
    {
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return string.Empty;
        }

        try
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;

            var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));

            if (engine is null)
            {
                return string.Empty;
            }

            var ocrResult = await engine.RecognizeAsync(softwareBitmap);
            return ocrResult?.Text?.Trim() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
