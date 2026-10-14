using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using Server.Network;
using Xunit;

namespace Server.Tests.Network.Packets;

[Collection("Sequential UOContent Tests")]
public class ClientPatchAdmissionTests : IDisposable
{
    private const long Release = 2026101301;
    private readonly bool _enabled = ClientPatchAdmission.Enabled;
    private readonly long _required = ClientPatchAdmission.RequiredReleaseId;
    private readonly PacketHandler[] _handlers = (PacketHandler[])IncomingPackets.Handlers.Clone();

    public ClientPatchAdmissionTests()
    {
        ClientPatchAdmission.Enabled = true;
        ClientPatchAdmission.RequiredReleaseId = Release;
        FreeshardProtocol.Configure();
        IncomingAccountPackets.Configure();
    }

    public void Dispose()
    {
        ClientPatchAdmission.Enabled = _enabled;
        ClientPatchAdmission.RequiredReleaseId = _required;
        _handlers.CopyTo(IncomingPackets.Handlers, 0);
        NetState.Slice();
    }

    private static byte[] Payload(long release = Release, byte protocol = 1, int length = 9)
    {
        var data = new byte[length];
        if (length > 0)
        {
            data[0] = protocol;
        }

        if (length >= 9)
        {
            BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(1, 8), release);
        }

        return data;
    }

    private static void Declare(NetState state, long release = Release, byte protocol = 1, int length = 9) =>
        ClientPatchAdmission.Receive(state, new SpanReader(Payload(release, protocol, length)));

    [Fact]
    public void DisabledPolicyPreservesExistingClients()
    {
        using var state = PacketTestUtilities.CreateTestNetState();
        ClientPatchAdmission.Enabled = false;
        Assert.True(ClientPatchAdmission.Require(state));
        Assert.Null(state.Account);
        Assert.Null(state.Mobile);
    }

    [Theory]
    [InlineData(2026101301, 1, 9, true)]
    [InlineData(2026101300, 1, 9, false)]
    [InlineData(2026101302, 1, 9, false)]
    [InlineData(0, 1, 9, false)]
    [InlineData(-1, 1, 9, false)]
    [InlineData(long.MaxValue, 1, 9, false)]
    [InlineData(2026101301, 0, 9, false)]
    [InlineData(2026101301, 2, 9, false)]
    [InlineData(2026101301, 1, 0, false)]
    [InlineData(2026101301, 1, 1, false)]
    [InlineData(2026101301, 1, 8, false)]
    [InlineData(2026101301, 1, 10, false)]
    public void RequiresExactPositiveReleaseProtocolAndLength(long release, byte protocol, int length, bool expected)
    {
        using var state = PacketTestUtilities.CreateTestNetState();
        Declare(state, release, protocol, length);
        Assert.Equal(expected, ClientPatchAdmission.IsAdmitted(state));
        Assert.Null(state.Account);
        Assert.Null(state.Mobile);
        Assert.False(state.SentFirstPacket);
        Assert.Equal(0, state.AuthId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4294967296)]
    [InlineData(long.MaxValue)]
    public void SupportsEntirePositiveInt64ReleaseRange(long release)
    {
        using var state = PacketTestUtilities.CreateTestNetState();
        ClientPatchAdmission.RequiredReleaseId = release;
        Declare(state, release);
        Assert.True(ClientPatchAdmission.IsAdmitted(state));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EnabledWithoutPositiveRequiredReleaseFailsClosed(long required)
    {
        using var state = PacketTestUtilities.CreateTestNetState();
        Declare(state);
        ClientPatchAdmission.RequiredReleaseId = required;
        Assert.False(ClientPatchAdmission.Require(state));
    }

    [Fact]
    public void DeclarationCannotBeReusedByAnotherConnectionFromSameAddress()
    {
        using var first = PacketTestUtilities.CreateTestNetState();
        Declare(first);
        using var second = PacketTestUtilities.CreateTestNetState();
        Assert.Equal(first.Address, second.Address);
        Assert.True(ClientPatchAdmission.IsAdmitted(first));
        Assert.False(ClientPatchAdmission.IsAdmitted(second));
    }

    [Fact]
    public void DuplicateDeclarationInvalidatesConnection()
    {
        using var state = PacketTestUtilities.CreateTestNetState();
        Declare(state);
        Declare(state);
        Assert.False(ClientPatchAdmission.IsAdmitted(state));
    }

    [Fact]
    public void DeclarationAfterAuthenticationDoesNotAdmitConnection()
    {
        using var state = PacketTestUtilities.CreateTestNetState();
        state.SentFirstPacket = true;
        Declare(state);
        Assert.False(ClientPatchAdmission.IsAdmitted(state));
    }

    [Fact]
    public void RejectedConnectionCannotRecoverBySendingMetadataAndDoesNotRepeatReply()
    {
        using var state = PacketTestUtilities.CreateTestNetState();
        Assert.False(ClientPatchAdmission.Require(state));
        var bytes = state._socket.SendBuffer.ReadableBytes;
        Declare(state);
        Assert.False(ClientPatchAdmission.Require(state));
        Assert.Equal(bytes, state._socket.SendBuffer.ReadableBytes);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xF8)]
    [InlineData(0x5D)]
    [InlineData(0x80)]
    [InlineData(0x83)]
    [InlineData(0x91)]
    [InlineData(0xA0)]
    public unsafe void EveryEntryHandlerRejectsBeforeParsingOrMutatingAccount(int packetId)
    {
        using var state = PacketTestUtilities.CreateTestNetState();
        var account = new MockAccount();
        state.Account = account;
        state.AuthId = 123;

        IncomingPackets.GetHandler(packetId).OnReceive(state, new SpanReader(ReadOnlySpan<byte>.Empty));

        Assert.Contains("Patch Required", state._disconnectReason);
        Assert.Same(account, state.Account);
        Assert.Null(state.Mobile);
        Assert.False(state.SentFirstPacket);
        Assert.Equal(123, state.AuthId);
        Assert.Equal(packetId == 0x91, state.CompressionEnabled);
    }

    [Fact]
    public void DeferredAccountCompletionStillRequiresAdmission()
    {
        using var state = PacketTestUtilities.CreateTestNetState();
        IncomingAccountPackets.CompleteAccountLogin(state, true, ALRReason.Invalid);
        Assert.Null(state.ServerInfo);
        Assert.Contains("Patch Required", state._disconnectReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatchRequiredReplyUsesCorrectCompressionAndDrainsBeforeClose(bool gameLogin)
    {
        using var state = PacketTestUtilities.CreateTestNetState(out var peer);
        using (peer)
        {
            Assert.False(ClientPatchAdmission.Require(state, gameLogin));
            byte[] expected = [0x82, 0xFD];
            if (gameLogin)
            {
                var output = new byte[64];
                var length = NetworkCompression.Compress(expected, output);
                expected = output.AsSpan(0, length).ToArray();
            }

            var actual = new byte[64];
            var total = 0;
            var eof = false;
            var deadline = Stopwatch.StartNew();
            while (!eof && deadline.ElapsedMilliseconds < 5000)
            {
                NetState.Slice();
                if (peer.Poll(1000, SelectMode.SelectRead))
                {
                    var count = peer.Receive(actual.AsSpan(total));
                    total += count;
                    eof = count == 0;
                }
            }

            Assert.True(eof);
            Assert.Equal(expected, actual.AsSpan(0, total).ToArray());
        }
    }

    [Fact]
    public unsafe void FreeshardDispatcherAcceptsOnlyOutOfGameMetadata()
    {
        using var state = PacketTestUtilities.CreateTestNetState();
        var payload = new byte[10];
        payload[0] = ClientPatchAdmission.Subcommand;
        Payload().CopyTo(payload, 1);
        IncomingPackets.GetHandler(0xF1).OnReceive(state, new SpanReader(payload));
        Assert.True(ClientPatchAdmission.IsAdmitted(state));

        using var inGame = PacketTestUtilities.CreateTestNetState();
        var mobile = new Mobile();
        try
        {
            inGame.Mobile = mobile;
            IncomingPackets.GetHandler(0xF1).OnReceive(inGame, new SpanReader(payload));
            Assert.False(ClientPatchAdmission.IsAdmitted(inGame));
            Assert.Contains("attached to a mobile", inGame._disconnectReason);
        }
        finally
        {
            inGame.Mobile = null;
            mobile.Delete();
        }
    }

    [Fact]
    public void ValidDeclarationDoesNotBypassAuthIdValidation()
    {
        using var state = PacketTestUtilities.CreateTestNetState();
        Declare(state);
        state.Seed = 123;
        var payload = new byte[64];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), 456);
        IncomingAccountPackets.GameLogin(state, new SpanReader(payload));
        Assert.Contains("Invalid auth id", state._disconnectReason);
        Assert.Null(state.Account);
        Assert.Null(state.Mobile);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealWireMetadataIsAcceptedBetweenSeedAndAuthentication(bool gameConnection)
    {
        using var state = PacketTestUtilities.CreateTestNetState(out var peer);
        using (peer)
        {
            var seedLength = gameConnection ? 4 : 21;
            var bytes = new byte[seedLength + 13];
            if (gameConnection)
            {
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), 0x01020304);
            }
            else
            {
                bytes[0] = 0xEF;
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(1, 4), 0x01020304);
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(5, 4), 7);
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(13, 4), 15);
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(17, 4), 1);
            }

            bytes[seedLength] = 0xF1;
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(seedLength + 1, 2), 13);
            bytes[seedLength + 3] = ClientPatchAdmission.Subcommand;
            Payload().CopyTo(bytes, seedLength + 4);
            peer.Send(bytes);

            var deadline = Stopwatch.StartNew();
            while (!ClientPatchAdmission.IsAdmitted(state) && deadline.ElapsedMilliseconds < 5000)
            {
                NetState.Slice();
            }

            Assert.True(ClientPatchAdmission.IsAdmitted(state));
            Assert.Equal(
                gameConnection ? NetState.ProtocolState.GameServer_AwaitingGameServerLogin :
                    NetState.ProtocolState.LoginServer_AwaitingLogin,
                state._protocolState
            );
            Assert.False(state.SentFirstPacket);
            Assert.Null(state.Account);
            Assert.Null(state.Mobile);
        }
    }
}
