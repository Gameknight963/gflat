using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardAsciiInPlaceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryByteSubrangesAliasesAndZeroCountsRespectCountedStorage(int optimization)
    {
        var result=CompilerTestHelper.Run(Sources("""
            int main()nothrow{
                char[258] bytes;bytes[0]='!';bytes[257]='?';
                for(nuint i=0;i<256;i++)bytes[i+1]=(char)i;
                char* borrowed=&bytes[1];std::Text::Ascii::ToLower(borrowed,256);
                for(nuint i=0;i<256;i++){
                    nuint expected=i;if(i>=65&&i<=90)expected=i+32;
                    if((nuint)(byte)bytes[i+1]!=expected)return 1;
                    if(borrowed[i]!=std::Text::Ascii::ToLower((char)i))return 2;
                }
                if(bytes[0]!='!'||bytes[257]!='?')return 3;
                std::Text::Ascii::ToLower(&bytes[1],256);
                for(nuint i=0;i<256;i++)if(bytes[i+1]!=std::Text::Ascii::ToLower((char)i))return 4;
                for(nuint i=0;i<256;i++)bytes[i+1]=(char)i;
                std::Text::Ascii::ToUpper(&bytes[1],256);
                for(nuint i=0;i<256;i++){
                    nuint expected=i;if(i>=97&&i<=122)expected=i-32;
                    if((nuint)(byte)borrowed[i]!=expected)return 5;
                }
                std::Text::Ascii::ToUpper(&bytes[1],256);
                for(nuint i=0;i<256;i++)if(bytes[i+1]!=std::Text::Ascii::ToUpper((char)i))return 6;
                if(bytes[0]!='!'||bytes[257]!='?')return 7;
                char[8] part=['!','A','B','\0','c','d','Z','?'];
                std::Text::Ascii::ToLower(&part[1],4);
                if(part[0]!='!'||part[1]!='a'||part[2]!='b'||part[3]!='\0'||part[4]!='c'||part[5]!='d'||part[6]!='Z'||part[7]!='?')return 8;
                std::Text::Ascii::ToUpper(&part[3],3);
                if(part[1]!='a'||part[2]!='b'||part[3]!='\0'||part[4]!='C'||part[5]!='D'||part[6]!='Z')return 9;
                // A readonly pointer slot still permits changing mutable pointed bytes.
                readonly(char*) fixedPointer=&part[1];std::Text::Ascii::ToUpper(fixedPointer,1);
                if(part[1]!='A')return 10;
                std::Text::Ascii::ToLower((char*)1,0);std::Text::Ascii::ToUpper((char*)1,0);
                std::Text::Ascii::ToLower(&part[1],0);std::Text::Ascii::ToUpper(&part[7],0);
                if(part[1]!='A'||part[7]!='?')return 11;return 0;
            }
            """),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData("readonly(char)*")]
    [InlineData("immutable(char*)")]
    public void ProtectedCharactersCannotBePassedAsMutableStorage(string type)
    {
        foreach(string operation in new[]{"ToLower","ToUpper"}){
            string code=$"int main(){{char[1] bytes=['A'];{type} view=bytes;std::Text::Ascii::{operation}(view,1);return 0;}}";
            var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(code),options:new(){NoStdLib=true,NoBuiltins=true}));
            Assert.Equal("ascii-in-place.gf",error.Span.Source?.Path);
            Assert.Contains(operation,error.Message);
        }
    }

    [Fact]
    public void ConversionRemainsFreestandingNothrowAndImportFree()
    {
        string ir=Compiler.Emit(Sources("""
            int main()nothrow{char value='A';std::Text::Ascii::ToLower(&value,1);std::Text::Ascii::ToUpper(&value,1);return (int)value-65;}
            """),options:new(){NoStdLib=true,NoBuiltins=true,InternalizeFunctions=true});
        foreach(string name in new[]{"ToLower","ToUpper"}){
            string[] definitions=ir.Split('\n').Where(line=>line.StartsWith("define ")&&line.Contains("$Ascii$"+name)).ToArray();
            Assert.NotEmpty(definitions);Assert.All(definitions,line=>Assert.Contains("nounwind",line));
        }
        Assert.All(ir.Split('\n').Where(line=>line.StartsWith("declare ")),line=>Assert.Contains("@llvm.",line));
        foreach(string forbidden in new[]{"invoke ","__gflat_eh","gflat$Memory$","llvm.memcpy"})Assert.DoesNotContain(forbidden,ir);
    }

    private static SourceFile[] Sources(string source)=>[
        new("Ascii.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/Text/ascii.gf"))),
        new("ascii-in-place.gf",source)];
}
