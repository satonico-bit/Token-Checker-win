namespace TokenChecker.Utilities;

public enum PollingInterval
{
    Sec30  = 30,
    Min1   = 60,
    Min2   = 120,
    Min3   = 180,
    Min5   = 300,
    Min10  = 600,
}

public static class PollingIntervalExtensions
{
    public static TimeSpan ToTimeSpan(this PollingInterval p)
        => TimeSpan.FromSeconds((int)p);

    public static string ToLabel(this PollingInterval p) => p switch
    {
        PollingInterval.Sec30 => "30秒",
        PollingInterval.Min1  => "1分",
        PollingInterval.Min2  => "2分",
        PollingInterval.Min3  => "3分",
        PollingInterval.Min5  => "5分",
        PollingInterval.Min10 => "10分",
        _                     => p.ToString(),
    };

    public static readonly PollingInterval Default = PollingInterval.Min5;

    public static readonly PollingInterval[] All =
    [
        PollingInterval.Min2,
        PollingInterval.Min3,
        PollingInterval.Min5,
        PollingInterval.Min10,
    ];
}
