using System;
using System.IO;
using Server;
using Server.Engines.PvpEvents;
using Xunit;

namespace UOContent.Tests.Engines.PvpEvents;

public class PvpEventHistoryTests
{
    private static readonly DateTime Finish = new(2026, 10, 13, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DuplicateFinalizationCannotAwardTotalsTwice()
    {
        var ledger = new PvpEventLedger();
        var result = Result();
        Assert.True(ledger.Record(result));
        Assert.False(ledger.Record(result with { Rows = [Score(points: 999)] }));
        Assert.Equal(1, ledger.Count);
        var row = Assert.Single(ledger.Rankings(EventKind.TownStruggle, StatsScope.Lifetime, Finish));
        Assert.Equal(20, row.Points);
        Assert.Equal(1, row.Matches);
        Assert.Equal(3, row.Rewards);
    }

    [Fact]
    public void CalendarQuarterRollsOverWithoutErasingLifetimeOrOldResults()
    {
        var ledger = new PvpEventLedger();
        var september = new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc);
        ledger.Record(Result() with { FinishedUtc = september });
        ledger.Record(Result() with { RoundNumber = 2, Rows = [Score(points: 35)] });
        Assert.Equal(20, Assert.Single(ledger.Rankings(EventKind.TownStruggle, StatsScope.Seasonal, september)).Points);
        Assert.Equal(35, Assert.Single(ledger.Rankings(EventKind.TownStruggle, StatsScope.Seasonal, Finish)).Points);
        Assert.Equal(55, Assert.Single(ledger.Rankings(EventKind.TownStruggle, StatsScope.Lifetime, Finish)).Points);
        Assert.Empty(ledger.Rankings(EventKind.TownStruggle, StatsScope.Seasonal, Finish.AddMonths(3)));
        Assert.Equal(2, ledger.Last(EventKind.TownStruggle).RoundNumber);
    }

    [Theory]
    [InlineData(1, "2026 Q1")]
    [InlineData(3, "2026 Q1")]
    [InlineData(4, "2026 Q2")]
    [InlineData(6, "2026 Q2")]
    [InlineData(7, "2026 Q3")]
    [InlineData(9, "2026 Q3")]
    [InlineData(10, "2026 Q4")]
    [InlineData(12, "2026 Q4")]
    public void SeasonLabelsAreQuarterly(int month, string expected) =>
        Assert.Equal(expected, PvpEventHistory.CurrentQuarter(new DateTime(2026, month, 1, 0, 0, 0, DateTimeKind.Utc)));

    [Fact]
    public void EventsAndControllersHaveIndependentRoundKeys()
    {
        var ledger = new PvpEventLedger();
        ledger.Record(Result());
        ledger.Record(Result() with { ControllerSerial = 999 });
        ledger.Record(Result() with { Event = EventKind.Extraction, Rows = [Score(points: 1)] });
        Assert.Equal(2, Assert.Single(ledger.Rankings(EventKind.TownStruggle, StatsScope.Lifetime, Finish)).Matches);
        Assert.Equal(1, Assert.Single(ledger.Rankings(EventKind.Extraction, StatsScope.Lifetime, Finish)).Matches);
        Assert.Equal(3, ledger.Count);
    }

    [Fact]
    public void ResultCopiesRosterAndPreservesIdentityWithoutLiveMobileReferences()
    {
        var ledger = new PvpEventLedger();
        var original = Score() with { Name = "Historical name", Guild = "OLD" };
        var rows = new[] { original };
        ledger.Record(Result() with { Rows = rows });
        rows[0] = Score(points: 999);
        Assert.Equal(original, Assert.Single(ledger.Last(EventKind.TownStruggle).Rows));
        ledger.Record(Result() with { RoundNumber = 2, Rows = [Score() with { Name = "New name", Guild = "NEW" }] });
        Assert.Equal("New name", Assert.Single(ledger.Rankings(EventKind.TownStruggle, StatsScope.Lifetime, Finish)).Name);
    }

    [Fact]
    public void SaveReloadRetainsAllHistoryTotalsAndDeduplication()
    {
        var ledger = new PvpEventLedger();
        ledger.Record(Result() with { FinishedUtc = Finish.AddMonths(-3) });
        ledger.Record(Result() with { RoundNumber = 2 });
        ledger.Record(Result() with { Event = EventKind.Extraction, Rows = [Score(points: 1)] });
        var bytes = Save(ledger);
        var reader = new BufferReader(bytes);
        var loaded = PvpEventLedger.Deserialize(reader);
        Assert.Equal(bytes.Length, reader.Position);
        Assert.Equal(3, loaded.Count);
        Assert.False(loaded.Record(Result()));
        Assert.Equal(2, loaded.Last(EventKind.TownStruggle).RoundNumber);
        Assert.Equal(1, loaded.Last(EventKind.Extraction).Rows[0].Points);
        Assert.Equal(40, Assert.Single(loaded.Rankings(EventKind.TownStruggle, StatsScope.Lifetime, Finish)).Points);
        Assert.Equal(20, Assert.Single(loaded.Rankings(EventKind.TownStruggle, StatsScope.Seasonal, Finish)).Points);
        Assert.Equal(bytes, Save(loaded));
    }

    [Fact]
    public void LatestMeansFinishTimeNotRecordArrivalOrder()
    {
        var ledger = new PvpEventLedger();
        ledger.Record(Result() with { RoundNumber = 2 });
        ledger.Record(Result() with { FinishedUtc = Finish.AddDays(-1) });
        Assert.Equal(2, ledger.Last(EventKind.TownStruggle).RoundNumber);
    }

    [Theory]
    [InlineData(StatsScope.Seasonal)]
    [InlineData(StatsScope.Lifetime)]
    public void DelayedOlderResultCannotOverwriteLatestStandingsIdentity(StatsScope scope)
    {
        var ledger = new PvpEventLedger();
        ledger.Record(Result() with { RoundNumber = 2, Rows = [Score(points: 35) with { Name = "Latest name", Guild = "NEW" }] });
        ledger.Record(Result() with
        {
            FinishedUtc = Finish.AddDays(-1), Rows = [Score() with { Name = "Earlier name", Guild = "OLD" }]
        });
        var row = Assert.Single(ledger.Rankings(EventKind.TownStruggle, scope, Finish));
        Assert.Equal(55, row.Points);
        Assert.Equal(2, row.Matches);
        Assert.Equal("Latest name", row.Name);
        Assert.Equal("NEW", row.Guild);
        Assert.Equal(2, ledger.Last(EventKind.TownStruggle).RoundNumber);
        var loaded = PvpEventLedger.Deserialize(new BufferReader(Save(ledger)));
        Assert.Equal(row, Assert.Single(loaded.Rankings(EventKind.TownStruggle, scope, Finish)));
    }

    [Fact]
    public void InvalidRowsCannotPartiallyPublishResult()
    {
        var ledger = new PvpEventLedger();
        Assert.Throws<InvalidDataException>(() => ledger.Record(Result() with { Rows = [Score(), Score()] }));
        Assert.Throws<InvalidDataException>(() => ledger.Record(Result() with { Rows = [Score(points: double.NaN)] }));
        Assert.Throws<InvalidDataException>(() => ledger.Record(Result() with { Rows = [] }));
        Assert.Equal(0, ledger.Count);
        Assert.Null(ledger.Last(EventKind.TownStruggle));
        Assert.Empty(ledger.Rankings(EventKind.TownStruggle, StatsScope.Lifetime, Finish));
    }

    [Fact]
    public void AllRankedPlayersRemainAvailableAndTiesAreDeterministic()
    {
        var rows = new PvpEventScore[25];
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = Score((uint)(25 - i));
        }
        var ledger = new PvpEventLedger();
        ledger.Record(Result() with { Rows = rows });
        var rankings = ledger.Rankings(EventKind.TownStruggle, StatsScope.Lifetime, Finish);
        Assert.Equal(25, rankings.Count);
        for (var i = 0; i < rankings.Count; i++)
        {
            Assert.Equal((uint)(i + 1), rankings[i].PlayerSerial);
        }
    }

    private static byte[] Save(PvpEventLedger ledger)
    {
        var writer = new BufferWriter(true);
        try
        {
            ledger.Serialize(writer);
            return writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
        }
        finally
        {
            writer.Close();
        }
    }

    private static PvpEventResult Result() => new(EventKind.TownStruggle, 123, 1, Finish, "Galven", 100, 90, [Score()]);
    private static PvpEventScore Score(uint serial = 1, double points = 20) =>
        new(serial, "Test player", "TEST", "Order", points, 3, 2, 1, 0, 1, 1, 15, "Win");
}
