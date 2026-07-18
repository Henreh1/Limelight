using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Limelight.Services
{
    public sealed class GlobalHotkeyService : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSystemKeyDown = 0x0104;
        private const int WmSystemKeyUp = 0x0105;
        private const int VkControl = 0x11;
        private const int VkMenu = 0x12;
        private const int VkShift = 0x10;
        private const int VkLeftWindows = 0x5B;
        private const int VkRightWindows = 0x5C;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;

        private LowLevelKeyboardProcedure? _keyboardProcedure;
        private IntPtr _keyboardHook;
        private Dispatcher? _dispatcher;
        private Func<bool>? _activationPredicate;
        private int _virtualKey;
        private uint _modifiers;
        private bool _keyHeld;

        public event Action? Pressed;

        public bool Register(
            Window owner,
            string gesture,
            Func<bool> activationPredicate,
            out string errorMessage)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(activationPredicate);

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

            _dispatcher =
                owner.Dispatcher;

            _activationPredicate =
                activationPredicate;

            _virtualKey =
                KeyInterop.VirtualKeyFromKey(key);

            _modifiers =
                modifiers;

            _keyboardProcedure =
                KeyboardMessageReceived;

            _keyboardHook =
                SetWindowsHookEx(
                    WhKeyboardLl,
                    _keyboardProcedure,
                    GetModuleHandle(null),
                    0);

            if (_keyboardHook == IntPtr.Zero)
            {
                int windowsError =
                    Marshal.GetLastWin32Error();

                Unregister();

                errorMessage =
                    "Limelight could not listen for the X19 hotkey. " +
                    $"Windows error {windowsError}.";

                return false;
            }

            errorMessage =
                string.Empty;

            return true;
        }

        public void Unregister()
        {
            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(
                    _keyboardHook);
            }

            _keyboardHook = IntPtr.Zero;
            _keyboardProcedure = null;
            _dispatcher = null;
            _activationPredicate = null;
            _virtualKey = 0;
            _modifiers = 0;
            _keyHeld = false;
        }

        public void Dispose()
        {
            Unregister();
        }

        private IntPtr KeyboardMessageReceived(
            int code,
            IntPtr messagePointer,
            IntPtr dataPointer)
        {
            if (code < 0)
            {
                return CallNextHookEx(
                    _keyboardHook,
                    code,
                    messagePointer,
                    dataPointer);
            }

            LowLevelKeyboardInput keyboardInput =
                Marshal.PtrToStructure<LowLevelKeyboardInput>(
                    dataPointer);

            if (keyboardInput.VirtualKey !=
                (uint)_virtualKey)
            {
                return CallNextHookEx(
                    _keyboardHook,
                    code,
                    messagePointer,
                    dataPointer);
            }

            int message =
                messagePointer.ToInt32();

            if (message == WmKeyUp ||
                message == WmSystemKeyUp)
            {
                _keyHeld = false;

                return CallNextHookEx(
                    _keyboardHook,
                    code,
                    messagePointer,
                    dataPointer);
            }

            if (message != WmKeyDown &&
                message != WmSystemKeyDown)
            {
                return CallNextHookEx(
                    _keyboardHook,
                    code,
                    messagePointer,
                    dataPointer);
            }

            if (_keyHeld)
            {
                return CallNextHookEx(
                    _keyboardHook,
                    code,
                    messagePointer,
                    dataPointer);
            }

            _keyHeld = true;

            bool isActive;

            try
            {
                isActive =
                    _activationPredicate?.Invoke() == true;
            }
            catch
            {
                isActive = false;
            }

            if (!isActive ||
                !ModifiersMatch())
            {
                // The key belongs to the foreground application whenever Dead
                // as Disco is not selected. Limelight does not reserve it.
                return CallNextHookEx(
                    _keyboardHook,
                    code,
                    messagePointer,
                    dataPointer);
            }

            Dispatcher? dispatcher =
                _dispatcher;

            if (dispatcher is null ||
                dispatcher.HasShutdownStarted)
            {
                return CallNextHookEx(
                    _keyboardHook,
                    code,
                    messagePointer,
                    dataPointer);
            }

            dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                    Pressed?.Invoke()));

            // The X19 key is consumed only while Dead as Disco owns focus so
            // the same press cannot trigger an unrelated in-game action.
            return new IntPtr(1);
        }

        private bool ModifiersMatch()
        {
            bool controlPressed =
                IsKeyPressed(VkControl);

            bool altPressed =
                IsKeyPressed(VkMenu);

            bool shiftPressed =
                IsKeyPressed(VkShift);

            bool windowsPressed =
                IsKeyPressed(VkLeftWindows) ||
                IsKeyPressed(VkRightWindows);

            return
                !windowsPressed &&
                controlPressed ==
                    ((_modifiers & ModControl) != 0) &&
                altPressed ==
                    ((_modifiers & ModAlt) != 0) &&
                shiftPressed ==
                    ((_modifiers & ModShift) != 0);
        }

        private static bool IsKeyPressed(
            int virtualKey)
        {
            return
                (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
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

        private delegate IntPtr LowLevelKeyboardProcedure(
            int code,
            IntPtr messagePointer,
            IntPtr dataPointer);

        [StructLayout(LayoutKind.Sequential)]
        private struct LowLevelKeyboardInput
        {
            public uint VirtualKey;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInformation;
        }

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int hookType,
            LowLevelKeyboardProcedure callback,
            IntPtr moduleHandle,
            uint threadId);

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(
            IntPtr hookHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hookHandle,
            int code,
            IntPtr messagePointer,
            IntPtr dataPointer);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(
            int virtualKey);

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern IntPtr GetModuleHandle(
            string? moduleName);
    }
}
