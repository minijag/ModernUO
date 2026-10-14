using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Server;
using Server.Engines.ConPVP;
using Server.Guilds;
using Server.Json;
using Server.Misc;
using Server.Mobiles;
using Server.Regions;
using Xunit;

namespace UOContent.Tests.Regions;

[Collection("Sequential UOContent Tests")]
public class ProtectedRegionTests : IDisposable
{
    private static readonly Point3D Inside = new(6001, 3501, 0);
    private static readonly Point3D Outside = new(6020, 3501, 0);
    private readonly List<IEntity> _created = [];
    private readonly List<Region> _regions = [];
    private readonly List<Guild> _guilds = [];
    private readonly Expansion _expansion = Core.Expansion;
    private readonly AllowHarmfulHandler _harmful = Mobile.AllowHarmfulHandler;

    public ProtectedRegionTests()
    {
        Core.Expansion = Expansion.T2A;
        Mobile.AllowHarmfulHandler = NotorietyHandlers.Mobile_AllowHarmful;
    }

    public void Dispose()
    {
        foreach (var guild in _guilds)
        {
            guild.Disband();
        }
        for (var index = _regions.Count - 1; index >= 0; index--)
        {
            _regions[index].Unregister();
        }
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            _created[index].Delete();
        }
        Core.Expansion = _expansion;
        Mobile.AllowHarmfulHandler = _harmful;
    }

    [Fact]
    public void JsonConstructor_AllowsIndependentMapBoundsAndPolicyFlags()
    {
        var region = JsonSerializer.Deserialize<ProtectedRegion>("""
            {
              "Name":"Reusable region", "Map":"Trammel", "Priority":173,
              "Area":[{"x1":6000,"y1":3500,"z1":-128,"x2":6010,"y2":3510,"z2":128}],
              "PreventPlayerCombat":false,"PreventPlayerSnooping":false,
              "PreventPlayerStealing":false,"PreventPlayerCorpseLooting":false
            }
            """, JsonConfig.DefaultOptions);

        Assert.Equal("Reusable region", region.Name);
        Assert.Same(Map.Trammel, region.Map);
        Assert.Equal(173, region.Priority);
        Assert.True(region.Contains(Inside));
        Assert.False(region.Contains(Outside));
        Assert.False(region.PreventPlayerCombat);
        Assert.False(region.PreventPlayerSnooping);
        Assert.False(region.PreventPlayerStealing);
        Assert.False(region.PreventPlayerCorpseLooting);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EitherPlayerEndpoint_IsProtectedEvenAcrossTheBorder(int protectedEndpoint)
    {
        Region();
        var first = Player(protectedEndpoint != 1 ? Inside : Outside);
        var second = Player(protectedEndpoint != 0 ? Inside : Outside);

        foreach (var action in Enum.GetValues<ProtectedInteraction>())
        {
            Assert.True(ProtectedRegion.ProtectsInteraction(first, second, action));
            Assert.True(ProtectedRegion.ProtectsInteraction(second, first, action));
        }
        Assert.False(first.CanBeHarmful(second, false));
        Assert.False(second.CanBeHarmful(first, false));
    }

    [Fact]
    public void DynamicOverlay_DoesNotHideConfiguredProtection()
    {
        var protectedRegion = Region();
        var overlay = new BaseRegion("House-like overlay", Map.Felucca, 999, protectedRegion.Area);
        overlay.Register();
        _regions.Add(overlay);
        var first = Player(Inside);
        var second = Player(Inside);

        Assert.Same(overlay, first.Region);
        Assert.True(ProtectedRegion.PreventsPlayerHarm(first, second));
        Assert.False(NotorietyHandlers.Mobile_AllowHarmful(first, second));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PetsAndSummons_CountTheirPhysicalLocationAndPlayerOwners(bool sourceSummoned, bool targetSummoned)
    {
        Region();
        var first = Player(Inside);
        var second = Player(Outside);
        var source = Creature(Outside, first, sourceSummoned);
        var target = Creature(Outside, second, targetSummoned);

        Assert.Same(first, ProtectedRegion.GetPlayerOwner(source));
        Assert.True(ProtectedRegion.PreventsPlayerHarm(source, target));
        Assert.True(ProtectedRegion.PreventsPlayerHarm(target, source));
        Assert.False(NotorietyHandlers.Mobile_AllowHarmful(source, target));
        first.MoveToWorld(Outside, Map.Felucca);
        Assert.False(ProtectedRegion.PreventsPlayerHarm(source, target));
        source.MoveToWorld(Inside, Map.Felucca);
        Assert.True(ProtectedRegion.PreventsPlayerHarm(source, target));
    }

    [Fact]
    public void NestedOwners_AreResolvedAndOwnershipCyclesAreBounded()
    {
        var owner = Player(Inside);
        var first = Creature(Inside, owner);
        var second = Creature(Inside, first);
        Assert.Same(owner, ProtectedRegion.GetPlayerOwner(second));
        first.ControlMaster = second;
        try
        {
            Assert.Null(ProtectedRegion.GetPlayerOwner(first));
        }
        finally
        {
            first.ControlMaster = owner;
        }
    }

    [Fact]
    public void PvE_SelfAndOwnPetInteractions_UnattributedDamageAndOutsidePvPRemainAvailable()
    {
        Region();
        var player = Player(Inside);
        var other = Player(Outside);
        var monster = Creature(Inside);
        var ownPet = Creature(Inside, player);
        Assert.False(ProtectedRegion.PreventsPlayerHarm(player, monster));
        Assert.False(ProtectedRegion.PreventsPlayerHarm(monster, player));
        Assert.False(ProtectedRegion.PreventsPlayerHarm(player, player));
        Assert.False(ProtectedRegion.PreventsPlayerHarm(player, ownPet));
        Assert.True(NotorietyHandlers.Mobile_AllowHarmful(player, monster));
        player.Damage(5, monster);
        player.Damage(5);
        player.Damage(5, player);
        Assert.Equal(85, player.Hits);
        monster.Damage(5, player);
        Assert.Equal(95, monster.Hits);
        player.MoveToWorld(Outside, Map.Felucca);
        Assert.True(NotorietyHandlers.Mobile_AllowHarmful(player, other));
        other.Damage(5, player);
        Assert.Equal(95, other.Hits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectPlayerAndPetDamage_IsRejectedBeforeHitLoss(bool petTarget)
    {
        Region();
        var attacker = Player(Outside);
        var owner = Player(Inside);
        Mobile target = petTarget ? Creature(Inside, owner) : owner;

        target.Damage(25, attacker);

        Assert.Equal(100, target.Hits);
        Assert.Equal(0, target.DamageEntries.Count);
        target.MoveToWorld(Outside, Map.Felucca);
        owner.MoveToWorld(Outside, Map.Felucca);
        target.Damage(25, attacker);
        Assert.Equal(75, target.Hits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaffEndpointsAndStaffOwnedSummons_BypassAddedRestrictions(bool staffOwner)
    {
        Region();
        var staff = Player(Inside);
        staff.AccessLevel = AccessLevel.GameMaster;
        Mobile source = staffOwner ? Creature(Inside, staff, true) : staff;
        var player = Player(Inside);

        Assert.False(ProtectedRegion.PreventsPlayerHarm(source, player));
        Assert.False(ProtectedRegion.PreventsPlayerHarm(player, source));
        player.Damage(5, source);
        Assert.Equal(95, player.Hits);
    }

    [Theory]
    [InlineData(ProtectedInteraction.Combat)]
    [InlineData(ProtectedInteraction.Snooping)]
    [InlineData(ProtectedInteraction.Stealing)]
    [InlineData(ProtectedInteraction.CorpseLooting)]
    public void DisablingOnePolicy_DoesNotDisableTheOtherThree(ProtectedInteraction disabled)
    {
        var region = Region();
        region.PreventPlayerCombat = disabled != ProtectedInteraction.Combat;
        region.PreventPlayerSnooping = disabled != ProtectedInteraction.Snooping;
        region.PreventPlayerStealing = disabled != ProtectedInteraction.Stealing;
        region.PreventPlayerCorpseLooting = disabled != ProtectedInteraction.CorpseLooting;
        var first = Player(Inside);
        var second = Player(Inside);

        foreach (var action in Enum.GetValues<ProtectedInteraction>())
        {
            Assert.Equal(action != disabled, ProtectedRegion.ProtectsInteraction(first, second, action));
        }
    }

    [Fact]
    public void GuildOrActiveDuel_DoesNotGrantAPvPException()
    {
        Region();
        var first = Player(Inside);
        var second = Player(Inside);
        var guild = new Guild(first, "Protected region test", "PRT");
        _guilds.Add(guild);
        guild.AddMember(second);
        var duel = new DuelContext(first, RulesetLayout.Root);
        duel.Participants[1].Add(second);
        typeof(DuelContext).GetField("<Started>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(duel, true);
        try
        {
            Assert.False(NotorietyHandlers.Mobile_AllowHarmful(first, second));
            Assert.False(NotorietyHandlers.Mobile_AllowHarmful(second, first));
            second.Damage(25, first);
            Assert.Equal(100, second.Hits);
        }
        finally
        {
            duel.Unregister();
        }
    }

    private ProtectedRegion Region()
    {
        var region = new ProtectedRegion("Protected region test", Map.Felucca, 150,
            new Rectangle3D(new Point3D(6000, 3500, -128), new Point3D(6010, 3510, 128)));
        region.Register();
        _regions.Add(region);
        return region;
    }

    private PlayerMobile Player(Point3D position)
    {
        var player = Track(new PlayerMobile { Player = true, Body = 0x190, Young = false });
        player.RawStr = 100;
        player.Hits = 100;
        player.MoveToWorld(position, Map.Felucca);
        return player;
    }

    private BaseCreature Creature(Point3D position, Mobile owner = null, bool summoned = false)
    {
        var creature = Track(new TestCreature());
        creature.SetStr(100);
        creature.SetHits(100);
        creature.Hits = 100;
        if (summoned)
        {
            creature.Summoned = true;
            creature.SummonMaster = owner;
        }
        else if (owner != null)
        {
            creature.Controlled = true;
            creature.ControlMaster = owner;
        }
        creature.MoveToWorld(position, Map.Felucca);
        return creature;
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private sealed class TestCreature : BaseCreature
    {
        public TestCreature() : base(AIType.AI_Melee)
        {
        }
    }
}
