namespace gflat.Tests;

public sealed class StandardArenaZeroedTests
{
    private const string Allocator="""
        using std;
        int allocations=0;int releases=0;bool fail=false;
        private extern void*? malloc(nuint bytes);private extern void free(void* storage);
        namespace Memory{
            public replace void*? Allocate(nuint bytes){
                if(fail)return null;
                void*? result=malloc(bytes);if(result!=null){allocations++;byte* data=(byte*)result;for(nuint i=0;i<bytes;i++)data[i]=(byte)165;}
                return result;
            }
            public replace void Free(void* storage){releases++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AlignmentAndBlockGrowthClearReservationsWithoutTouchingEarlierRegions(int optimization)
        => Run(Allocator+"""
            int main(){ {
                Arena arena=new(64);byte* first=(byte*)arena.Allocate(8,1);
                for(nuint i=0;i<8;i++)first[i]=(byte)(i+1);
                int[7] sizes=[0,1,3,17,31,33,129];
                for(nuint alignment=1;alignment<=4096;alignment*=2)
                for(nuint index=0;index<7;index++){
                    nuint requested=(nuint)sizes[index];byte* zero=(byte*)arena.AllocateZeroed(requested,alignment);
                    if((nuint)zero%alignment!=0)return 1;
                    nuint checked=requested;if(checked==0)checked=1;
                    for(nuint i=0;i<checked;i++)if(zero[i]!=(byte)0)return 2;
                    for(nuint i=0;i<8;i++)if(first[i]!=(byte)(i+1))return 3;
                    byte* uninitialized=(byte*)arena.Allocate(3,1);
                    for(nuint i=0;i<3;i++)if(uninitialized[i]!=(byte)165)return 4;
                    if(zero[0]!=(byte)0)return 5;
                }
                byte* empty=(byte*)arena.AllocateZeroed(0);byte* another=(byte*)arena.AllocateZeroed(0);
                if(empty==another || *empty!=(byte)0 || *another!=(byte)0)return 6;
                byte* header=(byte*)arena.AllocateZeroed(24,1);header[0]=(byte)1;
                for(nuint i=1;i<24;i++)if(header[i]!=(byte)0)return 7;
            }
            if(allocations!=releases)return 8;return 0;}
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailingGrowthAndInvalidRequestsKeepPriorDataAndCurrentBlockFreeSpace(int optimization)
        => Run(Allocator+"""
            int main(){ {
                Arena arena=new(64);byte* first=(byte*)arena.Allocate(4,1);
                for(nuint i=0;i<4;i++)first[i]=(byte)42;
                int live=allocations-releases;fail=true;bool caught=false;
                try{arena.AllocateZeroed(1000,1);}catch{caught=true;}
                if(!caught || allocations-releases!=live)return 1;
                byte* next=(byte*)arena.AllocateZeroed(4,1);
                if(next!=first+4 || allocations-releases!=live)return 2;
                for(nuint i=0;i<4;i++)if(first[i]!=(byte)42 || next[i]!=(byte)0)return 3;
                fail=false;int errors=0;
                try{arena.AllocateZeroed(1,0);}catch{errors++;}
                try{arena.AllocateZeroed(1,3);}catch{errors++;}
                try{arena.AllocateZeroed((nuint)0-(nuint)1,1);}catch{errors++;}
                try{arena.AllocateZeroed((nuint)0-(nuint)1,256);}catch{errors++;}
                if(errors!=4 || allocations-releases!=live)return 4;
                byte* following=(byte*)arena.AllocateZeroed(4,1);
                if(following!=next+4)return 5;
                for(nuint i=0;i<4;i++)if(first[i]!=(byte)42 || next[i]!=(byte)0 || following[i]!=(byte)0)return 6;
            }
            if(allocations!=releases)return 7;return 0;}
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndependentArenasAndMovesOwnStorageWithoutDestroyingPlacementObjects(int optimization)
        => Run(Allocator+"""
            int destroyed=0;
            struct Resource{public Resource(){ }~Resource(){destroyed++;}}
            int main(){ {
                Arena first=new(32);Arena second=new(16);
                byte* kept=(byte*)first.AllocateZeroed(8,1);kept[0]=(byte)91;
                byte* temporary=(byte*)second.AllocateZeroed(8,1);temporary[0]=(byte)77;
                Resource* resource=new* Resource() at (Resource*)second.AllocateZeroed((nuint)sizeof(Resource),(nuint)alignof(Resource));
                second.Reset();if(destroyed!=0 || kept[0]!=(byte)91)return 1;
                Arena moved=move first;if(kept[0]!=(byte)91)return 2;
                moved.Reset();moved.Reset();
                byte* fresh=(byte*)moved.AllocateZeroed(8,1);
                for(nuint i=0;i<8;i++)if(fresh[i]!=(byte)0)return 3;
                Resource* explicitLifetime=new* Resource() at (Resource*)moved.AllocateZeroed((nuint)sizeof(Resource),(nuint)alignof(Resource));
                explicitLifetime.~Resource();
            }
            if(destroyed!=1 || allocations!=releases)return 4;return 0;}
            """,optimization);

    private static void Run(string program,int optimization)
    {
        var sources=new[]{"Memory/Arena.gf"}.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("arena-zeroed.gf",program));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_arena_zeroed_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"arena.ll"),executable=Path.Combine(directory,"arena.exe");File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compilation=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compilation.ExitCode==0,compilation.StandardError);
            var result=NativeToolchain.Run(executable,[]);Assert.True(result.ExitCode==0,$"Arena zeroed exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }finally{Directory.Delete(directory,true);}
    }
}
