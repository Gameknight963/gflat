using System.Text;

namespace gflat.Tests;

public sealed class StandardUnicodeScalarQueryTests
{
    private const string Allocator="""
        using std; using std::Collections;int allocations=0;
        namespace Memory{
            public replace void*? Allocate(nuint count){allocations++;return null;}
            public replace void Free(void* pointer) nothrow{}
        }
        bool NonAscii(uint value)=>value>127u;
        uint Next(uint value)=>value+1u;
        ulong Sum(ulong sum,uint value)=>sum+(ulong)value;
        nuint ByteLength(immutable(Utf8Scalars)* text)nothrow=>text.ByteLength;
        nuint UnitLength(immutable(Utf16Scalars)* text)nothrow=>text.CodeUnitLength;
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ProtectedScalarQueriesMatchDotNetRunesWithoutAllocating(int optimization)
    {
        var text=new StringBuilder("A\0\u007f\u0080\u07ff\u0800\ud7ff\ue000\uffff\ufeff\U00010000\U0001f600\U0010ffffZ");
        var random=new Random(8837);
        for(int i=0;i<32;i++){int scalar;do{scalar=random.Next(0x110000);}while(scalar is >=0xd800 and <=0xdfff);text.Append(char.ConvertFromUtf32(scalar));}
        string original=text.ToString();byte[] utf8=Encoding.UTF8.GetBytes(original);
        uint[] scalars=original.EnumerateRunes().Select(r=>(uint)r.Value).ToArray();
        ulong expected=scalars.Where(value=>value>127).Select(value=>(ulong)value+1).Aggregate(0ul,(sum,value)=>sum+value);
        var code=new StringBuilder(Allocator+"int main(){\n");
        code.AppendLine($"char[{utf8.Length}] bytes=[{string.Join(",",utf8.Select(value=>"(char)"+value))}];");
        code.AppendLine($"ushort[{original.Length}] units=[{string.Join(",",original.Select(value=>"(ushort)"+(int)value))}];");
        foreach(var item in new[]{("Utf8Scalars","bytes",utf8.Length,"ByteLength"),("Utf16Scalars","units",original.Length,"UnitLength")})
            code.AppendLine($$"""
                { {{item.Item1}} text=new({{item.Item2}},{{item.Item3}});
                  readonly({{item.Item1}})* shallow=&text;immutable({{item.Item1}})* deep=&text;
                  if({{item.Item4}}(deep)!={{item.Item3}})return 1;
                  var query=deep.AsEnumerable().Where(NonAscii).Select(Next).Take(1000);
                  var other=shallow.AsEnumerable().Where(NonAscii).Select(Next).Take(1000);
                  if(query.Aggregate(0ul,Sum)!={{expected}}ul||other.Aggregate(0ul,Sum)!={{expected}}ul)return 2;
                  if(query.Aggregate(0ul,Sum)!={{expected}}ul||allocations!=0)return 3;
                }
                """);
        code.AppendLine("return 0;}");Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EarlyTakeAvoidsMalformedSuffixAndBorrowedMutationsRemainVisible(int optimization)
        => Run(Allocator+"""
            int main(){
                char[2] bytes=['a',(char)255];ushort[2] units=[(ushort)97,(ushort)0xd800];
                Utf8Scalars utf8=new(bytes,2);Utf16Scalars utf16=new(units,2);
                immutable(Utf8Scalars)* first=&utf8;readonly(Utf16Scalars)* second=&utf16;
                var left=first.AsEnumerable().Take(1);var right=second.AsEnumerable().Take(1);
                if(left.Aggregate(0ul,Sum)!=97ul||right.Aggregate(0ul,Sum)!=97ul)return 1;
                bytes[0]='b';units[0]=(ushort)98;
                if(left.Aggregate(0ul,Sum)!=98ul||right.Aggregate(0ul,Sum)!=98ul)return 2;
                if(first.AsEnumerable().Take(0).Aggregate(0ul,Sum)!=0ul||second.AsEnumerable().Take(0).Aggregate(0ul,Sum)!=0ul)return 3;
                if(allocations!=0)return 4;
                int caught=0;
                try{first.AsEnumerable().Take(2).Aggregate(0ul,Sum);}catch(ref Exception error){caught++;}
                try{second.AsEnumerable().Take(2).Aggregate(0ul,Sum);}catch(ref Exception error){caught++;}
                if(caught!=2)return 5;return 0;
            }
            """,optimization);

    private static void Run(string program,int optimization)
    {
        string[] names=["Utf8.gf","Utf16.gf","Utf8Scalars.gf","Utf16Scalars.gf","Span.gf","Collections/Vector.gf",
            "Collections/IEnumerable.gf","Collections/IEnumerator.gf","Collections/Enumerable.gf","Collections/EnumerableExtensions.gf",
            "Collections/WhereSource.gf","Collections/SelectSource.gf","Collections/SliceSource.gf","Collections/TakeWhileSource.gf","Collections/SkipWhileSource.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("scalar-queries.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
