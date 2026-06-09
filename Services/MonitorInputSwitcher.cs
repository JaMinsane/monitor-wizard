using System.Runtime.InteropServices;

namespace MonitorWizard.Services;

public enum MonitorInputSource : uint
{
    Hdmi = 5,
    DisplayPort = 15
}

public sealed record MonitorSwitchResult(int MonitorNumber, string Description, bool Success, string Message);

public sealed class MonitorInputSwitcher
{
    private const byte InputSourceVcpCode = 0x60;

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
                    var description = string.IsNullOrWhiteSpace(physicalMonitor.Description)
                        ? $"Monitor {monitorNumber}"
                        : physicalMonitor.Description;

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

    private static bool IsPrimaryMonitor(IntPtr displayHandle)
    {
        var monitorInfo = new NativeMethods.MonitorInfoEx
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
        };

        return NativeMethods.GetMonitorInfo(displayHandle, ref monitorInfo) &&
            (monitorInfo.Flags & NativeMethods.MonitorInfoPrimary) == NativeMethods.MonitorInfoPrimary;
    }

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
    }
}
