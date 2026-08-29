using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Server.Accounting;

namespace Server.Engines.AccountKeeper;

public static class AccountKeeperBridge
{
    // Keep this outside World.SavePath: world snapshots replace Saves completely during autosave.
    // AppContext.BaseDirectory is ModernUO's Distribution folder for the running shard.
    private static string Folder => Path.Combine(Core.BaseDirectory, "Data", "AccountKeeper");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static TimerExecutionToken _timerToken;

    public static void Configure()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "bridge-status.txt"), $"Bridge configured at {Core.Now:O}");
        EventSink.WorldLoad += PublishAccounts;
        Timer.StartTimer(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), ProcessRequests, out _timerToken);
    }

    private static void ProcessRequests()
    {
        Directory.CreateDirectory(Folder);
        foreach (var file in Directory.EnumerateFiles(Folder, "reset-*.json"))
        {
            try
            {
                var request = JsonSerializer.Deserialize<PasswordResetRequest>(File.ReadAllText(file));
                if (request?.Username is { Length: > 0 } username && request.Argon2Hash is { Length: > 0 } hash &&
                    Accounts.GetAccount(username) is Account account)
                {
                    account.SetArgon2PasswordHashForMaintenance(hash);
                    File.WriteAllText(Path.ChangeExtension(file, ".done"), $"Password reset for {account.Username} at {Core.Now:O}.");
                }
                else
                {
                    File.WriteAllText(Path.ChangeExtension(file, ".error"), "Account not found or request was invalid.");
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.ChangeExtension(file, ".error"), ex.Message);
            }
            finally
            {
                File.Delete(file);
            }
        }

        PublishAccounts();
    }

    private static void PublishAccounts()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var accounts = Accounts.GetAccounts().OfType<Account>().OrderBy(a => a.Username, StringComparer.OrdinalIgnoreCase)
                .Select(a => new AccountSummary(a.Username, a.AccessLevel.ToString(), a.Count, a.LastLogin, a.Banned)).ToList();
            File.WriteAllText(Path.Combine(Folder, "accounts.json"), JsonSerializer.Serialize(accounts, JsonOptions));
            File.WriteAllText(Path.Combine(Folder, "bridge-status.txt"), $"Published {accounts.Count} accounts at {Core.Now:O}");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Folder, "bridge-error.txt"), ex.ToString());
        }
    }

    private sealed record PasswordResetRequest(string Username, string Argon2Hash);
    private sealed record AccountSummary(string Username, string AccessLevel, int Characters, DateTime LastLogin, bool Banned);
}
