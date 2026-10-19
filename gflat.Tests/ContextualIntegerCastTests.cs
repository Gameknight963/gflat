namespace gflat.Tests;

public class ContextualIntegerCastTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitIntegerLiteralCastsKeepTheFullTargetWidth(int optimization)
    {
        var result=CompilerTestHelper.Run("""
            int main() {
                extralong min=(extralong)(-170141183460469231731687303715884105728);
                extralong max=(extralong)170141183460469231731687303715884105727;
                extralong expectedMin=-170141183460469231731687303715884105728;
                extralong expectedMax=170141183460469231731687303715884105727;
                if(min!=expectedMin || max!=expectedMax) return 1;
                if((long)(min>>64)!=-9223372036854775807l-1l || (ulong)max!=18446744073709551615ul) return 2;
                long wide=(long)4294967296; nuint size=(nuint)4294967296;
                if(wide!=4294967296l || size!=(nuint)wide) return 3;
                if((uint)4294967295!=4294967295u || (ulong)18446744073709551615!=18446744073709551615ul) return 4;
                // Explicit narrowing and negative-to-unsigned casts still wrap.
                if((byte)300!=(byte)44 || (ulong)-1!=18446744073709551615ul) return 5;
                return 0;
            }
            """,optimization);
        Assert.Equal(0,result.ExitCode);
    }
}
