using System.Text;
using gflat.VisualStudio;

namespace gflat.Tests;

public class ExceptionDebugReportTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void ReadsTypeMessageAndCodeWithoutExecutingDebuggeeMethods(ulong stage, bool unhandled)
    {
        byte[] memory = new byte[1024];
        ulong[] packet = [1, stage, 128, 256, 8, 16];
        for (int i = 0; i < packet.Length; i++) BitConverter.GetBytes(packet[i]).CopyTo(memory, i * 8);
        BitConverter.GetBytes(512UL).CopyTo(memory, 136);
        BitConverter.GetBytes(17).CopyTo(memory, 144);
        Encoding.UTF8.GetBytes("App::Problem\0").CopyTo(memory, 256);
        Encoding.UTF8.GetBytes("A useful message: é\0").CopyTo(memory, 512);
        var report = GflatExceptionReport.Read((address, count) => memory.Skip((int)address).Take(count).ToArray(), 0);
        Assert.NotNull(report);
        Assert.Equal(unhandled, report.Unhandled);
        Assert.Equal("App::Problem", report.Type);
        Assert.Equal("A useful message: é\nCode: 17", report.Message);
    }

    [Theory]
    [InlineData(0, 1, 48)]
    [InlineData(2, 1, 48)]
    [InlineData(1, 0, 48)]
    [InlineData(1, 3, 48)]
    [InlineData(1, 1, 47)]
    public void RejectsInvalidOrTruncatedPackets(ulong version, ulong stage, int length)
    {
        byte[] packet = new byte[length];
        BitConverter.GetBytes(version).CopyTo(packet, 0);
        BitConverter.GetBytes(stage).CopyTo(packet, 8);
        Assert.Null(GflatExceptionReport.Read((address, count) => packet, 0));
    }
}
