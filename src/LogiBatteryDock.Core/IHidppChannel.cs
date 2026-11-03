using HidSharp;

namespace LogiBatteryDock.Core;

internal interface IHidppChannel
{
    int ReadTimeout { set; }
    int WriteTimeout { set; }
    void Write(byte[] report);
    int Read(byte[] buffer, int offset, int count);
}

internal sealed class HidSharpChannel(HidStream stream) : IHidppChannel
{
    public int ReadTimeout { set => stream.ReadTimeout = value; }
    public int WriteTimeout { set => stream.WriteTimeout = value; }

    public void Write(byte[] report) => stream.Write(report);

    public int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);
}

