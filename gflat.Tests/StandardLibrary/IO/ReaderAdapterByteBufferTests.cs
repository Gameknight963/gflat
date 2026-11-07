namespace gflat.Tests.Std.IO;

public sealed class ReaderAdapterByteBufferTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("READER_ADAPTER_BYTES.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedByteScratchAndLimitedByteReadsPreserveReadAheadAllowancesAndFailureStates(int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/IO/IByteReader.gf", "core/IO/IOException.gf",
                "core/IO/BufferedReader.gf", "core/IO/LimitedReader.gf"))
            .Append(new SourceFile("adapter-byte-buffers.gf", """
                using Standard;
                using Standard::IO;

                int attempts = 0;
                namespace Memory
                {
                    public replace void*? Allocate(nuint bytes) { attempts++; return null; }
                    public replace void Free(void* storage) nothrow { }
                }
                struct Input : IByteReader
                {
                    public nuint Position;
                    public int Calls;
                    public bool Fail;
                    public Input() { Position = 0; Calls = 0; Fail = false; }
                    public nuint Read(char* output, nuint count)
                    {
                        Calls++;
                        if (Fail) throw new IOException("input failed");
                        nuint received = 256 - Position;
                        if (received > count) received = count;
                        for (nuint i = 0; i < received; i++) output[i] = (char)(byte)(Position + i);
                        Position += received;
                        return received;
                    }
                }
                int main()
                {
                    for (nuint capacity = 1; capacity <= 17; capacity++)
                    {
                        Input input = new();
                        byte[19] scratch;
                        scratch[0] = (byte)77; scratch[capacity + 1] = (byte)88;
                        BufferedReader buffered = new(&input, &scratch[1], capacity);
                        nuint position = 0;
                        byte[9] output;
                        while (position < 256)
                        {
                            if (buffered.PeekByte() != (int)position) return 1;
                            output[0] = (byte)77; output[8] = (byte)88;
                            nuint received = buffered.Read(&output[1], 7);
                            if (received == 0 || received > 7) return 2;
                            for (nuint i = 0; i < received; i++) if (output[i + 1] != (byte)(position + i)) return 3;
                            position += received;
                            if (output[0] != (byte)77 || output[8] != (byte)88 || buffered.Capacity != capacity) return 4;
                        }
                        if (buffered.Read(&output[1], 7) != 0 || buffered.PeekByte() != -1) return 5;
                        if (scratch[0] != (byte)77 || scratch[capacity + 1] != (byte)88) return 6;
                    }
                    for (nuint allowance = 0; allowance <= 260; allowance++)
                    {
                        Input input = new();
                        LimitedReader limited = new(&input, allowance);
                        byte[9] output;
                        output[0] = (byte)77; output[8] = (byte)88;
                        nuint position = 0;
                        while (true)
                        {
                            nuint received = limited.Read(&output[1], 7);
                            if (received == 0) break;
                            for (nuint i = 0; i < received; i++) if (output[i + 1] != (byte)(position + i)) return 7;
                            position += received;
                        }
                        nuint expected = allowance;
                        if (expected > 256) expected = 256;
                        if (position != expected || limited.Remaining != allowance - expected) return 8;
                        if (output[0] != (byte)77 || output[8] != (byte)88) return 9;
                    }
                    if (attempts != 0) return 10;
                    {
                        Input input = new(); input.Fail = true;
                        byte scratch = (byte)0;
                        byte output = (byte)77;
                        BufferedReader buffered = new(&input, &scratch, 1);
                        for (int i = 0; i < 2; i++)
                        {
                            bool caught = false;
                            try { buffered.Read(&output, 1); }
                            catch (Exception* error) { caught = true; }
                            if (!caught || output != (byte)77 || input.Calls != 1) return 11;
                        }
                        Input second = new(); second.Fail = true;
                        LimitedReader limited = new(&second, 3);
                        for (int i = 0; i < 2; i++)
                        {
                            bool caught = false;
                            try { limited.Read(&output, 1); }
                            catch (Exception* error) { caught = true; }
                            if (!caught || limited.Remaining != 3 || output != (byte)77 || second.Calls != 1) return 12;
                        }
                        bool caught = false;
                        try { BufferedReader invalid = new(&input, &scratch, 0); }
                        catch (Exception* error) { caught = true; }
                        if (!caught) return 13;
                    }
                    return 0;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
