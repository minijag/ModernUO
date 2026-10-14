using System;
using System.Collections.Generic;
using System.IO;
using Server.Engines.PvpEvents;

namespace Server.Engines.ModularDungeon;

public static class ExtractionEventStatistics
{
    private enum Outcome
    {
        Pending,
        Extracted,
        Death,
        TimeExpired,
        Unavailable
    }

    private sealed record Entry(uint Serial, string Name, string Guild, Outcome Outcome, long Kills, long Deaths);

    private sealed record Round(int Number, DateTime Deadline, Dictionary<uint, Entry> Players);

    private static readonly Dictionary<uint, Round> _rounds = [];

    internal static void Begin(ExtractionEventController controller)
    {
        if (controller.Phase != ExtractionEventPhase.Active || controller.Participants.Count == 0)
        {
            return;
        }

        var players = new Dictionary<uint, Entry>(controller.Participants.Count);
        foreach (var player in controller.Participants)
        {
            if (player != null)
            {
                players.Add(player.Serial.Value, new Entry(player.Serial.Value, Text(player.Name, 80),
                    Text(player.Guild?.Abbreviation, 40), Outcome.Pending, 0, 0));
            }
        }
        if (players.Count == 0)
        {
            Discard(controller);
            return;
        }
        _rounds[controller.Serial.Value] = new Round(controller.RoundNumber, controller.MatchDeadline, players);
    }

    internal static void RecordFinish(ExtractionEventController controller, Mobile player, ExtractionFinishReason reason)
    {
        if (!TryGetRound(controller, out var round) || player == null ||
            !round.Players.TryGetValue(player.Serial.Value, out var entry) || entry.Outcome != Outcome.Pending)
        {
            return;
        }

        var outcome = reason switch
        {
            ExtractionFinishReason.Extracted => Outcome.Extracted,
            ExtractionFinishReason.Death => Outcome.Death,
            ExtractionFinishReason.TimeExpired => Outcome.TimeExpired,
            _ => Outcome.Unavailable
        };
        round.Players[entry.Serial] = entry with
        {
            Outcome = outcome,
            Deaths = outcome is Outcome.Death or Outcome.TimeExpired ? 1 : entry.Deaths
        };
    }

    internal static void RecordDeath(ExtractionEventController controller, Mobile victim, Mobile killer)
    {
        if (!TryGetRound(controller, out var round) || victim == null ||
            !round.Players.TryGetValue(victim.Serial.Value, out var entry) || entry.Outcome != Outcome.Pending || entry.Deaths != 0)
        {
            return;
        }

        // Extraction has one elimination per roster entry; native death callbacks and recovery polling must not double-count it.
        round.Players[entry.Serial] = entry with { Deaths = 1 };
        if (killer != null && killer != victim && round.Players.TryGetValue(killer.Serial.Value, out var attacker) &&
            attacker.Outcome == Outcome.Pending)
        {
            round.Players[attacker.Serial] = attacker with { Kills = checked(attacker.Kills + 1) };
        }
    }

    internal static void RecordUnavailable(ExtractionEventController controller, Mobile player)
    {
        if (TryGetRound(controller, out var round) && player != null &&
            round.Players.TryGetValue(player.Serial.Value, out var entry) && entry.Outcome == Outcome.Pending)
        {
            round.Players[entry.Serial] = entry with { Outcome = Outcome.Unavailable };
        }
    }

    internal static bool Complete(ExtractionEventController controller, DateTime finishedUtc)
    {
        if (controller.Phase != ExtractionEventPhase.Finishing || controller.FinishReason != ExtractionFinishReason.TimeExpired ||
            controller.Participants.Count != 0 || !TryGetRound(controller, out var round) || finishedUtc < round.Deadline)
        {
            return false;
        }

        var rows = new PvpEventScore[round.Players.Count];
        var index = 0;
        foreach (var entry in round.Players.Values)
        {
            var extracted = entry.Outcome == Outcome.Extracted;
            var outcome = entry.Outcome switch
            {
                Outcome.Extracted => "Extracted",
                Outcome.Death => "Died",
                Outcome.TimeExpired => "Time expired",
                _ => "Unavailable"
            };
            rows[index++] = new PvpEventScore(entry.Serial, entry.Name, entry.Guild, "", extracted ? 1 : 0,
                0, entry.Kills, entry.Deaths, 0, 1, extracted ? 1 : 0, 0, outcome);
        }
        var recorded = PvpEventHistory.Record(new PvpEventResult(EventKind.Extraction, controller.Serial.Value, round.Number,
            round.Deadline, "Extraction Dungeon", 0, 0, rows));
        _rounds.Remove(controller.Serial.Value);
        return recorded;
    }

    internal static void Discard(ExtractionEventController controller) => _rounds.Remove(controller.Serial.Value);

    private static bool TryGetRound(ExtractionEventController controller, out Round round) =>
        _rounds.TryGetValue(controller.Serial.Value, out round) && round.Number == controller.RoundNumber &&
        round.Deadline == controller.MatchDeadline;

    private static string Text(string value, int maximum) => string.IsNullOrEmpty(value) ? "" : value[..Math.Min(value.Length, maximum)];

    internal static void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(0);
        writer.WriteEncodedInt(_rounds.Count);
        foreach (var (serial, round) in _rounds)
        {
            writer.Write(serial);
            writer.Write(round.Number);
            writer.Write(round.Deadline);
            writer.WriteEncodedInt(round.Players.Count);
            foreach (var entry in round.Players.Values)
            {
                writer.Write(entry.Serial);
                writer.Write(entry.Name);
                writer.Write(entry.Guild);
                writer.WriteEncodedInt((int)entry.Outcome);
                writer.Write(entry.Kills);
                writer.Write(entry.Deaths);
            }
        }
    }

    internal static void Deserialize(IGenericReader reader)
    {
        if (reader.ReadEncodedInt() != 0)
        {
            throw new InvalidDataException("Unsupported extraction statistics version.");
        }

        var loaded = new Dictionary<uint, Round>();
        var count = ReadCount(reader);
        var totalPlayers = 0;
        for (var i = 0; i < count; i++)
        {
            var serial = reader.ReadUInt();
            var number = reader.ReadInt();
            var deadline = reader.ReadDateTime();
            var playerCount = ReadCount(reader);
            totalPlayers = checked(totalPlayers + playerCount);
            if (!((Serial)serial).IsItem || number <= 0 || deadline == DateTime.MinValue || totalPlayers > 1_000_000)
            {
                throw new InvalidDataException("Invalid extraction statistics round.");
            }
            var players = new Dictionary<uint, Entry>();
            for (var j = 0; j < playerCount; j++)
            {
                var playerSerial = reader.ReadUInt();
                var name = reader.ReadString();
                var guild = reader.ReadString();
                var outcome = (Outcome)reader.ReadEncodedInt();
                var kills = reader.ReadLong();
                var deaths = reader.ReadLong();
                if (!((Serial)playerSerial).IsMobile || name is not { Length: <= 80 } ||
                    guild is not { Length: <= 40 } || outcome is < Outcome.Pending or > Outcome.Unavailable ||
                    kills < 0 || kills >= playerCount || deaths is < 0 or > 1 ||
                    !players.TryAdd(playerSerial, new Entry(playerSerial, name, guild, outcome, kills, deaths)))
                {
                    throw new InvalidDataException("Invalid extraction statistics player.");
                }
            }
            if (!loaded.TryAdd(serial, new Round(number, deadline, players)))
            {
                throw new InvalidDataException("Duplicate extraction statistics round.");
            }
        }

        _rounds.Clear();
        foreach (var (serial, round) in loaded)
        {
            _rounds.Add(serial, round);
        }
    }

    private static int ReadCount(IGenericReader reader)
    {
        var count = reader.ReadEncodedInt();
        return count is >= 0 and <= 1_000_000 ? count : throw new InvalidDataException("Invalid extraction statistics count.");
    }
}

public sealed class ExtractionEventStatisticsPersistence : GenericPersistence
{
    public static void Configure() => _ = new ExtractionEventStatisticsPersistence();

    public ExtractionEventStatisticsPersistence() : base("ExtractionEventStatistics", 10)
    {
    }

    public override void Serialize(IGenericWriter writer) => ExtractionEventStatistics.Serialize(writer);

    public override void Deserialize(IGenericReader reader) => ExtractionEventStatistics.Deserialize(reader);
}
