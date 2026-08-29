namespace Server.Gumps;

/// <summary>
/// Shared visual foundation for Zodiac's custom gumps.
/// </summary>
public static class CustomGumpStyle
{
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

    public static void AddWalnutSteelPanel(ref DynamicGumpBuilder builder, int x, int y, int width, int height)
    {
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
    }
}
