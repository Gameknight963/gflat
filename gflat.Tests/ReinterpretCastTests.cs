using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ReinterpretCastTests
{
    [Fact]
    public void ATrustedNonNullPointerViewOnlyChangesItsPointerRepresentation()
    {
        string ir = Compiler.Emit("byte* View(int* pointer) => pointer.reinterpret<byte*>(); int main()=>0;",
            options: new() { NoStdLib = true });
        Assert.Contains("bitcast i32*", ir);
        Assert.DoesNotContain("icmp ne", ir);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AddressesIntegerWidthsArrayDecayAndFunctionPointersMatchNativeCastCategories(int optimization)
        => Run("""
            int Increment(int value) nothrow => value + 1;
            enum Mode { First = 7 }
            int main() nothrow
            {
                int number = 42;
                int* pointer = &number;
                nuint address = pointer.reinterpret<nuint>();
                extralong wide = pointer.reinterpret<extralong>();
                byte* bytes = pointer.reinterpret<byte*>();
                if (address.reinterpret<int*>() != pointer || wide.reinterpret<int*>() != pointer || bytes.reinterpret<int*>() != pointer) return 1;
                int[2] values = [3, 9];
                int* first = values.reinterpret<int*>();
                if (first[1] != 9) return 2;
                int(int)* callback = &Increment;
                void* entry = callback.reinterpret<void*>();
                int(int)* restored = entry.reinterpret<int(int)*>();
                if (restored(41) != 42) return 3;
                int*? zero = 0.reinterpret<int*?>();
                void*? small = ((byte)1).reinterpret<void*?>();
                if (zero != null || small.reinterpret<nuint>() != (nuint)1) return 4;
                if (number.reinterpret<int>() != 42 || Mode::First.reinterpret<Mode>() != Mode::First || true.reinterpret<bool>() != true) return 5;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReferenceReinterpretationViewsExistingStorageWithoutCopyingOrConstructing(int optimization)
        => Run("""
            int destroyed = 0;
            struct Owner { public int Value; public Owner(int value) { Value = value; } ~Owner() { destroyed++; } }
            void Change(ref byte value) { value = (byte)7; }
            int main()
            {
                int number = 0;
                Change(ref number.reinterpret<ref byte>());
                byte* bytes = (&number).reinterpret<byte*>();
                if (bytes[0] != (byte)7) return 1;
                number.reinterpret<ref byte>() = (byte)9;
                if (bytes[0] != (byte)9) return 2;
                readonly(int)* view = &number;
                if ((*view).reinterpret<ref readonly(byte)>() != (byte)9) return 3;
                {
                    Owner owner = new(42);
                    if (owner.reinterpret<ref Owner>().Value != 42 || destroyed != 0) return 4;
                }
                return destroyed == 1 ? 0 : 5;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IntegralIdentityIsConstantButNumericValueConversionUsesAnOrdinaryCast(int optimization)
        => Run("""
            const int Value = 42.reinterpret<int>();
            const uint Converted = (uint)-1;
            int main() nothrow { return Value == 42 && Converted == 4294967295u ? 0 : 1; }
            """, optimization);

    [Theory]
    [InlineData("int* p=(int*)1;")]
    [InlineData("int value=1;byte* p=(byte*)&value;")]
    [InlineData("int value=1;nuint address=(nuint)&value;")]
    [InlineData("int(int)* callback=&Read;void* address=(void*)callback;")]
    public void OrdinaryCastRejectsRepresentationOnlyConversions(string statement)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Check("int Read(int value)nothrow=>value;void Probe(){"+statement+"}"));
        Assert.Contains("cast",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("int value=1;double result=value.reinterpret<double>();")]
    [InlineData("bool value=true;byte result=value.reinterpret<byte>();")]
    [InlineData("int value=1;char result=value.reinterpret<char>();")]
    [InlineData("float value=1.0f;uint result=value.reinterpret<uint>();")]
    [InlineData("int value=1;uint result=value.reinterpret<uint>();")]
    [InlineData("int value=1;int* address=&value;int truncated=address.reinterpret<int>();")]
    [InlineData("int value=1;readonly(int)* view=&value;byte* changed=view.reinterpret<byte*>();")]
    [InlineData("int value=1;readonly(int)* view=&value;(*view).reinterpret<ref byte>()=(byte)2;")]
    [InlineData("(1+2).reinterpret<ref int>();")]
    [InlineData("int*? value=null;int* changed=value.reinterpret<int*>();")]
    [InlineData("Plain value=new();int result=value.reinterpret<int>();")]
    [InlineData("int value=1;int result=value.reinterpret<int>(2);")]
    [InlineData("int value=1;int result=value.reinterpret();")]
    [InlineData("int* result=((nuint)0).reinterpret<int*>();")]
    public void InvalidReinterpretShapesRejectBeforeEmission(string statement)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Check("struct Plain{public int Field;}void Probe(){"+statement+"}"));
        Assert.Contains("reinterpret",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IntrinsicCannotBeOverloadedAsMemberOrExtension()
    {
        foreach(string source in new[]{
            "struct Box{public int reinterpret<T>()=>1;}",
            "extension int {public int reinterpret<T>()=>*this;}"})
        {
            var error=Assert.Throws<TypeCheckException>(()=>Check(source));
            Assert.Contains("reserved",error.Message,StringComparison.OrdinalIgnoreCase);
        }
        Check("int reinterpret(int value)nothrow=>value;int main()nothrow=>reinterpret(0);");
    }

    private static void Check(string source)=>Compiler.Check(source,options:new(){NoStdLib=true,NoBuiltins=true});
    private static void Run(string source,int optimization)
    {
        string[] names=["core/Exception.gf","libc/Memory/allocation.gf","libc/Exceptions/unhandled.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Standard",name))))
            .Append(new SourceFile("reinterpret-tests.gf",source));
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true});
        _=CompilerTestHelper.RunIr(ir,optimization,expectedExitCode:0);
    }
}

