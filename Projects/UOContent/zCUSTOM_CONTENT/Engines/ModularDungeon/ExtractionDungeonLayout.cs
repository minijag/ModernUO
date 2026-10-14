using System;
using System.Collections.Generic;

namespace Server.Engines.ModularDungeon;

public enum ExtractionCreatureKind
{
    Skeleton,
    Mummy,
    GiantSpider,
    DireWolf,
    SilverSerpent,
    Gazer,
    Wraith
}

public readonly record struct ExtractionCreatureSpawn(ExtractionCreatureKind Kind, Point3D Position, int RangeHome);

public sealed record ExtractionRoomSpawn(
    int ModuleId,
    string Name,
    bool IsOuter,
    Point3D PlayerStart,
    ExtractionCreatureSpawn[] Creatures
);

public static class ExtractionDungeonLayout
{
    public const string BlueprintSha256 = "391ba3505002ef9f20b72b4f5ea767227c049742bd23ffdfe533e7a4ffccde82";
    public const string AbundantBlueprintSha256 = "0ca6ec10f678a0fa130d3b60dc385ec5f9599d7e4a191374d5f7c18fbb874774";
    public const int ModuleSize = 25;
    public const int ModuleStride = 24;
    public const int GridSize = 5;
    public const int CreatureCount = 110;
    public const int MinDungeonZ = -25;
    public const int MaxDungeonZ = 50;

    public static Point3D GatePoint => new(1317, 1379, 0);
    public static Point3D TownReturn => new(1317, 1380, 0);
    public static Rectangle2D LobbyBounds => new(5384, 808, 25, 25);
    public static Point3D LobbyEntry => new(5396, 820, 0);
    public static Point3D LobbyReturnPoint => new(5405, 829, 0);
    public static Rectangle2D DungeonBounds => new(5248, 800, 136, 136);
    public static Rectangle3D DungeonArea => new(5248, 800, MinDungeonZ, 136, 136, MaxDungeonZ - MinDungeonZ + 1);

    // Spawn anchors depend on the installed blueprint's floor, chest and stair clearances.
    public static IReadOnlyList<ExtractionRoomSpawn> Rooms { get; } = Array.AsReadOnly<ExtractionRoomSpawn>(
    [
        new(0, "Chapel of Ash", true, new(5268, 815, 0),
        [
            new(ExtractionCreatureKind.Skeleton, new(5268, 820, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5264, 828, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5260, 820, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5276, 820, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5268, 824, 25), 3),
        ]),
        new(1, "The Silent Scriptorium", true, new(5297, 826, 0),
        [
            new(ExtractionCreatureKind.Gazer, new(5290, 818, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5301, 819, 0), 3),
            new(ExtractionCreatureKind.Gazer, new(5284, 828, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5284, 822, 0), 3),
            new(ExtractionCreatureKind.Gazer, new(5284, 824, 25), 3),
        ]),
        new(2, "Ossuary Galleries", true, new(5309, 826, 0),
        [
            new(ExtractionCreatureKind.Skeleton, new(5317, 821, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5308, 812, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5317, 812, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5310, 819, 0), 3),
        ]),
        new(3, "The Sluiceworks", true, new(5338, 820, 0),
        [
            new(ExtractionCreatureKind.SilverSerpent, new(5343, 820, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5332, 827, 0), 3),
            new(ExtractionCreatureKind.SilverSerpent, new(5333, 813, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5341, 812, 0), 3),
        ]),
        new(4, "Hall of Judgement", true, new(5359, 821, 0),
        [
            new(ExtractionCreatureKind.Skeleton, new(5364, 820, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5362, 811, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5363, 829, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5371, 818, 0), 3),
        ]),
        new(5, "The Ember Forge", true, new(5273, 843, 0),
        [
            new(ExtractionCreatureKind.Skeleton, new(5267, 843, 0), 3),
            new(ExtractionCreatureKind.DireWolf, new(5272, 852, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5276, 836, 0), 3),
            new(ExtractionCreatureKind.DireWolf, new(5264, 851, 0), 3),
        ]),
        new(6, "Armoury", false, new(5291, 844, 0),
        [
            new(ExtractionCreatureKind.Skeleton, new(5296, 840, 0), 3),
            new(ExtractionCreatureKind.DireWolf, new(5288, 852, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5284, 840, 0), 3),
            new(ExtractionCreatureKind.DireWolf, new(5297, 852, 0), 3),
        ]),
        new(7, "Storage Room", false, new(5321, 850, 0),
        [
            new(ExtractionCreatureKind.GiantSpider, new(5316, 844, 0), 3),
            new(ExtractionCreatureKind.DireWolf, new(5310, 836, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5308, 844, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5311, 852, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5316, 846, -25), 3),
        ]),
        new(8, "The Mirror Reliquary", false, new(5339, 844, 0),
        [
            new(ExtractionCreatureKind.Gazer, new(5344, 843, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5332, 852, 0), 3),
            new(ExtractionCreatureKind.Gazer, new(5332, 837, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5341, 852, 0), 3),
        ]),
        new(9, "The Twisting Trap Gallery", true, new(5364, 844, 0),
        [
            new(ExtractionCreatureKind.SilverSerpent, new(5359, 843, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5372, 836, 0), 3),
            new(ExtractionCreatureKind.SilverSerpent, new(5372, 850, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5356, 852, 0), 3),
        ]),
        new(10, "The Rootbound Sanctum", true, new(5268, 869, 0),
        [
            new(ExtractionCreatureKind.DireWolf, new(5264, 864, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5278, 868, 0), 3),
            new(ExtractionCreatureKind.DireWolf, new(5265, 876, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5272, 860, 0), 3),
        ]),
        new(11, "The Cairn Mines", false, new(5293, 864, 0),
        [
            new(ExtractionCreatureKind.GiantSpider, new(5292, 869, 0), 3),
            new(ExtractionCreatureKind.DireWolf, new(5299, 860, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5288, 860, 0), 3),
            new(ExtractionCreatureKind.DireWolf, new(5284, 872, 0), 3),
        ]),
        new(12, "The Twelve-Seal Vault", false, new(5315, 868, 0),
        [
            new(ExtractionCreatureKind.Gazer, new(5323, 862, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5307, 867, 0), 3),
            new(ExtractionCreatureKind.Gazer, new(5321, 876, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5310, 876, 0), 3),
            new(ExtractionCreatureKind.Gazer, new(5315, 869, -25), 3),
        ]),
        new(13, "The Alchemist's Laboratory", false, new(5344, 864, 0),
        [
            new(ExtractionCreatureKind.Gazer, new(5337, 871, 0), 3),
            new(ExtractionCreatureKind.SilverSerpent, new(5348, 874, 0), 3),
            new(ExtractionCreatureKind.Gazer, new(5337, 860, 0), 3),
            new(ExtractionCreatureKind.SilverSerpent, new(5336, 865, 0), 3),
        ]),
        new(14, "The Wraith Sepulchre", true, new(5366, 864, 0),
        [
            new(ExtractionCreatureKind.Mummy, new(5363, 869, 0), 3),
            new(ExtractionCreatureKind.Wraith, new(5372, 874, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5360, 860, 0), 3),
            new(ExtractionCreatureKind.Wraith, new(5372, 861, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5361, 865, -25), 3),
            new(ExtractionCreatureKind.Wraith, new(5374, 863, -25), 3),
        ]),
        new(15, "The Drowned Pilgrims", true, new(5267, 893, 0),
        [
            new(ExtractionCreatureKind.SilverSerpent, new(5267, 888, 0), 3),
            new(ExtractionCreatureKind.DireWolf, new(5264, 900, 0), 3),
            new(ExtractionCreatureKind.SilverSerpent, new(5276, 888, 0), 3),
            new(ExtractionCreatureKind.DireWolf, new(5276, 896, 0), 3),
        ]),
        new(16, "The Smugglers' Stores", false, new(5297, 898, 0),
        [
            new(ExtractionCreatureKind.GiantSpider, new(5294, 888, 0), 3),
            new(ExtractionCreatureKind.DireWolf, new(5285, 899, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5285, 885, 0), 3),
            new(ExtractionCreatureKind.DireWolf, new(5292, 896, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5291, 888, 25), 3),
        ]),
        new(17, "The Bone Wheel", false, new(5315, 892, 0),
        [
            new(ExtractionCreatureKind.Skeleton, new(5320, 891, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5307, 891, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5315, 900, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5315, 883, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5318, 890, 25), 3),
            new(ExtractionCreatureKind.Mummy, new(5306, 882, 25), 3),
        ]),
        new(18, "The Sacrificial Loom", false, new(5340, 886, 0),
        [
            new(ExtractionCreatureKind.Gazer, new(5337, 895, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5350, 897, 0), 3),
            new(ExtractionCreatureKind.Gazer, new(5333, 884, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5346, 884, 0), 3),
        ]),
        new(19, "The Oracle's Observatory", true, new(5367, 889, 0),
        [
            new(ExtractionCreatureKind.Gazer, new(5362, 890, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5374, 889, 0), 3),
            new(ExtractionCreatureKind.Gazer, new(5359, 899, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5367, 883, 0), 3),
        ]),
        new(20, "The Broken Bridge", true, new(5262, 911, 0),
        [
            new(ExtractionCreatureKind.SilverSerpent, new(5272, 912, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5262, 924, 0), 3),
            new(ExtractionCreatureKind.SilverSerpent, new(5272, 922, 0), 3),
            new(ExtractionCreatureKind.GiantSpider, new(5261, 916, 0), 3),
        ]),
        new(21, "bedroom", true, new(5291, 914, 0),
        [
            new(ExtractionCreatureKind.DireWolf, new(5290, 919, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5291, 907, 0), 3),
            new(ExtractionCreatureKind.DireWolf, new(5301, 919, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5300, 910, 0), 3),
        ]),
        new(22, "The Echo Court", true, new(5310, 915, 0),
        [
            new(ExtractionCreatureKind.Gazer, new(5316, 916, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5324, 910, 0), 3),
            new(ExtractionCreatureKind.Gazer, new(5324, 918, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5315, 924, 0), 3),
            new(ExtractionCreatureKind.Gazer, new(5322, 922, 25), 3),
        ]),
        new(23, "Tomb of the First King", true, new(5334, 916, 0),
        [
            new(ExtractionCreatureKind.Mummy, new(5340, 916, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5349, 915, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5346, 924, 0), 3),
            new(ExtractionCreatureKind.Skeleton, new(5332, 911, 0), 3),
        ]),
        new(24, "The Last Treasury", true, new(5358, 912, 0),
        [
            new(ExtractionCreatureKind.Mummy, new(5364, 916, 0), 3),
            new(ExtractionCreatureKind.Gazer, new(5364, 907, 0), 3),
            new(ExtractionCreatureKind.Mummy, new(5372, 922, 0), 3),
            new(ExtractionCreatureKind.Gazer, new(5356, 920, 0), 3),
        ]),
    ]);

    public static IReadOnlyList<ExtractionRoomSpawn> AbundantRooms { get; } = BuildAbundantRooms();
    public static IReadOnlyList<ExtractionModuleSpawn> ModuleSpawns { get; } = BuildModuleSpawns(Rooms);
    public static IReadOnlyList<ExtractionModuleSpawn> AbundantModuleSpawns { get; } = BuildModuleSpawns(AbundantRooms);

    public static bool IsSupportedManifest(ModularDungeonDeployment.Manifest manifest) => manifest is
        { InstallationId: "modular-dungeon-v17-final-felucca-5256-808", BlueprintSha256: BlueprintSha256, StaticCount: 21020 } or
        { InstallationId: "modular-dungeon-v35-final-felucca-5256-808", BlueprintSha256: AbundantBlueprintSha256, StaticCount: 22334 };

    public static IReadOnlyList<ExtractionRoomSpawn> RoomsFor(ModularDungeonDeployment.Manifest manifest)
    {
        if (!IsSupportedManifest(manifest))
        {
            throw new ArgumentException("The extraction layout requires a pinned installed dungeon.", nameof(manifest));
        }

        return manifest.BlueprintSha256 == AbundantBlueprintSha256 ? AbundantRooms : Rooms;
    }

    private static IReadOnlyList<ExtractionRoomSpawn> BuildAbundantRooms()
    {
        var rooms = new ExtractionRoomSpawn[Rooms.Count];
        for (var index = 0; index < rooms.Length; index++)
        {
            rooms[index] = Rooms[index];
        }

        rooms[1] = rooms[1] with
        {
            Creatures =
            [
                new(ExtractionCreatureKind.Gazer, new(5290, 818, 0), 3),
                new(ExtractionCreatureKind.Skeleton, new(5301, 820, 0), 3),
                new(ExtractionCreatureKind.Gazer, new(5285, 828, 0), 3),
                new(ExtractionCreatureKind.Skeleton, new(5285, 824, 0), 3),
                new(ExtractionCreatureKind.Gazer, new(5284, 824, 25), 3)
            ]
        };
        rooms[5] = rooms[5] with
        {
            Creatures =
            [
                new(ExtractionCreatureKind.Skeleton, new(5266, 845, 0), 3),
                new(ExtractionCreatureKind.DireWolf, new(5272, 852, 0), 3),
                new(ExtractionCreatureKind.Skeleton, new(5276, 838, 0), 3),
                new(ExtractionCreatureKind.DireWolf, new(5264, 851, 0), 3)
            ]
        };
        return Array.AsReadOnly(rooms);
    }

    private static IReadOnlyList<ExtractionModuleSpawn> BuildModuleSpawns(IReadOnlyList<ExtractionRoomSpawn> rooms)
    {
        var ordered = new ExtractionModuleSpawn[GridSize * GridSize];
        var index = 0;

        foreach (var room in rooms)
        {
            if (room.IsOuter)
            {
                ordered[index++] = new(room.ModuleId, room.PlayerStart);
            }
        }

        foreach (var room in rooms)
        {
            if (!room.IsOuter)
            {
                ordered[index++] = new(room.ModuleId, room.PlayerStart);
            }
        }

        return Array.AsReadOnly(ordered);
    }
}
