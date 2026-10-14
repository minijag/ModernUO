using System;
using System.Collections.Generic;
using Server;
using Server.Tests;
using Xunit;
using Movement = Server.Movement.Movement;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class DockRailingMapTests : IDisposable
{
    private const int DockStandingZ = 1;
    private readonly List<Mobile> _created = [];

    public void Dispose()
    {
        foreach (var mobile in _created)
        {
            mobile.Delete();
        }
    }

    [SkippableFact]
    public void EastExpansion_HasNativeSupportedPlanksAndTwoClearInteriorLanes()
    {
        TileDataRequirement.SkipIfMissing();

        for (var x = 1778; x <= 1780; x++)
        {
            for (var y = 927; y <= 939; y++)
            {
                Assert.True(HasNativePlank(x, y), $"Missing native dock plank at {x},{y},0.");
            }
        }

        for (var x = 1778; x <= 1779; x++)
        {
            for (var y = 928; y <= 938; y++)
            {
                Assert.True(Map.Felucca.CanFit(x, y, DockStandingZ, 16, true, false), $"The expanded dock lane is blocked at {x},{y},{DockStandingZ}.");
            }
        }
    }

    [SkippableFact]
    public void EastRopeBoundary_IsContinuousBlockedAndCappedAtBothEnds()
    {
        TileDataRequirement.SkipIfMissing();
        var player = CreateWalker();

        for (var y = 927; y <= 939; y++)
        {
            Assert.True(HasNativeRail(1780, y), $"Missing native rope or pier at 1780,{y}.");
            Assert.False(Map.Felucca.CanFit(1780, y, DockStandingZ, 16, true, false));
            if (y == 927 || y == 939)
            {
                continue;
            }
            player.MoveToWorld(new Point3D(1779, y, DockStandingZ), Map.Felucca);
            player.Direction = Direction.East;
            Assert.False(player.Move(Direction.East));
            Assert.Equal(new Point3D(1779, y, DockStandingZ), player.Location);
        }
    }

    [SkippableFact]
    public void NativeWalking_ConnectsTheOriginalDockApproachToTheExpandedLaneAndBack()
    {
        TileDataRequirement.SkipIfMissing();
        var player = CreateWalker();
        var path = FindNativePath(player, new Point2D(1768, 936), new Point2D(1779, 936), new Rectangle2D(1767, 928, 13, 11), DockStandingZ);

        WalkPath(player, path, DockStandingZ);
        path.Reverse();
        WalkPath(player, path, DockStandingZ);
        Assert.Equal(new Point3D(1768, 936, DockStandingZ), player.Location);
    }

    [SkippableFact]
    public void LocalMazeSetback_PreservesTheTwoTileHedgeWallAndOriginalEncounterCorridor()
    {
        TileDataRequirement.SkipIfMissing();
        for (var x = 1787; x <= 1788; x++)
        {
            for (var y = 928; y <= 930; y++)
            {
                Assert.True(HasNativeHedge(x, y), $"Retained maze hedge is missing at {x},{y}.");
                Assert.False(Map.Felucca.CanFit(x, y, 0, 16, true, false));
            }
        }
        for (var x = 1789; x <= 1791; x++)
        {
            for (var y = 929; y <= 931; y++)
            {
                Assert.True(Map.Felucca.CanFit(x, y, 0, 16, true, false), $"Original maze encounter corridor is blocked at {x},{y},0.");
            }
        }
        var player = CreateWalker();
        var path = FindNativePath(player, new Point2D(1790, 931), new Point2D(1790, 929), new Rectangle2D(1789, 929, 3, 3), 0);
        Assert.Contains(new Point2D(1790, 930), path);
        WalkPath(player, path, 0);
    }

    private static bool HasNativePlank(int x, int y)
    {
        foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(x, y))
        {
            if (tile.Z == 0 && tile.ID is >= 0x7CD and <= 0x7D0 && TileData.ItemTable[tile.ID].Surface && TileData.ItemTable[tile.ID].CalcHeight == DockStandingZ)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasNativeRail(int x, int y)
    {
        foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(x, y))
        {
            if (tile.Z is >= 0 and <= 2 && tile.ID is >= 0x3A5 and <= 0x3AC && TileData.ItemTable[tile.ID].Impassable)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasNativeHedge(int x, int y)
    {
        foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(x, y))
        {
            if (tile.Z == 0 && tile.ID is 0xDB8 or 0xDB9 && TileData.ItemTable[tile.ID].Impassable)
            {
                return true;
            }
        }
        return false;
    }

    private static List<Point2D> FindNativePath(Mobile player, Point2D start, Point2D destination, Rectangle2D bounds, int standingZ)
    {
        (int X, int Y, Direction Direction)[] steps =
        [
            (0, -1, Direction.North), (1, 0, Direction.East),
            (0, 1, Direction.South), (-1, 0, Direction.West)
        ];
        Assert.True(Map.Felucca.CanFit(start.X, start.Y, standingZ, 16, true, false));
        Assert.True(Map.Felucca.CanFit(destination.X, destination.Y, standingZ, 16, true, false));
        var pending = new Queue<Point2D>();
        var previous = new Dictionary<Point2D, Point2D> { [start] = start };
        pending.Enqueue(start);
        while (pending.TryDequeue(out var point))
        {
            if (point == destination)
            {
                var path = new List<Point2D> { destination };
                while (path[^1] != start)
                {
                    path.Add(previous[path[^1]]);
                }
                path.Reverse();
                return path;
            }
            foreach (var step in steps)
            {
                var next = new Point2D(point.X + step.X, point.Y + step.Y);
                if (!bounds.Contains(next) || previous.ContainsKey(next) || !Map.Felucca.CanFit(next.X, next.Y, standingZ, 16, true, false))
                {
                    continue;
                }
                player.MoveToWorld(new Point3D(point.X, point.Y, standingZ), Map.Felucca);
                player.Direction = step.Direction;
                if (!Movement.CheckMovement(player, step.Direction, out var z) || z != standingZ)
                {
                    continue;
                }
                previous.Add(next, point);
                pending.Enqueue(next);
            }
        }
        throw new InvalidOperationException($"No native walking path connects {start} to {destination} inside {bounds}.");
    }

    private static void WalkPath(Mobile player, List<Point2D> path, int standingZ)
    {
        player.MoveToWorld(new Point3D(path[0].X, path[0].Y, standingZ), Map.Felucca);
        for (var index = 1; index < path.Count; index++)
        {
            var from = path[index - 1];
            var to = path[index];
            var direction = to.Y < from.Y ? Direction.North : to.X > from.X ? Direction.East : to.Y > from.Y ? Direction.South : Direction.West;
            player.Direction = direction;
            Assert.True(player.Move(direction));
            Assert.Equal(new Point3D(to.X, to.Y, standingZ), player.Location);
        }
    }

    private Mobile CreateWalker()
    {
        var mobile = new Mobile { Player = true, Body = 0x190 };
        _created.Add(mobile);
        return mobile;
    }
}
