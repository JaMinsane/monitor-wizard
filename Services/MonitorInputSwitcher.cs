using System.Runtime.InteropServices;
using System.Management;

namespace MonitorWizard.Services;

public enum MonitorInputSource : uint
{
    Hdmi = 5,
    DisplayPort = 15
}

public sealed record MonitorDisplayInfo(
    int MonitorNumber,
    string Description,
    bool IsPrimary,
    uint? Brightness,
    uint? Contrast,
    uint? Sharpness,
    MonitorInputSource? ActiveInput);

public sealed record MonitorDisplaySettings(
    int MonitorNumber,
    uint? Brightness,
    uint? Contrast,
    uint? Sharpness,
    MonitorInputSource? ActiveInput);

public sealed record MonitorSwitchResult(int MonitorNumber, string Description, bool Success, string Message);

public sealed class MonitorInputSwitcher
{
    private const byte BrightnessVcpCode = 0x10;
    private const byte ContrastVcpCode = 0x12;
    private const byte InputSourceVcpCode = 0x60;
    private const byte SharpnessVcpCode = 0x87;

    public IReadOnlyList<MonitorDisplayInfo> GetDisplays()
    {
        var displayHandles = new List<IntPtr>();
        var displays = new List<MonitorDisplayInfo>();
        var identities = GetMonitorIdentitiesByDeviceId();

        if (!NativeMethods.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (monitorHandle, _, _, _) =>
                {
                    displayHandles.Add(monitorHandle);
                    return true;
                },
                IntPtr.Zero))
        {
            return displays;
        }

        var monitorNumber = 1;

        foreach (var displayHandle in displayHandles)
        {
            var monitorDeviceId = GetMonitorDeviceId(displayHandle);

            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(displayHandle, out var physicalMonitorCount) ||
                physicalMonitorCount == 0)
            {
                displays.Add(new MonitorDisplayInfo(
                    monitorNumber++,
                    GetDisplayName("Unknown monitor", monitorDeviceId, identities),
                    IsPrimaryMonitor(displayHandle),
                    null,
                    null,
                    null,
                    null));
                continue;
            }

            var physicalMonitors = new NativeMethods.PhysicalMonitor[physicalMonitorCount];
            var physicalMonitorsOpened = false;

            try
            {
                if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(displayHandle, physicalMonitorCount, physicalMonitors))
                {
                    displays.Add(new MonitorDisplayInfo(
                        monitorNumber++,
                        GetDisplayName("Unknown monitor", monitorDeviceId, identities),
                        IsPrimaryMonitor(displayHandle),
                        null,
                        null,
                        null,
                        null));
                    continue;
                }

                physicalMonitorsOpened = true;
                var isPrimary = IsPrimaryMonitor(displayHandle);

                foreach (var physicalMonitor in physicalMonitors)
                {
                    displays.Add(new MonitorDisplayInfo(
                        monitorNumber,
                        GetDisplayName(GetMonitorDescription(physicalMonitor), monitorDeviceId, identities),
                        isPrimary,
                        ReadVcpFeature(physicalMonitor.Handle, BrightnessVcpCode),
                        ReadVcpFeature(physicalMonitor.Handle, ContrastVcpCode),
                        ReadVcpFeature(physicalMonitor.Handle, SharpnessVcpCode),
                        ReadInputSource(physicalMonitor.Handle)));

                    monitorNumber++;
                }
            }
            finally
            {
                if (physicalMonitorsOpened)
                {
                    NativeMethods.DestroyPhysicalMonitors(physicalMonitorCount, physicalMonitors);
                }
            }
        }

        return displays;
    }

    public MonitorSwitchResult ApplySettings(MonitorDisplaySettings settings)
    {
        var displayHandles = new List<IntPtr>();
        var identities = GetMonitorIdentitiesByDeviceId();

        if (!NativeMethods.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (monitorHandle, _, _, _) =>
                {
                    displayHandles.Add(monitorHandle);
                    return true;
                },
                IntPtr.Zero))
        {
            return new MonitorSwitchResult(
                settings.MonitorNumber,
                "Display enumeration",
                false,
                "Unable to enumerate display monitors.");
        }

        var monitorNumber = 1;

        foreach (var displayHandle in displayHandles)
        {
            var monitorDeviceId = GetMonitorDeviceId(displayHandle);

            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(displayHandle, out var physicalMonitorCount) ||
                physicalMonitorCount == 0)
            {
                monitorNumber++;
                continue;
            }

            var physicalMonitors = new NativeMethods.PhysicalMonitor[physicalMonitorCount];
            var physicalMonitorsOpened = false;

            try
            {
                if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(displayHandle, physicalMonitorCount, physicalMonitors))
                {
                    monitorNumber++;
                    continue;
                }

                physicalMonitorsOpened = true;

                foreach (var physicalMonitor in physicalMonitors)
                {
                    var description = GetDisplayName(GetMonitorDescription(physicalMonitor), monitorDeviceId, identities);

                    if (monitorNumber == settings.MonitorNumber)
                    {
                        var appliedSettings = new List<string>();
                        var failedSettings = new List<string>();

                        if (settings.Brightness.HasValue &&
                            !NativeMethods.SetVCPFeature(physicalMonitor.Handle, BrightnessVcpCode, ClampVcpValue(settings.Brightness.Value)))
                        {
                            failedSettings.Add("brightness");
                        }
                        else if (settings.Brightness.HasValue)
                        {
                            appliedSettings.Add("brightness");
                        }

                        if (settings.Contrast.HasValue &&
                            !NativeMethods.SetVCPFeature(physicalMonitor.Handle, ContrastVcpCode, ClampVcpValue(settings.Contrast.Value)))
                        {
                            failedSettings.Add("contrast");
                        }
                        else if (settings.Contrast.HasValue)
                        {
                            appliedSettings.Add("contrast");
                        }

                        if (settings.Sharpness.HasValue &&
                            !NativeMethods.SetVCPFeature(physicalMonitor.Handle, SharpnessVcpCode, ClampVcpValue(settings.Sharpness.Value)))
                        {
                            failedSettings.Add("sharpness");
                        }
                        else if (settings.Sharpness.HasValue)
                        {
                            appliedSettings.Add("sharpness");
                        }

                        if (settings.ActiveInput.HasValue &&
                            !NativeMethods.SetVCPFeature(physicalMonitor.Handle, InputSourceVcpCode, (uint)settings.ActiveInput.Value))
                        {
                            failedSettings.Add("active input");
                        }
                        else if (settings.ActiveInput.HasValue)
                        {
                            appliedSettings.Add("active input");
                        }

                        return new MonitorSwitchResult(
                            monitorNumber,
                            description,
                            failedSettings.Count == 0,
                            BuildApplyMessage(appliedSettings, failedSettings));
                    }

                    monitorNumber++;
                }
            }
            finally
            {
                if (physicalMonitorsOpened)
                {
                    NativeMethods.DestroyPhysicalMonitors(physicalMonitorCount, physicalMonitors);
                }
            }
        }

        return new MonitorSwitchResult(
            settings.MonitorNumber,
            "Display enumeration",
            false,
            "The selected monitor was not found.");
    }

    public IReadOnlyList<MonitorSwitchResult> SwitchAll(MonitorInputSource inputSource)
    {
        return Switch(inputSource, primaryOnly: false);
    }

    public IReadOnlyList<MonitorSwitchResult> SwitchPrimary(MonitorInputSource inputSource)
    {
        return Switch(inputSource, primaryOnly: true);
    }

    private IReadOnlyList<MonitorSwitchResult> Switch(MonitorInputSource inputSource, bool primaryOnly)
    {
        var displayHandles = new List<IntPtr>();
        var results = new List<MonitorSwitchResult>();

        if (!NativeMethods.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (monitorHandle, _, _, _) =>
                {
                    displayHandles.Add(monitorHandle);
                    return true;
                },
                IntPtr.Zero))
        {
            return
            [
                new MonitorSwitchResult(0, "Display enumeration", false, "Unable to enumerate display monitors.")
            ];
        }

        var monitorNumber = 1;

        foreach (var displayHandle in displayHandles)
        {
            if (primaryOnly && !IsPrimaryMonitor(displayHandle))
            {
                monitorNumber++;
                continue;
            }

            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(displayHandle, out var physicalMonitorCount) ||
                physicalMonitorCount == 0)
            {
                results.Add(new MonitorSwitchResult(
                    monitorNumber++,
                    "Unknown monitor",
                    false,
                    "No physical monitor handle was returned for this display."));
                continue;
            }

            var physicalMonitors = new NativeMethods.PhysicalMonitor[physicalMonitorCount];
            var physicalMonitorsOpened = false;

            try
            {
                if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(displayHandle, physicalMonitorCount, physicalMonitors))
                {
                    results.Add(new MonitorSwitchResult(
                        monitorNumber++,
                        "Unknown monitor",
                        false,
                        "Unable to open the physical monitor handle."));
                    continue;
                }

                physicalMonitorsOpened = true;

                foreach (var physicalMonitor in physicalMonitors)
                {
                    var description = GetMonitorDescription(physicalMonitor);

                    var success = NativeMethods.SetVCPFeature(
                        physicalMonitor.Handle,
                        InputSourceVcpCode,
                        (uint)inputSource);

                    results.Add(new MonitorSwitchResult(
                        monitorNumber,
                        description,
                        success,
                        success
                            ? $"Changed to {(uint)inputSource}."
                            : "The monitor rejected the input-source command."));

                    monitorNumber++;
                }
            }
            finally
            {
                if (physicalMonitorsOpened)
                {
                    NativeMethods.DestroyPhysicalMonitors(physicalMonitorCount, physicalMonitors);
                }
            }
        }

        if (results.Count == 0)
        {
            var message = primaryOnly
                ? "No primary monitor was detected."
                : "No monitors were detected.";

            results.Add(new MonitorSwitchResult(0, "Display enumeration", false, message));
        }

        return results;
    }

    private static string? GetMonitorDeviceId(IntPtr displayHandle)
    {
        var monitorInfo = new NativeMethods.MonitorInfoEx
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
        };

        if (!NativeMethods.GetMonitorInfo(displayHandle, ref monitorInfo))
        {
            return null;
        }

        var displayDevice = new NativeMethods.DisplayDevice
        {
            Size = Marshal.SizeOf<NativeMethods.DisplayDevice>()
        };

        return NativeMethods.EnumDisplayDevices(monitorInfo.DeviceName, 0, ref displayDevice, 0)
            ? displayDevice.DeviceId
            : null;
    }

    private static bool IsPrimaryMonitor(IntPtr displayHandle)
    {
        var monitorInfo = new NativeMethods.MonitorInfoEx
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
        };

        return NativeMethods.GetMonitorInfo(displayHandle, ref monitorInfo) &&
            (monitorInfo.Flags & NativeMethods.MonitorInfoPrimary) == NativeMethods.MonitorInfoPrimary;
    }

    private static string GetMonitorDescription(NativeMethods.PhysicalMonitor physicalMonitor)
    {
        return string.IsNullOrWhiteSpace(physicalMonitor.Description)
            ? "Unknown monitor"
            : physicalMonitor.Description;
    }

    private static string BuildApplyMessage(IReadOnlyCollection<string> appliedSettings, IReadOnlyCollection<string> failedSettings)
    {
        if (failedSettings.Count > 0)
        {
            return $"Could not apply: {string.Join(", ", failedSettings)}.";
        }

        return appliedSettings.Count == 0
            ? "No changes to apply."
            : $"Applied {string.Join(", ", appliedSettings)}.";
    }

    private static Dictionary<string, MonitorIdentity> GetMonitorIdentitiesByDeviceId()
    {
        var identities = new Dictionary<string, MonitorIdentity>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi",
                "SELECT InstanceName, ManufacturerName, UserFriendlyName, ProductCodeID FROM WmiMonitorID");

            foreach (ManagementObject monitor in searcher.Get().Cast<ManagementObject>())
            {
                var instanceName = monitor["InstanceName"] as string;

                if (string.IsNullOrWhiteSpace(instanceName))
                {
                    continue;
                }

                var deviceId = NormalizeMonitorDeviceId(instanceName);

                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    continue;
                }

                var manufacturerCode = DecodeWmiString(monitor["ManufacturerName"]);
                var manufacturer = GetManufacturerName(manufacturerCode);
                var model = DecodeWmiString(monitor["UserFriendlyName"]);
                var productCode = DecodeWmiString(monitor["ProductCodeID"]);

                AddMonitorIdentityAliases(
                    identities,
                    deviceId,
                    new MonitorIdentity(manufacturer, model, productCode));
            }
        }
        catch (ManagementException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return identities;
    }

    private static string GetDisplayName(
        string fallbackDescription,
        string? monitorDeviceId,
        IReadOnlyDictionary<string, MonitorIdentity> identities)
    {
        if (!string.IsNullOrWhiteSpace(monitorDeviceId) &&
            TryGetMonitorIdentity(monitorDeviceId, identities, out var identity))
        {
            if (!string.IsNullOrWhiteSpace(identity.Model))
            {
                return string.IsNullOrWhiteSpace(identity.Manufacturer)
                    ? identity.Model
                    : $"{identity.Manufacturer} {identity.Model}";
            }

            if (!string.IsNullOrWhiteSpace(identity.Manufacturer) &&
                !string.IsNullOrWhiteSpace(identity.ProductCode))
            {
                return $"{identity.Manufacturer} {identity.ProductCode}";
            }

            if (!string.IsNullOrWhiteSpace(identity.Manufacturer))
            {
                return identity.Manufacturer;
            }
        }

        return fallbackDescription;
    }

    private static string NormalizeMonitorDeviceId(string deviceId)
    {
        var normalized = deviceId
            .Replace(@"\\?\", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace('#', '\\')
            .Replace("DISPLAY\\", @"MONITOR\", StringComparison.OrdinalIgnoreCase);

        var suffixIndex = normalized.IndexOf("_0", StringComparison.OrdinalIgnoreCase);

        if (suffixIndex > 0)
        {
            normalized = normalized[..suffixIndex];
        }

        suffixIndex = normalized.IndexOf("\\{", StringComparison.OrdinalIgnoreCase);

        if (suffixIndex > 0)
        {
            normalized = normalized[..suffixIndex];
        }

        return normalized.Trim('\\');
    }

    private static void AddMonitorIdentityAliases(
        IDictionary<string, MonitorIdentity> identities,
        string deviceId,
        MonitorIdentity identity)
    {
        var normalized = NormalizeMonitorDeviceId(deviceId);

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            identities[normalized] = identity;
        }

        var hardwareId = GetMonitorHardwareId(normalized);

        if (!string.IsNullOrWhiteSpace(hardwareId))
        {
            identities[hardwareId] = identity;
        }
    }

    private static bool TryGetMonitorIdentity(
        string deviceId,
        IReadOnlyDictionary<string, MonitorIdentity> identities,
        out MonitorIdentity identity)
    {
        var normalized = NormalizeMonitorDeviceId(deviceId);

        if (identities.TryGetValue(normalized, out identity!))
        {
            return true;
        }

        var hardwareId = GetMonitorHardwareId(normalized);

        if (!string.IsNullOrWhiteSpace(hardwareId) &&
            identities.TryGetValue(hardwareId, out identity!))
        {
            return true;
        }

        identity = default!;
        return false;
    }

    private static string GetMonitorHardwareId(string normalizedDeviceId)
    {
        var parts = normalizedDeviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 2
            ? parts[1]
            : string.Empty;
    }

    private static string DecodeWmiString(object value)
    {
        if (value is not ushort[] data)
        {
            return string.Empty;
        }

        var chars = data
            .TakeWhile(character => character != 0)
            .Select(character => (char)character)
            .ToArray();

        return new string(chars).Trim();
    }

    private static string GetManufacturerName(string manufacturerCode)
    {
        return ManufacturerNames.TryGetValue(manufacturerCode, out var manufacturerName)
            ? manufacturerName
            : manufacturerCode;
    }

    private static uint? ReadVcpFeature(IntPtr monitorHandle, byte vcpCode)
    {
        return NativeMethods.GetVCPFeatureAndVCPFeatureReply(
            monitorHandle,
            vcpCode,
            out _,
            out var currentValue,
            out _)
            ? ClampVcpValue(currentValue)
            : null;
    }

    private static MonitorInputSource? ReadInputSource(IntPtr monitorHandle)
    {
        if (!NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                monitorHandle,
                InputSourceVcpCode,
                out _,
                out var currentValue,
                out _))
        {
            return null;
        }

        return Enum.IsDefined(typeof(MonitorInputSource), currentValue)
            ? (MonitorInputSource)currentValue
            : null;
    }

    private static uint ClampVcpValue(uint value)
    {
        return Math.Min(value, 100);
    }

    private sealed record MonitorIdentity(string Manufacturer, string Model, string ProductCode);

    private static readonly Dictionary<string, string> ManufacturerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ACR"] = "Acer",
        ["AOC"] = "AOC",
        ["APP"] = "Apple",
        ["ASU"] = "ASUS",
        ["AUS"] = "ASUS",
        ["BNQ"] = "BenQ",
        ["DEL"] = "Dell",
        ["GSM"] = "LG",
        ["HWP"] = "HP",
        ["LEN"] = "Lenovo",
        ["MSI"] = "MSI",
        ["PHL"] = "Philips",
        ["SAM"] = "Samsung",
        ["SEC"] = "Samsung",
        ["SNY"] = "Sony",
        ["VSC"] = "ViewSonic"
    };

    private static partial class NativeMethods
    {
        internal const uint MonitorInfoPrimary = 0x00000001;

        internal delegate bool MonitorEnumProc(
            IntPtr monitorHandle,
            IntPtr deviceContextHandle,
            IntPtr rect,
            IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MonitorInfoEx
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DisplayDevice
        {
            public int Size;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;

            public uint StateFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PhysicalMonitor
        {
            public IntPtr Handle;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayMonitors(
            IntPtr deviceContextHandle,
                IntPtr clippingRect,
                MonitorEnumProc callback,
                IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(
            IntPtr monitorHandle,
            ref MonitorInfoEx monitorInfo);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayDevices(
            string deviceName,
            uint deviceNumber,
            ref DisplayDevice displayDevice,
            uint flags);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
            IntPtr monitorHandle,
            out uint numberOfPhysicalMonitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetPhysicalMonitorsFromHMONITOR(
            IntPtr monitorHandle,
            uint physicalMonitorArraySize,
            [Out] PhysicalMonitor[] physicalMonitorArray);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyPhysicalMonitors(
            uint physicalMonitorArraySize,
            [In] PhysicalMonitor[] physicalMonitorArray);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetVCPFeature(
            IntPtr monitorHandle,
            byte vcpCode,
            uint newValue);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetVCPFeatureAndVCPFeatureReply(
            IntPtr monitorHandle,
            byte vcpCode,
            out uint vcpCodeType,
            out uint currentValue,
            out uint maximumValue);
    }
}
