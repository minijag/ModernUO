using System;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Misc;
using Server.Mobiles;
using Server.Regions;
using Server.Spells;
using Xunit;

namespace UOContent.Tests.Regions;

[Collection("Sequential UOContent Tests")]
public sealed class ProtectedRegionSpellDamageTests : IDisposable
{
    private readonly List<Mobile> _mobiles = [];
    private readonly ProtectedRegion _region = new("Spell Protection Test", Map.Felucca, 100,
        new Rectangle3D(1000, 1000, -128, 10, 10, 256));

    public ProtectedRegionSpellDamageTests() => _region.Register();

    public void Dispose()
    {
        foreach (var mobile in _mobiles)
        {
            mobile.Delete();
        }
        _region.Unregister();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ImmediateSpellDamage_RejectsProtectedPlayerInteraction(bool aos, bool sourceProtected)
    {
        var from = Player(sourceProtected ? Inside : Outside);
        var target = Player(sourceProtected ? Outside : Inside);
        var hits = target.Hits;

        if (aos)
        {
            SpellHelper.Damage(null, TimeSpan.Zero, target, from, 15, 100, 0, 0, 0, 0);
        }
        else
        {
            SpellHelper.Damage(null, TimeSpan.Zero, target, from, 15);
        }

        Assert.Equal(hits, target.Hits);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DelayedSpell_RechecksBothEndpointsWhenEitherPlayerEntersProtection(bool aos, bool moveSource)
    {
        var from = Player(Outside);
        var target = Player(new Point3D(995, 1003, 0));
        var timer = DamageTimer(aos, target, from);
        (moveSource ? from : target).MoveToWorld(Inside, Map.Felucca);
        var hits = target.Hits;

        try
        {
            Tick(timer);
            Assert.Equal(hits, target.Hits);
        }
        finally
        {
            timer.Stop();
        }
    }

    [Fact]
    public void LegacyDelayedSpell_StillDamagesPlayersOutsideProtectedAreas()
    {
        var from = Player(Outside);
        var target = Player(new Point3D(995, 1003, 0));
        var timer = DamageTimer(false, target, from);
        var hits = target.Hits;

        try
        {
            Tick(timer);
            Assert.True(target.Hits < hits);
        }
        finally
        {
            timer.Stop();
        }
    }

    [Fact]
    public void MonsterSpellDamage_RemainsAvailableAgainstAProtectedPlayer()
    {
        var monster = new Mobile { Body = 0x190, RawStr = 100, Hits = 100 };
        _mobiles.Add(monster);
        monster.MoveToWorld(Inside, Map.Felucca);
        var target = Player(new Point3D(1004, 1003, 0));
        var hits = target.Hits;

        SpellHelper.Damage(null, TimeSpan.Zero, target, monster, 15);

        Assert.True(target.Hits < hits);
    }

    private PlayerMobile Player(Point3D point)
    {
        var player = new PlayerMobile { Player = true, Body = 0x190 };
        player.RawStr = 100;
        player.Hits = player.HitsMax;
        _mobiles.Add(player);
        player.MoveToWorld(point, Map.Felucca);
        return player;
    }

    private static Point3D Inside => new(1003, 1003, 0);
    private static Point3D Outside => new(996, 1003, 0);

    private static Timer DamageTimer(bool aos, Mobile target, Mobile from)
    {
        var type = typeof(SpellHelper).GetNestedType(aos ? "SpellDamageTimerAOS" : "SpellDamageTimer",
            BindingFlags.NonPublic);
        Assert.NotNull(type);
        object[] arguments = aos
            ? [null, TimeSpan.Zero, target, from, 15, 100, 0, 0, 0, 0, 0, DFAlgorithm.Standard]
            : [null, target, from, 15, TimeSpan.Zero];
        return Assert.IsAssignableFrom<Timer>(Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null));
    }

    private static void Tick(Timer timer)
    {
        var method = timer.GetType().GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(timer, null);
    }
}
