using System;
using System.Collections.Generic;
using System.IO;
using Server;
using Server.Engines.Labrynth;
using Server.Engines.Spawners;
using Server.Items;
using Server.Mobiles;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class GalvenHealerRoomRelocationTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GalvenHealerRoomRelocationTests-" + Guid.NewGuid());
    private string ArchivePath => Path.Combine(_directory, "before.json");

    public void Dispose()
    {
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            _created[index].Delete();
        }
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public void Plan_PinsBothHealers_AndTranslatesTheCompleteRoomByItsFloorOffset()
    {
        Assert.Equal(new Rectangle3D(1279, 1323, 20, 7, 12, 20), GalvenHealerRoomRelocation.Source);
        Assert.Equal(new Rectangle3D(1258, 1333, 10, 7, 12, 20), GalvenHealerRoomRelocation.Destination);
        Assert.Equal(2, GalvenHealerRoomRelocation.Pins.Length);
        Assert.Equal(new Guid("eeedbdd4-d136-4d90-8e23-fbcb4f7642eb"), GalvenHealerRoomRelocation.Pins[0].Guid);
        Assert.Equal(new Guid("2d7530ca-b91e-4f93-8c49-db57e1275ca5"), GalvenHealerRoomRelocation.Pins[1].Guid);
        Assert.Equal(new Point3D(1281, 1323, 20), GalvenHealerRoomRelocation.Pins[0].Position);
        Assert.Equal(new Point3D(1283, 1324, 20), GalvenHealerRoomRelocation.Pins[1].Position);
        Assert.Equal(new Point3D(1260, 1333, 10), GalvenHealerRoomRelocation.Translate(GalvenHealerRoomRelocation.Pins[0].Position));
        Assert.Equal(new Point3D(1262, 1334, 10), GalvenHealerRoomRelocation.Translate(GalvenHealerRoomRelocation.Pins[1].Position));
        Assert.Equal(GalvenHealerRoomRelocation.Destination, GalvenHealerRoomRelocation.TranslateBounds(GalvenHealerRoomRelocation.Source));
        Assert.Equal(new Point3D(1264, 1344, 29), GalvenHealerRoomRelocation.Translate(new Point3D(1285, 1334, 39)));
    }

    [Fact]
    public void SpawnBoundsTranslation_PreservesTheGlobalZInterval_AndOffsetsLocalZIntervals()
    {
        Assert.Equal(new Rectangle3D(1256, 1329, -128, 9, 9, 256),
            GalvenHealerRoomRelocation.TranslateBounds(new Rectangle3D(1277, 1319, -128, 9, 9, 256)));
        Assert.Equal(new Rectangle3D(1256, 1329, 10, 9, 9, 16),
            GalvenHealerRoomRelocation.TranslateBounds(new Rectangle3D(1277, 1319, 20, 9, 9, 16)));
    }

    [SkippableFact]
    public void Relocation_PreservesItemsSpawnersNpcsAndInventory_AndMovesHomesAndBoundsTogether()
    {
        TileDataRequirement.SkipIfMissing();
        var (spawners, healers) = SeedOriginal();
        var first = GalvenHealerRoomRelocation.Pins[0].Position;
        var decoration = Track(new Item(0xEED) { Movable = false, Hue = 0x482, Name = "room fixture", Amount = 7 });
        decoration.MoveToWorld(new Point3D(first.X + 1, first.Y, first.Z), Map.Felucca);
        var inventory = Track(new Item(0xEED));
        healers[0].AddItem(inventory);
        healers[0].Hits = 1;
        var decorationPosition = decoration.Location;
        var bounds = new Rectangle3D[spawners.Length];
        var homes = new Point3D[healers.Length];
        var locations = new Point3D[healers.Length];
        for (var index = 0; index < spawners.Length; index++)
        {
            bounds[index] = spawners[index].SpawnBounds;
            homes[index] = healers[index].Home;
            locations[index] = healers[index].Location;
        }

        var result = GalvenHealerRoomRelocation.Ensure(ArchivePath, false);

        Assert.True(result.Changed);
        Assert.True(File.Exists(ArchivePath));
        Assert.Equal(GalvenHealerRoomRelocation.RelocationId, result.Before.Relocation);
        Assert.Equal(5, result.Before.Entities.Length);
        Assert.Equal(2, result.Before.Spawners.Length);
        var archivedDecoration = Assert.Single(result.Before.Entities, entity => entity.Serial == decoration.Serial.Value);
        Assert.Equal(decorationPosition, archivedDecoration.Original);
        Assert.Equal(decoration.Location, archivedDecoration.Target);
        var archivedInventory = Assert.Single(result.Before.Attachments, item => item.Serial == inventory.Serial.Value);
        Assert.Equal(healers[0].Serial.Value, archivedInventory.ParentSerial);
        Assert.Same(decoration, World.FindItem(decoration.Serial));
        Assert.Equal(GalvenHealerRoomRelocation.Translate(decorationPosition), decoration.Location);
        Assert.Same(Map.Felucca, decoration.Map);
        Assert.False(decoration.Deleted);
        Assert.False(decoration.Movable);
        Assert.Equal(0x482, decoration.Hue);
        Assert.Equal("room fixture", decoration.Name);
        Assert.Equal(7, decoration.Amount);
        for (var index = 0; index < spawners.Length; index++)
        {
            var pin = GalvenHealerRoomRelocation.Pins[index];
            var spawner = spawners[index];
            var healer = healers[index];
            Assert.Same(spawner, World.FindItem(spawner.Serial));
            Assert.Same(healer, World.FindMobile(healer.Serial));
            Assert.Equal(pin.Guid, spawner.Guid);
            Assert.Equal(GalvenHealerRoomRelocation.Translate(pin.Position), spawner.Location);
            Assert.Equal(GalvenHealerRoomRelocation.TranslateBounds(bounds[index]), spawner.SpawnBounds);
            Assert.Equal(4, spawner.HomeRange);
            Assert.Equal(1, spawner.Count);
            Assert.Equal(TimeSpan.FromMinutes(8), spawner.MinDelay);
            Assert.Equal(TimeSpan.FromMinutes(10), spawner.MaxDelay);
            Assert.False(spawner.Running);
            Assert.Equal(GalvenHealerRoomRelocation.Translate(locations[index]), healer.Location);
            Assert.Equal(GalvenHealerRoomRelocation.Translate(homes[index]), healer.Home);
            Assert.Same(Map.Felucca, healer.HomeMap);
            Assert.Equal(4, healer.RangeHome);
            Assert.Same(spawner, healer.Spawner);
            Assert.Same(spawner.Entries[0], spawner.Spawned[healer]);
            Assert.Contains(healer, spawner.Entries[0].Spawned);
            Assert.False(spawner.Deleted);
            Assert.False(healer.Deleted);
        }
        Assert.Equal(1, healers[0].Hits);
        Assert.Same(healers[0], inventory.Parent);
        Assert.False(inventory.Deleted);

        var originalArchive = File.ReadAllText(ArchivePath);
        var reused = GalvenHealerRoomRelocation.Ensure(ArchivePath, false);

        Assert.False(reused.Changed);
        Assert.Equal(originalArchive, File.ReadAllText(ArchivePath));
        Assert.Same(healers[0], inventory.Parent);
        Assert.Equal(1, healers[0].Hits);
    }

    [SkippableFact]
    public void OwnedHealerOutsideRoom_IsFoundThroughItsSpawner_AndLandsAtItsTranslatedHome()
    {
        TileDataRequirement.SkipIfMissing();
        var (spawners, healers) = SeedOriginal();
        var pin = GalvenHealerRoomRelocation.Pins[0].Position;
        var wanderingPosition = new Point3D(pin.X + 10, pin.Y, pin.Z);
        healers[0].MoveToWorld(wanderingPosition, Map.Felucca);
        Assert.False(GalvenHealerRoomRelocation.Source.Contains(wanderingPosition));
        var healerSerial = healers[0].Serial;

        var result = GalvenHealerRoomRelocation.Ensure(ArchivePath, false);

        Assert.True(result.Changed);
        Assert.Same(healers[0], World.FindMobile(healerSerial));
        Assert.Equal(new Point3D(1260, 1333, 10), healers[0].Location);
        Assert.Equal(healers[0].Location, healers[0].Home);
        Assert.Same(spawners[0], healers[0].Spawner);
        Assert.Contains(healers[0], spawners[0].Spawned.Keys);
        var snapshot = Assert.Single(result.Before.Entities, entity => entity.Serial == healerSerial.Value);
        Assert.Equal(wanderingPosition, snapshot.Original);
        Assert.Equal(healers[0].Home, snapshot.Target);
        Assert.Equal(pin, snapshot.Home);
    }

    [SkippableFact]
    public void UnownedRoomHealer_MovesTheSameSerialAndTranslatesItsOwnHome()
    {
        TileDataRequirement.SkipIfMissing();
        SeedOriginal();
        var healer = Track(new Healer
        {
            Home = new Point3D(1284, 1332, 20),
            HomeMap = Map.Felucca,
            RangeHome = 2
        });
        healer.MoveToWorld(new Point3D(1285, 1334, 20), Map.Felucca);

        var result = GalvenHealerRoomRelocation.Ensure(ArchivePath, false);

        Assert.True(result.Changed);
        Assert.Same(healer, World.FindMobile(healer.Serial));
        Assert.Equal(new Point3D(1264, 1344, 10), healer.Location);
        Assert.Equal(new Point3D(1263, 1342, 10), healer.Home);
        Assert.Same(Map.Felucca, healer.HomeMap);
        Assert.Equal(2, healer.RangeHome);
        Assert.Null(healer.Spawner);
    }

    [SkippableTheory]
    [InlineData("Player")]
    [InlineData("Controlled")]
    [InlineData("Summoned")]
    [InlineData("Stabled")]
    [InlineData("UnrelatedMobile")]
    [InlineData("Corpse")]
    [InlineData("Door")]
    [InlineData("UnexpectedSpawner")]
    public void ProtectedOrUnexpectedEntity_FailsFullPreflightWithoutArchiveOrMutation(string conflict)
    {
        TileDataRequirement.SkipIfMissing();
        var (spawners, healers) = SeedOriginal();
        var location = GalvenHealerRoomRelocation.Pins[1].Position;
        switch (conflict)
        {
            case "Player":
            {
                var player = Track(new Mobile { Player = true, Body = 0x190 });
                player.MoveToWorld(location, Map.Felucca);
                break;
            }
            case "Controlled":
            {
                healers[1].Controlled = true;
                break;
            }
            case "Summoned":
            {
                healers[1].Summoned = true;
                break;
            }
            case "Stabled":
            {
                healers[1].IsStabled = true;
                break;
            }
            case "UnrelatedMobile":
            {
                Track(new Mobile { Body = 0x190 }).MoveToWorld(location, Map.Felucca);
                break;
            }
            case "Corpse":
            {
                var owner = Track(new Mobile { Player = true, Body = 0x190 });
                Track(new Corpse(owner, [])).MoveToWorld(location, Map.Felucca);
                break;
            }
            case "Door":
            {
                Track(new MetalDoor(DoorFacing.WestCW)).MoveToWorld(location, Map.Felucca);
                break;
            }
            case "UnexpectedSpawner":
            {
                var unexpected = Track(new Spawner("Sheep"));
                unexpected.MoveToWorld(location, Map.Felucca);
                unexpected.Stop();
                break;
            }
        }
        var before = CaptureState();
        var bounds = spawners[0].SpawnBounds;
        var home = healers[0].Home;

        Assert.Throws<InvalidOperationException>(() => GalvenHealerRoomRelocation.Ensure(ArchivePath, false));

        Assert.False(File.Exists(ArchivePath));
        AssertStateUnchanged(before);
        Assert.Equal(bounds, spawners[0].SpawnBounds);
        Assert.Equal(home, healers[0].Home);
        Assert.Same(spawners[0], healers[0].Spawner);
        Assert.False(spawners[0].Running);
    }

    [SkippableTheory]
    [InlineData("Position")]
    [InlineData("Entry")]
    [InlineData("Count")]
    [InlineData("MinDelay")]
    [InlineData("HomeRange")]
    public void MixedOrMismatchedPinnedSpawner_RejectsBeforeMovingAnyEntity(string conflict)
    {
        TileDataRequirement.SkipIfMissing();
        var (spawners, healers) = SeedOriginal();
        switch (conflict)
        {
            case "Position":
            {
                spawners[1].MoveToWorld(GalvenHealerRoomRelocation.Translate(spawners[1].Location), Map.Felucca);
                spawners[1].SpawnBounds = GalvenHealerRoomRelocation.TranslateBounds(spawners[1].SpawnBounds);
                break;
            }
            case "Entry":
            {
                spawners[1].Entries[0].SpawnedName = "Sheep";
                break;
            }
            case "Count":
            {
                spawners[1].Count = 2;
                break;
            }
            case "MinDelay":
            {
                spawners[1].MinDelay = TimeSpan.FromMinutes(1);
                break;
            }
            case "HomeRange":
            {
                spawners[1].HomeRange = 5;
                break;
            }
        }
        var before = CaptureState();
        var home = healers[0].Home;
        var bounds = spawners[0].SpawnBounds;

        Assert.Throws<InvalidOperationException>(() => GalvenHealerRoomRelocation.Ensure(ArchivePath, false));

        Assert.False(File.Exists(ArchivePath));
        AssertStateUnchanged(before);
        Assert.Equal(home, healers[0].Home);
        Assert.Equal(bounds, spawners[0].SpawnBounds);
    }

    [SkippableFact]
    public void InvalidExistingArchive_IsPreservedAndCannotAuthorizeMutation()
    {
        TileDataRequirement.SkipIfMissing();
        SeedOriginal();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ArchivePath, "{}");
        var before = CaptureState();

        Assert.Throws<InvalidOperationException>(() => GalvenHealerRoomRelocation.Ensure(ArchivePath, false));

        Assert.Equal("{}", File.ReadAllText(ArchivePath));
        AssertStateUnchanged(before);
    }

    [SkippableFact]
    public void ArchivedMixedState_RefusesWithoutOverwritingTheOriginalArchiveOrMovingAnything()
    {
        TileDataRequirement.SkipIfMissing();
        var (spawners, _) = SeedOriginal();
        GalvenHealerRoomRelocation.Ensure(ArchivePath, false);
        var archive = File.ReadAllText(ArchivePath);
        spawners[1].MoveToWorld(GalvenHealerRoomRelocation.Pins[1].Position, Map.Felucca);
        var before = CaptureState();
        var bounds = spawners[1].SpawnBounds;

        Assert.Throws<InvalidOperationException>(() => GalvenHealerRoomRelocation.Ensure(ArchivePath, false));

        Assert.Equal(archive, File.ReadAllText(ArchivePath));
        AssertStateUnchanged(before);
        Assert.Equal(bounds, spawners[1].SpawnBounds);
    }

    [SkippableTheory]
    [InlineData("Player")]
    [InlineData("Controlled")]
    [InlineData("UnexpectedSpawner")]
    public void ProtectedDestinationOccupant_RefusesWithoutMovingAnySourceEntity(string conflict)
    {
        TileDataRequirement.SkipIfMissing();
        var (spawners, healers) = SeedOriginal();
        var destination = GalvenHealerRoomRelocation.Translate(GalvenHealerRoomRelocation.Pins[1].Position);
        switch (conflict)
        {
            case "Player":
            {
                Track(new Mobile { Player = true, Body = 0x190 }).MoveToWorld(destination, Map.Felucca);
                break;
            }
            case "Controlled":
            {
                Track(new Healer { Controlled = true }).MoveToWorld(destination, Map.Felucca);
                break;
            }
            case "UnexpectedSpawner":
            {
                var spawner = Track(new Spawner("Sheep"));
                spawner.MoveToWorld(destination, Map.Felucca);
                spawner.Stop();
                break;
            }
        }
        var before = CaptureState();
        var bounds = spawners[0].SpawnBounds;
        var home = healers[0].Home;

        Assert.Throws<InvalidOperationException>(() => GalvenHealerRoomRelocation.Ensure(ArchivePath, false));

        AssertStateUnchanged(before);
        Assert.Equal(bounds, spawners[0].SpawnBounds);
        Assert.Equal(home, healers[0].Home);
    }

    private (Spawner[] Spawners, Healer[] Healers) SeedOriginal()
    {
        var spawners = new Spawner[GalvenHealerRoomRelocation.Pins.Length];
        var healers = new Healer[spawners.Length];
        for (var index = 0; index < spawners.Length; index++)
        {
            var pin = GalvenHealerRoomRelocation.Pins[index];
            var definition = new SpawnerDataDto
            {
                Guid = pin.Guid,
                Location = pin.Position,
                Map = Map.Felucca,
                Count = 1,
                MinDelay = TimeSpan.FromMinutes(8),
                MaxDelay = TimeSpan.FromMinutes(10),
                HomeRange = 4,
                WalkingRange = -1,
                Entries = [new SpawnerEntry("Healer", 100, 1)]
            };
            var spawner = Track((Spawner)definition.ToSpawner());
            spawner.MoveToWorld(pin.Position, Map.Felucca);
            spawner.Stop();
            var healer = Track(new Healer
            {
                Home = pin.Position,
                HomeMap = Map.Felucca,
                RangeHome = 4,
                Spawner = spawner
            });
            healer.MoveToWorld(new Point3D(pin.Position.X, pin.Position.Y + 1, pin.Position.Z), Map.Felucca);
            spawner.Spawned.Add(healer, spawner.Entries[0]);
            spawner.Entries[0].AddToSpawned(healer);
            spawners[index] = spawner;
            healers[index] = healer;
        }
        return (spawners, healers);
    }

    private EntityState[] CaptureState()
    {
        var state = new EntityState[_created.Count];
        for (var index = 0; index < _created.Count; index++)
        {
            var entity = _created[index];
            state[index] = new EntityState(entity, entity.Location, entity.Map, entity.Deleted);
        }
        return state;
    }

    private static void AssertStateUnchanged(EntityState[] state)
    {
        foreach (var entry in state)
        {
            Assert.Equal(entry.Location, entry.Entity.Location);
            Assert.Same(entry.Map, entry.Entity.Map);
            Assert.Equal(entry.Deleted, entry.Entity.Deleted);
        }
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private sealed record EntityState(IEntity Entity, Point3D Location, Map Map, bool Deleted);
}
