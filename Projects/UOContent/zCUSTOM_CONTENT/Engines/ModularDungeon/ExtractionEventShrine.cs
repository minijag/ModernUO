using System;
using System.Collections.Generic;
using ModernUO.Serialization;
using Server.Collections;
using Server.Items;
using Server.Network;

namespace Server.Engines.ModularDungeon;

internal readonly record struct ExtractionShrineComponent(int ItemId, Point3D Offset, int Hue, LightType Light)
{
    public int ItemID => ItemId;
    public int X => Offset.X;
    public int Y => Offset.Y;
    public int Z => Offset.Z;
}

[SerializationGenerator(2)]
public partial class ExtractionEventShrine : BaseAddon
{
    public const int ChannelMilliseconds = 12000;
    public const int ValidationMilliseconds = 100;
    public const int RedFlameHue = 0x28;
    public const int RedPortalHue = 0x21;
    public const int FlameHeight = 8;
    internal const int LegacyBlueFlameHue = 0x480;
    public const int RitualSound = 0x1FE;
    private const int FlameItemId = 0x19AB;

    private static readonly ExtractionShrineComponent[] _appearance =
    [
        new(0x120E, new(-1, -1, 0), 0, LightType.Empty),
        new(0x120F, new(0, -1, 0), 0, LightType.Empty),
        new(0x1210, new(1, -1, 0), 0, LightType.Empty),
        new(0x1215, new(-1, 0, 0), 0, LightType.Empty),
        new(0x1216, new(0, 0, 0), 0, LightType.Empty),
        new(0x1211, new(1, 0, 0), 0, LightType.Empty),
        new(0x1214, new(-1, 1, 0), 0, LightType.Empty),
        new(0x1213, new(0, 1, 0), 0, LightType.Empty),
        new(0x1212, new(1, 1, 0), 0, LightType.Empty),
        new(FlameItemId, new(0, 0, FlameHeight), RedFlameHue, LightType.Circle300),
        new(0x1854, new(-1, -1, 0), 0, LightType.Circle150),
        new(0x1858, new(1, -1, 0), 0, LightType.Circle150),
        new(0x1858, new(-1, 1, 0), 0, LightType.Circle150),
        new(0x1854, new(1, 1, 0), 0, LightType.Circle150),
        new(0xECC, new(-2, 0, 0), 0, LightType.Empty),
        new(0xECE, new(-2, 1, 0), 0, LightType.Empty),
        new(0xECE, new(2, -1, 0), 0, LightType.Empty),
        new(0xECC, new(2, 1, 0), 0, LightType.Empty)
    ];

    [CanBeNull]
    [SerializableField(0, setter: "private", fieldChanged: nameof(OnControllerChanged))]
    private ExtractionEventController _controller;

    [SerializableField(1, setter: "private")]
    private int _summonedRound;

    [SerializableField(2, setter: "private")]
    private bool _hasSummoned;

    [CanBeNull]
    [SerializableField(3, setter: "private")]
    private ExtractionShrinePortal _portal;

    // Reserved field slots keep existing v2 world saves compatible; flame rituals do not use rune progress.
    [SerializableField(4, setter: "private")]
    private int _runeMask;

    [SerializableField(5, setter: "private")]
    private int _runeRound;

    private readonly Dictionary<Mobile, Channel> _channels = [];
    private TimerExecutionToken _validationTimer;
    private long _nextPulseTick;

    internal static IReadOnlyList<ExtractionShrineComponent> Appearance { get; } = Array.AsReadOnly(_appearance);
    internal static IReadOnlyList<ExtractionShrineComponent> ComponentLayout => Appearance;

    [Constructible(AccessLevel.Administrator)]
    public ExtractionEventShrine() : this(null, false)
    {
    }

    private ExtractionEventShrine(ExtractionEventController controller, bool bind)
    {
        foreach (var definition in _appearance)
        {
            AddComponent(new AddonComponent(definition.ItemId)
            {
                Hue = definition.Hue,
                Light = definition.Light,
                Name = "an extraction flame shrine"
            }, definition.Offset.X, definition.Offset.Y, definition.Offset.Z);
        }

        if (bind)
        {
            Controller = controller;
        }
        SyncFlameVisibility();
    }

    public ExtractionEventShrine(ExtractionEventController controller) : this(ValidateController(controller), true)
    {
    }

    private static ExtractionEventController ValidateController(ExtractionEventController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (controller.Deleted)
        {
            throw new ArgumentException("The controller has been deleted.", nameof(controller));
        }

        return controller;
    }

    public override string DefaultName => "an extraction flame shrine";
    public override bool ShareHue => false;
    public override bool HandlesOnMovement => true;
    internal int ActiveChannels => _channels.Count;

    public bool ExactAppearance => HasExactAppearance(false, RedFlameHue, FlameHeight);
    internal bool ExactLegacyAppearance => HasExactAppearance(false, LegacyBlueFlameHue, 0);
    internal bool ExactPreviousRedAppearance => HasExactAppearance(false, RedPortalHue, 0);
    internal bool ExactPreviousFlameAppearance => ExactAppearance;
    internal bool ExactPreviousRuneAppearance => HasExactAppearance(true, 0, 0);
    internal bool HasSummonedForRound(int roundNumber) => _hasSummoned && _summonedRound == roundNumber;

    private bool HasExactAppearance(bool previousRunes, int flameHue, int flameHeight)
    {
        if (Deleted || Parent != null || Movable || Visible || ItemID != 1 ||
            Components == null || Components.Count != (previousRunes ? 21 : _appearance.Length))
        {
            return false;
        }

        for (var index = 0; index < Components.Count; index++)
        {
            var expected = index switch
            {
                9 when previousRunes => new ExtractionShrineComponent(0xE5C, new(-1, 0, 1), 0, LightType.Circle150),
                18 when previousRunes => new ExtractionShrineComponent(0xE5F, new(0, -1, 1), 0, LightType.Circle150),
                19 when previousRunes => new ExtractionShrineComponent(0xE62, new(1, 0, 1), 0, LightType.Circle150),
                20 when previousRunes => new ExtractionShrineComponent(0xE65, new(0, 1, 1), 0, LightType.Circle150),
                9 => new ExtractionShrineComponent(FlameItemId, new(0, 0, flameHeight), flameHue, LightType.Circle300),
                _ => _appearance[index]
            };
            var offset = expected.Offset;
            var actual = Components[index];
            if (actual is not { Deleted: false, Movable: false, Parent: null } ||
                !actual.Visible && (index != 9 || previousRunes) ||
                actual.GetType() != typeof(AddonComponent) || actual.Addon != this ||
                actual.Offset != offset || actual.ItemID != expected.ItemId ||
                actual.Hue != expected.Hue || actual.Light != expected.Light || actual.Map != Map ||
                actual.Location != new Point3D(X + offset.X, Y + offset.Y, Z + offset.Z))
            {
                return false;
            }
        }

        return true;
    }

    internal bool UpgradeLegacyAppearance()
    {
        if (!ExactLegacyAppearance && !ExactPreviousRedAppearance && !ExactPreviousRuneAppearance)
        {
            return false;
        }
        CancelAll();
        for (var index = Components.Count - 1; index >= 18; index--)
        {
            var component = Components[index];
            Components.RemoveAt(index);
            component.Addon = null;
            component.Delete();
        }
        var flame = _appearance[9];
        Components[9].ItemID = flame.ItemId;
        Components[9].Name = "an extraction flame";
        Components[9].Hue = flame.Hue;
        Components[9].Light = flame.Light;
        Components[9].Offset = flame.Offset;
        Components[9].MoveToWorld(new Point3D(X + flame.X, Y + flame.Y, Z + flame.Z), Map);
        SyncFlameVisibility();
        return ExactAppearance;
    }

    internal void SyncFlameVisibility()
    {
        // The flame alone is a policy-controlled visual; every other native component remains visible.
        if (ExactAppearance)
        {
            Components[9].Visible = _controller?.ExtractionUnlocked == true;
        }
    }

    public override void OnComponentUsed(AddonComponent component, Mobile from)
    {
        if (component?.Addon != this || !Components.Contains(component))
        {
            return;
        }
        if (!ExactAppearance)
        {
            Notify(from, "This extraction shrine is unavailable.");
            return;
        }
        if (!TryBegin(from, out var error))
        {
            Notify(from, error);
        }
    }

    public override void OnDoubleClick(Mobile from)
    {
        if (!TryBegin(from, out var error))
        {
            Notify(from, error);
        }
    }

    public bool TryBegin(Mobile player, out string error)
    {
        if (!ExtractionEventRuntime.CanUseShrine(this, player, out error))
        {
            return false;
        }

        if (_channels.ContainsKey(player))
        {
            error = "You are already performing the extraction ritual.";
            return false;
        }

        if (HasSummonedForRound(_controller.RoundNumber))
        {
            error = _portal is { Deleted: false }
                ? "The red extraction moongate is already open for everyone in this match."
                : "This shrine has already summoned this round's moongate.";
            return false;
        }

        var now = Core.TickCount;
        _channels.Add(player, new Channel(player.Location, player.Map, now + ChannelMilliseconds, _controller.RoundNumber));
        player.SendMessage("Stand on this tile for 12 seconds to summon the extraction moongate. Moving cancels; taking damage does not.");
        player.PrivateOverheadMessage(MessageType.Regular, RedPortalHue, false, "Summoning: 12 seconds", player.NetState);
        Components[9].PublicOverheadMessage(MessageType.Regular, RedPortalHue, false,
            $"{player.Name} begins an extraction ritual.");
        Effects.PlaySound(this, RitualSound);

        if (!_validationTimer.Running)
        {
            _nextPulseTick = now;
            var interval = TimeSpan.FromMilliseconds(ValidationMilliseconds);
            Timer.StartTimer(interval, interval, Advance, out _validationTimer);
        }

        error = null;
        return true;
    }

    internal void Advance()
    {
        using var snapshot = PooledRefList<Mobile>.Create();
        foreach (var player in _channels.Keys)
        {
            snapshot.Add(player);
        }

        var now = Core.TickCount;
        var maximumProgress = 0;
        foreach (var player in snapshot)
        {
            if (!_channels.TryGetValue(player, out var channel))
            {
                continue;
            }

            if (player.Location != channel.Start || player.Map != channel.Map)
            {
                Cancel(player, "You moved; the extraction ritual was canceled.");
                continue;
            }

            if (_controller == null || _controller.RoundNumber != channel.RoundNumber)
            {
                Cancel(player, "The match changed; the extraction ritual was canceled.");
                continue;
            }

            if (!ExtractionEventRuntime.CanUseShrine(this, player, out var error))
            {
                Cancel(player, error);
                continue;
            }

            var remaining = channel.Deadline - now;
            if (remaining <= 0)
            {
                _channels.Remove(player);
                if (ExtractionEventRuntime.TrySummonShrinePortal(this, player, out error))
                {
                    Effects.SendLocationEffect(Location, Map, 0x3728, 10, 10, RedPortalHue);
                    Effects.PlaySound(Location, Map, RitualSound);
                    Components[9].PublicOverheadMessage(MessageType.Regular, RedPortalHue, false,
                        "The shared extraction moongate is open.");
                }
                else
                {
                    Notify(player, error);
                }

                continue;
            }

            var seconds = (int)((remaining + 999) / 1000);
            if (seconds != channel.DisplayedSeconds)
            {
                channel.DisplayedSeconds = seconds;
                player.PrivateOverheadMessage(MessageType.Regular, RedPortalHue, false,
                    $"Summoning: {seconds} seconds", player.NetState);
            }

            maximumProgress = Math.Max(maximumProgress, ChannelMilliseconds - (int)remaining);
        }

        if (_channels.Count == 0)
        {
            _validationTimer.Cancel();
        }
        else if (now - _nextPulseTick >= 0)
        {
            Effects.SendLocationEffect(new Point3D(X, Y, Z + FlameHeight), Map, FlameItemId,
                3 + maximumProgress / 3000, 10, RedFlameHue);
            _nextPulseTick = now + 1000;
        }
    }

    public override void OnMovement(Mobile player, Point3D oldLocation)
    {
        NotifyPlayerMoved(player, oldLocation);
        base.OnMovement(player, oldLocation);
    }

    internal void NotifyPlayerMoved(Mobile player, Point3D oldLocation)
    {
        if (_channels.TryGetValue(player, out var channel) &&
            (oldLocation != player.Location || player.Location != channel.Start || player.Map != channel.Map))
        {
            Cancel(player, "You moved; the extraction ritual was canceled.");
        }
    }

    private void Cancel(Mobile player, string reason)
    {
        if (_channels.Remove(player))
        {
            Notify(player, reason);
        }

        if (_channels.Count == 0)
        {
            _validationTimer.Cancel();
        }
    }

    internal void CancelAll()
    {
        _validationTimer.Cancel();
        _channels.Clear();
    }

    internal bool TryCreatePortal(out string error)
    {
        if (HasSummonedForRound(_controller.RoundNumber))
        {
            error = "This shrine has already summoned this round's moongate.";
            return false;
        }

        DismissPortal();
        var portal = new ExtractionShrinePortal(this, _controller.RoundNumber);
        try
        {
            portal.MoveToWorld(Location, Map);
            Portal = portal;
            SummonedRound = _controller.RoundNumber;
            HasSummoned = true;
            CancelAll();
            portal.Resume();
            error = null;
            return true;
        }
        catch
        {
            portal.Delete();
            throw;
        }
    }

    internal void OnEventPhaseChanged(ExtractionEventController controller)
    {
        SyncFlameVisibility();
        if (_controller != controller || controller.Phase != ExtractionEventPhase.Active ||
            Core.Now >= controller.MatchDeadline ||
            _portal != null && _portal.RoundNumber != controller.RoundNumber)
        {
            CancelAll();
            DismissPortal();
        }
        else
        {
            RestorePortal();
        }
    }

    internal void RestorePortal()
    {
        if (_portal == null)
        {
            return;
        }

        if (ExtractionEventRuntime.IsOwnedActiveShrinePortal(_portal))
        {
            _portal.Visible = _controller.ExtractionUnlocked;
            if (_controller.ExtractionUnlocked)
            {
                _portal.Resume();
            }
            else
            {
                _portal.Suspend();
            }
        }
        else if (ExtractionEventRuntime.HasBoundController(_controller))
        {
            DismissPortal();
        }
    }

    internal void Suspend()
    {
        CancelAll();
        _portal?.Suspend();
    }

    internal void OwnerRemoved()
    {
        CancelAll();
        DismissPortal();
        Controller = null;
    }

    private void DismissPortal()
    {
        var portal = _portal;
        Portal = null;
        portal?.Delete();
    }

    internal void NotifyPortalDeleted(ExtractionShrinePortal portal)
    {
        if (_portal == portal)
        {
            Portal = null;
        }
    }

    private static void Notify(Mobile player, string message)
    {
        if (player is { Deleted: false, NetState: not null } && !string.IsNullOrEmpty(message))
        {
            player.SendMessage(message);
        }
    }

    private void OnControllerChanged(ExtractionEventController oldValue, ExtractionEventController newValue)
    {
        CancelAll();
        DismissPortal();
        SyncFlameVisibility();
    }

    [AfterDeserialization]
    private void AfterDeserialization()
    {
        CancelAll();
        SyncFlameVisibility();
    }

    private void MigrateFrom(V0Content content)
    {
        _controller = content.Controller;
    }

    private void MigrateFrom(V1Content content)
    {
        _controller = content.Controller;
        _summonedRound = content.SummonedRound;
        _hasSummoned = content.HasSummoned;
        _portal = content.Portal;
    }

    public override void OnLocationChange(Point3D oldLocation)
    {
        CancelAll();
        DismissPortal();
        base.OnLocationChange(oldLocation);
    }

    public override void OnMapChange()
    {
        CancelAll();
        DismissPortal();
        base.OnMapChange();
    }

    public override void OnAdded(IEntity parent)
    {
        CancelAll();
        DismissPortal();
        base.OnAdded(parent);
    }

    public override void OnDelete()
    {
        CancelAll();
        DismissPortal();
        base.OnDelete();
    }

    public override void OnAfterDelete()
    {
        CancelAll();
        DismissPortal();
        Controller = null;
        base.OnAfterDelete();
    }

    private sealed class Channel(Point3D start, Map map, long deadline, int roundNumber)
    {
        public Point3D Start { get; } = start;
        public Map Map { get; } = map;
        public long Deadline { get; } = deadline;
        public int RoundNumber { get; } = roundNumber;
        public int DisplayedSeconds { get; set; } = 12;
    }
}
