using System;
using System.Collections.Generic;
using ModernUO.Serialization;

namespace Server.Engines.TownStruggle;

[SerializationGenerator(2)]
public partial class TownStruggleParticipant : Item
{
    [SerializableField(0, setter: "internal")] private Mobile _player;
    [SerializableField(1, setter: "internal")] private TownStruggleTeam _team;
    [SerializableField(2, setter: "internal")] private bool _disqualified;
    [SerializableField(3, setter: "internal")] private DateTime _respawnAt;
    [SerializableField(4, setter: "internal")] private DateTime _idleSince;
    [SerializableField(5, setter: "internal")] private double _controlPoints;
    [SerializableField(6, setter: "internal")] private int _killPoints;
    [SerializableField(7, setter: "internal")] private int _healing;
    [SerializableField(8, setter: "internal")] private int _deaths;
    [SerializableField(9, setter: "internal")] private int _rewards;
    [CanBeNull]
    [SerializableField(10, setter: "internal")] private Mobile _voteFor;
    [SerializableField(11, setter: "internal")] private Point3D _returnLocation;
    [SerializableField(12, setter: "internal")] private Map _returnMap;
    [SerializableField(13, setter: "internal")] private bool _rewardsDelivered;
    [SerializableField(14, setter: "internal")] private int _guildId;
    [SerializableField(15, setter: "internal")] private bool _returned;
    [SerializableField(16, setter: "internal")] private DateTime _outsideSince;
    [SerializableField(17, setter: "internal")] private uint _playerSerial;
    [SerializableField(18, setter: "internal")] private string _playerName;
    [SerializableField(19, setter: "internal")] private string _guildAbbreviation;

    internal readonly Dictionary<Mobile, DateTime> DamageContributors = [];
    public double Score => ControlPoints + KillPoints;

    [Constructible(AccessLevel.Administrator)]
    public TownStruggleParticipant() : base(1)
    {
        Visible = false;
        Movable = false;
    }

    internal void CaptureIdentity()
    {
        CapturePlayerIdentity();
        if (GuildAbbreviation == null)
        {
            GuildAbbreviation = GuildId > 0 ? World.FindGuild((Serial)(uint)GuildId)?.Abbreviation ?? "" : "";
        }
    }

    private void CapturePlayerIdentity()
    {
        if (PlayerSerial == 0 && Player != null)
        {
            PlayerSerial = Player.Serial.Value;
            PlayerName = Player.RawName ?? "Unknown";
        }
    }

    [AfterDeserialization(false)]
    private void AfterDeserialization() => CaptureIdentity();

    public override void OnAfterDelete()
    {
        DamageContributors.Clear();
        Player = null;
        VoteFor = null;
        base.OnAfterDelete();
    }

    private void MigrateFrom(V0Content content)
    {
        _player = content.Player;
        _team = content.Team;
        _disqualified = content.Disqualified;
        _respawnAt = content.RespawnAt;
        _idleSince = content.IdleSince;
        _controlPoints = content.ControlPoints;
        _killPoints = content.KillPoints;
        _healing = content.Healing;
        _deaths = content.Deaths;
        _rewards = content.Rewards;
        _voteFor = content.VoteFor;
        _returnLocation = content.ReturnLocation;
        _returnMap = content.ReturnMap;
        _rewardsDelivered = content.RewardsDelivered;
        _guildId = content.GuildId;
        _returned = content.Returned;
        _outsideSince = DateTime.MinValue;
        CapturePlayerIdentity();
    }

    private void MigrateFrom(V1Content content)
    {
        _player = content.Player;
        _team = content.Team;
        _disqualified = content.Disqualified;
        _respawnAt = content.RespawnAt;
        _idleSince = content.IdleSince;
        _controlPoints = content.ControlPoints;
        _killPoints = content.KillPoints;
        _healing = content.Healing;
        _deaths = content.Deaths;
        _rewards = content.Rewards;
        _voteFor = content.VoteFor;
        _returnLocation = content.ReturnLocation;
        _returnMap = content.ReturnMap;
        _rewardsDelivered = content.RewardsDelivered;
        _guildId = content.GuildId;
        _returned = content.Returned;
        _outsideSince = content.OutsideSince;
        CapturePlayerIdentity();
    }
}
