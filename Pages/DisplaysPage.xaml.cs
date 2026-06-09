using Microsoft.UI.Xaml.Controls;
using MonitorWizard.Services;
using System.Collections.ObjectModel;

namespace MonitorWizard.Pages;

public sealed partial class DisplaysPage : Page
{
    private readonly MonitorInputSwitcher _monitorInputSwitcher = new();

    public ObservableCollection<DisplaySettingsViewModel> Displays { get; } = [];

    public DisplaysPage()
    {
        InitializeComponent();
        LoadDisplays();
    }

    private void RefreshDisplaysButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var selectedMonitorNumber = (DisplaysTabView.SelectedItem as DisplaySettingsViewModel)?.MonitorNumber;

        LoadDisplays();

        if (selectedMonitorNumber.HasValue)
        {
            var selectedDisplay = Displays.FirstOrDefault(display => display.MonitorNumber == selectedMonitorNumber.Value);

            if (selectedDisplay is not null)
            {
                DisplaysTabView.SelectedItem = selectedDisplay;
            }
        }

        if (Displays.Count > 0)
        {
            ApplyStatus.Severity = InfoBarSeverity.Informational;
            ApplyStatus.Title = "Display status refreshed";
            ApplyStatus.Message = "Monitor names, active inputs, and supported picture controls were read again.";
            ApplyStatus.IsOpen = true;
        }
    }

    private void LoadDisplays()
    {
        Displays.Clear();

        var displays = _monitorInputSwitcher.GetDisplays();
        var duplicateNames = displays
            .GroupBy(display => display.Description)
            .ToDictionary(group => group.Key, group => group.Count());
        var seenNames = new Dictionary<string, int>();

        foreach (var display in displays)
        {
            seenNames.TryGetValue(display.Description, out var seenCount);
            seenNames[display.Description] = ++seenCount;

            var tabHeader = duplicateNames[display.Description] > 1
                ? $"{display.Description} ({seenCount})"
                : display.Description;

            if (display.IsPrimary)
            {
                tabHeader += " - Primary";
            }

            Displays.Add(new DisplaySettingsViewModel(display, tabHeader));
        }

        if (Displays.Count == 0)
        {
            ApplyStatus.Severity = InfoBarSeverity.Warning;
            ApplyStatus.Title = "No displays detected";
            ApplyStatus.Message = "The app could not enumerate physical monitors.";
            ApplyStatus.IsOpen = true;
        }
    }

    private async void ApplyButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.FrameworkElement { Tag: DisplaySettingsViewModel displaySettings })
        {
            return;
        }

        ApplyStatus.IsOpen = false;

        var settings = new MonitorDisplaySettings(
            displaySettings.MonitorNumber,
            displaySettings.BrightnessToApply,
            displaySettings.ContrastToApply,
            displaySettings.SharpnessToApply,
            displaySettings.ActiveInputToApply);

        if (!displaySettings.HasPendingChanges)
        {
            ApplyStatus.Severity = InfoBarSeverity.Informational;
            ApplyStatus.Title = "No changes";
            ApplyStatus.Message = $"{displaySettings.TabHeader}: Nothing changed since the last refresh or apply.";
            ApplyStatus.IsOpen = true;
            return;
        }

        var result = await Task.Run(() => _monitorInputSwitcher.ApplySettings(settings));

        if (result.Success)
        {
            displaySettings.AcceptAppliedValues();
        }

        ApplyStatus.Severity = result.Success
            ? InfoBarSeverity.Success
            : InfoBarSeverity.Error;
        ApplyStatus.Title = result.Success
            ? "Settings applied"
            : "Settings not applied";
        ApplyStatus.Message = $"{result.Description}: {result.Message}";
        ApplyStatus.IsOpen = true;
    }
}

public sealed class DisplaySettingsViewModel
{
    private uint? _appliedBrightness;
    private uint? _appliedContrast;
    private uint? _appliedSharpness;
    private int? _appliedSelectedInputIndex;

    public DisplaySettingsViewModel(MonitorDisplayInfo display, string tabHeader)
    {
        MonitorNumber = display.MonitorNumber;
        TabHeader = tabHeader;
        Brightness = display.Brightness ?? 50;
        Contrast = display.Contrast ?? 50;
        Sharpness = display.Sharpness ?? 50;
        SupportsBrightness = display.Brightness.HasValue;
        SupportsContrast = display.Contrast.HasValue;
        SupportsSharpness = display.Sharpness.HasValue;
        SupportsActiveInput = display.ActiveInput.HasValue;
        SelectedInputIndex = display.ActiveInput == MonitorInputSource.Hdmi ? 1 : 0;

        AcceptAppliedValues();
    }

    public int MonitorNumber { get; }

    public string TabHeader { get; }

    public double Brightness { get; set; }

    public double Contrast { get; set; }

    public double Sharpness { get; set; }

    public bool SupportsBrightness { get; }

    public bool SupportsContrast { get; }

    public bool SupportsSharpness { get; }

    public bool SupportsActiveInput { get; }

    public int SelectedInputIndex { get; set; }

    public MonitorInputSource ActiveInput => SelectedInputIndex == 1
        ? MonitorInputSource.Hdmi
        : MonitorInputSource.DisplayPort;

    public uint? BrightnessToApply => GetPendingValue(SupportsBrightness, Brightness, _appliedBrightness);

    public uint? ContrastToApply => GetPendingValue(SupportsContrast, Contrast, _appliedContrast);

    public uint? SharpnessToApply => GetPendingValue(SupportsSharpness, Sharpness, _appliedSharpness);

    public MonitorInputSource? ActiveInputToApply =>
        SupportsActiveInput && SelectedInputIndex != _appliedSelectedInputIndex
            ? ActiveInput
            : null;

    public bool HasPendingChanges =>
        BrightnessToApply.HasValue ||
        ContrastToApply.HasValue ||
        SharpnessToApply.HasValue ||
        ActiveInputToApply.HasValue;

    public void AcceptAppliedValues()
    {
        _appliedBrightness = SupportsBrightness ? (uint)Math.Round(Brightness) : null;
        _appliedContrast = SupportsContrast ? (uint)Math.Round(Contrast) : null;
        _appliedSharpness = SupportsSharpness ? (uint)Math.Round(Sharpness) : null;
        _appliedSelectedInputIndex = SupportsActiveInput ? SelectedInputIndex : null;
    }

    private static uint? GetPendingValue(bool isSupported, double currentValue, uint? appliedValue)
    {
        if (!isSupported || !appliedValue.HasValue)
        {
            return null;
        }

        var roundedValue = (uint)Math.Round(currentValue);

        return roundedValue == appliedValue.Value
            ? null
            : roundedValue;
    }
}
