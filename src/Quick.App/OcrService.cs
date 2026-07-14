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
}
