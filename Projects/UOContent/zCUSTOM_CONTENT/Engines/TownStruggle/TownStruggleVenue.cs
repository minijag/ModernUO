using System;
using System.Collections.Generic;
using System.Text.Json;
using Server.Regions;

namespace Server.Engines.TownStruggle;

public sealed record TownStruggleVenue(
    string Name, Map Map, Rectangle2D Area, Point3D NeutralBase, Point3D OrderBase,
    Point3D ChaosBase, IReadOnlyList<Point3D> CapturePoints, IReadOnlyList<Point3D> RespawnPoints)
{
    public const int BaseRadius = 5;
    public const int CaptureRadius = 5;

    public bool Contains(Mobile player) => player.Map == Map && Area.Contains(player.Location);
    public bool IsAtBase(Mobile player) => player.Map == Map &&
        (player.InRange(OrderBase, BaseRadius) || player.InRange(ChaosBase, BaseRadius));
    public Point3D TeamBase(TownStruggleTeam team) => team == TownStruggleTeam.Order ? OrderBase : ChaosBase;

    public static TownStruggleVenue[] ParseCatalog(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 32768) { throw new ArgumentException("Invalid venue catalog size."); }
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("venues", out var list)) { return [Parse(json)]; }
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 16)
        {
            throw new ArgumentException("Configure between one and sixteen town venues.");
        }
        var venues = new TownStruggleVenue[list.GetArrayLength()];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < venues.Length; i++)
        {
            venues[i] = Parse(list[i].GetRawText());
            if (!names.Add(venues[i].Name) || i > 0 &&
                (venues[i].Map != venues[0].Map || venues[i].NeutralBase != venues[0].NeutralBase))
            {
                throw new ArgumentException("Town names must be unique and share a neutral signup base.");
            }
        }
        return venues;
    }

    public static TownStruggleVenue Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 32768)
        {
            throw new ArgumentException("A bounded, explicit venue configuration is required.");
        }
        var input = JsonSerializer.Deserialize<VenueFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (input == null || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 60 ||
            input.MapId < 0 || input.MapId >= Map.Maps.Length || Map.Maps[input.MapId] is not { } map || map == Map.Internal ||
            input.Width < 40 || input.Height < 40 || input.Width > 512 || input.Height > 512 ||
            input.X < 0 || input.Y < 0 || input.X + input.Width > map.Width || input.Y + input.Height > map.Height ||
            input.CapturePoints?.Length != 3 || input.RespawnPoints?.Length != 3 ||
            input.NeutralBase == null || input.OrderBase == null || input.ChaosBase == null)
        {
            throw new ArgumentException("Invalid venue: name, map, bounds, three capture points and three respawn points are required.");
        }
        if ((map.Rules & MapRules.HarmfulRestrictions) != 0)
        {
            throw new ArgumentException("Town Struggle requires an unrestricted PvP facet for native selective corpse looting.");
        }
        var captures = Array.ConvertAll(input.CapturePoints, Point);
        var respawns = Array.ConvertAll(input.RespawnPoints, Point);
        var venue = new TownStruggleVenue(input.Name, map, new Rectangle2D(input.X, input.Y, input.Width, input.Height),
            Point(input.NeutralBase), Point(input.OrderBase), Point(input.ChaosBase), Array.AsReadOnly(captures), Array.AsReadOnly(respawns));
        var points = new HashSet<Point3D>();
        foreach (var p in venue.BattlePoints())
        {
            if (!venue.Area.Contains(p) || !points.Add(p))
            {
                throw new ArgumentException("Battle points must be unique and inside the event area.");
            }
        }
        for (var i = 0; i < captures.Length; i++)
        {
            foreach (var p in venue.BattlePoints())
            {
                if (p != captures[i] && Math.Abs(p.X - captures[i].X) <= 12 && Math.Abs(p.Y - captures[i].Y) <= 12)
                {
                    throw new ArgumentException("Capture zones must be separated from other zones and team bases.");
                }
            }
        }
        if (Math.Abs(venue.OrderBase.X - venue.ChaosBase.X) <= 12 && Math.Abs(venue.OrderBase.Y - venue.ChaosBase.Y) <= 12)
        {
            throw new ArgumentException("Team bases must not overlap.");
        }
        return venue;
    }

    public void ValidateTerrain()
    {
        var surveyed = new HashSet<Region>();
        for (var x = Area.Start.X; x < Area.End.X + 16; x += 16)
        {
            for (var y = Area.Start.Y; y < Area.End.Y + 16; y += 16)
            {
                foreach (var region in Map.GetSector(Math.Min(x, Map.Width - 1), Math.Min(y, Map.Height - 1)).Regions)
                {
                    for (var current = region; current != null && surveyed.Add(current); current = current.Parent)
                    {
                        if (current is not ProtectedRegion { PreventPlayerCombat: true }) { continue; }
                        foreach (var bounds in current.Area)
                        {
                            if (bounds.Start.X < Area.End.X && bounds.End.X > Area.Start.X &&
                                bounds.Start.Y < Area.End.Y && bounds.End.Y > Area.Start.Y)
                            {
                                throw new ArgumentException("An event area cannot overlap a protected newbie region.");
                            }
                        }
                    }
                }
            }
        }
        foreach (var p in BattlePoints())
        {
            ValidatePoint(p);
        }
        ValidatePoint(NeutralBase);
        foreach (var capture in CapturePoints)
        {
            for (var x = capture.X - CaptureRadius; x <= capture.X + CaptureRadius; x++)
            {
                for (var y = capture.Y - CaptureRadius; y <= capture.Y + CaptureRadius; y++)
                {
                    if (!Area.Contains(new Point2D(x, y)))
                    {
                        throw new ArgumentException("A capture zone extends outside the battlefield.");
                    }
                }
            }
        }
    }

    private void ValidatePoint(Point3D point)
    {
        if (!Map.CanFit(point, 16, false, false, true))
        {
            throw new ArgumentException($"Venue point {point} is not walkable with player headroom.");
        }
        foreach (var region in Map.GetSector(point).Regions)
        {
            if (region is ProtectedRegion { PreventPlayerCombat: true } && region.Contains(point))
            {
                throw new ArgumentException("Town Struggle cannot be installed over a protected newbie area.");
            }
        }
    }

    private IEnumerable<Point3D> BattlePoints()
    {
        yield return OrderBase;
        yield return ChaosBase;
        foreach (var p in CapturePoints) { yield return p; }
        foreach (var p in RespawnPoints) { yield return p; }
    }

    private static Point3D Point(Coordinate p) => p == null || p.Z is < -128 or > 127
        ? throw new ArgumentException("Invalid venue coordinate.") : new Point3D(p.X, p.Y, p.Z);
    public sealed record Coordinate(int X, int Y, int Z);
    public sealed record VenueFile(string Name, int MapId, int X, int Y, int Width, int Height,
        Coordinate NeutralBase, Coordinate OrderBase, Coordinate ChaosBase, Coordinate[] CapturePoints, Coordinate[] RespawnPoints);
}
