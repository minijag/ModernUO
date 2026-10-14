using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Server.Engines.ModularDungeon;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ModularDungeonDeploymentTests
{
    [SkippableFact]
    public void ExactStagedManifest_DeserializesAndPassesRuntimeValidation()
    {
        var path = FindStagedManifest();
        Skip.If(path == null, "Generate the modular dungeon deployment stage before testing its exact manifest.");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(72086, bytes.Length);
        Assert.Equal("eb98913bb3a95419dcd05569df434b54dfa8e5be7f6859d7c7b05e9a5a173ea8",
            Convert.ToHexStringLower(SHA256.HashData(bytes)));

        var optionsField = typeof(ModularDungeonDeployment).GetField("_jsonOptions", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(optionsField);
        var options = Assert.IsType<JsonSerializerOptions>(optionsField.GetValue(null));
        var manifest = JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(bytes, options);
        Assert.NotNull(manifest);
        Assert.Equal(71, manifest.Doors.Length);
        Assert.Equal(25, manifest.Modules.Length);
        Assert.Equal(10, manifest.StairLanes.Length);

        var validateMethod = typeof(ModularDungeonDeployment).GetMethod("ValidateManifest", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(validateMethod);
        var exception = Record.Exception(() => validateMethod.Invoke(null, [manifest]));
        Assert.Null(exception);
    }

    private static string FindStagedManifest()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "artifacts", "modular-dungeon", "install-20261009",
                    "staged-map", "install-manifest.json");
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    [SkippableFact]
    public void ExactV4StagedManifest_DeserializesAndPassesRuntimeValidation()
    {
        var v3Path = FindStagedManifest();
        Skip.If(v3Path == null, "Generate the modular dungeon stage before testing its revision.");
        var artifactRoot = Directory.GetParent(v3Path).Parent.Parent.FullName;
        var path = Path.Combine(artifactRoot, "install-v4-20261009-r2", "staged-map", "install-manifest.json");
        Skip.IfNot(File.Exists(path), "Generate the V4 revision stage before testing its exact manifest.");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(104219, bytes.Length);
        Assert.Equal("70f3be6b5f3b0f586a1cece0a873b53e5827aea89417a786a1aa5c438b25729a",
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var optionsField = typeof(ModularDungeonDeployment).GetField("_jsonOptions", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(optionsField);
        var manifest = JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(bytes,
            Assert.IsType<JsonSerializerOptions>(optionsField.GetValue(null)));
        Assert.NotNull(manifest);
        Assert.Equal(99, manifest.Chests.Length);
        Assert.Equal(16, manifest.StairLanes.Length);
        Assert.Equal(5, manifest.EscapeRoutes.Length);
        Assert.Equal(-40, manifest.InstalledLandZ);
        var method = typeof(ModularDungeonDeployment).GetMethod("ValidateManifest", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Null(Record.Exception(() => method.Invoke(null, [manifest])));
    }

    [SkippableTheory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public void V4Chest_CannotOverlapAnyDoorReservation(bool overlapApproach, int reservation)
    {
        var v3Path = FindStagedManifest();
        Skip.If(v3Path == null, "Generate the modular dungeon stage before testing its revision.");
        var artifactRoot = Directory.GetParent(v3Path).Parent.Parent.FullName;
        var path = Path.Combine(artifactRoot, "install-v4-20261009-r2", "staged-map", "install-manifest.json");
        Skip.IfNot(File.Exists(path), "Generate the V4 revision stage before testing its exact manifest.");
        var optionsField = typeof(ModularDungeonDeployment).GetField("_jsonOptions", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(optionsField);
        var manifest = JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(File.ReadAllBytes(path),
            Assert.IsType<JsonSerializerOptions>(optionsField.GetValue(null)));
        Assert.NotNull(manifest);
        var method = typeof(ModularDungeonDeployment).GetMethod("ValidateManifest", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Null(Record.Exception(() => method.Invoke(null, [manifest])));

        var door = manifest.Doors[0];
        var target = reservation switch
        {
            0 => door.Closed,
            1 => door.Opened,
            2 => door.Approaches[0],
            _ => door.Approaches[1]
        };
        var paired = FindAvailableNeighbor(manifest, target);
        var chests = (ModularDungeonDeployment.ChestDefinition[])manifest.Chests.Clone();
        chests[0] = overlapApproach
            ? chests[0] with { Position = paired, Approach = target }
            : chests[0] with { Position = target, Approach = paired };

        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [manifest with { Chests = chests }]));
        var validation = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains(overlapApproach || reservation != 0 ? "reserved doorway" : "overlapping position", validation.Message);
    }

    private static ModularDungeonDeployment.Coordinate FindAvailableNeighbor(
        ModularDungeonDeployment.Manifest manifest, ModularDungeonDeployment.Coordinate point)
    {
        var occupied = new HashSet<ModularDungeonDeployment.Coordinate>();
        foreach (var door in manifest.Doors)
        {
            occupied.Add(door.Closed);
        }

        foreach (var chest in manifest.Chests)
        {
            occupied.Add(chest.Position);
        }

        foreach (var offset in new[] { (X: 1, Y: 0), (X: -1, Y: 0), (X: 0, Y: 1), (X: 0, Y: -1) })
        {
            var candidate = point with { X = point.X + offset.X, Y = point.Y + offset.Y };
            if (manifest.Bounds.Rectangle.Contains(candidate.Point) && !occupied.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The staged test doorway has no free neighboring coordinate.");
    }
}
