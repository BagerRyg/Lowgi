namespace LowgiPrimitives;

public static class RuntimeSettings
{
    public static int BatteryPollingIntervalSeconds { get; private set; } = 300;

    public static void SetBatteryPollingIntervalMinutes(int minutes)
    {
        BatteryPollingIntervalSeconds = Math.Max(1, minutes) * 60;
    }
}
