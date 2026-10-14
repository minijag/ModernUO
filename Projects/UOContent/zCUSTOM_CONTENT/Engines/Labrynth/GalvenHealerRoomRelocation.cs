using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Server.Engines.Spawners;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.Engines.Labrynth;

public static class GalvenHealerRoomRelocation
{
    internal const string RelocationId = "galven-healer-room-v1";
    internal static readonly Rectangle3D Source = new(1279, 1323, 20, 7, 12, 20);
    internal static readonly Rectangle3D Destination = new(1258, 1333, 10, 7, 12, 20);
    private const int MaximumEntities = 512;
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(GalvenHealerRoomRelocation));
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    internal readonly record struct Pin(Guid Guid, Point3D Position);
    internal static readonly Pin[] Pins =
    [
        new(new Guid("eeedbdd4-d136-4d90-8e23-fbcb4f7642eb"), new(1281, 1323, 20)),
        new(new Guid("2d7530ca-b91e-4f93-8c49-db57e1275ca5"), new(1283, 1324, 20))
    ];

    internal sealed record Bounds(Point3D Start, int Width, int Height, int Depth)
    {
        public Rectangle3D ToRectangle() => new(Start.X, Start.Y, Start.Z, Width, Height, Depth);
        public static Bounds Capture(Rectangle3D bounds) => new(bounds.Start, bounds.Width, bounds.Height, bounds.Depth);
    }

    internal sealed record EntitySnapshot(uint Serial, string Type, bool Mobile, Point3D Original, Point3D Target,
        int ItemId, int Hue, string Name, bool Movable, bool Visible, uint SpawnerSerial, Point3D Home, Point3D TargetHome);
    internal sealed record AttachmentSnapshot(uint Serial, string Type, uint ParentSerial, Point3D Location, int ItemId,
        int Hue, string Name, bool Movable, bool Visible);
    internal sealed record SpawnerSnapshot(uint Serial, Guid Guid, Bounds OriginalBounds, Bounds TargetBounds,
        JsonElement Configuration, bool Running, bool UseSpiralScan);
    internal sealed record Archive(string Relocation, EntitySnapshot[] Entities, AttachmentSnapshot[] Attachments,
        SpawnerSnapshot[] Spawners);
    internal sealed record Result(bool Changed, Archive Before);

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "GalvenHealerRoom");
        var pending = Path.Combine(directory, "install.pending");
        if (!File.Exists(pending) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }

        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            Require(File.ReadAllText(pending).Trim() == RelocationId, "The healer-room installation marker is invalid.");
            WriteJson(Path.Combine(directory, "survey.json"), new
            {
                RelocationId,
                Source = Survey(Source),
                Destination = Survey(Destination)
            });
            var result = Ensure(Path.Combine(directory, "before.json"));
            var after = new object[result.Before.Entities.Length];
            for (var index = 0; index < after.Length; index++)
            {
                var original = result.Before.Entities[index];
                var entity = FindEntity(original);
                after[index] = new
                {
                    original.Serial,
                    original.Type,
                    entity.Location,
                    original.ItemId,
                    Home = entity is Healer healer ? (Point3D?)healer.Home : null
                };
            }
            var spawners = new object[result.Before.Spawners.Length];
            for (var index = 0; index < spawners.Length; index++)
            {
                var spawner = (Spawner)World.FindItem((Serial)result.Before.Spawners[index].Serial);
                spawners[index] = new
                {
                    Serial = spawner.Serial.Value,
                    spawner.Guid,
                    Bounds = Bounds.Capture(spawner.SpawnBounds),
                    Configuration = JsonSerializer.SerializeToElement((SpawnerDataDto)spawner.ToDto(), SpawnerJsonSerializer.Options)
                };
            }
            WriteJson(resultPath, new
            {
                Success = true,
                RelocationId,
                result.Changed,
                AwaitingWorldSave = true,
                Entities = after,
                Spawners = spawners,
                Archive = "Data/GalvenHealerRoom/before.json"
            });
            _logger.Information("Galven healer-room relocation verified; changed={Changed}, entities={Count}; awaiting world save",
                result.Changed, after.Length);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Galven healer-room relocation refused");
            WriteJson(resultPath, new { Success = false, RelocationId, Error = exception.ToString() });
        }
    }

    internal static Point3D Translate(Point3D point) => new(point.X - 21, point.Y + 10, point.Z - 10);

    internal static Rectangle3D TranslateBounds(Rectangle3D bounds) => new(
        bounds.Start.X - 21, bounds.Start.Y + 10,
        bounds.Start.Z == -128 && bounds.Depth == 256 ? -128 : bounds.Start.Z - 10,
        bounds.Width, bounds.Height, bounds.Depth);

    internal static Result Ensure(string archivePath, bool checkSupport = true)
    {
        Archive archive;
        if (File.Exists(archivePath))
        {
            Require(new FileInfo(archivePath).Length is > 0 and <= 1024 * 1024, "The healer-room archive size is invalid.");
            archive = JsonSerializer.Deserialize<Archive>(File.ReadAllBytes(archivePath), _jsonOptions);
        }
        else
        {
            archive = Capture(checkSupport);
            Directory.CreateDirectory(Path.GetDirectoryName(archivePath));
            using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, archive, _jsonOptions);
            stream.Flush(true);
        }

        ValidateArchive(archive);
        var state = ValidateState(archive);
        ValidateRoomContents(archive);
        if (state)
        {
            return new Result(false, archive);
        }
        if (checkSupport)
        {
            ValidateStandingTiles(archive);
        }

        try
        {
            Apply(archive, false);
            Require(ValidateState(archive), "The healer-room move did not produce a complete installed state.");
        }
        catch
        {
            Apply(archive, true);
            throw;
        }
        return new Result(true, archive);
    }

    private static Archive Capture(bool checkSupport)
    {
        var entities = new List<EntitySnapshot>();
        var attachments = new List<AttachmentSnapshot>();
        var spawners = new SpawnerSnapshot[Pins.Length];
        var owned = new HashSet<uint>();
        for (var index = 0; index < Pins.Length; index++)
        {
            var pin = Pins[index];
            Spawner spawner = null;
            foreach (var candidate in Map.Felucca.GetItemsAt<BaseSpawner>(pin.Position))
            {
                Require(candidate.GetType() == typeof(Spawner) && candidate.Guid == pin.Guid && spawner == null,
                    $"Unexpected healer spawner at {pin.Position}.");
                spawner = (Spawner)candidate;
            }
            Require(spawner != null && spawner.Parent == null && spawner.Location == pin.Position,
                $"Healer spawner {pin.Guid} is missing or misplaced.");
            ValidateSpawner(spawner);
            Require(spawner.Spawned.Count == 1, $"Healer spawner {pin.Guid} must own exactly one healer.");
            spawners[index] = new SpawnerSnapshot(spawner.Serial.Value, pin.Guid, Bounds.Capture(spawner.SpawnBounds),
                Bounds.Capture(TranslateBounds(spawner.SpawnBounds)),
                JsonSerializer.SerializeToElement((SpawnerDataDto)spawner.ToDto(), SpawnerJsonSerializer.Options),
                spawner.Running, spawner.UseSpiralScan);
            entities.Add(CaptureItem(spawner));
            foreach (var spawned in spawner.Spawned.Keys)
            {
                Require(spawned is Healer, $"Spawner {pin.Guid} owns an unexpected entity.");
                var healer = (Healer)spawned;
                ValidateHealer(healer);
                Require(healer.Spawner == spawner && healer.Map == Map.Felucca && healer.InRange(pin.Position, 12),
                    $"Spawner {pin.Guid} owns a healer outside the bounded relocation survey.");
                var target = Translate(healer.Location);
                if (!Destination.Contains(target) || checkSupport && !Map.Felucca.CanFit(target, 16, true, false))
                {
                    target = Translate(pin.Position);
                }
                entities.Add(CaptureHealer(healer, target, Translate(pin.Position)));
                CaptureAttachments(healer.Items, healer.Serial.Value, attachments);
                Require(owned.Add(healer.Serial.Value), "A healer is owned by both pinned spawners.");
            }
        }

        foreach (var item in Map.Felucca.GetItemsInBounds<Item>(Flat(Source)))
        {
            if (item.Parent != null || !Source.Contains(item.Location))
            {
                continue;
            }
            if (item is BaseSpawner)
            {
                Require(IsPinnedSerial(item.Serial.Value, spawners), $"Unexpected spawner {item.Serial} in the healer room.");
                continue;
            }
            ValidateDecoration(item);
            entities.Add(CaptureItem(item));
            CaptureAttachments(item.Items, item.Serial.Value, attachments);
        }
        foreach (var mobile in Map.Felucca.GetMobilesInBounds<Mobile>(Flat(Source)))
        {
            if (!Source.Contains(mobile.Location) || owned.Contains(mobile.Serial.Value))
            {
                continue;
            }
            Require(mobile is Healer && mobile.Spawner == null, $"Unexpected mobile {mobile.Serial} in the healer room.");
            var healer = (Healer)mobile;
            ValidateHealer(healer);
            entities.Add(CaptureHealer(healer, Translate(healer.Location), Translate(healer.Home)));
            CaptureAttachments(healer.Items, healer.Serial.Value, attachments);
        }
        Require(entities.Count + attachments.Count <= MaximumEntities, "The healer-room survey exceeds its entity limit.");
        return new Archive(RelocationId, entities.ToArray(), attachments.ToArray(), spawners);
    }

    private static EntitySnapshot CaptureItem(Item item) => new(item.Serial.Value, item.GetType().FullName, false,
        item.Location, Translate(item.Location), item.ItemID, item.Hue, item.Name, item.Movable, item.Visible, 0, default, default);

    private static EntitySnapshot CaptureHealer(Healer healer, Point3D target, Point3D home) => new(healer.Serial.Value,
        healer.GetType().FullName, true, healer.Location, target, healer.Body, healer.Hue, healer.Name, false,
        !healer.Hidden, healer.Spawner is Item spawner ? spawner.Serial.Value : 0, healer.Home, home);

    private static void CaptureAttachments(IReadOnlyList<Item> items, uint parentSerial, List<AttachmentSnapshot> result)
    {
        foreach (var item in items)
        {
            Require(result.Count < MaximumEntities, "The healer-room attachment survey exceeds its entity limit.");
            result.Add(new AttachmentSnapshot(item.Serial.Value, item.GetType().FullName, parentSerial, item.Location,
                item.ItemID, item.Hue, item.Name, item.Movable, item.Visible));
            CaptureAttachments(item.Items, item.Serial.Value, result);
        }
    }

    private static void ValidateArchive(Archive archive)
    {
        Require(archive?.Relocation == RelocationId && archive.Spawners?.Length == Pins.Length &&
            archive.Entities?.Length is >= 4 and <= MaximumEntities && archive.Attachments != null &&
            archive.Entities.Length + archive.Attachments.Length <= MaximumEntities, "The healer-room archive is invalid.");
        var serials = new HashSet<uint>();
        foreach (var entity in archive.Entities)
        {
            Require(serials.Add(entity.Serial), "The healer-room archive repeats an entity serial.");
            Require(entity.Mobile ? Destination.Contains(entity.Target) : entity.Target == Translate(entity.Original),
                "The healer-room archive has an unexpected translation.");
        }
        foreach (var attachment in archive.Attachments)
        {
            Require(serials.Add(attachment.Serial), "The healer-room archive repeats an attachment serial.");
        }
        for (var index = 0; index < Pins.Length; index++)
        {
            var snapshot = archive.Spawners[index];
            Require(snapshot.Guid == Pins[index].Guid &&
                snapshot.TargetBounds.ToRectangle() == TranslateBounds(snapshot.OriginalBounds.ToRectangle()),
                "The healer-room archive has unexpected spawner bounds or identity.");
            var found = false;
            foreach (var entity in archive.Entities)
            {
                if (entity.Serial == snapshot.Serial && !entity.Mobile && entity.Original == Pins[index].Position)
                {
                    found = true;
                }
            }
            Require(found, "The healer-room archive has a missing or misplaced pinned spawner.");
        }
    }

    private static bool ValidateState(Archive archive)
    {
        var original = true;
        var installed = true;
        foreach (var snapshot in archive.Entities)
        {
            var entity = FindEntity(snapshot);
            Require(entity != null && !entity.Deleted && entity.Map == Map.Felucca && entity.GetType().FullName == snapshot.Type,
                $"Archived entity {snapshot.Serial} is missing or changed.");
            if (entity is Item item)
            {
                Require(item.Parent == null && item.ItemID == snapshot.ItemId && item.Hue == snapshot.Hue &&
                    item.Name == snapshot.Name && item.Movable == snapshot.Movable && item.Visible == snapshot.Visible,
                    $"Archived item {snapshot.Serial} differs from its snapshot.");
                original &= item.Location == snapshot.Original;
                installed &= item.Location == snapshot.Target;
            }
            else
            {
                var healer = (Healer)entity;
                ValidateHealer(healer);
                Require((healer.Spawner is Item spawner ? spawner.Serial.Value : 0) == snapshot.SpawnerSerial &&
                    healer.Body == snapshot.ItemId && healer.Hue == snapshot.Hue && healer.Name == snapshot.Name,
                    $"Archived healer {snapshot.Serial} differs from its snapshot.");
                original &= healer.Location == snapshot.Original && healer.Home == snapshot.Home;
                installed &= healer.Home == snapshot.TargetHome && healer.InRange(snapshot.TargetHome, 12) &&
                    healer.Z is >= 10 and <= 29;
            }
        }
        foreach (var snapshot in archive.Spawners)
        {
            var spawner = World.FindItem((Serial)snapshot.Serial) as Spawner;
            Require(spawner != null && spawner.Guid == snapshot.Guid && spawner.Running == snapshot.Running &&
                spawner.UseSpiralScan == snapshot.UseSpiralScan, "An archived healer spawner is missing or changed.");
            ValidateSpawner(spawner);
            Require(JsonNode.DeepEquals(ConfigurationWithoutLocation(snapshot.Configuration),
                ConfigurationWithoutLocation(JsonSerializer.SerializeToElement((SpawnerDataDto)spawner.ToDto(),
                    SpawnerJsonSerializer.Options))), "An archived healer spawner configuration changed.");
            original &= spawner.SpawnBounds == snapshot.OriginalBounds.ToRectangle();
            installed &= spawner.SpawnBounds == snapshot.TargetBounds.ToRectangle();
            Require(spawner.Spawned.Count == 1, "An archived healer spawner population changed.");
            foreach (var spawned in spawner.Spawned.Keys)
            {
                Require(spawned is Healer healer && HasMobile(archive, healer.Serial.Value, spawner.Serial.Value),
                    "An archived healer spawner owns an unexpected entity.");
            }
        }
        foreach (var snapshot in archive.Attachments)
        {
            var item = World.FindItem((Serial)snapshot.Serial);
            Require(item != null && !item.Deleted && item.GetType().FullName == snapshot.Type &&
                item.Parent is IEntity parent && parent.Serial.Value == snapshot.ParentSerial &&
                item.Location == snapshot.Location && item.ItemID == snapshot.ItemId && item.Hue == snapshot.Hue &&
                item.Name == snapshot.Name && item.Movable == snapshot.Movable && item.Visible == snapshot.Visible,
                $"Archived attachment {snapshot.Serial} differs from its snapshot.");
        }
        Require(original != installed, "The healer-room world contains a mixed or incomplete relocation state.");
        return installed;
    }

    private static void ValidateRoomContents(Archive archive)
    {
        var known = new HashSet<uint>();
        foreach (var entity in archive.Entities)
        {
            known.Add(entity.Serial);
        }
        foreach (var item in Map.Felucca.GetItemsInBounds<Item>(Flat(Source)))
        {
            if (item.Parent == null && Source.Contains(item.Location))
            {
                Require(known.Contains(item.Serial.Value), $"Unarchived item {item.Serial} remains in the source healer room.");
            }
        }
        foreach (var room in new[] { Source, Destination })
        {
            foreach (var mobile in Map.Felucca.GetMobilesInBounds<Mobile>(Flat(room)))
            {
                if (room.Contains(mobile.Location))
                {
                    Require(!mobile.Player && mobile is not BaseCreature { Controlled: true } &&
                        mobile is not BaseCreature { Summoned: true } && mobile is not BaseCreature { IsStabled: true },
                        "A player or player-owned creature occupies the healer-room relocation area.");
                    if (room == Source)
                    {
                        Require(known.Contains(mobile.Serial.Value), $"Unarchived mobile {mobile.Serial} remains in the source room.");
                    }
                }
            }
            foreach (var spawner in Map.Felucca.GetItemsInBounds<BaseSpawner>(Flat(room)))
            {
                if (room.Contains(spawner.Location))
                {
                    Require(IsPinnedSerial(spawner.Serial.Value, archive.Spawners), "An unexpected spawner occupies a healer room.");
                }
            }
        }
    }

    private static void ValidateStandingTiles(Archive archive)
    {
        foreach (var pin in Pins)
        {
            Require(Map.Felucca.CanFit(Translate(pin.Position), 16, true, false), "A destination healer home lacks clear supported space.");
        }
        foreach (var entity in archive.Entities)
        {
            if (entity.Mobile)
            {
                Require(Map.Felucca.CanFit(entity.Target, 16, true, false), "A destination healer position lacks clear supported space.");
            }
        }
    }

    private static void Apply(Archive archive, bool restore)
    {
        foreach (var snapshot in archive.Entities)
        {
            var entity = FindEntity(snapshot);
            entity.MoveToWorld(restore ? snapshot.Original : snapshot.Target, Map.Felucca);
            if (entity is Healer healer)
            {
                healer.Home = restore ? snapshot.Home : snapshot.TargetHome;
            }
        }
        foreach (var snapshot in archive.Spawners)
        {
            ((Spawner)World.FindItem((Serial)snapshot.Serial)).SpawnBounds =
                (restore ? snapshot.OriginalBounds : snapshot.TargetBounds).ToRectangle();
        }
    }

    private static IEntity FindEntity(EntitySnapshot snapshot) =>
        snapshot.Mobile ? World.FindMobile((Serial)snapshot.Serial) : World.FindItem((Serial)snapshot.Serial);

    private static void ValidateSpawner(Spawner spawner)
    {
        Require(spawner.GetType() == typeof(Spawner) && spawner.Count == 1 && spawner.HomeRange == 4 &&
            spawner.MinDelay == TimeSpan.FromMinutes(8) && spawner.MaxDelay == TimeSpan.FromMinutes(10) &&
            spawner.Team == 0 && spawner.WalkingRange == 4 && spawner.Entries.Count == 1 &&
            string.Equals(spawner.Entries[0].SpawnedName, "healer", StringComparison.OrdinalIgnoreCase) &&
            spawner.Entries[0].SpawnedProbability == 100 && spawner.Entries[0].SpawnedMaxCount == 1,
            "A pinned healer spawner has unexpected configuration.");
    }

    private static void ValidateHealer(Healer healer) => Require(!healer.Deleted && !healer.Player && !healer.Controlled &&
        !healer.Summoned && !healer.IsStabled && healer.ControlMaster == null && healer.SummonMaster == null,
        "A healer is a player or player-owned creature.");

    private static void ValidateDecoration(Item item) => Require(item is not BaseDoor && item is not Corpse &&
        !item.IsLockedDown && !item.IsSecure && item.Spawner == null &&
        (item.GetType() == typeof(Item) || item.GetType() == typeof(Static) ||
            Attribute.IsDefined(item.GetType(), typeof(FurnitureAttribute), true)),
        $"Unexpected dynamic item {item.Serial} ({item.GetType().Name}) in the healer room.");

    private static bool IsPinnedSerial(uint serial, SpawnerSnapshot[] spawners)
    {
        foreach (var spawner in spawners)
        {
            if (spawner.Serial == serial)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasMobile(Archive archive, uint serial, uint spawner)
    {
        foreach (var entity in archive.Entities)
        {
            if (entity.Mobile && entity.Serial == serial && entity.SpawnerSerial == spawner)
            {
                return true;
            }
        }
        return false;
    }

    private static JsonObject ConfigurationWithoutLocation(JsonElement configuration)
    {
        var result = JsonNode.Parse(configuration.GetRawText()).AsObject();
        result.Remove("location");
        return result;
    }

    private static Rectangle2D Flat(Rectangle3D bounds) => new(bounds.Start.X, bounds.Start.Y, bounds.Width, bounds.Height);

    private static object[] Survey(Rectangle3D bounds)
    {
        var result = new List<object>();
        foreach (var item in Map.Felucca.GetItemsInBounds<Item>(Flat(bounds)))
        {
            if (bounds.Contains(item.Location) && item.Parent == null)
            {
                result.Add(new { Serial = item.Serial.Value, Type = item.GetType().FullName, item.Location, item.ItemID, item.Name, item.Movable });
            }
        }
        foreach (var mobile in Map.Felucca.GetMobilesInBounds<Mobile>(Flat(bounds)))
        {
            if (bounds.Contains(mobile.Location))
            {
                result.Add(new { Serial = mobile.Serial.Value, Type = mobile.GetType().FullName, mobile.Location, mobile.Name, mobile.Player });
            }
        }
        Require(result.Count <= MaximumEntities, "The healer-room survey exceeds its entity limit.");
        return result.ToArray();
    }

    private static void WriteJson<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions));
        File.Move(temporary, path, true);
    }

    private static void Require(bool condition, string error)
    {
        if (!condition)
        {
            throw new InvalidOperationException(error);
        }
    }
}
