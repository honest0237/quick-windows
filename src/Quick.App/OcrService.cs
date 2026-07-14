using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Quick.App;

/// <summary>Windows 온디바이스 OCR (Windows.Media.Ocr). macOS Vision의 Windows 대응 — 무료·오프라인·다국어.</summary>
public static class OcrService
{
    public static async Task<string> RecognizeAsync(string imagePath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync();

            // 사용자 언어(한국어 등) 우선, 없으면 영어
            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                         ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
            if (engine is null) return "";

            var result = await engine.RecognizeAsync(bitmap);
            return result?.Text ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>메모리 비트맵 OCR(편집기 '텍스트 복사'용) — 임시 파일로 저장해 기존 경로 재사용.</summary>
    public static async Task<string> RecognizeAsync(System.Drawing.Bitmap bmp)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"quick-ocr-{Guid.NewGuid():N}.png");
        try
        {
            bmp.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
            return await RecognizeAsync(tmp);
        }
        catch { return ""; }
        finally { try { File.Delete(tmp); } catch { /* 무시 */ } }
    }
}
