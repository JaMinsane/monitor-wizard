using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using H.NotifyIcon;
using MonitorWizard.Pages;
using MonitorWizard.Services;
using System.Windows.Input;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MonitorWizard;

public sealed partial class MainWindow : Window
{
    private GlobalHotKey? _toggleHotKey;
    private UsbDeviceWatcher? _usbDeviceWatcher;
    private TaskbarIcon? _trayIcon;
    private ToggleMenuFlyoutItem? _autoSwitchMenuItem;
    private bool _isAutoSwitchEnabled;
    private bool _isExiting;

    public ICommand OpenWindowCommand { get; }
    public ICommand SwitchToPcCommand { get; }
    public ICommand SwitchToLaptopCommand { get; }
    public ICommand ToggleAutoSwitchCommand { get; }
    public ICommand ExitCommand { get; }

    public MainWindow()
    {
        OpenWindowCommand = new RelayCommand(ShowFromTray);
        SwitchToPcCommand = new RelayCommand(() => _ = AppServices.MonitorSwitch.SwitchToPcAsync());
        SwitchToLaptopCommand = new RelayCommand(() => _ = AppServices.MonitorSwitch.SwitchToLaptopAsync());
        ToggleAutoSwitchCommand = new RelayCommand(() => SetAutoSwitchEnabled(!_isAutoSwitchEnabled));
        ExitCommand = new RelayCommand(ExitFromTray);

        InitializeComponent();
        InitializeTrayIcon();

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

        AppWindow.Closing += AppWindow_Closing;
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

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExiting)
        {
            return;
        }

        args.Cancel = true;
        this.Hide();
        AppServices.MonitorSwitch.LogEntries.Insert(
            0,
            $"{DateTime.Now:HH:mm:ss} Window minimized to tray.");
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        AppWindow.Closing -= AppWindow_Closing;
        StopAutoSwitchWatcher();

        if (_trayIcon is not null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _toggleHotKey?.Dispose();
        _toggleHotKey = null;
    }

    private void SetAutoSwitchEnabled(bool isEnabled)
    {
        if (isEnabled)
        {
            StartAutoSwitchWatcher();
        }
        else
        {
            StopAutoSwitchWatcher();
            _isAutoSwitchEnabled = false;
            AppServices.MonitorSwitch.LogEntries.Insert(
                0,
                $"{DateTime.Now:HH:mm:ss} USB auto switch disabled.");
        }

        if (_autoSwitchMenuItem is not null)
        {
            _autoSwitchMenuItem.IsChecked = _isAutoSwitchEnabled;
        }
    }

    private void StartAutoSwitchWatcher()
    {
        if (_usbDeviceWatcher is not null)
        {
            _isAutoSwitchEnabled = true;
            return;
        }

        var watcher = new UsbDeviceWatcher();
        watcher.DeviceDetected += UsbDeviceWatcher_DeviceDetected;

        try
        {
            watcher.Start();
            _usbDeviceWatcher = watcher;
            _isAutoSwitchEnabled = true;
            AppServices.MonitorSwitch.LogEntries.Insert(
                0,
                $"{DateTime.Now:HH:mm:ss} USB auto switch watcher started.");
        }
        catch (Exception ex)
        {
            watcher.DeviceDetected -= UsbDeviceWatcher_DeviceDetected;
            watcher.Dispose();
            _isAutoSwitchEnabled = false;
            AppServices.MonitorSwitch.LogEntries.Insert(
                0,
                $"{DateTime.Now:HH:mm:ss} [ERROR] USB auto switch watcher could not start: {ex.Message}");
        }
    }

    private void StopAutoSwitchWatcher()
    {
        if (_usbDeviceWatcher is null)
        {
            return;
        }

        _usbDeviceWatcher.DeviceDetected -= UsbDeviceWatcher_DeviceDetected;
        _usbDeviceWatcher.Dispose();
        _usbDeviceWatcher = null;
    }

    private void ShowFromTray()
    {
        this.Show();
    }

    private void InitializeTrayIcon()
    {
        _autoSwitchMenuItem = new ToggleMenuFlyoutItem
        {
            Text = "Auto switch enabled",
            IsChecked = _isAutoSwitchEnabled,
            Command = ToggleAutoSwitchCommand
        };

        var contextMenu = new MenuFlyout();
        contextMenu.Items.Add(CreateMenuItem("Open Monitor Wizard", OpenWindowCommand));
        contextMenu.Items.Add(new MenuFlyoutSeparator());
        contextMenu.Items.Add(CreateMenuItem("Switch to PC", SwitchToPcCommand));
        contextMenu.Items.Add(CreateMenuItem("Switch to Laptop", SwitchToLaptopCommand));
        contextMenu.Items.Add(new MenuFlyoutSeparator());
        contextMenu.Items.Add(_autoSwitchMenuItem);
        contextMenu.Items.Add(new MenuFlyoutSeparator());
        contextMenu.Items.Add(CreateMenuItem("Exit", ExitCommand));

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Monitor Wizard",
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/AppIcon.ico")),
            ContextFlyout = contextMenu,
            DoubleClickCommand = OpenWindowCommand
        };
        _trayIcon.ForceCreate(enablesEfficiencyMode: false);
        if (!_trayIcon.IsCreated)
        {
            AppServices.MonitorSwitch.LogEntries.Insert(
                0,
                $"{DateTime.Now:HH:mm:ss} [ERROR] Tray icon could not be registered.");
        }
    }

    private static MenuFlyoutItem CreateMenuItem(string text, ICommand command)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Command = command
        };
        return item;
    }

    private void ExitFromTray()
    {
        _isExiting = true;
        Close();
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
