using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Server;
using Server.Engines.ModularDungeon;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

public class ExtractionDungeonLayoutTests
{
    private const string SpawnSha256 = "57d860a6edd8542aa1a0015e252720470900783378dd779dbbc7442e395df48b";

    [Fact]
    public void Layout_HasTwentyFiveUniqueRoomsAndExactlyOneHundredTenCreatures()
    {
        var rooms = ExtractionDungeonLayout.Rooms;
        var names = new HashSet<string>();
        var positions = new HashSet<Point3D>();
        var creatureCount = 0;

        Assert.Equal(25, rooms.Count);
        for (var id = 0; id < rooms.Count; ++id)
        {
            var room = rooms[id];
            Assert.Equal(id, room.ModuleId);
            Assert.True(names.Add(room.Name));
            Assert.Equal(id / 5 is 0 or 4 || id % 5 is 0 or 4, room.IsOuter);
            Assert.InRange(room.Creatures.Length, 4, 6);

            foreach (var creature in room.Creatures)
            {
                Assert.True(positions.Add(creature.Position));
                Assert.Equal(3, creature.RangeHome);
                Assert.True(Enum.IsDefined(creature.Kind));
                ++creatureCount;
            }
        }

        Assert.Equal(110, creatureCount);
        Assert.Equal(ExtractionDungeonLayout.CreatureCount, creatureCount);
    }

    [Fact]
    public void ModuleSpawns_VisitAllSixteenOutsideModulesBeforeTheNineInsideModulesAndLoop()
    {
        int[] expected =
        [
            0, 1, 2, 3, 4, 5, 9, 10, 14, 15, 19, 20, 21, 22, 23, 24,
            6, 7, 8, 11, 12, 13, 16, 17, 18
        ];

        Assert.Equal(25, ExtractionDungeonLayout.ModuleSpawns.Count);
        for (var index = 0; index < expected.Length; ++index)
        {
            var spawn = ExtractionDungeonLayout.ModuleSpawns[index];
            Assert.Equal(expected[index], spawn.ModuleId);
            Assert.Equal(ExtractionDungeonLayout.Rooms[spawn.ModuleId].PlayerStart, spawn.Location);
            Assert.Equal(index < 16, ExtractionDungeonLayout.Rooms[spawn.ModuleId].IsOuter);
        }

        for (var ordinal = 0; ordinal < expected.Length * 3; ++ordinal)
        {
            Assert.Equal(expected[ordinal % expected.Length], ExtractionEventController.GetSpawnModuleId(ordinal));
        }
    }

    [Fact]
    public void PublicRoomAndModuleCollections_CannotBeResizedOrReassigned()
    {
        var rooms = Assert.IsAssignableFrom<IList<ExtractionRoomSpawn>>(ExtractionDungeonLayout.Rooms);
        var spawns = Assert.IsAssignableFrom<IList<ExtractionModuleSpawn>>(ExtractionDungeonLayout.ModuleSpawns);
        Assert.True(rooms.IsReadOnly);
        Assert.True(spawns.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => rooms[0] = rooms[1]);
        Assert.Throws<NotSupportedException>(() => spawns[0] = spawns[1]);
    }

    [Fact]
    public void CreatureCoordinatesAndKinds_ExactlyMatchTheFrozenNativeWalkingPlan()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);

        foreach (var room in ExtractionDungeonLayout.Rooms)
        {
            foreach (var spawn in room.Creatures)
            {
                writer.Write(room.ModuleId);
                // The approved Wraith substitution must preserve the original frozen walking plan.
                writer.Write((int)(room.ModuleId == 14 && spawn.Kind == ExtractionCreatureKind.Wraith
                    ? ExtractionCreatureKind.Skeleton
                    : spawn.Kind));
                writer.Write(spawn.Position.X);
                writer.Write(spawn.Position.Y);
                writer.Write(spawn.Position.Z);
                writer.Write(spawn.RangeHome);
            }
        }

        writer.Flush();
        Assert.Equal(SpawnSha256, Convert.ToHexStringLower(SHA256.HashData(stream.ToArray())));
    }

    [Fact]
    public void PlayerAnchorsAndNames_ExactlyMatchTheInstalledV17Manifest()
    {
        var manifest = LoadManifest();
        Assert.Equal(ExtractionDungeonLayout.BlueprintSha256, manifest.BlueprintSha256);

        foreach (var room in ExtractionDungeonLayout.Rooms)
        {
            var module = Array.Find(manifest.Modules, candidate => candidate.Id == $"M{room.ModuleId:00}");
            Assert.NotNull(module);
            Assert.Equal(module.Name, room.Name);
            Assert.Equal(module.Start.Point, room.PlayerStart);
        }
    }

    [Fact]
    public void CreatureSpawns_StayInTheirModuleAndClearOfThePlayerAnchor()
    {
        foreach (var room in ExtractionDungeonLayout.Rooms)
        {
            var originX = 5256 + room.ModuleId % 5 * 24;
            var originY = 808 + room.ModuleId / 5 * 24;

            for (var index = 0; index < room.Creatures.Length; ++index)
            {
                var position = room.Creatures[index].Position;
                Assert.InRange(position.X, originX + 2, originX + 22);
                Assert.InRange(position.Y, originY + 2, originY + 22);
                Assert.Contains(position.Z, new[] { -25, 0, 25 });
                Assert.True(ExtractionDungeonLayout.DungeonArea.Contains(position));
                AssertOutsideReservation(position, room.PlayerStart, 4);

                for (var previous = 0; previous < index; ++previous)
                {
                    AssertOutsideReservation(position, room.Creatures[previous].Position, 3);
                }
            }
        }
    }

    [Fact]
    public void CreatureSpawns_AvoidEveryDoorPositionSwingPocketAndApproach()
    {
        var manifest = LoadManifest();

        foreach (var room in ExtractionDungeonLayout.Rooms)
        {
            foreach (var spawn in room.Creatures)
            {
                foreach (var door in manifest.Doors)
                {
                    AssertOutsideReservation(spawn.Position, door.Closed.Point, 2);
                    AssertOutsideReservation(spawn.Position, door.Opened.Point, 2);

                    foreach (var approach in door.Approaches)
                    {
                        AssertOutsideReservation(spawn.Position, approach.Point, 2);
                    }
                }
            }
        }
    }

    [Fact]
    public void CreatureSpawns_AvoidEveryChestAndItsOpeningApproach()
    {
        var manifest = LoadManifest();

        foreach (var room in ExtractionDungeonLayout.Rooms)
        {
            foreach (var spawn in room.Creatures)
            {
                foreach (var chest in manifest.Chests)
                {
                    AssertOutsideReservation(spawn.Position, chest.Position.Point, 2);
                    AssertOutsideReservation(spawn.Position, chest.Approach.Point, 2);
                }
            }
        }
    }

    [Fact]
    public void CreatureSpawns_AvoidPhysicalStairLanesAndEscapeLandings()
    {
        var manifest = LoadManifest();

        foreach (var room in ExtractionDungeonLayout.Rooms)
        {
            foreach (var spawn in room.Creatures)
            {
                foreach (var lane in manifest.StairLanes)
                {
                    foreach (var step in lane.Points)
                    {
                        AssertOutsideReservation(spawn.Position, step.Point, 2);
                    }
                }

                foreach (var escape in manifest.EscapeRoutes)
                {
                    AssertOutsideReservation(spawn.Position, escape.From.Point, 2);
                    AssertOutsideReservation(spawn.Position, escape.To.Point, 2);
                }
            }
        }
    }

    [Fact]
    public void OssuarySpawns_AreEntirelyOnTheGroundFloor()
    {
        var room = ExtractionDungeonLayout.Rooms[2];
        Assert.Equal("Ossuary Galleries", room.Name);
        Assert.Equal(4, room.Creatures.Length);
        Assert.All(room.Creatures, spawn => Assert.Equal(0, spawn.Position.Z));
    }

    [Theory]
    [InlineData(14, -25)]
    [InlineData(17, 25)]
    public void FullSizedAdditionalLevels_HaveTwoThemedMonstersWithoutBlockingTheirOpenings(int moduleId, int z)
    {
        var room = ExtractionDungeonLayout.Rooms[moduleId];
        Assert.Equal(6, room.Creatures.Length);
        Assert.Equal(2, room.Creatures.Count(spawn => spawn.Position.Z == z));
        Assert.DoesNotContain(room.Creatures, spawn => spawn.Position == new Point3D(5316, 891, 25));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(17)]
    [InlineData(23)]
    public void CoffinAndGraveRooms_ContainSkeletonsAndMummies(int moduleId)
    {
        var creatures = ExtractionDungeonLayout.Rooms[moduleId].Creatures;
        Assert.Contains(creatures, spawn => spawn.Kind == ExtractionCreatureKind.Skeleton);
        Assert.Contains(creatures, spawn => spawn.Kind == ExtractionCreatureKind.Mummy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WraithSepulchre_ReplacesOnlyItsThreeSkeletonsAndPreservesAllSixSpawnPositions(bool abundant)
    {
        var rooms = abundant ? ExtractionDungeonLayout.AbundantRooms : ExtractionDungeonLayout.Rooms;
        var room = rooms[14];
        Assert.Equal("The Wraith Sepulchre", room.Name);
        Assert.Equal(new Point3D(5366, 864, 0), room.PlayerStart);
        ExtractionCreatureSpawn[] expected =
        [
            new(ExtractionCreatureKind.Mummy, new(5363, 869, 0), 3),
            new(ExtractionCreatureKind.Wraith, new(5372, 874, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5360, 860, 0), 3),
            new(ExtractionCreatureKind.Wraith, new(5372, 861, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5361, 865, -25), 3),
            new(ExtractionCreatureKind.Wraith, new(5374, 863, -25), 3)
        ];
        Assert.Equal(expected, room.Creatures);
        Assert.Equal(3, room.Creatures.Count(spawn => spawn.Kind == ExtractionCreatureKind.Wraith));
        Assert.Equal(3, room.Creatures.Count(spawn => spawn.Kind == ExtractionCreatureKind.Mummy));
        Assert.DoesNotContain(room.Creatures, spawn => spawn.Kind == ExtractionCreatureKind.Skeleton);
        foreach (var other in rooms)
        {
            if (other.ModuleId != 14)
            {
                Assert.DoesNotContain(other.Creatures, spawn => spawn.Kind == ExtractionCreatureKind.Wraith);
            }
        }
        Assert.Equal(6, (int)ExtractionCreatureKind.Wraith);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(22)]
    public void MagicalRooms_ContainGazers(int moduleId) =>
        Assert.Contains(ExtractionDungeonLayout.Rooms[moduleId].Creatures,
            spawn => spawn.Kind == ExtractionCreatureKind.Gazer);

    [Fact]
    public void CobwebCoveredStorageRoom_ContainsSpiders() =>
        Assert.Contains(ExtractionDungeonLayout.Rooms[7].Creatures,
            spawn => spawn.Kind == ExtractionCreatureKind.GiantSpider);

    [Theory]
    [InlineData(3)]
    [InlineData(15)]
    [InlineData(20)]
    public void WetAndBrokenCrossingRooms_ContainSilverSerpents(int moduleId) =>
        Assert.Contains(ExtractionDungeonLayout.Rooms[moduleId].Creatures,
            spawn => spawn.Kind == ExtractionCreatureKind.SilverSerpent);

    [Fact]
    public void RootboundGarden_ContainsWolves() =>
        Assert.Contains(ExtractionDungeonLayout.Rooms[10].Creatures,
            spawn => spawn.Kind == ExtractionCreatureKind.DireWolf);

    [Fact]
    public void LobbyAndTownGate_AreOutsideTheArenaAndHaveTheExactReservedLocations()
    {
        Assert.Equal(new Point3D(1317, 1379, 0), ExtractionDungeonLayout.GatePoint);
        Assert.Equal(new Point3D(1317, 1380, 0), ExtractionDungeonLayout.TownReturn);
        Assert.Equal(new Rectangle2D(5384, 808, 25, 25), ExtractionDungeonLayout.LobbyBounds);
        Assert.Equal(new Point3D(5396, 820, 0), ExtractionDungeonLayout.LobbyEntry);
        Assert.True(ExtractionDungeonLayout.LobbyBounds.Contains(ExtractionDungeonLayout.LobbyEntry));
        Assert.False(ExtractionDungeonLayout.DungeonBounds.Contains(ExtractionDungeonLayout.LobbyEntry));
        Assert.False(ExtractionDungeonLayout.DungeonBounds.Contains(ExtractionDungeonLayout.GatePoint));
        Assert.False(ExtractionDungeonLayout.DungeonBounds.Contains(ExtractionDungeonLayout.TownReturn));
    }

    [Theory]
    [InlineData(-26, false)]
    [InlineData(-25, true)]
    [InlineData(0, true)]
    [InlineData(25, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void DungeonArea_CoversEveryEventLevelButNotAdjacentVerticalSpace(int z, bool expected)
    {
        Assert.Equal(new Rectangle2D(5248, 800, 136, 136), ExtractionDungeonLayout.DungeonBounds);
        Assert.Equal(expected, ExtractionDungeonLayout.DungeonArea.Contains(new Point3D(5316, 868, z)));
    }

    private static ModularDungeonDeployment.Manifest LoadManifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "ModularDungeonV17", "install-manifest.json");
        var raw = File.ReadAllBytes(path);
        Assert.Equal("7966d9845810420764daaa4a68b0947e0cc87b7bb236127477fecf0b1a31ab14",
            Convert.ToHexStringLower(SHA256.HashData(raw)));
        return JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static void AssertOutsideReservation(Point3D candidate, Point3D reserved, int radius)
    {
        if (Math.Abs(candidate.Z - reserved.Z) < 16)
        {
            var distance = Math.Max(Math.Abs(candidate.X - reserved.X), Math.Abs(candidate.Y - reserved.Y));
            Assert.True(distance > radius, $"Spawn {candidate} lies within {radius} tiles of reserved point {reserved}.");
        }
    }
}
