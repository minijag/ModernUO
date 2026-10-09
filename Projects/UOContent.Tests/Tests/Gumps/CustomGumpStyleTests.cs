using System;
using System.Text;
using Server.Gumps;
using Xunit;

namespace Server.Tests.Gumps;

[CollectionDefinition("Gump style layouts", DisableParallelization = true)]
public class GumpStyleLayoutCollection;

[Collection("Gump style layouts")]
public class CustomGumpStyleTests
{
    [Theory]
    [InlineData(50, 25, 540, 430)]
    [InlineData(-24, -24, 468, 488)]
    [InlineData(0, 0, 41, 41)]
    public void StaticAndDynamicPanelsMatch(int x, int y, int width, int height)
    {
        var dynamicBuilder = new DynamicGumpBuilder();
        var staticBuilder = new StaticGumpBuilder();
        try
        {
            CustomGumpStyle.AddPanel(ref dynamicBuilder, x, y, width, height);
            var expected = Encoding.ASCII.GetString(dynamicBuilder.LayoutData);
            CustomGumpStyle.AddPanel(ref staticBuilder, x, y, width, height);
            Assert.Equal(expected, Encoding.ASCII.GetString(staticBuilder.LayoutData));
            Assert.Equal(3, expected.Split("checkertrans").Length - 1);
            Assert.DoesNotContain("resizepic", expected);
            Assert.Contains($"gumppic {x} {y} 60002", expected);
            Assert.Contains($"gumppic {x + width - 20} {y + height - 20} 60009", expected);
            Assert.Contains($"gumppictiled {x + 17} {y + 17} {width - 34} {height - 34} 2624", expected);
            Assert.True(expected.LastIndexOf("checkertrans", StringComparison.Ordinal) <
                        expected.IndexOf("60002", StringComparison.Ordinal));
        }
        finally
        {
            dynamicBuilder.Dispose();
            staticBuilder.Dispose();
        }
    }

    [Fact]
    public void BannerAndTitleUseSharedCenteredPlacement()
    {
        var dynamicBuilder = new DynamicGumpBuilder();
        var staticBuilder = new StaticGumpBuilder();
        try
        {
            CustomGumpStyle.AddContentPanel(ref dynamicBuilder, 0, 0, 420, 440, "HOUSE MANAGEMENT");
            var expected = Encoding.ASCII.GetString(dynamicBuilder.LayoutData);
            CustomGumpStyle.AddContentPanel(ref staticBuilder, 0, 0, 420, 440, "HOUSE MANAGEMENT");
            // Builders share the string table; the second title gets the next text index.
            Assert.Equal(expected.Replace("230 24 0 0 0", "230 24 1 0 0"), Encoding.ASCII.GetString(staticBuilder.LayoutData));
            Assert.Contains("gumppic 60 -30 64040", expected);
            Assert.Contains("htmlgump 95 -23 230 24", expected);
            Assert.True(expected.IndexOf("64040", StringComparison.Ordinal) > expected.IndexOf("60009", StringComparison.Ordinal));
        }
        finally
        {
            dynamicBuilder.Dispose();
            staticBuilder.Dispose();
        }
    }

    [Fact]
    public void ContentPanelLeavesControlsInsideTheFrame()
    {
        var builder = new DynamicGumpBuilder();
        try
        {
            CustomGumpStyle.AddContentPanel(ref builder, 0, 0, 420, 440);
            var layout = Encoding.ASCII.GetString(builder.LayoutData);
            Assert.Contains("gumppic -24 -24 60002", layout);
            Assert.Contains("gumppic 424 444 60009", layout);
            Assert.Contains("gumppictiled -7 -7 434 454 2624", layout);
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Theory]
    [InlineData(40, 100)]
    [InlineData(100, 40)]
    public void RejectsPanelsTooSmallForCorners(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildPanel(width, height));
    }

    [Theory]
    [InlineData(98, 56, 1)]
    [InlineData(320, 168, 6)]
    public void ReusableMenuButtonsKeepLabelsAboveTheClickableArtwork(int centerX, int labelY, int buttonId)
    {
        var dynamicBuilder = new DynamicGumpBuilder();
        var staticBuilder = new StaticGumpBuilder();
        try
        {
            CustomGumpStyle.AddMenuButton(ref dynamicBuilder, centerX, labelY, buttonId, "Any action");
            CustomGumpStyle.AddMenuButton(ref staticBuilder, centerX, labelY, buttonId, "Any action");
            foreach (var layout in new[]
                     {
                         Encoding.ASCII.GetString(dynamicBuilder.LayoutData),
                         Encoding.ASCII.GetString(staticBuilder.LayoutData)
                     })
            {
                Assert.Contains($"htmlgump {centerX - 66} {labelY} 132 24", layout);
                Assert.Contains($"button {centerX - 28} {labelY + 28} 64042 64043 1 0 {buttonId}", layout);
                Assert.DoesNotContain("64032", layout);
                Assert.DoesNotContain("64034", layout);
            }
        }
        finally
        {
            dynamicBuilder.Dispose();
            staticBuilder.Dispose();
        }
    }

    private static void BuildPanel(int width, int height)
    {
        var builder = new DynamicGumpBuilder();
        try
        {
            CustomGumpStyle.AddPanel(ref builder, 0, 0, width, height);
        }
        finally
        {
            builder.Dispose();
        }
    }
}
