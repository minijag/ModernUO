using System;
using System.IO;
using Server.Engines.ModularDungeon;
using Server.Engines.PvpEvents;
using Server.Logging;
using Server.Mobiles;

namespace Server.Engines.TownStruggle;

public static class TownStruggleRuntime
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(TownStruggleRuntime));
    public static TownStruggleController Controller { get; private set; }

    public static void Configure()
    {
        CommandSystem.Register("TownStruggle", AccessLevel.Player, args => PvpEventsGump.DisplayTo(args.Mobile));
        CommandSystem.Register("TownStruggleAdmin", AccessLevel.Administrator, Admin);
    }

    public static bool TryGetParticipant(Mobile player, out TownStruggleParticipant participant)
    {
        participant = Controller is { Deleted: false } ? Controller.GetParticipant(player) : null;
        return participant is { Deleted: false, Returned: false };
    }
    public static bool IsEnrolled(Mobile player) => TryGetParticipant(player, out _);
    public static bool IsGuardExempt(Mobile player) => IsEnrolled(player) ||
        Controller is { Deleted: false, Phase: TownStrugglePhase.Results } controller && controller.Now < controller.ResultsDeadline &&
        controller.GetParticipant(player) is { Disqualified: false, Team: not TownStruggleTeam.Neutral } &&
        player is { Deleted: false } && controller.Venue.Contains(player);

    internal static void Restore(TownStruggleController controller)
    {
        if (Controller is { Deleted: false } && Controller != controller)
        {
            _logger.Error("Duplicate Town Struggle controllers require staff recovery; no replacement was created");
            return;
        }
        try
        {
            Controller = controller;
            controller.Bind(new Host());
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Town Struggle could not restore its approved venue");
        }
    }

    internal static void Removed(TownStruggleController controller)
    {
        if (Controller == controller) { Controller = null; }
    }

    internal static void UpdateMarkers(TownStruggleController controller)
    {
        foreach (var marker in controller.Markers)
        {
            if (marker is { Deleted: false }) { marker.UpdateAppearance(); }
        }
    }

    internal static bool TryInstall(string json, out TownStruggleController controller, out string error)
    {
        controller = null;
        if (Controller is { Deleted: false })
        {
            error = "Town Struggle is already installed; its controller will not be replaced.";
            return false;
        }

        TownStruggleController created = null;
        try
        {
            var venues = TownStruggleVenue.ParseCatalog(json);
            foreach (var venue in venues)
            {
                venue.ValidateTerrain();
            }
            created = new TownStruggleController();
            Controller = created;
            created.ConfigureVenue(json, new Host());
            for (var zone = 0; zone < 3; zone++)
            {
                for (var corner = 0; corner < 5; corner++)
                {
                    var marker = new TownStruggleControlMarker(created, zone, corner);
                    created.AddMarker(marker);
                    marker.MoveToWorld(marker.GetLocation(venues[0]), venues[0].Map);
                }
            }
            controller = created;
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            created?.Delete();
            _logger.Error(exception, "Town Struggle install refused");
            error = exception.Message;
            return false;
        }
    }

    private static void Admin(CommandEventArgs args)
    {
        var action = args.Length >= 1 ? args.GetString(0).ToLowerInvariant() : "status";
        if (action == "install")
        {
            if (Controller is { Deleted: false })
            {
                args.Mobile.SendMessage("Town Struggle is already installed; its controller will not be replaced.");
                return;
            }
            var path = Path.Combine(Core.BaseDirectory, "Data", "TownStruggle", "venues.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 32768)
            {
                args.Mobile.SendMessage("Create an approved Data/TownStruggle/venues.json before installing the event.");
                return;
            }
            args.Mobile.SendMessage(TryInstall(File.ReadAllText(path), out _, out var installError)
                ? "Town Struggle installed, dormant. Use [PvpRotation enable after both events are idle, or [TownStruggleAdmin signup for a manual round."
                : installError);
            return;
        }
        if (Controller is not { Deleted: false } controller)
        {
            args.Mobile.SendMessage("Town Struggle is not installed. Usage: [TownStruggleAdmin install | signup | abort | status");
            return;
        }
        switch (action)
        {
            case "loot":
                if (args.Length != 2 || args.GetString(1) is not ("on" or "off"))
                {
                    args.Mobile.SendMessage("Usage: [TownStruggleAdmin loot on | off. Off keeps all gear; on enables selective gear loot.");
                }
                else
                {
                    args.Mobile.SendMessage(controller.TrySetSelectiveLoot(args.GetString(1) == "on", out var lootError)
                        ? "The isolated Town Struggle loot rule has been updated." : lootError);
                }
                break;
            case "signup":
                args.Mobile.SendMessage(controller.TryOpenSignup(out var signupError) ? "Town Struggle signup is open for 15 minutes." : signupError);
                break;
            case "abort":
                args.Mobile.SendMessage(controller.TryAbort(out var abortError) ? "Town Struggle safely returned its participants; no rewards were issued." : abortError);
                break;
            default:
                args.Mobile.SendMessage($"Town Struggle: {controller.Phase}, round {controller.RoundNumber}, {controller.Participants.Count} recorded participants.");
                if (controller.FaultMessage != null) { args.Mobile.SendMessage(controller.FaultMessage); }
                break;
        }
    }

    private sealed class Host : ITownStruggleHost
    {
        private TownStrugglePhase _lastPhase;
        private int _lastRound;
        public DateTime Now => Core.Now;
        public bool Eligible(Mobile player)
        {
            if (player is not PlayerMobile { Deleted: false, Alive: true, NetState: not null, AccessLevel: AccessLevel.Player } mobile ||
                player.Map == null || player.Map == Map.Internal || player.Mounted || player.Followers > 0 ||
                player.Holding != null || player.HasTrade || player.Spell != null || player.Combatant != null || mobile.DuelContext != null)
            {
                return false;
            }
            foreach (var aggression in player.Aggressors)
            {
                if (Now - aggression.LastCombatTime < TimeSpan.FromSeconds(30)) { return false; }
            }
            foreach (var aggression in player.Aggressed)
            {
                if (Now - aggression.LastCombatTime < TimeSpan.FromSeconds(30)) { return false; }
            }
            return true;
        }

        public bool AtNeutralBase(Mobile player, TownStruggleVenue venue) => player.Map == venue.Map && player.InRange(venue.NeutralBase, 10);

        public bool MoveOutside(Mobile player, TownStruggleVenue venue, Point3D requestedPoint, Map requestedMap)
        {
            if (player is not { Deleted: false }) { return false; }
            bool Allowed(Point3D point, Map map) => map != null && map != Map.Internal &&
                (map != venue.Map || !venue.Area.Contains(point)) &&
                (map != Map.Felucca || !ExtractionDungeonLayout.LobbyBounds.Contains(point));
            bool TryPoint(Point3D point, Map map)
            {
                if (!Allowed(point, map) || point.X < 0 || point.Y < 0 || point.X >= map.Width || point.Y >= map.Height ||
                    !map.CanFit(point, 16, false, player.NetState != null, true)) { return false; }
                MoveExactly(player, point, map);
                return true;
            }
            for (var radius = 0; radius <= 2; radius++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) { continue; }
                        if (TryPoint(new Point3D(requestedPoint.X + dx, requestedPoint.Y + dy, requestedPoint.Z), requestedMap)) { return true; }
                    }
                }
            }
            // A town-based signup origin may itself lie inside this round's battlefield.
            var area = venue.Area;
            var x = Math.Clamp(requestedPoint.X, area.Start.X, area.End.X - 1);
            var y = Math.Clamp(requestedPoint.Y, area.Start.Y, area.End.Y - 1);
            var sides = new[]
            {
                new Point2D(area.Start.X - 1, y), new Point2D(area.End.X, y),
                new Point2D(x, area.Start.Y - 1), new Point2D(x, area.End.Y)
            };
            Array.Sort(sides, (a, b) =>
                (Math.Abs(a.X - x) + Math.Abs(a.Y - y)).CompareTo(Math.Abs(b.X - x) + Math.Abs(b.Y - y)));
            foreach (var edge in sides)
            {
                var vertical = edge.X < area.Start.X || edge.X >= area.End.X;
                var direction = vertical ? (edge.X < area.Start.X ? -1 : 1) : (edge.Y < area.Start.Y ? -1 : 1);
                for (var depth = 0; depth < 6; depth++)
                {
                    for (var delta = 0; delta <= 32; delta++)
                    {
                        var offset = delta % 2 == 0 ? delta / 2 : -(delta + 1) / 2;
                        var cx = edge.X + (vertical ? direction * depth : offset);
                        var cy = edge.Y + (vertical ? offset : direction * depth);
                        if (cx < 0 || cy < 0 || cx >= venue.Map.Width || cy >= venue.Map.Height) { continue; }
                        if (TryPoint(new Point3D(cx, cy, venue.Map.GetAverageZ(cx, cy)), venue.Map)) { return true; }
                    }
                }
            }
            return false;
        }

        private static void MoveExactly(Mobile player, Point3D point, Map map)
        {
            if (player.NetState == null)
            {
                player.LogoutMap = map;
                player.LogoutLocation = point;
                player.Internalize();
            }
            else { player.MoveToWorld(point, map); }
        }

        public bool Move(Mobile player, Point3D point, Map map)
        {
            if (player == null || player.Deleted || map == null || map == Map.Internal) { return false; }
            for (var radius = 0; radius <= 2; radius++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) { continue; }
                        var candidate = new Point3D(point.X + dx, point.Y + dy, point.Z);
                        if (!map.CanFit(candidate, 16, false, player.NetState != null, true)) { continue; }
                        MoveExactly(player, candidate, map);
                        return true;
                    }
                }
            }
            return false;
        }

        public void Notify(TownStruggleController controller)
        {
            if (controller.Phase == TownStrugglePhase.Signup && (_lastPhase != controller.Phase || _lastRound != controller.RoundNumber))
            {
                World.Broadcast(0x35, true, $"Town Struggle signup is open! Fight for {controller.Venue.Name}. Use [TownStruggle to join; teams form in 15 minutes.");
            }
            _lastPhase = controller.Phase;
            _lastRound = controller.RoundNumber;
            if (controller.Phase is TownStrugglePhase.Active or TownStrugglePhase.Results or TownStrugglePhase.Faulted && controller.Participants.Count > 0)
            {
                controller.RegisterRegion();
            }
            else { controller.ReleaseRegion(); }
            UpdateMarkers(controller);
            foreach (var participant in controller.Participants)
            {
                if (participant.Player is { Deleted: false, NetState: not null } player && !participant.Returned)
                {
                    player.SendMessage($"Town Struggle: {controller.Phase}. Use [TownStruggle for zones, scores and respawning.");
                }
            }
        }
    }
}
