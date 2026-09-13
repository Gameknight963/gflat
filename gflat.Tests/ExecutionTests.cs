using Xunit;

namespace gflat.Tests
{
    public class ExecutionTests
    {
        [Fact]
        public void SimpleReturnExitCode()
        {
            string code = """
                int main()
                {
                    int a = 20;
                    int b = 22;
                    return a + b;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void StructFieldsAndPointers()
        {
            string code = """
                struct Vector
                {
                    int x;
                    int y;
                }

                int main()
                {
                    Vector v;
                    v.x = 10;
                    v.y = 20;

                    Vector* ptr = &v;
                    ptr.x = 35;

                    return v.x + v.y;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(55, result.ExitCode);
        }

        [Fact]
        public void LoopsAndConditionals()
        {
            string code = """
                int main()
                {
                    int sum = 0;
                    for (int i = 0; i < 10; i++)
                    {
                        if (i == 5)
                        {
                            continue;
                        }
                        sum += i;
                    }
                    return sum;
                }
                """;

            // 0+1+2+3+4+6+7+8+9 = 40
            var result = CompilerTestHelper.Run(code);
            Assert.Equal(40, result.ExitCode);
        }

        [Fact]
        public void PrintfStandardOutput()
        {
            string code = """
                extern int printf(char* fmt, ...);

                void Greet(char* name)
                {
                    printf("Hello, %s!\n", name);
                }

                int main()
                {
                    Greet("gflat");
                    return 0;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Hello, gflat!", result.StandardOutput);
        }

        [Fact]
        public void StructMethodOnValueInstance()
        {
            string code = """
                struct Counter
                {
                    int count;

                    void Increment(int amount)
                    {
                        this.count += amount;
                    }

                    int Get()
                    {
                        return this.count;
                    }
                }

                int main()
                {
                    Counter c;
                    c.count = 5;
                    c.Increment(10);
                    return c.Get();
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(15, result.ExitCode);
        }

        [Fact]
        public void StructMethodOnPointerWithAutoDeref()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;

                    void Move(int dx, int dy)
                    {
                        this.x += dx;
                        this.y += dy;
                    }
                }

                int main()
                {
                    Point p;
                    p.x = 10;
                    p.y = 20;

                    Point* ptr = &p;
                    ptr.Move(5, 10);

                    return p.x + p.y;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(45, result.ExitCode);
        }

        [Fact]
        public void StructMethodWithImplicitThisFields()
        {
            string code = """
                struct Vector
                {
                    int x;
                    int y;

                    void Add(int dx, int dy)
                    {
                        x += dx;
                        y += dy;
                    }

                    int Sum()
                    {
                        return x + y;
                    }
                }

                int main()
                {
                    Vector v;
                    v.x = 100;
                    v.y = 200;
                    v.Add(50, 50);
                    return v.Sum();
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(400, result.ExitCode);
        }

        [Fact]
        public void StructMethodInsideNamespace()
        {
            string code = """
                namespace Geometry
                {
                    struct Rect
                    {
                        int width;
                        int height;

                        int Area()
                        {
                            return this.width * this.height;
                        }
                    }
                }

                int main()
                {
                    Geometry::Rect r;
                    r.width = 6;
                    r.height = 7;
                    return r.Area();
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void PointerIsNullTest()
        {
            string code = """
                struct Node
                {
                    int value;
                }

                int main()
                {
                    Node*? empty = null;
                    if (!empty->is_null)
                    {
                        return 1;
                    }

                    Node n;
                    n.value = 42;
                    Node* ptr = &n;
                    if (ptr->is_null)
                    {
                        return 2;
                    }

                    return n.value;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void PointerAddressTest()
        {
            string code = """
                int main()
                {
                    int val = 123;
                    int* ptr = &val;
                    long addr = ptr->address;
                    if (addr == 0L)
                    {
                        return 1;
                    }

                    int*? nullPtr = null;
                    long nullAddr = nullPtr->address;
                    if (nullAddr != 0L)
                    {
                        return 2;
                    }

                    return 0;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(0, result.ExitCode);
        }

        [Fact]
        public void PointerFreeTest()
        {
            string code = """
                extern void* malloc(long size);

                struct Item
                {
                    int id;
                    int count;
                }

                int main()
                {
                    Item* item = malloc(16L);
                    if (item->is_null)
                    {
                        return 1;
                    }

                    item.id = 100;
                    item.count = 25;
                    int total = item.id + item.count;

                    item->free();
                    return total;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(125, result.ExitCode);
        }

        [Fact]
        public void FixedSizeArrayBufferAndIndex()
        {
            string code = """
                int main()
                {
                    int[4] nums;
                    nums[0] = 10;
                    nums[1] = 20;
                    nums[2] = 30;
                    nums[3] = 40;
                    return nums[0] + nums[1] + nums[2] + nums[3];
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(100, result.ExitCode);
        }

        [Fact]
        public void StringLiteralToStackArrayAndModification()
        {
            string code = """
                int main()
                {
                    char[] a = "hello";
                    a[0] = 'H';
                    // 'H' is ASCII 72
                    return a[0];
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(72, result.ExitCode);
        }

        [Fact]
        public void ArrayDecayToFunctionPointer()
        {
            string code = """
                int StrLen(char* s)
                {
                    int len = 0;
                    while (s[len] != '\0')
                    {
                        len++;
                    }
                    return len;
                }

                int main()
                {
                    char[7] a = "string";
                    return StrLen(a);
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(6, result.ExitCode);
        }

        [Fact]
        public void PointerIndexing()
        {
            string code = """
                int main()
                {
                    int[3] arr;
                    arr[0] = 5;
                    arr[1] = 15;
                    arr[2] = 25;

                    int* ptr = arr;
                    return ptr[0] + ptr[1] + ptr[2];
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(45, result.ExitCode);
        }

        [Fact]
        public void DeferBlockExitLifoOrder()
        {
            string code = """
                int main()
                {
                    int result = 0;
                    {
                        defer result = result * 10 + 1;
                        defer result = result * 10 + 2;
                        defer result = result * 10 + 3;
                    }
                    return result;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(321, result.ExitCode);
        }

        [Fact]
        public void DeferEarlyReturnExecutesDefersInLifo()
        {
            string code = """
                int Foo(int* res)
                {
                    defer *res = *res * 10 + 1;
                    if (*res > 0)
                    {
                        defer *res = *res * 10 + 2;
                        return 42;
                    }
                    defer *res = *res * 10 + 3;
                    return 99;
                }

                int main()
                {
                    int r = 5;
                    int retVal = Foo(&r);
                    if (retVal != 42)
                    {
                        return 1;
                    }
                    if (r != 521)
                    {
                        return 2;
                    }
                    return 0;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(0, result.ExitCode);
        }

        [Fact]
        public void DeferReturnValueEvaluatedBeforeDeferRuns()
        {
            string code = """
                int Foo()
                {
                    int x = 10;
                    defer x = 99;
                    return x;
                }

                int main()
                {
                    return Foo();
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(10, result.ExitCode);
        }

        [Fact]
        public void DeferInLoopExecutesPerIteration()
        {
            string code = """
                int main()
                {
                    int total = 0;
                    for (int i = 1; i <= 3; i++)
                    {
                        defer total += i;
                    }
                    return total;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(6, result.ExitCode);
        }

        [Fact]
        public void DeferInLoopBreakAndContinue()
        {
            string code = """
                int main()
                {
                    int cleaned = 0;
                    for (int i = 0; i < 5; i++)
                    {
                        defer cleaned += 1;
                        if (i == 1)
                        {
                            defer cleaned += 10;
                            continue;
                        }
                        if (i == 3)
                        {
                            defer cleaned += 100;
                            break;
                        }
                    }
                    return cleaned;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(114, result.ExitCode);
        }

        [Fact]
        public void DeferPointerFreeOnScopeExit()
        {
            string code = """
                extern void* malloc(long size);

                int main()
                {
                    int* ptr = malloc(4L);
                    *ptr = 77;
                    defer ptr->free();
                    return *ptr;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(77, result.ExitCode);
        }

        [Fact]
        public void FunctionPointerDirectCall()
        {
            string code = """
                int Add(int a, int b)
                {
                    return a + b;
                }

                int main()
                {
                    int(int, int)* fn = &Add;
                    return fn(15, 27);
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void FunctionPointerPassedAsArgument()
        {
            string code = """
                int Add(int a, int b)
                {
                    return a + b;
                }

                int Multiply(int a, int b)
                {
                    return a * b;
                }

                int Apply(int a, int b, int(int, int)* op)
                {
                    return op(a, b);
                }

                int main()
                {
                    int sum = Apply(10, 20, &Add);
                    int prod = Apply(3, 4, &Multiply);
                    return sum + prod;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void NullableFunctionPointerAndMetadataProperties()
        {
            string code = """
                int Multiply(int a, int b)
                {
                    return a * b;
                }

                int main()
                {
                    int(int, int)*? fn = null;
                    int isNullBefore = 0;
                    if (fn->is_null)
                    {
                        isNullBefore = 1;
                    }

                    fn = &Multiply;
                    int isNullAfter = 0;
                    if (fn->is_null)
                    {
                        isNullAfter = 1;
                    }

                    long addr = fn->address;
                    int hasAddress = 0;
                    if (addr != 0L)
                    {
                        hasAddress = 1;
                    }

                    int val = fn(6, 7);
                    return isNullBefore * 100 + isNullAfter * 10 + hasAddress * val;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(142, result.ExitCode);
        }

        [Fact]
        public void TypeAliasLocalAndNamespaceScope()
        {
            string code = """
                namespace Calculations
                {
                    public alias BinOp = int(int, int)*;

                    public int Sub(int a, int b)
                    {
                        return a - b;
                    }
                }

                int main()
                {
                    alias LocalOp = Calculations::BinOp;
                    LocalOp op = &Calculations::Sub;
                    return op(50, 8);
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void FunctionPointerInStructAndArray()
        {
            string code = """
                alias OpFunc = int(int, int)*;

                struct Calculator
                {
                    OpFunc op;
                }

                int Add(int a, int b)
                {
                    return a + b;
                }

                int Sub(int a, int b)
                {
                    return a - b;
                }

                int main()
                {
                    Calculator calc;
                    calc.op = &Add;
                    int r1 = calc.op(20, 30);

                    OpFunc[2] ops;
                    ops[0] = &Add;
                    ops[1] = &Sub;
                    int r2 = ops[1](r1, 8);

                    return r2;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void PointerAdditionAndDereference()
        {
            string code = """
                int main()
                {
                    int[4] arr;
                    arr[0] = 10;
                    arr[1] = 20;
                    arr[2] = 30;
                    arr[3] = 40;

                    int* p = &arr[0];
                    int v0 = *p;
                    int v1 = *(p + 1);
                    int v2 = *(2 + p);
                    int* p3 = p + 3;
                    int v3 = *p3;

                    return v0 + v1 + v2 + v3;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(100, result.ExitCode);
        }

        [Fact]
        public void PointerIncrementAndDecrement()
        {
            string code = """
                int main()
                {
                    int[3] arr;
                    arr[0] = 11;
                    arr[1] = 22;
                    arr[2] = 33;

                    int* p = &arr[0];
                    int a = *p++;
                    int b = *p;
                    int c = *++p;
                    p--;
                    int d = *p;

                    return a + b + c + d;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(88, result.ExitCode);
        }

        [Fact]
        public void PointerCompoundAssignment()
        {
            string code = """
                int main()
                {
                    int[5] arr;
                    arr[0] = 10;
                    arr[1] = 20;
                    arr[2] = 30;
                    arr[3] = 40;
                    arr[4] = 50;

                    int* p = &arr[0];
                    p += 3;
                    int v3 = *p;
                    p -= 1;
                    int v2 = *p;

                    return v3 + v2;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(70, result.ExitCode);
        }

        [Fact]
        public void PointerDifferenceReturnsElementDistance()
        {
            string code = """
                struct Node
                {
                    int a;
                    int b;
                }

                int main()
                {
                    Node[10] nodes;
                    Node* start = &nodes[2];
                    Node* end = &nodes[7];

                    long dist = end - start;
                    int d = 0;
                    if (dist == 5L)
                    {
                        d = 42;
                    }
                    return d;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void FunctionPointerArithmeticInArray()
        {
            string code = """
                alias BinaryOp = int(int, int)*;

                int Add(int a, int b)
                {
                    return a + b;
                }

                int Sub(int a, int b)
                {
                    return a - b;
                }

                int Mul(int a, int b)
                {
                    return a * b;
                }

                int main()
                {
                    BinaryOp[3] ops;
                    ops[0] = &Add;
                    ops[1] = &Sub;
                    ops[2] = &Mul;

                    BinaryOp* p = &ops[0];
                    BinaryOp fn0 = *p;
                    p++;
                    BinaryOp fn1 = *p;
                    p += 1;
                    BinaryOp fn2 = *p;

                    long diff = p - &ops[0];

                    int r0 = fn0(10, 20);
                    int r1 = fn1(50, 8);
                    int r2 = fn2(3, 4);

                    int isDiffTwo = 0;
                    if (diff == 2L)
                    {
                        isDiffTwo = 1;
                    }

                    return r1 + isDiffTwo - 1;
                }
                """;

            var result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void FunctionPointerDirectArithmetic()
        {
            string code = """
                alias BinaryOp = int(int, int)*;

                int Add(int a, int b)
                {
                    return a + b;
                }

                int Sub(int a, int b)
                {
                    return a - b;
                }

                int main()
                {
                    BinaryOp[2] ops;
                    ops[0] = &Add;
                    ops[1] = &Sub;

                    BinaryOp f0 = ops[0];
                    BinaryOp f1 = f0 + 1;
                    long diff = f1 - f0;

                    int d = 0;
                    if (diff == 1L)
                    {
                        d = 42;
                    }
                    return d;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void EnumBasicReturnExitCode()
        {
            string code = """
                enum Color
                {
                    Red,
                    Green,
                    Blue = 42
                }

                int main()
                {
                    Color c = Color::Blue;
                    return c;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void EnumMemberAccessDotAndDoubleColon()
        {
            string code = """
                enum Status
                {
                    Pending = 10,
                    Active = 20,
                    Done = 30
                }

                int main()
                {
                    Status s1 = Status::Active;
                    Status s2 = Status.Active;
                    if (s1 == s2 && s1 == 20)
                    {
                        return 42;
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void EnumAutoIncrementAndExplicitValues()
        {
            string code = """
                enum Steps
                {
                    First = 10,
                    Second,
                    Third = 25,
                    Fourth
                }

                int main()
                {
                    if (Steps::Second == 11 && Steps::Fourth == 26)
                    {
                        return 42;
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void EnumCustomBackingTypesExecution()
        {
            string code = """
                enum ByteCode : char
                {
                    Start = 1,
                    Mid = 5,
                    End = 10
                }

                enum BigVal : long
                {
                    Small = 1L,
                    Big = 32L
                }

                int main()
                {
                    ByteCode b = ByteCode::End;
                    BigVal bg = BigVal.Big;
                    if (b == ByteCode::End && bg == BigVal::Big)
                    {
                        return 42;
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void EnumInNamespaceExecution()
        {
            string code = """
                namespace Graphics
                {
                    public enum Mode
                    {
                        Wireframe = 1,
                        Solid = 2,
                        Shaded = 42
                    }
                }

                int main()
                {
                    Graphics::Mode m1 = Graphics::Mode::Shaded;
                    Graphics::Mode m2 = Graphics.Mode.Shaded;
                    if (m1 == m2 && m1 == 42)
                    {
                        return 42;
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void EnumInStructFieldExecution()
        {
            string code = """
                enum Team
                {
                    Red = 1,
                    Blue = 2
                }

                struct Player
                {
                    public Team team;
                    public int score;
                }

                int main()
                {
                    Player p;
                    p.team = Team::Blue;
                    p.score = 40;

                    if (p.team == Team::Blue)
                    {
                        return p.score + 2;
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void EnumBitwiseFlagsExecution()
        {
            string code = """
                enum Flags
                {
                    None = 0,
                    Read = 1,
                    Write = 2,
                    Execute = 4
                }

                int main()
                {
                    Flags f = Flags::Read | Flags::Execute;
                    if ((f & Flags::Execute) == Flags::Execute && (f & Flags::Write) == 0)
                    {
                        return 42;
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void UIntArithmeticAndComparisonExecution()
        {
            string code = """
                int main()
                {
                    uint large = 3000000000u;
                    uint small = 10u;
                    if (large > small)
                    {
                        uint divRes = large / 1000000000u;
                        if (divRes == 3u)
                        {
                            return 42;
                        }
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ULongArithmeticAndComparisonExecution()
        {
            string code = """
                int main()
                {
                    ulong large = 10000000000000000000ul;
                    ulong small = 5ul;
                    if (large > small)
                    {
                        ulong div = large / 1000000000000000000ul;
                        if (div == 10ul)
                        {
                            return 42;
                        }
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void PointerSizedNIntNUIntExecution()
        {
            string code = """
                int main()
                {
                    int val = 42;
                    int* ptr = &val;
                    nuint addr = (nuint)ptr;
                    int* ptr2 = (int*)addr;
                    return *ptr2;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ExplicitCastsExecution()
        {
            string code = """
                enum Status
                {
                    Ok = 42
                }

                int main()
                {
                    long l = 0x10000002al;
                    int truncated = (int)l;
                    if (truncated != 42) return 1;

                    int neg = -1;
                    uint u = (uint)neg;
                    if (u <= 0u) return 2;

                    Status s = Status::Ok;
                    int enumVal = (int)s;
                    if (enumVal != 42) return 3;

                    Status s2 = (Status)42;
                    if ((int)s2 != 42) return 4;

                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void UnsignedLogicalShiftRightExecution()
        {
            string code = """
                int main()
                {
                    uint x = 0x80000000u;
                    uint y = x >> 1;
                    if (y == 0x40000000u)
                    {
                        return 42;
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ShiftLeftExecution()
        {
            string code = """
                int main()
                {
                    uint x = 21u;
                    uint y = x << 1;
                    return (int)y;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void LambdaExpressionBodyExecution()
        {
            string code = """
                int main()
                {
                    int(int, int)* add = (int a, int b) => a + b;
                    return add(20, 22);
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void LambdaStaticExpressionBodyExecution()
        {
            string code = """
                int main()
                {
                    int(int, int)* mul = static (int a, int b) => a * b;
                    return mul(6, 7);
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void LambdaBlockBodyExecution()
        {
            string code = """
                int main()
                {
                    int(int)* sumTo = (int n) =>
                    {
                        int s = 0;
                        for (int i = 1; i <= n; i = i + 1)
                        {
                            s = s + i;
                        }
                        return s;
                    };
                    return sumTo(6) + 21;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void LambdaPassedAsFunctionArgumentExecution()
        {
            string code = """
                int Apply(int a, int b, int(int, int)* op)
                {
                    return op(a, b);
                }

                int main()
                {
                    return Apply(40, 2, (int x, int y) => x + y);
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void LambdaImmediateInvocationExecution()
        {
            string code = """
                int main()
                {
                    return ((int x, int y) => x * y)(6, 7);
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void StructBinaryOperatorAddExecution()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;

                    public static Point operator +(Point a, Point b)
                    {
                        Point res;
                        res.x = a.x + b.x;
                        res.y = a.y + b.y;
                        return res;
                    }
                }

                int main()
                {
                    Point p1;
                    p1.x = 20;
                    p1.y = 1;
                    Point p2;
                    p2.x = 20;
                    p2.y = 1;
                    Point p3 = p1 + p2;
                    return p3.x + p3.y;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void StructBinaryOperatorMultiplyScalarExecution()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;

                    public static Point operator *(Point p, int scale)
                    {
                        Point res;
                        res.x = p.x * scale;
                        res.y = p.y * scale;
                        return res;
                    }

                    public static Point operator *(int scale, Point p)
                    {
                        return p * scale;
                    }
                }

                int main()
                {
                    Point p;
                    p.x = 3;
                    p.y = 4;
                    Point scaled1 = p * 3;
                    Point scaled2 = 2 * scaled1;
                    return scaled2.x + scaled2.y;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void StructComparisonOperatorsExecution()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;

                    public static bool operator ==(Point a, Point b)
                    {
                        return a.x == b.x && a.y == b.y;
                    }

                    public static bool operator !=(Point a, Point b)
                    {
                        return !(a == b);
                    }
                }

                int main()
                {
                    Point p1;
                    p1.x = 10;
                    p1.y = 20;
                    Point p2;
                    p2.x = 10;
                    p2.y = 20;
                    Point p3;
                    p3.x = 10;
                    p3.y = 99;

                    if (p1 == p2 && p1 != p3)
                    {
                        return 42;
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void StructUnaryOperatorNegateExecution()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;

                    public static Point operator -(Point a)
                    {
                        Point res;
                        res.x = 0 - a.x;
                        res.y = 0 - a.y;
                        return res;
                    }
                }

                int main()
                {
                    Point p;
                    p.x = -20;
                    p.y = -22;
                    Point neg = -p;
                    return neg.x + neg.y;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void StructCompoundAssignmentExecution()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;

                    public static Point operator +(Point a, Point b)
                    {
                        Point res;
                        res.x = a.x + b.x;
                        res.y = a.y + b.y;
                        return res;
                    }
                }

                int main()
                {
                    Point p;
                    p.x = 10;
                    p.y = 20;
                    Point step;
                    step.x = 6;
                    step.y = 6;
                    p += step;
                    return p.x + p.y;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void StructOperatorExpressionBodyExecution()
        {
            string code = """
                struct Num
                {
                    int val;

                    public static int operator +(Num a, Num b) => a.val + b.val;
                }

                int main()
                {
                    Num n1;
                    n1.val = 19;
                    Num n2;
                    n2.val = 23;
                    return n1 + n2;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ByteArithmeticAndWideningExecution()
        {
            string code = """
                int main()
                {
                    byte a = 200;
                    byte b = 50;
                    byte c = a - b;
                    if (c != 150)
                    {
                        return 1;
                    }

                    int widened = c;
                    if (widened != 150)
                    {
                        return 2;
                    }

                    byte overflowWrap = (byte)(a + 100);
                    if (overflowWrap != 44)
                    {
                        return 3;
                    }

                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void SByteNegativeValuesExecution()
        {
            string code = """
                int main()
                {
                    sbyte a = -10;
                    sbyte b = 52;
                    sbyte c = a + b;
                    if (c != 42)
                    {
                        return 1;
                    }

                    int widened = a;
                    if (widened != -10)
                    {
                        return 2;
                    }

                    if (a > b)
                    {
                        return 3;
                    }

                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ShortAndUShortArithmeticExecution()
        {
            string code = """
                int main()
                {
                    short s = -1000;
                    ushort u = 1042;

                    int sum = s + u;
                    if (sum != 42)
                    {
                        return 1;
                    }

                    ushort uBig = 60000;
                    ushort uSmall = 10000;
                    if (uBig < uSmall)
                    {
                        return 2;
                    }

                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void CastBetweenSizesExecution()
        {
            string code = """
                int main()
                {
                    int x = 300;
                    byte b = (byte)x;
                    if (b != 44)
                    {
                        return 1;
                    }

                    int neg = -5;
                    sbyte sb = (sbyte)neg;
                    if (sb != -5)
                    {
                        return 2;
                    }

                    short s = (short)sb;
                    if (s != -5)
                    {
                        return 3;
                    }

                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void StackStructInterfaceCallExecution()
        {
            string code = """
                interface IShape
                {
                    int Area();
                    int Perimeter();
                }

                struct Rect : IShape
                {
                    int w;
                    int h;

                    int Area()
                    {
                        return this.w * this.h;
                    }

                    int Perimeter()
                    {
                        return 2 * (this.w + this.h);
                    }
                }

                int main()
                {
                    Rect r;
                    r.w = 6;
                    r.h = 7;
                    IShape* s = &r;
                    if (s.Area() != 42)
                    {
                        return 1;
                    }
                    if (s.Perimeter() != 26)
                    {
                        return 2;
                    }
                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void HeapStructInterfaceCallExecution()
        {
            string code = """
                extern void* malloc(long size);

                interface IShape
                {
                    int Area();
                }

                struct Rect : IShape
                {
                    int w;
                    int h;

                    int Area()
                    {
                        return this.w * this.h;
                    }
                }

                int main()
                {
                    Rect* r = (Rect*)malloc(8L);
                    r.w = 5;
                    r.h = 8;
                    IShape* s = r;
                    if (s.Area() != 40)
                    {
                        return 1;
                    }
                    r->free();
                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void MultipleInterfacesExecution()
        {
            string code = """
                interface IShape
                {
                    int Area();
                }

                interface IDescribable
                {
                    int Code();
                }

                struct Circle : IShape, IDescribable
                {
                    int r;

                    int Area()
                    {
                        return 3 * this.r * this.r;
                    }

                    int Code()
                    {
                        return 123;
                    }
                }

                int main()
                {
                    Circle c;
                    c.r = 4;
                    IShape* s = &c;
                    IDescribable* d = &c;
                    if (s.Area() != 48)
                    {
                        return 1;
                    }
                    if (d.Code() != 123)
                    {
                        return 2;
                    }
                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void InterfacePassedAsParameterExecution()
        {
            string code = """
                interface ICalc
                {
                    int Compute(int x);
                }

                struct Multiplier : ICalc
                {
                    int factor;

                    int Compute(int x)
                    {
                        return x * this.factor;
                    }
                }

                struct Adder : ICalc
                {
                    int delta;

                    int Compute(int x)
                    {
                        return x + this.delta;
                    }
                }

                int ExecuteCalc(ICalc* calc, int val)
                {
                    return calc.Compute(val);
                }

                int main()
                {
                    Multiplier m;
                    m.factor = 3;

                    Adder a;
                    a.delta = 10;

                    int res1 = ExecuteCalc(&m, 5);
                    int res2 = ExecuteCalc(&a, 5);

                    if (res1 + res2 != 30)
                    {
                        return 1;
                    }
                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void InterfaceNullComparisonExecution()
        {
            string code = """
                interface IShape
                {
                    int Area();
                }

                struct Rect : IShape
                {
                    int Area()
                    {
                        return 1;
                    }
                }

                int main()
                {
                    IShape*? s = null;
                    if (s != null)
                    {
                        return 1;
                    }

                    Rect r;
                    s = &r;
                    if (s == null)
                    {
                        return 2;
                    }
                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void DefaultLiteralStructExecution()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                }

                int main()
                {
                    Point p = default;
                    if (p.x != 0 || p.y != 0)
                    {
                        return 1;
                    }
                    Point p2 = default(Point);
                    if (p2.x != 0 || p2.y != 0)
                    {
                        return 2;
                    }
                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void DefaultPrimitiveExecution()
        {
            string code = """
                int main()
                {
                    int x = default;
                    bool b = default;
                    int*? ptr = default;
                    int y = default(int);

                    if (x != 0 || b != false || ptr != null || y != 0)
                    {
                        return 1;
                    }

                    int* rawPtr = default;
                    if (!rawPtr->is_null)
                    {
                        return 2;
                    }
                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void FieldInitializerExecution()
        {
            string code = """
                struct Point
                {
                    int x = 15;
                    int y = 27;
                }

                int main()
                {
                    Point p = new Point();
                    if (p.x != 15 || p.y != 27)
                    {
                        return 1;
                    }

                    Point* hp = new* Point();
                    if (hp.x != 15 || hp.y != 27)
                    {
                        hp->free();
                        return 2;
                    }
                    hp->free();
                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void CustomConstructorStackExecution()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;

                    Point(int x, int y)
                    {
                        this.x = x;
                        this.y = y;
                    }
                }

                int main()
                {
                    Point p = new Point(10, 32);
                    return p.x + p.y;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void CustomConstructorHeapExecution()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;

                    Point(int x, int y)
                    {
                        this.x = x;
                        this.y = y;
                    }
                }

                int main()
                {
                    Point* p = new* Point(20, 22);
                    int sum = p.x + p.y;
                    p->free();
                    return sum;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ConstructorWithFieldInitializersExecution()
        {
            string code = """
                struct Widget
                {
                    int a = 10;
                    int b = 20;
                    int c = 30;

                    Widget(int customB)
                    {
                        this.b = customB;
                    }
                }

                int main()
                {
                    Widget w = new Widget(99);
                    if (w.a != 10 || w.b != 99 || w.c != 30)
                    {
                        return 1;
                    }
                    return 42;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void MultipleConstructorOverloadsExecution()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;

                    Point()
                    {
                        this.x = 1;
                        this.y = 2;
                    }

                    Point(int val)
                    {
                        this.x = val;
                        this.y = val;
                    }

                    Point(int x, int y)
                    {
                        this.x = x;
                        this.y = y;
                    }
                }

                int main()
                {
                    Point p0 = new Point();
                    Point p1 = new Point(10);
                    Point p2 = new Point(5, 14);

                    int total = (p0.x + p0.y) + (p1.x + p1.y) + (p2.x + p2.y);
                    // (1 + 2) + (10 + 10) + (5 + 14) = 3 + 20 + 19 = 42
                    return total;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }
    }
}

