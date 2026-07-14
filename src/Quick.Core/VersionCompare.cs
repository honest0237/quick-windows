namespace Quick.Core;

/// <summary>시맨틱 버전 비교 (pre-release 태그도 안전). Swift UpdateService.isNewer 이식.</summary>
public static class VersionCompare
{
    public static bool IsNewer(string a, string b)
    {
        var pa = Components(a);
        var pb = Components(b);
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            int x = i < pa.Length ? pa[i] : 0;
            int y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    private static int[] Components(string s) =>
        s.Split('.').Select(part =>
        {
            var digits = new string(part.TakeWhile(char.IsDigit).ToArray());  // "0-beta" → "0"
            return int.TryParse(digits, out var v) ? v : 0;
        }).ToArray();
}
