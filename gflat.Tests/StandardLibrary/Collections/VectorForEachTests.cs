namespace gflat.Tests.Std.Collections;

public sealed class VectorForEachTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("VECTOR_FOR_EACH.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActionsBorrowOriginalNoncopyableOwnersInOrderAndStopOnFailure(int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/vector_for_each.gf"))
            .Append(new SourceFile("vector-for-each.gf", """
                using Standard;
                using Standard::Collections;
                using Standard::Text;

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
                        return malloc(bytes);
                    }
                    public replace void Free(void* storage) nothrow => free(storage);
                }
                struct Owner
                {
                    public int Id;
                    public String Text;
                    public Owner(int id) { Id = id; Text = s"owned"; live++; }
                    ~Owner() { live--; }
                }
                nuint seen = 0;
                nuint[17] identities;
                int failAt = -1;
                bool wrong = false;
                void Visit(immutable(Owner)* item)
                {
                    if (item.Id != (int)seen || item.Text.Data.reinterpret<nuint>() != identities[seen]) wrong = true;
                    seen++;
                    if ((int)seen - 1 == failAt) throw new Exception("callback failed");
                }
                int main()
                {
                    for (nuint count = 0; count <= 17; count++)
                    {
                        {
                            Vector<Owner> values = new();
                            values.Reserve(32);
                            for (nuint i = 0; i < count; i++) values.Add(new Owner((int)i));
                            for (nuint i = 0; i < count; i++) identities[i] = values[i].Text.Data.reinterpret<nuint>();
                            nuint capacity = values.Capacity;
                            immutable(Vector<Owner>)* view = &values;
                            seen = 0; wrong = false; failAt = -1;
                            int before = attempts;
                            deny = true;
                            view.ForEach(Visit);
                            deny = false;
                            if (seen != count || wrong || attempts != before || live != (int)count) return 1;
                            seen = 0;
                            Standard::Collections::ForEach<Owner>(&values, Visit);
                            if (seen != count || wrong) return 2;
                            for (nuint failure = 0; failure < count; failure++)
                            {
                                seen = 0; wrong = false; failAt = (int)failure;
                                bool caught = false;
                                try { values.ForEach(Visit); }
                                catch (Exception* error) { caught = true; }
                                if (!caught || wrong || seen != failure + 1 || values.Count != count || values.Capacity != capacity) return 3;
                                for (nuint i = 0; i < count; i++)
                                    if (values[i].Id != (int)i || values[i].Text.Data.reinterpret<nuint>() != identities[i]) return 4;
                            }
                        }
                        if (live != 0) return 5;
                    }
                    return 0;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
