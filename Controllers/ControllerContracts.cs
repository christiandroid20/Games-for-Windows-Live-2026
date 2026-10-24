using System;

internal enum ControllerConnectionKind
{
    Unknown,
    Wired,
    Wireless
}

internal enum ControllerBatteryLevel
{
    Unknown,
    Empty,
    Low,
    Medium,
    Full
}

internal sealed record ControllerState(ushort Buttons, byte LeftTrigger, byte RightTrigger,
    short LeftX, short LeftY, short RightX, short RightY, uint PacketNumber);

internal sealed record ControllerBattery(ControllerConnectionKind Connection,
    ControllerBatteryLevel Level, string Source, bool IsAvailable);

internal interface IControllerProvider
{
    string Id { get; }
    bool TryGetState(int playerIndex, out ControllerState state);
    ControllerBattery GetBattery(int playerIndex);
}
