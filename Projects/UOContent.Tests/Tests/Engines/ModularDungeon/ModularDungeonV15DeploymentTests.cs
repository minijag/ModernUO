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
public class ModularDungeonV15DeploymentTests : IDisposable
{
    private const string V15Identity = "modular-dungeon-v15-final-felucca-5256-808";
    private static readonly Type _deployment = typeof(ModularDungeonDeployment);
    private static readonly Type _jobType = _deployment.GetNestedType("Job", BindingFlags.NonPublic);
    private static readonly FieldInfo _completedField = _deployment.GetField("_completed", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly bool _previousCompleted = (bool)_completedField.GetValue(null);
    private readonly ModularDungeonV6DeploymentTests _fixture = new();
    private readonly List<object> _auditJobs = new();

    public ModularDungeonV15DeploymentTests()
    {
        _completedField.SetValue(null, false);
        Directory.CreateDirectory(Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV15"));
    }

    public void Dispose()
    {
        foreach (var job in _auditJobs)
        {
            InvokeJob(job, "Stop");
        }
        _fixture.Dispose();
        _completedField.SetValue(null, _previousCompleted);
    }

    [SkippableFact]
    public void FrozenFullStage_HasExactV6PinsAndBoundedRuntimeDelta()
    {
        var previousPath = FindArtifact("install-v6-20261009-r2");
        Skip.If(previousPath == null, "The frozen installed V6 stage is required.");
        var previousBytes = File.ReadAllBytes(previousPath);
        var previous = JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(previousBytes, _json);
        var next = CompletedManifest();
        Assert.Null(Record.Exception(() => InvokeStatic("ValidateManifest", next)));
        Assert.Null(Record.Exception(() => InvokeStatic("ValidateCompletedDefinitions", previous, next)));
        Assert.False(next.SceneryOnly);
        Assert.Null(next.SceneryModuleId);
        Assert.Equal(17518, next.StaticCount);
        Assert.Equal(16311, next.OriginalStaticCount);
        Assert.Equal(HashBytes(previousBytes), next.PreviousManifestSha256);
        Assert.Equal("045f7e589fbfce2d11a432a0afe875c2a39307edfcf39352edf1cfee976f3503", next.PreviousManifestSha256);
        Assert.Equal("51cc6b644877a82ae372bae4bd6c62b209e4dbaba221f10dfadd5920efd790ee", next.PreviousReceiptSha256);
        Assert.Equal("fee9e69614d62fa09c4ac7414489a6b2627268c1eabe8a38ef70b4aeaa9ed345", next.OriginalNativeSha256);
        Assert.Equal("2177ad75b24727f1fde0a53123d202d00dfb67c8cb8b0fa7d2c56abfa763e639", next.InstalledNativeSha256);
    }

    [SkippableTheory]
    [InlineData("MissingPin")]
    [InlineData("SceneryAlias")]
    [InlineData("WrongBlueprint")]
    [InlineData("WrongStaticCount")]
    [InlineData("WrongPreviousCount")]
    [InlineData("WrongPredecessor")]
    [InlineData("WrongPriorBlueprint")]
    [InlineData("ChangedDoor")]
    [InlineData("ChangedCellar")]
    [InlineData("ChangedOtherChest")]
    [InlineData("WrongM22Position")]
    [InlineData("WrongM22Approach")]
    [InlineData("ChangedChestArt")]
    [InlineData("ChangedChestClass")]
    [InlineData("WrongM22Stair")]
    [InlineData("WrongOtherStair")]
    [InlineData("WrongM22Ledge")]
    [InlineData("WrongOtherLedge")]
    public void FullManifest_RejectsUnapprovedGeometryIdentityOrPins(string corruption)
    {
        var previous = PreviousV6();
        var next = CompletedManifest();
        switch (corruption)
        {
            case "MissingPin": next = next with { PreviousReceiptSha256 = null }; break;
            case "SceneryAlias": next = next with { SceneryOnly = true, SceneryModuleId = "M00" }; break;
            case "WrongBlueprint": next = next with { BlueprintSha256 = new string('a', 64) }; break;
            case "WrongStaticCount": next = next with { StaticCount = 17517 }; break;
            case "WrongPreviousCount": next = next with { OriginalStaticCount = 16095 }; break;
            case "WrongPredecessor": previous = previous with { InstallationId = "modular-dungeon-v5-final-felucca-5256-808" }; break;
            case "WrongPriorBlueprint": previous = previous with { BlueprintSha256 = new string('a', 64) }; break;
            case "ChangedDoor":
            {
                var doors = (ModularDungeonDeployment.DoorDefinition[])next.Doors.Clone();
                doors[0] = doors[0] with { ClosedId = doors[0].ClosedId + 2 };
                next = next with { Doors = doors };
                break;
            }
            case "ChangedCellar": next = next with { HiddenCellar = next.HiddenCellar with { Entry = next.HiddenCellar.Entry with { X = next.HiddenCellar.Entry.X + 1 } } }; break;
            case "ChangedOtherChest":
            case "WrongM22Position":
            case "WrongM22Approach":
            case "ChangedChestArt":
            case "ChangedChestClass":
            {
                var chests = (ModularDungeonDeployment.ChestDefinition[])next.Chests.Clone();
                var index = corruption == "ChangedOtherChest" ? 0 : Array.FindIndex(chests, c => c.Id == "M22-chest-1");
                var chest = chests[index];
                chests[index] = corruption switch
                {
                    "WrongM22Approach" => chest with { Approach = chest.Approach with { X = chest.Approach.X + 1 } },
                    "ChangedChestArt" => chest with { ItemId = 0xE43 },
                    "ChangedChestClass" => chest with { Type = "RootboundCellarChest" },
                    _ => chest with { Position = chest.Position with { X = chest.Position.X + 1 } }
                };
                next = next with { Chests = chests };
                break;
            }
            case "WrongM22Stair":
            case "WrongOtherStair":
            {
                var lanes = (ModularDungeonDeployment.StairLane[])next.StairLanes.Clone();
                var index = Array.FindIndex(lanes, lane => corruption == "WrongM22Stair" ? lane.ModuleId == "M22" : lane.ModuleId != "M22");
                var points = (ModularDungeonDeployment.Coordinate[])lanes[index].Points.Clone();
                for (var i = 0; i < points.Length; i++)
                {
                    points[i] = points[i] with { X = points[i].X + 1 };
                }
                lanes[index] = lanes[index] with { Points = points };
                next = next with { StairLanes = lanes };
                break;
            }
            case "WrongM22Ledge":
            case "WrongOtherLedge":
            {
                var routes = (ModularDungeonDeployment.EscapeRoute[])next.EscapeRoutes.Clone();
                var index = Array.FindIndex(routes, r => corruption == "WrongM22Ledge" ? r.ModuleId == "M22" : r.ModuleId != "M22");
                routes[index] = routes[index] with
                {
                    From = routes[index].From with { X = routes[index].From.X + 1 },
                    To = routes[index].To with { X = routes[index].To.X + 1 }
                };
                next = next with { EscapeRoutes = routes };
                break;
            }
        }
        Assert.NotNull(Record.Exception(() =>
        {
            InvokeStatic("ValidateManifest", next);
            InvokeStatic("ValidateCompletedDefinitions", previous, next);
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
    public void FullOwnership_RejectsUnpinnedOrChangedRecordedState(string corruption)
    {
        var previous = PreviousV6();
        var (receipt, features, reward) = _fixture.RetainedWorld(previous, true);
        var next = PinFixture(previous, receipt);
        switch (corruption)
        {
            case "FailedReceipt": receipt = receipt with { Success = false }; break;
            case "WrongReceiptVersion": receipt = receipt with { InstallationId = "modular-dungeon-v5-final-felucca-5256-808" }; break;
            case "WrongNative": receipt = receipt with { NativeSha256 = new string('a', 64) }; break;
            case "WrongChestClass":
            {
                var chests = (ModularDungeonDeployment.DoorSerial[])receipt.Chests.Clone();
                chests[0] = chests[0] with { Serial = (uint)_fixture.Track(new Item(1)).Serial };
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
        var path = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV6");
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
        var previous = PreviousV6();
        var (receipt, features, _) = _fixture.RetainedWorld(previous, true);
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
    public void FullRelocationAndRollback_KeepSerialsContentsAndConsumedCellarState(bool consumed)
    {
        var previous = PreviousV6();
        var (receipt, features, reward) = _fixture.RetainedWorld(previous, true);
        var door = (EventDungeonDoor)World.FindItem((Serial)receipt.Doors[0].Serial);
        door.Open = true;
        Empty(reward);
        var marker = _fixture.Track(new Gold(13));
        reward.DropItem(marker);
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
        var doors = (List<EventDungeonDoor>)JobField("_doors").GetValue(job);
        var chests = (List<EventDungeonChest>)JobField("_chests").GetValue(job);
        foreach (var serial in receipt.Doors)
        {
            doors.Add((EventDungeonDoor)World.FindItem((Serial)serial.Serial));
        }
        foreach (var serial in receipt.Chests)
        {
            var chest = (EventDungeonChest)World.FindItem((Serial)serial.Serial);
            chests.Add(chest);
            if (serial.Id.StartsWith("M22-chest-", StringComparison.Ordinal))
            {
                chest.DropItem(_fixture.Track(new Gold(17)));
            }
        }
        InvokeJob(job, "InternalizeCompletedChests");
        foreach (var serial in receipt.Chests)
        {
            var chest = World.FindItem((Serial)serial.Serial);
            Assert.Equal(serial.Id.StartsWith("M22-chest-", StringComparison.Ordinal) ? Map.Internal : Map.Felucca, chest.Map);
        }
        InvokeJob(job, "PlaceCompletedChests");
        InvokeJob(job, "ValidateAllDoors");
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "CreateHiddenCellar"));
        var result = (ModularDungeonDeployment.Result)InvokeJob(job, "CreateResult", true, "installed", null, false);
        Assert.Equal(receipt.Doors, result.Doors);
        Assert.Equal(receipt.Chests, result.Chests);
        Assert.Equal(receipt.Features, result.Features);
        Assert.Equal(receipt.Creatures, result.Creatures);
        InvokeJob(job, "Fail", new InvalidOperationException("deliberate full-revision survey failure"));
        AssertOriginalChests(previous, receipt);
        foreach (var serial in receipt.Chests)
        {
            if (serial.Id.StartsWith("M22-chest-", StringComparison.Ordinal))
            {
                Assert.Equal(17, Assert.IsType<Gold>(Assert.Single(((Container)World.FindItem((Serial)serial.Serial)).Items)).Amount);
            }
        }
        Assert.Same(marker, Assert.Single(reward.Items));
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
    public void FailureDuringChestSwap_RestoresOriginalWorldPlacements()
    {
        var previous = PreviousV6();
        var (receipt, features, reward) = _fixture.RetainedWorld(previous, true);
        WritePrevious(previous, receipt);
        var job = _fixture.NewJob(PinFixture(previous, receipt));
        InvokeJob(job, "InternalizeCompletedChests");
        InvokeJob(job, "Fail", new InvalidOperationException("deliberate fit rejection during chest swap"));
        AssertOriginalChests(previous, receipt);
        Assert.False(features.Hatch.Deleted);
        Assert.False(features.ReturnRune.Deleted);
        Assert.False(features.Lich.Deleted);
        Assert.False(reward.Deleted);
    }

    [SkippableFact]
    public void FailureAfterReceiptWrite_RestoresObjectsAndRequiresConfirmedRollbackSnapshot()
    {
        var previous = PreviousV6();
        var (receipt, features, reward) = _fixture.RetainedWorld(previous, true);
        WritePrevious(previous, receipt);
        var previousPath = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV6", "installed.json");
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
        InvokeJob(job, "InternalizeCompletedChests");
        InvokeJob(job, "PlaceCompletedChests");
        JobField("_installedResultWritten").SetValue(job, true);
        var directory = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV15");
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
            var pending = JsonSerializer.Deserialize<ModularDungeonDeployment.Result>(File.ReadAllBytes(Path.Combine(directory, "install-state.json")), _json);
            Assert.False(pending.RollbackSaveConfirmed);
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
    [InlineData(false)]
    [InlineData(true)]
    public void FullOwnership_RefusesUnrelatedRuntimeActors(bool mobile)
    {
        var previous = PreviousV6();
        var (receipt, _, _) = _fixture.RetainedWorld(previous, true);
        WritePrevious(previous, receipt);
        var job = _fixture.NewJob(PinFixture(previous, receipt));
        Assert.Null(Record.Exception(() => InvokeJob(job, "ValidateOccupancy", previous.Bounds.Rectangle, false)));
        if (mobile)
        {
            _fixture.Track(new Mobile()).MoveToWorld(previous.Entry.Point, Map.Felucca);
        }
        else
        {
            _fixture.Track(new Item(1)).MoveToWorld(previous.Entry.Point, Map.Felucca);
        }
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "ValidateOccupancy", previous.Bounds.Rectangle, false));
    }

    [SkippableFact]
    public void RetainedChestFit_ExcludesOnlyItsOwnContainerAndRejectsOtherObstructions()
    {
        Skip.If(!TestServerInitializer.TileDataLoaded, "Native support checks require tile data.");
        var previous = PreviousV6();
        var (receipt, _, _) = _fixture.RetainedWorld(previous, true);
        WritePrevious(previous, receipt);
        var job = _fixture.NewJob(PinFixture(previous, receipt));
        var chest = (EventDungeonChest)World.FindItem((Serial)receipt.Chests[0].Serial);
        Assert.True((bool)InvokeJob(job, "FitsRetainedContainerEndpoint", chest.Location, chest));
        var nativeTile = TileData.ItemTable[1];
        try
        {
            TileData.ItemTable[1] = new ItemData("fit test obstruction", TileFlag.Impassable, 0, 0, 0, 0, 0, 20);
            _fixture.Track(new Item(1) { Movable = false }).MoveToWorld(chest.Location, Map.Felucca);
            Assert.False((bool)InvokeJob(job, "FitsRetainedContainerEndpoint", chest.Location, chest));
        }
        finally
        {
            TileData.ItemTable[1] = nativeTile;
        }
    }

    [Fact]
    public void CompletedV15Receipt_TakesStartupPriorityWithoutRebuilding()
    {
        typeof(Core).GetField("_baseDirectory", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _fixture.FixtureRoot);
        var directory = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV15");
        File.WriteAllText(Path.Combine(directory, "installed.json"), "{\"success\":true}");
        File.WriteAllText(Path.Combine(directory, "install.completed"), "{\"manifestSha256\":\"already-completed\"}");
        ModularDungeonDeployment.Initialize();
        Assert.True((bool)_completedField.GetValue(null));
        Assert.Equal(V15Identity, _deployment.GetProperty("CurrentInstallationId", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.Equal(directory, _deployment.GetProperty("DirectoryPath", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.Null(_deployment.GetField("_job", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.False(File.Exists(Path.Combine(directory, "install-state.json")));
        Assert.Equal("{\"success\":true}", File.ReadAllText(Path.Combine(directory, "installed.json")));
    }

    private void WritePrevious(ModularDungeonDeployment.Manifest previous, ModularDungeonDeployment.Result receipt)
    {
        _fixture.WritePrevious(previous, receipt, true);
        _completedField.SetValue(null, true);
    }

    private static void AssertOriginalChests(ModularDungeonDeployment.Manifest previous, ModularDungeonDeployment.Result receipt)
    {
        foreach (var serial in receipt.Chests)
        {
            var chest = World.FindItem((Serial)serial.Serial);
            var definition = Array.Find(previous.Chests, c => c.Id == serial.Id);
            Assert.NotNull(chest);
            Assert.False(chest.Deleted);
            Assert.Equal(Map.Felucca, chest.Map);
            Assert.Equal(definition.Position.Point, chest.Location);
            Assert.Equal(definition.ItemId, chest.ItemID);
        }
    }

    private static ModularDungeonDeployment.Manifest PreviousV6() => SceneryManifest(PreviousManifest());
    private static ModularDungeonDeployment.Manifest CompletedManifest()
    {
        var path = FindArtifact("install-v15-20261010-r1");
        Skip.If(path == null, "The frozen completed-redesign stage is required.");
        return JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(File.ReadAllBytes(path), _json);
    }
    private static ModularDungeonDeployment.Manifest PinFixture(ModularDungeonDeployment.Manifest previous, ModularDungeonDeployment.Result receipt) =>
        CompletedManifest() with { PreviousManifestSha256 = Hash(previous), PreviousReceiptSha256 = Hash(receipt) };
    private static string Hash<T>(T value) => HashBytes(JsonSerializer.SerializeToUtf8Bytes(value, _json));
    private static string HashBytes(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
