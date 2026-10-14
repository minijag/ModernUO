using System;
using System.Collections.Generic;
using System.IO;

namespace Server.Engines.PvpEvents;

public sealed record PvpEventScore(
    uint PlayerSerial, string Name, string Guild, string Team, double Points, long Rewards, long Kills,
    long Deaths, long Penalties, long Matches, long Wins, long Healing, string Outcome
);

public sealed record PvpEventResult(
    EventKind Event, uint ControllerSerial, int RoundNumber, DateTime FinishedUtc, string Venue,
    int OrderScore, int ChaosScore, IReadOnlyList<PvpEventScore> Rows
);

public static class PvpEventHistory
{
    internal static PvpEventLedger Ledger { get; set; } = new();

    public static bool Record(PvpEventResult result) => Ledger.Record(result);
    public static PvpEventResult Last(EventKind kind) => Ledger.Last(kind);
    public static IReadOnlyList<PvpEventScore> Rankings(EventKind kind, StatsScope scope, DateTime now) =>
        Ledger.Rankings(kind, scope, now);
    public static string CurrentQuarter(DateTime now) => $"{now.Year} Q{(now.Month - 1) / 3 + 1}";
}

// Historical identity is a value snapshot, not an entity reference that vanishes when a character is deleted.
internal sealed class PvpEventLedger
{
    private readonly List<PvpEventResult> _results = [];
    private readonly HashSet<(EventKind, uint, int)> _recorded = [];
    private readonly Dictionary<EventKind, PvpEventResult> _last = [];
    private readonly Dictionary<(EventKind, int), Dictionary<uint, (PvpEventScore Score, DateTime IdentityUtc)>> _totals = [];

    internal int Count => _results.Count;

    internal bool Record(PvpEventResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var key = (result.Event, result.ControllerSerial, result.RoundNumber);
        if (_recorded.Contains(key))
        {
            return false;
        }

        if (!Enum.IsDefined(result.Event) || result.ControllerSerial == 0 || result.RoundNumber <= 0 ||
            result.FinishedUtc == DateTime.MinValue || result.FinishedUtc.Kind != DateTimeKind.Utc ||
            result.OrderScore < 0 || result.ChaosScore < 0 || result.Rows == null || result.Rows.Count == 0)
        {
            throw new InvalidDataException("A completed PvP result requires a valid round, UTC finish time and participant roster.");
        }

        var serials = new HashSet<uint>();
        var rows = new PvpEventScore[result.Rows.Count];
        for (var i = 0; i < rows.Length; i++)
        {
            var row = result.Rows[i];
            if (row == null || row.PlayerSerial == 0 || !serials.Add(row.PlayerSerial) ||
                !double.IsFinite(row.Points) || row.Points < 0 || row.Rewards < 0 || row.Kills < 0 ||
                row.Deaths < 0 || row.Penalties < 0 || row.Matches != 1 || row.Wins is < 0 or > 1 || row.Healing < 0)
            {
                throw new InvalidDataException("Invalid or duplicate PvP participant result.");
            }
            rows[i] = row with
            {
                Name = row.Name ?? "Unknown", Guild = row.Guild ?? "", Team = row.Team ?? "-", Outcome = row.Outcome ?? ""
            };
        }

        Array.Sort(rows, CompareScores);
        var snapshot = result with { Rows = Array.AsReadOnly(rows), Venue = result.Venue ?? "" };
        _recorded.Add(key);
        _results.Add(snapshot);
        if (!_last.TryGetValue(result.Event, out var previous) || previous.FinishedUtc <= result.FinishedUtc)
        {
            _last[result.Event] = snapshot;
        }

        Accumulate(snapshot, 0);
        Accumulate(snapshot, QuarterId(snapshot.FinishedUtc));
        return true;
    }

    internal PvpEventResult Last(EventKind kind) => _last.GetValueOrDefault(kind);

    internal IReadOnlyList<PvpEventScore> Rankings(EventKind kind, StatsScope scope, DateTime now)
    {
        if (scope == StatsScope.PreviousEvent)
        {
            return Last(kind)?.Rows ?? Array.Empty<PvpEventScore>();
        }

        if (!_totals.TryGetValue((kind, scope == StatsScope.Lifetime ? 0 : QuarterId(now)), out var totals))
        {
            return Array.Empty<PvpEventScore>();
        }

        var rows = new PvpEventScore[totals.Count];
        var index = 0;
        foreach (var total in totals.Values)
        {
            rows[index++] = total.Score;
        }
        Array.Sort(rows, CompareScores);
        return Array.AsReadOnly(rows);
    }

    private void Accumulate(PvpEventResult result, int quarter)
    {
        var key = (result.Event, quarter);
        if (!_totals.TryGetValue(key, out var totals))
        {
            totals = [];
            _totals.Add(key, totals);
        }
        foreach (var row in result.Rows)
        {
            if (!totals.TryGetValue(row.PlayerSerial, out var old))
            {
                totals.Add(row.PlayerSerial, (row with { Team = "-", Outcome = "" }, result.FinishedUtc));
                continue;
            }
            var newerIdentity = result.FinishedUtc >= old.IdentityUtc;
            var identity = newerIdentity ? row : old.Score;
            var updated = identity with
            {
                Team = "-", Outcome = "", Points = Math.Min(double.MaxValue, old.Score.Points + row.Points),
                Rewards = Add(old.Score.Rewards, row.Rewards), Kills = Add(old.Score.Kills, row.Kills),
                Deaths = Add(old.Score.Deaths, row.Deaths), Penalties = Add(old.Score.Penalties, row.Penalties),
                Matches = Add(old.Score.Matches, row.Matches), Wins = Add(old.Score.Wins, row.Wins), Healing = Add(old.Score.Healing, row.Healing)
            };
            totals[row.PlayerSerial] = (updated, newerIdentity ? result.FinishedUtc : old.IdentityUtc);
        }
    }

    private static long Add(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
    private static int QuarterId(DateTime when) => when.Year * 4 + (when.Month - 1) / 3;

    private static int CompareScores(PvpEventScore a, PvpEventScore b)
    {
        var comparison = b.Points.CompareTo(a.Points);
        if (comparison != 0) { return comparison; }
        comparison = b.Wins.CompareTo(a.Wins);
        if (comparison != 0) { return comparison; }
        comparison = b.Kills.CompareTo(a.Kills);
        if (comparison != 0) { return comparison; }
        comparison = a.Penalties.CompareTo(b.Penalties);
        if (comparison != 0) { return comparison; }
        comparison = a.Deaths.CompareTo(b.Deaths);
        return comparison != 0 ? comparison : a.PlayerSerial.CompareTo(b.PlayerSerial);
    }

    internal void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(0);
        writer.WriteEncodedInt(_results.Count);
        foreach (var result in _results)
        {
            writer.WriteEncodedInt((int)result.Event);
            writer.Write(result.ControllerSerial);
            writer.Write(result.RoundNumber);
            writer.Write(result.FinishedUtc);
            writer.Write(result.Venue);
            writer.Write(result.OrderScore);
            writer.Write(result.ChaosScore);
            writer.WriteEncodedInt(result.Rows.Count);
            foreach (var row in result.Rows)
            {
                WriteScore(writer, row);
            }
        }
    }

    internal static PvpEventLedger Deserialize(IGenericReader reader)
    {
        if (reader.ReadEncodedInt() != 0)
        {
            throw new InvalidDataException("Unsupported PvP event history version.");
        }

        var ledger = new PvpEventLedger();
        var count = ReadCount(reader);
        for (var i = 0; i < count; i++)
        {
            var kind = (EventKind)reader.ReadEncodedInt();
            var controller = reader.ReadUInt();
            var round = reader.ReadInt();
            var finish = reader.ReadDateTime();
            var venue = reader.ReadString();
            var order = reader.ReadInt();
            var chaos = reader.ReadInt();
            var rows = new PvpEventScore[ReadCount(reader)];
            for (var j = 0; j < rows.Length; j++)
            {
                rows[j] = ReadScore(reader);
            }
            if (!ledger.Record(new PvpEventResult(kind, controller, round, finish, venue, order, chaos, rows)))
            {
                throw new InvalidDataException("Duplicate completed PvP event in history.");
            }
        }
        return ledger;
    }

    internal static int ReadCount(IGenericReader reader)
    {
        var count = reader.ReadEncodedInt();
        return count is >= 0 and <= 1_000_000 ? count : throw new InvalidDataException("Invalid PvP history collection length.");
    }

    internal static void WriteScore(IGenericWriter writer, PvpEventScore row)
    {
        writer.Write(row.PlayerSerial);
        writer.Write(row.Name);
        writer.Write(row.Guild);
        writer.Write(row.Team);
        writer.Write(row.Points);
        writer.Write(row.Rewards);
        writer.Write(row.Kills);
        writer.Write(row.Deaths);
        writer.Write(row.Penalties);
        writer.Write(row.Matches);
        writer.Write(row.Wins);
        writer.Write(row.Healing);
        writer.Write(row.Outcome);
    }

    internal static PvpEventScore ReadScore(IGenericReader reader) => new(
        reader.ReadUInt(), reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadDouble(),
        reader.ReadLong(), reader.ReadLong(), reader.ReadLong(), reader.ReadLong(), reader.ReadLong(),
        reader.ReadLong(), reader.ReadLong(), reader.ReadString()
    );
}

public sealed class PvpEventHistoryPersistence : GenericPersistence
{
    public static void Configure() => _ = new PvpEventHistoryPersistence();

    public PvpEventHistoryPersistence() : base("PvpEventHistory", 10)
    {
    }

    public override void Serialize(IGenericWriter writer) => PvpEventHistory.Ledger.Serialize(writer);
    public override void Deserialize(IGenericReader reader) => PvpEventHistory.Ledger = PvpEventLedger.Deserialize(reader);
}
