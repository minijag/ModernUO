using System;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Engines.ModularDungeon;
using Server.Guilds;
using Server.Misc;
using Server.Mobiles;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ExtractionEventCombatTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly List<Region> _regions = [];
    private readonly List<Guild> _guilds = [];
    private readonly Expansion _startExpansion = Core.Expansion;
    private readonly DateTime _startTime = Core.Now;
    private readonly long _startTicks = Core.TickCount;
    private readonly NotorietyHandler _notorietyHandler = Notoriety.Handler;

    public ExtractionEventCombatTests()
    {
        Core.Expansion = Expansion.T2A;
        Timer.Init(Core.TickCount);
    }

    public void Dispose()
    {
        foreach (var guild in _guilds)
        {
            guild.Disband();
        }

        foreach (var region in _regions)
        {
            region.Unregister();
        }

        for (var index = _created.Count - 1; index >= 0; index--)
        {
            _created[index].Delete();
        }

        Core.Expansion = _startExpansion;
        Notoriety.Handler = _notorietyHandler;
        Core._now = _startTime;
        Core._tickCount = _startTicks;
        Timer.Init(Core.TickCount);
    }

    [Fact]
    public void ActiveArenaPlayers_AreAttackableInBothDirections_WithoutMembershipCollectionReads()
    {
        var arena = Arena(ExtractionEventPhase.Active);
        var first = Player(0);
        var second = Player(1);

        Assert.Same(arena, first.Region);
        Assert.Same(arena, second.Region);
        Assert.True(ExtractionEventCombat.IsEventCombat(first, second));
        Assert.True(ExtractionEventCombat.IsEventCombat(second, first));
        Assert.Equal(Notoriety.CanBeAttacked, NotorietyHandlers.MobileNotoriety(first, second));
        Assert.Equal(Notoriety.CanBeAttacked, NotorietyHandlers.MobileNotoriety(second, first));
    }

    [Theory]
    [InlineData("Players")]
    [InlineData("PetAttacks")]
    [InlineData("PetDefends")]
    public void NativeHarmfulAction_InActiveArena_DoesNotCreateCriminalAggressionOrMurderReports(string combatants)
    {
        Arena(ExtractionEventPhase.Active);
        var owner = Player(0);
        var opponent = Player(1);
        var pet = Track(new DireWolf());
        pet.SetControlMaster(owner);
        pet.MoveToWorld(ExtractionDungeonLayout.Rooms[0].PlayerStart, Map.Felucca);
        Notoriety.Handler = NotorietyHandlers.MobileNotoriety;
        Mobile attacker = combatants switch
        {
            "PetAttacks" => pet,
            "PetDefends" => opponent,
            _ => owner
        };
        Mobile defender = combatants == "PetDefends" ? pet : opponent;

        attacker.DoHarmful(defender);

        Assert.False(attacker.Criminal);
        Assert.False(owner.Criminal);
        var aggression = Assert.Single(defender.Aggressors, aggression => aggression.Attacker == attacker);
        Assert.Same(attacker, aggression.Attacker);
        Assert.False(aggression.CriminalAggression);
        Assert.False(aggression.CanReportMurder);
        Assert.All(defender.Aggressors, entry =>
        {
            Assert.False(entry.CriminalAggression);
            Assert.False(entry.CanReportMurder);
        });
    }

    [Fact]
    public void NativeHarmfulAction_OutsideTheEvent_RetainsCriminalAggressionAndMurderReports()
    {
        var attacker = Player(0);
        var defender = Player(1);
        attacker.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        defender.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);
        Notoriety.Handler = NotorietyHandlers.MobileNotoriety;

        attacker.DoHarmful(defender);

        Assert.True(attacker.Criminal);
        var aggression = Assert.Single(defender.Aggressors);
        Assert.True(aggression.CriminalAggression);
        Assert.True(aggression.CanReportMurder);
    }

    [Theory]
    [InlineData(ExtractionEventPhase.Waiting)]
    [InlineData(ExtractionEventPhase.Starting)]
    [InlineData(ExtractionEventPhase.Finishing)]
    [InlineData(ExtractionEventPhase.Faulted)]
    public void InactiveArena_DoesNotOverrideOrdinaryNotoriety(ExtractionEventPhase phase)
    {
        Arena(phase);
        var first = Player(0);
        var second = Player(1);

        Assert.False(ExtractionEventCombat.IsEventCombat(first, second));
        Assert.Equal(Notoriety.Innocent, NotorietyHandlers.MobileNotoriety(first, second));
    }

    [Theory]
    [InlineData(false, 0, Notoriety.Innocent)]
    [InlineData(true, 0, Notoriety.Criminal)]
    [InlineData(false, 5, Notoriety.Murderer)]
    public void PlayersOutsideTheEvent_RetainExistingNotoriety(bool criminal, int kills, int expected)
    {
        var first = Player(0);
        var second = Player(1);
        first.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        second.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);
        second.Criminal = criminal;
        second.Kills = kills;

        Assert.False(ExtractionEventCombat.IsEventCombat(first, second));
        Assert.Equal(expected, NotorietyHandlers.MobileNotoriety(first, second));
    }

    [Fact]
    public void CrossingAnArenaBoundary_DoesNotGrantAttackabilityOutsideIt()
    {
        Arena(ExtractionEventPhase.Active);
        var first = Player(0);
        var second = Player(1);
        second.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);

        Assert.False(ExtractionEventCombat.IsEventCombat(first, second));
        Assert.False(ExtractionEventCombat.IsEventCombat(second, first));
        Assert.Equal(Notoriety.Innocent, NotorietyHandlers.MobileNotoriety(first, second));
    }

    [Fact]
    public void DifferentArenaInstances_DoNotShareTheEventCombatOverride()
    {
        var firstArena = Arena(ExtractionEventPhase.Active);
        var first = Player(0);
        var second = Player(1);
        var otherController = Track(new ExtractionEventController());
        SetPhase(otherController, ExtractionEventPhase.Active);
        var otherArena = new ExtractionArenaRegion(otherController);
        typeof(Mobile).GetField("m_Region", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(second, otherArena);

        Assert.Same(firstArena, first.Region);
        Assert.Same(otherArena, second.Region);
        Assert.False(ExtractionEventCombat.IsEventCombat(first, second));
    }

    [Fact]
    public void SameGuildPlayers_AreAttackableInsideButRemainAlliesOutside()
    {
        Arena(ExtractionEventPhase.Active);
        var first = Player(0);
        var second = Player(1);
        var guild = new Guild(first, "Extraction combat test", "ECT");
        _guilds.Add(guild);
        guild.AddMember(second);

        Assert.Equal(Notoriety.CanBeAttacked, NotorietyHandlers.MobileNotoriety(first, second));
        first.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        second.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);
        Assert.Equal(Notoriety.Ally, NotorietyHandlers.MobileNotoriety(first, second));
    }

    [Fact]
    public void ControlledPets_UseTheirOrdinaryOwnersArena_ForBothAttackDirections()
    {
        Arena(ExtractionEventPhase.Active);
        var owner = Player(0);
        var opponent = Player(1);
        var pet = Track(new DireWolf());
        pet.SetControlMaster(owner);
        pet.MoveToWorld(ExtractionDungeonLayout.Rooms[0].PlayerStart, Map.Felucca);

        Assert.True(ExtractionEventCombat.IsEventCombat(pet, opponent));
        Assert.True(ExtractionEventCombat.IsEventCombat(opponent, pet));
        Assert.Equal(Notoriety.CanBeAttacked, NotorietyHandlers.MobileNotoriety(opponent, pet));
        Assert.Equal(Notoriety.CanBeAttacked, NotorietyHandlers.MobileNotoriety(pet, opponent));
    }

    [Theory]
    [InlineData("Uncontrolled")]
    [InlineData("OwnerOutside")]
    [InlineData("OwnerStaff")]
    [InlineData("SummonedOnly")]
    [InlineData("OwnerDeleted")]
    public void PetWithoutAnOrdinaryControlledArenaOwner_IsExcluded(string invalidation)
    {
        Arena(ExtractionEventPhase.Active);
        var owner = Player(0);
        var opponent = Player(1);
        var pet = Track(new DireWolf());
        pet.SetControlMaster(owner);
        pet.MoveToWorld(ExtractionDungeonLayout.Rooms[0].PlayerStart, Map.Felucca);

        switch (invalidation)
        {
            case "Uncontrolled":
            {
                pet.Controlled = false;
                break;
            }
            case "OwnerOutside":
            {
                owner.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
                break;
            }
            case "OwnerStaff":
            {
                owner.AccessLevel = AccessLevel.GameMaster;
                break;
            }
            case "SummonedOnly":
            {
                pet.Controlled = false;
                pet.Summoned = true;
                break;
            }
            case "OwnerDeleted":
            {
                owner.Delete();
                break;
            }
        }

        Assert.False(ExtractionEventCombat.IsEventCombat(pet, opponent));
        Assert.False(ExtractionEventCombat.IsEventCombat(opponent, pet));
    }

    [Theory]
    [InlineData(AccessLevel.Counselor)]
    [InlineData(AccessLevel.GameMaster)]
    [InlineData(AccessLevel.Administrator)]
    public void StaffAreExcludedFromTheEventOverride(AccessLevel accessLevel)
    {
        Arena(ExtractionEventPhase.Active);
        var first = Player(0);
        var second = Player(1);
        second.AccessLevel = accessLevel;

        Assert.False(ExtractionEventCombat.IsEventCombat(first, second));
        Assert.False(ExtractionEventCombat.IsEventCombat(second, first));
    }

    [Fact]
    public void EventMonstersAndMissingMobiles_AreExcluded()
    {
        Arena(ExtractionEventPhase.Active);
        var player = Player(0);
        var monster = Track(new Skeleton());
        monster.MoveToWorld(ExtractionDungeonLayout.Rooms[0].PlayerStart, Map.Felucca);

        Assert.False(ExtractionEventCombat.IsEventCombat(player, monster));
        Assert.False(ExtractionEventCombat.IsEventCombat(monster, player));
        Assert.False(ExtractionEventCombat.IsEventCombat(null, player));
        Assert.False(ExtractionEventCombat.IsEventCombat(player, null));
    }

    private ExtractionArenaRegion Arena(ExtractionEventPhase phase)
    {
        var controller = Track(new ExtractionEventController());
        SetPhase(controller, phase);
        var region = new ExtractionArenaRegion(controller);
        region.Register();
        _regions.Add(region);
        return region;
    }

    private PlayerMobile Player(int room)
    {
        var player = Track(new PlayerMobile { Player = true, Body = 0x190, Young = false });
        player.MoveToWorld(ExtractionDungeonLayout.Rooms[room].PlayerStart, Map.Felucca);
        return player;
    }

    private static void SetPhase(ExtractionEventController controller, ExtractionEventPhase phase) =>
        typeof(ExtractionEventController).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, phase);

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}
