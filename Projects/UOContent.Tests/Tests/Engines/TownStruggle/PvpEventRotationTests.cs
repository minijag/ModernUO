using System;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Engines.ModularDungeon;
using Server.Engines.TownStruggle;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

[Collection("Sequential UOContent Tests")]
public class PvpEventRotationTests : IDisposable
{
    private readonly List<Item> _created = [];
    private readonly DateTime _now = Core.Now;
    private readonly long _ticks = Core.TickCount;
    private readonly bool _decorationBusy = ExtractionLobbyDecoration.IsBusy;

    public PvpEventRotationTests() => Timer.Init(Core.TickCount);

    public void Dispose()
    {
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            _created[index].Delete();
        }
        Core._now = _now;
        Core._tickCount = _ticks;
        SetDecorationBusy(_decorationBusy);
        Timer.Init(Core.TickCount);
    }

    [Fact]
    public void RotationAlternatesOnlyAfterCompleteRoundsAndRespectsWaitingInterval()
    {
        var (rotation, integration) = Create();
        Assert.Equal(PvpRotatingEvent.Extraction, rotation.NextEvent);
        Assert.Equal(0, integration.ExtractionStarts);
        rotation.Advance();
        Assert.Equal(0, integration.ExtractionStarts);
        Core._now = rotation.NextDeadline;
        rotation.Advance();
        Assert.Equal(PvpRotatingEvent.Extraction, rotation.ActiveEvent);
        Assert.Equal(1, integration.ExtractionStarts);
        Assert.True(integration.AuthorizedExtraction);

        Core._now += TimeSpan.FromHours(3);
        rotation.Advance();
        Assert.Equal(0, integration.TownStarts);
        Assert.Equal(0, rotation.CompletedSlots);
        integration.ExtractionAvailability = PvpEventAvailability.Idle;
        rotation.Advance();
        Assert.Equal(PvpRotatingEvent.None, rotation.ActiveEvent);
        Assert.Equal(PvpRotatingEvent.TownStruggle, rotation.NextEvent);
        Assert.Equal(Core.Now, rotation.NextDeadline);
        Assert.Equal(1, rotation.CompletedSlots);
        Core._now = rotation.NextDeadline;
        rotation.Advance();
        Assert.Equal(1, integration.TownStarts);
        Assert.True(integration.AuthorizedTown);
        Assert.Equal(PvpRotatingEvent.TownStruggle, rotation.ActiveEvent);

        // Results and signup both remain busy until Town has returned everyone and become dormant.
        Core._now += TimeSpan.FromHours(1);
        rotation.Advance();
        Assert.Equal(1, integration.ExtractionStarts);
        integration.TownAvailability = PvpEventAvailability.Idle;
        rotation.Advance();
        Assert.Equal(2, rotation.CompletedSlots);
        Assert.Equal(PvpRotatingEvent.Extraction, rotation.NextEvent);
        Assert.Equal(Core.Now + TimeSpan.FromMinutes(15), rotation.NextDeadline);
    }

    [Fact]
    public void EmptyExtractionSlotAlternatesOnceAndOpensTownSignupOnTheNextTick()
    {
        var (rotation, integration) = Create();
        integration.EmptyExtraction = true;
        Core._now = rotation.NextDeadline + TimeSpan.FromDays(2);
        rotation.Advance();
        Assert.Equal(1, integration.ExtractionStarts);
        Assert.Equal(1, rotation.CompletedSlots);
        Assert.Equal(PvpRotatingEvent.TownStruggle, rotation.NextEvent);
        Assert.Equal(Core.Now, rotation.NextDeadline);
        rotation.Advance();
        Assert.Equal(1, integration.TownStarts);
    }

    [Theory]
    [InlineData((int)PvpEventAvailability.Busy)]
    [InlineData((int)PvpEventAvailability.Faulted)]
    [InlineData((int)PvpEventAvailability.Unavailable)]
    public void EnableRejectsUnfinishedOrUnavailableEvents(int availabilityValue)
    {
        var availability = (PvpEventAvailability)availabilityValue;
        var extraction = Track(new ExtractionEventController());
        var town = Track(new TownStruggleController());
        var rotation = Track(new PvpEventRotationController());
        var integration = new FakeIntegration { ExtractionAvailability = availability };
        Assert.False(rotation.TryEnable(extraction, town, integration, TimeSpan.FromMinutes(15), out _));
        Assert.False(rotation.Enabled);
        integration.ExtractionAvailability = PvpEventAvailability.Idle;
        integration.TownAvailability = availability;
        Assert.False(rotation.TryEnable(extraction, town, integration, TimeSpan.FromMinutes(15), out _));
        Assert.False(rotation.Enabled);
        Assert.Equal(0, integration.ExtractionStarts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FaultPausesWithoutStartingOrRecoveringEitherEvent(bool extractionFault)
    {
        var (rotation, integration) = Create();
        if (extractionFault)
        {
            integration.ExtractionAvailability = PvpEventAvailability.Faulted;
        }
        else
        {
            integration.TownAvailability = PvpEventAvailability.Faulted;
        }
        Core._now = rotation.NextDeadline;
        rotation.Advance();
        Assert.True(rotation.Paused);
        Assert.True(rotation.Enabled);
        Assert.False(rotation.TryDisable(out _));
        Assert.Equal(0, integration.ExtractionStarts + integration.TownStarts);
        integration.ExtractionAvailability = PvpEventAvailability.Idle;
        integration.TownAvailability = PvpEventAvailability.Idle;
        Core._now += TimeSpan.FromDays(1);
        rotation.Advance();
        Assert.Equal(0, integration.ExtractionStarts + integration.TownStarts);
        Assert.True(rotation.TryDisable(out _));
    }

    [Fact]
    public void FailedTownStartPausesWithoutConsumingOrSkippingItsSlot()
    {
        var (rotation, integration) = Create();
        integration.EmptyExtraction = true;
        Core._now = rotation.NextDeadline;
        rotation.Advance();
        integration.FailTown = true;
        Core._now = rotation.NextDeadline;
        rotation.Advance();
        Assert.True(rotation.Paused);
        Assert.Equal(PvpRotatingEvent.TownStruggle, rotation.ActiveEvent);
        Assert.Equal(1, rotation.CompletedSlots);
        Assert.Equal(1, integration.TownStarts);
    }

    [Fact]
    public void ExternalStartAndUnexpectedAutomaticSchedulerPauseRatherThanOverlap()
    {
        var (rotation, integration) = Create();
        integration.TownAvailability = PvpEventAvailability.Busy;
        rotation.Advance();
        Assert.True(rotation.Paused);
        Assert.Equal(0, integration.ExtractionStarts);
        integration.TownAvailability = PvpEventAvailability.Idle;
        Assert.True(rotation.TryDisable(out _));
        Assert.True(rotation.TryEnable(rotation.Extraction, rotation.Town, integration, TimeSpan.FromMinutes(15), out _));
        rotation.Extraction.ConfigureWaiting(TimeSpan.FromMinutes(1), true);
        rotation.Advance();
        Assert.True(rotation.Paused);
        Assert.Equal(0, integration.ExtractionStarts);
    }

    [Fact]
    public void DisableRequiresIdleAndRestoresPreviousExtractionSchedule()
    {
        var (rotation, integration) = Create(true);
        Assert.False(rotation.Extraction.AutomaticRounds);
        Core._now = rotation.NextDeadline;
        rotation.Advance();
        Assert.False(rotation.TryDisable(out _));
        Assert.True(rotation.Enabled);
        integration.ExtractionAvailability = PvpEventAvailability.Idle;
        Assert.True(rotation.TryDisable(out _));
        Assert.False(rotation.Enabled);
        Assert.True(rotation.Extraction.AutomaticRounds);
        Assert.Equal(TimeSpan.FromMinutes(7), rotation.Extraction.WaitingInterval);
        Core._now += TimeSpan.FromDays(2);
        rotation.Advance();
        Assert.Equal(1, integration.ExtractionStarts);
    }

    [Fact]
    public void SaveLoadRetainsOwnedActiveSlotAndDoesNotReplayStart()
    {
        var (rotation, integration) = Create();
        Core._now = rotation.NextDeadline;
        rotation.Advance();
        var extraction = rotation.Extraction;
        var town = rotation.Town;
        var bytes = Snapshot(rotation);
        rotation.Delete();
        var restored = Track(new PvpEventRotationController(World.NewItem));
        restored.Deserialize(new BufferReader(bytes));
        integration.Rotation = restored;
        restored.BindIntegration(integration);
        Core._now += TimeSpan.FromHours(1);
        restored.Advance();
        Assert.True(restored.Enabled);
        Assert.Same(extraction, restored.Extraction);
        Assert.Same(town, restored.Town);
        Assert.Equal(PvpRotatingEvent.Extraction, restored.ActiveEvent);
        Assert.Equal(1, integration.ExtractionStarts);
        Assert.Equal(0, integration.TownStarts);
        integration.ExtractionAvailability = PvpEventAvailability.Idle;
        restored.Advance();
        Assert.Equal(PvpRotatingEvent.TownStruggle, restored.NextEvent);
        Assert.Equal(1, restored.CompletedSlots);
    }

    [Fact]
    public void SaveLoadPreservesPauseAndDoesNotRecoverAutomatically()
    {
        var (rotation, integration) = Create();
        integration.TownAvailability = PvpEventAvailability.Faulted;
        rotation.Advance();
        var bytes = Snapshot(rotation);
        rotation.Delete();
        var restored = Track(new PvpEventRotationController(World.NewItem));
        restored.Deserialize(new BufferReader(bytes));
        integration.TownAvailability = PvpEventAvailability.Idle;
        restored.BindIntegration(integration);
        Core._now += TimeSpan.FromDays(1);
        restored.Advance();
        Assert.True(restored.Paused);
        Assert.Equal(0, integration.ExtractionStarts + integration.TownStarts);
    }

    [Fact]
    public void DeleteCancelsTimerAndClearsReferencesWithoutDeletingEvents()
    {
        var (rotation, _) = Create();
        var extraction = rotation.Extraction;
        var town = rotation.Town;
        var timer = typeof(PvpEventRotationController).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(((TimerExecutionToken)timer.GetValue(rotation)).Running);
        rotation.Delete();
        Assert.False(((TimerExecutionToken)timer.GetValue(rotation)).Running);
        Assert.Null(rotation.Extraction);
        Assert.Null(rotation.Town);
        Assert.False(extraction.Deleted);
        Assert.False(town.Deleted);
    }

    [Fact]
    public void EnabledCoordinatorBlocksDirectControllerStartsOutsideItsDispatch()
    {
        var (rotation, integration) = Create();
        PvpEventRotationRuntime.Restore(rotation);
        rotation.BindIntegration(integration);
        Assert.True(PvpEventRotationRuntime.IsEnabled);
        Assert.False(PvpEventRotationRuntime.CanStartExtraction(rotation.Extraction, out _));
        Assert.False(PvpEventRotationRuntime.CanStartTown(rotation.Town, out _));
        Assert.False(rotation.Extraction.TryStart(out var error));
        Assert.Contains("rotation owns", error);
        Assert.False(rotation.AllowsDispatch(PvpRotatingEvent.Extraction));
        Core._now = rotation.NextDeadline;
        rotation.Advance();
        Assert.True(integration.AuthorizedExtraction);
        Assert.False(rotation.AllowsDispatch(PvpRotatingEvent.Extraction));
        Assert.False(PvpEventRotationRuntime.CanStartExtraction(rotation.Extraction, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LobbyInstallationPreservesDueSlotAndResumesItOnce(bool townNext)
    {
        var (rotation, integration) = Create();
        if (townNext)
        {
            integration.EmptyExtraction = true;
            Core._now = rotation.NextDeadline;
            rotation.Advance();
        }
        var next = rotation.NextEvent;
        var deadline = rotation.NextDeadline;
        var completed = rotation.CompletedSlots;
        var extractionStarts = integration.ExtractionStarts;
        var townStarts = integration.TownStarts;
        SetDecorationBusy(true);
        integration.ExtractionAvailability = PvpEventAvailability.Unavailable;
        Core._now = deadline + TimeSpan.FromSeconds(20);
        rotation.Advance();
        rotation.Advance();
        Assert.True(rotation.Enabled);
        Assert.False(rotation.Paused);
        Assert.Equal(PvpRotatingEvent.None, rotation.ActiveEvent);
        Assert.Equal(next, rotation.NextEvent);
        Assert.Equal(deadline, rotation.NextDeadline);
        Assert.Equal(completed, rotation.CompletedSlots);
        Assert.Equal(extractionStarts, integration.ExtractionStarts);
        Assert.Equal(townStarts, integration.TownStarts);

        integration.ExtractionAvailability = PvpEventAvailability.Idle;
        SetDecorationBusy(false);
        rotation.Advance();
        rotation.Advance();
        Assert.False(rotation.Paused);
        Assert.Equal(next, rotation.ActiveEvent);
        Assert.Equal(extractionStarts + (townNext ? 0 : 1), integration.ExtractionStarts);
        Assert.Equal(townStarts + (townNext ? 1 : 0), integration.TownStarts);
    }

    [Fact]
    public void LobbyInstallationPreservesRestoredActiveTownSlotWithoutFalseFault()
    {
        var (rotation, integration) = Create();
        integration.EmptyExtraction = true;
        Core._now = rotation.NextDeadline;
        rotation.Advance();
        rotation.Advance();
        Assert.Equal(PvpRotatingEvent.TownStruggle, rotation.ActiveEvent);
        var completed = rotation.CompletedSlots;
        var next = rotation.NextEvent;
        var bytes = Snapshot(rotation);
        rotation.Delete();
        SetDecorationBusy(true);
        integration.ExtractionAvailability = PvpEventAvailability.Unavailable;
        var restored = Track(new PvpEventRotationController(World.NewItem));
        restored.Deserialize(new BufferReader(bytes));
        integration.Rotation = restored;
        restored.BindIntegration(integration);
        restored.Advance();
        Assert.False(restored.Paused);
        Assert.True(restored.Enabled);
        Assert.Equal(PvpRotatingEvent.TownStruggle, restored.ActiveEvent);
        Assert.Equal(next, restored.NextEvent);
        Assert.Equal(DateTime.MinValue, restored.NextDeadline);
        Assert.Equal(completed, restored.CompletedSlots);
        Assert.Equal(1, integration.TownStarts);

        integration.ExtractionAvailability = PvpEventAvailability.Idle;
        SetDecorationBusy(false);
        restored.Advance();
        Assert.False(restored.Paused);
        Assert.Equal(PvpRotatingEvent.TownStruggle, restored.ActiveEvent);
        Assert.Equal(1, integration.TownStarts);
        integration.TownAvailability = PvpEventAvailability.Idle;
        restored.Advance();
        Assert.Equal(completed + 1, restored.CompletedSlots);
        Assert.Equal(PvpRotatingEvent.Extraction, restored.NextEvent);
    }

    [Fact]
    public void LobbyInstallationBlocksDirectStartsWithoutConfiguringRotation()
    {
        var extraction = Track(new ExtractionEventController());
        var town = Track(new TownStruggleController());
        SetDecorationBusy(true);
        Assert.False(PvpEventRotationRuntime.CanStartExtraction(extraction, out var extractionError));
        Assert.Contains("lobby installation", extractionError);
        Assert.False(PvpEventRotationRuntime.CanStartTown(town, out var townError));
        Assert.Contains("lobby installation", townError);
        Assert.False(extraction.TryStart(out extractionError));
        Assert.Contains("lobby installation", extractionError);
        Assert.False(town.TryOpenSignup(out townError));
        Assert.Contains("lobby installation", townError);
    }

    [Fact]
    public void RealUnavailableEventStillPausesAfterLobbyInstallationReleases()
    {
        var (rotation, integration) = Create();
        SetDecorationBusy(true);
        integration.ExtractionAvailability = PvpEventAvailability.Unavailable;
        Core._now = rotation.NextDeadline;
        rotation.Advance();
        Assert.False(rotation.Paused);
        SetDecorationBusy(false);
        rotation.Advance();
        Assert.True(rotation.Paused);
        Assert.Equal(0, integration.ExtractionStarts + integration.TownStarts);
    }

    private static void SetDecorationBusy(bool value) =>
        typeof(ExtractionLobbyDecoration).GetProperty(nameof(ExtractionLobbyDecoration.IsBusy))!.SetValue(null, value);

    private (PvpEventRotationController Rotation, FakeIntegration Integration) Create(bool previousAutomatic = false)
    {
        var extraction = Track(new ExtractionEventController());
        extraction.ConfigureWaiting(TimeSpan.FromMinutes(7), previousAutomatic);
        var town = Track(new TownStruggleController());
        var rotation = Track(new PvpEventRotationController());
        var integration = new FakeIntegration { Rotation = rotation };
        Assert.True(rotation.TryEnable(extraction, town, integration, TimeSpan.FromMinutes(15), out var error), error);
        return (rotation, integration);
    }

    private T Track<T>(T item) where T : Item
    {
        _created.Add(item);
        return item;
    }

    private static byte[] Snapshot(Item item)
    {
        var writer = new BufferWriter(true);
        item.Serialize(writer);
        return writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
    }

    private sealed class FakeIntegration : IPvpEventRotationIntegration
    {
        public PvpEventRotationController Rotation { get; set; }
        public PvpEventAvailability ExtractionAvailability { get; set; }
        public PvpEventAvailability TownAvailability { get; set; }
        public string FailureReason => "Test event needs recovery.";
        public int ExtractionStarts { get; private set; }
        public int TownStarts { get; private set; }
        public bool EmptyExtraction { get; set; }
        public bool FailTown { get; set; }
        public bool AuthorizedExtraction { get; private set; }
        public bool AuthorizedTown { get; private set; }

        public bool TryStartExtraction(out string error)
        {
            ExtractionStarts++;
            AuthorizedExtraction = Rotation.AllowsDispatch(PvpRotatingEvent.Extraction);
            error = EmptyExtraction ? "No players waiting." : null;
            if (!EmptyExtraction)
            {
                ExtractionAvailability = PvpEventAvailability.Busy;
            }
            return !EmptyExtraction;
        }

        public bool TryStartTown(out string error)
        {
            TownStarts++;
            AuthorizedTown = Rotation.AllowsDispatch(PvpRotatingEvent.TownStruggle);
            error = FailTown ? "Test signup cannot open." : null;
            if (!FailTown)
            {
                TownAvailability = PvpEventAvailability.Busy;
            }
            return !FailTown;
        }
    }
}
