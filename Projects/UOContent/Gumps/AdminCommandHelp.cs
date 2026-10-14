using System;
using System.Collections.Generic;

namespace Server.Gumps;

internal static class AdminCommandHelp
{
    internal const int FirstInfoButton = 100000;
    internal sealed record Entry(int Index, AdminGumpPage Page, string Title, string Command, string Behavior,
        string Scope, string Caution, AccessLevel HelpAccess = AccessLevel.Administrator,
        AccessLevel CommandAccess = AccessLevel.Administrator);

    private const string WorldCaution = "Back up the world first. This changes live world objects, not client map files. There is no automatic pre-save or undo; a later world save persists the result. Stock generation can replace or conflict with custom content.";
    private const string EditCaution = "Changes affect the live world and are retained by a later world save. Inspect the target carefully; this information window does not run the command.";
    private static readonly Dictionary<int, Entry> _entries = BuildEntries();
    internal static IEnumerable<Entry> Entries => _entries.Values;

    internal static bool IsAdministerPage(AdminGumpPage page) => page is AdminGumpPage.Administer or
        AdminGumpPage.Administer_WorldBuilding or AdminGumpPage.Administer_Server or AdminGumpPage.Administer_Access or
        AdminGumpPage.Administer_Access_Lockdown or AdminGumpPage.Administer_Commands;

    internal static int InfoButton(int index) => FirstInfoButton + index;
    internal static int ActionButton(int index) => 1 + index * 11 + 3;
    internal static bool TryGet(int index, out Entry entry) => _entries.TryGetValue(index, out entry);

    internal static bool CanRead(Mobile from, AdminGumpPage page, Entry entry) =>
        from is { Deleted: false } && entry != null && from.AccessLevel >= entry.HelpAccess && IsAdministerPage(page) &&
        (entry.Index < 4 || entry.Page == page);

    internal static bool TryGetAction(int buttonId, AdminGumpPage page, Mobile from, out Entry entry)
    {
        entry = null;
        return buttonId > 0 && buttonId < FirstInfoButton && (buttonId - 1) % 11 == 3 &&
            TryGet((buttonId - 1) / 11, out entry) && CanRead(from, page, entry);
    }

    private static Dictionary<int, Entry> BuildEntries()
    {
        var entries = new Dictionary<int, Entry>();
        void Add(int index, AdminGumpPage page, string title, string command, string behavior, string scope, string caution,
            AccessLevel helpAccess = AccessLevel.Administrator, AccessLevel commandAccess = AccessLevel.Administrator) =>
            entries.Add(index, new Entry(index, page, title, command, behavior, scope, caution, helpAccess, commandAccess));
        void World(int index, string title, string command, string behavior, string scope) =>
            Add(index, AdminGumpPage.Administer_WorldBuilding, title, command, behavior, scope, WorldCaution,
                commandAccess: AccessLevel.Developer);
        void Edit(int index, string title, string command, string behavior, string scope, string caution = EditCaution) =>
            Add(index, AdminGumpPage.Administer_Commands, title, command, behavior, scope, caution);
        void Access(int index, string title, string command, string behavior, string scope, string caution,
            AccessLevel minimum = AccessLevel.Administrator) =>
            Add(index, AdminGumpPage.Administer_Access, title, command, behavior, scope, caution, minimum, minimum);

        Add(0, AdminGumpPage.Administer, "World Building", "Opens the World Building category", "Stock generators create or rebuild world objects such as teleporters, decorations and dungeon systems.", "Multiple locations and facets; individual generators need Developer access.", "Read each generator's information before use. These are not small local building tools and can overwrite custom world objects.");
        Add(1, AdminGumpPage.Administer, "Server", "Opens the Server category", "Save the world, shut down or restart the process, and broadcast messages.", "The running shard and its connected players.", "Shutdown buttons act immediately unless explicitly marked with a delay. Read the save and restart distinctions first.");
        Add(2, AdminGumpPage.Administer, "Access", "Opens the Access category", "Disconnect or ban players, block addresses, change character access levels, and configure server lockdown.", "Targeted characters, accounts or addresses; lockdown is server-wide.", "Access changes can remove a player's ability to connect or grant powerful staff permissions.");
        Add(3, AdminGumpPage.Administer, "Commands", "Opens the Commands category", "Open targeted tools for adding, inspecting, modifying, moving or deleting live objects.", "A chosen item, mobile, location or rectangle, depending on the command.", "The arrow runs the command; Info only explains it. Some tools are destructive without a confirmation or undo.");

        World(101, "Teleporters", "TelGen", "Reads Data/teleporters.json and creates its routes. Existing ordinary teleporters at configured endpoints within 12 Z are replaced; keyword and skill teleporters are excluded. Entries marked back also get reverse routes.", "Configured endpoints in the JSON file; no targeting step.");
        World(104, "Doors", "DoorGen", "Scans configured map regions and native doorframes, then creates usable world doors. Large scans can take time.", "Configured regions on Felucca, Trammel, Ilshenar and Malas; this does not edit native map files.");
        World(106, "Decorations", "Decorate", "Adds stock decoration from configuration files and may replace conflicting doors, lights or matching-art objects of another type. Exact matches may be reused.", "Configured Britannia/Felucca, Trammel, Ilshenar, Malas and Tokuno decoration sets, including enabled era-dependent objects.");
        World(112, "Doom", "GenLeverPuzzle; GenGauntlet", "Runs both Doom generators. The lever puzzle skips an existing controller; the gauntlet generator removes and rebuilds its fixtures, spawners, NPCs and gate.", "Doom's configured lever puzzle and gauntlet areas.");

        Add(200, AdminGumpPage.Administer_Server, "Save", "Save", "Requests a world save without shutting down the shard.", "World objects, characters and other registered persistence data.", "This commits current live changes; it does not restore or undo edits. Wait for the server's save completion before assuming the files are durable.");
        foreach (var (index, title, restart, save) in new[]
        {
            (201, "Shutdown (With Save)", false, true), (202, "Shutdown (Without Save)", false, false),
            (203, "Shutdown & Restart (With Save)", true, true), (204, "Shutdown & Restart (Without Save)", true, false)
        })
        {
            Add(index, AdminGumpPage.Administer_Server, title, "Admin shutdown handler",
                save ? "Invokes Save, waits for world-file writing to finish, then stops the server process." :
                    "Waits for any world-file writing already in progress, then stops the server process without requesting a new save.",
                restart ? "All connected players are disconnected; a replacement server process is requested." :
                    "All connected players are disconnected. This button does not request a restart.",
                save ? "Immediate operation: no confirmation or countdown. Notify players first. Restart depends on the server process environment." :
                    "Immediate operation: no confirmation or countdown. All unsaved changes can be lost. Restart does not recover unsaved work.");
        }
        Add(205, AdminGumpPage.Administer_Server, "Shutdown (With 15m Delay & Save)", "Admin ShutdownTimer", "Starts a 15-minute countdown, then disables automatic saves and invokes shutdown with a final save. Countdown messages go only to the administrator who clicked this button.", "The entire shard; it stops without requesting a restart.", "Notify players separately using Broadcast. This page has no cancel-countdown control. Repeated clicks can schedule multiple countdowns.");
        Add(210, AdminGumpPage.Administer_Server, "To Everyone", "BC <broadcast text>", "Sends the trimmed text from the Broadcast field to connected players. Empty text is rejected.", "Everyone currently connected.", "The message is sent immediately. Info does not broadcast the field's contents.");
        Add(211, AdminGumpPage.Administer_Server, "To Staff", "SM <broadcast text>", "Sends the trimmed Broadcast field as a staff message, prefixed with your name. Empty text is rejected.", "Connected staff at Counselor access or higher.", "The message is sent immediately. Info does not broadcast the field's contents.");

        Access(300, "Kick", "Kick", "Opens a target cursor to disconnect a connected player with lower character access than yours.", "One targeted player; command access checks still apply.", "Kicking does not ban an account; the player can reconnect if otherwise allowed.");
        Access(301, "Ban", "Ban", "Targets an online lower-access player, disconnects them and immediately marks their entire account banned. The ban-duration window opens after the ban is already applied.", "The targeted player's account, not just the one character.", "Closing the duration window does not cancel the ban; it leaves an unspecified ban. Confirm the identity and intended duration carefully.");
        Access(302, "Firewall", "Firewall", "Targets a connected player and adds the connection's current IP address to the persistent firewall list. It does not itself kick the player or ban the account.", "An IP address, potentially shared by more than one account.", "The block remains until removed and can affect unrelated users sharing the address. Review the Firewall section to manage blocked entries.");
        Access(303, "Lockdown", "Opens Server Lockdown", "Opens the minimum-access settings and Purge Invalid Clients control. Opening the page does not enable lockdown.", "Server-wide admission restrictions.", "Choosing a level and purging existing clients are separate actions.");
        foreach (var (index, title, level, minimum) in new[]
        {
            (310, "Make Player", "Player", AccessLevel.Administrator),
            (311, "Make Counselor", "Counselor", AccessLevel.Administrator),
            (312, "Make Game Master", "GameMaster", AccessLevel.Administrator),
            (313, "Make Seer", "Seer", AccessLevel.Administrator),
            (314, "Make Administrator", "Administrator", AccessLevel.Developer),
            (315, "Make Developer", "Developer", AccessLevel.Owner),
            (316, "Make Owner", "Owner", AccessLevel.Owner)
        })
        {
            Access(index, title, $"Set AccessLevel {level}", $"Opens a target cursor to set the target mobile's AccessLevel to {level}.",
                "One targeted character/mobile, not a bulk account-role change. Property and target access checks still apply.",
                "Staff access grants powerful commands. Confirm the target before granting or removing privileges; a later save persists the change.", minimum);
        }

        Edit(400, "Add", "Add", "Opens the searchable object-creation menu. Choosing a constructible type continues the placement workflow.", "Creates an item or mobile of the selected type at a chosen location.");
        Edit(401, "Remove", "Remove", "Targets an item or nonplayer mobile for deletion.", "One targeted deletable world object; players are protected.", "Destructive: no automatic undo. Removing a container can remove its contents. Back up important content first.");
        Edit(402, "Dupe", "Dupe", "Targets an item and creates one duplicate, placed in your backpack or at your location when you have no backpack. Constructible container contents are recursively copied.", "One source item; this is not a mobile-cloning tool.");
        Edit(403, "Dupe in bag", "DupeInBag", "Creates one duplicate in the source item's container, or its owning mobile's backpack. Constructible container contents are recursively copied.", "One source item and the source's parent container/mobile, if any.", "Check the destination container before duplicating. For ground sources, the copy appears at your feet, not at the source.");
        Edit(404, "Properties", "Props", "Targets an item or mobile and opens its property inspector/editor.", "One target, with property read/write permissions enforced by the editor.", "Opening the inspector is read-only; changing a value can immediately alter behavior. A later save persists edits.");
        Edit(405, "Skills", "Skills", "Targets a mobile and opens its skill editor.", "One mobile's skill values and related editable settings.", "Opening the editor does not itself change skills. Skill edits affect gameplay and are retained by a later save.");
        Edit(406, "Mortal", "Set Blessed False", "Opens a target cursor to clear the target's Blessed property.", "A mobile or another target exposing an editable Blessed property.", "For mobiles this removes blessed/invulnerable protection; it is not a permanent-death rule. Verify the target type.");
        Edit(407, "Immortal", "Set Blessed True", "Opens a target cursor to enable the target's Blessed property.", "A mobile or another target exposing an editable Blessed property.", "For mobiles this grants blessed/invulnerable protection. It does not resurrect a dead mobile or grant staff access.");
        Edit(408, "Squelch", "Set Squelched True", "Opens a target cursor to enable the mobile's Squelched property, preventing speech.", "One targeted mobile.");
        Edit(409, "Unsquelch", "Set Squelched False", "Opens a target cursor to clear the mobile's Squelched property, allowing speech again.", "One targeted mobile.");
        Edit(412, "Hide", "Set Hidden True", "Opens a target cursor to set the mobile's Hidden property.", "One targeted mobile, not you automatically.", "Hiding is not deletion or invulnerability; visibility rules and later actions can reveal the mobile.");
        Edit(413, "Unhide", "Set Hidden False", "Opens a target cursor to clear the mobile's Hidden property.", "One targeted mobile.", "This reveals the mobile; it does not change staff access or blessed status.");
        Edit(414, "Kill", "Kill", "Targets a living, damageable player or NPC and invokes its normal kill behavior. Blessed or otherwise non-damageable targets are blocked.", "One targeted mobile, subject to command checks.", "Destructive gameplay action: normal death, corpse, loot and other death hooks can apply. This is not Remove and has no automatic undo.");
        Edit(415, "Resurrect", "Resurrect", "Targets a ghost or dead bonded pet and invokes resurrection.", "One targeted mobile; it is not a corpse-loot recovery tool.", "Resurrection does not revive deleted NPCs, recreate deleted objects or automatically return items from a corpse.");
        Edit(416, "Move", "Move", "First targets the item or mobile to move, then asks for its destination on your current map.", "One target moved to a selected location; it is not copied.", "Check support, elevation and nearby blockers. Moving an active scripted object can trigger its location-change behavior.");
        Edit(417, "Wipe", "Wipe", "Asks for two bounding points, then deletes normal world items and nonplayer mobiles inside the XY rectangle at every elevation. BaseMulti objects and HouseSign objects are excluded.", "The selected rectangle on your facet; not terrain or native static-map tiles.", "Highly destructive: no confirmation or automatic undo. It can remove container contents and objects on other floors. Back up the world and verify both corners first.");
        Edit(418, "Teleport", "Tele", "Opens a location cursor and teleports your character to the chosen destination.", "Your character, not a targeted creature or group.", "Choose a safe, supported destination. This does not create a permanent teleporter or moongate.");
        Edit(419, "Teleport (Multiple)", "Multi Tele", "Runs Tele through the repeating Multi command interface, allowing successive destination selections.", "Your character, repeatedly; it does not teleport a group of players.", "Cancel the targeting cursor when finished. This does not create travel objects.");

        foreach (var (index, title, level) in new[]
        {
            (500, "Not Locked Down", "Player"), (501, "Counselors", "Counselor"),
            (502, "Game Masters", "Game Master"), (503, "Seers", "Seer"), (504, "Administrators", "Administrator")
        })
        {
            Add(index, AdminGumpPage.Administer_Access_Lockdown, title, $"Set server LockdownLevel to {level}",
                index == 500 ? "Removes the server lockdown minimum; normal login, ban and firewall checks still apply." :
                    $"Sets the minimum lockdown access level to {level}. Eligible access on the account or its characters can satisfy the admission check.",
                "The running shard's admission policy; this does not change anyone's staff role.",
                "The choice takes effect immediately, without disconnecting existing clients. Purge Invalid Clients is separate. Lockdown is a runtime setting, not a guaranteed persistent configuration edit.");
        }
        Add(510, AdminGumpPage.Administer_Access_Lockdown, "Purge Invalid Clients", "Admin lockdown purge", "While locked down, checks connected accounts and disconnects those with neither an account level nor a character level meeting the current minimum. Without lockdown, it does nothing.", "Connected clients with accounts; clients without an attached account are skipped.", "Immediate disconnection, without a confirmation. It does not ban accounts or delete characters; it enforces the current lockdown choice.");
        return entries;
    }
}
