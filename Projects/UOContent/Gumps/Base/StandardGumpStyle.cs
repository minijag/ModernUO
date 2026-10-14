using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;

namespace Server.Gumps;

/// <summary>Applies the shard's shared foundation without changing a dialog's controls or string slots.</summary>
internal static class StandardGumpStyle
{
    private static readonly Dictionary<int, Point2D> _imageSizes = new();

    internal static ReadOnlySpan<char> GetDefaultHtmlColor(ReadOnlySpan<byte> layout, int x, int y, int width, int height)
    {
        var commands = new Commands(layout);
        var page = 0;
        while (commands.MoveNext(out var command))
        {
            if (command.Is("gumppic"u8) && command.Number(2) == 60002)
            {
                return CustomGumpStyle.TextColor;
            }
            if (command.Is("page"u8))
            {
                page = command.Number(0);
            }
        }
        return HasRetainedBackdrop(layout, x, y, page) ? default : CustomGumpStyle.TextColor;
    }

    internal static Point2D WriteLayout(ReadOnlySpan<byte> layout, ref SpanWriter packetWriter, bool hasVisualElements,
        ReadOnlySpan<byte> strings = default, ReadOnlySpan<byte> dynamicStrings = default)
    {
        if (!hasVisualElements)
        {
            OutgoingGumpPackets.WritePacked(layout, ref packetWriter);
            return Point2D.Zero;
        }

        var styled = new SpanWriter(4096, resize: true);
        try
        {
            Apply(layout, ref styled, out var minimumPosition, strings, dynamicStrings);
            AdjustPosition(ref packetWriter, minimumPosition);
            OutgoingGumpPackets.WritePacked(styled.Span, ref packetWriter);
            return minimumPosition;
        }
        finally
        {
            styled.Dispose();
        }
    }

    internal static void AdjustPosition(ref SpanWriter packetWriter, Point2D minimumPosition)
    {
        var position = packetWriter.RawBuffer.Slice(packetWriter.Position - 8, 8);
        BinaryPrimitives.WriteInt32BigEndian(position,
            Math.Max(BinaryPrimitives.ReadInt32BigEndian(position), minimumPosition.X));
        BinaryPrimitives.WriteInt32BigEndian(position[4..],
            Math.Max(BinaryPrimitives.ReadInt32BigEndian(position[4..]), minimumPosition.Y));
    }

    internal static void Apply(ReadOnlySpan<byte> layout, ref SpanWriter output) =>
        Apply(layout, ref output, out _);

    internal static void Apply(ReadOnlySpan<byte> layout, ref SpanWriter output, out Point2D minimumPosition,
        ReadOnlySpan<byte> strings = default, ReadOnlySpan<byte> dynamicStrings = default)
    {
        minimumPosition = Point2D.Zero;
        var commands = new Commands(layout);
        var bounds = default(Rectangle);
        var hasBounds = false;
        while (commands.MoveNext(out var command))
        {
            if (command.Is("gumppic"u8) && command.Number(2) == 60002)
            {
                output.Write(layout);
                return;
            }

            if (TryGetBounds(command, out var rectangle))
            {
                if (command.Is("text"u8) && !HasLayoutSurface(layout, rectangle.X, rectangle.Y))
                {
                    rectangle = rectangle with { Width = GetLabelWidth(command.Number(3), strings, dynamicStrings) };
                }
                bounds = hasBounds ? bounds.Union(rectangle) : rectangle;
                hasBounds = true;
            }
        }

        if (!hasBounds)
        {
            output.Write(layout);
            return;
        }

        output.Write("{ page 0 }"u8);
        minimumPosition = new Point2D(Math.Max(0, CustomGumpStyle.ContentPadding - bounds.X),
            Math.Max(0, CustomGumpStyle.ContentPadding - bounds.Y));
        CustomGumpStyle.AddPanel(ref output, bounds.X - CustomGumpStyle.ContentPadding,
            bounds.Y - CustomGumpStyle.ContentPadding,
            bounds.Width + CustomGumpStyle.ContentPadding * 2,
            bounds.Height + CustomGumpStyle.ContentPadding * 2);

        commands = new Commands(layout);
        var page = 0;
        while (commands.MoveNext(out var command))
        {
            if (command.Is("page"u8))
            {
                page = command.Number(0);
            }
            if (IsOrdinaryBackdrop(command))
            {
                continue;
            }

            // Original checkertrans would lighten the shared black foundation again.
            if (command.Is("checkertrans"u8) && command.Number(2) >= 80 && command.Number(3) >= 80)
            {
                continue;
            }
            WriteContent(command, layout, page, ref output);
        }
        if (!layout.IsEmpty && layout[^1] == 0)
        {
            output.Write((byte)0);
        }
    }

    private static bool HasLayoutSurface(ReadOnlySpan<byte> layout, int x, int y)
    {
        var commands = new Commands(layout);
        while (commands.MoveNext(out var command))
        {
            if ((command.Is("resizepic"u8) || command.Is("gumppictiled"u8) || command.Is("gumppic"u8)) &&
                TryGetBounds(command, out var rectangle) && rectangle.Width >= 80 && rectangle.Height >= 80 &&
                rectangle.Contains(x, y))
            {
                return true;
            }
        }
        return false;
    }

    private static int GetLabelWidth(int index, ReadOnlySpan<byte> strings, ReadOnlySpan<byte> dynamicStrings)
    {
        for (var buffer = 0; buffer < 2; buffer++)
        {
            var remaining = buffer == 0 ? strings : dynamicStrings;
            while (remaining.Length >= 2)
            {
                var length = BinaryPrimitives.ReadUInt16BigEndian(remaining);
                remaining = remaining[2..];
                if (remaining.Length < length * 2)
                {
                    break;
                }
                if (index-- == 0)
                {
                    var width = 0;
                    for (var i = 0; i < length; i++)
                    {
                        var character = (char)BinaryPrimitives.ReadUInt16BigEndian(remaining[(i * 2)..]);
                        width += character switch
                        {
                            'i' or 'l' or '.' or ',' or ':' or ';' or '!' or '|' or ' ' => 5,
                            >= 'a' and <= 'z' or >= '0' and <= '9' => 9,
                            <= '\u007F' => 12,
                            _ => 16
                        };
                    }
                    return Math.Max(16, width);
                }
                remaining = remaining[(length * 2)..];
            }
        }
        return 80;
    }

    private static bool IsOrdinaryBackdrop(Command command)
    {
        if (command.Is("resizepic"u8))
        {
            return command.Number(3) >= 80 && command.Number(4) >= 80 &&
                   command.Number(2) is 83 or 2520 or 2600 or 2620 or 3000 or 3500 or 3600 or 5054 or
                       5100 or 5120 or 9200 or 9250 or 9260 or 9270 or 9300 or 9350 or 9380;
        }
        return command.Is("gumppictiled"u8) && command.Number(2) >= 80 && command.Number(3) >= 80 &&
               command.Number(4) is 2624 or 3004 or 3504 or 3604 or 5058 or 5104 or 5124 or 9204 or
                   9254 or 9264 or 9274 or 9304 or 9354 or 9384;
    }

    private static void WriteContent(Command command, ReadOnlySpan<byte> layout, int page, ref SpanWriter output)
    {
        var colorIndex = command.Is("text"u8) ? 2 :
            command.Is("croppedtext"u8) || command.Is("textentry"u8) || command.Is("textentrylimited"u8) ? 4 :
            command.Is("xmfhtmlgumpcolor"u8) ? 7 : command.Is("xmfhtmltok"u8) ? 6 : -1;
        var localized = command.Is("xmfhtmlgump"u8) || command.Is("xmfhtmlgumpcolor"u8) || command.Is("xmfhtmltok"u8);
        var htmlBackground = localized && command.Number(command.Is("xmfhtmltok"u8) ? 4 : 5) != 0;
        if (!htmlBackground && (colorIndex >= 0 && command.Number(colorIndex) == 0 || command.Is("xmfhtmlgump"u8)) &&
            !HasRetainedBackdrop(layout, command.Number(0), command.Number(1), page))
        {
            if (command.Is("xmfhtmlgump"u8))
            {
                output.WriteAscii($"{{ xmfhtmlgumpcolor {command.Number(0)} {command.Number(1)} {command.Number(2)} {command.Number(3)} {command.Number(4)} {command.Number(5)} {command.Number(6)} {CustomGumpStyle.LocalizedTextColor} }}");
                return;
            }
            command.WriteReplacingNumber(colorIndex,
                localized ? CustomGumpStyle.LocalizedTextColor : CustomGumpStyle.TextHue, ref output);
            return;
        }
        output.Write(command.Raw);
    }

    private static bool HasRetainedBackdrop(ReadOnlySpan<byte> layout, int x, int y, int targetPage)
    {
        var commands = new Commands(layout);
        var page = 0;
        while (commands.MoveNext(out var command))
        {
            if (command.Is("page"u8))
            {
                page = command.Number(0);
            }
            if (page != 0 && page != targetPage || IsOrdinaryBackdrop(command))
            {
                continue;
            }
            if ((command.Is("resizepic"u8) || command.Is("gumppictiled"u8) || command.Is("gumppic"u8)) &&
                TryGetBounds(command, out var rectangle) && rectangle.Contains(x, y))
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryGetBounds(Command command, out Rectangle bounds)
    {
        var x = command.Number(0);
        var y = command.Number(1);
        var width = 0;
        var height = 0;
        if (command.Is("resizepic"u8))
        {
            width = command.Number(3);
            height = command.Number(4);
        }
        else if (command.Is("gumppictiled"u8) || command.Is("htmlgump"u8) || command.Is("xmfhtmlgump"u8) ||
                 command.Is("xmfhtmlgumpcolor"u8) || command.Is("xmfhtmltok"u8) || command.Is("croppedtext"u8) ||
                 command.Is("textentry"u8) || command.Is("textentrylimited"u8))
        {
            width = command.Number(2);
            height = command.Number(3);
        }
        else if (command.Is("gumppic"u8) || command.Is("button"u8) || command.Is("checkbox"u8) ||
                 command.Is("radio"u8) || command.Is("buttontileart"u8))
        {
            var size = GetImageSize(command.Number(2));
            width = size.X;
            height = size.Y;
        }
        else if (command.Is("picinpic"u8))
        {
            width = command.Number(3);
            height = command.Number(4);
        }
        else if (command.Is("tilepic"u8) || command.Is("tilepichue"u8))
        {
            var itemId = command.Number(2);
            if ((uint)itemId < ItemBounds.Sizes.Length)
            {
                (width, height) = ItemBounds.Sizes[itemId];
            }
        }
        else if (command.Is("text"u8))
        {
            width = 16;
            height = 24;
        }
        bounds = new Rectangle(x, y, Math.Max(1, width), Math.Max(1, height));
        return width > 0 || height > 0;
    }

    private static Point2D GetImageSize(int id)
    {
        if (_imageSizes.TryGetValue(id, out var size))
        {
            return size;
        }
        size = new Point2D(64, 64);
        try
        {
            var indexPath = Core.FindDataFile("gumpidx.mul", false);
            if (id >= 0 && indexPath != null)
            {
                using var stream = File.OpenRead(indexPath);
                var offset = (long)id * 12;
                if (offset + 12 <= stream.Length)
                {
                    stream.Position = offset;
                    Span<byte> entry = stackalloc byte[12];
                    stream.ReadExactly(entry);
                    var dimensions = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
                    var width = (int)(dimensions >> 16);
                    var height = (int)(dimensions & 0xFFFF);
                    if (BinaryPrimitives.ReadInt32LittleEndian(entry) >= 0 && width > 0 && height > 0)
                    {
                        size = new Point2D(width, height);
                    }
                }
            }
        }
        catch (IOException)
        {
            // Artwork metadata is optional; the dialog must remain usable if client data is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // Artwork metadata is optional; the dialog must remain usable if client data is unavailable.
        }
        _imageSizes[id] = size;
        return size;
    }

    private readonly record struct Rectangle(int X, int Y, int Width, int Height)
    {
        public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
        public Rectangle Union(Rectangle other)
        {
            var x = Math.Min(X, other.X);
            var y = Math.Min(Y, other.Y);
            return new Rectangle(x, y, Math.Max(X + Width, other.X + other.Width) - x,
                Math.Max(Y + Height, other.Y + other.Height) - y);
        }
    }

    private ref struct Commands(ReadOnlySpan<byte> layout)
    {
        private ReadOnlySpan<byte> _remaining = layout;

        public bool MoveNext(out Command command)
        {
            var start = _remaining.IndexOf((byte)'{');
            if (start < 0)
            {
                command = default;
                return false;
            }
            var quoted = false;
            for (var i = start + 1; i < _remaining.Length; i++)
            {
                if (_remaining[i] == '@')
                {
                    quoted = !quoted;
                }
                else if (_remaining[i] == '}' && !quoted)
                {
                    command = new Command(_remaining[start..(i + 1)]);
                    _remaining = _remaining[(i + 1)..];
                    return true;
                }
            }
            command = default;
            return false;
        }
    }

    private readonly ref struct Command
    {
        internal readonly ReadOnlySpan<byte> Raw;
        private readonly ReadOnlySpan<byte> _name;
        private readonly int _argumentsOffset;

        public Command(ReadOnlySpan<byte> raw)
        {
            Raw = raw;
            var start = 1;
            while (start < raw.Length && raw[start] == ' ')
            {
                start++;
            }
            var end = start;
            while (end < raw.Length && raw[end] != ' ' && raw[end] != '}')
            {
                end++;
            }
            _name = raw[start..end];
            _argumentsOffset = end;
        }

        public bool Is(ReadOnlySpan<byte> name) => _name.SequenceEqual(name);
        public int Number(int index)
        {
            var (start, length) = Argument(index);
            return Utf8Parser.TryParse(Raw.Slice(start, length), out int result, out _) ? result : 0;
        }

        public void WriteReplacingNumber(int index, int value, ref SpanWriter writer)
        {
            var (start, length) = Argument(index);
            writer.Write(Raw[..start]);
            writer.WriteAscii($"{value}");
            writer.Write(Raw[(start + length)..]);
        }

        private (int Start, int Length) Argument(int index)
        {
            var start = _argumentsOffset;
            for (var i = 0; i <= index; i++)
            {
                while (start < Raw.Length && Raw[start] == ' ')
                {
                    start++;
                }
                var end = start;
                while (end < Raw.Length && Raw[end] != ' ' && Raw[end] != '}')
                {
                    end++;
                }
                if (i == index)
                {
                    return (start, end - start);
                }
                start = end;
            }
            return (0, 0);
        }
    }
}
