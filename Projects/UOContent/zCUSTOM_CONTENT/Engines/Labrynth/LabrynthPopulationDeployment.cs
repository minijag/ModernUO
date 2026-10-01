using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server.Engines.Spawners;
using Server.Json;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class LabrynthPopulationDeployment
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(LabrynthPopulationDeployment));
    private static readonly Rectangle2D _bounds = new(5168, 632, 144, 168);
    private static readonly HashSet<Point3D> _reached = new();
    private static readonly Queue<Point3D> _queue = new();
    private static readonly List<BaseSpawner> _created = new();
    private static readonly Direction[] _directions = { Direction.North, Direction.East, Direction.South, Direction.West };
    private static string Folder => Path.Combine(Core.BaseDirectory, "Data", "LabrynthPopulation");
    private static Mobile _probe;
    private static int _polls;

    public static void Initialize()
    {
        if (!File.Exists(Path.Combine(Folder, "survey.pending")) || File.Exists(Path.Combine(Folder, "installed.json")))
        {
            return;
        }
        _probe = new Mobile { Body = 0x190 };
        var start = new Point3D(5248, 784, 0);
        _reached.Add(start);
        _queue.Enqueue(start);
        Timer.DelayCall(TimeSpan.FromSeconds(1), Survey);
    }

    private static void Survey()
    {
        try
        {
            for (var n = 0; n < 300 && _queue.Count > 0; n++)
            {
                var p = _queue.Dequeue();
                _probe.Location = p;
                foreach (var direction in _directions)
                {
                    var x = p.X;
                    var y = p.Y;
                    Movement.Movement.Offset(direction, ref x, ref y);
                    if (!_bounds.Contains(x, y))
                    {
                        continue;
                    }
                    if (Movement.Movement.CheckMovement(_probe, Map.Felucca, p, direction, out var z) && z >= -40 && z <= 40)
                    {
                        var next = new Point3D(x, y, z);
                        if (_reached.Add(next))
                        {
                            _queue.Enqueue(next);
                        }
                    }
                }
            }
            if (_queue.Count > 0)
            {
                Timer.DelayCall(TimeSpan.FromMilliseconds(50), Survey);
                return;
            }
            _probe.Delete();
            _probe = null;
            var points = new List<int[]>();
            foreach (var p in _reached)
            {
                if (Map.Felucca.CanFit(p.X, p.Y, p.Z, 16, false, false))
                {
                    points.Add(new[] { p.X, p.Y, p.Z });
                }
            }
            var existing = new List<object>();
            foreach (var s in Map.Felucca.GetItemsInBounds<BaseSpawner>(_bounds))
            {
                existing.Add(new { s.Name, s.Guid, X = s.X, Y = s.Y, Z = s.Z });
            }
            File.WriteAllText(Path.Combine(Folder, "survey.json"), JsonSerializer.Serialize(new { Points = points, Existing = existing }));
            _logger.Information("Labrynth survey completed: {Count} reachable standing positions", points.Count);
            Timer.DelayCall(TimeSpan.FromSeconds(2), AwaitPlan);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private static void AwaitPlan()
    {
        try
        {
            if (!File.Exists(Path.Combine(Folder, "install.pending")))
            {
                if (++_polls < 900)
                {
                    Timer.DelayCall(TimeSpan.FromSeconds(2), AwaitPlan);
                }
                return;
            }
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Core.BaseDirectory, "zodiac.json")));
            var planned = new Queue<SpawnerDto>();
            var ids = new HashSet<Guid>();
            var allowed = new HashSet<string> { "TormentedMinotaur", "Gargoyle", "GiantSpider", "StoneHarpy", "GiantSerpent", "Wraith", "Lich" };
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("name", out var name) || name.GetString() is not { } label || !label.StartsWith("Labrynth Dungeon - ", StringComparison.Ordinal))
                {
                    continue;
                }
                // This deployment accepts only concrete standard spawners from its own prefix.
                var dto = element.Deserialize<SpawnerDataDto>(SpawnerJsonSerializer.Options);
                var p = dto.Location;
                if (dto.Map != Map.Felucca || !_reached.Contains(p) || !Map.Felucca.CanFit(p.X, p.Y, p.Z, 16, false, false) || !ids.Add(dto.Guid))
                {
                    throw new InvalidOperationException($"Invalid encounter {dto.Name}");
                }
                if (Math.Abs(p.X - 5248) + Math.Abs(p.Y - 784) < 15 || dto.Count < 1 || dto.Count > 8)
                {
                    throw new InvalidOperationException($"Unsafe encounter {dto.Name}");
                }
                foreach (var entry in dto.EntryView)
                {
                    if (!allowed.Contains(entry.SpawnedName))
                    {
                        throw new InvalidOperationException($"Unexpected monster in {dto.Name}");
                    }
                }
                planned.Enqueue(dto);
            }
            if (planned.Count < 40 || planned.Count > 180)
            {
                throw new InvalidOperationException("Unexpected encounter count");
            }
            foreach (var s in Map.Felucca.GetItemsInBounds<BaseSpawner>(_bounds))
            {
                if (ids.Contains(s.Guid))
                {
                    throw new InvalidOperationException("Population already partly installed; inspect before retrying");
                }
            }
            InstallBatch(planned);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private static void InstallBatch(Queue<SpawnerDto> planned)
    {
        try
        {
            if (World.Saving || World.WorldState == WorldState.PendingSave)
            {
                Timer.DelayCall(TimeSpan.FromSeconds(1), () => InstallBatch(planned));
                return;
            }
            for (var n = 0; n < 4 && planned.Count > 0; n++)
            {
                var dto = planned.Dequeue();
                var spawner = dto.ToSpawner();
                _created.Add(spawner);
                spawner.MoveToWorld(dto.Location, dto.Map);
                spawner.Respawn();
            }
            if (planned.Count > 0)
            {
                Timer.DelayCall(TimeSpan.FromMilliseconds(100), () => InstallBatch(planned));
                return;
            }
            var population = 0;
            var audit = new List<object>();
            foreach (var s in _created)
            {
                var spawned = 0;
                foreach (var entry in s.Entries)
                {
                    spawned += entry.Spawned.Count;
                    foreach (var entity in entry.Spawned)
                    {
                        if (entity is Mobile mobile && (mobile.Map != Map.Felucca || !_bounds.Contains(mobile.X, mobile.Y)))
                        {
                            throw new InvalidOperationException("Creature spawned outside dungeon");
                        }
                    }
                }
                if (spawned != s.Count)
                {
                    throw new InvalidOperationException($"Population mismatch at {s.Name}: {spawned}/{s.Count}");
                }
                population += spawned;
                audit.Add(new { s.Name, s.Guid, X = s.X, Y = s.Y, Spawned = spawned });
            }
            World.Save();
            File.WriteAllText(Path.Combine(Folder, "installed.json"), JsonSerializer.Serialize(new { Spawners = _created.Count, Population = population, Encounters = audit, Reachable = _reached.Count, At = DateTime.UtcNow }));
            File.Delete(Path.Combine(Folder, "install.pending"));
            File.Delete(Path.Combine(Folder, "survey.pending"));
            _logger.Information("Labrynth population installed: {Count} encounter spawners", _created.Count);
            _created.Clear();
        }
        catch (Exception ex)
        {
            foreach (var s in _created)
            {
                s.Delete();
            }
            _created.Clear();
            Fail(ex);
        }
    }

    private static void Fail(Exception ex)
    {
        _probe?.Delete();
        _probe = null;
        File.WriteAllText(Path.Combine(Folder, "failure.txt"), ex.ToString());
        _logger.Error(ex, "Labrynth population deployment failed");
    }
}
