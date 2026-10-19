namespace gflat.Tests;

public class StandardVectorTests
{
    [Fact]
    public void FailedReservePreservesElementsAndUsesEmergencyReserve()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            extern void* malloc(nuint size);
            extern void free(void* pointer);
            namespace Memory {
                public replace void*? Allocate(nuint size) { if (size != (nuint)16) return null; return malloc(size); }
                public replace void Free(void* pointer) { free(pointer); }
            }
            int main() {
                Vector<int> values = new Vector<int>();
                values.Add(11); values.Add(22); values.Add(33); values.Add(44);
                nuint capacity = values.Capacity;
                int* original = &values[(nuint)0];
                try { values.Reserve(100); return 1; }
                catch (ref BadAllocException error) { if (error.GetCode() != 1) return 2; }
                if (values.Count != (nuint)4 || values.Capacity != capacity || &values[(nuint)0] != original) return 3;
                if (values[(nuint)0] != 11 || values[(nuint)1] != 22 || values[(nuint)2] != 33 || values[(nuint)3] != 44) return 4;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    private static IEnumerable<SourceFile> Sources(string program) => new[] { "Collections/IEnumerable.gf", "Collections/Enumerable.gf", "IStringConvertable.gf", "String.gf", "Vector.gf", "Span.gf" }
        .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
        .Append(new SourceFile("main.gf", program));

    [Fact]
    public void AddingAStringCopiesItWhenNotExplicitlyMoved()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                Vector<String> values = new Vector<String>();
                String original = s"abc";
                values.Add(original);
                original[(nuint)0] = 'z';
                if (values[(nuint)0].Data[0] != 'a') return 1;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void StringsAreOrdinaryElementsWithExplicitMovement()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                Vector<String> words = new Vector<String>();
                words.Add(s"one");
                String second = s"two";
                words.Add(move second);
                second = s"reinitialized";
                if (second.Length != (nuint)13) return 1;
                for (int i = 0; i < 20; i++) words.Add(s"extra");
                words[(nuint)0].Append("!", 1);
                words[(nuint)1] = s"replacement";
                readonly(Vector<String>)* read = &words;
                if ((*read)[(nuint)0].Length != (nuint)4) return 2;
                String last = words.Pop();
                if (last.Length != (nuint)5 || words.Count != (nuint)21) return 3;
                Vector<String> moved = move words;
                if (moved.Count != (nuint)21) return 4;
                words = new Vector<String>();
                words.Add(s"reused");
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void PrimitiveElementsUseTheSameContainer()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                Vector<int> values = new Vector<int>();
                for (int i = 0; i < 100; i++) values.Add(i);
                if (values.Pop() != 99 || values[(nuint)50] != 50) return 1;
                values[(nuint)0]++;
                if (values[(nuint)0] != 1) return 2;
                values.Clear();
                if (values.Count != (nuint)0) return 3;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }
    [Fact]
    public void EmptyElementsCanGrowAndBeRemoved()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            struct Empty {}
            int main() {
                Vector<Empty> values = new Vector<Empty>();
                for (int i = 0; i < 10; i++) values.Add(new Empty());
                values.Pop();
                if (values.Count != (nuint)9) return 1;
                values.Clear();
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }
    [Fact]
    public void FailedGrowthCleansItsArgumentAndPreservesExistingElements()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            extern void* malloc(nuint size);
            extern void free(void* pointer);
            namespace Memory {
                public replace void*? Allocate(nuint size) {
                    if (size > (nuint)256) return null;
                    return malloc(size);
                }
                public replace void Free(void* pointer) { free(pointer); }
            }
            struct R {
                int* released;
                int id;
                public R(int* counter, int value) { released = counter; id = value; }
                ~R() { if (id != 0) (*released)++; }
            }
            int main() {
                int released = 0;
                nuint retained = 0;
                {
                    Vector<R> values = new Vector<R>();
                    try {
                        for (int i = 1; i < 100; i++) values.Add(new R(&released, i));
                        return 1;
                    } catch (ref BadAllocException error) { if (error.GetCode() != 1) return 4; }
                    retained = values.Count;
                    if (released != 1 || retained == (nuint)0) return 2;
                }
                if ((nuint)released != retained + (nuint)1) return 3;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }
}
