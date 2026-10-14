using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Server;
using Server.Engines.ModularDungeon;
using Server.Items;
using Server.Mobiles;
using Xunit;
using static UOContent.Tests.Engines.ModularDungeon.ModularDungeonV6DeploymentTests;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ModularDungeonV35DeploymentTests : IDisposable
{
    private const string PreviousIdentity = "modular-dungeon-v17-final-felucca-5256-808";
    private const string Identity = "modular-dungeon-v35-final-felucca-5256-808";
    private static readonly Type _deployment = typeof(ModularDungeonDeployment);
    private static readonly Type _jobType = _deployment.GetNestedType("Job", BindingFlags.NonPublic);
    private static readonly FieldInfo _abundantField = _deployment.GetField("_abundant", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo _ossuaryField = _deployment.GetField("_ossuary", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly bool _previousAbundant = (bool)_abundantField.GetValue(null);
    private readonly bool _previousOssuary = (bool)_ossuaryField.GetValue(null);
    private readonly ModularDungeonV6DeploymentTests _fixture = new();
    private readonly List<object> _auditJobs = new();

    public ModularDungeonV35DeploymentTests()
    {
        _abundantField.SetValue(null, false);
        _ossuaryField.SetValue(null, false);
        Directory.CreateDirectory(Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV17"));
        Directory.CreateDirectory(Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV35"));
    }

    public void Dispose()
    {
        foreach (var job in _auditJobs)
        {
            InvokeJob(job, "Stop");
        }
        _fixture.Dispose();
        _abundantField.SetValue(null, _previousAbundant);
        _ossuaryField.SetValue(null, _previousOssuary);
    }

    [SkippableFact]
    public void ExactFrozenContinuation_PreservesAllRuntimeObjectsAndOnlyAuthoredGeometryChanges()
    {
        var previous = PreviousManifest();
        var next = NextManifest();
        InvokeStatic("ValidateManifest", next);
        InvokeStatic("ValidateAbundantDefinitions", previous, next);
        Assert.Equal(22334, next.StaticCount);
        Assert.Equal(21020, next.OriginalStaticCount);
        Assert.Equal(24, next.StairLanes.Length);
        Assert.Equal(5, next.EscapeRoutes.Length);
        Assert.Equal(100, next.Chests.Length);
        Assert.Equal("0ca6ec10f678a0fa130d3b60dc385ec5f9599d7e4a191374d5f7c18fbb874774", next.BlueprintSha256);
        Assert.Equal("7966d9845810420764daaa4a68b0947e0cc87b7bb236127477fecf0b1a31ab14", next.PreviousManifestSha256);
        Assert.Equal("e7e5cd9470869a010f8f41a7ea30d870697cf08be0449766d4b1b55c60fddfad", next.PreviousReceiptSha256);
        Assert.Equal(JsonSerializer.Serialize(previous.Doors, _json), JsonSerializer.Serialize(next.Doors, _json));
        Assert.Equal(JsonSerializer.Serialize(previous.Chests, _json), JsonSerializer.Serialize(next.Chests, _json));
        Assert.Equal(previous.HiddenCellar, next.HiddenCellar);
        Assert.DoesNotContain(next.EscapeRoutes, route => route.ModuleId == "M00");
        Assert.Equal(4, Array.FindAll(next.StairLanes, lane => lane.ModuleId == "M00").Length);
        for (var i = 0; i < previous.Modules.Length; i++)
        {
            if (previous.Modules[i].Id is not ("M00" or "M05"))
            {
                Assert.Equal(JsonSerializer.Serialize(previous.Modules[i], _json), JsonSerializer.Serialize(next.Modules[i], _json));
            }
        }
    }

    [SkippableTheory]
    [InlineData("WrongBlueprint")]
    [InlineData("WrongPin")]
    [InlineData("MissingPin")]
    [InlineData("SceneryAlias")]
    [InlineData("WrongStaticCount")]
    [InlineData("WrongOriginalNative")]
    [InlineData("WrongInstalledNative")]
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
    [InlineData("ChangedChapelStair")]
    [InlineData("RetainedOldChapelStair")]
    [InlineData("WrongLaneCount")]
    [InlineData("ChangedEscape")]
    [InlineData("RetainedChapelEscape")]
    [InlineData("RemovedOtherEscape")]
    [InlineData("ChangedOtherModule")]
    [InlineData("ChangedLibraryApproach")]
    [InlineData("ChangedChapelStart")]
    [InlineData("ChangedChapelUpperFocus")]
    [InlineData("MissingChapelPOI")]
    [InlineData("ChangedForgePOI")]
    [InlineData("AddedForgePOI")]
    [InlineData("ChangedModuleOrder")]
    public void FrozenContinuation_RejectsUnapprovedGeometryAndRuntimeIdentity(string corruption)
    {
        var previous = PreviousManifest();
        var next = NextManifest();
        var modules = (ModularDungeonDeployment.ModuleDefinition[])next.Modules.Clone();
        var chests = (ModularDungeonDeployment.ChestDefinition[])next.Chests.Clone();
        var lanes = (ModularDungeonDeployment.StairLane[])next.StairLanes.Clone();
        var escapes = (ModularDungeonDeployment.EscapeRoute[])next.EscapeRoutes.Clone();
        switch (corruption)
        {
            case "WrongBlueprint": next = next with { BlueprintSha256 = new string('a', 64) }; break;
            case "WrongPin": next = next with { PreviousReceiptSha256 = new string('a', 64) }; break;
            case "MissingPin": next = next with { PreviousManifestSha256 = null }; break;
            case "SceneryAlias": next = next with { SceneryOnly = true, SceneryModuleId = "M05" }; break;
            case "WrongStaticCount": next = next with { StaticCount = 22333 }; break;
            case "WrongOriginalNative": next = next with { OriginalNativeSha256 = new string('a', 64) }; break;
            case "WrongInstalledNative": next = next with { InstalledNativeSha256 = new string('a', 64) }; break;
            case "WrongPreviousCount": next = next with { OriginalStaticCount = 21195 }; break;
            case "WrongPredecessor": previous = previous with { InstallationId = "modular-dungeon-v16-final-felucca-5256-808" }; break;
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
            case "ChangedChestPosition": chests[0] = chests[0] with { Position = chests[0].Position with { X = chests[0].Position.X + 1 } }; next = next with { Chests = chests }; break;
            case "ChangedChestApproach": chests[0] = chests[0] with { Approach = chests[0].Approach with { X = chests[0].Approach.X + 1 } }; next = next with { Chests = chests }; break;
            case "ChangedChestArt": chests[0] = chests[0] with { ItemId = 0xE43 }; next = next with { Chests = chests }; break;
            case "ChangedChestClass": chests[0] = chests[0] with { Type = "RootboundCellarChest" }; next = next with { Chests = chests }; break;
            case "ChangedRetainedStair":
            case "ChangedChapelStair":
            {
                var index = corruption == "ChangedChapelStair" ? 0 : Array.FindIndex(lanes, lane => lane.ModuleId == "M01");
                var points = (ModularDungeonDeployment.Coordinate[])lanes[index].Points.Clone();
                for (var i = 0; i < points.Length; i++)
                {
                    points[i] = points[i] with { X = points[i].X + 1 };
                }
                lanes[index] = lanes[index] with { Points = points };
                next = next with { StairLanes = lanes };
                break;
            }
            case "RetainedOldChapelStair": lanes[0] = previous.StairLanes[0]; next = next with { StairLanes = lanes }; break;
            case "WrongLaneCount": next = next with { StairLanes = lanes[1..] }; break;
            case "ChangedEscape": escapes[0] = escapes[0] with { From = escapes[0].From with { X = escapes[0].From.X + 1 }, To = escapes[0].To with { X = escapes[0].To.X + 1 } }; next = next with { EscapeRoutes = escapes }; break;
            case "RetainedChapelEscape": escapes[0] = previous.EscapeRoutes[0]; next = next with { EscapeRoutes = escapes }; break;
            case "RemovedOtherEscape": next = next with { EscapeRoutes = escapes[1..] }; break;
            case "ChangedOtherModule": modules[2] = modules[2] with { Name = "Unapproved room" }; next = next with { Modules = modules }; break;
            case "ChangedLibraryApproach":
            case "ChangedChapelUpperFocus":
            case "ChangedForgePOI":
            {
                var index = corruption == "ChangedLibraryApproach" ? 1 : corruption == "ChangedForgePOI" ? 5 : 0;
                var approaches = (ModularDungeonDeployment.Approach[])modules[index].Approaches.Clone();
                approaches[1] = approaches[1] with { Point = approaches[1].Point with { X = approaches[1].Point.X + 1 } };
                modules[index] = modules[index] with { Approaches = approaches };
                next = next with { Modules = modules };
                break;
            }
            case "ChangedChapelStart": modules[0] = modules[0] with { Start = new(5269, 815, 0) }; next = next with { Modules = modules }; break;
            case "MissingChapelPOI": modules[0] = modules[0] with { Approaches = modules[0].Approaches[1..] }; next = next with { Modules = modules }; break;
            case "AddedForgePOI":
            {
                var approaches = new List<ModularDungeonDeployment.Approach>(modules[5].Approaches) { new("unapproved", new(5273, 844, 0)) };
                modules[5] = modules[5] with { Approaches = approaches.ToArray() };
                next = next with { Modules = modules };
                break;
            }
            case "ChangedModuleOrder": (modules[2], modules[3]) = (modules[3], modules[2]); next = next with { Modules = modules }; break;
        }
        Assert.NotNull(Record.Exception(() =>
        {
            InvokeStatic("ValidateManifest", next);
            InvokeStatic("ValidateAbundantDefinitions", previous, next);
        }));
    }

    [SkippableTheory]
    [InlineData("WrongManifestPin")]
    [InlineData("WrongReceiptPin")]
    [InlineData("DifferentReceiptBytes")]
    [InlineData("DifferentManifestBytes")]
    [InlineData("FailedReceipt")]
    [InlineData("WrongReceiptIdentity")]
    [InlineData("MissingChest")]
    [InlineData("MovedChest")]
    [InlineData("DuplicateChestSerial")]
    [InlineData("DuplicateDoorSerial")]
    public void Ownership_RefusesUnpinnedOrChangedRetainedObjects(string corruption)
    {
        var previous = PreviousManifest();
        var (receipt, _, reward) = Retained(previous);
        switch (corruption)
        {
            case "FailedReceipt": receipt = receipt with { Success = false }; break;
            case "WrongReceiptIdentity": receipt = receipt with { InstallationId = "modular-dungeon-v16-final-felucca-5256-808" }; break;
            case "MissingChest": World.FindItem((Serial)receipt.Chests[0].Serial).Delete(); break;
            case "MovedChest": World.FindItem((Serial)receipt.Chests[0].Serial).MoveToWorld(new Point3D(5269, 815, 0), Map.Felucca); break;
            case "DuplicateChestSerial":
            {
                var chests = (ModularDungeonDeployment.DoorSerial[])receipt.Chests.Clone();
                chests[0] = chests[0] with { Serial = chests[1].Serial };
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
        var directory = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV17");
        switch (corruption)
        {
            case "WrongManifestPin": next = next with { PreviousManifestSha256 = new string('a', 64) }; break;
            case "WrongReceiptPin": next = next with { PreviousReceiptSha256 = new string('a', 64) }; break;
            case "DifferentReceiptBytes": File.AppendAllText(Path.Combine(directory, "installed.json"), "\n"); break;
            case "DifferentManifestBytes": File.AppendAllText(Path.Combine(directory, "install-manifest.json"), "\n"); break;
        }
        Assert.Throws<TargetInvocationException>(() => _fixture.NewJob(next));
        Assert.False(reward.Deleted);
    }

    [SkippableFact]
    public void OriginalMapAudit_CannotPreflightRelocateOrConstructObjects()
    {
        var previous = PreviousManifest();
        var (receipt, _, _) = Retained(previous);
        WritePrevious(previous, receipt);
        var job = Activator.CreateInstance(_jobType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [PinFixture(previous, receipt), new string('d', 64), true], null);
        _auditJobs.Add(job);
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "PreflightAndCreateDoors"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "RelocateOssuaryChest"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "InternalizeCompletedChests"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "PlaceCompletedChests"));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "CreateHiddenCellar"));
        AssertChests(previous, receipt);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoRelocationAndFailure_PreserveContentsDoorStatesAndConsumedCellar(bool consumed)
    {
        var previous = PreviousManifest();
        var (receipt, features, _) = Retained(previous);
        var door = (EventDungeonDoor)World.FindItem((Serial)receipt.Doors[0].Serial);
        door.Open = true;
        var loot = new Dictionary<uint, Gold>();
        foreach (var serial in receipt.Chests)
        {
            var chest = (Container)World.FindItem((Serial)serial.Serial);
            Empty(chest);
            var gold = _fixture.Track(new Gold(137));
            chest.DropItem(gold);
            loot.Add(serial.Serial, gold);
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
        PopulateRetainedLists(job, receipt);
        InvokeJob(job, "ValidateAllDoors");
        var result = (ModularDungeonDeployment.Result)InvokeJob(job, "CreateResult", true, "installed", null, false);
        Assert.Equal(receipt.Doors, result.Doors);
        Assert.Equal(receipt.Chests, result.Chests);
        Assert.Equal(receipt.Features, result.Features);
        Assert.Equal(receipt.Creatures, result.Creatures);
        InvokeJob(job, "Fail", new InvalidOperationException("deliberate retained survey failure"));
        AssertChests(previous, receipt);
        foreach (var serial in receipt.Chests)
        {
            Assert.Same(loot[serial.Serial], Assert.Single(((Container)World.FindItem((Serial)serial.Serial)).Items));
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
    [InlineData("Player")]
    [InlineData("Pet")]
    [InlineData("Item")]
    public void StrictOccupancy_RefusesPlayersPetsAndUnrelatedItems(string actor)
    {
        var previous = PreviousManifest();
        var (receipt, _, _) = Retained(previous);
        WritePrevious(previous, receipt);
        var job = _fixture.NewJob(PinFixture(previous, receipt));
        InvokeJob(job, "ValidateOccupancy", previous.Bounds.Rectangle, false);
        if (actor == "Item")
        {
            _fixture.Track(new Item(1)).MoveToWorld(new Point3D(5268, 815, 0), Map.Felucca);
        }
        else
        {
            _fixture.Track(new Mobile { Player = actor == "Player" }).MoveToWorld(new Point3D(5268, 815, 0), Map.Felucca);
        }
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "ValidateOccupancy", previous.Bounds.Rectangle, false));
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "ValidateRetainedDoorFits"));
        AssertChests(previous, receipt);
    }

    [SkippableFact]
    public void DeferredControllerRestore_FirstAuditCapturesOwnershipAndOwnedMonsterDoesNotBlockNativeDoorFit()
    {
        var previous = PreviousManifest();
        var (receipt, features, _) = Retained(previous);
        WritePrevious(previous, receipt);
        Assert.Null(ExtractionEventRuntime.Controller);
        var job = _fixture.NewJob(PinFixture(previous, receipt));
        Assert.Null(JobField("_extractionOwnership").GetValue(job));
        Assert.False((bool)JobField("_extractionOwnershipCaptured").GetValue(job));
        var runtime = typeof(ExtractionEventRuntime);
        var suspended = runtime.GetField("_deploymentSuspended", BindingFlags.Static | BindingFlags.NonPublic);
        var wasSuspended = (bool)suspended.GetValue(null);
        try
        {
            var controller = _fixture.Track(new ExtractionEventController());
            var installation = _fixture.Track(new ExtractionEventInstallation());
            var doors = new EventDungeonDoor[receipt.Doors.Length];
            var chests = new EventDungeonChest[receipt.Chests.Length];
            for (var i = 0; i < doors.Length; i++)
            {
                doors[i] = (EventDungeonDoor)World.FindItem((Serial)receipt.Doors[i].Serial);
            }
            for (var i = 0; i < chests.Length; i++)
            {
                chests[i] = (EventDungeonChest)World.FindItem((Serial)receipt.Chests[i].Serial);
            }
            installation.Initialize(controller, doors, chests, features);
            installation.MarkLobbyComplete();
            controller.AddManagedItem(installation);
            var monster = _fixture.Track(new Skeleton());
            controller.AddOwnedCreature(monster);
            monster.MoveToWorld(previous.Doors[0].Closed.Point, Map.Felucca);
            suspended.SetValue(null, true);
            runtime.GetMethod("Bind", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, [controller, installation, previous]);
            Assert.Same(controller, ExtractionEventRuntime.Controller);
            InvokeJob(job, "AuditNativeSlice");
            var ownership = (ExtractionEventRuntime.ExtractionDeploymentOwnership)JobField("_extractionOwnership").GetValue(job);
            Assert.NotNull(ownership);
            Assert.True(ownership.OwnsDeploymentEntity(monster));
            Assert.True((bool)JobField("_extractionOwnershipCaptured").GetValue(job));
            Assert.InRange((int)JobField("_cellCursor").GetValue(job), 1, 128);
            InvokeJob(job, "ValidateRetainedDoorFits");
            var outsider = _fixture.Track(new Skeleton());
            outsider.MoveToWorld(previous.Doors[0].Closed.Point, Map.Felucca);
            Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "ValidateRetainedDoorFits"));
            Assert.False(monster.Deleted);
            AssertChests(previous, receipt);
        }
        finally
        {
            runtime.GetMethod("Shutdown", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            runtime.GetProperty(nameof(ExtractionEventRuntime.Controller)).SetValue(null, null);
            suspended.SetValue(null, wasSuspended);
        }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavePublication_RequiresConfirmedSnapshotBeforeMarkerRetirement(bool rollback)
    {
        var previous = PreviousManifest();
        var (receipt, _, _) = Retained(previous);
        WritePrevious(previous, receipt);
        var job = _fixture.NewJob(PinFixture(previous, receipt));
        PopulateRetainedLists(job, receipt);
        var directory = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV35");
        File.WriteAllBytes(Path.Combine(directory, "install.pending"),
            JsonSerializer.SerializeToUtf8Bytes(new ModularDungeonDeployment.Marker(new string('d', 64)), _json));
        if (rollback)
        {
            InvokeJob(job, "Fail", new InvalidOperationException("deliberate publication failure"));
        }
        var savePath = World.SavePath;
        try
        {
            var fixtureSavePath = Path.Combine(_fixture.FixtureRoot, "Saves");
            Directory.CreateDirectory(fixtureSavePath);
            World.SetSavePathForTest(fixtureSavePath);
            var phase = rollback ? "AwaitRollbackSave" : "AwaitSave";
            JobField("_phase").SetValue(job, Enum.Parse(JobField("_phase").FieldType, phase));
            JobField("_saveDirectoryTimestamp").SetValue(job, DateTime.MinValue);
            JobField("_saveDeadline").SetValue(job, Core.TickCount + 120000);
            InvokeJob(job, "CheckSave", rollback);
            Assert.Equal(phase, JobField("_phase").GetValue(job).ToString());
            Assert.False(File.Exists(Path.Combine(directory, "installed.json")));
            Assert.True(File.Exists(Path.Combine(directory, "install.pending")));
            InvokeJob(job, "OnSuccessfulSnapshot");
            JobField("_saveDirectoryTimestamp").SetValue(job, DateTime.MaxValue);
            InvokeJob(job, "CheckSave", rollback);
            Assert.Equal(phase, JobField("_phase").GetValue(job).ToString());
            JobField("_saveDirectoryTimestamp").SetValue(job, DateTime.MinValue);
            InvokeJob(job, "CheckSave", rollback);
            var name = rollback ? "install-state.json" : "installed.json";
            var result = JsonSerializer.Deserialize<ModularDungeonDeployment.Result>(File.ReadAllBytes(Path.Combine(directory, name)), _json);
            Assert.Equal(!rollback, result.Success);
            Assert.Equal(rollback ? "rolled-back" : "installed", result.Stage);
            Assert.Equal(rollback, result.RollbackSaveConfirmed);
            Assert.Equal(receipt.Chests, result.Chests);
            Assert.Equal(rollback, File.Exists(Path.Combine(directory, "install.pending")));
            Assert.Equal(!rollback, File.Exists(Path.Combine(directory, "install.completed")));
            AssertChests(previous, receipt);
        }
        finally
        {
            World.SetSavePathForTest(savePath);
        }
    }

    [Fact]
    public void CompletedV35_TakesStartupPriorityAndOrdinaryRestartDoesNotRedeploy()
    {
        typeof(Core).GetField("_baseDirectory", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _fixture.FixtureRoot);
        File.WriteAllText(Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV17", "installed.json"), "{\"success\":true}");
        var directory = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV35");
        File.WriteAllText(Path.Combine(directory, "installed.json"), "{\"success\":true}");
        File.WriteAllText(Path.Combine(directory, "install.completed"), "{\"manifestSha256\":\"already-completed\"}");
        ModularDungeonDeployment.Initialize();
        Assert.True((bool)_abundantField.GetValue(null));
        Assert.Equal(Identity, _deployment.GetProperty("CurrentInstallationId", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.Equal(directory, _deployment.GetProperty("DirectoryPath", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.Null(_deployment.GetField("_job", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.False(File.Exists(Path.Combine(directory, "install-state.json")));
        Assert.Equal("{\"success\":true}", File.ReadAllText(Path.Combine(directory, "installed.json")));
    }

    private (ModularDungeonDeployment.Result Receipt, RootboundCellarFeature.Features Features, RootboundCellarChest Reward) Retained(
        ModularDungeonDeployment.Manifest previous)
    {
        var (receipt, features, reward) = _fixture.RetainedWorld(previous, true);
        return (receipt with { InstallationId = PreviousIdentity, StaticCount = 21020 }, features, reward);
    }

    private void WritePrevious(ModularDungeonDeployment.Manifest previous, ModularDungeonDeployment.Result receipt)
    {
        var directory = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV17");
        File.WriteAllBytes(Path.Combine(directory, "install-manifest.json"), JsonSerializer.SerializeToUtf8Bytes(previous, _json));
        File.WriteAllBytes(Path.Combine(directory, "installed.json"), JsonSerializer.SerializeToUtf8Bytes(receipt, _json));
        typeof(Core).GetField("_baseDirectory", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _fixture.FixtureRoot);
        _abundantField.SetValue(null, true);
        _ossuaryField.SetValue(null, true);
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

    private static ModularDungeonDeployment.Manifest PreviousManifest()
    {
        var path = FindArtifact("install-v17-20261010-r1");
        Skip.If(path == null, "The exact frozen V17 deployment stage is required.");
        return JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(File.ReadAllBytes(path), _json);
    }

    private static ModularDungeonDeployment.Manifest NextManifest()
    {
        var path = FindArtifact("install-v35-20261010-r1");
        Skip.If(path == null, "The exact frozen V35 deployment stage is required.");
        return JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(File.ReadAllBytes(path), _json);
    }

    private static ModularDungeonDeployment.Manifest PinFixture(ModularDungeonDeployment.Manifest previous, ModularDungeonDeployment.Result receipt) =>
        NextManifest() with { PreviousManifestSha256 = Hash(previous), PreviousReceiptSha256 = Hash(receipt) };

    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, _json)));
}
