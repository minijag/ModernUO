using System;
using System.Collections.Generic;
using ModernUO.Serialization;
using Server.Items;

namespace Server.Engines.ModularDungeon;

[SerializationGenerator(0)]
public partial class ExtractionEventInstallation : Item
{
    [CanBeNull]
    [SerializableField(0, setter: "private")]
    private ExtractionEventController _controller;

    [SerializableField(1, setter: "private")]
    private List<EventDungeonDoor> _doors = [];

    [SerializableField(2, setter: "private")]
    private List<EventDungeonChest> _chests = [];

    [CanBeNull]
    [SerializableField(3, setter: "private")]
    private Item _cover;

    [CanBeNull]
    [SerializableField(4, setter: "private")]
    private EventDungeonHiddenHatch _hatch;

    [CanBeNull]
    [SerializableField(5, setter: "private")]
    private RootboundCellarReturnRune _returnRune;

    [CanBeNull]
    [SerializableField(6, setter: "private")]
    private RootboundCellarLich _lich;

    [SerializableField(7, setter: "private")]
    private bool _lobbyComplete;

    [Constructible]
    public ExtractionEventInstallation() : base(1)
    {
        Visible = false;
        Movable = false;
    }

    public RootboundCellarFeature.Features Features => new()
    {
        Cover = _cover as WoodenBox,
        Hatch = _hatch,
        ReturnRune = _returnRune,
        Lich = _lich
    };

    public void Initialize(
        ExtractionEventController controller,
        EventDungeonDoor[] doors,
        EventDungeonChest[] chests,
        RootboundCellarFeature.Features features
    )
    {
        if (Deleted || _controller != null)
        {
            throw new InvalidOperationException("The installation cannot be rebound.");
        }

        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(chests);
        ValidateFeatures(features);

        if (controller.Deleted)
        {
            throw new ArgumentException("The controller has been deleted.", nameof(controller));
        }

        var identities = new HashSet<Item>();
        foreach (var door in doors)
        {
            if (door == null || door.Deleted || !identities.Add(door))
            {
                throw new ArgumentException("Door ownership must contain distinct live references.", nameof(doors));
            }
        }

        foreach (var chest in chests)
        {
            if (chest == null || chest.Deleted || !identities.Add(chest))
            {
                throw new ArgumentException("Chest ownership must contain distinct live references.", nameof(chests));
            }
        }

        Controller = controller;
        Doors = new List<EventDungeonDoor>(doors);
        Chests = new List<EventDungeonChest>(chests);
        Cover = features.Cover;
        Hatch = features.Hatch;
        ReturnRune = features.ReturnRune;
        Lich = features.Lich is { Deleted: false } ? features.Lich : null;
    }

    public void SyncFeatures(RootboundCellarFeature.Features features)
    {
        if (Deleted || _controller is not { Deleted: false })
        {
            throw new InvalidOperationException("The installation has no live controller.");
        }

        ValidateFeatures(features);
        if (features.Hatch != _hatch || features.ReturnRune != _returnRune)
        {
            throw new ArgumentException("The installation cannot exchange its persistent cellar transports.", nameof(features));
        }

        Cover = features.Cover;
        Lich = features.Lich is { Deleted: false } ? features.Lich : null;
    }

    public void MarkLobbyComplete()
    {
        if (Deleted || _controller is not { Deleted: false })
        {
            throw new InvalidOperationException("The installation has no live controller.");
        }

        LobbyComplete = true;
    }

    private static void ValidateFeatures(RootboundCellarFeature.Features features)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (features.Hatch is not { Deleted: false } || features.ReturnRune is not { Deleted: false })
        {
            throw new ArgumentException("The installation requires its persistent cellar transports.", nameof(features));
        }
    }

    [AfterDeserialization]
    private void AfterDeserialization()
    {
        Visible = false;
        Movable = false;
    }

    public override void OnAfterDelete()
    {
        Controller = null;
        _doors?.Clear();
        _chests?.Clear();
        Cover = null;
        Hatch = null;
        ReturnRune = null;
        Lich = null;
        base.OnAfterDelete();
    }
}
