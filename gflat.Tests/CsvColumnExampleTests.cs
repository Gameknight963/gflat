using System.Text;

namespace gflat.Tests;

public sealed class CsvColumnExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolExtractsOpaqueFieldsAndValidatesBeforeReplacingOutput(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-csv-column-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
                .Select(path=>new SourceFile(path,File.ReadAllText(path)))
                .Append(new SourceFile("csv-column.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/csv-column.gf"))));
            string ir=Path.Combine(directory,"main.ll"),exe=Path.Combine(directory,"csv-column.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,exe,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            string input=Path.Combine(directory,"input file.csv"),output=Path.Combine(directory,"output file.csv");
            var valid=new List<(byte[] Input,int Column,byte[][] Fields)> {
                ([],0,[]),([],123,[]),([13,10],0,[[]]),([10,10],0,[[],[]]),([34,34],0,[[]]),
                (Bytes("name,value\r\na,12\n"),1,[Bytes("value"),Bytes("12")]),
                (Bytes("a,\nlast,"),1,[[],[]]),
                (Bytes("\"a,b\",\"x\"\"y\"\r\n\"two\r\nlines\",\"zero\0field\"\n"),0,[Bytes("a,b"),Bytes("two\r\nlines")]),
                (Bytes("\"a,b\",\"x\"\"y\"\r\n\"two\r\nlines\",\"zero\0field\"\n"),1,[Bytes("x\"y"),Bytes("zero\0field")]),
                (Bytes("a,b,c\nx,y"),1,[Bytes("b"),Bytes("y")]),
                ([255,128,44,0,13,10],0,[[255,128]]),
                (Bytes("\ufeffname,01\r\nother,+2"),0,[Bytes("\ufeffname"),Bytes("other")])
            };
            var random=new Random(65047);
            byte[] alphabet=[0,1,9,10,13,32,34,44,65,97,128,195,255];
            for(int sample=0;sample<40;sample++)
            {
                int column=random.Next(0,4);
                byte[][][] rows=Enumerable.Range(0,random.Next(1,8)).Select(_=>
                    Enumerable.Range(0,random.Next(column+1,column+5)).Select(_=>
                        Enumerable.Range(0,random.Next(0,30)).Select(_=>alphabet[random.Next(alphabet.Length)]).ToArray()).ToArray()).ToArray();
                valid.Add((EncodeInput(rows,sample%2==0?[10]:[13,10],sample%3!=0),column,rows.Select(row=>row[column]).ToArray()));
            }
            byte[] large=Enumerable.Repeat((byte)'x',20000).Concat(new byte[]{34,0,10,255}).ToArray();
            valid.Add((EncodeInput([[Bytes("id"),large],[Bytes("next"),Bytes("small")]], [13,10],false),1,[large,Bytes("small")]));
            foreach(var sample in valid)
            {
                File.WriteAllBytes(input,sample.Input);
                var run=NativeToolchain.Run(exe,[input,sample.Column.ToString(),output]);
                Assert.True(run.ExitCode==0,run.StandardError);Assert.Empty(run.StandardOutput);Assert.Empty(run.StandardError);
                Assert.Equal(EncodeOutput(sample.Fields),File.ReadAllBytes(output));
                Assert.Equal(sample.Input,File.ReadAllBytes(input));
            }
            File.WriteAllBytes(input,Bytes("first,last\n"));
            var plus=NativeToolchain.Run(exe,[input,"+1",output]);Assert.Equal(0,plus.ExitCode);
            Assert.Equal(Bytes("last\r\n"),File.ReadAllBytes(output));
            // Reading and validation are complete before the destination opens, including same-path use.
            File.WriteAllBytes(input,Bytes("a,b\r\nx,y"));
            var inPlace=NativeToolchain.Run(exe,[input,"1",input]);Assert.Equal(0,inPlace.ExitCode);
            Assert.Equal(Bytes("b\r\ny\r\n"),File.ReadAllBytes(input));
            foreach(var (data,column,diagnostic) in new (byte[],string,string)[] {
                (Bytes("good,1\n\"unterminated"),"1","Invalid CSV at byte"),
                (Bytes("good,1\n\"closed\"suffix,2"),"1","Invalid CSV at byte"),
                (Bytes("good,1\nbad\"quote,2"),"1","Invalid CSV at byte"),
                (Bytes("good,1\nbare\r"),"1","Invalid CSV at byte"),
                (Bytes("good,1\nshort"),"1","Selected column is missing"),
                (Bytes("\n"),"1","Selected column is missing") })
            {
                byte[] sentinel=[9,0,255,10];File.WriteAllBytes(input,data);File.WriteAllBytes(output,sentinel);
                var run=NativeToolchain.Run(exe,[input,column,output]);Assert.Equal(2,run.ExitCode);
                Assert.Empty(run.StandardOutput);Assert.Contains(diagnostic,run.StandardError);
                Assert.Equal(sentinel,File.ReadAllBytes(output));Assert.Equal(data,File.ReadAllBytes(input));
            }
            foreach(string index in new[]{"","-1","-0"," 1","1 ","1x","0x1","18446744073709551616"})
            {
                File.Delete(output);
                var run=NativeToolchain.Run(exe,[input,index,output]);Assert.Equal(2,run.ExitCode);
                Assert.Empty(run.StandardOutput);Assert.Contains("Usage:",run.StandardError);Assert.False(File.Exists(output));
            }
            foreach(string[] arguments in new string[][]{[],[input],[input,"0"],[input,"0",output,"extra"]})
            {
                var run=NativeToolchain.Run(exe,arguments);Assert.Equal(2,run.ExitCode);
                Assert.Empty(run.StandardOutput);Assert.Equal("Usage: csv-column <input> <zero-based-column-index> <output>"+Environment.NewLine,run.StandardError);
            }
            byte[] preserved=[5,0,255];File.WriteAllBytes(output,preserved);
            var missing=NativeToolchain.Run(exe,[Path.Combine(directory,"missing.csv"),"0",output]);
            Assert.Equal(1,missing.ExitCode);Assert.Empty(missing.StandardOutput);Assert.NotEmpty(missing.StandardError);
            Assert.Equal(preserved,File.ReadAllBytes(output));
            File.WriteAllBytes(input,Bytes("one\n"));
            var badOutput=NativeToolchain.Run(exe,[input,"0",directory]);
            Assert.Equal(1,badOutput.ExitCode);Assert.Empty(badOutput.StandardOutput);Assert.Contains("Unable to write CSV column",badOutput.StandardError);
        }
        finally{Directory.Delete(directory,true);}
    }

    private static byte[] Bytes(string text)=>Encoding.UTF8.GetBytes(text);
    // Input generation always quotes every field, independent of canonical output quoting.
    // In particular a final sole empty field is "", even without a record ending.
    private static byte[] EncodeInput(byte[][][] rows,byte[] ending,bool finalEnding)
    {
        using var output=new MemoryStream();
        for(int row=0;row<rows.Length;row++)
        {
            for(int column=0;column<rows[row].Length;column++)
            {
                if(column!=0)output.WriteByte(44);
                output.WriteByte(34);
                foreach(byte value in rows[row][column]){output.WriteByte(value);if(value==34)output.WriteByte(34);}
                output.WriteByte(34);
            }
            if(finalEnding||row+1<rows.Length)output.Write(ending);
        }
        return output.ToArray();
    }

    private static byte[] EncodeOutput(byte[][] fields)
    {
        using var output=new MemoryStream();
        foreach(byte[] field in fields)
        {
            bool quoted=field.Any(value=>value is 34 or 44 or 13 or 10);
            if(quoted)output.WriteByte(34);
            foreach(byte value in field){output.WriteByte(value);if(quoted&&value==34)output.WriteByte(34);}
            if(quoted)output.WriteByte(34);
            output.WriteByte(13);output.WriteByte(10);
        }
        return output.ToArray();
    }
}
