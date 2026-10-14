using System;
using System.Collections.Generic;
using Server;
using Server.Engines.Labrynth;
using Server.Items;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class OrcCaveFootstepsTests : IDisposable
{
    private readonly List<IEntity> _created = [];

    public void Dispose()
    {
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            _created[index].Delete();
        }
    }

    [Fact]
    public void Trail_UsesSevenNorthFacingFootprintsLeadingTowardTheCavePads()
    {
        Assert.Equal(0x1E04, OrcCaveFootsteps.ArtId);
        Assert.Equal("Cave entrance footprints", OrcCaveFootsteps.TrailName);
        Assert.Equal(7, OrcCaveFootsteps.Positions.Length);
        for (var index = 0; index < OrcCaveFootsteps.Positions.Length; index++)
        {
            Assert.Equal(new Point3D(1405, 1469 - index, 1), OrcCaveFootsteps.Positions[index]);
        }
    }

    [SkippableFact]
    public void Ensure_CreatesSevenOrdinaryVisibleStaticsOnce()
    {
        TileDataRequirement.SkipIfMissing();
        var trail = EnsureAndTrack(out var created);
        Assert.Equal(7, created);
        var reused = OrcCaveFootsteps.Ensure(out created);
        Assert.Equal(0, created);
        for (var index = 0; index < trail.Length; index++)
        {
            var footprint = Assert.IsType<Static>(trail[index]);
            Assert.Same(footprint, reused[index]);
            Assert.Equal(OrcCaveFootsteps.Positions[index], footprint.Location);
            Assert.Same(Map.Felucca, footprint.Map);
            Assert.Equal(OrcCaveFootsteps.ArtId, footprint.ItemID);
            Assert.Equal(OrcCaveFootsteps.TrailName, footprint.Name);
            Assert.Equal(0, footprint.Hue);
            Assert.True(footprint.Visible);
            Assert.False(footprint.Movable);
            Assert.False(footprint.ItemData.Surface);
            Assert.False(footprint.ItemData.Impassable);
            Assert.Equal(0, footprint.ItemData.CalcHeight);
        }
    }

    [SkippableFact]
    public void NativeWalking_NorthAlongEveryFootprint_RemainsUnobstructed()
    {
        TileDataRequirement.SkipIfMissing();
        EnsureAndTrack(out _);
        var player = Track(new Mobile { Player = true, Body = 0x190, Direction = Direction.North });
        player.MoveToWorld(new Point3D(1405, 1469, 0), Map.Felucca);
        for (var y = 1469; y >= 1463; y--)
        {
            Assert.True(Map.Felucca.CanFit(1405, y, 0, 16, true, false));
            if (y < 1469)
            {
                Assert.True(player.Move(Direction.North));
            }
            Assert.Equal(new Point3D(1405, y, 0), player.Location);
            Assert.Same(Map.Felucca, player.Map);
        }
    }

    [SkippableTheory]
    [InlineData("WrongOwnArt")]
    [InlineData("OtherName")]
    [InlineData("Duplicate")]
    [InlineData("WrongZ")]
    public void LateConflict_IsRejectedBeforeAnyEarlierMissingFootprintIsCreated(string conflict)
    {
        TileDataRequirement.SkipIfMissing();
        var last = OrcCaveFootsteps.Positions[^1];
        var conflicting = Track(new Static(conflict == "WrongOwnArt" ? 0x1E06 : OrcCaveFootsteps.ArtId)
        {
            Name = conflict == "OtherName" ? "Unrelated footprints" : OrcCaveFootsteps.TrailName
        });
        var conflictPoint = conflict == "WrongZ" ? new Point3D(last.X, last.Y, 0) : last;
        conflicting.MoveToWorld(conflictPoint, Map.Felucca);
        Static duplicate = null;
        if (conflict == "Duplicate")
        {
            duplicate = Track(new Static(OrcCaveFootsteps.ArtId) { Name = OrcCaveFootsteps.TrailName });
            duplicate.MoveToWorld(last, Map.Felucca);
        }

        Assert.Throws<InvalidOperationException>(() => OrcCaveFootsteps.Ensure(out _));

        for (var index = 0; index < OrcCaveFootsteps.Positions.Length - 1; index++)
        {
            foreach (var item in Map.Felucca.GetItemsAt<Static>(OrcCaveFootsteps.Positions[index]))
            {
                Assert.Fail($"Footprint created before full preflight: {item.Location}");
            }
        }
        Assert.False(conflicting.Deleted);
        Assert.Equal(conflictPoint, conflicting.Location);
        Assert.Equal(conflict == "WrongOwnArt" ? 0x1E06 : OrcCaveFootsteps.ArtId, conflicting.ItemID);
        Assert.Equal(conflict == "OtherName" ? "Unrelated footprints" : OrcCaveFootsteps.TrailName, conflicting.Name);
        if (duplicate != null)
        {
            Assert.False(duplicate.Deleted);
            Assert.Equal(last, duplicate.Location);
        }
    }

    [SkippableFact]
    public void Ensure_PreservesUnrelatedStaticsAndNeighboringItems()
    {
        TileDataRequirement.SkipIfMissing();
        var sameTile = Track(new Static(0xEED) { Name = "Unrelated coins" });
        sameTile.MoveToWorld(OrcCaveFootsteps.Positions[0], Map.Felucca);
        var neighbor = Track(new Item(0xEED));
        var neighborPoint = new Point3D(1406, 1469, 0);
        neighbor.MoveToWorld(neighborPoint, Map.Felucca);

        EnsureAndTrack(out var created);

        Assert.Equal(7, created);
        Assert.False(sameTile.Deleted);
        Assert.Equal(OrcCaveFootsteps.Positions[0], sameTile.Location);
        Assert.Equal("Unrelated coins", sameTile.Name);
        Assert.False(neighbor.Deleted);
        Assert.Equal(neighborPoint, neighbor.Location);
    }

    [SkippableFact]
    public void OrdinarySaveLoad_PreservesAndReusesAllSevenFootprints()
    {
        TileDataRequirement.SkipIfMissing();
        var trail = EnsureAndTrack(out _);
        var restored = new Static[trail.Length];
        for (var index = 0; index < trail.Length; index++)
        {
            var writer = new BufferWriter(true);
            trail[index].Serialize(writer);
            var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
            trail[index].Delete();
            restored[index] = Track(new Static(World.NewItem));
            var reader = new BufferReader(bytes);
            restored[index].Deserialize(reader);
            Assert.Equal(bytes.Length, reader.Position);
        }

        var reused = OrcCaveFootsteps.Ensure(out var created);

        Assert.Equal(0, created);
        for (var index = 0; index < restored.Length; index++)
        {
            Assert.Same(restored[index], reused[index]);
            Assert.Equal(OrcCaveFootsteps.Positions[index], reused[index].Location);
            Assert.Equal(OrcCaveFootsteps.ArtId, reused[index].ItemID);
            Assert.Equal(OrcCaveFootsteps.TrailName, reused[index].Name);
            Assert.True(reused[index].Visible);
            Assert.False(reused[index].Movable);
        }
    }

    private Static[] EnsureAndTrack(out int created)
    {
        var trail = OrcCaveFootsteps.Ensure(out created);
        foreach (var footprint in trail)
        {
            if (!_created.Contains(footprint))
            {
                Track(footprint);
            }
        }
        return trail;
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}
