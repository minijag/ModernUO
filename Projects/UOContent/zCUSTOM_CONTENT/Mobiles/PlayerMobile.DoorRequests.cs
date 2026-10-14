namespace Server.Mobiles;

public partial class PlayerMobile
{
    internal const int DoorMacroCooldownMilliseconds = 250;
    internal const int DoorUseCooldownMilliseconds = 500;
    internal const int DoorErrorCooldownMilliseconds = 5000;
    private bool _hasDoorMacroRequest;
    private long _nextDoorMacroTick;
    private bool _hasDoorUseRequest;
    private long _nextDoorUseTick;
    private bool _hasDoorErrorMessage;
    private long _nextDoorErrorTick;

    internal bool TryDoorMacroRequest()
    {
        var now = Core.TickCount;
        if (_hasDoorMacroRequest && unchecked(now - _nextDoorMacroTick) < 0)
        {
            return false;
        }
        _hasDoorMacroRequest = true;
        _nextDoorMacroTick = unchecked(now + DoorMacroCooldownMilliseconds);
        return true;
    }

    internal bool TryDoorUseRequest()
    {
        var now = Core.TickCount;
        if (_hasDoorUseRequest && unchecked(now - _nextDoorUseTick) < 0)
        {
            return false;
        }
        _hasDoorUseRequest = true;
        _nextDoorUseTick = unchecked(now + DoorUseCooldownMilliseconds);
        return true;
    }

    internal bool TryDoorErrorMessage()
    {
        var now = Core.TickCount;
        if (_hasDoorErrorMessage && unchecked(now - _nextDoorErrorTick) < 0)
        {
            return false;
        }
        _hasDoorErrorMessage = true;
        _nextDoorErrorTick = unchecked(now + DoorErrorCooldownMilliseconds);
        return true;
    }
}
