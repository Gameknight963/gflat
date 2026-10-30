namespace gflat.Tests.Std.Binary;

public sealed class BinaryCursorPeekSliceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HostBoundsAndSliceOraclePreserveParentAndNormalizeEmptyAndShortRequests(int optimization)
    {
        byte[] backing = Enumerable.Range(0, 32).Select(i => (byte)(i * 37 + 11)).ToArray();
        var packet = new List<string>(); int rows = 0;
        foreach (int length in new[] { 0, 1, 2, 7, 8, 15, 16, 17 })
        foreach (int position in new[] { 0, length / 2, length }.Distinct())
        foreach (ulong count in new ulong[] { 0, 1, 2, (ulong)length, (ulong)length + 1, ulong.MaxValue }.Distinct())
        {
            bool fits = count <= (ulong)(length - position);
            packet.AddRange([$"(nuint){length}", $"(nuint){position}",
                count == ulong.MaxValue ? "((nuint)0 - (nuint)1)" : $"(nuint){count}", fits ? "(nuint)1" : "(nuint)0"]);
            if (fits) packet.AddRange(backing.AsSpan(3 + position, (int)count).ToArray().Select(value => $"(nuint){value}"));
            rows++;
        }
        string bytes = string.Join(',', backing.Select(value => $"(byte){value}"));
        Run($$"""
            using std::Binary;
            int main() nothrow {
                nuint[{{packet.Count}}] packet = [{{string.Join(',', packet)}}]; nuint at = 0;
                byte[32] data = [{{bytes}}]; byte[4] other = [(byte)222, (byte)173, (byte)190, (byte)239];
                for (int row = 0; row < {{rows}}; row++) {
                    nuint length = packet[at++]; nuint position = packet[at++]; nuint count = packet[at++]; bool expected = packet[at++] != 0;
                    BinaryBufferReader parent = new(&data[3], length); if (!parent.TrySeek(position)) return 1;
                    BinaryBufferReader child = new(other, 4); if (!child.TrySkip(2)) return 2;
                    bool success = parent.TryPeekSlice(count, ref child);
                    if (success != expected || parent.Data != &data[3] || parent.Length != length || parent.Position != position || parent.Remaining != length - position) return 3;
                    if (!expected || count == 0) {
                        if (child.Data != null || child.Length != 0 || child.Position != 0 || child.Remaining != 0) return 4;
                    } else {
                        if (child.Data != &data[3 + position] || child.Length != count || child.Position != 0 || child.Remaining != count) return 5;
                    }
                    if (expected) {
                        for (nuint i = 0; i < count; i++) {
                            byte value = (byte)0; if (!child.TryReadByte(ref value) || (nuint)value != packet[at++]) return 6;
                        }
                        if (child.Position != count || child.Remaining != 0) return 7;
                    }
                    if (parent.Data != &data[3] || parent.Length != length || parent.Position != position || parent.Remaining != length - position) return 8;
                }
                if (at != {{packet.Count}}) return 9;
                for (nuint i = 0; i < 32; i++) if (data[i] != (byte)(i * 37 + 11)) return 10;
                if (other[0] != (byte)222 || other[1] != (byte)173 || other[2] != (byte)190 || other[3] != (byte)239) return 11;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ProtectedParentsYieldIndependentLiveChildrenWithoutMutatingBackingOrParentMetadata(int optimization)
        => Run("""
            using std::Binary;
            bool PeekReadOnly(readonly(BinaryBufferReader)* reader, nuint count, ref BinaryBufferReader child) nothrow
                => reader.TryPeekSlice(count, ref child);
            bool PeekImmutable(immutable(BinaryBufferReader)* reader, nuint count, ref BinaryBufferReader child) nothrow
                => reader.TryPeekSlice(count, ref child);
            int main() nothrow {
                byte[8] data = [(byte)91, (byte)10, (byte)20, (byte)30, (byte)40, (byte)50, (byte)60, (byte)92];
                byte[8] saved = [(byte)91, (byte)10, (byte)20, (byte)30, (byte)40, (byte)50, (byte)60, (byte)92];
                BinaryBufferReader parent = new(&data[1], 6); if (!parent.TrySkip(1)) return 1;
                readonly(BinaryBufferReader) shallow = parent; immutable(BinaryBufferReader) deep = parent;
                BinaryBufferReader first = new(); BinaryBufferReader second = new(); BinaryBufferReader third = new();
                if (!parent.TryPeekSlice(4, ref first) || !PeekReadOnly(&shallow, 4, ref second) || !PeekImmutable(&deep, 4, ref third)) return 2;
                if (first.Data != &data[2] || second.Data != &data[2] || third.Data != &data[2] || first.Length != 4 || second.Length != 4 || third.Length != 4) return 3;
                if (parent.Position != 1 || shallow.Position != 1 || deep.Position != 1 || first.Position != 0 || second.Position != 0 || third.Position != 0) return 4;
                byte value = (byte)0;
                if (!first.TryReadByte(ref value) || value != (byte)20 || !second.TrySeek(3) || third.Position != 0) return 5;
                if (!parent.TrySeek(4) || first.Position != 1 || second.Position != 3 || third.Position != 0 || shallow.Position != 1 || deep.Position != 1) return 6;
                for (nuint i = 0; i < 8; i++) if (data[i] != saved[i]) return 7;
                // This deliberate external edit is visible in all captured views.
                data[3] = (byte)231;
                if (!first.TryReadByte(ref value) || value != (byte)231) return 8;
                if (!third.TryReadByte(ref value) || value != (byte)20 || !third.TryReadByte(ref value) || value != (byte)231) return 9;
                if (!second.TryReadByte(ref value) || value != (byte)50 || first.Position != 2 || third.Position != 2) return 10;
                if (parent.Data != &data[1] || shallow.Data != &data[1] || deep.Data != &data[1] || parent.Length != 6 || shallow.Length != 6 || deep.Length != 6) return 11;
                if (parent.Position != 4 || parent.Remaining != 2 || shallow.Position != 1 || shallow.Remaining != 5 || deep.Position != 1 || deep.Remaining != 5) return 12;
                nuint maximum = (nuint)0 - (nuint)1;
                if (PeekImmutable(&deep, maximum, ref first) || first.Data != null || first.Length != 0 || first.Position != 0 || first.Remaining != 0) return 13;
                if (!PeekReadOnly(&shallow, 0, ref second) || second.Data != null || second.Length != 0 || second.Position != 0 || second.Remaining != 0) return 14;
                if (deep.Data != &data[1] || deep.Length != 6 || deep.Position != 1 || deep.Remaining != 5 || shallow.Data != &data[1] || shallow.Length != 6 || shallow.Position != 1 || shallow.Remaining != 5) return 15;
                BinaryBufferReader empty = new(); BinaryBufferReader output = new(data, 8); output.TrySkip(3);
                if (!empty.TryPeekSlice(0, ref output) || output.Data != null || output.Length != 0 || output.Position != 0 || output.Remaining != 0) return 16;
                output = new BinaryBufferReader(data, 8); output.TrySkip(3);
                if (empty.TryPeekSlice(maximum, ref output) || output.Data != null || output.Length != 0 || output.Position != 0 || output.Remaining != 0) return 17;
                if (empty.Data != null || empty.Length != 0 || empty.Position != 0 || empty.Remaining != 0) return 18;
                for (nuint i = 0; i < 8; i++) { byte expected = saved[i]; if (i == 3) expected = (byte)231; if (data[i] != expected) return 19; }
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        SourceFile[] sources = new[] { "Binary/BinaryPrimitives.gf", "IO/IByteReader.gf", "Binary/BinaryBufferReader.gf", "Memory/bytes.gf" }
            .Select(name => Path.Combine(AppContext.BaseDirectory, "Std", "core", name))
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("binary-peek-slice.gf", program)).ToArray();
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "invoke ", "personality", "__gflat_eh_", "gflat$Memory$Allocate", "gflat$Memory$Free", "@malloc", "@free", "__gflat_gc" })
            Assert.DoesNotContain(forbidden, ir);
        string definition = Assert.Single(ir.Split('\n'), line => line.StartsWith("define ") && line.Contains("$TryPeekSlice("));
        Assert.Contains("nounwind", definition);
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
