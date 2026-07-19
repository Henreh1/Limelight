using System;
using System.Runtime.InteropServices;

namespace Limelight.Services
{
    [Flags]
    internal enum XInputButton : ushort
    {
        None = 0,
        DPadUp = 0x0001,
        DPadDown = 0x0002,
        DPadLeft = 0x0004,
        DPadRight = 0x0008,
        Start = 0x0010,
        Back = 0x0020,
        LeftThumb = 0x0040,
        RightThumb = 0x0080,
        LeftShoulder = 0x0100,
        RightShoulder = 0x0200,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000
    }

    internal static class XInputControllerService
    {
        private const uint XInputSuccess = 0;
        private const uint MaximumControllerCount = 4;

        private static readonly (XInputButton Button, string Gesture)[]
            SupportedButtons =
            {
                (XInputButton.DPadUp, "GAMEPAD D-PAD UP"),
                (XInputButton.DPadDown, "GAMEPAD D-PAD DOWN"),
                (XInputButton.DPadLeft, "GAMEPAD D-PAD LEFT"),
                (XInputButton.DPadRight, "GAMEPAD D-PAD RIGHT"),
                (XInputButton.LeftShoulder, "GAMEPAD LB"),
                (XInputButton.RightShoulder, "GAMEPAD RB"),
                (XInputButton.LeftThumb, "GAMEPAD L3"),
                (XInputButton.RightThumb, "GAMEPAD R3"),
                (XInputButton.Start, "GAMEPAD MENU"),
                (XInputButton.Back, "GAMEPAD VIEW"),
                (XInputButton.A, "GAMEPAD A"),
                (XInputButton.B, "GAMEPAD B"),
                (XInputButton.X, "GAMEPAD X"),
                (XInputButton.Y, "GAMEPAD Y")
            };

        public static bool TryReadCombinedButtons(
            out XInputButton buttons)
        {
            buttons =
                XInputButton.None;

            bool controllerConnected =
                false;

            try
            {
                // I combine connected pads so the chosen X19 control follows
                // whichever controller is currently being used.
                for (uint controllerIndex = 0;
                     controllerIndex < MaximumControllerCount;
                     controllerIndex++)
                {
                    if (XInputGetState(
                            controllerIndex,
                            out XInputState state) !=
                        XInputSuccess)
                    {
                        continue;
                    }

                    controllerConnected = true;
                    buttons |=
                        state.Gamepad.Buttons;
                }
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (BadImageFormatException)
            {
                return false;
            }

            return controllerConnected;
        }

        public static bool TryParseGesture(
            string gesture,
            out XInputButton button)
        {
            button =
                XInputButton.None;

            if (string.IsNullOrWhiteSpace(gesture))
            {
                return false;
            }

            foreach ((XInputButton candidate, string name) in
                     SupportedButtons)
            {
                if (!string.Equals(
                        gesture.Trim(),
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                button = candidate;
                return true;
            }

            return false;
        }

        public static bool TryCreateGesture(
            XInputButton newlyPressedButtons,
            out string gesture)
        {
            foreach ((XInputButton button, string name) in
                     SupportedButtons)
            {
                if ((newlyPressedButtons & button) == 0)
                {
                    continue;
                }

                gesture = name;
                return true;
            }

            gesture =
                string.Empty;

            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint PacketNumber;
            public XInputGamepad Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad
        {
            public XInputButton Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short LeftThumbX;
            public short LeftThumbY;
            public short RightThumbX;
            public short RightThumbY;
        }

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(
            uint userIndex,
            out XInputState state);
    }
}
