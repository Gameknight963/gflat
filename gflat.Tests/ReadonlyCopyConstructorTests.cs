using gflat.CompileExceptions;

namespace gflat.Tests;

public class ReadonlyCopyConstructorTests
{
    [Fact]
    public void ReadonlyOwnersCopyThroughConstructorsForArgumentsAssignmentsAndFields()
    {
        var run=CompilerTestHelper.Run("""
            struct Resource {
                public int* data;
                public Resource(int n) { data=new* int(n); }
                public copy Resource(immutable(Resource)* source) { data=new* int(*source.data); }
                ~Resource() { delete data; }
            }
            struct Holder {
                public Resource resource;
                public Holder(Resource value) { resource=move value; }
            }
            int Consume(Resource copy) { *copy.data=20; return *copy.data; }
            int main() {
                Resource original=new(42); immutable(Resource)* view=&original;
                Resource copy=*view; *copy.data=1;
                if(*original.data!=42 || Consume(*view)!=20 || *original.data!=42) return 1;
                copy=*view; if(*copy.data!=42 || copy.data==original.data) return 2;
                Holder holder=new(new Resource(7)); immutable(Holder)* borrowed=&holder;
                Holder cloned=*borrowed; *cloned.resource.data=9;
                if(*holder.resource.data!=7) return 3;
                return 0;
            }
            """);
        Assert.Equal(0,run.ExitCode);
    }

    [Theory]
    [InlineData("struct Resource { public int* data; }")]
    [InlineData("struct Resource { public int* data; private copy Resource(immutable(Resource)* source) {data=new* int(*source.data);} }")]
    public void ReadonlyCopyStillRejectsMutableAliasExposureAndInaccessibleConstructors(string type)
    {
        Assert.Throws<TypeCheckException>(()=>Compiler.Emit(type+"""
            int main() { int n=1; Resource source=new(); source.data=&n; immutable(Resource)* view=&source; Resource copy=*view; return 0; }
            """));
    }
}
