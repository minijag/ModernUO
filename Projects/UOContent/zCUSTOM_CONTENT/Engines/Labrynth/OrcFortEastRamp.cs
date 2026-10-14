using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class OrcFortEastRamp
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(OrcFortEastRamp));
    internal static readonly Serial[] PadSerials =
    [
        (Serial)0x40007511u, (Serial)0x40007512u, (Serial)0x40007513u, (Serial)0x40007514u
    ];
    internal static readonly Point3D[] PadPositions =
    [
        new(5284, 984, 20), new(5285, 984, 20), new(5284, 983, 20), new(5285, 983, 20)
    ];

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data");
        if (!File.Exists(Path.Combine(directory, "orc-fort-east-ramp.pending")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "orc-fort-east-ramp-result.json");
        try
        {
            var removed = Ensure();
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                Success = true, RemovedTeleporters = removed, RemainingTeleporters = 2,
                RemainingSerials = new[] { PadSerials[0].Value, PadSerials[1].Value },
                RemovedSerials = new[] { PadSerials[2].Value, PadSerials[3].Value },
                AwaitingWorldSave = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            _logger.Information("Verified fort east ramp endpoints; removed={Removed}, retained=2; awaiting world save", removed);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Fort east ramp endpoint cleanup refused; authorization remains pending");
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = false, Error = exception.Message }));
        }
    }

    internal static int Ensure()
    {
        var pads = new Teleporter[PadSerials.Length];
        for (var index = 0; index < pads.Length; index++)
        {
            var found = World.FindItem(PadSerials[index]);
            if (found == null && index >= 2)
            {
                continue;
            }
            if (found is not Teleporter item || item.GetType() != typeof(Teleporter) || item.Parent != null ||
                item.Map != Map.Felucca || item.Location != PadPositions[index] ||
                item.Name != "Dungeon - Return to Orc Fort" || item.PointDest != new Point3D(1666, 1247, 0) ||
                item.MapDest != Map.Felucca || item.Flags != TeleporterFlags.Active || item.Visible || item.Movable ||
                item.Delay != TimeSpan.Zero || item.SoundID != 0)
            {
                throw new InvalidOperationException($"Pinned fort teleporter {PadSerials[index]} has unexpected state.");
            }
            pads[index] = item;
        }
        if ((pads[2] == null) != (pads[3] == null))
        {
            throw new InvalidOperationException("Fort cleanup requires both surplus pads present or both already absent.");
        }
        var observed = 0;
        foreach (var item in Map.Felucca.GetItemsInBounds<Teleporter>(new Rectangle2D(5282, 977, 5, 10)))
        {
            var managed = false;
            foreach (var pad in pads)
            {
                if (item == pad)
                {
                    managed = true;
                    break;
                }
            }
            if (!managed)
            {
                throw new InvalidOperationException($"Unexpected teleporter in the fort entrance at {item.Location}.");
            }
            observed++;
        }
        var installed = pads[2] == null;
        if (observed != (installed ? 2 : 4) || !Map.Felucca.CanFit(PadPositions[0], 16, true, false) ||
            !Map.Felucca.CanFit(PadPositions[1], 16, true, false) ||
            !Map.Felucca.CanFit(5284, 985, 20, 16, true, false) ||
            !Map.Felucca.CanFit(1666, 1247, 0, 16, true, false))
        {
            throw new InvalidOperationException("Fort endpoint count or native landing support is invalid.");
        }
        if (installed)
        {
            return 0;
        }
        pads[2].Delete();
        pads[3].Delete();
        return 2;
    }
}
