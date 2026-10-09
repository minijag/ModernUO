using System;
using System.Collections.Generic;
using System.IO;
using Server.Guilds;
using Server.Json;
using Server.Mobiles;

namespace Server.Engines.Custom;

public readonly record struct GuildProgress(long GoldFarmed, long BossesKilled, long StandingPoints)
{
    public int Level => GuildProgressService.GetLevel(StandingPoints);
    public int PointsToNextLevel => GuildProgressService.GetPointsToNextLevel(StandingPoints);
}

public sealed class GuildStandingConfiguration
{
    public string[] BossTypes { get; set; } = [];
}

public static class GuildProgressService
{
    public const int PointsPerBoss = 1;
    public const int PointsPerLevel = 10;

    private static readonly Dictionary<Serial, GuildProgress> _progress = new();
    private static readonly HashSet<string> _bossTypes = new(StringComparer.Ordinal);

    public static void Configure()
    {
        var path = Path.Combine(Core.BaseDirectory, "Configuration", "GuildProgress", "bosses.json");
        var configuration = JsonConfig.Deserialize<GuildStandingConfiguration>(path);
        if (configuration == null)
        {
            configuration = new GuildStandingConfiguration();
            JsonConfig.Serialize(path, configuration);
        }

        SetBossTypes(configuration.BossTypes);
    }

    internal static void SetBossTypes(IEnumerable<string> bossTypes)
    {
        _bossTypes.Clear();
        if (bossTypes == null)
        {
            return;
        }

        foreach (var name in bossTypes)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _bossTypes.Add(name.Trim());
            }
        }
    }

    // Only explicitly configured creature types award standing; difficulty alone is not a boss designation.
    public static bool IsStandingBoss(BaseCreature creature) => creature is
        { Deleted: false, Controlled: false, Summoned: false } && _bossTypes.Contains(creature.GetType().FullName);

    public static GuildProgress GetProgress(Guild guild) => guild is { Disbanded: false } &&
        _progress.TryGetValue(guild.Serial, out var progress) ? progress : default;

    public static int GetLevel(long standingPoints) =>
        (int)Math.Min(int.MaxValue, 1 + Math.Max(0, standingPoints) / PointsPerLevel);

    public static int GetPointsToNextLevel(long standingPoints) =>
        PointsPerLevel - (int)(Math.Max(0, standingPoints) % PointsPerLevel);

    internal static long AddTotal(long total, long amount) =>
        amount <= 0 ? Math.Max(0, total) : Math.Max(0, total) > long.MaxValue - amount ? long.MaxValue : Math.Max(0, total) + amount;

    public static void RecordGold(PlayerMobile player, long amount)
    {
        if (amount <= 0 || player is not { Deleted: false, Guild: Guild { Disbanded: false } guild } || !guild.IsMember(player))
        {
            return;
        }

        var progress = GetProgress(guild);
        _progress[guild.Serial] = progress with { GoldFarmed = AddTotal(progress.GoldFarmed, amount) };
    }

    public static bool RecordBossKill(PlayerMobile player, BaseCreature creature)
    {
        if (!IsStandingBoss(creature) || player is not { Deleted: false, Guild: Guild { Disbanded: false } guild } ||
            !guild.IsMember(player) || !creature.TryCreditGuildStanding())
        {
            return false;
        }

        var progress = GetProgress(guild);
        _progress[guild.Serial] = progress with
        {
            BossesKilled = AddTotal(progress.BossesKilled, 1),
            StandingPoints = AddTotal(progress.StandingPoints, PointsPerBoss)
        };
        return true;
    }

    public static void RemoveGuild(Guild guild) => _progress.Remove(guild.Serial);

    internal static void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(0);
        writer.WriteEncodedInt(_progress.Count);
        foreach (var (serial, progress) in _progress)
        {
            writer.Write(serial);
            writer.Write(progress.GoldFarmed);
            writer.Write(progress.BossesKilled);
            writer.Write(progress.StandingPoints);
        }
    }

    internal static void Deserialize(IGenericReader reader)
    {
        var version = reader.ReadEncodedInt();
        if (version != 0)
        {
            throw new InvalidDataException("Unsupported guild progress version.");
        }

        _progress.Clear();
        var count = reader.ReadEncodedInt();
        for (var i = 0; i < count; i++)
        {
            var guild = reader.ReadEntity<Guild>();
            var gold = Math.Max(0, reader.ReadLong());
            var bosses = Math.Max(0, reader.ReadLong());
            var standing = Math.Max(0, reader.ReadLong());
            if (guild is { Disbanded: false })
            {
                _progress[guild.Serial] = new GuildProgress(gold, bosses, standing);
            }
        }
    }
}

public sealed class GuildProgressPersistence : GenericPersistence
{
    private static GuildProgressPersistence _instance;

    public static void Configure() => _instance = new GuildProgressPersistence();

    public GuildProgressPersistence() : base("GuildProgress", 10)
    {
    }

    public override void Serialize(IGenericWriter writer) => GuildProgressService.Serialize(writer);

    public override void Deserialize(IGenericReader reader) => GuildProgressService.Deserialize(reader);
}
