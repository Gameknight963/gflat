using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace gflat.Tests;

public sealed class ComponentsExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolReportsGraphPartitionsAndRejectsInputBeforePublishingOutput(int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("components.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/components.gf"))));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_components_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"components.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);

            var graphs=new List<(int Nodes,(int A,int B)[] Edges,string? Input,string? Count)>{
                (0,[]," \t\r\n\n",null),(1,[],"",null),(6,[],null,null),
                (4,[(0,0),(0,1),(1,0),(1,1),(2,3),(2,3)],null,null),
                (8,[(0,1),(2,3),(4,5),(6,7),(0,2),(4,6),(0,4)],null,null),
                (2,[(0,1)],new string('0',125)+" 1\n",null),
                (3,[(0,1),(1,2)],"\t+0 +1 \r\n\n1\t2","+3")
            };
            var random=new Random(641583);
            for(int sample=0;sample<8;sample++){
                int nodes=random.Next(1,19);
                var edges=Enumerable.Range(0,random.Next(0,nodes*3+1)).Select(_=>(random.Next(nodes),random.Next(nodes))).ToArray();
                graphs.Add((nodes,edges,null,null));
            }
            foreach(var graph in graphs){
                string input=graph.Input??string.Join("\r\n",graph.Edges.Select(edge=>$"{edge.A} {edge.B}"))+"\r\n";
                var result=await Run(executable,[graph.Count??graph.Nodes.ToString(CultureInfo.InvariantCulture)],Encoding.ASCII.GetBytes(input));
                Assert.True(result.Code==0,$"{graph.Nodes} nodes: exit {result.Code}, stderr {result.Error}");
                Assert.Equal("",result.Error);
                AssertPartition(graph.Nodes,graph.Edges,Normalize(result.Output));
            }

            var invalid=new List<(string Name,byte[] Input,int Line,bool Oversized)>{
                ("missing second token","0\n"u8.ToArray(),1,false),
                ("third token","0 1 2\n"u8.ToArray(),1,false),
                ("malformed","0 x\n"u8.ToArray(),1,false),
                ("negative","-1 0\n"u8.ToArray(),1,false),
                ("endpoint overflow","0 18446744073709551616\n"u8.ToArray(),1,false),
                ("out of range","0 3\n"u8.ToArray(),1,false),
                ("late malformed","0 1\n\t\n1 suffix\n"u8.ToArray(),3,false),
                ("embedded NUL",[48,32,49,0,10],1,false),
                ("high byte",[48,32,255,10],1,false),
                ("128 payload bytes",Encoding.ASCII.GetBytes(new string('0',126)+" 1\n"),1,true),
                ("late oversized blank",Encoding.ASCII.GetBytes("0 1\n\n"+new string(' ',128)+"\n"),3,true)
            };
            foreach(var sample in invalid){
                var result=await Run(executable,["3"],sample.Input);
                Assert.True(result.Code==2,$"{sample.Name}: exit {result.Code}, stderr {result.Error}");
                Assert.Equal("",result.Output);
                string expected=sample.Oversized?Oversized(sample.Line):Invalid(sample.Line);
                if(sample.Name=="missing second token"||sample.Name=="third token")expected=Tokens(sample.Line);
                if(sample.Name=="out of range")expected=OutOfRange(sample.Line);
                Assert.Equal(expected,Normalize(result.Error));
            }
            var zeroEdge=await Run(executable,["0"],"0 0\n"u8.ToArray());
            Assert.Equal(2,zeroEdge.Code);Assert.Equal("",zeroEdge.Output);Assert.Equal(OutOfRange(1),Normalize(zeroEdge.Error));
            string[][] invalidArguments=[[],["2","3"],["-1"],["bad"],["18446744073709551616"]];
            foreach(var arguments in invalidArguments){
                var result=await Run(executable,arguments,[]);
                Assert.Equal(2,result.Code);Assert.Equal("",result.Output);Assert.Equal(Usage,Normalize(result.Error));
            }
        }finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    private static void AssertPartition(int count,(int A,int B)[] edges,string output)
    {
        var adjacency=Enumerable.Range(0,count).Select(_=>new List<int>()).ToArray();
        foreach(var (a,b) in edges){adjacency[a].Add(b);adjacency[b].Add(a);}
        int[] components=Enumerable.Repeat(-1,count).ToArray();int componentCount=0;
        for(int node=0;node<count;node++){
            if(components[node]>=0)continue;
            var pending=new Queue<int>();pending.Enqueue(node);components[node]=componentCount;
            while(pending.TryDequeue(out int current))foreach(int next in adjacency[current])if(components[next]<0){components[next]=componentCount;pending.Enqueue(next);}
            componentCount++;
        }
        string[] lines=output.Split('\n');Assert.Equal(count+2,lines.Length);Assert.Equal("",lines[^1]);
        Assert.Equal($"Components: {componentCount}",lines[0]);int[] representatives=new int[count];
        for(int node=0;node<count;node++){
            string[] pair=lines[node+1].Split(' ');Assert.Equal(2,pair.Length);
            Assert.True(int.TryParse(pair[0],NumberStyles.None,CultureInfo.InvariantCulture,out int index));Assert.Equal(node,index);
            Assert.True(int.TryParse(pair[1],NumberStyles.None,CultureInfo.InvariantCulture,out int representative));Assert.InRange(representative,0,count-1);
            representatives[node]=representative;Assert.Equal(components[node],components[representative]);
        }
        Assert.Equal(componentCount,representatives.Distinct().Count());
        for(int a=0;a<count;a++)for(int b=0;b<count;b++)Assert.Equal(components[a]==components[b],representatives[a]==representatives[b]);
    }

    private const string Usage="Usage: components <node-count>\n";
    private static string Tokens(int line)=>$"Line {line}: Expected two node indices\n";
    private static string Invalid(int line)=>$"Line {line}: Expected unsigned decimal node indices\n";
    private static string OutOfRange(int line)=>$"Line {line}: Node index is outside the declared universe\n";
    private static string Oversized(int line)=>$"Line {line}: Line exceeds the 127-byte payload limit\n";
    private static string Normalize(string text)=>text.Replace("\r\n","\n");

    private static async Task<(int Code,string Output,string Error)> Run(string executable,string[] arguments,byte[] input)
    {
        using var process=new Process{StartInfo=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(string argument in arguments)process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());Task<string> output=process.StandardOutput.ReadToEndAsync(),error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{
            await process.StandardInput.BaseStream.WriteAsync(input,timeout.Token);process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }catch(OperationCanceledException){process.Kill(true);throw new TimeoutException("components timed out.");}
        return(process.ExitCode,await output,await error);
    }
}
