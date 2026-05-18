using System.Management;
using MonitorWizard.Configuration;

namespace MonitorWizard.Services;

public sealed class UsbDeviceWatcher : IDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(2.5);

    private readonly object _syncRoot = new();
    private ManagementEventWatcher? _watcher;
    private DateTimeOffset _lastTriggerTime = DateTimeOffset.MinValue;
    private bool _isStarted;

    public event Action<string>? DeviceDetected;

    public void Start()
    {
        if (_watcher is not null)
        {
            return;
        }

        var query = new WqlEventQuery(
            "__InstanceCreationEvent",
            TimeSpan.FromSeconds(1),
            "TargetInstance ISA 'Win32_PnPEntity'");

        _watcher = new ManagementEventWatcher(query);
        _watcher.EventArrived += Watcher_EventArrived;
        _watcher.Start();
        _isStarted = true;
    }

    public void Dispose()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EventArrived -= Watcher_EventArrived;
        if (_isStarted)
        {
            _watcher.Stop();
        }

        _watcher.Dispose();
        _watcher = null;
        _isStarted = false;
    }

    private void Watcher_EventArrived(object sender, EventArrivedEventArgs e)
    {
        if (e.NewEvent["TargetInstance"] is not ManagementBaseObject targetInstance)
        {
            return;
        }

        var deviceName = targetInstance["Name"]?.ToString() ?? string.Empty;
        if (!deviceName.Contains(AppConfiguration.UsbDeviceKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var now = DateTimeOffset.Now;
        lock (_syncRoot)
        {
            if (now - _lastTriggerTime <= DebounceInterval)
            {
                return;
            }

            _lastTriggerTime = now;
        }

        DeviceDetected?.Invoke(deviceName);
    }
}
