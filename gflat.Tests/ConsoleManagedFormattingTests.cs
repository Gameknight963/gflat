namespace gflat.Tests;

public sealed class ConsoleManagedFormattingTests
{
    [Fact]
    public void NullableManagedReferenceRequiresANullCheck()
    {
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() =>
            Compiler.Check(Sources("""
                class Message : IFormattable
                {
                    public immutable void Format(ITextWriter* output) {}
                }
                int main()
                {
                    Message^? message = null;
                    Console::WriteLine(message);
                    return 0;
                }
                """), options: new() { NoStdLib = true }));
        Assert.Contains("No matching overload", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TemporaryReturnedAndNamedReferencesSurviveCollectionDuringFormatting(int optimization)
    {
        ExecutionResult result = Run("""
            using Standard;

            nuint baseline = 0;
            int calls = 0;
            int sum = 0;
            class Message : IFormattable
            {
                public int Number;
                public Message(int number) { Number = number; }
                public immutable void Format(ITextWriter* output)
                {
                    if (GC::Collect() != 0 || TracingCollector::ManagedCount != baseline + 1)
                        throw new Exception("Lost message during formatting");
                    calls++;
                    sum += Number;
                    output.Write("item", 4);
                }
            }
            Message^ Create() => new^ Message(2);
            void Named()
            {
                Message^ message = new^ Message(3);
                Console::Write(message);
                if (GC::Collect() != 0 || message.Number != 3)
                    throw new Exception("Named reference was consumed");
                message.Number = 4;
                Console::WriteLine(message);
                if (message.Number != 4) throw new Exception("Changed managed object");
            }
            int main()
            {
                GC::Collect();
                baseline = TracingCollector::ManagedCount;
                Console::WriteLine(new^ Message(1));
                if (GC::Collect() != 1 || TracingCollector::ManagedCount != baseline) return 1;
                Console::WriteLine(Create());
                if (GC::Collect() != 1 || TracingCollector::ManagedCount != baseline) return 2;
                Named();
                if (GC::Collect() != 1 || TracingCollector::ManagedCount != baseline) return 3;
                if (calls != 4 || sum != 10) return 4;
                Console::Flush();
                return 0;
            }
            """, optimization);
        string newline = Environment.NewLine;
        Assert.Equal($"item{newline}item{newline}itemitem{newline}", result.StandardOutput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BaseInterfaceAndGuardedNullableViewsKeepVirtualFormatting(int optimization)
    {
        ExecutionResult result = Run("""
            using Standard;

            nuint baseline = 0;
            int calls = 0;
            class Base : IFormattable
            {
                public immutable virtual void Format(ITextWriter* output) { output.Write("base", 4); }
            }
            class Derived : Base
            {
                public immutable override void Format(ITextWriter* output)
                {
                    if (GC::Collect() != 0 || TracingCollector::ManagedCount != baseline + 1)
                        throw new Exception("Lost formatted view");
                    calls++;
                    output.Write("derived", 7);
                }
            }
            void Views()
            {
                Derived^ value = new^ Derived();
                Base^ baseView = value;
                IFormattable^ interfaceView = value;
                Console::WriteLine(baseView);
                Console::WriteLine(interfaceView);
                Derived^? optional = value;
                if (optional != null) Console::WriteLine(optional);
                else throw new Exception("Unexpected null");
            }
            int main()
            {
                GC::Collect();
                baseline = TracingCollector::ManagedCount;
                Views();
                if (GC::Collect() != 1 || TracingCollector::ManagedCount != baseline || calls != 3) return 10;
                Console::Flush();
                return 0;
            }
            """, optimization);
        Assert.Equal(string.Concat(Enumerable.Repeat("derived" + Environment.NewLine, 3)), result.StandardOutput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitCollectorReceivesParameterRootNotifications(int optimization)
    {
        ExecutionResult result = Run("""
            using Standard;

            nuint baseline = 0;
            int calls = 0;
            // Reuse tracing storage while observing the distinct provider's notifications.
            static class OtherCollector : IGarbageCollector
            {
                public static int Added;
                public static int Removed;
                public static void* Allocate(nuint bytes, nuint alignment)
                    => TracingCollector::Allocate(bytes, alignment);
                public static void ReferenceAdded(void* slot) nothrow
                {
                    Added++;
                    TracingCollector::ReferenceAdded(slot);
                }
                public static void ReferenceRemoved(void* slot) nothrow
                {
                    Removed++;
                    TracingCollector::ReferenceRemoved(slot);
                }
                public static void RegisterDestructor(void* storage, void(void*)* destroy) nothrow => TracingCollector::RegisterDestructor(storage, destroy);
                public static nuint Collect() => TracingCollector::Collect();
            }
            class Message : IFormattable
            {
                public int Number;
                public Message(int number) { Number = number; }
                public immutable void Format(ITextWriter* output)
                {
                    if (OtherCollector::Collect() != 0 || TracingCollector::ManagedCount != baseline + 1)
                        throw new Exception("Lost custom-collector message");
                    calls++;
                    output.Write("other", 5);
                }
            }
            void Named()
            {
                managed<Message, OtherCollector> message = new^ Message(7) with OtherCollector;
                int added = OtherCollector::Added;
                int removed = OtherCollector::Removed;
                Console::WriteLine(message);
                if (OtherCollector::Added <= added || OtherCollector::Removed <= removed || message.Number != 7)
                    throw new Exception("Collector identity lost in parameter");
            }
            int main()
            {
                GC::Collect();
                baseline = TracingCollector::ManagedCount;
                Console::WriteLine(new^ Message(6) with OtherCollector);
                if (OtherCollector::Collect() != 1 || TracingCollector::ManagedCount != baseline) return 20;
                Named();
                if (OtherCollector::Collect() != 1 || TracingCollector::ManagedCount != baseline || calls != 2) return 21;
                Console::Flush();
                return 0;
            }
            """, optimization);
        Assert.Equal("other" + Environment.NewLine + "other" + Environment.NewLine, result.StandardOutput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingFormatterSkipsNewlineAndReleasesTemporaryRoots(int optimization)
    {
        ExecutionResult result = Run("""
            using Standard;

            nuint baseline = 0;
            class Broken : IFormattable
            {
                public immutable void Format(ITextWriter* output)
                {
                    if (GC::Collect() != 0 || TracingCollector::ManagedCount != baseline + 1)
                        throw new Exception("Lost throwing formatter");
                    output.Write("prefix", 6);
                    throw new Exception("Formatting failed", 71);
                }
            }
            int main()
            {
                GC::Collect();
                baseline = TracingCollector::ManagedCount;
                bool caught = false;
                try { Console::WriteLine(new^ Broken()); }
                catch (ref Exception error) { caught = error.GetCode() == 71; }
                if (!caught || GC::Collect() != 1 || TracingCollector::ManagedCount != baseline) return 30;
                Console::Write("tail");
                Console::Flush();
                return 0;
            }
            """, optimization);
        Assert.Equal("prefixtail", result.StandardOutput);
    }

    private static ExecutionResult Run(string code, int optimization)
    {
        string ir = Compiler.Emit(Sources(code), options: new() { NoStdLib = true, InternalizeFunctions = true });
        return CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("console-managed-formatting.gf", code));
}
