using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class AttributeReflectionTests
{
    [Fact]
    public void AttributesRemainAttachedToAliasesEnumsAndSpecializedMembers()
    {
        Compiler.Check("""
            class Tag : Attribute { public int Value; public Tag(int value) { Value = value; } }
            [Tag(1)] struct Box<T>
            {
                [Tag(2)] public T Item;
                [Tag(3)] public T Read() => Item;
            }
            [Tag(4)] alias Selected = Box<int>;
            [Tag(5)] enum State { Ready }
            int main()
            {
                static if ([:reflect(Box<int>).Attributes[0].Value:].Value != 1) wrongType();
                static if ([:reflect(Box<int>).Fields[0].Attributes[0].Value:].Value != 2) wrongField();
                static if ([:reflect(Box<int>).Methods[0].Attributes[0].Value:].Value != 3) wrongMethod();
                static if ([:reflect(Selected).Attributes[0].Value:].Value != 4) wrongAlias();
                static if ([:reflect(State).Attributes[0].Value:].Value != 5) wrongEnum();
                return 0;
            }
            """);
    }

    [Fact]
    public void AttributeTypesAndConstructedValuesAreAvailableDuringConstantEvaluation()
    {
        const string source = """
            class Tag : Attribute { public int Value; public Tag(int value) { Value = value; } }
            [Tag(42)] struct Marked {}
            const reflection attribute = reflect(Marked).Attributes[0];
            const int answer = [:attribute.Value:].Value;
            int main()
            {
                static if (attribute.Type != reflect(Tag)) invalidAttributeType();
                return answer;
            }
            """;
        string ir = Compiler.Emit(source);
        Assert.Equal(42, CompilerTestHelper.RunIr(ir).ExitCode);
    }

    [Fact]
    public void BackingFieldsCarryTheOrdinaryCompilerGeneratedAttribute()
    {
        Compiler.Check("""
            struct Owner { public int Value { get; set; } public int Computed => 42; }
            const reflection owner = reflect(Owner);
            int main()
            {
                static if (owner.Fields.Length != 1) wrongFieldCount();
                static if (owner.Properties.Length != 2) wrongPropertyCount();
                static if (owner.Fields[0].Attributes.Length != 1) missingMarker();
                static if (owner.Fields[0].Attributes[0].Type != reflect(CompilerGeneratedAttribute)) wrongMarker();
                return 0;
            }
            """);
    }

    [Fact]
    public void PropertySplicesUseOrdinaryAccessors()
    {
        string ir = Compiler.Emit("""
            struct Owner { public int Value { get; set; } }
            int main()
            {
                Owner owner = new();
                const reflection property = reflect(Owner).Properties[0];
                owner.[:property:] = 42;
                return owner.[:property:];
            }
            """);
        Assert.Equal(42, CompilerTestHelper.RunIr(ir).ExitCode);
    }

    [Fact]
    public void AttributeValueAccessPreservesMemberPrivacy()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            class Tag : Attribute { private int secret = 42; }
            [Tag] struct Owner {}
            const int secret = [:reflect(Owner).Attributes[0].Value:].secret;
            """));
}
