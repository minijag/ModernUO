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
using static UOContent.Tests.Engines.ModularDungeon.ModularDungeonV6DeploymentTests;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ModularDungeonV17DeploymentTests : IDisposable
{
    private const string V16Identity = "modular-dungeon-v16-final-felucca-5256-808";
    private const string V17Identity = "modular-dungeon-v17-final-felucca-5256-808";
    private const string BlueprintSha256 = "391ba3505002ef9f20b72b4f5ea767227c049742bd23ffdfe533e7a4ffccde82";
    private static readonly Type _deployment = typeof(ModularDungeonDeployment);
    private static readonly Type _jobType = _deployment.GetNestedType("Job", BindingFlags.NonPublic);
    private static readonly FieldInfo _ossuaryField = _deployment.GetField("_ossuary", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo _fullLevelsField = _deployment.GetField("_fullLevels", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo _completedField = _deployment.GetField("_completed", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly bool _previousOssuary = (bool)_ossuaryField.GetValue(null);
    private readonly bool _previousFullLevels = (bool)_fullLevelsField.GetValue(null);
    private readonly bool _previousCompleted = (bool)_completedField.GetValue(null);
    private readonly ModularDungeonV6DeploymentTests _fixture = new();
    private readonly List<object> _auditJobs = new();

    public ModularDungeonV17DeploymentTests()
    {
        _ossuaryField.SetValue(null, false);
        _fullLevelsField.SetValue(null, false);
        _completedField.SetValue(null, false);
        Directory.CreateDirectory(Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV16"));
        Directory.CreateDirectory(Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV17"));
    }

    public void Dispose()
    {
        foreach (var job in _auditJobs)
        {
            InvokeJob(job, "Stop");
        }
        _fixture.Dispose();
        _ossuaryField.SetValue(null, _previousOssuary);
        _fullLevelsField.SetValue(null, _previousFullLevels);
        _completedField.SetValue(null, _previousCompleted);
    }

    [SkippableFact]
    public void ExactOssuaryContinuation_RemovesOnlyItsTwoLanesAndBasementFocusAndMovesOneChest()
    {
        var previous = PreviousV16();
        var next = OssuaryManifest(previous);
        InvokeStatic("ValidateManifest", next);
        InvokeStatic("ValidateOssuaryDefinitions", previous, next);
        Assert.Equal(21020, next.StaticCount);
        Assert.Equal(21195, next.OriginalStaticCount);
        Assert.Equal(22, next.StairLanes.Length);
        Assert.Equal(6, next.EscapeRoutes.Length);
        Assert.Equal(100, next.Chests.Length);
        Assert.Equal(BlueprintSha256, next.BlueprintSha256);
        Assert.Equal("bf27d8c62210cfb4b8b5c6e814640fb2fb3500038890a1a0913a20488443abce", next.PreviousManifestSha256);
        Assert.Equal("e323973da3e9d70072d269d7f6da2403bcee304331bc193bb3b19ea710cbbb13", next.PreviousReceiptSha256);
        Assert.Equal(previous.HiddenCellar, next.HiddenCellar);
        Assert.Equal(JsonSerializer.Serialize(previous.Doors, _json), JsonSerializer.Serialize(next.Doors, _json));
        Assert.Equal(previous.EscapeRoutes, next.EscapeRoutes);
        Assert.DoesNotContain(next.StairLanes, lane => lane.ModuleId == "M02");
        Assert.DoesNotContain(Array.Find(next.Modules, module => module.Id == "M02").Approaches,
            approach => approach.Id == "R02-basement-focus");
        var expected = Array.Find(next.Chests, chest => chest.Id == "M02-chest-1");
        Assert.Equal(new(5316, 818, 0), expected.Position);
        Assert.Equal(new(5316, 817, 0), expected.Approach);
    }

    [SkippableTheory]
    [InlineData("MissingPin")]
    [InlineData("WrongBlueprint")]
    [InlineData("SceneryAlias")]
    [InlineData("WrongStaticCount")]
    [InlineData("WrongPreviousCount")]
    [InlineData("WrongPredecessor")]
    [InlineData("WrongPriorBlueprint")]
    [InlineData("WrongPriorNative")]
    [InlineData("WrongPriorCellarPosition")]
    [InlineData("ChangedDoor")]
    [InlineData("ChangedCellar")]
    [InlineData("ChangedOtherChestPosition")]
    [InlineData("ChangedOtherChestApproach")]
    [InlineData("ChangedCellarChestPosition")]
    [InlineData("ChangedCellarChestApproach")]
    [InlineData("ChangedCellarChestArt")]
    [InlineData("ChangedCellarChestClass")]
    [InlineData("ChangedRetainedStair")]
    [InlineData("RemovedWrongStair")]
    [InlineData("RetainedOssuaryStair")]
    [InlineData("ChangedEscape")]
    [InlineData("ChangedOtherModule")]
    [InlineData("ChangedOtherModuleApproach")]
    [InlineData("ChangedOssuaryStart")]
    [InlineData("RemovedOtherOssuaryApproach")]
    [InlineData("RetainedBasementFocus")]
    [InlineData("AddedOssuaryApproach")]
    public void OssuaryManifest_RejectsAnyUnapprovedGeometryOrIdentity(string corruption)
    {
        var previous = PreviousV16();
        var next = OssuaryManifest(previous);
        var chests = (ModularDungeonDeployment.ChestDefinition[])next.Chests.Clone();
        var chestIndex = Array.FindIndex(chests, chest => chest.Id == "M02-chest-1");
        var modules = (ModularDungeonDeployment.ModuleDefinition[])next.Modules.Clone();
        var moduleIndex = Array.FindIndex(modules, module => module.Id == "M02");
        switch (corruption)
        {
            case "MissingPin": next = next with { PreviousReceiptSha256 = null }; break;
            case "WrongBlueprint": next = next with { BlueprintSha256 = new string('a', 64) }; break;
            case "SceneryAlias": next = next with { SceneryOnly = true, SceneryModuleId = "M02" }; break;
            case "WrongStaticCount": next = next with { StaticCount = 21019 }; break;
            case "WrongPreviousCount": next = next with { OriginalStaticCount = 17518 }; break;
            case "WrongPredecessor": previous = previous with { InstallationId = "modular-dungeon-v15-final-felucca-5256-808" }; break;
            case "WrongPriorBlueprint": previous = previous with { BlueprintSha256 = new string('a', 64) }; break;
            case "WrongPriorNative": previous = previous with { InstalledNativeSha256 = new string('a', 64) }; break;
            case "WrongPriorCellarPosition":
            {
                var oldChests = (ModularDungeonDeployment.ChestDefinition[])previous.Chests.Clone();
                oldChests[chestIndex] = oldChests[chestIndex] with { Position = new(5313, 818, -25) };
                previous = previous with { Chests = oldChests };
                break;
            }
            case "ChangedDoor":
            {
                var doors = (ModularDungeonDeployment.DoorDefinition[])next.Doors.Clone();
                doors[0] = doors[0] with { ClosedId = doors[0].ClosedId + 2 };
                next = next with { Doors = doors };
                break;
            }
            case "ChangedCellar": next = next with { HiddenCellar = next.HiddenCellar with { Entry = next.HiddenCellar.Entry with { X = next.HiddenCellar.Entry.X + 1 } } }; break;
            case "ChangedOtherChestPosition": chests[0] = chests[0] with { Position = chests[0].Position with { X = chests[0].Position.X + 1 } }; next = next with { Chests = chests }; break;
            case "ChangedOtherChestApproach": chests[0] = chests[0] with { Approach = chests[0].Approach with { X = chests[0].Approach.X + 1 } }; next = next with { Chests = chests }; break;
            case "ChangedCellarChestPosition": chests[chestIndex] = chests[chestIndex] with { Position = new(5317, 818, 0) }; next = next with { Chests = chests }; break;
            case "ChangedCellarChestApproach": chests[chestIndex] = chests[chestIndex] with { Approach = new(5316, 819, 0) }; next = next with { Chests = chests }; break;
            case "ChangedCellarChestArt": chests[chestIndex] = chests[chestIndex] with { ItemId = 0xE43 }; next = next with { Chests = chests }; break;
            case "ChangedCellarChestClass": chests[chestIndex] = chests[chestIndex] with { Type = "RootboundCellarChest" }; next = next with { Chests = chests }; break;
            case "ChangedRetainedStair":
            {
                var lanes = (ModularDungeonDeployment.StairLane[])next.StairLanes.Clone();
                var points = (ModularDungeonDeployment.Coordinate[])lanes[0].Points.Clone();
                for (var i = 0; i < points.Length; i++)
                {
                    points[i] = points[i] with { X = points[i].X + 1 };
                }
                lanes[0] = lanes[0] with { Points = points };
                next = next with { StairLanes = lanes };
                break;
            }
            case "RemovedWrongStair":
            case "RetainedOssuaryStair":
            {
                var lanes = (ModularDungeonDeployment.StairLane[])next.StairLanes.Clone();
                lanes[0] = Array.Find(previous.StairLanes, lane => lane.Id == "R02-basement-stairs-lane-0");
                next = next with { StairLanes = lanes };
                break;
            }
            case "ChangedEscape":
            {
                var escapes = (ModularDungeonDeployment.EscapeRoute[])next.EscapeRoutes.Clone();
                escapes[0] = escapes[0] with { From = escapes[0].From with { X = escapes[0].From.X + 1 }, To = escapes[0].To with { X = escapes[0].To.X + 1 } };
                next = next with { EscapeRoutes = escapes };
                break;
            }
            case "ChangedOtherModule": modules[0] = modules[0] with { Name = "Unapproved room" }; next = next with { Modules = modules }; break;
            case "ChangedOtherModuleApproach":
            {
                var approaches = (ModularDungeonDeployment.Approach[])modules[0].Approaches.Clone();
                approaches[0] = approaches[0] with { Point = approaches[0].Point with { X = approaches[0].Point.X + 1 } };
                modules[0] = modules[0] with { Approaches = approaches };
                next = next with { Modules = modules };
                break;
            }
            case "ChangedOssuaryStart": modules[moduleIndex] = modules[moduleIndex] with { Start = new(5310, 826, 0) }; next = next with { Modules = modules }; break;
            case "RemovedOtherOssuaryApproach": modules[moduleIndex] = modules[moduleIndex] with { Approaches = modules[moduleIndex].Approaches[1..] }; next = next with { Modules = modules }; break;
            case "RetainedBasementFocus": modules[moduleIndex] = previous.Modules[moduleIndex]; next = next with { Modules = modules }; break;
            case "AddedOssuaryApproach":
            {
                var approaches = new List<ModularDungeonDeployment.Approach>(modules[moduleIndex].Approaches) { new("unapproved", new(5316, 817, 0)) };
                modules[moduleIndex] = modules[moduleIndex] with { Approaches = approaches.ToArray() };
                next = next with { Modules = modules };
                break;
            }
        }
        Assert.NotNull(Record.Exception(() =>
        {
            InvokeStatic("ValidateManifest", next);
            InvokeStatic("ValidateOssuaryDefinitions", previous, next);
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
    [InlineData("MissingChest")]
    [InlineData("MovedChest")]
    [InlineData("WrongChestClass")]
    [InlineData("DuplicateChestSerial")]
    [InlineData("DuplicateDoorSerial")]
    public void OssuaryOwnership_RejectsUnpinnedOrChangedPredecessor(string corruption)
    {
        var previous = PreviousV16();
        var (receipt, features, reward) = RetainedV16(previous);
        switch (corruption)
        {
            case "FailedReceipt": receipt = receipt with { Success = false }; break;
            case "WrongReceiptVersion": receipt = receipt with { InstallationId = "modular-dungeon-v15-final-felucca-5256-808" }; break;
            case "WrongNative": receipt = receipt with { NativeSha256 = new string('a', 64) }; break;
            case "MissingChest": World.FindItem((Serial)receipt.Chests[0].Serial).Delete(); break;
            case "MovedChest": World.FindItem((Serial)receipt.Chests[0].Serial).MoveToWorld(new Point3D(5310, 826, 0), Map.Felucca); break;
            case "WrongChestClass":
            case "DuplicateChestSerial":
            {
                var chests = (ModularDungeonDeployment.DoorSerial[])receipt.Chests.Clone();
                chests[0] = chests[0] with { Serial = corruption == "WrongChestClass" ? (uint)_fixture.Track(new Item(1)).Serial : chests[1].Serial };
                receipt = receipt with { Chests = chests };
                break;
            }
            case "DuplicateDoorSerial":
            {
                var doors = (ModularDungeonDeployment.DoorSerial[])receipt.Doors.Clone();
                doors[0] = doors[0] with { Serial = doors[1].Serial };
                receipt = receipt with { Doors = doors };
                break;
            }
        }
        var next = PinFixture(previous, receipt);
        WritePrevious(previous, receipt);
        var directory = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV16");
        switch (corruption)
        {
            case "WrongManifestPin": next = next with { PreviousManifestSha256 = new string('a', 64) }; break;
            case "WrongReceiptPin": next = next with { PreviousReceiptSha256 = new string('a', 64) }; break;
            case "DifferentReceiptBytes": File.AppendAllText(Path.Combine(directory, "installed.json"), "\n"); break;
            case "DifferentManifestBytes": File.AppendAllText(Path.Combine(directory, "install-manifest.json"), "\n"); break;
        }
        Assert.Throws<TargetInvocationException>(() => _fixture.NewJob(next));
        Assert.False(reward.Deleted);
        Assert.False(features.Hatch.Deleted);
        Assert.False(features.ReturnRune.Deleted);
    }

    [SkippableFact]
    public void OriginalMapAudit_CannotMoveChestOrValidatePatchedGroundOrCreateObjects()
    {
        var previous = PreviousV16();
        var (receipt, _, _) = RetainedV16(previous);
        WritePrevious(previous, receipt);
        var job = Activator.CreateInstance(_jobType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [PinFixture(previous, receipt), new string('d', 64), true], null);
        _auditJobs.Add(job);
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "PreflightAndCreateDoors"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "RelocateOssuaryChest"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "ValidateOssuaryGround"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "InternalizeCompletedChests"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "PlaceCompletedChests"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "CreateHiddenCellar"));
        AssertChests(previous, receipt);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SoleChestRelocationAndFailure_PreserveSerialsContentsDoorStatesAndConsumedCellar(bool consumed)
    {
        Skip.If(!TestServerInitializer.TileDataLoaded, "Native placement requires tile data.");
        var previous = PreviousV16();
        var (receipt, features, _) = RetainedV16(previous);
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
        var next = PinFixture(previous, receipt);
        var job = _fixture.NewJob(next);
        AddRestoredGroundSupport();
        InvokeJob(job, "RelocateOssuaryChest");
        AssertChests(next, receipt);
        PopulateRetainedLists(job, receipt);
        InvokeJob(job, "ValidateAllDoors");
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "InternalizeCompletedChests"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "PlaceCompletedChests"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "CreateHiddenCellar"));
        var result = (ModularDungeonDeployment.Result)InvokeJob(job, "CreateResult", true, "installed", null, false);
        Assert.Equal(receipt.Doors, result.Doors);
        Assert.Equal(receipt.Chests, result.Chests);
        Assert.Equal(receipt.Features, result.Features);
        Assert.Equal(receipt.Creatures, result.Creatures);
        Assert.True(door.Open);
        InvokeJob(job, "Fail", new InvalidOperationException("deliberate post-move survey failure"));
        AssertChests(previous, receipt);
        foreach (var serial in receipt.Chests)
        {
            Assert.Same(markers[serial.Serial], Assert.Single(((Container)World.FindItem((Serial)serial.Serial)).Items));
        }
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
        Assert.Equal("Rollback", JobField("_phase").GetValue(job).ToString());
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlockedNewPlacementOrOpeningApproach_RefusesBeforeMovingAnyChest(bool approach)
    {
        Skip.If(!TestServerInitializer.TileDataLoaded, "Native placement requires tile data.");
        var previous = PreviousV16();
        var (receipt, _, _) = RetainedV16(previous);
        WritePrevious(previous, receipt);
        var job = _fixture.NewJob(PinFixture(previous, receipt));
        AddRestoredGroundSupport();
        var tile = TileData.ItemTable[1];
        try
        {
            TileData.ItemTable[1] = new ItemData("Ossuary placement obstruction", TileFlag.Impassable, 0, 0, 0, 0, 0, 20);
            _fixture.Track(new Item(1) { Movable = false }).MoveToWorld(new Point3D(5316, approach ? 817 : 818, 0), Map.Felucca);
            Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "RelocateOssuaryChest"));
            AssertChests(previous, receipt);
        }
        finally
        {
            TileData.ItemTable[1] = tile;
        }
    }

    [SkippableTheory]
    [InlineData("Player")]
    [InlineData("Pet")]
    [InlineData("Item")]
    public void OssuaryOccupancy_RefusesPlayersPetsAndUnrelatedItems(string actor)
    {
        var previous = PreviousV16();
        var (receipt, _, _) = RetainedV16(previous);
        WritePrevious(previous, receipt);
        var job = _fixture.NewJob(PinFixture(previous, receipt));
        InvokeJob(job, "ValidateOccupancy", previous.Bounds.Rectangle, false);
        if (actor == "Item")
        {
            _fixture.Track(new Item(1)).MoveToWorld(new Point3D(5316, 818, 0), Map.Felucca);
        }
        else
        {
            _fixture.Track(new Mobile { Player = actor == "Player" }).MoveToWorld(new Point3D(5316, 818, 0), Map.Felucca);
        }
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "ValidateOccupancy", previous.Bounds.Rectangle, false));
        AssertChests(previous, receipt);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoredGround_RequiresEverySupportedCellReachableWithOnlyChestTileException(bool unreachable)
    {
        Skip.If(!TestServerInitializer.TileDataLoaded, "Native placement requires tile data.");
        var previous = PreviousV16();
        var (receipt, _, _) = RetainedV16(previous);
        WritePrevious(previous, receipt);
        var job = _fixture.NewJob(PinFixture(previous, receipt));
        AddRestoredGroundSupport();
        InvokeJob(job, "RelocateOssuaryChest");
        var visited = (HashSet<Point3D>)JobField("_visited").GetValue(job);
        for (var x = 5316; x <= 5317; x++)
        {
            for (var y = 814; y <= 818; y++)
            {
                if ((x, y) != (5316, 818))
                {
                    visited.Add(new(x, y, 0));
                }
            }
        }
        if (unreachable)
        {
            visited.Remove(new(5317, 818, 0));
            Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "ValidateOssuaryGround"));
        }
        else
        {
            InvokeJob(job, "ValidateOssuaryGround");
            var results = (List<ModularDungeonDeployment.MovementResult>)JobField("_movementResults").GetValue(job);
            Assert.Equal(10, results.Count);
            Assert.All(results, result => { Assert.True(result.Success); Assert.Equal("restored-ground-support", result.Kind); });
        }
    }

    [SkippableFact]
    public void ReceiptWriteFailure_RestoresOnlyChestAndWaitsForConfirmedNormalRollbackSave()
    {
        Skip.If(!TestServerInitializer.TileDataLoaded, "Native placement requires tile data.");
        var previous = PreviousV16();
        var (receipt, features, reward) = RetainedV16(previous);
        WritePrevious(previous, receipt);
        var previousPath = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV16", "installed.json");
        var previousBytes = File.ReadAllBytes(previousPath);
        var job = _fixture.NewJob(PinFixture(previous, receipt));
        AddRestoredGroundSupport();
        InvokeJob(job, "RelocateOssuaryChest");
        PopulateRetainedLists(job, receipt);
        JobField("_installedResultWritten").SetValue(job, true);
        var directory = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV17");
        var installed = (ModularDungeonDeployment.Result)InvokeJob(job, "CreateResult", true, "installed", null, false);
        File.WriteAllBytes(Path.Combine(directory, "installed.json"), JsonSerializer.SerializeToUtf8Bytes(installed, _json));
        InvokeJob(job, "Fail", new InvalidOperationException("deliberate receipt retirement failure"));
        AssertChests(previous, receipt);
        Assert.Equal(previousBytes, File.ReadAllBytes(previousPath));
        Assert.False(ReadResult(directory, "installed.json").Success);
        Assert.False(features.Hatch.Deleted);
        Assert.False(features.ReturnRune.Deleted);
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
            Assert.False(ReadResult(directory, "install-state.json").RollbackSaveConfirmed);
            InvokeJob(job, "OnSuccessfulSnapshot");
            InvokeJob(job, "CheckSave", true);
            Assert.True(ReadResult(directory, "install-state.json").RollbackSaveConfirmed);
            Assert.Equal("rolled-back", ReadResult(directory, "install-state.json").Stage);
            AssertChests(previous, receipt);
            Assert.Equal(previousBytes, File.ReadAllBytes(previousPath));
        }
        finally
        {
            World.SetSavePathForTest(savePath);
        }
    }

    [Fact]
    public void CompletedV17_TakesStartupPriorityWithoutAnyRedeployment()
    {
        typeof(Core).GetField("_baseDirectory", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _fixture.FixtureRoot);
        File.WriteAllText(Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV16", "installed.json"), "{\"success\":true}");
        var directory = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV17");
        File.WriteAllText(Path.Combine(directory, "installed.json"), "{\"success\":true}");
        File.WriteAllText(Path.Combine(directory, "install.completed"), "{\"manifestSha256\":\"already-completed\"}");
        ModularDungeonDeployment.Initialize();
        Assert.True((bool)_ossuaryField.GetValue(null));
        Assert.Equal(V17Identity, _deployment.GetProperty("CurrentInstallationId", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.Equal(directory, _deployment.GetProperty("DirectoryPath", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.Null(_deployment.GetField("_job", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.False(File.Exists(Path.Combine(directory, "install-state.json")));
        Assert.Equal("{\"success\":true}", File.ReadAllText(Path.Combine(directory, "installed.json")));
    }

    [SkippableFact]
    public void SuccessfulInstall_RequiresNormalSnapshotAndDiskPublicationBeforeRetiringMarker()
    {
        Skip.If(!TestServerInitializer.TileDataLoaded, "Native placement requires tile data.");
        var previous = PreviousV16();
        var (receipt, _, _) = RetainedV16(previous);
        WritePrevious(previous, receipt);
        var next = PinFixture(previous, receipt);
        var job = _fixture.NewJob(next);
        AddRestoredGroundSupport();
        InvokeJob(job, "RelocateOssuaryChest");
        PopulateRetainedLists(job, receipt);
        var directory = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV17");
        File.WriteAllBytes(Path.Combine(directory, "install.pending"),
            JsonSerializer.SerializeToUtf8Bytes(new ModularDungeonDeployment.Marker(new string('d', 64)), _json));
        var savePath = World.SavePath;
        try
        {
            var fixtureSavePath = Path.Combine(_fixture.FixtureRoot, "Saves");
            Directory.CreateDirectory(fixtureSavePath);
            World.SetSavePathForTest(fixtureSavePath);
            JobField("_phase").SetValue(job, Enum.Parse(JobField("_phase").FieldType, "AwaitSave"));
            JobField("_saveDirectoryTimestamp").SetValue(job, DateTime.MinValue);
            JobField("_saveDeadline").SetValue(job, Core.TickCount + 120000);
            InvokeJob(job, "CheckSave", false);
            Assert.False(File.Exists(Path.Combine(directory, "installed.json")));
            Assert.True(File.Exists(Path.Combine(directory, "install.pending")));
            InvokeJob(job, "OnSuccessfulSnapshot");
            JobField("_saveDirectoryTimestamp").SetValue(job, DateTime.MaxValue);
            InvokeJob(job, "CheckSave", false);
            Assert.False(File.Exists(Path.Combine(directory, "installed.json")));
            Assert.True(File.Exists(Path.Combine(directory, "install.pending")));
            JobField("_saveDirectoryTimestamp").SetValue(job, DateTime.MinValue);
            InvokeJob(job, "CheckSave", false);
            var installed = ReadResult(directory, "installed.json");
            Assert.True(installed.Success);
            Assert.Equal("installed", installed.Stage);
            Assert.Equal(receipt.Chests, installed.Chests);
            Assert.False(File.Exists(Path.Combine(directory, "install.pending")));
            Assert.True(File.Exists(Path.Combine(directory, "install.completed")));
            AssertChests(next, receipt);
        }
        finally
        {
            World.SetSavePathForTest(savePath);
        }
    }

    private (ModularDungeonDeployment.Result Receipt, RootboundCellarFeature.Features Features, RootboundCellarChest Reward) RetainedV16(
        ModularDungeonDeployment.Manifest previous)
    {
        var (receipt, features, reward) = _fixture.RetainedWorld(previous, true);
        return (receipt with { InstallationId = V16Identity, StaticCount = 21195 }, features, reward);
    }

    private void WritePrevious(ModularDungeonDeployment.Manifest previous, ModularDungeonDeployment.Result receipt)
    {
        var directory = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV16");
        File.WriteAllBytes(Path.Combine(directory, "install-manifest.json"), JsonSerializer.SerializeToUtf8Bytes(previous, _json));
        File.WriteAllBytes(Path.Combine(directory, "installed.json"), JsonSerializer.SerializeToUtf8Bytes(receipt, _json));
        typeof(Core).GetField("_baseDirectory", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _fixture.FixtureRoot);
        _ossuaryField.SetValue(null, true);
        _fullLevelsField.SetValue(null, true);
    }

    private void AddRestoredGroundSupport()
    {
        // The test host reads the V16 facet; production requires V17 native fingerprint verification first.
        for (var x = 5316; x <= 5317; x++)
        {
            for (var y = 814; y <= 818; y++)
            {
                _fixture.Track(new Item(0x51C) { Movable = false }).MoveToWorld(new Point3D(x, y, 0), Map.Felucca);
            }
        }
    }

    private static void PopulateRetainedLists(object job, ModularDungeonDeployment.Result receipt)
    {
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
    }

    private static void AssertChests(ModularDungeonDeployment.Manifest expected, ModularDungeonDeployment.Result receipt)
    {
        foreach (var serial in receipt.Chests)
        {
            var chest = World.FindItem((Serial)serial.Serial);
            var definition = Array.Find(expected.Chests, candidate => candidate.Id == serial.Id);
            Assert.NotNull(chest);
            Assert.False(chest.Deleted);
            Assert.Equal(Map.Felucca, chest.Map);
            Assert.Equal(definition.Position.Point, chest.Location);
            Assert.Equal(definition.ItemId, chest.ItemID);
        }
    }

    private static ModularDungeonDeployment.Result ReadResult(string directory, string name) =>
        JsonSerializer.Deserialize<ModularDungeonDeployment.Result>(File.ReadAllBytes(Path.Combine(directory, name)), _json);

    private static ModularDungeonDeployment.Manifest PreviousV16()
    {
        var path = FindArtifact("install-v16-20261010-r1");
        Skip.If(path == null, "The exact frozen V16 stage is required.");
        return JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(File.ReadAllBytes(path), _json);
    }

    private static ModularDungeonDeployment.Manifest OssuaryManifest(ModularDungeonDeployment.Manifest previous)
    {
        var path = FindArtifact("install-v17-20261010-r1");
        Skip.If(path == null, "The exact frozen V17 stage is required.");
        return JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(File.ReadAllBytes(path), _json);
    }

    private static ModularDungeonDeployment.Manifest PinFixture(ModularDungeonDeployment.Manifest previous, ModularDungeonDeployment.Result receipt) =>
        OssuaryManifest(previous) with { PreviousManifestSha256 = Hash(previous), PreviousReceiptSha256 = Hash(receipt) };

    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, _json)));
}
