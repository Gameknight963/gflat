using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ReflectionRuntimeBoundaryTests
{
    [Theory]
    [InlineData("reflection Info = reflect(int);")]
    [InlineData("reflection[1] Values = [reflect(int)];")]
    [InlineData("struct Item { public reflection Info; }")]
    [InlineData("class Item { public reflection[1] Values; }")]
    [InlineData("struct Item { public static reflection Info = reflect(int); }")]
    [InlineData("class Item { public static reflection[1] Values = [reflect(int)]; }")]
    public void MutableFieldsAndGlobalsCannotStoreReflectionValues(string declaration)
        => Reject(declaration + " int main() => 0;", "reflection storage must be const");

    [Theory]
    [InlineData("reflection", "Item left, Item right", "reflect(int)")]
    [InlineData("reflection[1]", "Item left, Item right", "[reflect(int)]")]
    [InlineData("Item", "Item left, reflection right", "left")]
    [InlineData("Item", "Item left, ref reflection right", "left")]
    public void OperatorsCannotHaveReflectionSignatures(string result, string parameters, string body)
        => Reject($"struct Item {{ public static {result} operator +({parameters}) => {body}; }} int main() => 0;",
            "An operator cannot use reflection");

    [Theory]
    [InlineData("reflection Read();")]
    [InlineData("void Write(reflection info);")]
    [InlineData("const reflection Read();")]
    [InlineData("const bool Examine(ref reflection info);")]
    public void InstanceInterfacesCannotDispatchReflectionValues(string method)
        => Reject("interface Reader { " + method + " } int main() => 0;", "reflection");

    [Theory]
    [InlineData("reflection(int)*?")]
    [InlineData("void(reflection)*?")]
    [InlineData("void(ref reflection)*?")]
    [InlineData("reflection[1]()*?")]
    public void RawFunctionPointersCannotCarryReflectionValues(string type)
        => Reject(type + " callback = null; int main() => 0;", "A function pointer cannot use reflection");

    [Theory]
    [InlineData("reflection Read();")]
    [InlineData("reflection[1] Read();")]
    [InlineData("void Write(reflection info);")]
    [InlineData("void Write(ref reflection info);")]
    public void ExternSignaturesCannotCarryReflectionValues(string signature)
        => Reject("extern " + signature + " int main() => 0;", "Extern signatures cannot contain reflection");

    [Theory]
    [InlineData("struct", "", "reflection")]
    [InlineData("class", "", "reflection[1]")]
    [InlineData("struct", "const ", "reflection")]
    [InlineData("class", "const ", "ref reflection")]
    public void ConstructorsCannotHaveReflectionParameters(string kind, string modifier, string type)
        => Reject($"{kind} Item {{ public {modifier}Item({type} info) {{ }} }} int main() => 0;",
            "Constructor parameters cannot contain reflection");

    [Theory]
    [InlineData("const reflection Info = reflect(int);", "&Info")]
    [InlineData("const reflection[1] Values = [reflect(int)];", "&Values")]
    [InlineData("struct Holder { public const reflection Info = reflect(int); }", "&Holder::Info")]
    [InlineData("struct Holder { public const reflection[1] Values = [reflect(int)]; }", "&Holder::Values")]
    public void ConstantReflectionDeclarationsHaveNoRuntimeAddress(string declaration, string expression)
        => Reject(declaration + " int main() { " + expression + "; return 0; }", "no runtime address");

    [Fact]
    public void LocalConstantReflectionArraysHaveNoRuntimeAddress()
        => Reject("int main() { const reflection[1] Values = [reflect(int)]; &Values; return 0; }", "no runtime address");

    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void ConstantFieldsAndMutableCompileTimeScratchRemainValid(string kind)
    {
        _ = Compiler.Emit($$"""
            {{kind}} Holder
            {
                public const reflection Info = reflect(int);
                public const reflection[1] Values = [reflect(long)];
            }
            const reflection Echo(reflection info) => info;
            const reflection[1] Build()
            {
                reflection[1] scratch;
                scratch[0] = Echo(Holder::Values[0]);
                return scratch;
            }
            int main()
            {
                const reflection[1] saved = Build();
                static if (Echo(Holder::Info) != reflect(int)) { invalidScalar(); }
                static if (saved[0] != reflect(long)) { invalidArray(); }
                return 0;
            }
            """, options: new() { NoStdLib = true, InternalizeFunctions = true });
    }

    [Fact]
    public void ConstantStructInstanceHelpersCanAcceptAndReturnReflectionValues()
    {
        _ = Compiler.Emit("""
            struct Inspector
            {
                public const Inspector() { }
                public const reflection Inspect(reflection info) => info;
            }
            const reflection Choose()
            {
                Inspector item = new();
                return item.Inspect(reflect(int));
            }
            int main()
            {
                const reflection chosen = Choose();
                static if (chosen != reflect(int)) { invalidChoice(); }
                return 0;
            }
            """, options: new() { NoStdLib = true, InternalizeFunctions = true });
    }

    [Fact]
    public void UnusedConstantClassInstanceHelpersDoNotRequireRuntimeEmission()
    {
        _ = Compiler.Emit("""
            class Inspector
            {
                public const reflection Inspect(reflection info) => info;
            }
            int main() => 0;
            """, options: new() { NoStdLib = true, InternalizeFunctions = true });
    }

    [Fact]
    public void StaticConstantInterfaceContractsRemainUsable()
    {
        _ = Compiler.Emit("""
            static interface IInspector
            {
                const reflection Inspect(reflection info);
            }
            static class Inspector : IInspector
            {
                public static const reflection Inspect(reflection info) => info;
            }
            int main()
            {
                const reflection chosen = Inspector::Inspect(reflect(int));
                static if (chosen != reflect(int)) { invalidChoice(); }
                return 0;
            }
            """, options: new() { NoStdLib = true, InternalizeFunctions = true });
    }

    private static void Reject(string source, string diagnostic)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.True(error.Description.Contains(diagnostic, StringComparison.OrdinalIgnoreCase), error.Description);
    }
}
