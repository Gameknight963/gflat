using System;
using System.Linq;
using gflat.diagnostics;
using Xunit;

namespace gflat.Tests
{
    public class AllocatorTests
    {
        [Fact]
        public void DefaultAllocator_NewAndDelete_Succeeds()
        {
            string code = @"
struct Node
{
    int val;
}

int main()
{
    Node* n = new* Node();
    n.val = 42;
    int res = n.val;
    delete n;
    return res;
}
";
            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void CustomAllocator_HeaderTagging_InterceptsNewAndDelete()
        {
            string code = @"
extern void* malloc(nuint size);
extern void free(void* ptr);

namespace Allocator { public replace void*? Allocate(nuint size)
{
    byte* raw = (byte*)malloc(size + 8UL);
    ulong* header = (ulong*)raw;
    *header = 12345UL;
    return (void*)(raw + 8UL);
} }

namespace Allocator { public replace void Free(void* ptr)
{
    byte* raw = (byte*)ptr - 8UL;
    ulong* header = (ulong*)raw;
    if (*header == 12345UL)
    {
        *header = 0UL;
    }
    free((void*)raw);
} }

struct Point
{
    int x;
    int y;
}

int main()
{
    Point* p = new* Point();
    p.x = 10;
    p.y = 20;
    int sum = p.x + p.y;

    byte* raw = (byte*)p - 8UL;
    ulong* header = (ulong*)raw;
    if (*header != 12345UL)
    {
        return 0;
    }

    delete p;
    return sum; // 30
}
";
            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(30, result.ExitCode);
        }

        [Fact]
        public void CustomAllocator_ExpressionBodyHooks_Succeeds()
        {
            string code = @"
extern void* malloc(nuint size);
extern void free(void* ptr);

namespace Allocator { public replace void*? Allocate(nuint size) => malloc(size); }
namespace Allocator { public replace void Free(void* ptr) => free(ptr); }

struct Data
{
    int value;
}

int main()
{
    Data* d = new* Data();
    d.value = 99;
    int res = d.value;
    delete d;
    return res;
}
";
            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(99, result.ExitCode);
        }

        [Fact]
        public void Directive_NoDefaultAllocator_MissingCustom_ReportsDiagnostic()
        {
            string code = @"
#no_default_allocator

int main()
{
    return 0;
}
";
            (gflat.ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.CheckDiagnostics(code);
            Assert.True(checker.Diagnostics.HasErrors);
            Assert.Contains(checker.Diagnostics.Items, d => d.Message.Contains("obsolete"));
        }

        [Fact]
        public void CustomAllocator_BlockBodies_Succeeds()
        {
            string code = @"


extern void* malloc(nuint size);
extern void free(void* ptr);

namespace Allocator { public replace void*? Allocate(nuint size)
{
    return malloc(size);
} }

namespace Allocator { public replace void Free(void* ptr)
{
    free(ptr);
} }

struct Node
{
    int val;
}

int main()
{
    Node* n = new* Node();
    n.val = 33;
    int res = n.val;
    delete n;
    return res;
}
";
            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(33, result.ExitCode);
        }

        [Fact]
        public void CustomAllocator_AlignedWrapper_Succeeds()
        {
            string code = @"
extern void* malloc(nuint size);
extern void free(void* ptr);

namespace Allocator { public replace void*? Allocate(nuint size)
{
    nuint aligned = ((size + 15u) / 16u) * 16u;
    return malloc(aligned);
} }

namespace Allocator { public replace void Free(void* ptr)
{
    free(ptr);
} }

struct Vec
{
    int x;
    int y;
    int z;
}

int main()
{
    Vec* v = new* Vec();
    v.x = 10;
    v.y = 20;
    v.z = 30;
    int total = v.x + v.y + v.z;
    delete v;
    return total;
}
";
            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(60, result.ExitCode);
        }

        [Fact]
        public void AllocatorSignature_InvalidReturnType_ReportsDiagnostic()
        {
            string code = @"
namespace Allocator { public replace int Allocate(nuint size)
{
    return 0;
} }

namespace Allocator { public replace void Free(void* ptr)
{
} }

int main()
{
    return 0;
}
";
            (gflat.ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.CheckDiagnostics(code);
            Assert.True(checker.Diagnostics.HasErrors);
            Assert.Contains(checker.Diagnostics.Items, d => d.Message.Contains("weak"));
        }

        [Fact]
        public void AllocatorSignature_InvalidFreeParameter_ReportsDiagnostic()
        {
            string code = @"
namespace Allocator { public replace void*? Allocate(nuint size)
{
    return (void*)1;
} }

namespace Allocator { public replace void Free(int notAPointer)
{
} }

int main()
{
    return 0;
}
";
            (gflat.ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.CheckDiagnostics(code);
            Assert.True(checker.Diagnostics.HasErrors);
            Assert.Contains(checker.Diagnostics.Items, d => d.Message.Contains("weak"));
        }
    }
}
