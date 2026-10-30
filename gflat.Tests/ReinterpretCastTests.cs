using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ReinterpretCastTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NumericBitPatternsAddressesAndFunctionPointersUseExplicitIntrinsic(int optimization)
        => Run("""
            int Increment(int value) nothrow => value+1;
            int main() nothrow {
                uint[10] singles=[0u,2147483648u,1u,1065353216u,3212836864u,2139095040u,4286578688u,2143294004u,2139099700u,4290844280u];
                for(nuint index=0;index<10;index++){
                    uint bits=singles[index];float value=bits.reinterpret<float>();
                    if(value.reinterpret<uint>()!=bits)return 1;
                }
                ulong[8] doubles=[0ul,9223372036854775808ul,1ul,4607182418800017408ul,9218868437227405312ul,18442240474082181120ul,9221120237041095220ul,9218868437227414068ul];
                for(nuint index=0;index<8;index++){
                    ulong bits=doubles[index];double value=bits.reinterpret<double>();
                    if(value.reinterpret<ulong>()!=bits)return 2;
                }
                int number=42;int* pointer=&number;
                nuint address=pointer.reinterpret<nuint>();byte* bytes=pointer.reinterpret<byte*>();
                if(address.reinterpret<int*>()!=pointer||bytes.reinterpret<int*>()!=pointer)return 3;
                void* opaque=(void*)pointer;if((int*)opaque!=pointer)return 4;
                int(int)* callback=&Increment;void* entry=callback.reinterpret<void*>();
                int(int)* restored=entry.reinterpret<int(int)*>();if(restored(41)!=42)return 5;
                int negative=-1;if(negative.reinterpret<uint>()!=4294967295u)return 6;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstantsPreserveFiniteFloatBitsNegativeZeroAndNumericConversionDistinction(int optimization)
        => Run("""
            const uint OneBits=(1.0f).reinterpret<uint>();
            const uint ZeroBits=(-0.0f).reinterpret<uint>();
            const float One=1065353216u.reinterpret<float>();
            const ulong DoubleBits=(1.0).reinterpret<ulong>();
            const long Signed=18446744073709551615ul.reinterpret<long>();
            int main() nothrow {
                if(OneBits!=1065353216u||ZeroBits!=2147483648u||One!=1.0f||DoubleBits!=4607182418800017408ul||Signed!=-1)return 1;
                if((uint)1.0f!=1u)return 2;
                return 0;
            }
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

