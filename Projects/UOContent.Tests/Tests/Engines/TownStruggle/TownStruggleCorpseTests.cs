using System;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Engines.TownStruggle;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

public partial class TownStruggleLootTests
{
    [Theory]
    [InlineData(TownStruggleTeam.Order)]
    [InlineData(TownStruggleTeam.Chaos)]
    public void ActiveParticipantCanLootEventCorpse(TownStruggleTeam team)
    {
        var owner = Player();
        var looter = Player();
        var controller = ActiveLootController(owner, looter);
        controller.GetParticipant(looter).Team = team;
        var corpse = EventCorpse(controller, owner);
        var equipment = Track(new Longsword());
        corpse.DropItem(equipment);
        var rejection = LRReason.Inspecific;

        Assert.True(corpse.CanEventLoot(looter));
        Assert.True(corpse.CheckItemUse(looter, corpse));
        Assert.True(corpse.CheckItemUse(looter, equipment));
        Assert.True(corpse.CheckLift(looter, equipment, ref rejection));
    }

    [Theory]
    [InlineData("outsider")]
    [InlineData("disqualified")]
    [InlineData("returned")]
    [InlineData("neutral")]
    [InlineData("respawning")]
    [InlineData("outside")]
    [InlineData("otherMap")]
    [InlineData("dead")]
    public void InvalidLooterCannotUseOrLiftEventLoot(string state)
    {
        var owner = Player();
        var looter = Player();
        var controller = state == "outsider" ? ActiveLootController(owner) : ActiveLootController(owner, looter);
        var participant = controller.GetParticipant(looter);
        switch (state)
        {
            case "disqualified": participant.Disqualified = true; break;
            case "returned": participant.Returned = true; break;
            case "neutral": participant.Team = TownStruggleTeam.Neutral; break;
            case "respawning": participant.RespawnAt = Core.Now + TimeSpan.FromMinutes(1); break;
            case "outside": looter.MoveToWorld(new Point3D(500, 500, 0), Map.Felucca); break;
            case "otherMap": looter.MoveToWorld(looter.Location, Map.Trammel); break;
            case "dead": looter.Body = 0x192; break;
        }
        var corpse = EventCorpse(controller, owner);
        var equipment = Track(new Longsword());
        corpse.DropItem(equipment);
        var rejection = LRReason.Inspecific;

        Assert.False(corpse.CanEventLoot(looter));
        Assert.False(corpse.CheckItemUse(looter, corpse));
        Assert.False(corpse.CheckItemUse(looter, equipment));
        Assert.False(corpse.CheckLift(looter, equipment, ref rejection));
        Assert.Equal(LRReason.CannotLift, rejection);
        corpse.Open(looter, true);
        Assert.Same(corpse, equipment.Parent);
    }

    [Theory]
    [InlineData(TownStrugglePhase.Dormant)]
    [InlineData(TownStrugglePhase.Signup)]
    [InlineData(TownStrugglePhase.Results)]
    [InlineData(TownStrugglePhase.Faulted)]
    public void FinishedEventUsesOrdinaryCorpseLootRules(TownStrugglePhase phase)
    {
        var owner = Player();
        var controller = ActiveLootController(owner);
        var corpse = EventCorpse(controller, owner);
        var looter = Player();
        var equipment = Track(new Longsword());
        corpse.DropItem(equipment);
        SetControllerValue(controller, "Phase", phase);
        var rejection = LRReason.Inspecific;

        Assert.True(corpse.CanEventLoot(looter));
        Assert.True(corpse.CheckItemUse(looter, equipment));
        Assert.True(corpse.CheckLift(looter, equipment, ref rejection));
    }

    [Fact]
    public void OlderRoundDoesNotReserveItsCorpseForANewerRoster()
    {
        var owner = Player();
        var controller = ActiveLootController(owner);
        var corpse = EventCorpse(controller, owner);
        SetControllerValue(controller, "RoundNumber", controller.RoundNumber + 1);
        Assert.True(corpse.CanEventLoot(Player()));
    }

    [Fact]
    public void EventCorpseCannotBeUsedAsAResupplyContainerOrCorpseResource()
    {
        var owner = Player();
        var controller = ActiveLootController(owner);
        var corpse = EventCorpse(controller, owner);
        var outsider = Player();
        var equipment = Track(new Longsword());
        outsider.Backpack.DropItem(equipment);

        Assert.False(corpse.CheckHold(outsider, equipment, false));
        Assert.False(corpse.OnDragDrop(outsider, equipment));
        Assert.False(corpse.OnDragDropInto(outsider, equipment, Point3D.Zero));
        Assert.False(corpse.CheckHold(owner, equipment, false));
        Assert.Same(outsider.Backpack, equipment.Parent);
        Assert.True(corpse.Carved);
        Assert.True(corpse.Channeled);
    }

    [Fact]
    public void EventCorpseSaveLoadPreservesControllerAndRoundIdentity()
    {
        var owner = Player();
        var controller = ActiveLootController(owner);
        var corpse = EventCorpse(controller, owner);
        var writer = new BufferWriter(true);
        corpse.Serialize(writer);
        var restored = Track(new TownStruggleCorpse(World.NewItem));
        var reader = new BufferReader(writer.Buffer.AsSpan(0, (int)writer.Position).ToArray());
        restored.Deserialize(reader);

        Assert.Equal(writer.Position, reader.Position);
        Assert.Same(controller, restored.Controller);
        Assert.Equal(controller.RoundNumber, restored.RoundNumber);
        Assert.False(restored.CanEventLoot(Player()));
        Assert.True(restored.Carved);
        Assert.True(restored.Channeled);
        restored.Delete();
        Assert.Null(restored.Controller);
    }

    private TownStruggleCorpse EventCorpse(TownStruggleController controller, PlayerMobile owner)
    {
        var corpse = Track(new TownStruggleCorpse(controller, owner));
        corpse.MoveToWorld(owner.Location, owner.Map);
        return corpse;
    }

    private TownStruggleController ActiveLootController(params PlayerMobile[] players)
    {
        var controller = Track(new TownStruggleController());
        SetControllerValue(controller, "Phase", TownStrugglePhase.Active);
        SetControllerValue(controller, "RoundNumber", 3);
        SetControllerValue(controller, "MatchDeadline", Core.Now + TimeSpan.FromMinutes(15));
        var type = typeof(TownStruggleController);
        type.GetField("_venue", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller,
            new TownStruggleVenue("Loot test", Map.Felucca, new Rectangle2D(40, 40, 100, 100),
                new Point3D(20, 20, 0), new Point3D(45, 45, 0), new Point3D(130, 130, 0), [], []));
        var members = (Dictionary<Mobile, TownStruggleParticipant>)type.GetField("_members", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(controller);
        foreach (var player in players)
        {
            var participant = Track(new TownStruggleParticipant { Player = player, Team = TownStruggleTeam.Order });
            members.Add(player, participant);
        }
        return controller;
    }

    private static void SetControllerValue(TownStruggleController controller, string name, object value) =>
        typeof(TownStruggleController).GetProperty(name).SetValue(controller, value);
}
