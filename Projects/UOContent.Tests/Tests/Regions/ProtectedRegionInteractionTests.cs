using System;
using System.Collections.Generic;
using Server;
using Server.Engines.PartySystem;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;
using Server.Regions;
using Server.SkillHandlers;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Regions;

[Collection("Sequential UOContent Tests")]
public class ProtectedRegionInteractionTests : IDisposable
{
    private static readonly Point3D Inside = new(6000, 3800, 0);
    private static readonly Point3D Outside = new(5999, 3800, 0);
    private readonly List<IEntity> _created = [];
    private readonly List<ProtectedRegion> _regions = [];
    private readonly List<Guild> _guilds = [];

    public void Dispose()
    {
        foreach (var guild in _guilds)
        {
            guild.Delete();
        }
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            if (_created[index] is Mobile mobile)
            {
                mobile.Party = null;
            }
            _created[index].Delete();
        }
        foreach (var region in _regions)
        {
            region.Unregister();
        }
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Snooping_BlocksEitherProtectedEndpointBeforeDisplayingContainer(bool sourceInside)
    {
        TileDataRequirement.SkipIfMissing();
        AddRegion();
        var snooper = AddPlayer(sourceInside ? Inside : Outside);
        var owner = AddPlayer(sourceInside ? Outside : Inside);
        var container = Track(new DisplayTrackingContainer());
        owner.Backpack.DropItem(container);
        snooper.Skills.Snooping.Base = 100;

        Assert.False(Snooping.CheckSnoopAllowed(snooper, owner));
        Snooping.Container_Snoop(container, snooper);

        Assert.False(container.Displayed);
        Assert.Equal(0, snooper.Karma);
    }

    [SkippableFact]
    public void Snooping_PlayerOwnedPetIsProtectedButWildNpcAndStaffKeepExistingBehavior()
    {
        TileDataRequirement.SkipIfMissing();
        AddRegion();
        var snooper = AddPlayer(Inside);
        var owner = AddPlayer(Outside);
        var pet = Track(new Dog());
        pet.SetControlMaster(owner);
        pet.MoveToWorld(Outside, Map.Felucca);
        Assert.False(Snooping.CheckSnoopAllowed(snooper, pet));

        var npc = AddNpc(Outside);
        var container = Track(new DisplayTrackingContainer());
        npc.Backpack.DropItem(container);
        var previous = Mobile.SkillCheckTargetHandler;
        try
        {
            Mobile.SkillCheckTargetHandler = (_, _, _, _, _) => true;
            Assert.True(Snooping.CheckSnoopAllowed(snooper, npc));
            Snooping.Container_Snoop(container, snooper);
            Assert.True(container.Displayed);
        }
        finally
        {
            Mobile.SkillCheckTargetHandler = previous;
        }

        snooper.AccessLevel = AccessLevel.GameMaster;
        var playerContainer = Track(new DisplayTrackingContainer());
        owner.Backpack.DropItem(playerContainer);
        Snooping.Container_Snoop(playerContainer, snooper);
        Assert.True(playerContainer.Displayed);
    }

    [SkippableTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Stealing_RechecksQueuedItemAndMobileTargetsAfterEitherEndpointEntersProtection(
        bool thiefInside, bool targetMobile
    )
    {
        TileDataRequirement.SkipIfMissing();
        AddRegion();
        var thief = AddPlayer(Outside);
        thief.NpcGuild = NpcGuild.ThievesGuild;
        var victim = AddPlayer(Outside);
        var item = Track(new Item(0xEED) { Weight = 0, Stackable = true });
        Container parent = victim.Backpack;
        if (!targetMobile)
        {
            parent = Track(new Backpack());
            victim.Backpack.DropItem(parent);
        }
        parent.DropItem(item);
        Stealing.OnUse(thief);
        var target = thief.Target;
        Assert.NotNull(target);
        target.CheckLOS = false;

        (thiefInside ? thief : victim).MoveToWorld(Inside, Map.Felucca);
        var previous = Mobile.SkillCheckTargetHandler;
        var skillChecked = false;
        try
        {
            Mobile.SkillCheckTargetHandler = (_, _, _, _, _) => skillChecked = true;
            target.Invoke(thief, targetMobile ? victim : item);
        }
        finally
        {
            Mobile.SkillCheckTargetHandler = previous;
        }

        Assert.False(skillChecked);
        Assert.Same(parent, item.Parent);
        Assert.Empty(thief.PermaFlags);
        Assert.Equal(0, thief.Karma);
        Assert.False(thief.Criminal);
    }

    [SkippableFact]
    public void Stealing_BlocksItemsOwnedByAnotherPlayersPet()
    {
        TileDataRequirement.SkipIfMissing();
        AddRegion();
        var thief = AddPlayer(Outside);
        var owner = AddPlayer(Inside);
        var pet = Track(new Dog());
        pet.SetControlMaster(owner);
        pet.MoveToWorld(Outside, Map.Felucca);
        pet.AddItem(Track(new Backpack()));
        var item = Track(new Item(0xEED) { Weight = 0, Stackable = true });
        pet.Backpack.DropItem(item);
        Stealing.OnUse(thief);
        var target = thief.Target;
        Assert.NotNull(target);
        target.CheckLOS = false;
        target.Invoke(thief, item);

        Assert.Same(pet.Backpack, item.Parent);
        Assert.Equal(0, thief.Karma);
    }

    [SkippableFact]
    public void Stealing_WildNpcItemsCanStillBeStolenFromInsideProtection()
    {
        TileDataRequirement.SkipIfMissing();
        AddRegion();
        var thief = AddPlayer(Inside);
        var npc = AddNpc(Outside);
        var item = Track(new Item(0xEED) { Weight = 0, Stackable = true });
        npc.Backpack.DropItem(item);
        Stealing.OnUse(thief);
        var target = thief.Target;
        Assert.NotNull(target);
        target.CheckLOS = false;
        var previous = Mobile.SkillCheckTargetHandler;
        try
        {
            Mobile.SkillCheckTargetHandler = (_, _, _, _, _) => true;
            target.Invoke(thief, item);
        }
        finally
        {
            Mobile.SkillCheckTargetHandler = previous;
        }

        Assert.Same(thief.Backpack, item.Parent);
    }

    [SkippableTheory]
    [InlineData(false, 0, false, false, true)]
    [InlineData(false, 0, false, false, false)]
    [InlineData(true, 0, false, false, true)]
    [InlineData(true, 0, false, false, false)]
    [InlineData(false, 5, false, false, true)]
    [InlineData(false, 5, false, false, false)]
    [InlineData(false, 0, true, false, true)]
    [InlineData(false, 0, true, false, false)]
    [InlineData(false, 0, false, true, true)]
    [InlineData(false, 0, false, true, false)]
    public void CorpseLoot_BlocksOtherPlayersIncludingCriminalMurdererPartyAndGuild(
        bool criminal, int kills, bool party, bool guild, bool looterInside
    )
    {
        TileDataRequirement.SkipIfMissing();
        AddRegion();
        var owner = AddPlayer(new Point3D(5990, 3800, 0));
        owner.Criminal = criminal;
        owner.Kills = kills;
        var looter = AddPlayer(Outside);
        if (party)
        {
            var group = new Party(owner);
            owner.Party = group;
            group.Add(looter);
            group[owner].CanLoot = true;
        }
        if (guild)
        {
            var group = new Guild(owner, "ProtectedLootTest", "PLT");
            _guilds.Add(group);
            group.AddMember(looter);
        }
        var corpse = Track(new Corpse(owner, []));
        corpse.MoveToWorld(Outside, Map.Felucca);
        var container = Track(new Backpack());
        var nested = Track(new Backpack());
        var item = Track(new Item(0xEED));
        corpse.DropItem(container);
        container.DropItem(nested);
        nested.DropItem(item);
        Assert.True(corpse.CanLoot(looter, item));

        if (looterInside)
        {
            looter.MoveToWorld(Inside, Map.Felucca);
        }
        else
        {
            corpse.MoveToWorld(Inside, Map.Felucca);
        }

        Assert.False(corpse.CanLoot(looter, item));
        Assert.False(corpse.CheckLoot(looter, item));
        Assert.False(corpse.CheckItemUse(looter, item));
        Assert.False(item.CheckItemUse(looter));
        Assert.False(nested.CheckItemUse(looter));
        Assert.False(item.CheckLift(looter));
        Assert.False(nested.CheckLift(looter));
        Assert.Same(nested, item.Parent);
        Assert.Empty(corpse.Looters);
        Assert.True(corpse.CanLoot(owner, item));
        Assert.True(item.CheckItemUse(owner));
        Assert.True(item.CheckLift(owner));
    }

    [SkippableFact]
    public void CorpseLoot_UsesActualCorpseLocationAndRechecksRemoteHooks()
    {
        TileDataRequirement.SkipIfMissing();
        AddRegion();
        var owner = AddPlayer(Inside);
        var looter = AddPlayer(new Point3D(5990, 3800, 0));
        var corpse = Track(new Corpse(owner, []));
        corpse.MoveToWorld(Outside, Map.Felucca);
        var item = Track(new Item(0xEED));
        corpse.DropItem(item);
        Assert.True(corpse.CanLoot(looter, item));

        corpse.MoveToWorld(Inside, Map.Felucca);
        Assert.False(corpse.CanLoot(looter, item));
        Assert.False(item.CheckLift(looter));
        Assert.False(item.CheckItemUse(looter));

        corpse.MoveToWorld(Outside, Map.Felucca);
        Assert.True(item.CheckLift(looter));
        Assert.True(item.CheckItemUse(looter));
        looter.MoveToWorld(Inside, Map.Felucca);
        Assert.False(item.CheckLift(looter));
    }

    [SkippableFact]
    public void CorpseLoot_StaffBypassAndDisabledPolicyPreserveExistingEligibility()
    {
        TileDataRequirement.SkipIfMissing();
        var region = AddRegion();
        var owner = AddPlayer(Outside);
        var looter = AddPlayer(Outside);
        var corpse = Track(new Corpse(owner, []));
        corpse.MoveToWorld(Inside, Map.Felucca);
        var item = Track(new Item(0xEED));
        corpse.DropItem(item);
        Assert.False(corpse.CanLoot(looter, item));

        looter.AccessLevel = AccessLevel.GameMaster;
        Assert.True(corpse.CanLoot(looter, item));
        Assert.True(item.CheckLift(looter));
        looter.AccessLevel = AccessLevel.Player;
        owner.AccessLevel = AccessLevel.Counselor;
        Assert.True(corpse.CanLoot(looter, item));
        owner.AccessLevel = AccessLevel.Player;
        region.PreventPlayerCorpseLooting = false;
        Assert.True(corpse.CanLoot(looter, item));
    }

    [SkippableTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MonsterCorpseLoot_PreservesEarnedAndUnearnedRulesOnBothFacets(bool trammel, bool earned)
    {
        TileDataRequirement.SkipIfMissing();
        var map = trammel ? Map.Trammel : Map.Felucca;
        AddRegion(map);
        var looter = AddPlayer(Outside, map);
        var monster = Track(new Dog());
        monster.MoveToWorld(Outside, map);
        var corpse = Track(new Corpse(monster, []));
        corpse.MoveToWorld(Outside, map);
        if (earned)
        {
            corpse.Aggressors.Add(looter);
        }
        var item = Track(new Item(0xEED));
        corpse.DropItem(item);
        var expected = !trammel || earned;
        Assert.Equal(expected, corpse.CanLoot(looter, item));

        looter.MoveToWorld(Inside, map);
        corpse.MoveToWorld(Inside, map);
        Assert.Equal(expected, corpse.CanLoot(looter, item));
        Assert.Equal(expected, item.CheckItemUse(looter));
        Assert.Equal(expected, item.CheckLift(looter));
    }

    private ProtectedRegion AddRegion(Map map = null)
    {
        var region = new ProtectedRegion("ProtectedInteractionTest", map ?? Map.Felucca, 100,
            new Rectangle3D(6000, 3800, -128, 10, 10, 256));
        _regions.Add(region);
        region.Register();
        return region;
    }

    private PlayerMobile AddPlayer(Point3D point, Map map = null)
    {
        var mobile = Track(new PlayerMobile { Player = true });
        mobile.Body = 0x190;
        mobile.AddItem(Track(new Backpack()));
        mobile.MoveToWorld(point, map ?? Map.Felucca);
        return mobile;
    }

    private Mobile AddNpc(Point3D point)
    {
        var mobile = Track(new Mobile());
        mobile.Body = 0x190;
        mobile.AddItem(Track(new Backpack()));
        mobile.MoveToWorld(point, Map.Felucca);
        return mobile;
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private class DisplayTrackingContainer : Container
    {
        public DisplayTrackingContainer() : base(0xE75)
        {
        }

        public bool Displayed { get; private set; }

        public override void DisplayTo(Mobile to)
        {
            Displayed = true;
        }
    }
}
