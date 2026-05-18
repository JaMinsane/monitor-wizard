using Microsoft.UI.Xaml.Controls;
using MonitorWizard.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MonitorWizard.Pages;

public sealed partial class HomePage : Page
{
    private readonly MonitorSwitchController _monitorSwitch = AppServices.MonitorSwitch;

    public object LogEntries => _monitorSwitch.LogEntries;

    public HomePage()
    {
        InitializeComponent();
        SetBusyState(_monitorSwitch.IsSwitching);
        Loaded += HomePage_Loaded;
        Unloaded += HomePage_Unloaded;
    }

    private async void SwitchToPcButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await _monitorSwitch.SwitchToPcAsync();
    }

    private async void SwitchToLaptopButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await _monitorSwitch.SwitchToLaptopAsync();
    }

    private void HomePage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _monitorSwitch.SwitchingChanged += SetBusyState;
        SetBusyState(_monitorSwitch.IsSwitching);
    }

    private void HomePage_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _monitorSwitch.SwitchingChanged -= SetBusyState;
    }

    private void SetBusyState(bool isBusy)
    {
        SwitchToPcButton.IsEnabled = !isBusy;
        SwitchToLaptopButton.IsEnabled = !isBusy;
        SwitchProgress.IsActive = isBusy;
        SwitchProgress.Visibility = isBusy
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }
}
