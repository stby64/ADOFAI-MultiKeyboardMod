using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MultiKeyboardProbeClean
{
    internal static class RawInputProbe
    {
        private const int RIM_TYPEKEYBOARD = 1;
        private const uint RIDI_DEVICENAME = 0x20000007;
        private const uint RIDI_DEVICEINFO = 0x2000000b;

        internal static List<KeyboardGroup> GetInterestingKeyboardGroups()
        {
            Dictionary<string, KeyboardGroup> groups = new Dictionary<string, KeyboardGroup>();
            uint deviceCount = 0;
            int listResult = GetRawInputDeviceList(IntPtr.Zero, ref deviceCount, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST)));
            if (listResult != 0 || deviceCount == 0)
                return new List<KeyboardGroup>();

            RAWINPUTDEVICELIST[] list = new RAWINPUTDEVICELIST[deviceCount];
            int read = GetRawInputDeviceList(list, ref deviceCount, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST)));
            if (read < 0)
                return new List<KeyboardGroup>();

            foreach (RAWINPUTDEVICELIST entry in list)
            {
                if (entry.dwType != RIM_TYPEKEYBOARD)
                    continue;

                string path = TryGetDeviceName(entry.hDevice);
                string vid = ExtractId(path, "VID");
                string pid = ExtractId(path, "PID");
                string vendorName = GuessVendorName(vid, pid);
                if (string.IsNullOrEmpty(vendorName))
                    continue;

                string key = vid + ":" + pid;
                KeyboardGroup group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new KeyboardGroup();
                    group.GroupKey = key;
                    group.DisplayName = vendorName;
                    group.VendorId = vid;
                    group.ProductId = pid;
                    groups.Add(key, group);
                }

                group.InterfaceCount += 1;
                string instance = ExtractTrailingInstance(path);
                if (!string.IsNullOrEmpty(instance))
                {
                    if (group.InstancePreview.Length > 0)
                        group.InstancePreview.Append(", ");
                    group.InstancePreview.Append(instance);
                }
            }

            return new List<KeyboardGroup>(groups.Values);
        }

        private static string GuessVendorName(string vid, string pid)
        {
            if (vid == "1B1C")
                return "Corsair Keyboard";
            if (vid == "0416" && pid == "9258")
                return "Archon Keyboard";
            return string.Empty;
        }

        private static string TryGetDeviceName(IntPtr hDevice)
        {
            uint size = 0;
            GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref size);
            if (size == 0)
                return string.Empty;

            IntPtr buffer = Marshal.AllocHGlobal((int)(size * 2));
            try
            {
                uint finalSize = size;
                uint result = GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buffer, ref finalSize);
                if (result == uint.MaxValue)
                    return string.Empty;
                string value = Marshal.PtrToStringUni(buffer);
                return value ?? string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string ExtractId(string text, string key)
        {
            Match match = Regex.Match(text ?? string.Empty, key + "_([0-9a-fA-F]{4})", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : string.Empty;
        }

        private static string ExtractTrailingInstance(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            string[] parts = text.Split('#');
            if (parts.Length == 0)
                return string.Empty;
            return parts[parts.Length - 1];
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        [DllImport("User32.dll", SetLastError = true)]
        private static extern int GetRawInputDeviceList([In, Out] RAWINPUTDEVICELIST[] pRawInputDeviceList, ref uint uiNumDevices, uint cbSize);

        [DllImport("User32.dll", SetLastError = true)]
        private static extern int GetRawInputDeviceList(IntPtr pRawInputDeviceList, ref uint uiNumDevices, uint cbSize);

        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        internal sealed class KeyboardGroup
        {
            public string GroupKey;
            public string DisplayName;
            public string VendorId;
            public string ProductId;
            public int InterfaceCount;
            public StringBuilder InstancePreview;

            public KeyboardGroup()
            {
                GroupKey = string.Empty;
                DisplayName = string.Empty;
                VendorId = string.Empty;
                ProductId = string.Empty;
                InterfaceCount = 0;
                InstancePreview = new StringBuilder();
            }
        }
    }
}
