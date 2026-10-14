using System;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Engines.TownStruggle;
using Server.Items;
using Server.Mobiles;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

[Collection("Sequential UOContent Tests")]
public class TownStruggleInteractionTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly DateTime _originalTime = Core.Now;
    private readonly TownStruggleController _previous = TownStruggleRuntime.Controller;
    private readonly TownStruggleController _controller;
    private readonly PlayerMobile _player;
    private readonly PlayerMobile _ally;
    private readonly PlayerMobile _outsider;

    public TownStruggleInteractionTests()
    {
        Timer.Init(Core.TickCount);
        SetController(null);
        _controller = Track(new TownStruggleController());
        const string json = """
            {"name":"Interactions","mapId":0,"x":5800,"y":3000,"width":100,"height":100,
             "neutralBase":{"x":100,"y":100,"z":0},
             "orderBase":{"x":5810,"y":3010,"z":0},"chaosBase":{"x":5890,"y":3090,"z":0},
             "capturePoints":[{"x":5830,"y":3030,"z":0},{"x":5850,"y":3050,"z":0},{"x":5870,"y":3070,"z":0}],
             "respawnPoints":[{"x":5810,"y":3090,"z":0},{"x":5890,"y":3010,"z":0},{"x":5870,"y":3030,"z":0}]}
            """;
        _controller.ConfigureVenue(json, new Host(), false);
        SetController(_controller);
        _player = Player();
        _ally = Player();
        _outsider = Player();
        Assert.True(_controller.TryOpenSignup(out _));
        Assert.True(_controller.TryJoin(_player, out _));
        Assert.True(_controller.TryJoin(_ally, out _));
        Core._now = _controller.SignupDeadline;
        _controller.Advance();
        foreach (var entry in _controller.Participants)
        {
            _created.Add(entry);
            entry.Team = TownStruggleTeam.Order;
            entry.Player.MoveToWorld(new Point3D(5850, 3050, 0), Map.Felucca);
        }
    }

    [Fact]
    public void OutsiderTradesAndDirectGiftsAreBlockedInBothDirections()
    {
        var gold = Track(new Gold());
        Assert.False(_player.CheckTrade(_outsider, null, null, false, false, 0, 0));
        Assert.False(_outsider.CheckTrade(_player, null, null, false, false, 0, 0));
        Assert.False(_player.OnDroppedItemToMobile(gold, _outsider));
        Assert.False(_outsider.OnDroppedItemToMobile(gold, _player));
        Assert.False(_outsider.OnDroppedItemToItem(gold, _player.Backpack, Point3D.Zero));
        Assert.False(_player.CheckNonlocalDrop(_outsider, gold, _player.Backpack));
    }

    [Fact]
    public void TeamExchangeRequiresBothPlayersStillEligibleInsideBattlefield()
    {
        Assert.True(TownStruggleInteractions.CanExchange(_player, _ally));
        _controller.GetParticipant(_ally).Disqualified = true;
        Assert.False(TownStruggleInteractions.CanExchange(_player, _ally));
        _controller.GetParticipant(_ally).Disqualified = false;
        _ally.MoveToWorld(new Point3D(5901, 3050, 0), Map.Felucca);
        Assert.False(TownStruggleInteractions.CanExchange(_player, _ally));
    }

    [Fact]
    public void GroundAndUnrelatedContainerResupplyAreBlockedButCarriedItemsWork()
    {
        var gold = Track(new Gold());
        gold.MoveToWorld(_player.Location, _player.Map);
        Assert.False(_player.OnDragLift(gold));
        Assert.True(_outsider.OnDragLift(gold));
        var box = Track(new WoodenBox());
        box.DropItem(gold);
        Assert.False(_player.OnDragLift(gold));
        _player.Backpack.DropItem(box);
        Assert.True(_player.OnDragLift(gold));
        Assert.True(_player.OnDroppedItemToItem(gold, box, Point3D.Zero));
        Assert.False(_player.OnDroppedItemToWorld(gold, _player.Location));
    }

    [Fact]
    public void BankCannotBeUsedToResupplyOrProtectEventEquipment()
    {
        var gold = Track(new Gold());
        var bank = _player.BankBox;
        bank.DropItem(gold);
        Assert.False(_player.OnDragLift(gold));
        Assert.False(_player.OnDroppedItemToItem(gold, bank, Point3D.Zero));
    }

    [Fact]
    public void OrdinaryBystanderInteractionsRemainUnchanged()
    {
        var other = Player();
        var gold = Track(new Gold());
        Assert.True(TownStruggleInteractions.CanExchange(_outsider, other));
        Assert.True(_outsider.OnDroppedItemToMobile(gold, other));
        Assert.True(_outsider.OnDroppedItemToItem(gold, other.Backpack, Point3D.Zero));
    }

    [Fact]
    public void ProductionOutsideReturnFindsSupportedTileOutsideGalvenAndSharedLobby()
    {
        var hostType = typeof(TownStruggleRuntime).GetNestedType("Host", BindingFlags.NonPublic)!;
        var host = (ITownStruggleHost)Activator.CreateInstance(hostType, true)!;
        var venue = new TownStruggleVenue("Galven", Map.Felucca, new Rectangle2D(1145, 1273, 276, 167),
            new Point3D(5396, 820, 0), new Point3D(1181, 1340, 0), new Point3D(1386, 1354, 0), [], []);
        Assert.True(host.MoveOutside(_outsider, venue, new Point3D(1331, 1390, 0), Map.Felucca));
        Assert.Same(Map.Internal, _outsider.Map);
        Assert.Same(Map.Felucca, _outsider.LogoutMap);
        Assert.False(venue.Area.Contains(_outsider.LogoutLocation));
        Assert.False(Server.Engines.ModularDungeon.ExtractionDungeonLayout.LobbyBounds.Contains(_outsider.LogoutLocation));
        Assert.True(Map.Felucca.CanFit(_outsider.LogoutLocation, 16, false, false, true));
    }

    private PlayerMobile Player()
    {
        var player = Track(new PlayerMobile { Player = true, Body = 0x190 });
        player.AddItem(Track(new Backpack()));
        player.MoveToWorld(new Point3D(5850, 3050, 0), Map.Felucca);
        return player;
    }
    private T Track<T>(T entity) where T : IEntity { _created.Add(entity); return entity; }
    private static void SetController(TownStruggleController controller) =>
        typeof(TownStruggleRuntime).GetProperty(nameof(TownStruggleRuntime.Controller), BindingFlags.Public | BindingFlags.Static)!.SetValue(null, controller);

    public void Dispose()
    {
        for (var i = _created.Count - 1; i >= 0; i--) { _created[i].Delete(); }
        SetController(_previous);
        Core._now = _originalTime;
        TownStruggleCombat.Publish(_previous);
        Timer.Init(Core.TickCount);
    }

    private sealed class Host : ITownStruggleHost
    {
        public DateTime Now => Core.Now;
        public bool Eligible(Mobile player) => player is { Alive: true, Deleted: false };
        public bool AtNeutralBase(Mobile player, TownStruggleVenue venue) => true;
        public bool Move(Mobile player, Point3D point, Map map) { player.MoveToWorld(point, map); return true; }
        public bool MoveOutside(Mobile player, TownStruggleVenue venue, Point3D point, Map map) =>
            Move(player, new Point3D(venue.Area.Start.X - 1, venue.Area.Start.Y, 0), venue.Map);
        public void Notify(TownStruggleController controller) { }
    }
}
