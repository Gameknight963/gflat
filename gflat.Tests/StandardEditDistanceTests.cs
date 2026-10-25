using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardEditDistanceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedBytesMatchIndependentBreadthFirstEditGraphWithCallerStorage(int optimization)
    {
        List<byte[]> words = [[]];
        for (int length = 1; length <= 3; length++)
        {
            byte[][] prefixes = words.Where(word => word.Length == length - 1).ToArray();
            foreach (byte[] prefix in prefixes)
            foreach (byte value in new byte[] { 0, 128 })
                words.Add([.. prefix, value]);
        }
        Assert.Equal(15, words.Count);
        var packet = new List<byte>();
        int rows = 0;
        foreach (byte[] first in words)
        foreach (byte[] second in words)
        {
            packet.Add((byte)first.Length);
            packet.Add((byte)second.Length);
            packet.Add((byte)BreadthFirstDistance(first, second));
            packet.AddRange(first);
            packet.AddRange(second);
            rows++;
        }
        Assert.Equal(225, rows);
        string hex = Convert.ToHexString(packet.ToArray());
        string program = $$$"""
            using std; using std::Collections;
            int attempts = 0;
            namespace Memory {
                public replace void*? Allocate(nuint bytes) { attempts++; return null; }
                public replace void Free(void* storage) nothrow {}
            }
            nuint ReadByte(readonly(char)* data, ref nuint position) {
                nuint result = 0;
                for (int digit = 0; digit < 2; digit++) {
                    char current = data[position]; position++;
                    int value = (int)current - (int)'0';
                    if (current >= 'A') value = (int)current - (int)'A' + 10;
                    result = result * 16 + (nuint)value;
                }
                return result;
            }
            bool KnownDistance(readonly(char)* first, nuint firstLength, readonly(char)* second, nuint secondLength, nuint expected) {
                Span<readonly(char)> a = new(first, firstLength);
                Span<readonly(char)> b = new(second, secondLength);
                nuint[8] workspace; Span<nuint> scratch = new(workspace, 8); nuint distance = 99;
                return EditDistance::TryCompute(a, b, scratch, ref distance) && distance == expected;
            }
            int main() {
                readonly(char)* packet = "{{{hex}}}"; nuint position = 0;
                char[5] first; char[5] second; char[5] savedFirst; char[5] savedSecond;
                nuint[9] storage;
                for (int row = 0; row < {{{rows}}}; row++) {
                    nuint firstLength = ReadByte(packet, ref position);
                    nuint secondLength = ReadByte(packet, ref position);
                    nuint expected = ReadByte(packet, ref position);
                    for (nuint i = 0; i < 5; i++) { first[i] = (char)85; second[i] = (char)170; }
                    for (nuint i = 0; i < firstLength; i++) first[i + 1] = (char)ReadByte(packet, ref position);
                    for (nuint i = 0; i < secondLength; i++) second[i + 1] = (char)ReadByte(packet, ref position);
                    for (nuint i = 0; i < 5; i++) { savedFirst[i] = first[i]; savedSecond[i] = second[i]; }
                    Span<readonly(char)> a = new(&first[1], firstLength);
                    Span<readonly(char)> b = new(&second[1], secondLength);
                    nuint required = 0;
                    if (firstLength != 0 && secondLength != 0) {
                        required = firstLength;
                        if (secondLength < required) required = secondLength;
                        required++;
                    }
                    for (int mode = 0; mode < 4; mode++) {
                        nuint capacity = 0;
                        if (mode == 1 && required != 0) capacity = required - 1;
                        if (mode == 2) capacity = required;
                        if (mode == 3) capacity = required + 3;
                        for (nuint i = 0; i < 9; i++) storage[i] = 12345;
                        Span<nuint> scratch = new(&storage[1], capacity);
                        nuint distance = 99;
                        bool result = EditDistance::TryCompute(a, b, scratch, ref distance);
                        bool success = capacity >= required;
                        if (result != success || (result && distance != expected) || (!result && distance != 99)) return 1;
                        for (nuint i = 0; i < 9; i++) {
                            if ((!success || i == 0 || i > required) && storage[i] != 12345) return 2;
                        }
                        for (nuint i = 0; i < 5; i++)
                            if (first[i] != savedFirst[i] || second[i] != savedSecond[i]) return 3;
                    }
                }
                if (position != {{{hex.Length}}}) return 4;
                char[5] overlap = ['a', '\0', 'a', (char)255, 'a'];
                Span<readonly(char)> left = new(overlap, 3);
                Span<readonly(char)> right = new(&overlap[2], 3);
                nuint[4] overlapScratch; Span<nuint> scratch = new(overlapScratch, 4); nuint distance = 99;
                if (!EditDistance::TryCompute(left, right, scratch, ref distance) || distance != 1) return 5;
                if (!EditDistance::TryCompute(right, left, scratch, ref distance) || distance != 1) return 6;
                if (overlap[0] != 'a' || overlap[1] != '\0' || overlap[2] != 'a' || overlap[3] != (char)255 || overlap[4] != 'a') return 7;
                Span<readonly(char)> empty = new(); Span<nuint> none = new();
                if (!EditDistance::TryCompute(empty, empty, none, ref distance) || distance != 0) return 8;
                if (!EditDistance::TryCompute(empty, left, none, ref distance) || distance != 3) return 9;
                if (!EditDistance::TryCompute(left, empty, none, ref distance) || distance != 3) return 10;
                if (!KnownDistance("kitten", 6, "sitting", 7, 3) || !KnownDistance("ab", 2, "ba", 2, 2)) return 11;
                if (!KnownDistance("A", 1, "a", 1, 1)) return 12;
                char[2] composed = [(char)195, (char)169];
                char[3] decomposed = ['e', (char)204, (char)129];
                if (!KnownDistance(composed, 2, "e", 1, 2) || !KnownDistance(composed, 2, decomposed, 3, 3)) return 13;
                if (attempts != 0) return 14;
                return 0;
            }
            """;
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"Edit distance exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void ReachableDistanceKernelIsNothrowAndHasNoAllocatorCollectorOrExceptionCalls()
    {
        string ir = Compiler.Emit(Sources("""
            using std; using std::Collections;
            int main() {
                Span<readonly(char)> first = new("kitten", 6);
                Span<readonly(char)> second = new("sitting", 7);
                nuint[7] row; Span<nuint> scratch = new(row, 7); nuint distance = 99;
                if (!EditDistance::TryCompute(first, second, scratch, ref distance)) return 1;
                return (int)distance - 3;
            }
            """));
        // Span constructors/checked siblings retain their ordinary EH support.
        // Only this kernel and its metadata getters make the no-runtime-call claim.
        string[] bodies = Regex.Matches(ir, @"(?ms)^define[^\r\n]*\$EditDistance\$[^\r\n]*\{.*?^\}")
            .Select(match => match.Value).ToArray();
        Assert.Equal(2, bodies.Length);
        foreach (string body in bodies)
        {
            Assert.Contains("nounwind", body.Split('\n')[0]);
            Assert.DoesNotMatch(@"\b(invoke|landingpad|catchpad|cleanuppad)\b", body);
            Assert.DoesNotMatch(@"@(malloc|free|__gflat[^ (]*|[^ (]*(Memory|Collect|GC)[^ (]*)\(", body);
        }
        string[] getters = Regex.Matches(ir, @"(?ms)^define[^\r\n]*\$Span\$[^\r\n]*\$get\$(Data|Length)\([^\r\n]*\{.*?^\}")
            .Select(match => match.Value).ToArray();
        Assert.NotEmpty(getters);
        foreach (string getter in getters)
        {
            Assert.Contains("nounwind", getter.Split('\n')[0]);
            Assert.DoesNotContain("call ", getter);
            Assert.DoesNotContain("invoke ", getter);
        }
    }

    private static IEnumerable<SourceFile> Sources(string program)
        => new[] { "EditDistance.gf", "Span.gf", "Collections/Enumerable.gf", "Collections/IEnumerable.gf" }
            .Select(relative => Path.Combine(AppContext.BaseDirectory, "Std", "core", relative))
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("edit-distance.gf", program));

    // Search an edit graph directly; this deliberately does not implement the DP recurrence.
    private static int BreadthFirstDistance(byte[] first, byte[] second)
    {
        if (first.SequenceEqual(second)) return 0;
        byte[] alphabet = first.Concat(second).Distinct().ToArray();
        int bound = Math.Max(first.Length, second.Length);
        var pending = new Queue<(byte[] Value, int Depth)>();
        var seen = new HashSet<string> { Convert.ToHexString(first) };
        pending.Enqueue((first, 0));
        while (pending.TryDequeue(out var current))
        {
            if (current.Depth == bound) continue;
            foreach (byte[] next in Neighbors(current.Value, alphabet))
            {
                if (!seen.Add(Convert.ToHexString(next))) continue;
                int depth = current.Depth + 1;
                if (next.SequenceEqual(second)) return depth;
                pending.Enqueue((next, depth));
            }
        }
        throw new InvalidOperationException("Constructive edit bound did not reach target");
    }

    private static IEnumerable<byte[]> Neighbors(byte[] value, byte[] alphabet)
    {
        for (int index = 0; index <= value.Length; index++)
        foreach (byte symbol in alphabet)
        {
            byte[] inserted = new byte[value.Length + 1];
            value.AsSpan(0, index).CopyTo(inserted);
            inserted[index] = symbol;
            value.AsSpan(index).CopyTo(inserted.AsSpan(index + 1));
            yield return inserted;
        }
        for (int index = 0; index < value.Length; index++)
        {
            yield return value.Where((_, position) => position != index).ToArray();
            foreach (byte symbol in alphabet)
            {
                if (symbol == value[index]) continue;
                byte[] substituted = (byte[])value.Clone();
                substituted[index] = symbol;
                yield return substituted;
            }
        }
    }
}
