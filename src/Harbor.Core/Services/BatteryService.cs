using System.Diagnostics;
using Windows.Win32;

namespace Harbor.Core.Services;

public enum BatteryIconState
{
    Critical,
    Low,
    Medium,
    High,
    Full,
    Charging,
}

public enum PowerSource
{
    Battery,
    AC,
    Unknown,
}

public sealed class BatteryChangedEventArgs : EventArgs
{
    public int ChargePercent { get; init; }
    public bool IsCharging { get; init; }
    public PowerSource PowerSource { get; init; }
    public BatteryIconState IconState { get; init; }
    public int? EstimatedMinutesRemaining { get; init; }
    public bool HasBattery { get; init; }
}

public sealed class BatteryService : IDisposable
{
    private readonly object _lock = new();
    private Timer? _pollTimer;
    private bool _disposed;

    public int ChargePercent { get; private set; }
    public bool IsCharging { get; private set; }
    public PowerSource PowerSource { get; private set; }
    public BatteryIconState IconState { get; private set; }
    public int? EstimatedMinutesRemaining { get; private set; }
    public bool HasBattery { get; private set; } = true;

    public event EventHandler<BatteryChangedEventArgs>? BatteryChanged;

    public BatteryService()
    {
        UpdateBatteryState();

        // Poll every 60 seconds (battery state changes slowly)
        _pollTimer = new Timer(_ => UpdateBatteryState(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        Trace.WriteLine("[Harbor] BatteryService: Initialized.");
    }

    private void UpdateBatteryState()
    {
        if (_disposed) return;

        try
        {
            PInvoke.GetSystemPowerStatus(out var status);

            // BatteryFlag == 128 means no system battery
            var hasBattery = (status.BatteryFlag & 128) == 0 && status.BatteryFlag != 255;
            var chargePercent = status.BatteryLifePercent <= 100
                ? (int)status.BatteryLifePercent
                : 0;
            var isCharging = (status.BatteryFlag & 8) != 0;
            var powerSource = status.ACLineStatus switch
            {
                0 => PowerSource.Battery,
                1 => PowerSource.AC,
                _ => PowerSource.Unknown,
            };

            int? minutesRemaining = null;
            if (!isCharging && status.BatteryLifeTime != 0xFFFFFFFF && status.BatteryLifeTime > 0)
            {
                minutesRemaining = (int)(status.BatteryLifeTime / 60);
            }

            var iconState = ComputeIconState(chargePercent, isCharging);

            bool stateChanged;
            lock (_lock)
            {
                stateChanged = HasBattery != hasBattery ||
                               ChargePercent != chargePercent ||
                               IsCharging != isCharging ||
                               PowerSource != powerSource ||
                               EstimatedMinutesRemaining != minutesRemaining;

                HasBattery = hasBattery;
                ChargePercent = chargePercent;
                IsCharging = isCharging;
                PowerSource = powerSource;
                EstimatedMinutesRemaining = minutesRemaining;
                IconState = iconState;
            }

            if (stateChanged)
            {
                BatteryChanged?.Invoke(this, new BatteryChangedEventArgs
                {
                    ChargePercent = chargePercent,
                    IsCharging = isCharging,
                    PowerSource = powerSource,
                    IconState = iconState,
                    EstimatedMinutesRemaining = minutesRemaining,
                    HasBattery = hasBattery,
                });
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BatteryService: Failed to query power status: {ex.Message}");
        }
    }

    private static BatteryIconState ComputeIconState(int percent, bool isCharging)
    {
        if (isCharging) return BatteryIconState.Charging;
        if (percent <= 10) return BatteryIconState.Critical;
        if (percent <= 20) return BatteryIconState.Low;
        if (percent <= 50) return BatteryIconState.Medium;
        if (percent <= 79) return BatteryIconState.High;
        return BatteryIconState.Full;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pollTimer?.Dispose();
        _pollTimer = null;

        Trace.WriteLine("[Harbor] BatteryService: Disposed.");
    }
}
