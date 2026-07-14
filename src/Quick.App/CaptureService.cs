using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Quick.App;

/// <summary>화면 캡처 — Windows엔 '스샷→폴더' 흐름이 약해서 직접 캡처를 제공.
/// macOS는 OS 캡처를 감시만 하지만, Windows판은 캡처 자체를 담당.</summary>
public static class CaptureService
{
    public static Rectangle VirtualScreen => SystemInformation.VirtualScreen;

    /// <summary>지정 화면 영역(가상 화면 좌표)의 픽셀을 잡아 비트맵으로.</summary>
    public static Bitmap CaptureRegion(Rectangle region)
    {
        var bmp = new Bitmap(Math.Max(1, region.Width), Math.Max(1, region.Height), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(region.Location, Point.Empty, region.Size);
        return bmp;
    }

    public static Bitmap CaptureFullScreen() => CaptureRegion(VirtualScreen);

    /// <summary>스크린샷 폴더에 저장. 파일명에 "Screenshot" 포함 → 감지 패턴과 일치.</summary>
    public static string Save(Bitmap bmp, string directory, string format = "png")
    {
        Directory.CreateDirectory(directory);
        var jpeg = format.Equals("jpeg", StringComparison.OrdinalIgnoreCase);
        var ext = jpeg ? "jpg" : "png";
        var name = $"Screenshot {DateTime.Now:yyyy-MM-dd HH-mm-ss}.{ext}";
        var path = Path.Combine(directory, name);
        bmp.Save(path, jpeg ? ImageFormat.Jpeg : ImageFormat.Png);
        return path;
    }
}
