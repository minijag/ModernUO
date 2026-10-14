using System;
using System.Collections.Generic;
using Server.Engines.PvpEvents;
using Server.Gumps;
using Server.Network;

namespace Server.Engines.TownStruggle;

public sealed class TownStruggleGump : DynamicGump
{
    internal const int PanelWidth = 720;
    internal const int PanelHeight = 600;
    internal const int RosterPageSize = 8;
    private readonly Mobile _viewer;
    private readonly TownStruggleController _controller;
    private readonly int _roundNumber;
    private readonly View _view;
    private readonly Mobile[] _voteTargets;
    private bool _responded;

    public override bool Singleton => true;

    private TownStruggleGump(Mobile viewer, TownStruggleController controller, bool roster, int page) : base(50, 50)
    {
        _viewer = viewer;
        _controller = controller;
        _roundNumber = controller.RoundNumber;
        _view = Capture(viewer, controller, roster, page, out _voteTargets);
    }

    public static void DisplayTo(Mobile viewer) => DisplayTo(viewer, TownStruggleRuntime.Controller);

    public static void DisplayTo(Mobile viewer, TownStruggleController controller, bool roster = false, int page = 0)
    {
        if (viewer is not { Deleted: false, NetState: not null })
        {
            return;
        }

        if (controller is not { Deleted: false })
        {
            viewer.SendMessage("Town Struggle is not currently configured.");
            return;
        }

        viewer.SendGump(new TownStruggleGump(viewer, controller, roster, page));
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder) => BuildBoard(ref builder, _view);

    internal static void BuildBoard(ref DynamicGumpBuilder builder, View view)
    {
        builder.AddPage();
        CustomGumpStyle.AddPanel(ref builder, 0, 0, PanelWidth, PanelHeight, "TOWN STRUGGLE");
        builder.AddLabelCropped(28, 36, 440, 24, CustomGumpStyle.TextHue, view.Venue);
        builder.AddHtml(490, 36, 202, 24, $"{view.Phase}", CustomGumpStyle.TitleColor, CustomGumpStyle.StandardFontSize);
        builder.AddHtml(28, 65, 440, 24,
            $"Time remaining: {(int)view.Remaining.TotalMinutes}:{view.Remaining.Seconds:D2}",
            CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);

        if (view.Phase == TownStrugglePhase.Results)
        {
            builder.AddHtml(28, 96, 664, 24, $"Final score: Order {view.OrderScore} / Chaos {view.ChaosScore}",
                CustomGumpStyle.TitleColor, CustomGumpStyle.StandardFontSize);
        }
        else if (view.Team != TownStruggleTeam.Neutral && view.Phase == TownStrugglePhase.Active)
        {
            builder.AddHtml(28, 96, 664, 24, $"Your team: {view.Team} - {view.OwnScore} points (opponent score hidden)",
                CustomGumpStyle.TitleColor, CustomGumpStyle.StandardFontSize);
        }
        else
        {
            builder.AddHtml(28, 96, 664, 24,
                view.Disqualified ? "You are disqualified from this round." : view.Registered ? "You are enrolled for this round." : "Join during signup to participate.",
                CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
        }

        AddAction(ref builder, 28, 130, 4, "BATTLE", !view.ShowRoster);
        AddAction(ref builder, 212, 130, 5, "TEAM / MVP", view.ShowRoster);
        builder.AddHtml(435, 134, 258, 24, view.SelectiveLootEnabled ? "Selective gear loot: ON" : "Equipment loss: OFF",
            CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);

        if (view.ShowRoster)
        {
            BuildRoster(ref builder, view);
        }
        else
        {
            BuildZones(ref builder, view);
        }

        builder.AddHtml(28, 477, 664, 24,
            $"Your control: {view.ControlPoints:F1} | K/A: {view.KillPoints / TownStruggleRules.KillIndividualPoints} | Healing: {view.Healing} | Tokens: {view.Rewards}",
            CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
        builder.AddHtml(28, 502, 664, 36,
            view.SelectiveLootEnabled
                ? "Loot ON: magic gear + exceptional colored gear can drop. Ordinary gear stays; blessed/insured protections apply."
                : "Loot OFF: all equipment is retained on event death.",
            CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
        AddAction(ref builder, 28, 541, 1, "REFRESH");
        if (view.Phase == TownStrugglePhase.Signup && !view.Registered && !view.Disqualified)
        {
            AddAction(ref builder, 212, 541, 2, "JOIN");
            AddAction(ref builder, 384, 541, 8, "EVENTS");
        }
        else
        {
            AddAction(ref builder, 212, 541, 8, "EVENTS");
        }
        if (view.Registered)
        {
            AddAction(ref builder, 384, 541, 3, "LEAVE");
        }
        AddAction(ref builder, 584, 541, 0, "CLOSE");
    }

    private static void BuildZones(ref DynamicGumpBuilder builder, View view)
    {
        for (var i = 0; i < TownStruggleRules.CaptureZoneCount; i++)
        {
            var state = view.Captures[i];
            var y = 178 + i * 48;
            builder.AddHtml(28, y, 495, 24, $"Capture {i + 1}: {state.Owner} | Progress {state.Progress}",
                CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
            var available = view.Phase == TownStrugglePhase.Active && view.Registered && !view.Disqualified &&
                            view.Team != TownStruggleTeam.Neutral && state.Owner == view.Team;
            if (available)
            {
                AddAction(ref builder, 552, y - 3, 10 + i, "DEPLOY");
            }
            else
            {
                builder.AddHtml(552, y, 140, 24, "Unavailable", CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
            }
        }

        for (var i = 0; i < TownStruggleRules.RespawnZoneCount; i++)
        {
            var y = 322 + i * 43;
            builder.AddHtml(28, y, 495, 24, $"Respawn {i + 1}: unconditional team deployment", CustomGumpStyle.WhiteTextColor,
                CustomGumpStyle.StandardFontSize);
            if (view.Phase == TownStrugglePhase.Active && view.Registered && !view.Disqualified)
            {
                AddAction(ref builder, 552, y - 3, 13 + i, "DEPLOY");
            }
        }

        if (view.RespawnRemaining > TimeSpan.Zero)
        {
            builder.AddHtml(28, 450, 664, 24, $"Respawn available in {(int)Math.Ceiling(view.RespawnRemaining.TotalSeconds)} seconds.",
                CustomGumpStyle.TitleColor, CustomGumpStyle.StandardFontSize);
        }
        else
        {
            builder.AddHtml(28, 450, 664, 24, "Stand alone to capture. Contested zones freeze. Control scores every 30 seconds.",
                CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
        }
    }

    private static void BuildRoster(ref DynamicGumpBuilder builder, View view)
    {
        builder.AddHtml(28, 174, 250, 24, "TEAM MEMBER", CustomGumpStyle.TitleColor, CustomGumpStyle.StandardFontSize);
        builder.AddHtml(310, 174, 190, 24, "SCORE / HEALING", CustomGumpStyle.TitleColor, CustomGumpStyle.StandardFontSize);
        builder.AddHtml(556, 174, 136, 24, "MVP VOTE", CustomGumpStyle.TitleColor, CustomGumpStyle.StandardFontSize);
        for (var i = 0; i < view.Roster.Length; i++)
        {
            var row = view.Roster[i];
            var y = 207 + i * 29;
            builder.AddLabelCropped(28, y, 274, 24, CustomGumpStyle.TextHue, row.Name);
            builder.AddHtml(310, y, 222, 24, $"{row.Score:F1} / {row.Healing}",
                CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
            if (row.CanVote)
            {
                CustomGumpStyle.AddOptionButton(ref builder, 584, y - 3, 100 + i, row.Selected);
            }
        }

        if (view.Roster.Length == 0)
        {
            builder.AddHtml(28, 215, 664, 48, "Your assigned teammates appear here. Vote for a teammate after the match.",
                CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
        }

        if (view.Page > 0)
        {
            AddAction(ref builder, 28, 445, 6, "PREVIOUS");
        }
        builder.AddHtml(270, 449, 220, 24, $"Page {view.Page + 1} / {view.Pages}",
            CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
        if (view.Page + 1 < view.Pages)
        {
            AddAction(ref builder, 552, 445, 7, "NEXT");
        }
    }

    private static void AddAction(ref DynamicGumpBuilder builder, int x, int y, int id, string caption, bool selected = false)
    {
        CustomGumpStyle.AddOptionButton(ref builder, x, y, id, selected);
        builder.AddHtml(x + 35, y + 3, Math.Min(140, PanelWidth - CustomGumpStyle.ContentPadding - x - 35), 24,
            caption, CustomGumpStyle.ActionColor, CustomGumpStyle.StandardFontSize);
    }

    public override void OnResponse(NetState sender, in RelayInfo info)
    {
        if (_responded || sender?.Mobile != _viewer || _viewer.Deleted || _controller.Deleted ||
            TownStruggleRuntime.Controller != _controller || _controller.RoundNumber != _roundNumber)
        {
            return;
        }
        _responded = true;
        var roster = _view.ShowRoster;
        var page = _view.Page;
        string message = null;
        switch (info.ButtonID)
        {
            case 0:
                return;
            case 1:
                break;
            case 2:
                _controller.TryJoin(_viewer, out message);
                break;
            case 3:
                _controller.TryLeave(_viewer, out message);
                break;
            case 4:
                roster = false;
                break;
            case 5:
                roster = true;
                break;
            case 6 when roster && page > 0:
                page--;
                break;
            case 7 when roster && page + 1 < _view.Pages:
                page++;
                break;
            case 8:
                PvpEventsGump.DisplayTo(_viewer);
                return;
            case >= 10 and <= 15:
                _controller.TryDeploy(_viewer, info.ButtonID - 10, out message);
                break;
            case >= 100 and < 100 + RosterPageSize when info.ButtonID - 100 < _voteTargets.Length:
                _controller.TryVote(_viewer, _voteTargets[info.ButtonID - 100], out message);
                break;
            default:
                return;
        }

        if (!string.IsNullOrEmpty(message))
        {
            _viewer.SendMessage(message);
        }
        DisplayTo(_viewer, _controller, roster, page);
    }

    private static View Capture(Mobile viewer, TownStruggleController controller, bool roster, int page, out Mobile[] targets)
    {
        var participant = controller.GetParticipant(viewer);
        var team = participant?.Team ?? TownStruggleTeam.Neutral;
        var rows = new List<Row>();
        var teammates = new List<Mobile>();
        if (team != TownStruggleTeam.Neutral)
        {
            foreach (var member in controller.Participants)
            {
                if (member.Team != team || member.Disqualified || member.Player is not { Deleted: false })
                {
                    continue;
                }
                teammates.Add(member.Player);
                rows.Add(new Row(member.Player.Name ?? "Unknown", member.ControlPoints + member.KillPoints, member.Healing,
                    controller.Phase == TownStrugglePhase.Results && participant is { Disqualified: false } && member.Player != viewer,
                    participant?.VoteFor == member.Player));
            }
        }
        var pages = Math.Max(1, (rows.Count + RosterPageSize - 1) / RosterPageSize);
        page = Math.Clamp(page, 0, pages - 1);
        var count = Math.Min(RosterPageSize, rows.Count - page * RosterPageSize);
        var pageRows = new Row[count];
        targets = new Mobile[count];
        for (var i = 0; i < count; i++)
        {
            pageRows[i] = rows[page * RosterPageSize + i];
            targets[i] = teammates[page * RosterPageSize + i];
        }
        var captures = new TownStruggleCaptureState[TownStruggleRules.CaptureZoneCount];
        for (var i = 0; i < captures.Length && i < controller.CaptureStates.Count; i++)
        {
            captures[i] = controller.CaptureStates[i];
        }
        var now = controller.Now;
        var deadline = controller.Phase switch
        {
            TownStrugglePhase.Signup => controller.SignupDeadline,
            TownStrugglePhase.Active => controller.MatchDeadline,
            TownStrugglePhase.Results => controller.ResultsDeadline,
            _ => now
        };
        return new View(controller.Phase, controller.Venue?.Name ?? "Unconfigured town", Positive(deadline - now), team,
            participant != null, participant?.Disqualified == true,
            team == TownStruggleTeam.Order ? controller.OrderScore : team == TownStruggleTeam.Chaos ? controller.ChaosScore : 0,
            controller.Phase == TownStrugglePhase.Results ? controller.OrderScore : 0,
            controller.Phase == TownStrugglePhase.Results ? controller.ChaosScore : 0,
            participant?.ControlPoints ?? 0, participant?.KillPoints ?? 0, participant?.Healing ?? 0, participant?.Rewards ?? 0,
            participant == null ? TimeSpan.Zero : Positive(participant.RespawnAt - now), captures, pageRows, page, pages, roster,
            controller.SelectiveLootEnabled);
    }

    private static TimeSpan Positive(TimeSpan duration) => duration < TimeSpan.Zero ? TimeSpan.Zero : duration;

    internal sealed record View(
        TownStrugglePhase Phase, string Venue, TimeSpan Remaining, TownStruggleTeam Team, bool Registered,
        bool Disqualified, int OwnScore, int OrderScore, int ChaosScore, double ControlPoints, int KillPoints,
        int Healing, int Rewards, TimeSpan RespawnRemaining, TownStruggleCaptureState[] Captures, Row[] Roster,
        int Page, int Pages, bool ShowRoster, bool SelectiveLootEnabled = false
    );

    internal readonly record struct Row(string Name, double Score, int Healing, bool CanVote, bool Selected);
}
