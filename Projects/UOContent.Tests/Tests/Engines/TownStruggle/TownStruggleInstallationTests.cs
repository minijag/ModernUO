using System;
using System.Collections.Generic;
using System.IO;
using Server;
using Server.Engines.ModularDungeon;
using Server.Engines.TownStruggle;
using Server.Items;
using Server.Mobiles;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

[Collection("Sequential UOContent Tests")]
public class TownStruggleInstallationTests : IDisposable
{
    private readonly List<IEntity> _created = [];

    public void Dispose()
    {
        for (var i = _created.Count - 1; i >= 0; i--)
        {
            _created[i].Delete();
        }
    }

    [Fact]
    public void AbsentPending_IsInert()
    {
        Assert.False(TownStruggleInstallation.HasPending(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Theory]
    [InlineData(ExtractionEventPhase.Faulted, true, 1, 0)]
    [InlineData(ExtractionEventPhase.Faulted, true, 0, 1)]
    [InlineData(ExtractionEventPhase.Waiting, false, 0, 0)]
    [InlineData(ExtractionEventPhase.Waiting, true, 0, 0)]
    public void Preflight_OnlyAuthorizedSafeStatesAccepted(ExtractionEventPhase phase, bool recovery, int players, int queued)
    {
        TownStruggleInstallation.ValidatePreflight(TownStruggleInstallation.ApprovedExtractionSerial,
            phase, players, queued, recovery, false, false);
    }

    [Theory]
    [InlineData(ExtractionEventPhase.Active, true, 1, 0)]
    [InlineData(ExtractionEventPhase.Starting, true, 0, 1)]
    [InlineData(ExtractionEventPhase.Finishing, true, 1, 0)]
    [InlineData(ExtractionEventPhase.Faulted, false, 1, 0)]
    [InlineData(ExtractionEventPhase.Faulted, false, 0, 0)]
    [InlineData(ExtractionEventPhase.Waiting, true, 1, 0)]
    [InlineData(ExtractionEventPhase.Waiting, true, 0, 1)]
    [InlineData(ExtractionEventPhase.Faulted, true, 129, 0)]
    [InlineData(ExtractionEventPhase.Faulted, true, 0, 129)]
    [InlineData(ExtractionEventPhase.Faulted, true, -1, 0)]
    [InlineData(ExtractionEventPhase.Faulted, true, 0, -1)]
    public void Preflight_RefusesUnsafeOrExcessiveStates(ExtractionEventPhase phase, bool recovery, int players, int queued)
    {
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.ValidatePreflight(
            TownStruggleInstallation.ApprovedExtractionSerial, phase, players, queued, recovery, false, false));
    }

    [Theory]
    [InlineData(1u, false, false)]
    [InlineData(TownStruggleInstallation.ApprovedExtractionSerial, true, false)]
    [InlineData(TownStruggleInstallation.ApprovedExtractionSerial, false, true)]
    public void Preflight_RefusesWrongIdentityOrExistingOwnership(uint serial, bool townExists, bool rotationExists)
    {
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.ValidatePreflight(serial,
            ExtractionEventPhase.Faulted, 1, 0, true, townExists, rotationExists));
    }

    [Theory]
    [InlineData("different", TownStruggleInstallation.ApprovedSha256, TownStruggleInstallation.ApprovedExtractionSerial)]
    [InlineData(TownStruggleInstallation.InstallationId, "wrong-hash", TownStruggleInstallation.ApprovedExtractionSerial)]
    [InlineData(TownStruggleInstallation.InstallationId, TownStruggleInstallation.ApprovedSha256, 1u)]
    [InlineData(TownStruggleInstallation.InstallationId, TownStruggleInstallation.ApprovedSha256, TownStruggleInstallation.ApprovedExtractionSerial)]
    public void Approval_RequiresExactIdentityAndPinnedBytes(string id, string hash, uint serial)
    {
        var marker = new TownStruggleInstallation.Marker(id, hash, serial, true);
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.ValidateApproval(marker, "{}"u8.ToArray()));
    }

    [Fact]
    public void Approval_RejectsMissingMarker()
    {
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.ValidateApproval(null, "{}"u8.ToArray()));
    }

    [Fact]
    public void Snapshot_PreservesNestedBackpackAndEquippedItems()
    {
        var player = Player();
        var pack = Add(new Backpack());
        player.AddItem(pack);
        var bag = Add(new Bag());
        pack.DropItem(bag);
        var coins = Add(new Gold(123));
        bag.DropItem(coins);
        var robe = Add(new Robe());
        player.AddItem(robe);

        var snapshot = TownStruggleInstallation.Capture(player, true);
        Assert.Equal(4, snapshot.Possessions.Length);
        Assert.Contains(snapshot.Possessions, item => item.Serial == coins.Serial.Value && item.ParentSerial == bag.Serial.Value && item.Amount == 123);
        Assert.Contains(snapshot.Possessions, item => item.Serial == robe.Serial.Value && item.ParentSerial == player.Serial.Value);
        TownStruggleInstallation.ValidatePreserved(snapshot, false);
    }

    [Fact]
    public void Snapshot_DoesNotEnumerateBankContents()
    {
        var player = Player();
        var bank = player.BankBox;
        bank.DropItem(Add(new Gold(900)));
        Assert.Empty(TownStruggleInstallation.Capture(player, false).Possessions);
    }

    [Fact]
    public void Snapshot_RejectsHoldingItem()
    {
        var player = Player();
        player.Holding = Add(new Gold(1));
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.Capture(player, true));
        player.Holding = null;
    }

    [Fact]
    public void Recovery_RejectsRemovedInventoryIdentity()
    {
        var player = Player();
        var pack = Add(new Backpack());
        player.AddItem(pack);
        var gold = Add(new Gold(123));
        pack.DropItem(gold);
        var snapshot = TownStruggleInstallation.Capture(player, true);
        gold.Delete();
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.ValidatePreserved(snapshot, false));
    }

    [Fact]
    public void Recovery_RejectsChangedAmount()
    {
        var player = Player();
        var pack = Add(new Backpack());
        player.AddItem(pack);
        var gold = Add(new Gold(123));
        pack.DropItem(gold);
        var snapshot = TownStruggleInstallation.Capture(player, true);
        gold.Amount--;
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.ValidatePreserved(snapshot, false));
    }

    [Fact]
    public void Recovery_RejectsSubstitutionWithIdenticalItemType()
    {
        var player = Player();
        var robe = Add(new Robe());
        player.AddItem(robe);
        var snapshot = TownStruggleInstallation.Capture(player, true);
        robe.Delete();
        player.AddItem(Add(new Robe()));
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.ValidatePreserved(snapshot, false));
    }

    [Fact]
    public void Recovery_RejectsUnexpectedCorpse()
    {
        var player = Player();
        var snapshot = TownStruggleInstallation.Capture(player, true);
        player.Corpse = Add(new Bag());
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.ValidatePreserved(snapshot, false));
    }

    [Fact]
    public void Recovery_RequiresParticipantToReachLobby()
    {
        var player = Player();
        player.LogoutMap = Map.Felucca;
        player.LogoutLocation = new Point3D(5260, 820, 0);
        var snapshot = TownStruggleInstallation.Capture(player, true);
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.ValidatePreserved(snapshot, true));
        player.LogoutLocation = ExtractionDungeonLayout.LobbyEntry;
        TownStruggleInstallation.ValidatePreserved(snapshot, true);
    }

    [Fact]
    public void Recovery_DoesNotMoveQueueOnlyPlayer()
    {
        var player = Player();
        var snapshot = TownStruggleInstallation.Capture(player, false);
        player.LogoutLocation = ExtractionDungeonLayout.LobbyEntry;
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.ValidatePreserved(snapshot, true));
    }

    [Fact]
    public void Snapshot_RefusesDeletedPlayer()
    {
        var player = Player();
        player.Delete();
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.Capture(player, true));
    }

    [Fact]
    public void Catalog_RejectsMissingTown()
    {
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.ValidateCatalog([]));
    }

    [Fact]
    public void Catalog_RejectsDuplicateTown()
    {
        var venue = new TownStruggleVenue("Galven", Map.Felucca, new Rectangle2D(100, 100, 100, 100),
            ExtractionDungeonLayout.LobbyEntry, new Point3D(100, 100, 0), new Point3D(190, 190, 0), [], []);
        Assert.Throws<InvalidOperationException>(() => TownStruggleInstallation.ValidateCatalog([venue, venue, venue]));
    }

    [Fact]
    public void IncompleteReceipt_PausesOnlyTheNewlyOwnedEnabledRotation()
    {
        var previous = PvpEventRotationRuntime.Current;
        var owned = Add(new PvpEventRotationController());
        var unrelated = Add(new PvpEventRotationController());
        var currentProperty = typeof(PvpEventRotationRuntime).GetProperty(nameof(PvpEventRotationRuntime.Current));
        var enabledProperty = typeof(PvpEventRotationController).GetProperty(nameof(PvpEventRotationController.Enabled));
        try
        {
            enabledProperty.SetValue(owned, true);
            enabledProperty.SetValue(unrelated, true);
            currentProperty.SetValue(null, unrelated);
            TownStruggleInstallation.PauseIncompleteRotation(owned);
            Assert.False(owned.Paused);
            Assert.False(unrelated.Paused);

            currentProperty.SetValue(null, owned);
            TownStruggleInstallation.PauseIncompleteRotation(owned);
            Assert.True(owned.Paused);
            Assert.False(unrelated.Paused);
        }
        finally
        {
            currentProperty.SetValue(null, previous);
        }
    }

    private PlayerMobile Player() => Add(new PlayerMobile());

    private T Add<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}
