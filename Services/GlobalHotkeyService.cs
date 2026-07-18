using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Limelight.Services
{
    public sealed class GlobalHotkeyService : IDisposable
    {
        private const int HotkeyId = 0x4C19;
        private const int WmHotkey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModNoRepeat = 0x4000;

        private HwndSource? _windowSource;
        private IntPtr _windowHandle;
        private bool _isRegistered;

        public event Action? Pressed;

        public bool Register(
            Window owner,
            string gesture,
            out string errorMessage)
        {
            ArgumentNullException.ThrowIfNull(owner);

            Unregister();

            if (!TryParseGesture(
                    gesture,
                    out Key key,
                    out uint modifiers))
            {
                errorMessage =
                    "The configured X19 hotkey could not be understood.";

                return false;
            }

            WindowInteropHelper helper =
                new(owner);

            _windowHandle =
                helper.EnsureHandle();

            _windowSource =
                HwndSource.FromHwnd(
                    _windowHandle);

            if (_windowSource is null)
            {
                errorMessage =
                    "Limelight could not attach the X19 hotkey to its window.";

                return false;
            }

            _windowSource.AddHook(
                WindowMessageReceived);

            int virtualKey =
                KeyInterop.VirtualKeyFromKey(
                    key);

            _isRegistered =
                RegisterHotKey(
                    _windowHandle,
                    HotkeyId,
                    modifiers | ModNoRepeat,
                    (uint)virtualKey);

            if (!_isRegistered)
            {
                _windowSource.RemoveHook(
                    WindowMessageReceived);

                _windowSource = null;
                _windowHandle = IntPtr.Zero;

                errorMessage =
                    $"{gesture.ToUpperInvariant()} is already being used by Windows or another application.";

                return false;
            }

            errorMessage =
                string.Empty;

            return true;
        }

        public void Unregister()
        {
            if (_isRegistered &&
                _windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(
                    _windowHandle,
                    HotkeyId);
            }

            if (_windowSource is not null)
            {
                _windowSource.RemoveHook(
                    WindowMessageReceived);
            }

            _isRegistered = false;
            _windowSource = null;
            _windowHandle = IntPtr.Zero;
        }

        public void Dispose()
        {
            Unregister();
        }

        private IntPtr WindowMessageReceived(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == WmHotkey &&
                wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                Pressed?.Invoke();
            }

            return IntPtr.Zero;
        }

        private static bool TryParseGesture(
            string gesture,
            out Key key,
            out uint modifiers)
        {
            key = Key.None;
            modifiers = 0;

            if (string.IsNullOrWhiteSpace(gesture))
            {
                return false;
            }

            string[] parts =
                gesture.Split(
                    '+',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

            foreach (string part in parts)
            {
                switch (part.ToUpperInvariant())
                {
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= ModControl;
                        continue;

                    case "ALT":
                        modifiers |= ModAlt;
                        continue;

                    case "SHIFT":
                        modifiers |= ModShift;
                        continue;
                }

                string keyName =
                    part.Length == 1 &&
                    char.IsDigit(part[0])
                        ? $"D{part}"
                        : part;

                if (!Enum.TryParse(
                        keyName,
                        ignoreCase: true,
                        out key))
                {
                    return false;
                }
            }

            return key != Key.None;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(
            IntPtr windowHandle,
            int id,
            uint modifiers,
            uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(
            IntPtr windowHandle,
            int id);
    }
}
