using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Server;
using Server.Engines.ModularDungeon;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

public class ExtractionV35CompatibilityTests
{
    [Fact]
    public void V35Layout_KeepsEveryPlayerAnchorAndChangesOnlyObstructedLibraryAndForgeSpawns()
    {
        var manifest = LoadManifest(out _);
        var rooms = ExtractionDungeonLayout.RoomsFor(manifest);
        Assert.Equal(25, rooms.Count);
        var creatures = 0;
        for (var index = 0; index < rooms.Count; index++)
        {
            var room = rooms[index];
            Assert.Equal(manifest.Modules[index].Start.Point, room.PlayerStart);
            Assert.Equal(manifest.Modules[index].Name, room.Name);
            Assert.Equal(ExtractionDungeonLayout.Rooms[index].PlayerStart, room.PlayerStart);
            if (index is not (1 or 5))
            {
                Assert.Same(ExtractionDungeonLayout.Rooms[index], room);
            }
            foreach (var spawn in room.Creatures)
            {
                creatures++;
                foreach (var start in manifest.Modules)
                {
                    AssertClear(spawn.Position, start.Start.Point, 4);
                }
                foreach (var door in manifest.Doors)
                {
                    AssertClear(spawn.Position, door.Closed.Point, 2);
                    AssertClear(spawn.Position, door.Opened.Point, 2);
                    foreach (var approach in door.Approaches)
                    {
                        AssertClear(spawn.Position, approach.Point, 2);
                    }
                }
                foreach (var lane in manifest.StairLanes)
                {
                    foreach (var point in lane.Points)
                    {
                        AssertClear(spawn.Position, point.Point, 2);
                    }
                }
                foreach (var chest in manifest.Chests)
                {
                    AssertClear(spawn.Position, chest.Position.Point, 2);
                    AssertClear(spawn.Position, chest.Approach.Point, 1);
                }
                foreach (var escape in manifest.EscapeRoutes)
                {
                    AssertClear(spawn.Position, escape.From.Point, 2);
                    AssertClear(spawn.Position, escape.To.Point, 2);
                }
            }
        }
        Assert.Equal(110, creatures);
        Assert.Equal(ExtractionDungeonLayout.ModuleSpawns, ExtractionDungeonLayout.AbundantModuleSpawns);
    }

    [Fact]
    public void V35Manifest_RequiresExactPinAndSuccessfulMatchingReceipt()
    {
        var manifest = LoadManifest(out var bytes);
        var receipt = new ModularDungeonDeployment.Result(
            1, manifest.InstallationId, Convert.ToHexStringLower(SHA256.HashData(bytes)), manifest.InstalledNativeSha256,
            true, "installed", null, manifest.StaticCount, 71, 0, 0, DateTime.UtcNow, manifest.Bounds,
            new ModularDungeonDeployment.DoorSerial[71], [], Chests: new ModularDungeonDeployment.DoorSerial[100]);
        var validated = ExtractionEventRuntime.ValidateInstalledManifest(bytes, receipt, true);
        Assert.Equal(manifest.BlueprintSha256, validated.BlueprintSha256);
        Assert.Equal(manifest.InstallationId, validated.InstallationId);
        Assert.Throws<InvalidOperationException>(() => ExtractionEventRuntime.ValidateInstalledManifest(bytes, receipt with { Success = false }, true));
        Assert.Throws<InvalidOperationException>(() => ExtractionEventRuntime.ValidateInstalledManifest(bytes, receipt with { Stage = "audit" }, true));
        Assert.Throws<InvalidOperationException>(() => ExtractionEventRuntime.ValidateInstalledManifest(bytes, receipt with { NativeSha256 = new string('0', 64) }, true));
        Assert.Throws<InvalidOperationException>(() => ExtractionEventRuntime.ValidateInstalledManifest(bytes, receipt with { InstallationId = "modular-dungeon-v17-final-felucca-5256-808" }, true));
        Assert.Throws<InvalidOperationException>(() => ExtractionEventRuntime.ValidateInstalledManifest(bytes, receipt, false));
        var changed = (byte[])bytes.Clone();
        changed[^1] ^= 1;
        Assert.Throws<InvalidOperationException>(() => ExtractionEventRuntime.ValidateInstalledManifest(changed, receipt, true));
    }

    private static ModularDungeonDeployment.Manifest LoadManifest(out byte[] bytes)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        bytes = File.ReadAllBytes(Path.Combine(root.FullName, "artifacts", "modular-dungeon", "install-v35-20261010-r1", "staged-map", "install-manifest.json"));
        return JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(bytes,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static void AssertClear(Point3D spawn, Point3D reserved, int radius)
    {
        if (Math.Abs(spawn.Z - reserved.Z) < 16)
        {
            Assert.True(Math.Max(Math.Abs(spawn.X - reserved.X), Math.Abs(spawn.Y - reserved.Y)) > radius,
                $"Spawn {spawn} is too close to reserved point {reserved}.");
        }
    }
}
