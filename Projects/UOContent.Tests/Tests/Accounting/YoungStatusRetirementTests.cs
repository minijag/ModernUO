using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server.Accounting;
using Server.Accounting.Security;
using Server.Engines.Labrynth;
using Server.Mobiles;
using Server.Systems.FeatureFlags;
using Xunit;

namespace Server.Tests.Accounting;

[Collection("Sequential UOContent Tests")]
public sealed class YoungStatusRetirementTests : IDisposable
{
    private readonly bool _originalFeature = ContentFeatureFlags.YoungPlayerSystem;
    private readonly PasswordProtectionAlgorithm _originalAlgorithm = AccountSecurity.CurrentAlgorithm;
    private readonly List<Account> _accounts = [];
    private readonly List<PlayerMobile> _players = [];
    private readonly List<Mobile> _nonPlayers = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"modernuo-young-retirement-{Guid.NewGuid():N}");
    private string ArchivePath => Path.Combine(_directory, "before.json");

    public YoungStatusRetirementTests()
    {
        ContentFeatureFlags.YoungPlayerSystem = false;
        AccountSecurity.CurrentAlgorithm = PasswordProtectionAlgorithm.SHA2;
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        foreach (var player in _players)
        {
            player.Account = null;
            player.Delete();
        }
        foreach (var account in _accounts)
        {
            account.Young = false;
            Accounts.Remove(account);
        }
        foreach (var mobile in _nonPlayers)
        {
            mobile.Account = null;
            mobile.Delete();
        }
        ContentFeatureFlags.YoungPlayerSystem = _originalFeature;
        AccountSecurity.CurrentAlgorithm = _originalAlgorithm;
        File.Delete(ArchivePath);
        Directory.Delete(_directory);
    }

    [Fact]
    public void Retirement_ClearsRawOfflineFlagsAcrossEveryAccountSlot_PreservesOtherState()
    {
        var first = CreateAccount();
        var second = CreateAccount();
        first.Flags = 0x101;
        second.Flags = 0x82;
        Assert.True(first.DepositGold(123));
        var a = CreatePlayer(first, 0, PlayerFlag.Young | PlayerFlag.Masonry | PlayerFlag.RefuseTrades);
        var b = CreatePlayer(first, 6, PlayerFlag.Young | PlayerFlag.KarmaLocked);
        var c = CreatePlayer(second, 3, PlayerFlag.Glassblowing);
        a.LogoutLocation = new Point3D(5592, 1352, 0);
        a.LogoutMap = Map.Felucca;
        var bank = a.BankBox;
        var password = first.Password;
        Assert.False(first.Young);
        Assert.False(a.Young);
        Assert.True(a.GetFlag(PlayerFlag.Young));

        var result = YoungStatusRetirement.Ensure(ArchivePath, new IAccount[] { first, second });

        Assert.Equal(2, result.AccountsChecked);
        Assert.Equal(3, result.CharactersChecked);
        Assert.Equal(3, result.OfflineCharactersChecked);
        Assert.Equal(1, result.AccountYoungFlagsCleared);
        Assert.Equal(2, result.PlayerYoungFlagsCleared);
        Assert.Equal(0, result.RemainingRawYoungAccounts);
        Assert.Equal(0, result.RemainingRawYoungPlayers);
        Assert.Equal(0x103, first.Flags);
        Assert.Equal(0x82, second.Flags);
        Assert.Equal(PlayerFlag.Masonry | PlayerFlag.RefuseTrades, a.Flags);
        Assert.Equal(PlayerFlag.KarmaLocked, b.Flags);
        Assert.Equal(PlayerFlag.Glassblowing, c.Flags);
        Assert.Same(first, a.Account);
        Assert.Same(a, first[0]);
        Assert.Same(b, first[6]);
        Assert.Same(c, second[3]);
        Assert.Same(bank, a.BankBox);
        Assert.Equal(new Point3D(5592, 1352, 0), a.LogoutLocation);
        Assert.Same(Map.Felucca, a.LogoutMap);
        Assert.Equal(password, first.Password);
        Assert.Equal(123, first.TotalGold);
        var text = File.ReadAllText(ArchivePath);
        Assert.DoesNotContain(first.Username, text);
        Assert.DoesNotContain(second.Username, text);
        Assert.DoesNotContain(password, text);
        Assert.DoesNotContain("Username", text);
        Assert.DoesNotContain("Password", text);
        Assert.DoesNotContain("Email", text);
        var archive = JsonSerializer.Deserialize<YoungStatusRetirement.Archive>(text);
        Assert.Equal(YoungStatusRetirement.InstallationId, archive.InstallationId);
        Assert.Equal(0x101, archive.Accounts[0].Flags);
        Assert.True((archive.Accounts[0].Players[0].Flags & (int)PlayerFlag.Young) != 0);
    }

    [Fact]
    public void EnabledFeature_RefusesBeforeAnyMutationOrReceipt()
    {
        var account = CreateAccount();
        var player = CreatePlayer(account, 0, PlayerFlag.Young | PlayerFlag.Masonry);
        var accountFlags = account.Flags;
        ContentFeatureFlags.YoungPlayerSystem = true;

        Assert.Throws<InvalidOperationException>(() => YoungStatusRetirement.Ensure(ArchivePath, new IAccount[] { account }));

        Assert.Equal(accountFlags, account.Flags);
        Assert.Equal(PlayerFlag.Young | PlayerFlag.Masonry, player.Flags);
        Assert.False(File.Exists(ArchivePath));
    }

    [Fact]
    public void StaleAndMissingOwnership_ClearsFlagsWithoutRepairingOwnersOrSlots()
    {
        var first = CreateAccount();
        var second = CreateAccount();
        var a = CreatePlayer(first, 0, PlayerFlag.Young);
        var b = CreatePlayer(second, 0, PlayerFlag.Young);
        b.Account = first;
        a.Account = null;

        var result = YoungStatusRetirement.Ensure(ArchivePath, new IAccount[] { first, second });

        Assert.True(first.GetFlag(1));
        Assert.True(second.GetFlag(1));
        Assert.False(a.GetFlag(PlayerFlag.Young));
        Assert.False(b.GetFlag(PlayerFlag.Young));
        Assert.Null(a.Account);
        Assert.Same(first, b.Account);
        Assert.Same(a, first[0]);
        Assert.Same(b, second[0]);
        Assert.Equal(2, result.MismatchedOwnerPlayerReferences);
    }

    [Fact]
    public void SharedPlayerReferences_AreClearedOnceAndRemainInEveryOriginalSlot()
    {
        var first = CreateAccount();
        var second = CreateAccount();
        var player = CreatePlayer(first, 0, PlayerFlag.Young | PlayerFlag.Masonry);
        first[5] = player;
        second[2] = player;
        var owner = player.Account;

        var result = YoungStatusRetirement.Ensure(ArchivePath, new IAccount[] { first, second });
        var before = File.ReadAllBytes(ArchivePath);
        var replay = YoungStatusRetirement.Ensure(ArchivePath, new IAccount[] { second, first });

        Assert.Equal(1, result.CharactersChecked);
        Assert.Equal(1, result.PlayerYoungFlagsCleared);
        Assert.Equal(2, result.DuplicatePlayerReferences);
        Assert.Equal(2, result.MismatchedOwnerPlayerReferences);
        Assert.Equal(0, replay.PlayerYoungFlagsCleared);
        Assert.Equal(before, File.ReadAllBytes(ArchivePath));
        Assert.Same(player, first[0]);
        Assert.Same(player, first[5]);
        Assert.Same(player, second[2]);
        Assert.Same(owner, player.Account);
        Assert.Equal(PlayerFlag.Masonry, player.Flags);
    }

    [Fact]
    public void NonPlayerAccountReference_IsCountedAndPreservedWithoutYoungFlagMutation()
    {
        var account = CreateAccount();
        var player = CreatePlayer(account, 1, PlayerFlag.Young);
        var other = new Mobile();
        _nonPlayers.Add(other);
        account[0] = other;

        var result = YoungStatusRetirement.Ensure(ArchivePath, new IAccount[] { account });

        Assert.Equal(1, result.NonPlayerCharacterReferencesSkipped);
        Assert.Equal(1, result.CharactersChecked);
        Assert.False(player.GetFlag(PlayerFlag.Young));
        Assert.Same(other, account[0]);
        Assert.Same(account, other.Account);
        Assert.False(other.Deleted);
    }

    [Fact]
    public void UnregisteredPlayerIdentity_RefusesBeforeAnyAccountOrPlayerMutation()
    {
        var first = CreateAccount();
        var second = CreateAccount();
        var a = CreatePlayer(first, 0, PlayerFlag.Young);
        var b = CreatePlayer(second, 0, PlayerFlag.Young);
        World.RemoveEntity(b);
        try
        {
            Assert.Throws<InvalidOperationException>(() => YoungStatusRetirement.Ensure(ArchivePath, new IAccount[] { first, second }));
            Assert.False(first.GetFlag(1));
            Assert.False(second.GetFlag(1));
            Assert.True(a.GetFlag(PlayerFlag.Young));
            Assert.True(b.GetFlag(PlayerFlag.Young));
            Assert.False(File.Exists(ArchivePath));
        }
        finally
        {
            World.AddEntity(b);
        }
    }

    [Fact]
    public void Replay_IsIdempotentAndPreservesOriginalBeforeReceipt()
    {
        var account = CreateAccount();
        var player = CreatePlayer(account, 5, PlayerFlag.Young | PlayerFlag.SandMining);
        YoungStatusRetirement.Ensure(ArchivePath, new IAccount[] { account });
        var before = File.ReadAllBytes(ArchivePath);

        var result = YoungStatusRetirement.Ensure(ArchivePath, new IAccount[] { account });

        Assert.Equal(0, result.AccountYoungFlagsCleared);
        Assert.Equal(0, result.PlayerYoungFlagsCleared);
        Assert.Equal(before, File.ReadAllBytes(ArchivePath));
        Assert.Equal(PlayerFlag.SandMining, player.Flags);
        account.SetFlag(6, true);
        Assert.Throws<InvalidOperationException>(() => YoungStatusRetirement.Ensure(ArchivePath, new IAccount[] { account }));
        Assert.Equal(before, File.ReadAllBytes(ArchivePath));
    }

    [Fact]
    public void DisabledFeature_PreventsFutureEffectiveYoungAndCharacterCreationAssignment()
    {
        var account = CreateAccount();
        var player = CreatePlayer(account, 0, PlayerFlag.Masonry);

        Assert.False(account.Young);
        Assert.False(player.Young);
        if (player.AccessLevel == AccessLevel.Player && (player.Account as Account)?.Young == true)
        {
            player.Young = true;
        }
        Assert.False(player.GetFlag(PlayerFlag.Young));
        Assert.False(File.Exists(ArchivePath));
    }

    private Account CreateAccount()
    {
        var account = new Account($"young-retirement-{Guid.NewGuid():N}", "test-only-password");
        _accounts.Add(account);
        return account;
    }

    private PlayerMobile CreatePlayer(Account account, int slot, PlayerFlag flags)
    {
        var player = new PlayerMobile { Flags = flags };
        account[slot] = player;
        _players.Add(player);
        return player;
    }
}
