using System.Collections.ObjectModel;

namespace MonitorWizard.Services;

public sealed class MonitorSwitchController
{
    private readonly MonitorInputSwitcher _monitorInputSwitcher = new();
    private MonitorInputSource? _lastRequestedInputSource;

    public event Action<bool>? SwitchingChanged;

    public ObservableCollection<string> LogEntries { get; } = [];

    public bool IsSwitching { get; private set; }

    public MonitorSwitchController()
    {
        LogEntries.Add($"{DateTime.Now:HH:mm:ss} Ready.");
    }

    public Task SwitchToPcAsync()
    {
        return SwitchToAsync(MonitorInputSource.DisplayPort, "PC / DisplayPort", "Manual button");
    }

    public Task SwitchToLaptopAsync()
    {
        return SwitchToAsync(MonitorInputSource.Hdmi, "Laptop / HDMI", "Manual button");
    }

    public Task TogglePcLaptopAsync()
    {
        var nextInputSource = _lastRequestedInputSource == MonitorInputSource.DisplayPort
            ? MonitorInputSource.Hdmi
            : MonitorInputSource.DisplayPort;

        var label = nextInputSource == MonitorInputSource.DisplayPort
            ? "PC / DisplayPort"
            : "Laptop / HDMI";

        return SwitchToAsync(nextInputSource, label, "Ctrl+Alt+F12");
    }

    private async Task SwitchToAsync(MonitorInputSource inputSource, string label, string trigger)
    {
        if (IsSwitching)
        {
            LogEntries.Insert(0, $"{DateTime.Now:HH:mm:ss} Ignored {trigger}; a switch is already running.");
            return;
        }

        IsSwitching = true;
        SwitchingChanged?.Invoke(true);
        LogEntries.Insert(0, $"{DateTime.Now:HH:mm:ss} Switching monitors to {label} ({trigger})...");

        try
        {
            var results = await Task.Run(() => _monitorInputSwitcher.SwitchAll(inputSource));
            _lastRequestedInputSource = inputSource;

            foreach (var result in results.Reverse())
            {
                var status = result.Success ? "OK" : "ERROR";
                LogEntries.Insert(
                    0,
                    $"{DateTime.Now:HH:mm:ss} [{status}] Monitor {result.MonitorNumber}: {result.Description} - {result.Message}");
            }
        }
        catch (Exception ex)
        {
            LogEntries.Insert(0, $"{DateTime.Now:HH:mm:ss} [ERROR] {ex.Message}");
        }
        finally
        {
            IsSwitching = false;
            SwitchingChanged?.Invoke(false);
        }
    }
}
