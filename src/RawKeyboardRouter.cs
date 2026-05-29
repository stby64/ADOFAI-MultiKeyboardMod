using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using UnityEngine;
using UnityModManagerNet;

namespace MultiKeyboardProbeClean
{
    internal static class RawKeyboardRouter
    {
        private const int WM_INPUT = 0x00FF;
        private const int RIM_TYPEKEYBOARD = 1;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDI_DEVICENAME = 0x20000007;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const ushort RI_KEY_BREAK = 0x0001;

        private static readonly object Gate = new object();
        private static readonly PlayerState[] Players = { new PlayerState(), new PlayerState() };

        private static RawInputWindow window;
        private static string statusText = "not started";

        internal static bool IsRunning { get { return window != null && window.Handle != IntPtr.Zero; } }
        internal static string StatusText { get { return statusText; } }

        internal static void Start(UnityModManager.ModEntry entry)
        {
            Stop();
            try
            {
                window = new RawInputWindow();
                window.CreateHandle(new CreateParams());

                RAWINPUTDEVICE[] devices = new RAWINPUTDEVICE[1];
                devices[0].usUsagePage = 0x01;
                devices[0].usUsage = 0x06;
                devices[0].dwFlags = RIDEV_INPUTSINK;
                devices[0].hwndTarget = window.Handle;

                bool ok = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
                statusText = ok ? "running" : "RegisterRawInputDevices failed: " + Marshal.GetLastWin32Error();
                entry.Logger.Log("Raw input router: " + statusText);
            }
            catch (Exception ex)
            {
                statusText = "start failed: " + ex.Message;
                entry.Logger.Log("Raw input router start failed: " + ex);
                Stop();
            }
        }

        internal static void Stop()
        {
            if (window != null)
            {
                window.DestroyHandle();
                window = null;
            }
            ClearAll();
            statusText = "stopped";
        }

        internal static void ClearAll()
        {
            lock (Gate)
            {
                for (int i = 0; i < Players.Length; i++)
                    Players[i].Clear();
            }
        }

        internal static bool HasPendingPress(int playerId)
        {
            if (!IsValidPlayer(playerId))
                return false;
            lock (Gate)
                return Players[playerId].PendingPressKeys.Count > 0;
        }

        internal static int ConsumePressCount(int playerId)
        {
            if (!IsValidPlayer(playerId))
                return 0;

            lock (Gate)
            {
                PlayerState state = Players[playerId];
                int count = state.PendingPressKeys.Count;
                state.LastPressKeys.Clear();
                for (int i = 0; i < state.PendingPressKeys.Count; i++)
                    state.LastPressKeys.Add(state.PendingPressKeys[i]);
                state.LastPressFrame = Time.frameCount;
                state.PendingPressKeys.Clear();
                return count;
            }
        }

        internal static int GetFramePressCount(int playerId)
        {
            if (!IsValidPlayer(playerId))
                return 0;

            lock (Gate)
            {
                PlayerState state = Players[playerId];
                int frame = Time.frameCount;
                if (state.ServedPressFrame == frame)
                    return state.ServedPressCount;

                state.ServedPressFrame = frame;
                state.ServedPressCount = state.PendingPressKeys.Count;
                state.LastPressKeys.Clear();
                for (int i = 0; i < state.PendingPressKeys.Count; i++)
                    state.LastPressKeys.Add(state.PendingPressKeys[i]);
                state.LastPressFrame = frame;
                state.PendingPressKeys.Clear();

                if (state.ServedPressCount == 0 && frame - state.PendingPressFrame > 2)
                {
                    state.LastPressKeys.Clear();
                    state.LastPressFrame = -1000;
                }

                return state.ServedPressCount;
            }
        }

        internal static int GetHeldCount(int playerId)
        {
            if (!IsValidPlayer(playerId))
                return 0;

            lock (Gate)
                return Players[playerId].HeldKeys.Count;
        }

        internal static string DebugState
        {
            get
            {
                lock (Gate)
                {
                    return "P1 pending=" + Players[0].PendingPressKeys.Count +
                           " held=" + Players[0].HeldKeys.Count +
                           " served=" + Players[0].ServedPressCount + "@" + Players[0].ServedPressFrame +
                           " | P2 pending=" + Players[1].PendingPressKeys.Count +
                           " held=" + Players[1].HeldKeys.Count +
                           " served=" + Players[1].ServedPressCount + "@" + Players[1].ServedPressFrame;
                }
            }
        }

        internal static bool ConsumeRelease(int playerId)
        {
            if (!IsValidPlayer(playerId))
                return false;

            lock (Gate)
            {
                bool value = Players[playerId].PendingRelease;
                Players[playerId].PendingRelease = false;
                return value;
            }
        }

        internal static List<AnyKeyCode> GetAnyKeys(int playerId, bool pressedOnly)
        {
            List<AnyKeyCode> result = new List<AnyKeyCode>();
            if (!IsValidPlayer(playerId))
                return result;

            lock (Gate)
            {
                PlayerState state = Players[playerId];
                if (pressedOnly)
                {
                    if (Time.frameCount - state.LastPressFrame > 1)
                        return result;
                    for (int i = 0; i < state.LastPressKeys.Count; i++)
                        result.Add(CreateAnyKey(playerId, state.LastPressKeys[i]));
                    return result;
                }

                foreach (string key in state.HeldKeys)
                    result.Add(CreateAnyKey(playerId, key));
                return result;
            }
        }

        internal static int GetPressTotal(int playerId)
        {
            if (!IsValidPlayer(playerId))
                return 0;
            lock (Gate)
                return Players[playerId].TotalPresses;
        }

        private static AnyKeyCode CreateAnyKey(int playerId, string key)
        {
            return new AnyKeyCode("MKP-P" + (playerId + 1) + "-" + key, typeof(string), SyntheticKeyEquals);
        }

        private static bool SyntheticKeyEquals(object left, object right)
        {
            return object.Equals(left, right);
        }

        private static bool IsValidPlayer(int playerId)
        {
            return playerId >= 0 && playerId < Players.Length;
        }

        private static void ProcessRawInput(IntPtr lParam)
        {
            uint size = 0;
            GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
            if (size == 0)
                return;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                uint read = GetRawInputData(lParam, RID_INPUT, buffer, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
                if (read == uint.MaxValue || read == 0)
                    return;

                RAWINPUTHEADER header = (RAWINPUTHEADER)Marshal.PtrToStructure(buffer, typeof(RAWINPUTHEADER));
                if (header.dwType != RIM_TYPEKEYBOARD)
                    return;

                IntPtr keyboardPtr = IntPtr.Add(buffer, Marshal.SizeOf(typeof(RAWINPUTHEADER)));
                RAWKEYBOARD keyboard = (RAWKEYBOARD)Marshal.PtrToStructure(keyboardPtr, typeof(RAWKEYBOARD));
                if (keyboard.VKey == 0 || keyboard.VKey == 255 || ShouldIgnoreVKey(keyboard.VKey))
                    return;

                string groupKey = GetGroupKey(header.hDevice);
                int playerId = ResolvePlayer(groupKey);
                if (playerId < 0)
                    return;

                bool isUp = (keyboard.Flags & RI_KEY_BREAK) != 0;
                string key = keyboard.VKey.ToString("X2") + ":" + keyboard.MakeCode.ToString("X2");

                lock (Gate)
                {
                    PlayerState state = Players[playerId];
                    if (isUp)
                    {
                        if (state.HeldKeys.Remove(key))
                            state.PendingRelease = true;
                    }
                    else if (!state.HeldKeys.Contains(key))
                    {
                        state.HeldKeys.Add(key);
                        state.PendingPressKeys.Add(key);
                        state.PendingPressFrame = Time.frameCount;
                        state.TotalPresses++;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool ShouldIgnoreVKey(ushort vkey)
        {
            return vkey == 0x1B || vkey == 0x11 || vkey == 0xA2 || vkey == 0xA3;
        }

        private static int ResolvePlayer(string groupKey)
        {
            if (string.Equals(groupKey, Main.Player1GroupKey, StringComparison.OrdinalIgnoreCase))
                return 0;
            if (string.Equals(groupKey, Main.Player2GroupKey, StringComparison.OrdinalIgnoreCase))
                return 1;
            return -1;
        }

        private static string GetGroupKey(IntPtr hDevice)
        {
            string path = TryGetDeviceName(hDevice);
            string vid = ExtractId(path, "VID");
            string pid = ExtractId(path, "PID");
            if (string.IsNullOrEmpty(vid) || string.IsNullOrEmpty(pid))
                return string.Empty;
            return vid + ":" + pid;
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
                return Marshal.PtrToStringUni(buffer) ?? string.Empty;
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

        private sealed class PlayerState
        {
            public readonly HashSet<string> HeldKeys = new HashSet<string>();
            public readonly List<string> PendingPressKeys = new List<string>();
            public readonly List<string> LastPressKeys = new List<string>();
            public bool PendingRelease;
            public int PendingPressFrame = -1000;
            public int ServedPressFrame = -1000;
            public int ServedPressCount;
            public int LastPressFrame = -1000;
            public int TotalPresses;

            public void Clear()
            {
                HeldKeys.Clear();
                PendingPressKeys.Clear();
                LastPressKeys.Clear();
                PendingRelease = false;
                PendingPressFrame = -1000;
                ServedPressFrame = -1000;
                ServedPressCount = 0;
                LastPressFrame = -1000;
                TotalPresses = 0;
            }
        }

        private sealed class RawInputWindow : NativeWindow
        {
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_INPUT)
                    ProcessRawInput(m.LParam);
                base.WndProc(ref m);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWKEYBOARD
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [DllImport("User32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("User32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);
    }
}
