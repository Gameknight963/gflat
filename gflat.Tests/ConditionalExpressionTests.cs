using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConditionalExpressionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OnlyTheSelectedBranchRunsAndAssociativityAndConstantEvaluationAgree(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            int calls = 0;
            int Next(int value) { calls++; return value; }
            const int Choose(bool flag) => flag ? 4 : 1 / 0;
            const int Folded = Choose(true);
            T Pick<T>(bool flag, T left, T right) => flag ? left : right;
            int main()
            {
                int value = false ? Next(1) : true ? Next(42) : Next(3);
                if (value != 42 || calls != 1 || Folded != 4 || Pick<int>(false, 3, 9) != 9) return 1;
                int assigned;
                value = value > 0 ? (assigned = 7) : (assigned = 8);
                if (assigned != 7) return 2;
                int local = 4;
                int*? maybe = &local;
                value = maybe != null ? *maybe : 99;
                return value == 4 ? 0 : 3;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SelectedOwnersCopyOrMoveOnceAndUnselectedTemporariesAreNotCreated(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            int constructed = 0;
            int copied = 0;
            int destroyed = 0;
            struct Owner
            {
                public int Value;
                public Owner(int value) { Value = value; constructed++; }
                public copy Owner(immutable(Owner)* other) { Value = other.Value; copied++; }
                ~Owner() { destroyed++; }
            }
            int main()
            {
                {
                    Owner first = new(4);
                    Owner second = new(9);
                    Owner chosen = true ? first : second;
                    if (chosen.Value != 4 || copied != 1) return 1;
                    Owner fresh = false ? new Owner(7) : new Owner(8);
                    Owner targetTyped = true ? new(10) : new(11);
                    if (fresh.Value != 8 || targetTyped.Value != 10 || constructed != 4) return 2;
                }
                return destroyed == 5 ? 0 : 3;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PointerNullAndNumericBranchesUseACommonTypeAndVoidBranchesWork(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            int changed = 0;
            void Change(int value) { changed = value; }
            int main()
            {
                int local = 42;
                int*? first = true ? &local : null;
                int*? second = false ? null : &local;
                long wide = false ? 1 : 4294967296l;
                true ? Change(7) : Change(8);
                if (first == null || second == null) return 1;
                return *first == 42 && *second == 42 && wide == 4294967296l && changed == 7 ? 0 : 2;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("int F() => 1 ? 2 : 3;", "bool")]
    [InlineData("int F(bool flag) { int x; int y = flag ? (x = 1) : 2; return x; }", "unassigned")]
    [InlineData("struct Owner { ~Owner() {} } Owner F(bool flag, ref Owner x) => flag ? x : x;", "branch")]
    public void InvalidConditionsAndUnassignedOrNoncopyableBranchesAreRejected(string source, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Contains(message, error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CopyingASelectedBranchInvalidatesEscapedNullableStorageFacts()
    {
        const string source = """
            int*?*? watched = null;
            void Remember(ref int*? value) { watched = &value; }
            struct Owner
            {
                public Owner() {}
                public copy Owner(immutable(Owner)* source) { *(watched!) = null; }
                ~Owner() {}
            }
            void Probe(bool flag)
            {
                int value = 42;
                int*? pointer = &value;
                Remember(ref pointer);
                if (pointer == null) return;
                Owner owner = new();
                Owner selected = flag ? owner : owner;
                *pointer = 9;
            }
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Contains("nullable", error.Description, StringComparison.OrdinalIgnoreCase);
    }
}
