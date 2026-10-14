using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server;
using Server.Engines.ConPVP;
using Server.Engines.ModularDungeon;
using Server.Engines.Quests.Necro;
using Server.Items;
using Server.Spells.Seventh;
using Xunit;

namespace UOContent.Tests.Items;

[Collection("Sequential UOContent Tests")]
public sealed class MoongateArtworkTests : IDisposable
{
    private readonly List<Item> _items = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"modernuo-moongate-art-{Guid.NewGuid():N}");
    private string ArchivePath => Path.Combine(_directory, "before.json");

    public MoongateArtworkTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        foreach (var item in _items)
        {
            item.Delete();
        }
        if (File.Exists(ArchivePath))
        {
            File.Delete(ArchivePath);
        }
        Directory.Delete(_directory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryLegacyFrameMapsIntoItsOwnAnimatedFamily(int offset)
    {
        Assert.Equal(MoongateAppearance.Blue + offset * 3, MoongateAppearance.ConvertLegacy(MoongateAppearance.LegacyBlue + offset));
        Assert.Equal(MoongateAppearance.Dark + offset * 3, MoongateAppearance.ConvertLegacy(MoongateAppearance.LegacyDark + offset));
        Assert.True(MoongateAppearance.IsLegacy(MoongateAppearance.LegacyBlue + offset));
        Assert.True(MoongateAppearance.IsLegacy(MoongateAppearance.LegacyDark + offset));
        Assert.True(MoongateAppearance.IsMoongate(MoongateAppearance.LegacyBlue + offset));
        Assert.True(MoongateAppearance.IsMoongate(MoongateAppearance.LegacyDark + offset));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(15)]
    public void ReplacementFramesAreRecognizedAndConversionIsIdempotent(int offset)
    {
        foreach (var first in new[] { MoongateAppearance.Blue, MoongateAppearance.Dark })
        {
            Assert.True(MoongateAppearance.IsMoongate(first + offset));
            Assert.False(MoongateAppearance.IsLegacy(first + offset));
            Assert.Equal(first + offset, MoongateAppearance.ConvertLegacy(first + offset));
        }
    }

    [Theory]
    [InlineData(0xF6B)]
    [InlineData(0xF71)]
    [InlineData(0x1FD3)]
    [InlineData(0x1FD9)]
    [InlineData(0x637F)]
    [InlineData(0x63A0)]
    [InlineData(0x1822)]
    [InlineData(0x1BC3)]
    public void UnrelatedArtworkIncludingRunesAndPadsIsUntouched(int itemId)
    {
        Assert.False(MoongateAppearance.IsMoongate(itemId));
        Assert.Equal(itemId, MoongateAppearance.ConvertLegacy(itemId));
    }

    [Fact]
    public void FutureGateConstructorsUseAnimatedArtworkAndExtractionRunesStayRunes()
    {
        Assert.Equal(MoongateAppearance.Blue, Track(new Moongate()).ItemID);
        Assert.Equal(MoongateAppearance.Blue, Track(new PublicMoongate()).ItemID);
        Assert.Equal(MoongateAppearance.Blue, Track(new ResGate()).ItemID);
        Assert.Equal(MoongateAppearance.Blue, Track(new ExtractionShrinePortal()).ItemID);
        Assert.Equal(MoongateAppearance.Blue, Track(new ExtractionEventGate()).ItemID);
        Assert.Equal(MoongateAppearance.Dark, Track(new StarRoomGate()).ItemID);
        Assert.Equal(MoongateAppearance.Dark, Track(new ArenasMoongate()).ItemID);
        Assert.Equal(MoongateAppearance.Dark, Track(new GateTravelMoongate(new Point3D(100, 100, 0), Map.Felucca)).ItemID);
        Assert.Equal(MoongateAppearance.Blue, Track(new GateTravelMoongate(new Point3D(100, 100, 0), Map.Trammel)).ItemID);
        var controller = Track(new ExtractionEventController());
        Assert.Equal(0x1822, Track(new ExtractionEventGate(controller, ExtractionEventGateKind.Extraction)).ItemID);
        Assert.Equal(0x1BC3, Track(new Teleporter()).ItemID);
    }

    [Fact]
    public void ReplacementArtworkPreservesNativeLabelsWithoutRenamingItems()
    {
        foreach (var item in new Item[] { Track(new Moongate()), Track(new PublicMoongate()), Track(new SummonedPaladinMoongate()) })
        {
            Assert.Equal(1020000 + MoongateAppearance.LegacyBlue, item.LabelNumber);
            Assert.Null(item.Name);
        }
        var decoration = Track(new Static(MoongateAppearance.Dark + 12));
        Assert.Equal(1020000 + MoongateAppearance.LegacyDark + 4, decoration.LabelNumber);
        Assert.Null(decoration.Name);
        Assert.Equal(1020000 + 0x1822, Track(new Static(0x1822)).LabelNumber);
        Assert.Equal(1049498, Track(new StarRoomGate()).LabelNumber);
        Assert.Equal(1026095, Track(new Teleporter { ItemID = MoongateAppearance.Blue }).LabelNumber);
    }

    [Fact]
    public void MigrationArchivesOriginalArtworkAndPreservesEveryRouteProperty()
    {
        var teleporter = Track(new Teleporter(new Point3D(1331, 1390, 1), Map.Felucca)
        {
            ItemID = MoongateAppearance.LegacyBlue, Hue = 0x482, SoundID = 0x1FE,
            Name = "Galven", Visible = true, Delay = TimeSpan.FromSeconds(3),
            Flags = TeleporterFlags.Active | TeleporterFlags.CombatCheck, Light = LightType.Circle300
        });
        teleporter.MoveToWorld(new Point3D(5590, 1315, 0), Map.Felucca);
        var gate = Track(new Moongate(new Point3D(300, 301, 4), Map.Trammel, false)
        {
            ItemID = MoongateAppearance.LegacyDark, Hue = 33, Name = "dark route"
        });
        gate.MoveToWorld(new Point3D(200, 201, 0), Map.Felucca);
        var art = Track(new Static(MoongateAppearance.LegacyBlue + 4) { Hue = 99, Name = "decoration" });
        art.MoveToWorld(new Point3D(202, 201, 2), Map.Felucca);
        var beforeTeleporter = MoongateArtworkInstallation.Describe(teleporter);
        var beforeGate = MoongateArtworkInstallation.Describe(gate);
        var beforeArt = MoongateArtworkInstallation.Describe(art);

        var result = MoongateArtworkInstallation.Ensure(ArchivePath, new Item[] { teleporter, gate, art });

        Assert.Equal(3, result.Changed);
        Assert.Equal(0, result.RemainingLegacy);
        Assert.Equal(beforeTeleporter with { OldItemId = MoongateAppearance.Blue }, MoongateArtworkInstallation.Describe(teleporter));
        Assert.Equal(beforeGate with { OldItemId = MoongateAppearance.Dark }, MoongateArtworkInstallation.Describe(gate));
        Assert.Equal(beforeArt with { OldItemId = MoongateAppearance.Blue + 12 }, MoongateArtworkInstallation.Describe(art));
        var archived = JsonSerializer.Deserialize<MoongateArtworkInstallation.Archive>(File.ReadAllBytes(ArchivePath));
        Assert.Equal(MoongateArtworkInstallation.InstallationId, archived.InstallationId);
        Assert.Equal(beforeTeleporter, archived.Items[0]);
        Assert.Equal(beforeGate, archived.Items[1]);
        Assert.Equal(beforeArt, archived.Items[2]);
    }

    [Fact]
    public void MigrationReplayPreservesArchiveAndRejectsDestinationChangesBeforeFurtherMutation()
    {
        var first = Track(new Teleporter(new Point3D(100, 120, 0), Map.Felucca) { ItemID = MoongateAppearance.LegacyBlue });
        first.MoveToWorld(new Point3D(100, 110, 0), Map.Felucca);
        var second = Track(new Static(MoongateAppearance.LegacyDark));
        second.MoveToWorld(new Point3D(100, 111, 0), Map.Felucca);
        MoongateArtworkInstallation.Ensure(ArchivePath, new Item[] { first, second });
        var bytes = File.ReadAllBytes(ArchivePath);
        var result = MoongateArtworkInstallation.Ensure(ArchivePath, new Item[] { second, first });
        Assert.Equal(0, result.Changed);
        Assert.Equal(2, result.AlreadyConverted);
        Assert.Equal(bytes, File.ReadAllBytes(ArchivePath));
        first.ItemID = MoongateAppearance.LegacyBlue;
        second.ItemID = MoongateAppearance.LegacyDark;
        first.PointDest = new Point3D(101, 120, 0);

        Assert.Throws<InvalidOperationException>(() => MoongateArtworkInstallation.Ensure(ArchivePath, new Item[] { second, first }));

        Assert.Equal(MoongateAppearance.LegacyBlue, first.ItemID);
        Assert.Equal(MoongateAppearance.LegacyDark, second.ItemID);
        Assert.Equal(bytes, File.ReadAllBytes(ArchivePath));
    }

    [Fact]
    public void InvalidOrDuplicateTargetsAreRejectedWithoutAnArchiveOrChanges()
    {
        var first = Track(new Static(MoongateAppearance.LegacyBlue));
        first.MoveToWorld(new Point3D(100, 110, 0), Map.Felucca);
        Assert.Throws<InvalidOperationException>(() => MoongateArtworkInstallation.Ensure(ArchivePath, new Item[] { first, first }));
        var unrelated = Track(new Static(0x1822));
        unrelated.MoveToWorld(new Point3D(100, 111, 0), Map.Felucca);
        Assert.Throws<InvalidOperationException>(() => MoongateArtworkInstallation.Ensure(ArchivePath, new Item[] { first, unrelated }));
        Assert.Equal(MoongateAppearance.LegacyBlue, first.ItemID);
        Assert.False(File.Exists(ArchivePath));
    }

    private T Track<T>(T item) where T : Item
    {
        if (!ReferenceEquals(World.FindItem(item.Serial), item))
        {
            World.AddEntity(item);
        }
        _items.Add(item);
        return item;
    }
}
