using System;

namespace Server.Gumps;

/// <summary>
/// Shared visual foundation for Zodiac's custom gumps.
/// </summary>
public static class CustomGumpStyle
{
    // Design ceiling for all custom gumps: the [stats] frame is 720 x 600 pixels.
    // The title ribbon extends 6 pixels above it, making the full footprint 720 x 606.
    public const int MaximumPanelWidth = 720;
    public const int MaximumPanelHeight = 600;
    public const int ContentPadding = 24;
    // UO HTML font setting used by the Help menu; not an ASCII font index.
    public const int StandardFontSize = 4;
    public const int TextHue = 0x481;
    public const int LocalizedTextColor = 0x7FFF;
    public const string TextColor = "#F2F0E8";
    public const string WhiteTextColor = "#FFFFFF";
    public const string TitleColor = "#F4D27A";
    public const string ActionColor = "#FFE8A6";
    public const int TitleBannerGumpId = 64040;
    public const int TitleBannerWidth = 300;
    public const int MenuButtonNormalGumpId = 64042;
    public const int MenuButtonPressedGumpId = 64043;
    public const int MenuButtonSize = 56;
    public const int MenuButtonLabelWidth = 132;
    public const string BannerTitleColor = "#24180F";
    // Center the visible font-4 glyphs on the cloth, rather than their HTML line box.
    private const int TitleTextOffsetY = 1;

    private const int CornerSize = 20;
    // Let the black backing overlap the inner edge of the frame.  The wood rails are
    // drawn afterwards, so this removes the one-pixel seam without covering the art.
    private const int PanelInset = 17;
    private const int TopLeftGumpId = 60002;
    private const int TopGumpId = 60003;
    private const int TopRightGumpId = 60004;
    private const int LeftGumpId = 60005;
    private const int RightGumpId = 60006;
    private const int BottomLeftGumpId = 60007;
    private const int BottomGumpId = 60008;
    private const int BottomRightGumpId = 60009;

    /// <summary>Places a reusable button beneath its separate, centered label.</summary>
    public static void AddMenuButton(ref DynamicGumpBuilder builder, int centerX, int labelY, int buttonId, string label)
    {
        builder.AddHtml(centerX - MenuButtonLabelWidth / 2, labelY, MenuButtonLabelWidth, 24,
            label, WhiteTextColor, StandardFontSize, align: TextAlignment.Center);
        builder.AddButton(centerX - MenuButtonSize / 2, labelY + 28,
            MenuButtonNormalGumpId, MenuButtonPressedGumpId, buttonId);
    }

    public static void AddMenuButton(ref StaticGumpBuilder builder, int centerX, int labelY, int buttonId, string label)
    {
        builder.AddHtml(centerX - MenuButtonLabelWidth / 2, labelY, MenuButtonLabelWidth, 24,
            label, WhiteTextColor, StandardFontSize, align: TextAlignment.Center);
        builder.AddButton(centerX - MenuButtonSize / 2, labelY + 28,
            MenuButtonNormalGumpId, MenuButtonPressedGumpId, buttonId);
    }

    public static void AddWalnutSteelPanel(ref DynamicGumpBuilder builder, int x, int y, int width, int height) =>
        AddPanel(ref builder, x, y, width, height);

    /// <summary>Frames an existing content rectangle without moving its controls.</summary>
    public static void AddContentPanel(ref DynamicGumpBuilder builder, int x, int y, int width, int height, string title = null) =>
        AddPanel(ref builder, x - ContentPadding, y - ContentPadding,
            width + ContentPadding * 2, height + ContentPadding * 2, title);

    public static void AddContentPanel(ref StaticGumpBuilder builder, int x, int y, int width, int height, string title = null) =>
        AddPanel(ref builder, x - ContentPadding, y - ContentPadding,
            width + ContentPadding * 2, height + ContentPadding * 2, title);

    /// <summary>Draws the shared black backing and tiled walnut/steel frame inside the supplied bounds.</summary>
    public static void AddPanel(ref DynamicGumpBuilder builder, int x, int y, int width, int height, string title = null)
    {
        ValidateSize(width, height);
        var innerWidth = width - CornerSize * 2;
        var innerHeight = height - CornerSize * 2;
        var panelWidth = width - PanelInset * 2;
        var panelHeight = height - PanelInset * 2;

        builder.AddImageTiled(x + PanelInset, y + PanelInset, panelWidth, panelHeight, 2624);

        // Each alpha region halves the remaining scene visibility. Three layers leave roughly 12.5%
        // of the world visible: the closest available rendering to a 90% opaque black panel.
        builder.AddAlphaRegion(x + PanelInset, y + PanelInset, panelWidth, panelHeight);
        builder.AddAlphaRegion(x + PanelInset, y + PanelInset, panelWidth, panelHeight);
        builder.AddAlphaRegion(x + PanelInset, y + PanelInset, panelWidth, panelHeight);

        builder.AddImage(x, y, TopLeftGumpId);
        builder.AddImageTiled(x + CornerSize, y, innerWidth, CornerSize, TopGumpId);
        builder.AddImage(x + width - CornerSize, y, TopRightGumpId);
        builder.AddImageTiled(x, y + CornerSize, CornerSize, innerHeight, LeftGumpId);
        builder.AddImageTiled(x + width - CornerSize, y + CornerSize, CornerSize, innerHeight, RightGumpId);
        builder.AddImage(x, y + height - CornerSize, BottomLeftGumpId);
        builder.AddImageTiled(x + CornerSize, y + height - CornerSize, innerWidth, CornerSize, BottomGumpId);
        builder.AddImage(x + width - CornerSize, y + height - CornerSize, BottomRightGumpId);
        if (!string.IsNullOrEmpty(title))
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, TitleBannerWidth + CornerSize * 2);
            var bannerX = x + (width - TitleBannerWidth) / 2;
            builder.AddImage(bannerX, y - 6, TitleBannerGumpId);
            builder.AddHtml(bannerX + 35, y + TitleTextOffsetY, 230, 24, title, BannerTitleColor, StandardFontSize, align: TextAlignment.Center);
        }
    }

    public static void AddPanel(ref StaticGumpBuilder builder, int x, int y, int width, int height, string title = null)
    {
        ValidateSize(width, height);
        var innerWidth = width - CornerSize * 2;
        var innerHeight = height - CornerSize * 2;
        var panelWidth = width - PanelInset * 2;
        var panelHeight = height - PanelInset * 2;

        builder.AddImageTiled(x + PanelInset, y + PanelInset, panelWidth, panelHeight, 2624);
        builder.AddAlphaRegion(x + PanelInset, y + PanelInset, panelWidth, panelHeight);
        builder.AddAlphaRegion(x + PanelInset, y + PanelInset, panelWidth, panelHeight);
        builder.AddAlphaRegion(x + PanelInset, y + PanelInset, panelWidth, panelHeight);

        builder.AddImage(x, y, TopLeftGumpId);
        builder.AddImageTiled(x + CornerSize, y, innerWidth, CornerSize, TopGumpId);
        builder.AddImage(x + width - CornerSize, y, TopRightGumpId);
        builder.AddImageTiled(x, y + CornerSize, CornerSize, innerHeight, LeftGumpId);
        builder.AddImageTiled(x + width - CornerSize, y + CornerSize, CornerSize, innerHeight, RightGumpId);
        builder.AddImage(x, y + height - CornerSize, BottomLeftGumpId);
        builder.AddImageTiled(x + CornerSize, y + height - CornerSize, innerWidth, CornerSize, BottomGumpId);
        builder.AddImage(x + width - CornerSize, y + height - CornerSize, BottomRightGumpId);
        if (!string.IsNullOrEmpty(title))
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, TitleBannerWidth + CornerSize * 2);
            var bannerX = x + (width - TitleBannerWidth) / 2;
            builder.AddImage(bannerX, y - 6, TitleBannerGumpId);
            builder.AddHtml(bannerX + 35, y + TitleTextOffsetY, 230, 24, title, BannerTitleColor, StandardFontSize, align: TextAlignment.Center);
        }
    }

    private static void ValidateSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, CornerSize * 2 + 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, CornerSize * 2 + 1);
    }
}
