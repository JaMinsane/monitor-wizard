using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonitorWizard.Pages;
using MonitorWizard.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MonitorWizard;

public sealed partial class MainWindow : Window
{
    private GlobalHotKey? _toggleHotKey;
    private UsbDeviceWatcher? _usbDeviceWatcher;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        NavFrame.Navigate(typeof(HomePage));

        _toggleHotKey = new GlobalHotKey(this, TogglePcLaptopFromHotKey);
        if (!_toggleHotKey.Register())
        {
            AppServices.MonitorSwitch.LogEntries.Insert(
                0,
                $"{DateTime.Now:HH:mm:ss} [ERROR] Ctrl+Alt+F12 could not be registered.");
        }

        _usbDeviceWatcher = new UsbDeviceWatcher();
        _usbDeviceWatcher.DeviceDetected += UsbDeviceWatcher_DeviceDetected;
        try
        {
            _usbDeviceWatcher.Start();
            AppServices.MonitorSwitch.LogEntries.Insert(
                0,
                $"{DateTime.Now:HH:mm:ss} USB auto switch watcher started.");
        }
        catch (Exception ex)
        {
            AppServices.MonitorSwitch.LogEntries.Insert(
                0,
                $"{DateTime.Now:HH:mm:ss} [ERROR] USB auto switch watcher could not start: {ex.Message}");
        }

        Closed += MainWindow_Closed;
    }

    private async void TogglePcLaptopFromHotKey()
    {
        await AppServices.MonitorSwitch.TogglePcLaptopAsync();
    }

    private void UsbDeviceWatcher_DeviceDetected(string deviceName)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(200);
            await AppServices.MonitorSwitch.SwitchToPcFromUsbAsync(deviceName);
        });
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_usbDeviceWatcher is not null)
        {
            _usbDeviceWatcher.DeviceDetected -= UsbDeviceWatcher_DeviceDetected;
            _usbDeviceWatcher.Dispose();
            _usbDeviceWatcher = null;
        }

        _toggleHotKey?.Dispose();
        _toggleHotKey = null;
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "home":
                    NavFrame.Navigate(typeof(HomePage));
                    break;
                case "about":
                    NavFrame.Navigate(typeof(AboutPage));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
            }
        }
    }
}
