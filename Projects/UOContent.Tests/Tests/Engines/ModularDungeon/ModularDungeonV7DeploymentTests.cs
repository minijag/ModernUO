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
public class ModularDungeonV7DeploymentTests : IDisposable
{
    private const string V7Identity = "modular-dungeon-v7-final-felucca-5256-808";
    private static readonly Type _deployment = typeof(ModularDungeonDeployment);
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly ModularDungeonV6DeploymentTests _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [SkippableFact]
    public void FrozenChapelStage_MatchesInstalledV6PinsAndRetainedRuntimeDefinitions()
    {
        var path = FindArtifact("install-v7-20261010-r1");
        Skip.If(path == null, "The final chapel native stage is required.");
        var next = JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(File.ReadAllBytes(path), _json);
        var previousPath = FindArtifact("install-v6-20261009-r2");
        Skip.If(previousPath == null, "The exact previous V6 native stage is required.");
        var previousBytes = File.ReadAllBytes(previousPath);
        var previous = JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(previousBytes, _json);
        Assert.Null(Record.Exception(() => InvokeStatic("ValidateManifest", next)));
        Assert.Null(Record.Exception(() => InvokeStatic("ValidateSceneryDefinitions", previous, next)));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(previousBytes)), next.PreviousManifestSha256);
        Assert.Equal("045f7e589fbfce2d11a432a0afe875c2a39307edfcf39352edf1cfee976f3503", next.PreviousManifestSha256);
        Assert.Equal("51cc6b644877a82ae372bae4bd6c62b209e4dbaba221f10dfadd5920efd790ee", next.PreviousReceiptSha256);
        Assert.Equal("fee9e69614d62fa09c4ac7414489a6b2627268c1eabe8a38ef70b4aeaa9ed345", next.OriginalNativeSha256);
        Assert.Equal(16311, next.OriginalStaticCount);
        Assert.Equal("M00", next.SceneryModuleId);
    }

    [SkippableFact]
    public void ChapelManifest_RequiresV6PredecessorAndExactReceiptPins()
    {
        var previous = PreviousV6();
        var (receipt, _, _) = _fixture.RetainedWorld(previous, true);
        var next = ChapelManifest(previous, receipt);
        Assert.Null(Record.Exception(() => InvokeStatic("ValidateManifest", next)));
        Assert.Null(Record.Exception(() => InvokeStatic("ValidateSceneryDefinitions", previous, next)));
        Assert.Equal(16311, next.OriginalStaticCount);
        Assert.True(next.SceneryOnly);
        Assert.Equal("M00", next.SceneryModuleId);
        Assert.Equal(receipt.ManifestSha256, next.PreviousManifestSha256);
        Assert.Equal(Hash(receipt), next.PreviousReceiptSha256);
    }

    [SkippableTheory]
    [InlineData("MissingManifestPin")]
    [InlineData("MissingReceiptPin")]
    [InlineData("MalformedReceiptPin")]
    [InlineData("TreasuryModule")]
    [InlineData("MissingSceneryOnly")]
    [InlineData("V5StaticCount")]
    [InlineData("V5Predecessor")]
    [InlineData("ChangedChest")]
    [InlineData("ChangedDoor")]
    [InlineData("ChangedHatch")]
    [InlineData("ChangedEscape")]
    public void ChapelManifest_RejectsWrongVersionPinsScopeOrRuntimeDefinitions(string corruption)
    {
        var previous = PreviousV6();
        var (receipt, _, _) = _fixture.RetainedWorld(previous, true);
        var next = ChapelManifest(previous, receipt);
        switch (corruption)
        {
            case "MissingManifestPin": next = next with { PreviousManifestSha256 = null }; break;
            case "MissingReceiptPin": next = next with { PreviousReceiptSha256 = null }; break;
            case "MalformedReceiptPin": next = next with { PreviousReceiptSha256 = "not-a-hash" }; break;
            case "TreasuryModule": next = next with { SceneryModuleId = "M24" }; break;
            case "MissingSceneryOnly": next = next with { SceneryOnly = false }; break;
            case "V5StaticCount": next = next with { OriginalStaticCount = 16095 }; break;
            case "V5Predecessor": previous = PreviousManifest(); break;
            case "ChangedChest":
            {
                var definitions = (ModularDungeonDeployment.ChestDefinition[])next.Chests.Clone();
                definitions[0] = definitions[0] with { Position = definitions[0].Position with { X = definitions[0].Position.X + 1 } };
                next = next with { Chests = definitions };
                break;
            }
            case "ChangedDoor":
            {
                var definitions = (ModularDungeonDeployment.DoorDefinition[])next.Doors.Clone();
                definitions[0] = definitions[0] with { ClosedId = definitions[0].ClosedId + 2 };
                next = next with { Doors = definitions };
                break;
            }
            case "ChangedHatch": next = next with { HiddenCellar = next.HiddenCellar with { Entry = next.HiddenCellar.Entry with { X = next.HiddenCellar.Entry.X + 1 } } }; break;
            case "ChangedEscape":
            {
                var definitions = (ModularDungeonDeployment.EscapeRoute[])next.EscapeRoutes.Clone();
                definitions[0] = definitions[0] with { To = definitions[0].To with { Y = definitions[0].To.Y + 1 } };
                next = next with { EscapeRoutes = definitions };
                break;
            }
        }
        Assert.NotNull(Record.Exception(() =>
        {
            InvokeStatic("ValidateManifest", next);
            InvokeStatic("ValidateSceneryDefinitions", previous, next);
        }));
    }

    [SkippableTheory]
    [InlineData("WrongManifestPin")]
    [InlineData("WrongReceiptPin")]
    [InlineData("DifferentReceiptBytes")]
    [InlineData("DifferentManifestBytes")]
    [InlineData("V5Receipt")]
    [InlineData("WrongStaticCount")]
    [InlineData("WrongNative")]
    [InlineData("FailedReceipt")]
    public void ChapelOwnership_RejectsUnpinnedOrUnsuccessfulPreviousInstallation(string corruption)
    {
        var previous = PreviousV6();
        var (receipt, features, reward) = _fixture.RetainedWorld(previous, true);
        var next = ChapelManifest(previous, receipt);
        _fixture.WritePrevious(previous, receipt, true);
        var path = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV6");
        switch (corruption)
        {
            case "WrongManifestPin": next = next with { PreviousManifestSha256 = new string('a', 64) }; break;
            case "WrongReceiptPin": next = next with { PreviousReceiptSha256 = new string('a', 64) }; break;
            case "DifferentReceiptBytes": File.AppendAllText(Path.Combine(path, "installed.json"), "\n"); break;
            case "DifferentManifestBytes": File.AppendAllText(Path.Combine(path, "install-manifest.json"), "\n"); break;
            case "V5Receipt": receipt = receipt with { InstallationId = "modular-dungeon-v5-final-felucca-5256-808" }; break;
            case "WrongStaticCount": receipt = receipt with { StaticCount = 16095 }; break;
            case "WrongNative": receipt = receipt with { NativeSha256 = new string('a', 64) }; break;
            case "FailedReceipt": receipt = receipt with { Success = false }; break;
        }
        if (corruption is "V5Receipt" or "WrongStaticCount" or "WrongNative" or "FailedReceipt")
        {
            _fixture.WritePrevious(previous, receipt, true);
            next = next with { PreviousReceiptSha256 = Hash(receipt) };
        }
        Assert.Throws<TargetInvocationException>(() => _fixture.NewJob(next));
        Assert.False(features.Hatch.Deleted);
        Assert.False(features.ReturnRune.Deleted);
        Assert.False(reward.Deleted);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChapelContinuation_RetainsAllSerialsContentsAndConsumedFeatureState(bool missing)
    {
        Skip.If(!TestServerInitializer.TileDataLoaded, "Native retained door validation requires tile data.");
        var previous = PreviousV6();
        var (receipt, features, reward) = _fixture.RetainedWorld(previous, true);
        var door = (EventDungeonDoor)World.FindItem((Serial)receipt.Doors[0].Serial);
        door.Open = true;
        Empty(reward);
        var marker = _fixture.Track(new Gold(13));
        reward.DropItem(marker);
        if (missing)
        {
            features.Cover.Delete();
            features.Lich.Delete();
        }
        else
        {
            _fixture.Track(new Backpack()).DropItem(features.Cover);
            var point = features.Lich.Location;
            features.Lich.MoveToWorld(new Point3D(point.X + 1, point.Y, point.Z), Map.Felucca);
        }
        var parent = features.Cover.Parent;
        var lichLocation = features.Lich.Location;
        _fixture.WritePrevious(previous, receipt, true);
        var job = _fixture.NewJob(ChapelManifest(previous, receipt));
        var audit = (ModularDungeonDeployment.Result)InvokeJob(job, "CreateResult", true, "audit", null, false);
        Assert.Equal(0, audit.DoorCount);
        Assert.Equal(receipt.Features, audit.Features);
        Assert.Equal(receipt.Creatures, audit.Creatures);
        InvokeJob(job, "PreflightAndCreateDoors");
        InvokeJob(job, "CheckDoorCrossings");
        Assert.True(door.Open);
        var frontier = (Queue<Point3D>)JobField("_frontier").GetValue(job);
        var visited = (HashSet<Point3D>)JobField("_visited").GetValue(job);
        var targets = (HashSet<Point3D>)JobField("_targets").GetValue(job);
        visited.Add(previous.Entry.Point);
        frontier.Enqueue(previous.Entry.Point);
        targets.Add(new Point3D(-9999, -9999, 0));
        InvokeJob(job, "CheckReachabilitySlice");
        Assert.True(door.Open);
        var result = (ModularDungeonDeployment.Result)InvokeJob(job, "CreateResult", true, "installed", null, false);
        Assert.Equal(receipt.Doors, result.Doors);
        Assert.Equal(receipt.Chests, result.Chests);
        Assert.Equal(receipt.Features, result.Features);
        Assert.Equal(receipt.Creatures, result.Creatures);
        Assert.Throws<TargetInvocationException>(() => InvokeJob(job, "CreateHiddenCellar"));
        InvokeJob(job, "Fail", new InvalidOperationException("deliberate chapel scenery survey failure"));
        Assert.Same(marker, Assert.Single(reward.Items));
        Assert.Equal(missing, features.Cover.Deleted);
        Assert.Equal(missing, features.Lich.Deleted);
        if (!missing)
        {
            Assert.Same(parent, features.Cover.Parent);
            Assert.Equal(lichLocation, features.Lich.Location);
        }
        Assert.False(features.Hatch.Deleted);
        Assert.False(features.ReturnRune.Deleted);
        Assert.True(door.Open);
        foreach (var serial in receipt.Chests)
        {
            var chest = World.FindItem((Serial)serial.Serial);
            Assert.NotNull(chest);
            Assert.Equal(Array.Find(previous.Chests, c => c.Id == serial.Id).Position.Point, chest.Location);
        }
    }

    [SkippableFact]
    public void ChapelFailureAfterReceiptWrite_InvalidatesOnlyV7ReceiptAndKeepsV6ReceiptExact()
    {
        var previous = PreviousV6();
        var (receipt, features, reward) = _fixture.RetainedWorld(previous, true);
        _fixture.WritePrevious(previous, receipt, true);
        var previousPath = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV6", "installed.json");
        var previousBytes = File.ReadAllBytes(previousPath);
        var job = _fixture.NewJob(ChapelManifest(previous, receipt));
        InvokeJob(job, "PreflightAndCreateDoors");
        JobField("_installedResultWritten").SetValue(job, true);
        var path = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV7", "installed.json");
        var result = (ModularDungeonDeployment.Result)InvokeJob(job, "CreateResult", true, "installed", null, false);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(result, _json));
        InvokeJob(job, "Fail", new InvalidOperationException("deliberate chapel marker retirement failure"));
        Assert.False(JsonSerializer.Deserialize<ModularDungeonDeployment.Result>(File.ReadAllBytes(path), _json).Success);
        Assert.Equal(previousBytes, File.ReadAllBytes(previousPath));
        Assert.False(features.Hatch.Deleted);
        Assert.False(features.ReturnRune.Deleted);
        Assert.False(reward.Deleted);
    }

    [Fact]
    public void NormalStartupWithCompletedV7Receipt_TakesPriorityWithoutStartingAnotherJob()
    {
        typeof(Core).GetField("_baseDirectory", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _fixture.FixtureRoot);
        var path = Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV7");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(_fixture.FixtureRoot, "Data", "ModularDungeonV6", "installed.json"), "{\"success\":true}");
        File.WriteAllText(Path.Combine(path, "installed.json"), "{\"success\":true}");
        File.WriteAllText(Path.Combine(path, "install.completed"), "{\"manifestSha256\":\"already-completed\"}");
        Assert.Null(_deployment.GetField("_job", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        ModularDungeonDeployment.Initialize();
        Assert.True((bool)_deployment.GetField("_chapel", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.Equal(V7Identity, _deployment.GetProperty("CurrentInstallationId", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.Null(_deployment.GetField("_job", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        Assert.False(File.Exists(Path.Combine(path, "install-state.json")));
        Assert.Equal("{\"success\":true}", File.ReadAllText(Path.Combine(path, "installed.json")));
    }

    private static ModularDungeonDeployment.Manifest PreviousV6() => SceneryManifest(PreviousManifest());
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, _json)));
    private static ModularDungeonDeployment.Manifest ChapelManifest(ModularDungeonDeployment.Manifest previous, ModularDungeonDeployment.Result receipt) => previous with
    {
        InstallationId = V7Identity, OriginalStaticCount = 16311, OriginalNativeSha256 = previous.InstalledNativeSha256,
        SceneryOnly = true, SceneryModuleId = "M00", PreviousManifestSha256 = receipt.ManifestSha256, PreviousReceiptSha256 = Hash(receipt)
    };
}
