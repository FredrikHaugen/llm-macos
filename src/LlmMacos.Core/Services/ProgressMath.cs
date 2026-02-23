namespace LlmMacos.Core.Services;

public static class ProgressMath
{
    public static double? CalculatePercent(long downloaded, long? total)
    {
        if (downloaded < 0 || total is null || total <= 0)
        {
            return null;
        }

        return Math.Clamp((double)downloaded / total.Value * 100d, 0d, 100d);
    }

    public static double? CalculateBytesPerSecond(long downloadedDelta, TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds <= 0 || downloadedDelta < 0)
        {
            return null;
        }

        return downloadedDelta / elapsed.TotalSeconds;
    }

    public static TimeSpan? CalculateEta(long downloaded, long? total, double? bytesPerSecond)
    {
        if (total is null || bytesPerSecond is null || bytesPerSecond <= 0)
        {
            return null;
        }

        var remaining = total.Value - downloaded;
        if (remaining <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(remaining / bytesPerSecond.Value);
    }
}
