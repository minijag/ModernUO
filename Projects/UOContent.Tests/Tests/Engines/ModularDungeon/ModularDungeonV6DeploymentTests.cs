using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Server;
using Server.Engines.ModularDungeon;
using Server.Items;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ModularDungeonV6DeploymentTests : IDisposable
{
    private const string V5Identity = "modular-dungeon-v5-final-felucca-5256-808";
    private const string V6Identity = "modular-dungeon-v6-final-felucca-5256-808";
    private static readonly Type _deployment = typeof(ModularDungeonDeployment);
    private static readonly Type _jobType = _deployment.GetNestedType("Job", BindingFlags.NonPublic);
    private static readonly FieldInfo _baseDirectory = typeof(Core).GetField("_baseDirectory", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly List<IEntity> _created = new();
    private readonly List<object> _jobs = new();
    private readonly string _previousDirectory = (string)_baseDirectory.GetValue(null);
    private readonly Expansion _previousExpansion = Core.Expansion;
    private readonly bool _previousScenery = (bool)_deployment.GetField("_scenery", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private readonly bool _previousChapel = (bool)_deployment.GetField("_chapel", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private readonly bool _previousDecorated = (bool)_deployment.GetField("_decorated", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private readonly bool _previousRevision = (bool)_deployment.GetField("_revision", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private readonly string _fixtureRoot = Path.Combine(Path.GetTempPath(), "ModernUO-v6-tests-" + Guid.NewGuid().ToString("N"));
    internal string FixtureRoot => _fixtureRoot;

    public ModularDungeonV6DeploymentTests()
    {
        Core.Expansion = Expansion.T2A;
        _deployment.GetField("_chapel", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
        Directory.CreateDirectory(Path.Combine(_fixtureRoot, "Data", "ModularDungeonV5"));
        Directory.CreateDirectory(Path.Combine(_fixtureRoot, "Data", "ModularDungeonV6"));
    }

    public void Dispose()
    {
        foreach (var job in _jobs)
        {
            InvokeJob(job, "Stop");
        }
        for (var i = _created.Count - 1; i >= 0; i--)
        {
            _created[i].Delete();
        }
        _baseDirectory.SetValue(null, _previousDirectory);
        _deployment.GetField("_scenery", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _previousScenery);
        _deployment.GetField("_chapel", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _previousChapel);
        _deployment.GetField("_decorated", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _previousDecorated);
        _deployment.GetField("_revision", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _previousRevision);
        Core.Expansion = _previousExpansion;
        var resolved = Path.GetFullPath(_fixtureRoot);
        var parent = Path.GetFullPath(Path.GetTempPath());
        Assert.Equal(parent.TrimEnd(Path.DirectorySeparatorChar), Directory.GetParent(resolved).FullName.TrimEnd(Path.DirectorySeparatorChar));
        Assert.StartsWith("ModernUO-v6-tests-", Path.GetFileName(resolved));
        Directory.Delete(resolved, true);
    }

    [SkippableFact]
    public void ExactSceneryManifest_RequiresPreviousV5AndPreservesAllRuntimeDefinitions()
    {
        var previous = PreviousManifest();
        var next = SceneryManifest(previous);
        AssertValid(next);
        Assert.Null(Record.Exception(() => InvokeStatic("ValidateSceneryDefinitions", previous, next)));
        Assert.Equal(16095, next.OriginalStaticCount);
        Assert.Equal(100, next.Chests.Length);
        Assert.Equal(71, next.Doors.Length);
        Assert.True(next.SceneryOnly);
        Assert.Equal("M24", next.SceneryModuleId);
    }

    [SkippableTheory]
    [InlineData("MissingSceneryOnly")]
    [InlineData("WrongSceneryModule")]
    [InlineData("WrongPreviousStaticCount")]
    [InlineData("ChangedChestArt")]
    [InlineData("ChangedChestPosition")]
    [InlineData("ChangedHatchDestination")]
    [InlineData("ChangedReturnDestination")]
    [InlineData("ChangedDoorPlacement")]
    [InlineData("WrongPreviousNative")]
    public void StaticOnlyManifest_RejectsRuntimeGeometryOrContractChanges(string corruption)
    {
        var previous = PreviousManifest();
        var next = SceneryManifest(previous);
        switch (corruption)
        {
            case "MissingSceneryOnly": next = next with { SceneryOnly = false }; break;
            case "WrongSceneryModule": next = next with { SceneryModuleId = "M10" }; break;
            case "WrongPreviousStaticCount": next = next with { OriginalStaticCount = 15508 }; break;
            case "ChangedChestArt":
            case "ChangedChestPosition":
            {
                var chests = (ModularDungeonDeployment.ChestDefinition[])next.Chests.Clone();
                chests[0] = corruption == "ChangedChestArt" ? chests[0] with { ItemId = 0xE43 } :
                    chests[0] with { Position = chests[0].Position with { X = chests[0].Position.X + 1 } };
                next = next with { Chests = chests };
                break;
            }
            case "ChangedHatchDestination": next = next with { HiddenCellar = next.HiddenCellar with { Entry = next.HiddenCellar.Entry with { X = next.HiddenCellar.Entry.X + 1 } } }; break;
            case "ChangedReturnDestination": next = next with { HiddenCellar = next.HiddenCellar with { SurfaceReturn = next.HiddenCellar.SurfaceReturn with { Y = next.HiddenCellar.SurfaceReturn.Y + 1 } } }; break;
            case "ChangedDoorPlacement":
            {
                var doors = (ModularDungeonDeployment.DoorDefinition[])next.Doors.Clone();
                doors[0] = doors[0] with { Opened = doors[0].Opened with { X = doors[0].Opened.X + 1 } };
                next = next with { Doors = doors };
                break;
            }
            case "WrongPreviousNative": next = next with { OriginalNativeSha256 = new string('a', 64) }; break;
        }
        Assert.NotNull(Record.Exception(() => { InvokeStatic("ValidateManifest", next); InvokeStatic("ValidateSceneryDefinitions", previous, next); }));
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SceneryOwnership_AcceptsMovedOrConsumedCoverAndLichWithoutRefilling(bool missing)
    {
        var previous = PreviousManifest();
        var (receipt, features, reward) = RetainedWorld(previous);
        var markerItem = Track(new Gold(13));
        Empty(reward);
        reward.DropItem(markerItem);
        if (missing)
        {
            features.Cover.Delete();
            features.Lich.Delete();
        }
        else
        {
            Track(new Backpack()).DropItem(features.Cover);
            var lichPoint = previous.HiddenCellar.CreaturePosition.Point;
            features.Lich.MoveToWorld(new Point3D(lichPoint.X + 1, lichPoint.Y, lichPoint.Z), Map.Felucca);
        }
        WritePrevious(previous, receipt);
        var job = NewJob(SceneryManifest(previous));
        var retained = (RootboundCellarFeature.Features)JobField("_cellarFeatures").GetValue(job);
        Assert.Equal(missing, retained.Cover == null);
        Assert.Equal(missing, retained.Lich == null);
        Assert.Same(features.Hatch, retained.Hatch);
        Assert.Same(features.ReturnRune, retained.ReturnRune);
        Assert.Same(markerItem, Assert.Single(reward.Items));
        var result = (ModularDungeonDeployment.Result)InvokeJob(job, "CreateResult", true, "audit", null, false);
        Assert.Equal(receipt.Features, result.Features);
        Assert.Equal(receipt.Creatures, result.Creatures);
    }

    [SkippableTheory]
    [InlineData("WrongDoorClass")]
    [InlineData("WrongChestClass")]
    [InlineData("MissingChest")]
    [InlineData("WrongRewardClass")]
    [InlineData("DuplicateDoorSerial")]
    [InlineData("WrongHatchDestination")]
    [InlineData("WrongRuneDestination")]
    [InlineData("MissingHatch")]
    [InlineData("WrongFeatureIdentity")]
    [InlineData("InvalidCoverSerial")]
    [InlineData("InvalidLichSerial")]
    [InlineData("WrongLichClass")]
    public void SceneryOwnership_RejectsChangedClassesSerialsAndTransportState(string corruption)
    {
        var previous = PreviousManifest();
        var (receipt, features, reward) = RetainedWorld(previous);
        var doors = (ModularDungeonDeployment.DoorSerial[])receipt.Doors.Clone();
        var chests = (ModularDungeonDeployment.DoorSerial[])receipt.Chests.Clone();
        switch (corruption)
        {
            case "WrongDoorClass": doors[0] = doors[0] with { Serial = (uint)Track(new Item(1)).Serial }; break;
            case "DuplicateDoorSerial": doors[0] = doors[0] with { Serial = doors[1].Serial }; break;
            case "WrongChestClass": chests[0] = chests[0] with { Serial = (uint)Track(new Item(1)).Serial }; break;
            case "MissingChest": World.FindItem((Serial)chests[0].Serial).Delete(); break;
            case "WrongRewardClass":
            {
                for (var i = 0; i < chests.Length; i++)
                {
                    if (chests[i].Id == "M10-hidden-chest")
                    {
                        var generic = Track(new EventDungeonChest(reward.ItemID));
                        generic.MoveToWorld(reward.Location, Map.Felucca);
                        chests[i] = chests[i] with { Serial = (uint)generic.Serial };
                    }
                }
                break;
            }
            case "WrongHatchDestination": features.Hatch.PointDest = new Point3D(features.Hatch.PointDest.X + 1, features.Hatch.PointDest.Y, features.Hatch.PointDest.Z); break;
            case "WrongRuneDestination": features.ReturnRune.PointDest = new Point3D(features.ReturnRune.PointDest.X + 1, features.ReturnRune.PointDest.Y, features.ReturnRune.PointDest.Z); break;
            case "MissingHatch": features.Hatch.Delete(); break;
            case "WrongFeatureIdentity": receipt = receipt with { Features = [receipt.Features[0] with { Id = "unknown-cover" }, receipt.Features[1], receipt.Features[2]] }; break;
            case "InvalidCoverSerial": receipt = receipt with { Features = [receipt.Features[0] with { Serial = 0 }, receipt.Features[1], receipt.Features[2]] }; break;
            case "InvalidLichSerial": receipt = receipt with { Creatures = [receipt.Creatures[0] with { Serial = (uint)features.Cover.Serial }] }; break;
            case "WrongLichClass": receipt = receipt with { Creatures = [receipt.Creatures[0] with { Serial = (uint)Track(new Mobile()).Serial }] }; break;
        }
        receipt = receipt with { Doors = doors, Chests = chests };
        WritePrevious(previous, receipt);
        Assert.Throws<TargetInvocationException>(() => NewJob(SceneryManifest(previous)));
        Assert.False(reward.Deleted);
        Assert.True(reward.LootInitialized);
    }

    [SkippableFact]
    public void StaticOnlySurveyAndFailure_NeverRelocateDeleteRefillOrRecreateObjects()
    {
        Skip.If(!TestServerInitializer.TileDataLoaded, "Native event object validation requires tile data.");
        var previous = PreviousManifest();
        var (receipt, features, reward) = RetainedWorld(previous);
        var retainedDoor = (EventDungeonDoor)World.FindItem((Serial)receipt.Doors[0].Serial);
        retainedDoor.Open = true;
        Empty(reward);
        var marker = Track(new Gold(13));
        reward.DropItem(marker);
        Track(new Backpack()).DropItem(features.Cover);
        var coverParent = features.Cover.Parent;
        var lichLocation = features.Lich.Location;
        WritePrevious(previous, receipt);
        var job = NewJob(SceneryManifest(previous));
        InvokeJob(job, "PreflightAndCreateDoors");
        Assert.Equal(71, ((List<EventDungeonDoor>)JobField("_doors").GetValue(job)).Count);
        Assert.Equal(100, ((List<EventDungeonChest>)JobField("_chests").GetValue(job)).Count);
        InvokeJob(job, "CheckDoorCrossings");
        Assert.True(retainedDoor.Open);
        var frontier = (Queue<Point3D>)JobField("_frontier").GetValue(job);
        var visited = (HashSet<Point3D>)JobField("_visited").GetValue(job);
        var targets = (HashSet<Point3D>)JobField("_targets").GetValue(job);
        visited.Add(previous.Entry.Point);
        frontier.Enqueue(previous.Entry.Point);
        targets.Add(new Point3D(-9999, -9999, 0));
        InvokeJob(job, "CheckReachabilitySlice");
        Assert.True(retainedDoor.Open);
        retainedDoor.Open = false;
        InvokeJob(job, "CloseDoors");
        Assert.True(retainedDoor.Open);
        InvokeJob(job, "ValidateAllDoors");
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "CreateHiddenCellar"));
        InvokeJob(job, "Fail", new InvalidOperationException("deliberate static-only survey failure"));
        foreach (var serial in receipt.Chests)
        {
            var chest = World.FindItem((Serial)serial.Serial);
            Assert.NotNull(chest);
            Assert.False(chest.Deleted);
            var definition = Array.Find(previous.Chests, c => c.Id == serial.Id);
            Assert.Equal(definition.Position.Point, chest.Location);
            Assert.Equal(definition.ItemId, chest.ItemID);
        }
        Assert.Same(marker, Assert.Single(reward.Items));
        Assert.Same(coverParent, features.Cover.Parent);
        Assert.Equal(lichLocation, features.Lich.Location);
        Assert.False(features.Hatch.Deleted);
        Assert.False(features.ReturnRune.Deleted);
        Assert.False(features.Lich.Deleted);
        Assert.True(retainedDoor.Open);
    }

    [SkippableFact]
    public void FailureAfterSuccessfulReceiptWrite_InvalidatesThatReceiptWithoutDeletingObjects()
    {
        var previous = PreviousManifest();
        var (receipt, features, reward) = RetainedWorld(previous);
        WritePrevious(previous, receipt);
        var job = NewJob(SceneryManifest(previous));
        InvokeJob(job, "PreflightAndCreateDoors");
        JobField("_installedResultWritten").SetValue(job, true);
        var result = (ModularDungeonDeployment.Result)InvokeJob(job, "CreateResult", true, "installed", null, false);
        File.WriteAllBytes(Path.Combine(_fixtureRoot, "Data", "ModularDungeonV6", "installed.json"), JsonSerializer.SerializeToUtf8Bytes(result, _json));
        InvokeJob(job, "Fail", new InvalidOperationException("deliberate marker retirement failure"));
        var failed = JsonSerializer.Deserialize<ModularDungeonDeployment.Result>(File.ReadAllBytes(
            Path.Combine(_fixtureRoot, "Data", "ModularDungeonV6", "installed.json")), _json);
        Assert.False(failed.Success);
        Assert.Equal("failed", failed.Stage);
        Assert.False(features.Hatch.Deleted);
        Assert.False(features.ReturnRune.Deleted);
        Assert.False(features.Lich.Deleted);
        Assert.False(reward.Deleted);
    }

    [SkippableFact]
    public void NativeScenerySurvey_IgnoresOnlyTheRecordedRemovableCoverAndRestoresGlobalPolicy()
    {
        Skip.If(!TestServerInitializer.TileDataLoaded, "Native floor support and cover collision require tile data.");
        var previous = PreviousManifest();
        var (receipt, features, reward) = RetainedWorld(previous);
        WritePrevious(previous, receipt);
        var job = NewJob(SceneryManifest(previous));
        var coverLocation = features.Cover.Location;
        var nativePolicy = Server.Movement.MovementImpl.IgnoreMovableImpassables;
        var point = previous.HiddenCellar.Hatch.Point;
        Assert.True((bool)InvokeJob(job, "FitsSceneryEndpoint", point));
        var probe = Track(new Mobile { Player = true, Body = 0x190 });
        var arguments = new object[] { probe, new Point3D(point.X - 1, point.Y, point.Z), Direction.East, 0 };
        Assert.True((bool)_jobType.GetMethod("SurveyMovement", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(job, arguments));
        Assert.Equal(0, arguments[3]);
        Assert.Equal(nativePolicy, Server.Movement.MovementImpl.IgnoreMovableImpassables);
        Assert.Equal(coverLocation, features.Cover.Location);
        Assert.Null(features.Cover.Parent);
        Assert.True(features.Hatch.IsCovered);

        var tileId = 1;
        var previousTile = TileData.ItemTable[tileId];
        try
        {
            TileData.ItemTable[tileId] = new ItemData("scenery test obstruction", TileFlag.Impassable, 0, 0, 0, 0, 0, 20);
            Track(new Item(tileId) { Movable = false }).MoveToWorld(point, Map.Felucca);
            Assert.False((bool)InvokeJob(job, "FitsSceneryEndpoint", point));
            Assert.False((bool)_jobType.GetMethod("SurveyMovement", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(job, arguments));
            Assert.Equal(nativePolicy, Server.Movement.MovementImpl.IgnoreMovableImpassables);
        }
        finally
        {
            TileData.ItemTable[tileId] = previousTile;
        }
        Assert.False(reward.Deleted);
    }

    [Fact]
    public void NormalStartupWithCompletedV6Receipt_DoesNotStartADeploymentJob()
    {
        _baseDirectory.SetValue(null, _fixtureRoot);
        var path = Path.Combine(_fixtureRoot, "Data", "ModularDungeonV6");
        File.WriteAllText(Path.Combine(path, "installed.json"), "{\"success\":true}");
        File.WriteAllText(Path.Combine(path, "install.completed"), "{\"manifestSha256\":\"already-completed\"}");
        Assert.Null(_deployment.GetField("_job", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        ModularDungeonDeployment.Initialize();
        Assert.True((bool)_deployment.GetField("_scenery", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.Null(_deployment.GetField("_job", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.False(File.Exists(Path.Combine(path, "install-state.json")));
        Assert.Equal("{\"success\":true}", File.ReadAllText(Path.Combine(path, "installed.json")));
    }

    internal (ModularDungeonDeployment.Result Receipt, RootboundCellarFeature.Features Features, RootboundCellarChest Reward) RetainedWorld(
        ModularDungeonDeployment.Manifest manifest, bool chapel = false)
    {
        var doors = new ModularDungeonDeployment.DoorSerial[manifest.Doors.Length];
        for (var i = 0; i < doors.Length; i++)
        {
            var definition = manifest.Doors[i];
            var door = Track(new EventDungeonDoor(Enum.Parse<DoorFacing>(definition.Facing)) { Locked = true });
            door.MoveToWorld(definition.Closed.Point, Map.Felucca);
            doors[i] = new(definition.Id, (uint)door.Serial);
        }
        var chests = new ModularDungeonDeployment.DoorSerial[manifest.Chests.Length];
        RootboundCellarChest reward = null;
        for (var i = 0; i < chests.Length; i++)
        {
            var definition = manifest.Chests[i];
            EventDungeonChest chest;
            if (definition.Type == "RootboundCellarChest")
            {
                reward = Track(new RootboundCellarChest(definition.ItemId));
                chest = reward;
            }
            else
            {
                chest = Track(new EventDungeonChest(definition.ItemId));
            }
            chest.MoveToWorld(definition.Position.Point, Map.Felucca);
            chests[i] = new(definition.Id, (uint)chest.Serial);
        }
        var cellar = manifest.HiddenCellar;
        var features = RootboundCellarFeature.Create(Map.Felucca, cellar.Hatch.Point, cellar.Entry.Point,
            cellar.ReturnRune.Point, cellar.SurfaceReturn.Point, cellar.CreaturePosition.Point, reward);
        Track(features.Cover); Track(features.Hatch); Track(features.ReturnRune); Track(features.Lich);
        var receipt = new ModularDungeonDeployment.Result(1, chapel ? V6Identity : V5Identity,
            Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(manifest, _json))),
            manifest.InstalledNativeSha256, true, "installed", null, chapel ? 16311 : 16095, 71, 0, 0, DateTime.UtcNow, manifest.Bounds,
            doors, [], false, chests,
            [new("R10-hidden-cover", (uint)features.Cover.Serial), new("R10-hidden-descent", (uint)features.Hatch.Serial), new("R10-hidden-return", (uint)features.ReturnRune.Serial)],
            [new("R10-cellar-lich", (uint)features.Lich.Serial)]);
        return (receipt, features, reward);
    }

    internal void WritePrevious(ModularDungeonDeployment.Manifest manifest, ModularDungeonDeployment.Result receipt, bool chapel = false)
    {
        var path = Path.Combine(_fixtureRoot, "Data", chapel ? "ModularDungeonV6" : "ModularDungeonV5");
        File.WriteAllBytes(Path.Combine(path, "install-manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, _json));
        File.WriteAllBytes(Path.Combine(path, "installed.json"), JsonSerializer.SerializeToUtf8Bytes(receipt, _json));
        _baseDirectory.SetValue(null, _fixtureRoot);
        _deployment.GetField("_scenery", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
        _deployment.GetField("_chapel", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, chapel);
        if (chapel)
        {
            Directory.CreateDirectory(Path.Combine(_fixtureRoot, "Data", "ModularDungeonV7"));
        }
    }

    internal object NewJob(ModularDungeonDeployment.Manifest manifest)
    {
        var job = Activator.CreateInstance(_jobType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [manifest, new string('d', 64), false], null);
        _jobs.Add(job);
        return job;
    }

    internal static object InvokeStatic(string name, params object[] arguments) =>
        _deployment.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments);
    internal static object InvokeJob(object job, string name, params object[] arguments) =>
        _jobType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(job, arguments);
    internal static FieldInfo JobField(string name) => _jobType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    private static void AssertValid(ModularDungeonDeployment.Manifest manifest) => Assert.Null(Record.Exception(() => InvokeStatic("ValidateManifest", manifest)));
    internal T Track<T>(T entity) where T : IEntity { _created.Add(entity); return entity; }
    internal static void Empty(Container container) { while (container.Items.Count > 0) { container.Items[0].Delete(); } }

    internal static ModularDungeonDeployment.Manifest SceneryManifest(ModularDungeonDeployment.Manifest previous)
    {
        var path = FindArtifact("install-v6-20261009-r2");
        return path == null ? previous with
        {
            InstallationId = V6Identity, OriginalStaticCount = 16095, OriginalNativeSha256 = previous.InstalledNativeSha256,
            StaticCount = 16311, SceneryOnly = true, SceneryModuleId = "M24"
        } : JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(File.ReadAllBytes(path), _json);
    }

    internal static ModularDungeonDeployment.Manifest PreviousManifest()
    {
        var path = FindArtifact("install-v5-20261009-r2");
        Skip.If(path == null, "The exact prior V5 native stage is required.");
        return JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(File.ReadAllBytes(path), _json);
    }

    internal static string FindArtifact(string stage)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "artifacts", "modular-dungeon", stage, "staged-map", "install-manifest.json");
                if (File.Exists(path)) { return path; }
            }
        }
        return null;
    }
}
