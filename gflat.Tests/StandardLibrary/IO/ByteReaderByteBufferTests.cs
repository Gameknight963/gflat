namespace gflat.Tests.Std.IO;

public sealed class ByteReaderByteBufferTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("BYTE_READER_BUFFERS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShortReadsEofAndCopyUseByteBuffersWithoutChangingTheirContracts(int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/IO/IByteReader.gf", "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf",
                "core/IO/byte_reader.gf", "core/IO/byte_reader_read_at_least.gf", "core/IO/byte_reader_byte_buffers.gf"))
            .Append(new SourceFile("reader-byte-buffers.gf", """
                using Standard;
                using Standard::IO;
                using Standard::Text;

                struct Reader : IByteReader
                {
                    public nuint Length;
                    public nuint Position;
                    public nuint Chunk;
                    public bool Invalid;
                    public Reader(nuint length, nuint chunk) { Length = length; Chunk = chunk; Position = 0; Invalid = false; }
                    public nuint Read(char* data, nuint count)
                    {
                        if (Invalid) return count + 1;
                        nuint received = Length - Position;
                        if (received > count) received = count;
                        if (received > Chunk) received = Chunk;
                        for (nuint i = 0; i < received; i++) data[i] = (char)(byte)(Position + i);
                        Position += received;
                        return received;
                    }
                }
                int main()
                {
                    for (nuint length = 0; length <= 20; length++)
                    for (nuint request = 0; request <= 20; request++)
                    for (nuint chunk = 1; chunk <= 5; chunk++)
                    {
                        byte[40] output;
                        for (nuint i = 0; i < 40; i++) output[i] = (byte)165;
                        Reader input = new(length, chunk);
                        IByteReader* reader = &input;
                        bool caught = false;
                        try { reader.ReadExactly(&output[1], request); }
                        catch (Exception* error) { caught = true; }
                        if (caught != (request > length)) return 1;
                        nuint expected = request;
                        if (expected > length) expected = length;
                        if (input.Position != expected) return 2;
                        for (nuint i = 0; i < 40; i++)
                        {
                            byte value = (byte)165;
                            if (i > 0 && i - 1 < expected) value = (byte)(i - 1);
                            if (output[i] != value) return 3;
                        }
                        input = new Reader(length, chunk);
                        for (nuint i = 0; i < 40; i++) output[i] = (byte)165;
                        nuint capacity = request + 5;
                        expected = 0;
                        while (expected < request && expected < length)
                        {
                            nuint step = length - expected;
                            if (step > chunk) step = chunk;
                            if (step > capacity - expected) step = capacity - expected;
                            expected += step;
                        }
                        nuint received = reader.ReadAtLeast(&output[1], capacity, request, false);
                        if (received != expected || input.Position != expected) return 4;
                        for (nuint i = 0; i < 40; i++)
                        {
                            byte value = (byte)165;
                            if (i > 0 && i - 1 < expected) value = (byte)(i - 1);
                            if (output[i] != value) return 5;
                        }
                    }
                    for (nuint chunk = 1; chunk <= 7; chunk++)
                    {
                        Reader input = new(256, chunk);
                        String copied = new();
                        byte[9] scratch;
                        scratch[0] = (byte)77; scratch[8] = (byte)88;
                        nuint total = 0;
                        if (chunk % 2 == 0) total = Standard::IO::CopyTo(&input, &copied, &scratch[1], 7);
                        else
                        {
                            IByteReader* reader = &input;
                            total = reader.CopyTo(&copied, &scratch[1], 7);
                        }
                        if (total != 256 || copied.Length != 256 || scratch[0] != (byte)77 || scratch[8] != (byte)88) return 6;
                        for (nuint i = 0; i < 256; i++) if ((byte)copied.Data[i] != (byte)i) return 7;
                    }
                    {
                        Reader shortInput = new(1, 1);
                        IByteReader* reader = &shortInput;
                        byte[3] output = [(byte)77, (byte)88, (byte)99];
                        bool caught = false;
                        try { reader.ReadAtLeast(&output[0], 3, 2); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || shortInput.Position != 1 || output[0] != (byte)0 || output[1] != (byte)88) return 9;
                    }
                    Reader invalid = new(0, 1);
                    invalid.Invalid = true;
                    IByteReader* reader = &invalid;
                    byte output = (byte)77;
                    for (int mode = 0; mode < 3; mode++)
                    {
                        bool caught = false;
                        try
                        {
                            if (mode == 0) reader.ReadExactly(&output, 0);
                            else if (mode == 1) reader.ReadAtLeast(&output, 1, 0);
                            else reader.ReadAtLeast(&output, 1, 2);
                        }
                        catch (Exception* error) { caught = true; }
                        if (!caught || output != (byte)77 || invalid.Position != 0) return 8;
                    }
                    return 0;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
