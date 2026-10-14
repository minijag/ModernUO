using ModernUO.Serialization;
using Server.Items;

namespace Server.Engines.ModularDungeon;

[SerializationGenerator(0)]
public partial class RootboundCellarReturnRune : Teleporter
{
    [SerializableField(0)]
    private Point3D _hatchOrigin;

    [Constructible]
    public RootboundCellarReturnRune() : this(Point3D.Zero)
    {
    }

    public RootboundCellarReturnRune(Point3D destination, Map map = null, Point3D hatchOrigin = default)
        : base(destination, map, false)
    {
        ItemID = 0x1822;
        Visible = true;
        Name = "a runed exit tile";
        _hatchOrigin = hatchOrigin;
    }

    public override bool CanTeleport(Mobile from) =>
        !Deleted && Parent == null && Map != null && Map != Map.Internal &&
        from is { Deleted: false, Player: true } && from.Map == Map && base.CanTeleport(from);

    public override void DoTeleport(Mobile from)
    {
        if (!Active || !CanTeleport(from))
        {
            return;
        }

        if (!TryFindReturnPoint(out var destination))
        {
            from.SendMessage("The exit is obstructed. Clear the ground around the hidden hatch.");
            return;
        }

        var preferred = PointDest;
        try
        {
            // Native Teleporter also moves following pets and emits its configured effects.
            PointDest = destination;
            base.DoTeleport(from);
        }
        finally
        {
            PointDest = preferred;
        }
    }

    public bool TryFindReturnPoint(out Point3D destination)
    {
        var preferred = PointDest;
        var map = MapDest;
        if (map != null && map != Map.Internal)
        {
            for (var radius = 0; radius <= 2; ++radius)
            {
                for (var dx = -radius; dx <= radius; ++dx)
                {
                    for (var dy = -radius; dy <= radius; ++dy)
                    {
                        if (radius > 0 && System.Math.Abs(dx) != radius && System.Math.Abs(dy) != radius)
                        {
                            continue;
                        }

                        var candidate = new Point3D(preferred.X + dx, preferred.Y + dy, preferred.Z);
                        if (candidate != _hatchOrigin && map.CanFit(candidate, 16, true, false))
                        {
                            destination = candidate;
                            return true;
                        }
                    }
                }
            }
        }

        destination = Point3D.Zero;
        return false;
    }

    public override bool OnMoveOver(Mobile from) => Deleted || base.OnMoveOver(from);
}
