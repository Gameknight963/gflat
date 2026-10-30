namespace gflat.Tests.Std.Text;

public class ParsingAliasTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ResultStorageMayOverlapCountedAndNullTerminatedInput(int optimization)
    {
        (string Type,string Suffix)[] types=[("sbyte","SByte"),("byte","Byte"),("short","Short"),("ushort","UShort"),
            ("int","Int"),("uint","UInt"),("long","Long"),("ulong","ULong"),("nint","NInt"),("nuint","NUInt")];
        var source=new System.Text.StringBuilder("using Standard; using Standard::Text; int main(){");
        foreach(var (type,suffix) in types)
        {
            source.Append($$"""
                { {{type}}[2] storage=[({{type}})0,({{type}})0];
                  char* input=(&storage[0]).reinterpret<char*>(); input[0]='7'; input[1]='\0';
                  if(!TryParse{{suffix}}(input,1,&storage[0]) || storage[0]!=({{type}})7) return 1;
                  input[0]='8'; input[1]='\0';
                  if(!TryParse{{suffix}}(input,&storage[0]) || storage[0]!=({{type}})8) return 2;
                  input[0]='x'; input[1]='\0';
                  if(TryParse{{suffix}}(input,1,&storage[0]) || storage[0]!=({{type}})0) return 3;
                }
                """);
        }
        source.Append("""
            int[4] storage=[0,0,0,0]; char* input=(&storage[0]).reinterpret<char*>();
            for(nuint i=0;i<11;i++) input[i]="-2147483648"[i];
            if(!TryParseInt(input,11,&storage[0]) || storage[0]!=-2147483647-1) return 4;
            return 0;}
            """);
        var files=new[]{"Text/parsing.gf","Text/integer_parsing.gf"}
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Standard/core",name))))
            .Append(new SourceFile("main.gf",source.ToString()));
        Assert.Equal(0,CompilerTestHelper.Run(files,optimization).ExitCode);
    }
}
