using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Server.Items;

namespace Server.Engines.MorganCastle;

// Recovery blueprint only. Normal world saves own the live doors.
public static class MorganCastleDoors
{
    public sealed record Definition(int X, int Y, int Z, string Facing, bool Locked, uint KeyValue);

    public static void Configure() => CommandSystem.Register("RebuildMorganDoors", AccessLevel.Developer, e =>
    {
        try
        {
            var count = Rebuild();
            e.Mobile.SendMessage($"Created {count} missing castle gates. Existing gates were left unchanged. Save the world to persist changes.");
        }
        catch (Exception ex) { e.Mobile.SendMessage($"Castle door rebuild stopped: {ex.Message}"); }
    });

    public static Definition[] ReadDefinitions()
    {
        var definitions = JsonSerializer.Deserialize<Definition[]>(File.ReadAllText(
            Path.Combine(Core.BaseDirectory, "Data", "MorganCastle", "doors.json")));
        if (definitions == null || definitions.Length != 27 ||
            definitions.Select(d => (d.X, d.Y, d.Z)).Distinct().Count() != definitions.Length ||
            definitions.Any(d => d.X < 5424 || d.X > 5521 || d.Y < 910 || d.Y > 1005 || d.Z != 0 ||
                !Enum.TryParse<DoorFacing>(d.Facing, out var facing) || !Enum.IsDefined(facing)))
        { throw new InvalidOperationException("Invalid castle door blueprint."); }
        return definitions;
    }

    public static Point3D ClosedLocation(BaseDoor door) => door.Open
        ? new Point3D(door.X - door.Offset.X, door.Y - door.Offset.Y, door.Z - door.Offset.Z)
        : door.Location;

    public static int Rebuild()
    {
        var definitions = ReadDefinitions();
        var doors = World.Items.Values.OfType<BaseDoor>().Where(d => d.Map == Map.Felucca).ToArray();
        var missing = definitions.Where(d => !doors.Any(g => ClosedLocation(g) == new Point3D(d.X, d.Y, d.Z))).ToArray();
        // Preflight the whole batch; never place through scenery or other solid items.
        if (missing.Any(d => !Map.Felucca.CanFit(d.X, d.Y, d.Z, 16, false, false)))
        { throw new InvalidOperationException("A missing door location is obstructed; no doors were created."); }
        foreach (var d in missing)
        {
            var gate = new IronGate(Enum.Parse<DoorFacing>(d.Facing)) { Locked = d.Locked, KeyValue = d.KeyValue };
            gate.MoveToWorld(new Point3D(d.X, d.Y, d.Z), Map.Felucca);
        }
        return missing.Length;
    }
}
