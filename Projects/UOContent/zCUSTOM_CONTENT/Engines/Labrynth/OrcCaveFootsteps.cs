using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class OrcCaveFootsteps
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(OrcCaveFootsteps));
    internal const int ArtId = 0x1E04;
    internal const string TrailName = "Cave entrance footprints";
    internal static readonly Point3D[] Positions =
    [
        new(1405, 1469, 1), new(1405, 1468, 1), new(1405, 1467, 1), new(1405, 1466, 1),
        new(1405, 1465, 1), new(1405, 1464, 1), new(1405, 1463, 1)
    ];

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data");
        if (!File.Exists(Path.Combine(directory, "orc-cave-footsteps.pending")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "orc-cave-footsteps-result.json");
        try
        {
            var footprints = Ensure(out var created);
            var pieces = new object[footprints.Length];
            for (var index = 0; index < pieces.Length; index++)
            {
                var item = footprints[index];
                pieces[index] = new { Serial = item.Serial.Value, item.X, item.Y, item.Z, item.ItemID, item.Hue };
            }
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                Success = true, Created = created, Map = "Felucca", Pieces = pieces, AwaitingWorldSave = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            _logger.Information("Verified cave entrance footprints; created={Created}; awaiting world save", created);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Cave footprint installation refused; authorization remains pending");
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = false, Error = exception.Message }));
        }
    }

    internal static Static[] Ensure(out int created)
    {
        created = 0;
        var art = TileData.ItemTable[ArtId];
        if (art.Height != 0 || art.Surface || art.Impassable)
        {
            throw new InvalidOperationException("Cave footprints must use flat, nonblocking native artwork.");
        }
        var footprints = new Static[Positions.Length];
        for (var index = 0; index < Positions.Length; index++)
        {
            var position = Positions[index];
            foreach (var item in Map.Felucca.GetItemsAt<Item>(position))
            {
                if (item.Name != TrailName && item.ItemID != ArtId)
                {
                    continue;
                }
                if (footprints[index] != null || item.GetType() != typeof(Static) || item.ItemID != ArtId ||
                    item.Name != TrailName || item.Location != position || item.Map != Map.Felucca ||
                    item.Hue != 0 || !item.Visible || item.Movable)
                {
                    throw new InvalidOperationException($"Conflicting or duplicate cave footprints at {position}.");
                }
                footprints[index] = (Static)item;
            }
            // The decal sits just above the native floor to keep it visible without adding a walking surface.
            if (!Map.Felucca.CanFit(new Point3D(position.X, position.Y, position.Z - 1), 16, true, false))
            {
                throw new InvalidOperationException($"The cave footprint trail needs supported walking ground at {position}.");
            }
        }
        var added = new bool[footprints.Length];
        try
        {
            for (var index = 0; index < footprints.Length; index++)
            {
                if (footprints[index] != null)
                {
                    continue;
                }
                footprints[index] = new Static(ArtId) { Name = TrailName };
                added[index] = true;
                footprints[index].MoveToWorld(Positions[index], Map.Felucca);
                created++;
            }
            return footprints;
        }
        catch
        {
            for (var index = 0; index < footprints.Length; index++)
            {
                if (added[index])
                {
                    footprints[index].Delete();
                }
            }
            created = 0;
            throw;
        }
    }
}
