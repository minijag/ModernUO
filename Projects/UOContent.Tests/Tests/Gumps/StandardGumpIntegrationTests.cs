using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Server.Compression;
using Server.Gumps;
using Server.Network;
using Xunit;

namespace Server.Tests.Gumps;

[Collection("Sequential UOContent Tests")]
public class StandardGumpIntegrationTests
{
    private const string LongLabel = "A long standalone label needs room inside the frame";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryBaseAppliesStandardFrameWithoutChangingControls(int kind)
    {
        BaseGump gump = kind switch
        {
            0 => new DynamicFixture(),
            1 => new StaticFixture(),
            _ => new LegacyFixture()
        };

        var packet = Read(gump);
        AssertSingleFrame(packet.Layout);
        Assert.DoesNotContain("resizepic 0 0 5054 300 220", packet.Layout);
        Assert.Contains("{ page 0 }", packet.Layout);
        Assert.Contains("{ page 2 }", packet.Layout);
        Assert.Contains("{ button 35 140 4005 4007 1 0 7 }", packet.Layout);
        Assert.Contains("{ button 90 140 4017 4019 1 0 0 }", packet.Layout);
        Assert.Contains("{ radio 35 90 210 211 1 47 }", packet.Layout);
        Assert.Contains("{ textentry 70 90 150 24 1153 99", packet.Layout);
        Assert.Contains("{ button 180 140 4005 4007 0 2 0 }", packet.Layout);
        Assert.Equal(1, gump.Switches);
        Assert.Equal(1, gump.TextEntries);
        Assert.Contains(packet.Strings, text => text.Contains("Standard body", StringComparison.Ordinal));
        Assert.Contains("<BASEFONT COLOR=#F2F0E8>Standard body</BASEFONT>", packet.Strings);
        Assert.Contains("Entry value", packet.Strings);

        gump.OnResponse(null, Reply(7));
        Assert.Equal(7, ((IResponseFixture)gump).LastButton);
    }

    [Fact]
    public void CachedStaticLayoutKeepsEachInstancesPlaceholderText()
    {
        var first = Read(new PlaceholderFixture("First destination"));
        var second = Read(new PlaceholderFixture("Second destination"));

        AssertSingleFrame(first.Layout);
        Assert.Equal(first.Layout, second.Layout);
        var match = Regex.Match(first.Layout, @"\{ htmlgump 30 50 230 40 (\d+) 0 0 \}");
        Assert.True(match.Success);
        var index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.InRange(index, 0, first.Strings.Length - 1);
        Assert.InRange(index, 0, second.Strings.Length - 1);
        Assert.Contains("First destination", first.Strings[index]);
        Assert.Contains("Second destination", second.Strings[index]);
        Assert.Equal(1, PlaceholderFixture.LayoutBuilds);
    }

    [Fact]
    public void ExistingCustomFrameIsNotWrappedAgain()
    {
        var packet = Read(new StyledFixture());
        AssertSingleFrame(packet.Layout);
        Assert.Contains("{ gumppic 0 0 60002 }", packet.Layout);
        Assert.Contains("{ gumppic 360 180 60009 }", packet.Layout);
        Assert.Contains("{ gumppic 40 -6 64040 }", packet.Layout);
        Assert.Contains(packet.Strings, text => text.Contains(CustomGumpStyle.TitleColor, StringComparison.Ordinal));
    }

    [Fact]
    public void FoundationPreservesSpecialArtworkSmallFieldsAndQuotedPayloads()
    {
        var layout = Theme(
            "{ page 0 }{ resizepic 0 0 5054 320 240 }{ resizepic 2 2 2620 316 236 }" +
            "{ gumppictiled 10 10 300 220 2624 }{ gumppic 220 20 104 }" +
            "{ resizepic 20 60 3000 180 26 }{ text 25 180 0 0 }{ text 25 65 0 1 }" +
            "{ xmfhtmlgump 20 110 200 50 3000101 0 0 }" +
            "{ tooltip 3000000 @Text with {braces} intact@ }{ button 220 180 4005 4007 1 0 42 }\0");

        AssertSingleFrame(layout);
        Assert.DoesNotContain("{ resizepic 0 0 5054 320 240 }", layout);
        Assert.DoesNotContain("{ resizepic 2 2 2620 316 236 }", layout);
        Assert.DoesNotContain("{ gumppictiled 10 10 300 220 2624 }", layout);
        Assert.Contains("{ gumppic 220 20 104 }", layout);
        Assert.Contains("{ resizepic 20 60 3000 180 26 }", layout);
        Assert.Contains("{ text 25 180 1153 0 }", layout);
        Assert.Contains("{ text 25 65 0 1 }", layout);
        Assert.Contains("{ xmfhtmlgumpcolor 20 110 200 50 3000101 0 0 32767 }", layout);
        Assert.Contains("{ tooltip 3000000 @Text with {braces} intact@ }", layout);
        Assert.Contains("{ button 220 180 4005 4007 1 0 42 }", layout);
        Assert.EndsWith("\0", layout);
    }

    [Fact]
    public void FoundationPreservesSemanticColorsAndBackgroundHtml()
    {
        var layout = Theme(
            "{ page 0 }{ resizepic 0 0 5054 320 240 }{ text 25 30 33 0 }" +
            "{ xmfhtmlgumpcolor 20 65 200 50 3000101 0 0 1234 }" +
            "{ xmfhtmlgump 20 125 200 50 3000101 1 0 }\0");

        AssertSingleFrame(layout);
        Assert.Contains("{ text 25 30 33 0 }", layout);
        Assert.Contains("{ xmfhtmlgumpcolor 20 65 200 50 3000101 0 0 1234 }", layout);
        Assert.Contains("{ xmfhtmlgump 20 125 200 50 3000101 1 0 }", layout);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(0, 50)]
    [InlineData(1, 50)]
    [InlineData(2, 50)]
    public void OutgoingPositionKeepsTheFrameOnscreenWithoutMovingControls(int kind, int origin)
    {
        BaseGump gump = kind switch
        {
            0 => new DynamicFixture(origin, origin),
            1 => new StaticFixture(origin, origin),
            _ => new LegacyFixture(origin, origin)
        };

        var packet = Read(gump);
        Assert.Equal(Math.Max(origin, CustomGumpStyle.ContentPadding), packet.X);
        Assert.Equal(Math.Max(origin, CustomGumpStyle.ContentPadding), packet.Y);
        Assert.Equal(origin, gump.X);
        Assert.Equal(origin, gump.Y);
        Assert.Contains("{ button 35 140 4005 4007 1 0 7 }", packet.Layout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LabelOnlyDialogsFrameTheWholeLabel(int kind)
    {
        BaseGump gump = kind switch
        {
            0 => new DynamicLabelFixture(),
            1 => new StaticLabelFixture(),
            _ => new LegacyLabelFixture()
        };

        var packet = Read(gump);
        AssertSingleFrame(packet.Layout);
        var corner = Regex.Match(packet.Layout, @"\{ gumppic (-?\d+) -?\d+ 60009 \}");
        Assert.True(corner.Success);
        var right = int.Parse(corner.Groups[1].Value, CultureInfo.InvariantCulture) + 20;
        Assert.True(right >= 10 + LongLabel.Length * 6 + CustomGumpStyle.ContentPadding);
        Assert.Contains(LongLabel, packet.Strings);
        Assert.Contains("{ text 10 10 1153", packet.Layout);
    }

    [Fact]
    public void CachedStaticFrameClampsEachOutgoingPosition()
    {
        var first = Read(new StaticLabelFixture());
        var second = Read(new StaticLabelFixture(50, 50));

        Assert.Equal(CustomGumpStyle.ContentPadding - 10, first.X);
        Assert.Equal(CustomGumpStyle.ContentPadding - 10, first.Y);
        Assert.Equal(50, second.X);
        Assert.Equal(50, second.Y);
        Assert.Equal(first.Layout, second.Layout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DefaultHtmlColorFollowsTheUnderlyingBackdrop(int kind)
    {
        BaseGump gump = kind switch
        {
            0 => new DynamicHtmlFixture(),
            1 => new StaticHtmlFixture(),
            _ => new LegacyHtmlFixture()
        };

        var packet = Read(gump);
        Assert.Contains("Inside input", packet.Strings);
        Assert.Contains("On native artwork", packet.Strings);
        Assert.Contains("Own light backing", packet.Strings);
        Assert.Contains("<BASEFONT COLOR=#F2F0E8>On black backing</BASEFONT>", packet.Strings);
    }

    private static string Theme(string raw)
    {
        var writer = new SpanWriter(new byte[0x10000]);
        try
        {
            StandardGumpStyle.Apply(Encoding.ASCII.GetBytes(raw), ref writer);
            return Encoding.ASCII.GetString(writer.Span);
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void AssertSingleFrame(string layout)
    {
        Assert.Equal(1, Regex.Matches(layout, @"\{ gumppic -?\d+ -?\d+ 60002 \}").Count);
        Assert.Equal(1, Regex.Matches(layout, @"\{ gumppic -?\d+ -?\d+ 60009 \}").Count);
        Assert.Contains(" 2624 }", layout);
        Assert.Equal(3, Regex.Matches(layout, @"\{ checkertrans ").Count);
    }

    private static RelayInfo Reply(int buttonId) =>
        new(buttonId, ReadOnlySpan<int>.Empty, ReadOnlySpan<ushort>.Empty, ReadOnlySpan<Range>.Empty, ReadOnlySpan<byte>.Empty);

    private static Packet Read(BaseGump gump)
    {
        var writer = new SpanWriter(new byte[0x10000]);
        try
        {
            gump.Compile(ref writer);
            ReadOnlySpan<byte> remaining = writer.Span[19..];
            var layout = Encoding.ASCII.GetString(ReadPacked(ref remaining));
            var stringCount = BinaryPrimitives.ReadInt32BigEndian(remaining);
            remaining = remaining[4..];
            ReadOnlySpan<byte> stringsData = ReadPacked(ref remaining);
            var strings = new List<string>();
            while (!stringsData.IsEmpty)
            {
                var length = BinaryPrimitives.ReadUInt16BigEndian(stringsData) * 2;
                strings.Add(Encoding.BigEndianUnicode.GetString(stringsData.Slice(2, length)));
                stringsData = stringsData[(length + 2)..];
            }
            Assert.Equal(stringCount, strings.Count);
            Assert.True(remaining.IsEmpty);
            return new Packet(layout, strings.ToArray(), BinaryPrimitives.ReadInt32BigEndian(writer.Span[11..]),
                BinaryPrimitives.ReadInt32BigEndian(writer.Span[15..]));
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static byte[] ReadPacked(ref ReadOnlySpan<byte> data)
    {
        var packedLength = BinaryPrimitives.ReadInt32BigEndian(data);
        if (packedLength == 0)
        {
            data = data[4..];
            return [];
        }

        var unpackedLength = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
        var output = new byte[unpackedLength];
        Assert.Equal(LibDeflateResult.Success, Deflate.Standard.Unpack(output, data.Slice(8, packedLength - 4), out var written));
        Assert.Equal(unpackedLength, written);
        data = data[(packedLength + 4)..];
        return output;
    }

    private readonly record struct Packet(string Layout, string[] Strings, int X, int Y);

    private interface IResponseFixture
    {
        int LastButton { get; }
    }

    private sealed class DynamicFixture(int x = 50, int y = 50) : DynamicGump(x, y), IResponseFixture
    {
        public int LastButton { get; private set; }

        protected override void BuildLayout(ref DynamicGumpBuilder builder)
        {
            builder.AddPage();
            builder.AddBackground(0, 0, 300, 220, 5054);
            builder.AddHtml(30, 35, 230, 40, "Standard body");
            builder.AddRadio(35, 90, 210, 211, true, 47);
            builder.AddTextEntry(70, 90, 150, 24, 1153, 99, "Entry value");
            builder.AddButton(35, 140, 4005, 4007, 7);
            builder.AddButton(90, 140, 4017, 4019, 0);
            builder.AddButton(180, 140, 4005, 4007, 0, GumpButtonType.Page, 2);
            builder.AddPage(2);
            builder.AddLabel(35, 180, 1153, "Second page");
        }

        public override void OnResponse(NetState sender, in RelayInfo info) => LastButton = info.ButtonID;
    }

    private sealed class StaticFixture(int x = 50, int y = 50) : StaticGump<StaticFixture>(x, y), IResponseFixture
    {
        public int LastButton { get; private set; }

        protected override void BuildLayout(ref StaticGumpBuilder builder)
        {
            builder.AddPage();
            builder.AddBackground(0, 0, 300, 220, 5054);
            builder.AddHtml(30, 35, 230, 40, "Standard body");
            builder.AddRadio(35, 90, 210, 211, true, 47);
            builder.AddTextEntry(70, 90, 150, 24, 1153, 99, "Entry value");
            builder.AddButton(35, 140, 4005, 4007, 7);
            builder.AddButton(90, 140, 4017, 4019, 0);
            builder.AddButton(180, 140, 4005, 4007, 0, GumpButtonType.Page, 2);
            builder.AddPage(2);
            builder.AddLabel(35, 180, 1153, "Second page");
        }

        public override void OnResponse(NetState sender, in RelayInfo info) => LastButton = info.ButtonID;
    }

    private sealed class LegacyFixture : Gump, IResponseFixture
    {
        public int LastButton { get; private set; }

        public LegacyFixture(int x = 50, int y = 50) : base(x, y)
        {
            AddPage(0);
            AddBackground(0, 0, 300, 220, 5054);
            AddHtml(30, 35, 230, 40, "Standard body");
            AddRadio(35, 90, 210, 211, true, 47);
            AddTextEntry(70, 90, 150, 24, 1153, 99, "Entry value");
            AddButton(35, 140, 4005, 4007, 7);
            AddButton(90, 140, 4017, 4019, 0);
            AddButton(180, 140, 4005, 4007, 0, GumpButtonType.Page, 2);
            AddPage(2);
            AddLabel(35, 180, 1153, "Second page");
        }

        public override void OnResponse(NetState sender, in RelayInfo info) => LastButton = info.ButtonID;
    }

    private sealed class PlaceholderFixture(string value) : StaticGump<PlaceholderFixture>(50, 50)
    {
        public static int LayoutBuilds { get; private set; }
        protected override bool Cached => true;

        protected override void BuildLayout(ref StaticGumpBuilder builder)
        {
            LayoutBuilds++;
            builder.AddPage();
            builder.AddBackground(0, 0, 300, 180, 5054);
            builder.AddHtmlPlaceholder(30, 50, 230, 40, "destination");
            builder.AddButton(35, 120, 4005, 4007, 1);
        }

        protected override void BuildStrings(ref GumpStringsBuilder builder) =>
            builder.SetStringSlot("destination", value);
    }

    private sealed class StyledFixture() : DynamicGump(50, 50)
    {
        protected override void BuildLayout(ref DynamicGumpBuilder builder)
        {
            builder.AddPage();
            CustomGumpStyle.AddPanel(ref builder, 0, 0, 380, 200, "EXISTING");
            builder.AddHtml(30, 45, 300, 40, "Golden emphasis", CustomGumpStyle.TitleColor);
            builder.AddButton(35, 140, 4005, 4007, 1);
        }
    }

    private sealed class DynamicLabelFixture() : DynamicGump(0, 0)
    {
        protected override void BuildLayout(ref DynamicGumpBuilder builder) => builder.AddLabel(10, 10, 0, LongLabel);
    }

    private sealed class StaticLabelFixture(int x = 0, int y = 0) : StaticGump<StaticLabelFixture>(x, y)
    {
        protected override bool Cached => true;
        protected override void BuildLayout(ref StaticGumpBuilder builder) => builder.AddLabel(10, 10, 0, LongLabel);
    }

    private sealed class LegacyLabelFixture : Gump
    {
        public LegacyLabelFixture() : base(0, 0) => AddLabel(10, 10, 0, LongLabel);
    }

    private sealed class DynamicHtmlFixture() : DynamicGump(50, 50)
    {
        protected override void BuildLayout(ref DynamicGumpBuilder builder)
        {
            builder.AddPage();
            builder.AddBackground(0, 0, 320, 240, 5054);
            builder.AddBackground(20, 60, 180, 26, 3000);
            builder.AddHtml(25, 65, 150, 20, "Inside input");
            builder.AddImage(220, 20, 104);
            builder.AddHtml(221, 21, 60, 20, "On native artwork");
            builder.AddHtml(30, 150, 200, 25, "On black backing");
            builder.AddHtml(30, 180, 200, 25, "Own light backing", background: true);
        }
    }

    private sealed class StaticHtmlFixture() : StaticGump<StaticHtmlFixture>(50, 50)
    {
        protected override void BuildLayout(ref StaticGumpBuilder builder)
        {
            builder.AddPage();
            builder.AddBackground(0, 0, 320, 240, 5054);
            builder.AddBackground(20, 60, 180, 26, 3000);
            builder.AddHtml(25, 65, 150, 20, "Inside input");
            builder.AddImage(220, 20, 104);
            builder.AddHtml(221, 21, 60, 20, "On native artwork");
            builder.AddHtml(30, 150, 200, 25, "On black backing");
            builder.AddHtml(30, 180, 200, 25, "Own light backing", background: true);
        }
    }

    private sealed class LegacyHtmlFixture : Gump
    {
        public LegacyHtmlFixture() : base(50, 50)
        {
            AddPage(0);
            AddBackground(0, 0, 320, 240, 5054);
            AddBackground(20, 60, 180, 26, 3000);
            AddHtml(25, 65, 150, 20, "Inside input");
            AddImage(220, 20, 104);
            AddHtml(221, 21, 60, 20, "On native artwork");
            AddHtml(30, 150, 200, 25, "On black backing");
            AddHtml(30, 180, 200, 25, "Own light backing", true);
        }
    }
}
