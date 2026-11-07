namespace gflat.Tests.Std.Collections;

public sealed class VectorConvertAllTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("VECTOR_CONVERT_ALL.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedNoncopyableInputsProduceOwnedOutputsAndCleanEveryFailurePrefix(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/vector_convert_all.gf"))
            .Append(new SourceFile("vector-convert-all.gf", """
            using Standard;
            using Standard::Collections;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int attempts = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny) return null;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            int calls = 0;
            int failAt = -1;
            int outputDestroyed = 0;
            int inputDestroyed = 0;
            struct Input
            {
                public int Key;
                public Input(int key) { Key = key; }
                ~Input() { inputDestroyed++; }
            }
            struct Output
            {
                public long Value;
                public Output(long value) { Value = value; }
                ~Output() { outputDestroyed++; }
            }
            Output Convert(immutable(Input)* input)
            {
                if (input.Key != calls) throw new Exception("out of order");
                calls++;
                if (input.Key == failAt) throw new Exception("conversion failed");
                return new Output((long)input.Key * 7l);
            }
            Vector<Output> ConvertSource(immutable(Vector<Input>)* source) => source.ConvertAll<Output>(Convert);
            int main()
            {
                for (int length = 0; length <= 8; length++)
                for (int failure = -1; failure < length; failure++)
                {
                    calls = 0; failAt = failure; outputDestroyed = 0; inputDestroyed = 0;
                    {
                        Vector<Input> source = new();
                        for (int i = 0; i < length; i++) source.Add(new Input(i));
                        Vector<Output> target = new();
                        target.Add(new Output(99l));
                        bool caught = false;
                        int beforeLive = live;
                        try { target = ConvertSource(&source); }
                        catch (Exception* error) { caught = true; }
                        if (failure < 0)
                        {
                            if (caught || calls != length || target.Count != (nuint)length || outputDestroyed != 1) return 1;
                            for (nuint i = 0; i < target.Count; i++) if (target[i].Value != (long)i * 7l) return 2;
                        }
                        else
                        {
                            if (!caught || calls != failure + 1 || outputDestroyed != failure || live != beforeLive) return 3;
                            if (target.Count != 1 || target[0].Value != 99l) return 4;
                        }
                        if (source.Count != (nuint)length || inputDestroyed != 0) return 5;
                        for (nuint i = 0; i < source.Count; i++) if (source[i].Key != (int)i) return 6;
                    }
                    int expected = failure + 1;
                    if (failure < 0) expected = length + 1;
                    if (outputDestroyed != expected || inputDestroyed != length || live != 0) return 7;
                }
                {
                    Vector<Input> source = new();
                    calls = 0; failAt = -1;
                    int before = attempts;
                    deny = true;
                    Vector<Output> empty = source.ConvertAll<Output>(Convert);
                    deny = false;
                    if (empty.Count != 0 || attempts != before || calls != 0) return 8;
                    source.Add(new Input(0));
                    deny = true;
                    bool caught = false;
                    try { Vector<Output> result = ConvertAll<Input, Output>(&source, Convert); }
                    catch (Exception* error) { caught = true; }
                    deny = false;
                    if (!caught || calls != 0 || source.Count != 1) return 9;
                }
                if (live != 0) return 10;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"ConvertAll exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
