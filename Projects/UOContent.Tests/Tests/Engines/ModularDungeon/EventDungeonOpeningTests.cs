using System;
using Server;
using Server.Engines.ModularDungeon;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class EventDungeonOpeningTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(-2000L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue - 100)]
    public void WorkingReplay_RespectsCadenceAndCompletionDeadline_AcrossTickRollover(long started)
    {
        Assert.Equal(0x241, EventDungeonSounds.Working);
        Assert.Equal(0xF01, EventDungeonSounds.Unlocked);
        Assert.Equal(2680, EventDungeonSounds.WorkingCadenceMilliseconds);
        var previousTick = Core.TickCount;
        var nextSoundTick = started + EventDungeonSounds.WorkingCadenceMilliseconds;
        var deadlineTick = started + 6000;

        try
        {
            Core._tickCount = started + EventDungeonSounds.WorkingCadenceMilliseconds - 1;
            Assert.False(EventDungeonOpening.CanReplayWorkingSound(nextSoundTick, deadlineTick));
            Core._tickCount++;
            Assert.True(EventDungeonOpening.CanReplayWorkingSound(nextSoundTick, deadlineTick));
            Core._tickCount = deadlineTick - 1;
            Assert.True(EventDungeonOpening.CanReplayWorkingSound(nextSoundTick, deadlineTick));
            Core._tickCount++;
            Assert.False(EventDungeonOpening.CanReplayWorkingSound(nextSoundTick, deadlineTick));
            Core._tickCount = deadlineTick + 1;
            Assert.False(EventDungeonOpening.CanReplayWorkingSound(nextSoundTick, deadlineTick));
        }
        finally
        {
            Core._tickCount = previousTick;
        }
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(50, 0, 1)]
    [InlineData(100, 0, 2)]
    [InlineData(0, 50, 0.5)]
    [InlineData(0, 100, 1)]
    [InlineData(50, 50, 1.5)]
    [InlineData(100, 100, 3)]
    [InlineData(120, 0, 2)]
    [InlineData(0, 120, 1)]
    [InlineData(120, 120, 3)]
    [InlineData(12.5, 25, 0.5)]
    public void EffectiveSkillValues_ReduceBothDelaysProportionally_WithIndividualCaps(
        double lockpicking, double removeTrap, double reduction
    )
    {
        var player = new Mobile();

        try
        {
            player.Skills[SkillName.Lockpicking].Base = lockpicking;
            player.Skills[SkillName.RemoveTrap].Base = removeTrap;
            Assert.Equal(lockpicking, player.Skills[SkillName.Lockpicking].Value);
            Assert.Equal(removeTrap, player.Skills[SkillName.RemoveTrap].Value);

            Assert.Equal(TimeSpan.FromSeconds(4 - reduction), EventDungeonOpening.GetDelay(player, 4));
            Assert.Equal(TimeSpan.FromSeconds(6 - reduction), EventDungeonOpening.GetDelay(player, 6));
        }
        finally
        {
            player.Delete();
        }
    }

    [Theory]
    [InlineData(-20, -40, 0)]
    [InlineData(-20, 100, 1)]
    [InlineData(100, -40, 2)]
    [InlineData(150, 150, 3)]
    public void EffectiveSkillModifiers_AreClampedBeforeCalculatingReduction(
        double lockpicking, double removeTrap, double reduction
    )
    {
        var player = new Mobile();

        try
        {
            player.AddSkillMod(new DefaultSkillMod(SkillName.Lockpicking, "opening-test-lockpicking", false, lockpicking));
            player.AddSkillMod(new DefaultSkillMod(SkillName.RemoveTrap, "opening-test-remove-trap", false, removeTrap));
            Assert.Equal(Math.Max(0, lockpicking), player.Skills[SkillName.Lockpicking].Value);
            Assert.Equal(Math.Max(0, removeTrap), player.Skills[SkillName.RemoveTrap].Value);

            Assert.Equal(TimeSpan.FromSeconds(4 - reduction), EventDungeonOpening.GetDelay(player, 4));
            Assert.Equal(TimeSpan.FromSeconds(6 - reduction), EventDungeonOpening.GetDelay(player, 6));
        }
        finally
        {
            player.Delete();
        }
    }

    [Fact]
    public void EffectiveSkillBonuses_AreIncludedRatherThanOnlyBaseSkill()
    {
        var player = new Mobile();

        try
        {
            player.Skills[SkillName.Lockpicking].Base = 25;
            player.Skills[SkillName.RemoveTrap].Base = 25;
            player.AddSkillMod(new DefaultSkillMod(SkillName.Lockpicking, "opening-test-lockpicking", true, 25));
            player.AddSkillMod(new DefaultSkillMod(SkillName.RemoveTrap, "opening-test-remove-trap", true, 25));
            Assert.Equal(50, player.Skills[SkillName.Lockpicking].Value);
            Assert.Equal(50, player.Skills[SkillName.RemoveTrap].Value);

            Assert.Equal(TimeSpan.FromSeconds(2.5), EventDungeonOpening.GetDelay(player, 4));
            Assert.Equal(TimeSpan.FromSeconds(4.5), EventDungeonOpening.GetDelay(player, 6));
        }
        finally
        {
            player.Delete();
        }
    }
}
