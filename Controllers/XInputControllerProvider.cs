using System;
using System.Runtime.InteropServices;

internal sealed class XInputControllerProvider : IControllerProvider
{
    public string Id => "xinput";

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeState
    {
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBattery
    {
        public byte Type;
        public byte Level;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "#100", CallingConvention = CallingConvention.StdCall)]
    private static extern int XInputGetStateEx(int userIndex, out NativeState state);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation", CallingConvention = CallingConvention.StdCall)]
    private static extern int XInputGetBatteryInformation(int userIndex, byte deviceType, out NativeBattery battery);

    public bool TryGetState(int playerIndex, out ControllerState state)
    {
        state = new ControllerState(0, 0, 0, 0, 0, 0, 0, 0);
        if (playerIndex < 0 || playerIndex > 3)
            return false;

        try
        {
            if (XInputGetStateEx(playerIndex, out var native) != 0)
                return false;

            state = new ControllerState(native.Buttons, native.LeftTrigger, native.RightTrigger,
                native.LeftX, native.LeftY, native.RightX, native.RightY, native.PacketNumber);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
    }

    public ControllerBattery GetBattery(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex > 3)
            return new ControllerBattery(ControllerConnectionKind.Unknown, ControllerBatteryLevel.Unknown, "XInput", false);

        try
        {
            // BATTERY_DEVTYPE_GAMEPAD = 0
            if (XInputGetBatteryInformation(playerIndex, 0, out var battery) != 0)
                return new ControllerBattery(ControllerConnectionKind.Unknown, ControllerBatteryLevel.Unknown, "XInput", false);

            var connection = battery.Type switch
            {
                1 => ControllerConnectionKind.Wired,
                2 or 3 => ControllerConnectionKind.Wireless,
                _ => ControllerConnectionKind.Unknown
            };

            var level = battery.Level switch
            {
                0 => ControllerBatteryLevel.Empty,
                1 => ControllerBatteryLevel.Low,
                2 => ControllerBatteryLevel.Medium,
                3 => ControllerBatteryLevel.Full,
                _ => ControllerBatteryLevel.Unknown
            };

            // XInput's battery API is not a universal physical-device API. Treat an
            // unknown type/level as unavailable rather than guessing.
            bool available = connection != ControllerConnectionKind.Unknown &&
                             (connection == ControllerConnectionKind.Wired || level != ControllerBatteryLevel.Unknown);
            return new ControllerBattery(connection, level, "XInput", available);
        }
        catch (DllNotFoundException)
        {
            return new ControllerBattery(ControllerConnectionKind.Unknown, ControllerBatteryLevel.Unknown, "XInput", false);
        }
        catch (EntryPointNotFoundException)
        {
            return new ControllerBattery(ControllerConnectionKind.Unknown, ControllerBatteryLevel.Unknown, "XInput", false);
        }
        catch (BadImageFormatException)
        {
            return new ControllerBattery(ControllerConnectionKind.Unknown, ControllerBatteryLevel.Unknown, "XInput", false);
        }
    }
}
