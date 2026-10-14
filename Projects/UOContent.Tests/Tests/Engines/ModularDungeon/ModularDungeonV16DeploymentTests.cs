using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Server;
using Server.Engines.ModularDungeon;
using Server.Items;
using Server.Movement;
using Server.Tests;
using Xunit;
using CalcMovement = Server.Movement.Movement;
using static UOContent.Tests.Engines.ModularDungeon.ModularDungeonV6DeploymentTests;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ModularDungeonV16DeploymentTests : IDisposable
{
    private const string V15Identity = "modular-dungeon-v15-final-felucca-5256-808";
    private const string V16Identity = "modular-dungeon-v16-final-felucca-5256-808";
    private static readonly Type _deployment = typeof(ModularDungeonDeployment);
    private static readonly Type _jobType = _deployment.GetNestedType("Job", BindingFlags.NonPublic);
    private static readonly FieldInfo _completedField = _deployment.GetField("_completed", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo _fullLevelsField = _deployment.GetField("_fullLevels", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly bool _previousCompleted = (bool)_completedField.GetValue(null);
    private readonly bool _previousFullLevels = (bool)_fullLevelsField.GetValue(null);
    private readonly ModularDungeonV6DeploymentTests _fixture = new();
    private readonly List<object> _auditJobs = new();

    public ModularDungeonV16DeploymentTests()
    {
        _completedField.SetValue(null, false);
        _fullLevelsField.SetValue(null, false);
        Directory.CreateDirectory(Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV15"));
        Directory.CreateDirectory(Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV16"));
    }

    public void Dispose()
    {
        foreach (var job in _auditJobs)
        {
            InvokeJob(job, "Stop");
        }
        _fixture.Dispose();
        _completedField.SetValue(null, _previousCompleted);
        _fullLevelsField.SetValue(null, _previousFullLevels);
    }

    [SkippableFact]
    public void FrozenFullLevelStage_HasExactV15PinsAndUnchangedRuntimeObjects()
    {
        var previous = PreviousV15();
        var next = FullLevelsManifest();
        Assert.Null(Record.Exception(() => InvokeStatic("ValidateManifest", next)));
        Assert.Null(Record.Exception(() => InvokeStatic("ValidateFullLevelDefinitions", previous, next)));
        Assert.Equal("0c1e0b200db665048225ce252bf25d73cd82d6c36207b5722a9ac2b52b7c4dcf", next.BlueprintSha256);
        Assert.Equal(21195, next.StaticCount);
        Assert.Equal(17518, next.OriginalStaticCount);
        Assert.False(next.SceneryOnly);
        Assert.Null(next.SceneryModuleId);
        Assert.Equal(HashBytes(File.ReadAllBytes(FindArtifact("install-v15-20261010-r1"))), next.PreviousManifestSha256);
        Assert.Equal(previous.InstalledNativeSha256, next.OriginalNativeSha256);
        Assert.Equal(JsonSerializer.Serialize(previous.Doors, _json), JsonSerializer.Serialize(next.Doors, _json));
        Assert.Equal(previous.Chests, next.Chests);
        Assert.Equal(previous.HiddenCellar, next.HiddenCellar);
        Assert.Equal(24, next.StairLanes.Length);
        Assert.Equal(6, next.EscapeRoutes.Length);
        foreach (var lane in previous.StairLanes)
        {
            var retained = Array.Find(next.StairLanes, candidate => candidate.Id == lane.Id);
            Assert.Equal(JsonSerializer.Serialize(lane, _json), JsonSerializer.Serialize(retained, _json));
        }
        foreach (var route in previous.EscapeRoutes)
        {
            Assert.Equal(route, Array.Find(next.EscapeRoutes, candidate => candidate.Id == route.Id));
        }
        Assert.Equal(new ModularDungeonDeployment.EscapeRoute("R17-collapsed-floor-escape", "M17",
            new(5315, 891, 25), new(5316, 891, 0)),
            Array.Find(next.EscapeRoutes, route => route.Id == "R17-collapsed-floor-escape"));
    }

    [SkippableTheory]
    [InlineData("MissingPin")]
    [InlineData("SceneryAlias")]
    [InlineData("WrongBlueprint")]
    [InlineData("WrongStaticCount")]
    [InlineData("WrongPreviousCount")]
    [InlineData("WrongPredecessor")]
    [InlineData("WrongPriorBlueprint")]
    [InlineData("WrongPriorNative")]
    [InlineData("ChangedDoor")]
    [InlineData("ChangedCellar")]
    [InlineData("ChangedChestPosition")]
    [InlineData("ChangedChestApproach")]
    [InlineData("ChangedChestArt")]
    [InlineData("ChangedChestClass")]
    [InlineData("ChangedRetainedStair")]
    [InlineData("ChangedR14Flight1")]
    [InlineData("ChangedR14Flight2")]
    [InlineData("ChangedR17Flight1")]
    [InlineData("ChangedR17Flight2")]
    [InlineData("ChangedRetainedEscape")]
    [InlineData("ChangedCollapsedEscape")]
    [InlineData("MissingStairLane")]
    [InlineData("DuplicateStairIdentity")]
    [InlineData("DuplicateEscapeIdentity")]
    public void FullLevelManifest_RejectsUnapprovedGeometryIdentityOrPins(string corruption)
    {
        var previous = PreviousV15();
        var next = FullLevelsManifest();
        switch (corruption)
        {
            case "MissingPin": next = next with { PreviousReceiptSha256 = null }; break;
            case "SceneryAlias": next = next with { SceneryOnly = true, SceneryModuleId = "M14" }; break;
            case "WrongBlueprint": next = next with { BlueprintSha256 = new string('a', 64) }; break;
            case "WrongStaticCount": next = next with { StaticCount = 21194 }; break;
            case "WrongPreviousCount": next = next with { OriginalStaticCount = 16311 }; break;
            case "WrongPredecessor": previous = previous with { InstallationId = "modular-dungeon-v6-final-felucca-5256-808" }; break;
            case "WrongPriorBlueprint": previous = previous with { BlueprintSha256 = new string('a', 64) }; break;
            case "WrongPriorNative": previous = previous with { InstalledNativeSha256 = new string('a', 64) }; break;
            case "ChangedDoor":
            {
                var doors = (ModularDungeonDeployment.DoorDefinition[])next.Doors.Clone();
                doors[0] = doors[0] with { ClosedId = doors[0].ClosedId + 2 };
                next = next with { Doors = doors };
                break;
            }
            case "ChangedCellar": next = next with { HiddenCellar = next.HiddenCellar with { Entry = next.HiddenCellar.Entry with { X = next.HiddenCellar.Entry.X + 1 } } }; break;
            case "ChangedChestPosition":
            case "ChangedChestApproach":
            case "ChangedChestArt":
            case "ChangedChestClass":
            {
                var chests = (ModularDungeonDeployment.ChestDefinition[])next.Chests.Clone();
                var chest = chests[0];
                chests[0] = corruption switch
                {
                    "ChangedChestApproach" => chest with { Approach = chest.Approach with { X = chest.Approach.X + 1 } },
                    "ChangedChestArt" => chest with { ItemId = 0xE43 },
                    "ChangedChestClass" => chest with { Type = "RootboundCellarChest" },
                    _ => chest with { Position = chest.Position with { X = chest.Position.X + 1 } }
                };
                next = next with { Chests = chests };
                break;
            }
            case "ChangedRetainedStair":
            case "ChangedR14Flight1":
            case "ChangedR14Flight2":
            case "ChangedR17Flight1":
            case "ChangedR17Flight2":
            {
                var lanes = (ModularDungeonDeployment.StairLane[])next.StairLanes.Clone();
                var identity = corruption switch
                {
                    "ChangedR14Flight1" => "R14-full-level-1-stairs-lane-1",
                    "ChangedR14Flight2" => "R14-full-level-2-stairs-lane-1",
                    "ChangedR17Flight1" => "R17-full-level-1-stairs-lane-1",
                    "ChangedR17Flight2" => "R17-full-level-2-stairs-lane-1",
                    _ => previous.StairLanes[0].Id
                };
                var index = Array.FindIndex(lanes, lane => lane.Id == identity);
                var points = (ModularDungeonDeployment.Coordinate[])lanes[index].Points.Clone();
                for (var i = 0; i < points.Length; i++)
                {
                    points[i] = points[i] with { X = points[i].X + 1 };
                }
                lanes[index] = lanes[index] with { Points = points };
                next = next with { StairLanes = lanes };
                break;
            }
            case "ChangedRetainedEscape":
            case "ChangedCollapsedEscape":
            {
                var routes = (ModularDungeonDeployment.EscapeRoute[])next.EscapeRoutes.Clone();
                var identity = corruption == "ChangedCollapsedEscape" ? "R17-collapsed-floor-escape" : previous.EscapeRoutes[0].Id;
                var index = Array.FindIndex(routes, route => route.Id == identity);
                routes[index] = routes[index] with
                {
                    From = routes[index].From with { X = routes[index].From.X + 1 },
                    To = routes[index].To with { X = routes[index].To.X + 1 }
                };
                next = next with { EscapeRoutes = routes };
                break;
            }
            case "MissingStairLane": next = next with { StairLanes = next.StairLanes[..^1] }; break;
            case "DuplicateStairIdentity":
            {
                var lanes = (ModularDungeonDeployment.StairLane[])next.StairLanes.Clone();
                lanes[^1] = lanes[0];
                next = next with { StairLanes = lanes };
                break;
            }
            case "DuplicateEscapeIdentity":
            {
                var routes = (ModularDungeonDeployment.EscapeRoute[])next.EscapeRoutes.Clone();
                routes[^1] = routes[0];
                next = next with { EscapeRoutes = routes };
                break;
            }
        }
        Assert.NotNull(Record.Exception(() =>
        {
            InvokeStatic("ValidateManifest", next);
            InvokeStatic("ValidateFullLevelDefinitions", previous, next);
        }));
    }

    [SkippableTheory]
    [InlineData("WrongManifestPin")]
    [InlineData("WrongReceiptPin")]
    [InlineData("DifferentReceiptBytes")]
    [InlineData("DifferentManifestBytes")]
    [InlineData("FailedReceipt")]
    [InlineData("WrongReceiptVersion")]
    [InlineData("WrongNative")]
    [InlineData("WrongChestClass")]
    [InlineData("MissingChest")]
    [InlineData("DuplicateDoorSerial")]
    [InlineData("DuplicateChestSerial")]
    public void FullLevelOwnership_RejectsUnpinnedOrChangedRecordedState(string corruption)
    {
        var previous = PreviousV15();
        var (receipt, features, reward) = RetainedV15(previous);
        var next = PinFixture(previous, receipt);
        switch (corruption)
        {
            case "FailedReceipt": receipt = receipt with { Success = false }; break;
            case "WrongReceiptVersion": receipt = receipt with { InstallationId = "modular-dungeon-v6-final-felucca-5256-808" }; break;
            case "WrongNative": receipt = receipt with { NativeSha256 = new string('a', 64) }; break;
            case "WrongChestClass":
            case "DuplicateChestSerial":
            {
                var chests = (ModularDungeonDeployment.DoorSerial[])receipt.Chests.Clone();
                chests[0] = chests[0] with
                {
                    Serial = corruption == "WrongChestClass" ? (uint)_fixture.Track(new Item(1)).Serial : chests[1].Serial
                };
                receipt = receipt with { Chests = chests };
                break;
            }
            case "MissingChest": World.FindItem((Serial)receipt.Chests[0].Serial).Delete(); break;
            case "DuplicateDoorSerial":
            {
                var doors = (ModularDungeonDeployment.DoorSerial[])receipt.Doors.Clone();
                doors[0] = doors[0] with { Serial = doors[1].Serial };
                receipt = receipt with { Doors = doors };
                break;
            }
        }
        next = next with { PreviousReceiptSha256 = Hash(receipt) };
        WritePrevious(previous, receipt);
        var path = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV15");
        switch (corruption)
        {
            case "WrongManifestPin": next = next with { PreviousManifestSha256 = new string('a', 64) }; break;
            case "WrongReceiptPin": next = next with { PreviousReceiptSha256 = new string('a', 64) }; break;
            case "DifferentReceiptBytes": File.AppendAllText(Path.Combine(path, "installed.json"), "\n"); break;
            case "DifferentManifestBytes": File.AppendAllText(Path.Combine(path, "install-manifest.json"), "\n"); break;
        }
        Assert.Throws<TargetInvocationException>(() => _fixture.NewJob(next));
        Assert.False(features.Hatch.Deleted);
        Assert.False(features.ReturnRune.Deleted);
        Assert.False(reward.Deleted);
    }

    [SkippableFact]
    public void OriginalMapAudit_NeverMovesOrInternalizesRetainedObjects()
    {
        var previous = PreviousV15();
        var (receipt, features, _) = RetainedV15(previous);
        WritePrevious(previous, receipt);
        var job = Activator.CreateInstance(_jobType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [PinFixture(previous, receipt), new string('d', 64), true], null);
        _auditJobs.Add(job);
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "PreflightAndCreateDoors"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "InternalizeCompletedChests"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "PlaceCompletedChests"));
        AssertOriginalChests(previous, receipt);
        Assert.Equal(previous.HiddenCellar.Hatch.Point, features.Hatch.Location);
        var result = (ModularDungeonDeployment.Result)InvokeJob(job, "CreateResult", true, "audit", null, false);
        Assert.Equal(receipt.Features, result.Features);
        Assert.Equal(receipt.Creatures, result.Creatures);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullLevelInstallAndFailure_PreserveAllSerialsContentsOpenStatesAndConsumedCellarState(bool consumed)
    {
        Skip.If(!TestServerInitializer.TileDataLoaded, "Native support checks require tile data.");
        var previous = PreviousV15();
        var (receipt, features, reward) = RetainedV15(previous);
        var door = (EventDungeonDoor)World.FindItem((Serial)receipt.Doors[0].Serial);
        door.Open = true;
        var markers = new Dictionary<uint, Gold>();
        foreach (var serial in receipt.Chests)
        {
            var chest = (Container)World.FindItem((Serial)serial.Serial);
            Empty(chest);
            var marker = _fixture.Track(new Gold(13));
            chest.DropItem(marker);
            markers.Add(serial.Serial, marker);
        }
        if (consumed)
        {
            features.Cover.Delete();
            features.Lich.Delete();
        }
        else
        {
            _fixture.Track(new Backpack()).DropItem(features.Cover);
            features.Lich.MoveToWorld(new Point3D(features.Lich.X + 1, features.Lich.Y, features.Lich.Z), Map.Felucca);
        }
        var coverParent = features.Cover.Parent;
        var lichLocation = features.Lich.Location;
        WritePrevious(previous, receipt);
        var job = _fixture.NewJob(PinFixture(previous, receipt));
        InvokeJob(job, "PreflightAndCreateDoors");
        AssertOriginalChests(previous, receipt);
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "InternalizeCompletedChests"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "PlaceCompletedChests"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "CreateHiddenCellar"));
        var result = (ModularDungeonDeployment.Result)InvokeJob(job, "CreateResult", true, "installed", null, false);
        Assert.Equal(receipt.Doors, result.Doors);
        Assert.Equal(receipt.Chests, result.Chests);
        Assert.Equal(receipt.Features, result.Features);
        Assert.Equal(receipt.Creatures, result.Creatures);
        Assert.True(door.Open);
        InvokeJob(job, "CloseDoors");
        InvokeJob(job, "Fail", new InvalidOperationException("deliberate full-level survey failure"));
        AssertOriginalChests(previous, receipt);
        foreach (var serial in receipt.Chests)
        {
            Assert.Same(markers[serial.Serial], Assert.Single(((Container)World.FindItem((Serial)serial.Serial)).Items));
        }
        Assert.Same(markers[(uint)reward.Serial], Assert.Single(reward.Items));
        Assert.True(door.Open);
        Assert.False(features.Hatch.Deleted);
        Assert.False(features.ReturnRune.Deleted);
        Assert.Equal(consumed, features.Cover.Deleted);
        Assert.Equal(consumed, features.Lich.Deleted);
        if (!consumed)
        {
            Assert.Same(coverParent, features.Cover.Parent);
            Assert.Equal(lichLocation, features.Lich.Location);
        }
    }

    [SkippableFact]
    public void ReceiptFailure_PreservesV15ObjectsAndRequiresConfirmedRollbackSnapshot()
    {
        var previous = PreviousV15();
        var (receipt, features, reward) = RetainedV15(previous);
        WritePrevious(previous, receipt);
        var previousPath = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV15", "installed.json");
        var previousBytes = File.ReadAllBytes(previousPath);
        var job = _fixture.NewJob(PinFixture(previous, receipt));
        var doors = (List<EventDungeonDoor>)JobField("_doors").GetValue(job);
        var chests = (List<EventDungeonChest>)JobField("_chests").GetValue(job);
        foreach (var serial in receipt.Doors)
        {
            doors.Add((EventDungeonDoor)World.FindItem((Serial)serial.Serial));
        }
        foreach (var serial in receipt.Chests)
        {
            chests.Add((EventDungeonChest)World.FindItem((Serial)serial.Serial));
        }
        JobField("_installedResultWritten").SetValue(job, true);
        var directory = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV16");
        var installed = (ModularDungeonDeployment.Result)InvokeJob(job, "CreateResult", true, "installed", null, false);
        File.WriteAllBytes(Path.Combine(directory, "installed.json"), JsonSerializer.SerializeToUtf8Bytes(installed, _json));
        InvokeJob(job, "Fail", new InvalidOperationException("deliberate receipt retirement failure"));
        AssertOriginalChests(previous, receipt);
        Assert.Equal(previousBytes, File.ReadAllBytes(previousPath));
        Assert.False(JsonSerializer.Deserialize<ModularDungeonDeployment.Result>(File.ReadAllBytes(Path.Combine(directory, "installed.json")), _json).Success);
        Assert.Equal("Rollback", JobField("_phase").GetValue(job).ToString());
        Assert.False(features.Hatch.Deleted);
        Assert.False(features.ReturnRune.Deleted);
        Assert.False(features.Lich.Deleted);
        Assert.False(reward.Deleted);
        var savePath = World.SavePath;
        try
        {
            var fixtureSavePath = Path.Combine(_fixture.FixtureRoot, "Saves");
            Directory.CreateDirectory(fixtureSavePath);
            World.SetSavePathForTest(fixtureSavePath);
            JobField("_phase").SetValue(job, Enum.Parse(JobField("_phase").FieldType, "AwaitRollbackSave"));
            JobField("_saveDirectoryTimestamp").SetValue(job, DateTime.MinValue);
            JobField("_saveDeadline").SetValue(job, Core.TickCount + 120000);
            InvokeJob(job, "CheckSave", true);
            Assert.Equal("AwaitRollbackSave", JobField("_phase").GetValue(job).ToString());
            Assert.False(JsonSerializer.Deserialize<ModularDungeonDeployment.Result>(File.ReadAllBytes(Path.Combine(directory, "install-state.json")), _json).RollbackSaveConfirmed);
            InvokeJob(job, "OnSuccessfulSnapshot");
            InvokeJob(job, "CheckSave", true);
            var rolledBack = JsonSerializer.Deserialize<ModularDungeonDeployment.Result>(File.ReadAllBytes(Path.Combine(directory, "install-state.json")), _json);
            Assert.Equal("rolled-back", rolledBack.Stage);
            Assert.True(rolledBack.RollbackSaveConfirmed);
            Assert.False(rolledBack.Success);
            Assert.Equal(previousBytes, File.ReadAllBytes(previousPath));
            AssertOriginalChests(previous, receipt);
        }
        finally
        {
            World.SetSavePathForTest(savePath);
        }
    }

    [SkippableTheory]
    [InlineData("Player")]
    [InlineData("Pet")]
    [InlineData("Item")]
    public void FullLevelOccupancy_RefusesPlayersPetsAndUnrelatedItems(string actor)
    {
        var previous = PreviousV15();
        var (receipt, _, _) = RetainedV15(previous);
        WritePrevious(previous, receipt);
        var job = _fixture.NewJob(PinFixture(previous, receipt));
        Assert.Null(Record.Exception(() => InvokeJob(job, "ValidateOccupancy", previous.Bounds.Rectangle, false)));
        if (actor == "Item")
        {
            _fixture.Track(new Item(1)).MoveToWorld(previous.Entry.Point, Map.Felucca);
        }
        else
        {
            _fixture.Track(new Mobile { Player = actor == "Player" }).MoveToWorld(previous.Entry.Point, Map.Felucca);
        }
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "ValidateOccupancy", previous.Bounds.Rectangle, false));
    }

    [SkippableFact]
    public void FullLevelSurvey_ChecksEveryLaneBothWaysAndCollapsedFloorThroughNativeMovement()
    {
        var previous = PreviousV15();
        var (receipt, _, _) = RetainedV15(previous);
        WritePrevious(previous, receipt);
        var next = PinFixture(previous, receipt);
        var job = _fixture.NewJob(next);
        var priorImpl = CalcMovement.Impl;
        var priorIgnore = MovementImpl.IgnoreMovableImpassables;
        var movement = new RouteMovement(next);
        try
        {
            CalcMovement.Impl = movement;
            for (var i = 0; i < next.StairLanes.Length; i++)
            {
                InvokeJob(job, "CheckStairLanes");
            }
            Assert.Equal(288, movement.Calls);
            for (var i = 0; i < next.EscapeRoutes.Length; i++)
            {
                InvokeJob(job, "CheckEscapeRoutes");
            }
            Assert.Equal(294, movement.Calls);
            Assert.Equal(priorIgnore, MovementImpl.IgnoreMovableImpassables);
            var results = (List<ModularDungeonDeployment.MovementResult>)JobField("_movementResults").GetValue(job);
            foreach (var lane in next.StairLanes)
            {
                Assert.Contains(new(lane.Id, "stair-lane-both-directions", true), results);
            }
            Assert.Contains(new("R17-collapsed-floor-escape", "collapsed-floor-descent", true), results);
            Assert.Equal("Reachability", JobField("_phase").GetValue(job).ToString());
            var targets = (HashSet<Point3D>)JobField("_targets").GetValue(job);
            foreach (var module in next.Modules)
            {
                Assert.Contains(module.Start.Point, targets);
                foreach (var approach in module.Approaches)
                {
                    Assert.Contains(approach.Point.Point, targets);
                }
            }
            foreach (var route in next.EscapeRoutes)
            {
                Assert.Contains(route.From.Point, targets);
                Assert.Contains(route.To.Point, targets);
            }
        }
        finally
        {
            CalcMovement.Impl = priorImpl;
            MovementImpl.IgnoreMovableImpassables = priorIgnore;
        }
    }

    [SkippableTheory]
    [InlineData("R14-full-level-1-stairs-lane-1")]
    [InlineData("R14-full-level-2-stairs-lane-1")]
    [InlineData("R17-full-level-1-stairs-lane-1")]
    [InlineData("R17-full-level-2-stairs-lane-1")]
    [InlineData("R17-collapsed-floor-escape")]
    public void FullLevelSurvey_RefusesAnIndependentlyBlockedFlightOrEscape(string identity)
    {
        var previous = PreviousV15();
        var (receipt, _, _) = RetainedV15(previous);
        WritePrevious(previous, receipt);
        var next = PinFixture(previous, receipt);
        var job = _fixture.NewJob(next);
        var priorImpl = CalcMovement.Impl;
        var priorIgnore = MovementImpl.IgnoreMovableImpassables;
        var movement = new RouteMovement(next);
        var laneIndex = Array.FindIndex(next.StairLanes, lane => lane.Id == identity);
        var routeIndex = Array.FindIndex(next.EscapeRoutes, route => route.Id == identity);
        movement.FailFrom = laneIndex >= 0 ? next.StairLanes[laneIndex].Points[0].Point : next.EscapeRoutes[routeIndex].From.Point;
        try
        {
            CalcMovement.Impl = movement;
            JobField("_cursor").SetValue(job, laneIndex >= 0 ? laneIndex : routeIndex);
            Assert.Throws<TargetInvocationException>(() => InvokeJob(job, laneIndex >= 0 ? "CheckStairLanes" : "CheckEscapeRoutes"));
            Assert.Equal(priorIgnore, MovementImpl.IgnoreMovableImpassables);
            var results = (List<ModularDungeonDeployment.MovementResult>)JobField("_movementResults").GetValue(job);
            Assert.DoesNotContain(results, result => result.Id == identity);
        }
        finally
        {
            CalcMovement.Impl = priorImpl;
            MovementImpl.IgnoreMovableImpassables = priorIgnore;
        }
    }

    [Fact]
    public void FullLevelsV16Receipt_TakesStartupPriorityWithoutRebuilding()
    {
        typeof(Core).GetField("_baseDirectory", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _fixture.FixtureRoot);
        var directory = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV16");
        File.WriteAllText(Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV15", "installed.json"), "{\"success\":true}");
        File.WriteAllText(Path.Combine(directory, "installed.json"), "{\"success\":true}");
        File.WriteAllText(Path.Combine(directory, "install.completed"), "{\"manifestSha256\":\"already-completed\"}");
        ModularDungeonDeployment.Initialize();
        Assert.True((bool)_fullLevelsField.GetValue(null));
        Assert.Equal(V16Identity, _deployment.GetProperty("CurrentInstallationId", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.Equal(directory, _deployment.GetProperty("DirectoryPath", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.Null(_deployment.GetField("_job", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.False(File.Exists(Path.Combine(directory, "install-state.json")));
        Assert.Equal("{\"success\":true}", File.ReadAllText(Path.Combine(directory, "installed.json")));
    }

    private (ModularDungeonDeployment.Result Receipt, RootboundCellarFeature.Features Features, RootboundCellarChest Reward) RetainedV15(
        ModularDungeonDeployment.Manifest previous)
    {
        var (receipt, features, reward) = _fixture.RetainedWorld(previous, true);
        return (receipt with { InstallationId = V15Identity, StaticCount = 17518 }, features, reward);
    }

    private void WritePrevious(ModularDungeonDeployment.Manifest previous, ModularDungeonDeployment.Result receipt)
    {
        var path = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV15");
        File.WriteAllBytes(Path.Combine(path, "install-manifest.json"), JsonSerializer.SerializeToUtf8Bytes(previous, _json));
        File.WriteAllBytes(Path.Combine(path, "installed.json"), JsonSerializer.SerializeToUtf8Bytes(receipt, _json));
        typeof(Core).GetField("_baseDirectory", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _fixture.FixtureRoot);
        _completedField.SetValue(null, true);
        _fullLevelsField.SetValue(null, true);
    }

    private static void AssertOriginalChests(ModularDungeonDeployment.Manifest previous, ModularDungeonDeployment.Result receipt)
    {
        foreach (var serial in receipt.Chests)
        {
            var chest = World.FindItem((Serial)serial.Serial);
            var definition = Array.Find(previous.Chests, candidate => candidate.Id == serial.Id);
            Assert.NotNull(chest);
            Assert.False(chest.Deleted);
            Assert.Equal(Map.Felucca, chest.Map);
            Assert.Equal(definition.Position.Point, chest.Location);
            Assert.Equal(definition.ItemId, chest.ItemID);
        }
    }

    private static ModularDungeonDeployment.Manifest PreviousV15() => ReadStage("install-v15-20261010-r1");
    private static ModularDungeonDeployment.Manifest FullLevelsManifest() => ReadStage("install-v16-20261010-r1");
    private static ModularDungeonDeployment.Manifest ReadStage(string stage)
    {
        var path = FindArtifact(stage);
        Skip.If(path == null, "The exact frozen native stage is required.");
        return JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(File.ReadAllBytes(path), _json);
    }
    private static ModularDungeonDeployment.Manifest PinFixture(ModularDungeonDeployment.Manifest previous, ModularDungeonDeployment.Result receipt) =>
        FullLevelsManifest() with { PreviousManifestSha256 = Hash(previous), PreviousReceiptSha256 = Hash(receipt) };
    private static string Hash<T>(T value) => HashBytes(JsonSerializer.SerializeToUtf8Bytes(value, _json));
    private static string HashBytes(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // This verifies native movement dispatch and refusal paths; the staged-map audit proves physical tile geometry.
    private sealed class RouteMovement : IMovementImpl
    {
        private readonly Dictionary<(Point3D From, Direction Direction), int> _destinations = new();
        public int Calls { get; private set; }
        public Point3D? FailFrom { get; set; }

        public RouteMovement(ModularDungeonDeployment.Manifest manifest)
        {
            foreach (var lane in manifest.StairLanes)
            {
                for (var i = 1; i < lane.Points.Length; i++)
                {
                    Add(lane.Points[i - 1].Point, lane.Points[i].Point);
                    Add(lane.Points[i].Point, lane.Points[i - 1].Point);
                }
            }
            foreach (var route in manifest.EscapeRoutes)
            {
                Add(route.From.Point, route.To.Point);
            }
        }

        public bool CheckMovement(Mobile mobile, Direction direction, out int newZ) =>
            CheckMovement(mobile, mobile.Map, mobile.Location, direction, out newZ);

        public bool CheckMovement(Mobile mobile, Map map, Point3D from, Direction direction, out int newZ)
        {
            Calls++;
            Assert.True(mobile.Player);
            Assert.Equal(AccessLevel.Player, mobile.AccessLevel);
            Assert.Equal(Map.Internal, mobile.Map);
            Assert.Equal(Map.Felucca, map);
            Assert.True(MovementImpl.IgnoreMovableImpassables);
            newZ = from.Z;
            return FailFrom != from && _destinations.TryGetValue((from, direction), out newZ);
        }

        private void Add(Point3D from, Point3D to)
        {
            var direction = (to.X - from.X, to.Y - from.Y) switch
            {
                (0, -1) => Direction.North,
                (1, 0) => Direction.East,
                (0, 1) => Direction.South,
                (-1, 0) => Direction.West,
                _ => throw new InvalidOperationException("A fixture route must be cardinally adjacent.")
            };
            if (_destinations.TryGetValue((from, direction), out var existing))
            {
                Assert.Equal(to.Z, existing);
            }
            _destinations[(from, direction)] = to.Z;
        }
    }
}
