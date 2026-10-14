using System.Buffers;
using System.Runtime.CompilerServices;
using Server.Logging;

namespace Server.Network;

// This is a client-reported compatibility check, not remote attestation or account authentication.
public static class ClientPatchAdmission
{
    public const byte Subcommand = 0x52;
    public const byte ProtocolVersion = 1;
    public const byte PatchRequiredReason = 0xFD;

    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(ClientPatchAdmission));

    // Weak connection keys also release pre-authentication records that never acquire a Mobile.
    private static readonly ConditionalWeakTable<NetState, Admission> _admissions = new();

    public static bool Enabled { get; internal set; }
    public static long RequiredReleaseId { get; internal set; }

    public static void Configure()
    {
        Enabled = ServerConfiguration.GetSetting("clientPatchAdmission.enabled", false);
        RequiredReleaseId = ServerConfiguration.GetSetting("clientPatchAdmission.requiredReleaseId", 0L);

        if (Enabled && RequiredReleaseId <= 0)
        {
            _logger.Error("Client patch admission is enabled without a positive required release ID; all logins will be rejected.");
        }
    }

    public static void Receive(NetState state, SpanReader reader)
    {
        if (_admissions.TryGetValue(state, out var admission))
        {
            admission.ReleaseId = 0;
            return;
        }

        admission = new Admission();
        _admissions.Add(state, admission);

        // A declaration is single-use and must precede authentication on each TCP connection.
        if (state.SentFirstPacket || state.Mobile != null || reader.Remaining != 9 ||
            reader.ReadByte() != ProtocolVersion)
        {
            return;
        }

        var releaseId = reader.ReadInt64();
        admission.ReleaseId = releaseId > 0 ? releaseId : 0;
    }

    internal static bool IsAdmitted(NetState state) =>
        !Enabled || RequiredReleaseId > 0 &&
        _admissions.TryGetValue(state, out var admission) && !admission.Rejected &&
        admission.ReleaseId == RequiredReleaseId;

    public static bool Require(NetState state, bool gameLogin = false)
    {
        if (IsAdmitted(state))
        {
            return true;
        }

        if (!_admissions.TryGetValue(state, out var admission))
        {
            admission = new Admission();
            _admissions.Add(state, admission);
        }

        if (admission.Rejected)
        {
            return false;
        }

        admission.Rejected = true;

        // The game socket enables Huffman decoding before 0x91; the login socket does not.
        if (gameLogin)
        {
            state.CompressionEnabled = true;
        }

        state.SendAccountLoginRejected((ALRReason)PatchRequiredReason);
        state.Disconnect("Patch Required: use the launcher to install the current release.");
        return false;
    }

    private sealed class Admission
    {
        public long ReleaseId;
        public bool Rejected;
    }
}
