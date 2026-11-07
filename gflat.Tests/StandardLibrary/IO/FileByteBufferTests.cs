namespace gflat.Tests.Std.IO;

public sealed class FileByteBufferTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("FILE_BYTE_BUFFERS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConcreteFileByteBuffersRoundTripAndKeepCopyAndClosedStateChecks(int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/IO/IByteReader.gf", "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf",
                "core/IO/SeekOrigin.gf", "libc/IO/FileStream.gf"))
            .Append(new SourceFile("file-byte-buffers.gf", """
                using Standard;
                using Standard::IO;
                using Standard::Text;

                int main()
                {
                    byte[256] input;
                    for (nuint i = 0; i < 256; i++) input[i] = (byte)i;
                    FileStream file = FileStream::OpenTemporary();
                    for (nuint length = 0; length <= 256; length++)
                    {
                        file.SetLength(0l);
                        file.Position = 0l;
                        file.Write(&input[0], length);
                        file.WriteByte((byte)255);
                        file.Position = 0l;
                        byte[258] output;
                        for (nuint i = 0; i < 258; i++) output[i] = (byte)165;
                        file.ReadExactly(&output[1], length);
                        for (nuint i = 0; i < length; i++) if (output[i + 1] != (byte)i) return 1;
                        if (output[0] != (byte)165 || output[length + 1] != (byte)165 || file.ReadByte() != 255) return 2;
                        file.Position = 0l;
                        if (file.Read(&output[1], length + 1) != length + 1 || output[length + 1] != (byte)255) return 3;
                        file.Position = 0l;
                        FileStream copied = FileStream::OpenTemporary();
                        byte[9] scratch;
                        scratch[0] = (byte)77; scratch[8] = (byte)88;
                        if (file.CopyTo(&copied, &scratch[1], 7) != length + 1) return 4;
                        copied.Position = 0l;
                        copied.ReadExactly(&output[0], length + 1);
                        for (nuint i = 0; i < length; i++) if (output[i] != input[i]) return 5;
                        if (output[length] != (byte)255 || scratch[0] != (byte)77 || scratch[8] != (byte)88) return 6;
                        file.Position = 0l;
                        String text = new();
                        if (file.CopyTo(&text, &scratch[1], 7) != length + 1 || text.Length != length + 1) return 7;
                        for (nuint i = 0; i < length; i++) if ((byte)text.Data[i] != input[i]) return 8;
                    }
                    file.Position = 256l;
                    byte[2] tail = [(byte)77, (byte)88];
                    bool caught = false;
                    try { file.ReadExactly(&tail[0], 2); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || tail[0] != (byte)255 || tail[1] != (byte)88) return 9;
                    file.Position = 0l;
                    caught = false;
                    try { file.CopyTo(&file, &tail[0], 2); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || file.Position != 0l) return 10;
                    file.Close();
                    for (int mode = 0; mode < 3; mode++)
                    {
                        caught = false;
                        try
                        {
                            if (mode == 0) file.Read(&tail[0], 0);
                            else if (mode == 1) file.ReadExactly(&tail[0], 0);
                            else file.Write(&tail[0], 0);
                        }
                        catch (Exception* error) { caught = true; }
                        if (!caught) return 11;
                    }
                    return 0;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
