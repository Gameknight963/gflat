namespace gflat.Tests;

public sealed class StandardBufferWriterTests
{
    [Fact]
    public void BufferMetadataAndUnboundedRandomDrawsDoNotIntroduceExceptionBoundaries()
    {
        var files = new[] { "BufferTooSmallException.gf", "BufferWriter.gf", "PseudoRandom.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", """
                using std;
                bool Inspect(char* buffer) nothrow {
                    BufferWriter writer = new(buffer, 16);
                    PseudoRandom random = new(42ul);
                    random.NextUInt(); random.NextULong(); random.NextDouble();
                    random.Fill(buffer, 16);
                    writer.Clear();
                    return writer.Length == 0 && writer.Capacity == 15 && writer.Remaining == 15 && writer.Data[0] == '\0';
                }
                int main() => 0;
                """));
        string ir = Compiler.Emit(files);
        var function = System.Text.RegularExpressions.Regex.Match(ir,
            @"(?ms)^define [^\n]*@gflat\$Inspect\([^\n]*\{\r?\n(?<body>.*?)^\}");
        Assert.True(function.Success);
        Assert.DoesNotContain("invoke", function.Value);
        Assert.DoesNotContain("personality", function.Value);
        Assert.DoesNotContain("__gflat_eh_", function.Value);
    }

    private static void Run(string program)
    {
        var files = new[] { "core/BufferTooSmallException.gf", "core/BufferWriter.gf", "core/Formatting.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", program));
        var result = CompilerTestHelper.Run(files);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void CountedAndCStringWritesPreserveZerosAndMaintainCapacityAndTerminator()
    {
        Run("""
            using std;
            int main() {
                char[9] storage;
                BufferWriter writer = new BufferWriter(storage, 9);
                if (writer.Length != (nuint)0 || writer.Capacity != (nuint)8 || writer.Remaining != (nuint)8 || writer.Data[0] != '\0') return 1;
                writer.Write("A\0B", 3); writer.Write("cd\0ignored");
                if (writer.Length != (nuint)5 || writer.Remaining != (nuint)3) return 2;
                if (writer.Data[0] != 'A' || writer.Data[1] != '\0' || writer.Data[2] != 'B' || writer.Data[3] != 'c' || writer.Data[4] != 'd' || writer.Data[5] != '\0') return 3;
                writer.Write("xyz", 3);
                if (writer.Length != writer.Capacity || writer.Remaining != (nuint)0 || writer.Data[8] != '\0') return 4;
                writer.Write("", 0);
                writer.Clear();
                if (writer.Length != (nuint)0 || writer.Remaining != (nuint)8 || writer.Data[0] != '\0') return 5;
                writer.Write("reused");
                if (writer.Length != (nuint)6 || writer.Data[5] != 'd' || writer.Data[6] != '\0') return 6;
                return 0;
            }
            """);
    }

    [Fact]
    public void OverflowLeavesEntireBufferAndPositionUntouched()
    {
        Run("""
            using std;
            int main() {
                char[6] storage;
                for (int i = 0; i < 6; i++) storage[i] = '?';
                BufferWriter writer = new BufferWriter(storage, 6);
                writer.Write("ab");
                try { writer.Write("cdef", 4); return 1; } catch (BufferTooSmallException* error) { }
                if (writer.Length != (nuint)2 || writer.Remaining != (nuint)3) return 2;
                if (storage[0] != 'a' || storage[1] != 'b' || storage[2] != '\0' || storage[3] != '?' || storage[4] != '?' || storage[5] != '?') return 3;
                try { writer.Write("ignored", (nuint)0 - 1); return 4; } catch (BufferTooSmallException* error) { }
                writer.Write("cde");
                if (writer.Length != (nuint)5 || storage[5] != '\0') return 5;
                return 0;
            }
            """);
    }

    [Fact]
    public void ZeroStorageNeverTouchesPointerAndOneByteStorageOnlyHoldsTerminator()
    {
        Run("""
            using std;
            int main() {
                char sentinel = '?';
                BufferWriter zero = new BufferWriter(&sentinel, 0);
                if (zero.Length != (nuint)0 || zero.Capacity != (nuint)0 || zero.Remaining != (nuint)0 || sentinel != '?') return 1;
                zero.Write("", 0); zero.Clear();
                if (sentinel != '?') return 2;
                try { zero.Write("x"); return 3; } catch (BufferTooSmallException* error) { }
                if (sentinel != '?') return 4;
                BufferWriter one = new BufferWriter(&sentinel, 1);
                if (one.Capacity != (nuint)0 || sentinel != '\0') return 5;
                try { one.Write("x", 1); return 6; } catch (BufferTooSmallException* error) { }
                if (sentinel != '\0' || one.Length != (nuint)0) return 7;
                return 0;
            }
            """);
    }

    [Fact]
    public void OverlappingWritesUseMemmoveOrderInBothDirections()
    {
        Run("""
            using std;
            int main() {
                char[16] storage;
                BufferWriter writer = new BufferWriter(storage, 16);
                writer.Write("ab");
                storage[2] = 'c'; storage[3] = 'd'; storage[4] = 'e'; storage[5] = 'f';
                writer.Write(storage, 6);
                immutable(char*) expected = "ababcdef";
                for (int i = 0; i < 8; i++) if (storage[i] != expected[i]) return 1;
                if (writer.Length != (nuint)8 || storage[8] != '\0') return 2;
                writer.Clear();
                storage[2] = 'x'; storage[3] = 'y'; storage[4] = 'z';
                writer.Write(&storage[2], 3);
                if (storage[0] != 'x' || storage[1] != 'y' || storage[2] != 'z' || storage[3] != '\0') return 3;
                writer.Write(writer.Data, writer.Length);
                if (writer.Length != (nuint)6 || storage[3] != 'x' || storage[4] != 'y' || storage[5] != 'z' || storage[6] != '\0') return 4;
                return 0;
            }
            """);
    }

    [Fact]
    public void PrimitiveAndUserFormattingThroughInterfacesNeverAllocate()
    {
        Run("""
            using std;
            static class Calls { public static int Allocations; }
            namespace Memory {
                public replace void*? Allocate(nuint size) { Calls::Allocations++; return null; }
                public replace void Free(void* storage) { }
            }
            class Label : IFormattable {
                public Label() { }
                public immutable void Format(ITextWriter* output) { output.Write("<label>", 7); }
            }
            int main() {
                char[64] storage;
                BufferWriter writer = new BufferWriter(storage, 64);
                ITextWriter* output = &writer;
                int number = -42;
                number.Format(output); true.Format(output); '!'.Format(output);
                Label label = new Label();
                IFormattable* formattable = &label;
                formattable.Format(output);
                immutable(char*) expected = "-42true!<label>";
                if (writer.Length != (nuint)15 || Calls::Allocations != 0) return 1;
                for (nuint i = 0; i < writer.Length; i++) if (writer.Data[i] != expected[i]) return 2;
                return 0;
            }
            """);
    }
}
