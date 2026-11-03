using LogiBatteryDock.Core;
using Xunit;

namespace LogiBatteryDock.Core.Tests;

public sealed class HidppProtocolTests
{
    [Fact]
    public void NormalizeAcceptsReportIdWithAndWithoutLeadingPadding()
    {
        var frame = Enumerable.Range(0, 20).Select(value => (byte)value).ToArray();
        frame[0] = 0x11;
        var padded = new byte[21];
        Array.Copy(frame, 0, padded, 1, frame.Length);

        Assert.Equal(frame, HidppProtocol.Normalize(frame, 20));
        Assert.Equal(frame, HidppProtocol.Normalize(padded, 21));
    }

    [Fact]
    public void NormalizeRejectsShortOrWrongReport()
    {
        Assert.Null(HidppProtocol.Normalize(new byte[19], 19));
        var wrong = new byte[20];
        wrong[0] = 0x10;
        Assert.Null(HidppProtocol.Normalize(wrong, 20));
    }
}
