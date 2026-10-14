using ModernUO.Serialization;
using Server.Items;

namespace Server.Engines.ModularDungeon;

[SerializationGenerator(0)]
public partial class EventDungeonHiddenHatch : Teleporter
{
    [CanBeNull]
    [SerializableField(0)]
    private Item _coverBox;

    [SerializableField(1)]
    private Point3D _coverOrigin;

    [SerializableField(2)]
    private Map _coverMap;

    [Constructible]
    public EventDungeonHiddenHatch()
    {
    }

    public EventDungeonHiddenHatch(Map map, Point3D origin, Point3D destination, Item coverBox)
        : base(destination, map, false)
    {
        _coverBox = coverBox;
        _coverOrigin = origin;
        _coverMap = map;
        MoveToWorld(origin, map);
    }

    public bool IsCovered => _coverBox is { Deleted: false, Parent: null } &&
        _coverBox.Map == _coverMap && _coverBox.Location == _coverOrigin;

    public override bool CanTeleport(Mobile from) =>
        !Deleted && Parent == null && Map != null && Map != Map.Internal &&
        from is { Deleted: false, Player: true } && from.Map == Map &&
        !IsCovered && base.CanTeleport(from);

    public override void DoTeleport(Mobile from)
    {
        if (Active && CanTeleport(from))
        {
            base.DoTeleport(from);
        }
    }

    // Teleporter returns false after activation so the current walk cannot overwrite its destination.
    public override bool OnMoveOver(Mobile from) => Deleted || base.OnMoveOver(from);

    public override void OnAfterDelete()
    {
        _coverBox = null;
        _coverMap = null;
        base.OnAfterDelete();
    }
}
