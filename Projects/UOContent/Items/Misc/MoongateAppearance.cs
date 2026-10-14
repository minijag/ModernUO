namespace Server.Items;

public static class MoongateAppearance
{
    public const int LegacyBlue = 0xF6C;
    public const int LegacyDark = 0x1FD4;
    public const int Blue = 0x6380;
    public const int Dark = 0x6390;
    public const int FrameCount = 16;

    public static bool IsLegacy(int itemId) =>
        itemId is >= LegacyBlue and <= LegacyBlue + 4 or >= LegacyDark and <= LegacyDark + 4;

    public static bool IsReplacement(int itemId) =>
        itemId is >= Blue and < Blue + FrameCount or >= Dark and < Dark + FrameCount;

    public static bool IsMoongate(int itemId) => IsLegacy(itemId) || IsReplacement(itemId);

    public static int LegacyLabelNumber(int itemId) => 1020000 + (itemId switch
    {
        >= Blue and < Blue + FrameCount => LegacyBlue + System.Math.Min(4, (itemId - Blue) / 3),
        >= Dark and < Dark + FrameCount => LegacyDark + System.Math.Min(4, (itemId - Dark) / 3),
        _ => itemId
    });

    public static bool IsBlueBase(int itemId) => itemId is LegacyBlue or Blue;

    public static int ConvertLegacy(int itemId) => itemId switch
    {
        >= LegacyBlue and <= LegacyBlue + 4 => Blue + (itemId - LegacyBlue) * 3,
        >= LegacyDark and <= LegacyDark + 4 => Dark + (itemId - LegacyDark) * 3,
        _ => itemId
    };
}
