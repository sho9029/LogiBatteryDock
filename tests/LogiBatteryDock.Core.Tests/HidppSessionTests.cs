using LogiBatteryDock.Core;
using Xunit;

namespace LogiBatteryDock.Core.Tests;

public sealed class HidppSessionTests
{
    [Fact]
    public void PingMatchesDeviceAndSoftwareId()
    {
        var channel = new FakeChannel([Reply(1, 0, 0x1B, 0, 0, 0xA7)]);
        var session = new HidppSession(channel);

        Assert.True(session.Ping(1));
        Assert.Equal(0x11, channel.Writes.Single()[0]);
        Assert.Equal(1, channel.Writes.Single()[1]);
    }

    [Fact]
    public void PingRejectsErrorFrame()
    {
        var channel = new FakeChannel([Error(1, 0)]);
        var session = new HidppSession(channel);

        Assert.False(session.Ping(1));
    }

    [Fact]
    public void CanceledPingDoesNotWriteToTheChannel()
    {
        var channel = new FakeChannel([]);
        var session = new HidppSession(channel);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => session.Ping(1, cancellation.Token));
        Assert.Empty(channel.Writes);
    }

    [Fact]
    public void NameReadsOffsetsWithoutByteCounterWrap()
    {
        var channel = new FakeChannel([
            Reply(1, 0, 0x0B, 5, 0, 0),
            Reply(1, 5, 0x0B, 32, 0, 0),
            NameChunk(1, 5, 0, "0123456789ABCDEF"),
            NameChunk(1, 5, 0, "GHIJKLMNOPQRSTUV")
        ]);
        var session = new HidppSession(channel);

        Assert.Equal("0123456789ABCDEFGHIJKLMNOPQRSTUV", session.ReadName(1));
        Assert.Equal(0, channel.Writes[2][4]);
        Assert.Equal(16, channel.Writes[3][4]);
    }

    private static byte[] Reply(byte device, byte feature, byte functionAndSoftwareId, byte p0, byte p1, byte p2)
    {
        var frame = new byte[20];
        frame[0] = 0x11;
        frame[1] = device;
        frame[2] = feature;
        frame[3] = functionAndSoftwareId;
        frame[4] = p0;
        frame[5] = p1;
        frame[6] = p2;
        return frame;
    }

    private static byte[] Error(byte device, byte feature)
    {
        var frame = Reply(device, 0xFF, 0, 0, 0, 0);
        frame[3] = feature;
        return frame;
    }

    private static byte[] NameChunk(byte device, byte feature, byte offset, string text)
    {
        var frame = Reply(device, feature, 0x1B, 0, 0, 0);
        Array.Copy(System.Text.Encoding.UTF8.GetBytes(text), 0, frame, 4, 16);
        return frame;
    }

    private sealed class FakeChannel(IReadOnlyList<byte[]> responses) : IHidppChannel
    {
        private readonly Queue<byte[]> _responses = new(responses);
        public List<byte[]> Writes { get; } = [];
        public int ReadTimeout { private get; set; }
        public int WriteTimeout { private get; set; }

        public void Write(byte[] report) => Writes.Add(report.ToArray());

        public int Read(byte[] buffer, int offset, int count)
        {
            if (_responses.Count == 0)
            {
                throw new TimeoutException();
            }

            var response = _responses.Dequeue();
            Array.Copy(response, 0, buffer, offset, Math.Min(response.Length, count));
            return Math.Min(response.Length, count);
        }
    }
}
