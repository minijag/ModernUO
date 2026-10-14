using ModernUO.Serialization;

namespace Server.Engines.TownStruggle;

[SerializationGenerator(0)]
public partial class TownStruggleControlMarker : Item
{
    [CanBeNull]
    [SerializableField(0, setter: "private")] private TownStruggleController _controller;
    [SerializableField(1, setter: "private")] private int _zone;
    [SerializableField(2, setter: "private")] private int _corner;

    [Constructible(AccessLevel.Administrator)]
    public TownStruggleControlMarker() : base(0x1F14)
    {
        Movable = false;
        Visible = false;
    }
    public TownStruggleControlMarker(TownStruggleController controller, int zone, int corner = 0) : this()
    {
        Controller = controller;
        Zone = zone;
        Corner = corner;
        if (corner != 0) { ItemID = 5643; }
        UpdateAppearance();
    }
    public override string DefaultName => "Town Struggle control point";
    public Point3D GetLocation(TownStruggleVenue venue)
    {
        var p = venue.CapturePoints[Zone];
        var offset = TownStruggleVenue.CaptureRadius;
        return Corner switch
        {
            1 => new Point3D(p.X - offset, p.Y - offset, p.Z),
            2 => new Point3D(p.X + offset, p.Y - offset, p.Z),
            3 => new Point3D(p.X - offset, p.Y + offset, p.Z),
            4 => new Point3D(p.X + offset, p.Y + offset, p.Z),
            _ => p
        };
    }
    internal void UpdateAppearance()
    {
        Visible = Controller is { Deleted: false, Phase: TownStrugglePhase.Active } && Zone is >= 0 and < 3;
        if (Visible)
        {
            var state = Controller.CaptureStates[Zone];
            Hue = state.Owner switch { TownStruggleTeam.Order => 0x489, TownStruggleTeam.Chaos => 0x21, _ => 0 };
            var label = $"Capture {Zone + 1}: {state.Owner} ({System.Math.Abs(state.Progress)}%)";
            if (Name != label) { Name = label; }
        }
    }
    public override void OnDoubleClick(Mobile from)
    {
        if (Visible && from.Map == Map && from.InRange(Location, 12)) { TownStruggleGump.DisplayTo(from); }
    }
    public override void OnAfterDelete()
    {
        Controller = null;
        base.OnAfterDelete();
    }
}
