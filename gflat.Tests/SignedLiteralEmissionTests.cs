namespace gflat.Tests;

public sealed class SignedLiteralEmissionTests
{
    [Fact]
    public void MinimumSignedLiteralDoesNotParseItsPositiveMagnitudeAsInt64()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            int main() {
                long minimum = -9223372036854775808l;
                if (minimum != -9223372036854775808l) return 1;
                if (!(-9223372036854775808l < -1l)) return 2;
                if ((ulong)-9223372036854775808l != 9223372036854775808ul) return 3;
                uint unsigned = -1u;
                if (unsigned != 4294967295u) return 4;
                return 0;
            }
            """).ExitCode);
}
