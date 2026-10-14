using System.Text.Json.Serialization;
using Server.Mobiles;

namespace Server.Regions;

public enum ProtectedInteraction
{
    Combat,
    Snooping,
    Stealing,
    CorpseLooting
}

public class ProtectedRegion : BaseRegion
{
    [JsonConstructor]
    public ProtectedRegion(string name, Map map, int priority, params Rectangle3D[] area)
        : base(name, map, priority, area)
    {
    }

    public bool PreventPlayerCombat { get; set; } = true;
    public bool PreventPlayerSnooping { get; set; } = true;
    public bool PreventPlayerStealing { get; set; } = true;
    public bool PreventPlayerCorpseLooting { get; set; } = true;

    public override bool AllowHarmful(Mobile from, Mobile target) =>
        !PreventsPlayerHarm(from, target) && base.AllowHarmful(from, target);

    public override bool OnCombatantChange(Mobile mobile, Mobile old, Mobile next) =>
        !PreventsPlayerHarm(mobile, next) && base.OnCombatantChange(mobile, old, next);

    public static bool PreventsPlayerHarm(Mobile source, Mobile target) =>
        ProtectsInteraction(source, target, ProtectedInteraction.Combat);

    public static bool ProtectsInteraction(Mobile source, Mobile target, ProtectedInteraction interaction)
    {
        var sourceOwner = GetPlayerOwner(source);
        var targetOwner = GetPlayerOwner(target);
        return IsPlayerInteraction(source, target, sourceOwner, targetOwner) &&
            (IsProtected(source.Location, source.Map, interaction) ||
             IsProtected(target.Location, target.Map, interaction) ||
             IsProtected(sourceOwner.Location, sourceOwner.Map, interaction) ||
             IsProtected(targetOwner.Location, targetOwner.Map, interaction));
    }

    public static bool ProtectsInteraction(
        Mobile source, Mobile target, ProtectedInteraction interaction, Point3D targetLocation, Map targetMap
    )
    {
        var sourceOwner = GetPlayerOwner(source);
        var targetOwner = GetPlayerOwner(target);
        // A corpse stays protected by its own location, not by its resurrected owner's later location.
        return IsPlayerInteraction(source, target, sourceOwner, targetOwner) &&
            (IsProtected(source.Location, source.Map, interaction) ||
             IsProtected(sourceOwner.Location, sourceOwner.Map, interaction) ||
             IsProtected(targetLocation, targetMap, interaction));
    }

    public static Mobile GetPlayerOwner(Mobile mobile)
    {
        for (var depth = 0; mobile != null && depth < 16; depth++)
        {
            if (mobile.Player)
            {
                return mobile;
            }
            mobile = (mobile as BaseCreature)?.GetMaster();
        }
        return null;
    }

    private static bool IsPlayerInteraction(Mobile source, Mobile target, Mobile sourceOwner, Mobile targetOwner) =>
        sourceOwner != null && targetOwner != null && sourceOwner != targetOwner &&
        source.AccessLevel == AccessLevel.Player && target.AccessLevel == AccessLevel.Player &&
        sourceOwner.AccessLevel == AccessLevel.Player && targetOwner.AccessLevel == AccessLevel.Player;

    private static bool IsProtected(Point3D location, Map map, ProtectedInteraction interaction)
    {
        if (map == null || map == Map.Internal)
        {
            return false;
        }
        // House and other dynamic overlays can outrank a configured protected region without inheriting it.
        var regions = map.GetSector(location).Regions;
        for (var index = 0; index < regions.Count; index++)
        {
            var region = regions[index];
            if (!region.Contains(location))
            {
                continue;
            }
            for (; region != null; region = region.Parent)
            {
                if (region is ProtectedRegion protectedRegion && protectedRegion.Prevents(interaction))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool Prevents(ProtectedInteraction interaction) => interaction switch
    {
        ProtectedInteraction.Combat => PreventPlayerCombat,
        ProtectedInteraction.Snooping => PreventPlayerSnooping,
        ProtectedInteraction.Stealing => PreventPlayerStealing,
        ProtectedInteraction.CorpseLooting => PreventPlayerCorpseLooting,
        _ => false
    };
}
