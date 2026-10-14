using System;
using Server.Gumps;
using Server.Network;

namespace Server.Items;

public sealed class MoongateTravelConfirmation : StaticGump<MoongateTravelConfirmation>
{
    private readonly Request _request;
    private readonly string _name;

    public override bool Singleton => true;

    private MoongateTravelConfirmation(Request request, Item source) : base(100, 100)
    {
        _request = request;
        _name = source.Name ?? "Moongate";
    }

    public static void DisplayTo(Mobile from, Item source, Func<Mobile, bool> validate, Action<Mobile> travel)
    {
        var request = CreateRequest(from, source, validate, travel);
        if (request != null)
        {
            from.SendGump(new MoongateTravelConfirmation(request, source));
        }
    }

    internal static Request CreateRequest(Mobile from, Item source, Func<Mobile, bool> validate, Action<Mobile> travel)
    {
        if (from?.NetState == null || validate == null || travel == null || !IsInRange(from, source) || !validate(from))
        {
            return null;
        }
        return new Request(from, source, validate, travel);
    }

    internal static bool IsInRange(Mobile from, Item source) => from != null && !from.Deleted && source != null &&
        !source.Deleted && source.Parent == null && source.Map != null && source.Map != Map.Internal &&
        from.Map == source.Map && from.InRange(source.Location, 1) && Math.Abs(from.Z - source.Z) < 16;

    protected override void BuildLayout(ref StaticGumpBuilder builder)
    {
        builder.AddPage();
        CustomGumpStyle.AddContentPanel(ref builder, 0, 0, 380, 190, "MOONGATE");
        builder.AddLabelPlaceholder(25, 20, CustomGumpStyle.TextHue, "gate");
        builder.AddHtml(25, 55, 330, 65, "Enter this moongate?<br>Enter to travel, or Cancel to stay here.",
            CustomGumpStyle.TextColor, CustomGumpStyle.StandardFontSize);
        builder.AddButton(35, 145, 4005, 4007, 1);
        builder.AddLabel(70, 145, CustomGumpStyle.TextHue, "Enter");
        builder.AddButton(215, 145, 4017, 4019, 0);
        builder.AddLabel(250, 145, CustomGumpStyle.TextHue, "Cancel");
    }

    protected override void BuildStrings(ref GumpStringsBuilder builder) => builder.SetStringSlot("gate", _name);

    public override void OnResponse(NetState sender, in RelayInfo info)
    {
        if (sender?.Mobile?.NetState == sender)
        {
            _request.Respond(sender?.Mobile, info.ButtonID == 1);
        }
    }

    internal sealed class Request
    {
        private readonly Mobile _owner;
        private readonly NetState _state;
        private readonly Item _source;
        private readonly Point3D _location;
        private readonly Map _map;
        private readonly int _itemId;
        private readonly Func<Mobile, bool> _validate;
        private readonly Action<Mobile> _travel;
        private bool _used;

        internal Request(Mobile owner, Item source, Func<Mobile, bool> validate, Action<Mobile> travel)
        {
            _owner = owner;
            _state = owner.NetState;
            _source = source;
            _location = source.Location;
            _map = source.Map;
            _itemId = source.ItemID;
            _validate = validate;
            _travel = travel;
        }

        internal void Respond(Mobile from, bool accepted)
        {
            if (_used)
            {
                return;
            }
            _used = true;
            if (accepted && from == _owner && from.NetState == _state && _state?.Mobile == from &&
                IsInRange(from, _source) && _source.Location == _location && _source.Map == _map && _source.ItemID == _itemId && _validate(from))
            {
                _travel(from);
            }
        }
    }
}
