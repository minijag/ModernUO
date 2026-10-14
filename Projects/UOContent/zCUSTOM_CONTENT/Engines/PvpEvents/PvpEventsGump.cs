using System;
using System.Collections.Immutable;
using Server.Engines.TownStruggle;
using Server.Gumps;
using Server.Network;

namespace Server.Engines.PvpEvents;

public enum EventKind
{
    Extraction,
    TownStruggle
}

public enum StatsScope
{
    PreviousEvent,
    Seasonal,
    Lifetime
}

public sealed class PvpEventsGump : DynamicGump
{
    internal const int PanelWidth = 720;
    internal const int PanelHeight = 600;
    internal const int RowsPerPage = 8;
    internal const int OwnRowHue = 0x35;
    private readonly Mobile _viewer;
    private readonly View _view;
    private bool _responded;

    public override bool Singleton => true;

    private PvpEventsGump(Mobile viewer, View view) : base(50, 50)
    {
        _viewer = viewer;
        _view = view;
    }

    public static void DisplayTo(Mobile viewer, EventKind eventKind = EventKind.TownStruggle,
        StatsScope scope = StatsScope.PreviousEvent, int page = 0)
    {
        if (viewer is not { Deleted: false, NetState: not null } || !Enum.IsDefined(eventKind) || !Enum.IsDefined(scope))
        {
            return;
        }
        viewer.SendGump(new PvpEventsGump(viewer, Capture(viewer.Serial.Value, eventKind, scope, page, Core.Now)));
    }

    internal static View Capture(uint viewerSerial, EventKind eventKind, StatsScope scope, int page, DateTime now)
    {
        var previous = scope == StatsScope.PreviousEvent ? PvpEventHistory.Last(eventKind) : null;
        var source = PvpEventHistory.Rankings(eventKind, scope, now);
        var rows = ImmutableArray.CreateBuilder<Row>(source.Count);
        Row? own = null;
        for (var i = 0; i < source.Count; i++)
        {
            var entry = source[i];
            var row = new Row(i + 1, entry.PlayerSerial, entry.Name, entry.Guild, entry.Team, entry.Points,
                entry.Rewards, entry.Kills, entry.Deaths, entry.Penalties, entry.Matches, entry.Wins, entry.Healing, entry.Outcome);
            rows.Add(row);
            if (entry.PlayerSerial == viewerSerial) { own = row; }
        }
        var period = scope switch
        {
            StatsScope.PreviousEvent => "PREVIOUS EVENT - FINAL RESULTS",
            StatsScope.Seasonal => PvpEventHistory.CurrentQuarter(now) + " - UTC CALENDAR QUARTER",
            _ => "LIFETIME - ALL RECORDED EVENTS"
        };
        var controls = eventKind == EventKind.TownStruggle && TownStruggleRuntime.Controller is { Deleted: false };
        var pending = eventKind == EventKind.TownStruggle &&
            TownStruggleRuntime.Controller is { Deleted: false, Phase: TownStrugglePhase.Results, RewardsCalculated: false };
        var view = new View(eventKind, scope, period, previous?.Venue, previous?.FinishedUtc,
            scope == StatsScope.PreviousEvent ? previous != null : source.Count > 0, previous != null,
            previous?.OrderScore, previous?.ChaosScore, rows.MoveToImmutable(), own, controls, page, pending);
        return view with { Page = ClampPage(view) };
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder) => BuildBoard(ref builder, _view);

    internal static void BuildBoard(ref DynamicGumpBuilder builder, View view)
    {
        builder.AddPage();
        CustomGumpStyle.AddPanel(ref builder, 0, 0, PanelWidth, PanelHeight, "PVP EVENTS");
        AddAction(ref builder, 28, 39, 10, 184, "EXTRACTION", view.Event == EventKind.Extraction);
        AddAction(ref builder, 222, 39, 11, 216, "TOWN STRUGGLE", view.Event == EventKind.TownStruggle);
        if (HasVisibleData(view) && view.OwnRow.HasValue) { AddAction(ref builder, 478, 39, 2, 214, "SHOW MY ROW"); }
        AddAction(ref builder, 28, 82, 20, 214, "PREVIOUS EVENT", view.Scope == StatsScope.PreviousEvent);
        AddAction(ref builder, 250, 82, 21, 214, "SEASONAL STATS", view.Scope == StatsScope.Seasonal);
        AddAction(ref builder, 472, 82, 22, 220, "LIFETIME STATS", view.Scope == StatsScope.Lifetime);
        builder.AddLabelCropped(28, 122, 664, 24, OwnRowHue, view.PeriodLabel);

        var visible = HasVisibleData(view);
        if (view.Scope == StatsScope.PreviousEvent && visible)
        {
            builder.AddLabelCropped(28, 150, 664, 24, CustomGumpStyle.TextHue,
                $"{view.Venue} | Finished {view.FinishedUtc:yyyy-MM-dd HH:mm} UTC");
            if (view.Event == EventKind.TownStruggle && view.OrderTotal.HasValue && view.ChaosTotal.HasValue)
            {
                builder.AddHtml(28, 178, 664, 24, $"FINAL TEAM TOTALS: Order {view.OrderTotal:N0} / Chaos {view.ChaosTotal:N0}",
                    CustomGumpStyle.TitleColor, CustomGumpStyle.StandardFontSize);
            }
            else if (view.Event == EventKind.TownStruggle)
            {
                builder.AddHtml(28, 178, 664, 24, "Final team totals are unavailable for this recorded event.",
                    CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
            }
        }
        else
        {
            builder.AddHtml(28, 150, 664, 24,
                view.Scope == StatsScope.PreviousEvent ? "No completed event has been recorded yet." : "Totals include completed, recorded events only.",
                CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
            if (view.Event == EventKind.TownStruggle)
            {
                builder.AddHtml(28, 178, 664, 24,
                    view.Scope == StatsScope.Seasonal ? "Seasons follow UTC calendar quarters. Lifetime statistics never reset."
                        : "Recording begins with this update; earlier results are not reconstructed.",
                    CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
            }
        }
        if (view.Event == EventKind.Extraction)
        {
            builder.AddHtml(28, 178, 664, 24, "Stand in the PvP lobby before the round starts to enter.",
                CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
        }
        builder.AddHtml(28, 207, 486, 24,
            view.Event == EventKind.TownStruggle ? "K/A = credited kills or assists. Your row is highlighted." : "Extraction opens after 7 minutes. Your row is highlighted.",
            CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
        builder.AddHtml(526, 207, 166, 24, $"Page {ClampPage(view) + 1} / {PageCount(view)}",
            CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);

        BuildRankings(ref builder, view);
        builder.AddHtml(28, 459, 664, 24,
            view.Event == EventKind.Extraction ? "Ranked by successful extractions. Lifetime statistics never reset."
                : view.ResultsPending ? "Current results are pending: rankings update after the 15-minute MVP vote."
                : "Town Struggle results enter rankings after the 15-minute MVP voting period.",
            CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
        BuildOwnStatistics(ref builder, view);
        AddAction(ref builder, 28, 541, 1, 121, "REFRESH");
        if (view.Event == EventKind.TownStruggle && view.ControlsAvailable) { AddAction(ref builder, 158, 541, 5, 185, "BATTLE CONTROLS"); }
        if (ClampPage(view) > 0) { AddAction(ref builder, 352, 541, 3, 119, "PREVIOUS"); }
        if (ClampPage(view) + 1 < PageCount(view)) { AddAction(ref builder, 480, 541, 4, 96, "NEXT"); }
        AddAction(ref builder, 585, 541, 0, 107, "CLOSE");
    }

    private static void BuildRankings(ref DynamicGumpBuilder builder, View view)
    {
        const int headerY = 238;
        Label(ref builder, 28, headerY, 30, "#", OwnRowHue);
        Label(ref builder, 62, headerY, view.Event == EventKind.TownStruggle ? 184 : 216, "PLAYER / GUILD", OwnRowHue);
        if (view.Event == EventKind.TownStruggle)
        {
            Label(ref builder, 250, headerY, 67, view.Scope == StatsScope.PreviousEvent ? "TEAM" : "WINS", OwnRowHue);
            Label(ref builder, 321, headerY, 73, "POINTS", OwnRowHue);
            Label(ref builder, 398, headerY, 56, "REW", OwnRowHue);
            Label(ref builder, 458, headerY, 56, "K/A", OwnRowHue);
            Label(ref builder, 518, headerY, 66, "DEATHS", OwnRowHue);
            Label(ref builder, 588, headerY, 104, "PENALTIES", OwnRowHue);
        }
        else
        {
            Label(ref builder, 282, headerY, 132, view.Scope == StatsScope.PreviousEvent ? "OUTCOME" : "EVENTS", OwnRowHue);
            Label(ref builder, 418, headerY, 102, "EXTRACTED", OwnRowHue);
            Label(ref builder, 524, headerY, 66, "DEATHS", OwnRowHue);
            Label(ref builder, 594, headerY, 98, "KILLS", OwnRowHue);
        }
        if (!HasVisibleData(view) || view.Rows.IsDefaultOrEmpty)
        {
            builder.AddHtml(62, 294, 600, 60,
                "Recording begins with this update. No rankings are available for this selection; completed events will appear as they are recorded.",
                CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
            return;
        }
        var start = ClampPage(view) * RowsPerPage;
        var count = Math.Min(RowsPerPage, view.Rows.Length - start);
        for (var i = 0; i < count; i++)
        {
            var row = view.Rows[start + i];
            var hue = row.PlayerSerial == view.OwnRow?.PlayerSerial ? OwnRowHue : CustomGumpStyle.TextHue;
            var y = 266 + i * 24;
            builder.AddLabelCropped(28, y, 30, 24, hue, $"{row.Rank}");
            var nameWidth = view.Event == EventKind.TownStruggle ? 184 : 216;
            if (string.IsNullOrEmpty(row.Guild)) { Label(ref builder, 62, y, nameWidth, row.Name, hue); }
            else { builder.AddLabelCropped(62, y, nameWidth, 24, hue, $"{row.Name} [{row.Guild}]"); }
            if (view.Event == EventKind.TownStruggle)
            {
                if (view.Scope == StatsScope.PreviousEvent) { Label(ref builder, 250, y, 67, row.Team, hue); }
                else { builder.AddLabelCropped(250, y, 67, 24, hue, $"{row.Wins:N0}"); }
                builder.AddLabelCropped(321, y, 73, 24, hue, $"{row.Points:N1}");
                builder.AddLabelCropped(398, y, 56, 24, hue, $"{row.Rewards:N0}");
                builder.AddLabelCropped(458, y, 56, 24, hue, $"{row.KillAssists:N0}");
                builder.AddLabelCropped(518, y, 66, 24, hue, $"{row.Deaths:N0}");
                builder.AddLabelCropped(588, y, 104, 24, hue, $"{row.Penalties:N0}");
            }
            else
            {
                if (view.Scope == StatsScope.PreviousEvent) { Label(ref builder, 282, y, 132, row.Outcome, hue); }
                else { builder.AddLabelCropped(282, y, 132, 24, hue, $"{row.Matches:N0}"); }
                builder.AddLabelCropped(418, y, 102, 24, hue, $"{row.Wins:N0}");
                builder.AddLabelCropped(524, y, 66, 24, hue, $"{row.Deaths:N0}");
                builder.AddLabelCropped(594, y, 98, 24, hue, $"{row.KillAssists:N0}");
            }
        }
    }

    private static void BuildOwnStatistics(ref DynamicGumpBuilder builder, View view)
    {
        if (!HasVisibleData(view) || !view.OwnRow.HasValue)
        {
            builder.AddHtml(28, 486, 664, 48, "YOUR STATS: You have no recorded participation in this selection.",
                CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
            return;
        }
        var own = view.OwnRow.Value;
        if (view.Event == EventKind.TownStruggle)
        {
            builder.AddLabelCropped(28, 486, 664, 24, OwnRowHue,
                $"YOUR STATS: Rank {own.Rank:N0} | Events {own.Matches:N0} | Wins {own.Wins:N0} | Rewards {own.Rewards:N0}");
            builder.AddLabelCropped(28, 512, 664, 24, CustomGumpStyle.TextHue,
                $"Points {own.Points:N1} | K/A {own.KillAssists:N0} | Healing {own.Healing:N0} | Deaths {own.Deaths:N0} | Penalties {own.Penalties:N0}");
        }
        else
        {
            builder.AddLabelCropped(28, 486, 664, 24, OwnRowHue,
                $"YOUR STATS: Rank {own.Rank:N0} | Events {own.Matches:N0} | Successful extractions {own.Wins:N0}");
            builder.AddLabelCropped(28, 512, 664, 24, CustomGumpStyle.TextHue,
                $"Kills {own.KillAssists:N0} | Deaths {own.Deaths:N0} | Success rate {(own.Matches > 0 ? (double)own.Wins / own.Matches : 0):P0}");
        }
    }

    private static void Label(ref DynamicGumpBuilder builder, int x, int y, int width, string text, int hue) =>
        builder.AddLabelCropped(x, y, width, 24, hue, text ?? "");

    private static void AddAction(ref DynamicGumpBuilder builder, int x, int y, int id, int width, string text, bool selected = false)
    {
        CustomGumpStyle.AddOptionButton(ref builder, x, y, id, selected);
        builder.AddHtml(x + 35, y + 3, width - 35, 24, text, CustomGumpStyle.ActionColor, CustomGumpStyle.StandardFontSize);
    }

    internal static bool HasVisibleData(View view) => view.HasRecordedData && (view.Scope != StatsScope.PreviousEvent || view.Finalized);
    internal static int PageCount(View view) => Math.Max(1, (HasVisibleData(view) && !view.Rows.IsDefault ? view.Rows.Length + RowsPerPage - 1 : 0) / RowsPerPage);
    internal static int ClampPage(View view) => Math.Clamp(view.Page, 0, PageCount(view) - 1);

    internal static bool TryNavigate(View view, int button, out Navigation navigation)
    {
        var eventKind = view.Event;
        var scope = view.Scope;
        var page = ClampPage(view);
        switch (button)
        {
            case 1: break;
            case 2 when HasVisibleData(view) && view.OwnRow.HasValue:
                for (var i = 0; i < view.Rows.Length; i++)
                {
                    if (view.Rows[i].PlayerSerial == view.OwnRow.Value.PlayerSerial) { page = i / RowsPerPage; break; }
                }
                break;
            case 3 when page > 0: page--; break;
            case 4 when page + 1 < PageCount(view): page++; break;
            case 10: eventKind = EventKind.Extraction; page = 0; break;
            case 11: eventKind = EventKind.TownStruggle; page = 0; break;
            case 20: scope = StatsScope.PreviousEvent; page = 0; break;
            case 21: scope = StatsScope.Seasonal; page = 0; break;
            case 22: scope = StatsScope.Lifetime; page = 0; break;
            default: navigation = default; return false;
        }
        navigation = new Navigation(eventKind, scope, page);
        return true;
    }

    public override void OnResponse(NetState sender, in RelayInfo info)
    {
        if (_responded || sender?.Mobile != _viewer || _viewer.Deleted || _viewer.NetState != sender) { return; }
        _responded = true;
        if (info.ButtonID == 0) { return; }
        if (info.ButtonID == 5 && _view.Event == EventKind.TownStruggle && _view.ControlsAvailable)
        {
            TownStruggleGump.DisplayTo(_viewer);
            return;
        }
        if (TryNavigate(_view, info.ButtonID, out var navigation))
        {
            DisplayTo(_viewer, navigation.Event, navigation.Scope, navigation.Page);
        }
    }

    internal sealed record View(EventKind Event, StatsScope Scope, string PeriodLabel, string Venue, DateTime? FinishedUtc,
        bool HasRecordedData, bool Finalized, int? OrderTotal, int? ChaosTotal, ImmutableArray<Row> Rows, Row? OwnRow,
        bool ControlsAvailable, int Page, bool ResultsPending = false);
    internal readonly record struct Row(int Rank, uint PlayerSerial, string Name, string Guild, string Team, double Points,
        long Rewards, long KillAssists, long Deaths, long Penalties, long Matches, long Wins, long Healing, string Outcome);
    internal readonly record struct Navigation(EventKind Event, StatsScope Scope, int Page);
}
