using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Server;
using Server.Engines.ModularDungeon;
using Server.Items;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ModularDungeonV5DeploymentTests
{
    private const string V5Identity = "modular-dungeon-v5-final-felucca-5256-808";
    private static readonly MethodInfo _validate = typeof(ModularDungeonDeployment).GetMethod(
        "ValidateManifest", BindingFlags.Static | BindingFlags.NonPublic
    );

    [SkippableFact]
    public void ExactV5Stage_ContainsTheAuthorizedPreviousObjectsAndOneNewCellarChest()
    {
        var path = FindArtifact("install-v5-20261009-r2");
        Skip.If(path == null, "Generate the V5 native stage before testing its exact deployment manifest.");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(108748, bytes.Length);
        Assert.Equal("62749a5ad3a53cd75d49f9019805df7e7d57e0954b3a3388bd7e2bb2c4cb9170",
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var manifest = Deserialize(bytes);
        Assert.Equal(V5Identity, manifest.InstallationId);
        Assert.Equal(new ModularDungeonDeployment.Bounds(5248, 800, 136, 136), manifest.Bounds);
        Assert.Equal(-40, manifest.OriginalLandZ);
        Assert.Equal(-40, manifest.InstalledLandZ);
        Assert.Equal(15508, manifest.OriginalStaticCount);
        Assert.Equal(71, manifest.Doors.Length);
        Assert.Equal(100, manifest.Chests.Length);
        Assert.NotNull(manifest.HiddenCellar);
        AssertValid(manifest);

        var previousPath = FindArtifact("install-v4-20261009-r2");
        Skip.If(previousPath == null, "The exact installed V4 manifest is needed to compare retained identities.");
        var previous = Deserialize(File.ReadAllBytes(previousPath));
        var previousIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chest in previous.Chests)
        {
            previousIds.Add(chest.Id);
        }
        var retained = 0;
        var reward = 0;
        foreach (var chest in manifest.Chests)
        {
            if (chest.Type == "RootboundCellarChest")
            {
                reward++;
                Assert.Equal("M10-hidden-chest", chest.Id);
                Assert.Equal("M10", chest.ModuleId);
                Assert.DoesNotContain(chest.Id, previousIds);
            }
            else
            {
                retained++;
                Assert.Equal("EventDungeonChest", chest.Type);
                Assert.True(previousIds.Remove(chest.Id));
            }
        }
        Assert.Equal(99, retained);
        Assert.Equal(1, reward);
        Assert.Empty(previousIds);

        var previousDoors = new Dictionary<string, ModularDungeonDeployment.DoorDefinition>(StringComparer.Ordinal);
        foreach (var door in previous.Doors)
        {
            previousDoors.Add(door.Id, door);
        }
        foreach (var door in manifest.Doors)
        {
            var old = previousDoors[door.Id];
            Assert.Equal(old.Closed, door.Closed);
            Assert.Equal(old.Opened, door.Opened);
            Assert.Equal(old.Facing, door.Facing);
            Assert.Equal(old.ClosedId, door.ClosedId);
            Assert.Equal(old.OpenedId, door.OpenedId);
        }
    }

    [SkippableTheory]
    [InlineData("MissingCellar")]
    [InlineData("UnknownModule")]
    [InlineData("WrongChestId")]
    [InlineData("HatchOutsideModule")]
    [InlineData("EntryOutsideModule")]
    [InlineData("RuneOutsideBounds")]
    [InlineData("SurfaceReturnOutsideModule")]
    [InlineData("CreatureOutsideModule")]
    [InlineData("HatchWrongZ")]
    [InlineData("EntryWrongZ")]
    [InlineData("RuneWrongZ")]
    [InlineData("SurfaceReturnWrongZ")]
    [InlineData("CreatureWrongZ")]
    [InlineData("RecursiveSurfaceReturn")]
    [InlineData("NonAdjacentSurfaceReturn")]
    [InlineData("NullEntry")]
    [InlineData("TooFewChests")]
    [InlineData("MissingRewardType")]
    [InlineData("UnknownChestType")]
    [InlineData("MultipleRewardTypes")]
    [InlineData("WrongRewardIdentity")]
    [InlineData("WrongRewardModule")]
    [InlineData("RewardOutsideModule")]
    [InlineData("RewardAtGround")]
    [InlineData("RewardBlocksEntry")]
    [InlineData("RewardApproachActivatesReturnRune")]
    [InlineData("StandardApproachActivatesHatch")]
    [InlineData("PreviousLandWrongZ")]
    [InlineData("PreviousLandWrongId")]
    [InlineData("PreviousStaticCountWrong")]
    [InlineData("InstalledLandWrongZ")]
    [InlineData("InvalidPreviousHash")]
    public void V5Manifest_RejectsUnauthorizedCellarOrPreviousNativeProfile(string corruption)
    {
        var manifest = Fixture();
        AssertValid(manifest);
        var cellar = manifest.HiddenCellar;
        var chests = (ModularDungeonDeployment.ChestDefinition[])manifest.Chests.Clone();
        var rewardIndex = FindReward(chests);
        switch (corruption)
        {
            case "MissingCellar": manifest = manifest with { HiddenCellar = null }; break;
            case "UnknownModule": cellar = cellar with { ModuleId = "M00" }; break;
            case "WrongChestId": cellar = cellar with { ChestId = "M10-fake-chest" }; break;
            case "HatchOutsideModule": cellar = cellar with { Hatch = cellar.Hatch with { X = 5281 } }; break;
            case "EntryOutsideModule": cellar = cellar with { Entry = cellar.Entry with { Y = 855 } }; break;
            case "RuneOutsideBounds": cellar = cellar with { ReturnRune = cellar.ReturnRune with { X = 5400 } }; break;
            case "SurfaceReturnOutsideModule": cellar = cellar with { SurfaceReturn = cellar.SurfaceReturn with { X = 5280 } }; break;
            case "CreatureOutsideModule": cellar = cellar with { CreaturePosition = cellar.CreaturePosition with { Y = 880 } }; break;
            case "HatchWrongZ": cellar = cellar with { Hatch = cellar.Hatch with { Z = 1 } }; break;
            case "EntryWrongZ": cellar = cellar with { Entry = cellar.Entry with { Z = -24 } }; break;
            case "RuneWrongZ": cellar = cellar with { ReturnRune = cellar.ReturnRune with { Z = 0 } }; break;
            case "SurfaceReturnWrongZ": cellar = cellar with { SurfaceReturn = cellar.SurfaceReturn with { Z = 25 } }; break;
            case "CreatureWrongZ": cellar = cellar with { CreaturePosition = cellar.CreaturePosition with { Z = 0 } }; break;
            case "RecursiveSurfaceReturn": cellar = cellar with { SurfaceReturn = cellar.Hatch }; break;
            case "NonAdjacentSurfaceReturn": cellar = cellar with { SurfaceReturn = cellar.Hatch with { X = cellar.Hatch.X + 2 } }; break;
            case "NullEntry": cellar = cellar with { Entry = null }; break;
            case "TooFewChests":
            {
                Array.Resize(ref chests, chests.Length - 1);
                break;
            }
            case "MissingRewardType": chests[rewardIndex] = chests[rewardIndex] with { Type = "EventDungeonChest" }; break;
            case "UnknownChestType": chests[rewardIndex] = chests[rewardIndex] with { Type = "WoodenChest" }; break;
            case "MultipleRewardTypes": chests[rewardIndex == 0 ? 1 : 0] = chests[rewardIndex == 0 ? 1 : 0] with { Type = "RootboundCellarChest" }; break;
            case "WrongRewardIdentity": chests[rewardIndex] = chests[rewardIndex] with { Id = "M10-fake-chest" }; break;
            case "WrongRewardModule": chests[rewardIndex] = chests[rewardIndex] with { ModuleId = "M09" }; break;
            case "RewardOutsideModule":
            {
                chests[rewardIndex] = chests[rewardIndex] with
                {
                    Position = new ModularDungeonDeployment.Coordinate(5300, 870, -25),
                    Approach = new ModularDungeonDeployment.Coordinate(5301, 870, -25)
                };
                break;
            }
            case "RewardAtGround":
            {
                chests[rewardIndex] = chests[rewardIndex] with
                {
                    Position = chests[rewardIndex].Position with { Z = 0 },
                    Approach = chests[rewardIndex].Approach with { Z = 0 }
                };
                break;
            }
            case "RewardBlocksEntry":
            {
                chests[rewardIndex] = chests[rewardIndex] with
                {
                    Position = cellar.Entry,
                    Approach = cellar.Entry with { X = cellar.Entry.X + 1 }
                };
                break;
            }
            case "RewardApproachActivatesReturnRune":
            {
                chests[rewardIndex] = chests[rewardIndex] with
                {
                    Approach = cellar.ReturnRune,
                    Position = AvailableNeighbor(manifest, cellar.ReturnRune)
                };
                break;
            }
            case "StandardApproachActivatesHatch":
            {
                var standard = 0;
                while (chests[standard].ModuleId != "M10" || chests[standard].Type != "EventDungeonChest" ||
                       chests[standard].Position.Z != 0)
                {
                    standard++;
                }
                chests[standard] = chests[standard] with
                {
                    Approach = cellar.Hatch,
                    Position = AvailableNeighbor(manifest, cellar.Hatch)
                };
                break;
            }
            case "PreviousLandWrongZ": manifest = manifest with { OriginalLandZ = 0 }; break;
            case "PreviousLandWrongId": manifest = manifest with { OriginalLandId = 2 }; break;
            case "PreviousStaticCountWrong": manifest = manifest with { OriginalStaticCount = 15001 }; break;
            case "InstalledLandWrongZ": manifest = manifest with { InstalledLandZ = 0 }; break;
            case "InvalidPreviousHash": manifest = manifest with { OriginalNativeSha256 = new string('g', 64) }; break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        if (corruption != "MissingCellar")
        {
            manifest = manifest with { HiddenCellar = cellar };
        }
        manifest = manifest with { Chests = chests };
        AssertInvalid(manifest);
    }

    [SkippableTheory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(0, 3)]
    [InlineData(0, 4)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(1, 4)]
    [InlineData(2, 3)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    public void HiddenCellar_RejectsEveryPairOfCoincidentEndpoints(int first, int second)
    {
        var manifest = Fixture();
        var cellar = manifest.HiddenCellar;
        var points = new[] { cellar.Hatch, cellar.Entry, cellar.ReturnRune, cellar.SurfaceReturn, cellar.CreaturePosition };
        points[second] = points[first];
        AssertInvalid(manifest with
        {
            HiddenCellar = cellar with
            {
                Hatch = points[0], Entry = points[1], ReturnRune = points[2],
                SurfaceReturn = points[3], CreaturePosition = points[4]
            }
        });
    }

    [SkippableFact]
    public void PreviousV4Manifest_CannotIntroduceARewardChestWithoutCellarAuthorization()
    {
        var path = FindArtifact("install-v4-20261009-r2");
        Skip.If(path == null, "The installed V4 fixture is required.");
        var previous = Deserialize(File.ReadAllBytes(path));
        AssertValid(previous);
        var chests = (ModularDungeonDeployment.ChestDefinition[])previous.Chests.Clone();
        chests[0] = chests[0] with { Type = "RootboundCellarChest" };
        AssertInvalid(previous with { Chests = chests });
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitTransport_EnqueuesItsSupportedDestinationWithoutCountingAFakeWalkingStep(bool returnTrip)
    {
        Skip.If(!TestServerInitializer.TileDataLoaded, "Native movement support requires tile data.");
        var floorId = 1;
        var previousFloor = TileData.ItemTable[floorId];
        var cellar = new ModularDungeonDeployment.HiddenCellarDefinition("M10", "M10-hidden-chest",
            new(101, 100, 80), new(106, 105, 105), new(107, 105, 105), new(102, 100, 80), new(110, 110, 105));
        var source = returnTrip ? cellar.ReturnRune : cellar.Hatch;
        var destination = returnTrip ? cellar.SurfaceReturn : cellar.Entry;
        Item floor = null;
        try
        {
            TileData.ItemTable[floorId] = new ItemData("transport test floor", TileFlag.Surface, 0, 0, 0, 0, 0, 0);
            floor = new Item(floorId) { Movable = false };
            floor.MoveToWorld(destination.Point, Map.Felucca);
            var job = TransportJob(cellar, out var visited, out var frontier);
            InvokeTransport(job, source.Point);
            Assert.Contains(destination.Point, visited);
            Assert.Equal(destination.Point, Assert.Single(frontier));
            Assert.Equal(0, JobField("_movementChecks").GetValue(job));
            InvokeTransport(job, source.Point);
            Assert.Single(frontier);
            var otherJob = TransportJob(cellar, out var unrelatedVisited, out var unrelatedFrontier);
            InvokeTransport(otherJob, new Point3D(103, 100, 80));
            Assert.Empty(unrelatedVisited);
            Assert.Empty(unrelatedFrontier);
        }
        finally
        {
            floor?.Delete();
            TileData.ItemTable[floorId] = previousFloor;
        }
    }

    private static object TransportJob(ModularDungeonDeployment.HiddenCellarDefinition cellar,
        out HashSet<Point3D> visited, out Queue<Point3D> frontier)
    {
        var type = typeof(ModularDungeonDeployment).GetNestedType("Job", BindingFlags.NonPublic);
        Assert.NotNull(type);
        var job = RuntimeHelpers.GetUninitializedObject(type);
        var manifest = new ModularDungeonDeployment.Manifest(1, V5Identity, new string('a', 64), new string('b', 64),
            new string('c', 64), new(5256, 808, 0), new(5248, 800, 136, 136), 0x244, -40, 17000,
            new(5316, 807, 0), [], [], [], 15508, 0x244, -40, [], [], cellar);
        visited = new HashSet<Point3D>();
        frontier = new Queue<Point3D>();
        JobField("_manifest").SetValue(job, manifest);
        JobField("_visited").SetValue(job, visited);
        JobField("_frontier").SetValue(job, frontier);
        return job;
    }

    private static FieldInfo JobField(string name) =>
        typeof(ModularDungeonDeployment).GetNestedType("Job", BindingFlags.NonPublic)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

    private static void InvokeTransport(object job, Point3D source) =>
        job.GetType().GetMethod("AddTransportDestination", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(job, [source]);

    private static ModularDungeonDeployment.Manifest Fixture()
    {
        var path = FindArtifact("install-v5-20261009-r2");
        if (path != null)
        {
            return Deserialize(File.ReadAllBytes(path));
        }
        path = FindArtifact("install-v4-20261009-r2");
        Skip.If(path == null, "The exact V4 fixture or generated V5 stage is required.");
        var previous = Deserialize(File.ReadAllBytes(path));
        var chests = new ModularDungeonDeployment.ChestDefinition[previous.Chests.Length + 1];
        Array.Copy(previous.Chests, chests, previous.Chests.Length);
        chests[^1] = new("M10-hidden-chest", "M10", new(5269, 862, -25), 0xE42,
            new(5268, 862, -25), "RootboundCellarChest");
        var cellar = new ModularDungeonDeployment.HiddenCellarDefinition("M10", "M10-hidden-chest",
            new(5268, 868, 0), new(5263, 859, -25), new(5264, 859, -25), new(5269, 868, 0), new(5266, 861, -25));
        return previous with
        {
            InstallationId = V5Identity, OriginalLandZ = -40, OriginalStaticCount = 15508,
            StaticCount = 17000, OriginalNativeSha256 = previous.InstalledNativeSha256,
            Chests = chests, HiddenCellar = cellar
        };
    }

    private static int FindReward(ModularDungeonDeployment.ChestDefinition[] chests)
    {
        for (var i = 0; i < chests.Length; i++)
        {
            if (chests[i].Type == "RootboundCellarChest")
            {
                return i;
            }
        }
        throw new InvalidOperationException("The V5 fixture lacks its dedicated reward container.");
    }

    private static ModularDungeonDeployment.Coordinate AvailableNeighbor(
        ModularDungeonDeployment.Manifest manifest, ModularDungeonDeployment.Coordinate point)
    {
        var occupied = new HashSet<ModularDungeonDeployment.Coordinate>();
        foreach (var chest in manifest.Chests)
        {
            occupied.Add(chest.Position);
        }
        var cellar = manifest.HiddenCellar;
        foreach (var endpoint in new[] { cellar.Hatch, cellar.Entry, cellar.ReturnRune, cellar.SurfaceReturn, cellar.CreaturePosition })
        {
            occupied.Add(endpoint);
        }
        foreach (var offset in new[] { (X: 1, Y: 0), (X: -1, Y: 0), (X: 0, Y: 1), (X: 0, Y: -1) })
        {
            var candidate = point with { X = point.X + offset.X, Y = point.Y + offset.Y };
            if (candidate.X is > 5256 and < 5280 && candidate.Y is > 856 and < 880 && !occupied.Contains(candidate))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException("The transport fixture has no available same-level neighboring position.");
    }

    private static ModularDungeonDeployment.Manifest Deserialize(byte[] bytes)
    {
        var options = (JsonSerializerOptions)typeof(ModularDungeonDeployment).GetField(
            "_jsonOptions", BindingFlags.Static | BindingFlags.NonPublic
        ).GetValue(null);
        return JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(bytes, options);
    }

    private static void AssertValid(ModularDungeonDeployment.Manifest manifest) =>
        Assert.Null(Record.Exception(() => _validate.Invoke(null, [manifest])));

    private static void AssertInvalid(ModularDungeonDeployment.Manifest manifest)
    {
        var exception = Assert.Throws<TargetInvocationException>(() => _validate.Invoke(null, [manifest]));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private static string FindArtifact(string stage)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "artifacts", "modular-dungeon", stage,
                    "staged-map", "install-manifest.json");
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }
        return null;
    }
}
