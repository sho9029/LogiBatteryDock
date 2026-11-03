using System.Diagnostics;
using System.Text;

namespace GHubTray.Core;

internal sealed class HidppSession(IHidppChannel channel)
{
    private static readonly ushort[] BatteryFeatures = [0x1004, 0x1000, 0x1001];

    public bool Ping(byte index, CancellationToken cancellationToken = default)
    {
        const byte echo = 0xA7;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reply = Exchange(
                HidppProtocol.Request(index, 0, 1, 0, 0, echo),
                index,
                0,
                1,
                350,
                cancellationToken);
            if (reply is not null && reply[6] == echo)
            {
                return true;
            }
        }
        return false;
    }

    public (ushort Id, byte Index)? ResolveBatteryFeature(
        byte index,
        CancellationToken cancellationToken = default)
    {
        foreach (var id in BatteryFeatures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var featureIndex = ResolveFeature(index, id, cancellationToken);
            if (featureIndex != 0)
            {
                return (id, featureIndex);
            }
        }
        return null;
    }

    public byte[]? ReadBattery(
        byte index,
        ushort id,
        byte featureIndex,
        CancellationToken cancellationToken = default)
    {
        var function = id == 0x1004 ? (byte)1 : (byte)0;
        return Exchange(
            HidppProtocol.Request(index, featureIndex, function),
            index,
            featureIndex,
            function,
            500,
            cancellationToken);
    }

    public string? ReadName(byte index, CancellationToken cancellationToken = default)
    {
        var featureIndex = ResolveFeature(index, 0x0005, cancellationToken);
        if (featureIndex == 0)
        {
            return null;
        }

        var info = Exchange(
            HidppProtocol.Request(index, featureIndex, 0),
            index,
            featureIndex,
            0,
            350,
            cancellationToken);
        if (info is null || info[4] == 0)
        {
            return null;
        }

        var expected = info[4];
        var bytes = new List<byte>(expected);
        // Use an int counter; a malformed 255-byte name must not wrap the protocol offset.
        for (var offset = 0; offset < expected; offset += 16)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var part = Exchange(
                HidppProtocol.Request(index, featureIndex, 1, (byte)offset),
                index,
                featureIndex,
                1,
                350,
                cancellationToken);
            if (part is null)
            {
                break;
            }
            bytes.AddRange(part.Skip(4).Take(Math.Min(16, expected - offset)));
        }

        return bytes.Count == 0 ? null : Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\0');
    }

    private byte ResolveFeature(byte index, ushort id, CancellationToken cancellationToken)
    {
        var reply = Exchange(
            HidppProtocol.Request(index, 0, 0, (byte)(id >> 8), (byte)id, 0),
            index,
            0,
            0,
            350,
            cancellationToken);
        return reply is null ? (byte)0 : reply[4];
    }

    private byte[]? Exchange(
        byte[] request,
        byte deviceIndex,
        byte featureIndex,
        byte functionId,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            channel.WriteTimeout = 350;
            channel.Write(request);
            var budget = Stopwatch.StartNew();
            while (budget.ElapsedMilliseconds < timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                channel.ReadTimeout = Math.Max(1, timeoutMs - (int)budget.ElapsedMilliseconds);
                var buffer = new byte[64];
                var count = channel.Read(buffer, 0, buffer.Length);
                var reply = HidppProtocol.Normalize(buffer, count);
                if (reply is null || reply[1] != deviceIndex)
                {
                    continue;
                }
                if (reply[2] is 0x8F or 0xFF)
                {
                    // HID++ error frames echo the requested feature index at byte 3.
                    if (reply[3] == featureIndex)
                    {
                        return null;
                    }
                    continue;
                }
                if (reply[2] == featureIndex
                    && reply[3] == ((functionId << 4) | HidppProtocol.SoftwareId))
                {
                    return reply;
                }
            }
        }
        catch (TimeoutException)
        {
        }
        catch (IOException)
        {
        }
        return null;
    }
}
