# Shared gump template

The walnut-and-metal border and black backing are the default foundation for
all server-created gumps. Static, dynamic, and legacy compilation passes through
`StandardGumpStyle`, using the same `CustomGumpStyle` artwork as Help and the
leaderboard. Ordinary window backdrops are replaced automatically; specialized
foreground artwork, input fields, page navigation, and reply identifiers stay
intact. Already themed layouts are recognized and do not get a second frame.
Native client windows such as the paperdoll are not server-created gumps.

Use `CustomGumpStyle` directly when designing a layout or adding a title banner.
It supports both `DynamicGumpBuilder` and `StaticGumpBuilder`. Client gump art
60002–60009 must be installed.

For a new layout, specify the **outer** bounds and keep controls at least
`CustomGumpStyle.ContentPadding` (24 pixels) inside them:

The maximum design size for custom gumps is the `[stats` frame: 720 x 600 pixels
(`MaximumPanelWidth` / `MaximumPanelHeight`). Including the title ribbon, the full
footprint is 720 x 606 pixels. Use pagination rather than exceeding this size.

```csharp
protected override void BuildLayout(ref DynamicGumpBuilder builder)
{
    builder.AddPage();
    CustomGumpStyle.AddPanel(ref builder, 0, 0, 460, 340, "TITLE");
    // Content goes here.
    builder.AddButton(24, 292, 4005, 4007, 0);
    builder.AddHtml(60, 292, 150, 24, "CLOSE", CustomGumpStyle.ActionColor, 3);
}
```

For existing layouts, `AddContentPanel(ref builder, x, y, width, height)` treats
the bounds as the **content** rectangle and adds the frame outside it. Existing
control positions and button IDs do not need to change. Allow space around the
gump's screen position for the 24-pixel padding.

Pass the optional final `title` argument to either panel method for the centered
muted ochre-gold cloth banner (client gump 64040, 300 x 32). It protrudes 6 pixels above
the outer frame and ends 26 pixels below its top. Reserve the first 2 pixels of
the content rectangle beneath its center for the lower edge of the cloth.
Use a screen Y of at least 30 with `AddContentPanel` (our menus use 50), or at
least 6 with `AddPanel`. Titled frames must be at least 340 pixels wide.
Titles use centered espresso text (`BannerTitleColor`, #24180F); keep them short
enough for the 230-pixel text area. Body text colors are unchanged.
Artwork source and reproducible staging tool are in `../../AI/Tools/Server/GumpBanner`.

Use `TextColor` for HTML, `LocalizedTextColor` for localized HTML, and `TextHue`
for labels. These APIs use different color formats; do not interchange them.

The standard font is `CustomGumpStyle.StandardFontSize` (UO HTML setting 4),
shared by Help and all banner titles. Use it for new themed HTML text instead
of hardcoding a size:

```csharp
builder.AddHtml(24, 60, 300, 24, "Menu text", CustomGumpStyle.WhiteTextColor,
    CustomGumpStyle.StandardFontSize);
```

This is an HTML font setting, not an ASCII font index. Native labels and localized
text do not automatically inherit it. Existing compact layouts must be reviewed
for clipping before changing their text sizes.

Draw the panel on page 0 before controls. Do not add another background or alpha
region over it. The backing intentionally overlaps the inner rail edge, and the
corners/rails are drawn afterward. Rails tile at their native size so the rivets
are not stretched. The backing retains Help's three alpha commands; actual
transparency is client-dependent, not an exact configurable 90% opacity.

The shared foundation does not alter navigation, permissions, responses, or
server-side gump lifecycle. Gumps inherit it without opting in. Explicit calls
to the helper control precise frame bounds and the optional title banner.
Unformatted HTML uses light text on the black backing; text on retained native
artwork or input fields keeps its native contrast. HTML placeholders should use
`SetHtmlText` with an explicit appropriate color, not raw `SetStringSlot`.

## Help menu

The client Help button opens `HelpMenuGump`. The first row offers Website
(https://www.google.com/), Wiki (https://www.bing.com/), Discord
(https://www.discord.com/), and In-game Help. The second row offers Stats,
Rankings, and Enchantments. Links use `Mobile.LaunchBrowser` and reopen the hub;
the client handles opening the browser. These are placeholder URLs.

In-game Help retains the existing stuck/harassment/bug menu and support queue
checks through `HelpGumpCustom.RequestInGameHelp`. Queue restrictions do not gate
the other hub options. Stats opens the character sheet; Rankings opens the
existing leaderboard. `EnchantmentsGump` is an intentionally blank, framed
placeholder for future account enchantment purchases, with Back and Close
controls and no purchase logic. All hub/placeholder text uses the standard font,
white body text, and the shared walnut border/black backing. No custom shield
buttons are required.

`CustomGumpStyle.AddMenuButton` provides the reusable round walnut/brass button
for both static and dynamic gumps. Its text is a separate centered label above
the artwork, never baked into the button. Pass the horizontal center, label Y,
reply ID, and label. Labels are 132 x 24 pixels; the 56 x 56 button starts 28
pixels below the label. Native art 64042/64043 supplies normal/pressed states.
Artwork and the reproducible native packer live in `../../AI/Tools/Server/MenuButtonArt`.

## Leaderboards

`[leaderboard` and Help -> Rankings open the same 700 x 580 leaderboard. Native
ore, log, leather, map, gold, and skull item art identify its six categories;
no custom shield artwork is required. Ten players appear per page, with global
ranking numbers and Previous/Next controls when more players are ranked.
Show My Ranking jumps to the requesting player's page and highlights their row.
Players with no positive total are unranked. Each navigation action refreshes
the scores; opening or paging through the board never awards progress.

Leaderboard controls use the existing 29 x 29 gold-bordered diamond artwork:
gray idle (9721), blue pressed (2151), and checked blue (2154) for the selected
category. Only the active category stays checked; paging, Show My Ranking and
Close remain gray until pressed. Previous/Next captions identify paging without
arrow artwork. `CustomGumpStyle.AddOptionButton` supplies these states for dynamic
or static layouts without changing the round Help-menu buttons or other gumps.

Gold Farmed is the configured average gold value credited by monster kills,
not physical gold looted or held. New kill earnings credit the member's current
guild; historical personal totals are not replayed into a guild.

## PvP events

`[PvpEvents`, `[TownStruggle`, and the lobby's existing Town Struggle stone open
the shared 720 x 600 PvP Events board. It switches between Extraction and Town
Struggle, with Previous Event, Seasonal Stats, and Lifetime Stats views. Seasons
are UTC calendar quarters. Eight ranked players appear per page, and Show My Row
finds and highlights the requesting character. The standard gray/pressed-blue/
checked-blue diamond buttons indicate the selected event and view.

Previous Event contains only finalized recorded results. Town Struggle's final
team scores, rewards, K/A credits, deaths and abandonment penalties are retained;
its Battle Controls button preserves signup, deployment, leave and MVP voting.
Extraction ranks successful escapes and shows participant deaths and PvP kills.
No historical scores are fabricated. See `../../AI/Tools/Server/TownStruggle/PVP-EVENT-STATS.md`
for persistence paths, quarterly boundaries and finalization timing.

## Character sheet

`[stats` opens `CharacterStatsGump` for the requesting player only. It is movable,
singleton, and refreshed manually with its Refresh button. `[serverstats` retains
the Counselor-only server diagnostics formerly registered as `[stats`.

The bottom Melee / Spells controls use native sword/spellbook item art and
standard UO buttons. The selected tab is highlighted in gold;
Refresh preserves the selected tab. Both views share the 720 x 600 frame.
Refresh, Melee, modifier pagination, Spells, and Close share one footer row;
the complete artwork and text bounds stay inside the frame's content padding.

The combat overview shows weapon damage, swing interval,
nominal swings/minute, armor rating, and total damage/swing-rate gains. The lower
table pages through eight modifier rows at a time (effect, amount, source).
There are no health/stamina/mana totals, standalone attribute/skill list, weight,
followers, fame, or karma.

`CharacterCombatSummary` targets the configured T2A rules and refuses to show a
misleading breakdown under another era. Damage endpoints include the T2A flat
enchantment bonus before the weapon's `ScaleDamageOld(..., false)` call. The total
damage percentage compares the midpoint of the effective range with the midpoint
of the weapon's unmodified base range, including integer rounding and durability.
It is pre-mitigation damage against monsters, not PvP damage or measured DPS.
Swing delay uses `GetDelay`; swing-rate gain compares to the same weapon at zero
stamina. This is not a separate magical swing-speed attribute.

Sources include effective Tactics, effective strength, effective Anatomy, quality,
enchantment, virtual weapon damage, durability, stamina, individual worn armor,
and virtual armor. Effective attribute/skill contributions include their buffs;
the sheet does not claim to identify every underlying spell or skill-mod source.
Tactics and durability multiply the other damage bonuses, so the rows should not
be summed. Armor contributions are AR points, not damage-reduction percentages.
No random damage rolls, skill checks, or combat actions occur on opening/refresh.

The Spells tab uses `CharacterSpellSummary` for T2A Magery. It shows Magic Arrow,
Fireball, Lightning, Energy Bolt, Explosion, and Flame Strike damage ranges and
cast times, plus total damage improvement and a modifier/source table. Its
explicit reference target is a non-human monster with zero Magic Resist and no
successful resist roll. The scalar mirrors `Spell.GetDamageScalar`: the EvalInt
and Magery bonuses add before the non-human monster multiplier. Total improvement
is relative to the spell's raw base range, before integer truncation. Creature
overrides, region hooks, evasion, and other target-specific reductions are not
predicted. Mind Blast is excluded because its damage depends on target stats.
Spell instances are only queried for names and cast delays; no spell is cast and
no synthetic target is created.
