using System;
using System.Reflection;
using Server;
using Server.Engines.TownStruggle;
using Server.Gumps;
using Server.Items;
using Server.Network;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

public partial class TownStruggleLootTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StaleResurrectionCannotChangeEnrolledPlayerHealth(bool enrolled)
    {
        const int floorId = 0x3FFD;
        var originalFloor = TileData.ItemTable[floorId];
        var previousController = TownStruggleRuntime.Controller;
        var player = Player();
        var outsider = Player();
        var gump = new ResurrectGump(outsider, 1.0);
        var controller = ActiveLootController(enrolled ? player : outsider);
        using var state = PacketTestUtilities.CreateTestNetState();
        try
        {
            SetLootRuntimeController(controller);
            player.NetState = state;
            state.Mobile = player;
            TileData.ItemTable[floorId] = new ItemData("resurrection test floor", TileFlag.Surface, 0, 0, 0, 0, 0, 0);
            var floor = Track(new Item(floorId) { Movable = false });
            floor.MoveToWorld(new Point3D(50, 50, 100), Map.Felucca);
            player.MoveToWorld(floor.Location, floor.Map);
            player.Hits = 12;
            Assert.True(player.Map.CanFit(player.Location, 16, false, false));
            var response = new RelayInfo(1, ReadOnlySpan<int>.Empty, ReadOnlySpan<ushort>.Empty,
                ReadOnlySpan<Range>.Empty, ReadOnlySpan<byte>.Empty);

            gump.OnResponse(state, in response);

            Assert.Equal(enrolled ? 12 : player.HitsMax, player.Hits);
            Assert.True(player.Alive);
        }
        finally
        {
            player.NetState = null;
            state.Mobile = null;
            TileData.ItemTable[floorId] = originalFloor;
            SetLootRuntimeController(previousController);
        }
    }

    [Theory]
    [InlineData("current", true)]
    [InlineData("olderRound", false)]
    [InlineData("differentController", false)]
    [InlineData("ordinaryCorpse", false)]
    public void EnrolledPickupAcceptsOnlyItsCurrentRoundCorpse(string source, bool expected)
    {
        var previousController = TownStruggleRuntime.Controller;
        var player = Player();
        var controller = ActiveLootController(player);
        var corpse = source switch
        {
            "ordinaryCorpse" => Track(new Corpse(player, [])),
            "differentController" => EventCorpse(ActiveLootController(player), player),
            _ => EventCorpse(controller, player)
        };
        var equipment = Track(new Longsword());
        corpse.DropItem(equipment);
        if (source == "olderRound")
        {
            SetControllerValue(controller, "RoundNumber", controller.RoundNumber + 1);
        }
        try
        {
            SetLootRuntimeController(controller);
            Assert.Equal(expected, TownStruggleInteractions.CanLift(player, equipment));
        }
        finally
        {
            SetLootRuntimeController(previousController);
        }
    }

    private static void SetLootRuntimeController(TownStruggleController controller) =>
        typeof(TownStruggleRuntime).GetProperty(nameof(TownStruggleRuntime.Controller), BindingFlags.Static | BindingFlags.Public)
            .SetValue(null, controller);
}
