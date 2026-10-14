using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Server.Accounting;
using Server.Logging;
using Server.Mobiles;
using Server.Systems.FeatureFlags;

namespace Server.Engines.Labrynth;

public static class YoungStatusRetirement
{
    internal const string InstallationId = "young-status-retirement-v1";
    internal const string FeatureFlagKey = "young_player_system";
    private const int RetiredAccountBit = 1 << 1;
    private const int YoungPlayerBit = (int)PlayerFlag.Young;
    private const int BatchSize = 64;
    private sealed record Marker(string InstallationId);
    internal sealed record PlayerSnapshot(uint Serial, int Flags, bool Offline);
    internal sealed record AccountSnapshot(uint Serial, int Flags, PlayerSnapshot[] Players, uint[] CharacterReferences);
    internal sealed record Archive(string InstallationId, string FeatureFlagKey, AccountSnapshot[] Accounts);
    private sealed record Target(Account Account, AccountSnapshot Snapshot, PlayerMobile[] Players, Mobile[] Characters,
        int DuplicatePlayerReferences, int NonPlayerCharacterReferencesSkipped, int MismatchedOwnerPlayerReferences);
    internal sealed record Result(int AccountsChecked, int CharactersChecked, int OfflineCharactersChecked,
        int AccountYoungFlagsCleared, int PlayerYoungFlagsCleared, int RemainingRawYoungAccounts, int RemainingRawYoungPlayers,
        int DuplicatePlayerReferences, int NonPlayerCharacterReferencesSkipped, int MismatchedOwnerPlayerReferences);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(YoungStatusRetirement));
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "YoungStatusRetirement");
        if (!File.Exists(Path.Combine(directory, "install.pending")) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }
        Timer.StartTimer(() => Run(directory));
    }

    private static async void Run(string directory)
    {
        try
        {
            var markerPath = Path.Combine(directory, "install.pending");
            Require(new FileInfo(markerPath).Length is > 0 and <= 1024, "The retirement marker has an invalid size.");
            var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllBytes(markerPath), _options);
            Require(marker is { InstallationId: InstallationId }, "The marker must authorize this exact young-status retirement.");
            FeatureFlagManager.Initialize();
            Require(FeatureFlagManager.GetFlag(FeatureFlagKey) is { Enabled: false }, "The configured young-player feature must be disabled first.");
            var result = await EnsureAsync(Path.Combine(directory, "before.json"), null, true);
            var receipt = new
            {
                Success = true,
                InstallationId,
                FeatureFlagKey,
                FeatureEnabled = false,
                result.AccountsChecked,
                result.CharactersChecked,
                result.OfflineCharactersChecked,
                result.AccountYoungFlagsCleared,
                result.PlayerYoungFlagsCleared,
                result.RemainingRawYoungAccounts,
                result.RemainingRawYoungPlayers,
                result.DuplicatePlayerReferences,
                result.NonPlayerCharacterReferencesSkipped,
                result.MismatchedOwnerPlayerReferences,
                AccountOwnershipAndSlotsUnchanged = true,
                UnrelatedFlagsPreserved = true,
                AwaitingWorldSave = true,
                Archive = "Data/YoungStatusRetirement/before.json"
            };
            WriteJson(Path.Combine(directory, "installed.json"), receipt);
            WriteJson(Path.Combine(directory, "result.json"), receipt);
            _logger.Information("Retired stored young status for {Accounts} accounts and {Characters} characters; awaiting world save",
                result.AccountsChecked, result.CharactersChecked);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Young-status retirement refused");
            WriteJson(Path.Combine(directory, "result.json"), new { Success = false, InstallationId, Error = exception.Message });
        }
    }

    internal static Result Ensure(string archivePath, IEnumerable<IAccount> source = null) =>
        EnsureAsync(archivePath, source, false).GetAwaiter().GetResult();

    private static async Task<Result> EnsureAsync(string archivePath, IEnumerable<IAccount> source, bool yieldTicks)
    {
        Require(!ContentFeatureFlags.YoungPlayerSystem, "The young-player feature must be disabled before clearing stored flags.");
        var accounts = new List<Account>();
        var accountSerials = new HashSet<uint>();
        foreach (var entry in source ?? Accounts.GetAccounts())
        {
            Require(entry is Account { Deleted: false } account && ReferenceEquals(Accounts.FindAccount(account.Serial), account),
                "The retirement source contains an unregistered or unsupported account.");
            var candidate = (Account)entry;
            Require(accountSerials.Add(candidate.Serial.Value), "The retirement source repeats an account.");
            accounts.Add(candidate);
        }
        var targets = new List<Target>(accounts.Count);
        var playersBySerial = new Dictionary<uint, PlayerMobile>();
        var duplicatePlayerReferences = 0;
        var nonPlayerCharacterReferencesSkipped = 0;
        var mismatchedOwnerPlayerReferences = 0;
        for (var i = 0; i < accounts.Count; i++)
        {
            var target = Capture(accounts[i], playersBySerial);
            targets.Add(target);
            duplicatePlayerReferences += target.DuplicatePlayerReferences;
            nonPlayerCharacterReferencesSkipped += target.NonPlayerCharacterReferencesSkipped;
            mismatchedOwnerPlayerReferences += target.MismatchedOwnerPlayerReferences;
            if (yieldTicks && (i + 1) % BatchSize == 0)
            {
                await Timer.Pause(1);
            }
        }
        var snapshots = new AccountSnapshot[targets.Count];
        for (var i = 0; i < targets.Count; i++)
        {
            snapshots[i] = targets[i].Snapshot;
        }
        if (File.Exists(archivePath))
        {
            Require(new FileInfo(archivePath).Length is > 0 and <= 33554432, "The retirement archive has an invalid size.");
            var archive = JsonSerializer.Deserialize<Archive>(File.ReadAllBytes(archivePath), _options);
            ValidateArchive(archive, snapshots);
        }
        Require(!ContentFeatureFlags.YoungPlayerSystem, "The young-player feature was enabled during preflight.");
        Require(source != null || Accounts.Count == accounts.Count, "The account registry changed during preflight.");
        foreach (var target in targets)
        {
            ValidateTarget(target);
        }
        if (!File.Exists(archivePath))
        {
            using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, new Archive(InstallationId, FeatureFlagKey, snapshots), _options);
            stream.Flush(true);
        }
        var clearedAccounts = 0;
        var clearedPlayers = 0;
        var characters = 0;
        var offlineCharacters = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            Require(!ContentFeatureFlags.YoungPlayerSystem, "The young-player feature was enabled during retirement.");
            ValidateTarget(target);
            if (!target.Account.GetFlag(1))
            {
                clearedAccounts++;
            }
            target.Account.Young = false;
            foreach (var player in target.Players)
            {
                characters++;
                if (player.NetState == null)
                {
                    offlineCharacters++;
                }
                // Young getters mask stored flags when the feature is disabled.
                if (player.GetFlag(PlayerFlag.Young))
                {
                    player.Young = false;
                    player.MarkDirty();
                    clearedPlayers++;
                }
            }
            Require(target.Account.Flags == (target.Snapshot.Flags | RetiredAccountBit), "An unrelated account flag changed.");
            for (var j = 0; j < target.Players.Length; j++)
            {
                Require((int)target.Players[j].Flags == (target.Snapshot.Players[j].Flags & ~YoungPlayerBit),
                    "An unrelated character flag changed.");
            }
            if (yieldTicks && (i + 1) % BatchSize == 0)
            {
                await Timer.Pause(1);
            }
        }
        foreach (var target in targets)
        {
            Require(target.Account.GetFlag(1), "An account retains stored young eligibility.");
            foreach (var player in target.Players)
            {
                Require(!player.GetFlag(PlayerFlag.Young), "A character retains stored young status.");
            }
        }
        return new Result(targets.Count, characters, offlineCharacters, clearedAccounts, clearedPlayers, 0, 0,
            duplicatePlayerReferences, nonPlayerCharacterReferencesSkipped, mismatchedOwnerPlayerReferences);
    }

    private static Target Capture(Account account, Dictionary<uint, PlayerMobile> playersBySerial)
    {
        var players = new List<PlayerMobile>();
        var snapshots = new List<PlayerSnapshot>();
        var characters = new List<Mobile>();
        var references = new List<uint>();
        var duplicates = 0;
        var nonPlayers = 0;
        var mismatchedOwners = 0;
        // The account enumerator reads live slots without the indexer's deleted-slot cleanup.
        foreach (var mobile in account)
        {
            characters.Add(mobile);
            references.Add(mobile.Serial.Value);
            if (mobile is not PlayerMobile player)
            {
                nonPlayers++;
                continue;
            }
            Require(ReferenceEquals(World.FindMobile(player.Serial), player), "An account references a conflicting or unregistered player identity.");
            if (!ReferenceEquals(player.Account, account))
            {
                mismatchedOwners++;
            }
            // Young belongs to the mobile, even when multiple account slots reference it.
            if (playersBySerial.TryGetValue(player.Serial.Value, out var existing))
            {
                Require(ReferenceEquals(existing, player), "One player serial refers to conflicting object identities.");
                duplicates++;
                continue;
            }
            playersBySerial.Add(player.Serial.Value, player);
            players.Add(player);
            snapshots.Add(new PlayerSnapshot(player.Serial.Value, (int)player.Flags, player.NetState == null));
        }
        return new Target(account, new AccountSnapshot(account.Serial.Value, account.Flags, snapshots.ToArray(), references.ToArray()),
            players.ToArray(), characters.ToArray(), duplicates, nonPlayers, mismatchedOwners);
    }

    private static void ValidateTarget(Target target)
    {
        Require(!target.Account.Deleted && ReferenceEquals(Accounts.FindAccount(target.Account.Serial), target.Account) &&
            target.Account.Flags == target.Snapshot.Flags, "An account changed after retirement preflight.");
        var index = 0;
        foreach (var mobile in target.Account)
        {
            Require(index < target.Characters.Length && ReferenceEquals(mobile, target.Characters[index]),
                "An account character reference changed after retirement preflight.");
            index++;
        }
        Require(index == target.Characters.Length, "An account character disappeared after retirement preflight.");
        for (var i = 0; i < target.Players.Length; i++)
        {
            var player = target.Players[i];
            Require(!player.Deleted && ReferenceEquals(World.FindMobile(player.Serial), player) &&
                (int)player.Flags == target.Snapshot.Players[i].Flags, "A player identity or flags changed after retirement preflight.");
        }
    }

    private static void ValidateArchive(Archive archive, AccountSnapshot[] current)
    {
        Require(archive is { InstallationId: InstallationId, FeatureFlagKey: FeatureFlagKey, Accounts: not null } &&
            archive.Accounts.Length == current.Length, "The retirement archive has another identity or account set.");
        var prior = new Dictionary<uint, AccountSnapshot>();
        foreach (var account in archive.Accounts)
        {
            Require(account?.Players != null && account.CharacterReferences != null && prior.TryAdd(account.Serial, account),
                "The retirement archive has duplicate or incomplete accounts.");
        }
        foreach (var account in current)
        {
            Require(prior.TryGetValue(account.Serial, out var original) && (original.Flags | RetiredAccountBit) == (account.Flags | RetiredAccountBit) &&
                original.CharacterReferences.Length == account.CharacterReferences.Length,
                "An account differs from the durable retirement archive.");
            for (var i = 0; i < account.CharacterReferences.Length; i++)
            {
                Require(original.CharacterReferences[i] == account.CharacterReferences[i], "Account character references differ from the durable archive.");
            }
        }
        var players = new Dictionary<uint, PlayerSnapshot>();
        foreach (var account in archive.Accounts)
        {
            foreach (var player in account.Players)
            {
                Require(player != null && players.TryAdd(player.Serial, player), "The retirement archive has duplicate or incomplete characters.");
            }
        }
        var matched = 0;
        foreach (var account in current)
        {
            foreach (var player in account.Players)
            {
                Require(players.TryGetValue(player.Serial, out var before) && (before.Flags & ~YoungPlayerBit) == (player.Flags & ~YoungPlayerBit),
                    "A character differs from the durable retirement archive.");
                matched++;
            }
        }
        Require(matched == players.Count, "The durable retirement archive has another unique player set.");
    }

    private static void WriteJson<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(value, _options));
        File.Move(temporary, path, true);
    }

    private static void Require(bool condition, string error)
    {
        if (!condition)
        {
            throw new InvalidOperationException(error);
        }
    }
}
