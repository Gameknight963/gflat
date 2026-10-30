using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class AbiScalarInteropTests
{
    [Theory]
    [InlineData(false, 1, false, "zeroext")]
    [InlineData(true, 1, false, "zeroext")]
    [InlineData(false, 8, false, "")]
    [InlineData(false, 8, true, "")]
    [InlineData(false, 16, false, "")]
    [InlineData(false, 16, true, "")]
    [InlineData(true, 8, false, "signext")]
    [InlineData(true, 8, true, "zeroext")]
    [InlineData(true, 16, false, "signext")]
    [InlineData(true, 16, true, "zeroext")]
    [InlineData(true, 32, false, "")]
    [InlineData(true, 64, true, "")]
    [InlineData(true, 128, false, "")]
    [InlineData(true, 0, false, "")]
    [InlineData(true, 7, false, "")]
    [InlineData(true, 17, true, "")]
    public void TargetScalarPolicyMatchesTheClangAbiContract(bool extend, int bits, bool unsigned, string expected)
    {
        var target = TargetInfo.Default with { ExtendNarrowIntegers = extend };
        Assert.Equal(expected, target.GetIntegerExtensionAttribute(bits, unsigned));
    }

    private static string Extension(string type) => type == "bool" ? "zeroext" :
        !OperatingSystem.IsLinux() ? "" : type is "sbyte" or "short" or "char" ? "signext" : "zeroext";

    [Theory]
    [InlineData("bool", "i1")]
    [InlineData("sbyte", "i8")]
    [InlineData("byte", "i8")]
    [InlineData("char", "i8")]
    [InlineData("short", "i16")]
    [InlineData("ushort", "i16")]
    public void DefinitionsExternsAndDirectIndirectCallsUseTheSameScalarAbi(string type, string llvm)
    {
        string ir = Compiler.Emit($$"""
            extern {{type}} Native({{type}} value);
            {{type}} Identity({{type}} value)=>value;
            {{type}} Forward({{type}} value)=>Native(value);
            {{type}} Invoke({{type}}({{type}})* callback,{{type}} value)=>callback(value);
            """, options: new() { NoStdLib = true });
        string extension = Extension(type);
        string result = extension.Length == 0 ? llvm : extension + " " + llvm;
        string parameter = extension.Length == 0 ? llvm : llvm + " " + extension;
        Assert.Contains($"declare {result} @Native({parameter})", ir);
        Assert.Contains($"define {result} @gflat$Identity({parameter} %arg.value)", ir);
        Assert.Contains($"call {result} @Native({parameter} ", ir);
        Assert.Matches(@"call " + Regex.Escape(result) + @" %[^\s(]+\(" + Regex.Escape(parameter) + " ", ir);
        // Pointer storage is erased; call sites retain scalar ABI attributes.
        Assert.Contains($"@gflat$Invoke(i8* %arg.callback, {parameter} %arg.value)", ir);
    }

    [Fact]
    public void ReferenceAndPointerParametersHaveNoPointeeExtensionAttribute()
    {
        string ir = Compiler.Emit("extern void Native(ref bool value,bool* pointer);void Forward(ref bool value,bool* pointer){Native(ref value,pointer);}", options: new() { NoStdLib = true });
        Assert.Contains("declare void @Native(i1*, i1*)", ir);
        Assert.Contains("define void @gflat$Forward(i1* %arg.value, i1* %arg.pointer)", ir);
    }

    [Fact]
    public void AliasesAndEnumUnderlyingTypesRetainSignedness()
    {
        string ir = Compiler.Emit("alias Small=sbyte;enum Code:ushort{Value=1}Small Signed(Small value)=>value;Code Unsigned(Code value)=>value;", options: new() { NoStdLib = true });
        string signedResult = Extension("sbyte") is { Length: > 0 } se ? se + " i8" : "i8";
        string unsignedResult = Extension("ushort") is { Length: > 0 } ue ? ue + " i16" : "i16";
        Assert.Contains($"define {signedResult} @gflat$Signed(", ir);
        Assert.Contains($"define {unsignedResult} @gflat$Unsigned(", ir);
    }

    [Fact]
    public void VariadicFixedParametersAreAnnotatedButPromotedExtrasAreNot()
    {
        string ir = Compiler.Emit("extern bool Native(bool value,...);bool Forward(bool value,byte extra)=>Native(value,extra);", options: new() { NoStdLib = true });
        Assert.Contains("declare zeroext i1 @Native(i1 zeroext, ...)", ir);
        Assert.Matches(@"call zeroext i1 \(i1, \.\.\.\) @Native\(i1 zeroext %[^,]+, i32 %", ir);
    }

    [Fact]
    public void GeneratedCallFamiliesAndRawLlvmWrappersUseTheirRespectiveBoundaries()
    {
        const string source = """
            interface I{bool Echo(bool value);}
            class Base:I{public virtual bool Echo(bool value)=>value;}
            class Derived:Base{public override bool Echo(bool value)=>value;}
            struct Value{
                public bool stored;
                public Value(bool value){stored=value;}
                public bool Echo(bool value)=>value;
                public static bool operator ==(Value left,Value right)=>true;
            }
            bool Raw(bool value)=>__llvm<bool>("ret i1 %0",value);
            int main(){
                Derived instance=new Derived();Base* parent=&instance;I* view=(I*)&instance;
                Value value=new Value(true);bool(bool)* callback=static(bool item)=>item;
                if(!parent.Echo(true)||!view.Echo(true)||!value.Echo(true)||!callback(true)||!Raw(true))return 1;
                if(!(value==value))return 2;
                return 0;
            }
            """;
        string ir = Compiler.Emit(source);
        Assert.Contains("define zeroext i1 @gflat$Value$op_Equality", ir);
        Assert.Contains("define zeroext i1 @gflat$Value$Echo(%Value* %this, i1 zeroext %arg.value)", ir);
        Assert.Matches(@"define void @[^\s(]+\(%Value\* %this, i1 zeroext %arg\.value\)", ir);
        Assert.Matches(@"define internal zeroext i1 @[^\s(]+\$interface_thunk\([^,]+, i1 zeroext %arg1\)", ir);
        Assert.Matches(@"define zeroext i1 @[^\s(]*lambda[^\s(]*\(i1 zeroext %arg\.item\)", ir);
        Assert.Contains("define zeroext i1 @gflat$Raw(i1 zeroext %arg.value)", ir);
        Assert.Matches(@"define private i1 @gflat\$llvm\$[^\s(]+\(i1 %0\)", ir);
        Assert.Matches(@"call i1 @gflat\$llvm\$[^\s(]+\(i1 ", ir);
        Assert.Equal(0, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void ManagedResultSlotsFollowSourceParametersWithoutAcquiringScalarAttributes()
    {
        string ir = Compiler.Emit("""
            alias GC=Collector;
            static class Collector{
                public static void* Allocate(nuint bytes,nuint alignment)=>((nuint)1).reinterpret<void*>();
                public static void ReferenceAdded(void* slot) nothrow {}
                public static void ReferenceRemoved(void* slot) nothrow {}
            }
            int^ Forward(int^ item,bool flag)=>item;
            int^ Invoke(int^(int^,bool)* callback,int^ item,bool flag)=>callback(item,flag);
            """);
        Assert.Contains("define void @gflat$Forward(i32* %arg.item, i1 zeroext %arg.flag, i32** %gc.result)", ir);
        Assert.Contains("@gflat$Invoke(i8* %arg.callback, i32* %arg.item, i1 zeroext %arg.flag, i32** %gc.result)", ir);
        Assert.Matches(@"(?:call|invoke) void %[^\s(]+\(i32\* %[^,]+, i1 zeroext %[^,]+, i32\*\* %", ir);
    }
}
