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

        [Fact]
        public void ClassBasicInheritanceExecution()
        {
            string code = """
                class Animal
                {
                    public int age;

                    public Animal()
                    {
                        this.age = 0;
                    }

                    public Animal(int a)
                    {
                        this.age = a;
                    }

                    public int GetAge()
                    {
                        return this.age;
                    }
                }

                class Dog : Animal
                {
                    public int barkPitch;

                    public Dog(int a, int pitch)
                    {
                        this.age = a;
                        this.barkPitch = pitch;
                    }

                    public int GetPitch()
                    {
                        return this.barkPitch;
                    }
                }

                int main()
                {
                    Dog d = new Dog(5, 42);
                    return d.GetAge() + d.GetPitch();
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(47, result.ExitCode);
        }

        [Fact]
        public void ClassVirtualMethodDispatchExecution()
        {
            string code = """
                class Animal
                {
                    public int id;

                    public Animal()
                    {
                        this.id = 0;
                    }

                    public Animal(int id)
                    {
                        this.id = id;
                    }

                    public virtual int Speak()
                    {
                        return 10;
                    }
                }

                class Dog : Animal
                {
                    public Dog(int id)
                    {
                        this.id = id;
                    }

                    public override int Speak()
                    {
                        return 20;
                    }
                }

                class Cat : Animal
                {
                    public Cat(int id)
                    {
                        this.id = id;
                    }

                    public override int Speak()
                    {
                        return 30;
                    }
                }

                int MakeSound(Animal* a)
                {
                    return a.Speak();
                }

                int main()
                {
                    Dog d = new Dog(1);
                    Cat c = new Cat(2);
                    Animal baseAnimal = new Animal(3);

                    int s1 = MakeSound(&d);
                    int s2 = MakeSound(&c);
                    int s3 = MakeSound(&baseAnimal);

                    return s1 + s2 + s3; // 20 + 30 + 10 = 60
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(60, result.ExitCode);
        }

        [Fact]
        public void ClassAbstractMethodExecution()
        {
            string code = """
                abstract class Shape
                {
                    public abstract int Area();

                    public int ScaleArea(int factor)
                    {
                        return this.Area() * factor;
                    }
                }

                class Rect : Shape
                {
                    public int w;
                    public int h;

                    public Rect(int w, int h)
                    {
                        this.w = w;
                        this.h = h;
                    }

                    public override int Area()
                    {
                        return this.w * this.h;
                    }
                }

                int main()
                {
                    Rect r = new Rect(4, 5);
                    Shape* s = &r;
                    return s.ScaleArea(3); // (4 * 5) * 3 = 60
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(60, result.ExitCode);
        }

        [Fact]
        public void ClassMultiLevelInheritanceExecution()
        {
            string code = """
                class A
                {
                    public virtual int Value()
                    {
                        return 1;
                    }
                }

                class B : A
                {
                    public override int Value()
                    {
                        return 10;
                    }
                }

                class C : B
                {
                    public override int Value()
                    {
                        return 100;
                    }
                }

                int GetVal(A* a)
                {
                    return a.Value();
                }

                int main()
                {
                    A a = new A();
                    B b = new B();
                    C c = new C();

                    return GetVal(&a) + GetVal(&b) + GetVal(&c); // 1 + 10 + 100 = 111
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(111, result.ExitCode);
        }

        [Fact]
        public void ClassHeapAllocationAndFree()
        {
            string code = """
                class Widget
                {
                    public int width;
                    public int height;

                    public Widget(int w, int h)
                    {
                        this.width = w;
                        this.height = h;
                    }

                    public virtual int Area()
                    {
                        return this.width * this.height;
                    }
                }

                int main()
                {
                    Widget* w = new* Widget(6, 7);
                    int area = w.Area();
                    w->free();
                    return area;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ClassAbiLayoutAndRawDispatchExecution()
        {
            string code = """
                class Animal
                {
                    public int age;

                    public Animal()
                    {
                        this.age = 0;
                    }

                    public Animal(int a)
                    {
                        this.age = a;
                    }

                    public virtual int Speak()
                    {
                        return 100;
                    }
                }

                class Dog : Animal
                {
                    public int barkPitch;

                    public Dog(int a, int pitch)
                    {
                        this.age = a;
                        this.barkPitch = pitch;
                    }

                    public override int Speak()
                    {
                        return 200 + this.barkPitch;
                    }
                }

                int main()
                {
                    Dog* dog = new* Dog(7, 42);

                    // 1. Verify byte layout compatibility with C++ ABI:
                    // In C++ layout on 64-bit platforms:
                    // Offset 0..7:   vtable pointer (8 bytes)
                    // Offset 8..11:  Animal.age (4 bytes)
                    // Offset 12..15: Dog.barkPitch (4 bytes)
                    byte* raw = (byte*)dog;
                    int ageVal = *(int*)(raw + 8);
                    int pitchVal = *(int*)(raw + 12);

                    if (ageVal != 7 || pitchVal != 42)
                    {
                        dog->free();
                        return 1;
                    }

                    // 2. Direct ABI vtable dispatch:
                    // Load vtable pointer at offset 0
                    void*** vtablePtrLocation = (void***)dog;
                    void** vtable = *vtablePtrLocation;

                    // Slot 0 is Speak()
                    void* rawMethod = vtable[0];
                    int(Dog*)* directFn = (int(Dog*)*)rawMethod;

                    // Invoke slot 0 directly with 'dog' as 'this'
                    int directResult = directFn(dog);

                    dog->free();

                    // Expected directResult = 200 + 42 = 242
                    if (directResult != 242)
                    {
                        return 2;
                    }

                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(0, result.ExitCode);
        }

        [Fact]
        public void ClassBaseConstructorExecution()
        {
            string code = """
                class Shape
                {
                    public int x;
                    public int y;

                    public Shape(int x, int y)
                    {
                        this.x = x;
                        this.y = y;
                    }
                }

                class Rectangle : Shape
                {
                    public int width;
                    public int height;

                    public Rectangle(int x, int y, int w, int h) : base(x, y)
                    {
                        this.width = w;
                        this.height = h;
                    }

                    public int Area()
                    {
                        return this.width * this.height;
                    }
                }

                int main()
                {
                    Rectangle r = new Rectangle(10, 20, 5, 4);
                    return r.x + r.y + r.Area(); // 10 + 20 + 20 = 50
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(50, result.ExitCode);
        }

        [Fact]
        public void ClassDestructorExecution()
        {
            string code = """
                class Resource
                {
                    public int* counter;

                    public Resource(int* c)
                    {
                        this.counter = c;
                    }

                    public ~Resource()
                    {
                        *this.counter = *this.counter + 10;
                    }
                }

                int main()
                {
                    int flag = 5;
                    Resource* r = new* Resource(&flag);
                    r->free();
                    return flag; // 15
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(15, result.ExitCode);
        }

        [Fact]
        public void ClassPolymorphicVirtualDestructorExecution()
        {
            string code = """
                class Base
                {
                    public int* counter;

                    public Base(int* c)
                    {
                        this.counter = c;
                    }

                    public virtual ~Base()
                    {
                        *this.counter = *this.counter + 1;
                    }
                }

                class Derived : Base
                {
                    public Derived(int* c) : base(c)
                    {
                    }

                    public ~Derived()
                    {
                        *this.counter = *this.counter + 10;
                    }
                }

                int main()
                {
                    int c = 0;
                    Base* b = new* Derived(&c);
                    b->free(); // Virtual destructor dispatch: Derived runs (+10), then Base runs (+1)
                    return c; // 11
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(11, result.ExitCode);
        }

        [Fact]
        public void ClassMultipleInterfacesExecution()
        {
            string code = """
                interface IGreet
                {
                    int Greet();
                }

                interface ICalc
                {
                    int Add(int a, int b);
                }

                class Service : IGreet, ICalc
                {
                    public int factor;

                    public Service(int f)
                    {
                        this.factor = f;
                    }

                    public int Greet()
                    {
                        return 100 * this.factor;
                    }

                    public int Add(int a, int b)
                    {
                        return (a + b) * this.factor;
                    }
                }

                int main()
                {
                    Service* s = new* Service(2);
                    IGreet* g = s;
                    ICalc* c = s;

                    int r1 = g.Greet();   // 100 * 2 = 200
                    int r2 = c.Add(3, 4); // (3 + 4) * 2 = 14
                    s->free();
                    return r1 + r2;       // 214
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(214, result.ExitCode);
        }

        [Fact]
        public void ClassAbiVirtualDestructorSlotMatchesCppExecution()
        {
            string code = """
                class Base
                {
                    public int* tracker;

                    public Base(int* t)
                    {
                        this.tracker = t;
                    }

                    public virtual ~Base()
                    {
                        *this.tracker = *this.tracker + 1;
                    }

                    public virtual int Value()
                    {
                        return 10;
                    }
                }

                class Derived : Base
                {
                    public Derived(int* t) : base(t) { }

                    public ~Derived()
                    {
                        *this.tracker = *this.tracker + 20;
                    }

                    public override int Value()
                    {
                        return 30;
                    }
                }

                int main()
                {
                    int tracker = 0;
                    Derived* d = new* Derived(&tracker);
                    Base* b = d;

                    // b.Value() is virtual slot 0
                    if (b.Value() != 30) return 1;

                    // b->free() invokes virtual destructor
                    b->free();

                    // Derived destructor (20) + Base destructor (1) = 21
                    if (tracker != 21) return 2;

                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(0, result.ExitCode);
        }

        [Fact]
        public void ReadonlyPointerExecution()
        {
            string code = """
                int Sum(readonly int* a, readonly int* b)
                {
                    return *a + *b;
                }

                int main()
                {
                    int x = 15;
                    int y = 27;
                    return Sum(&x, &y);
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ReadonlyMethodExecution()
        {
            string code = """
                struct Rectangle
                {
                    int width;
                    int height;

                    public readonly int Area()
                    {
                        return this.width * this.height;
                    }
                }

                int main()
                {
                    Rectangle r;
                    r.width = 6;
                    r.height = 7;
                    readonly Rectangle* ptr = &r;
                    return ptr.Area();
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ReadonlyFieldExecution()
        {
            string code = """
                class User
                {
                    readonly int id;
                    int score;

                    public User(int id, int score)
                    {
                        this.id = id;
                        this.score = score;
                    }

                    public readonly int GetTotal()
                    {
                        return this.id + this.score;
                    }
                }

                int main()
                {
                    User* u = new* User(10, 32);
                    int total = u.GetTotal();
                    u->free();
                    return total;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ConstFunction_RuntimeExecution()
        {
            string code = """
                const int Multiply(int a, int b)
                {
                    return a * b;
                }

                int main()
                {
                    int x = 6;
                    int y = 7;
                    return Multiply(x, y);
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ConstParameter_RuntimeExecution()
        {
            string code = """
                int AddConstant(const int step, int val)
                {
                    return val + step;
                }

                int main()
                {
                    int x = 40;
                    return AddConstant(2, x);
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ConstVariable_ArraySizingExecution()
        {
            string code = """
                const int BufferSize = 5;

                int main()
                {
                    int[BufferSize] arr;
                    for (int i = 0; i < BufferSize; i++)
                    {
                        arr[i] = i * 2;
                    }
                    return arr[4];
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(8, result.ExitCode);
        }

        [Fact]
        public void ConstVariable_InliningExecution()
        {
            string code = """
                const int A = 15;
                const int B = 27;

                int main()
                {
                    return A + B;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void Sizeof_RuntimeExecution()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                }

                int main()
                {
                    int sizeInt = sizeof(int);
                    int sizePoint = sizeof(Point);
                    int sizeArray = sizeof(int[5]);
                    int sizePtr = sizeof(void*);
                    int total = sizeInt + sizePoint + sizeArray + sizePtr + 2;
                    return total;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void Nameof_RuntimeExecution()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                }

                int main()
                {
                    char* name = nameof(Point);
                    if (name[0] == 'P' && name[4] == 't')
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
        public void CStringPrefix_Execution()
        {
            string code = """
                int main()
                {
                    readonly char* s = c"hello";
                    if (s[0] == 'h' && s[4] == 'o' && s[5] == '\0')
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
        public void CustomStringPrefix_Struct_Execution()
        {
            string code = """
                [string_prefix("sql")]
                struct SqlQuery
                {
                    readonly char* query;
                    const SqlQuery(readonly char* raw)
                    {
                        this.query = raw;
                    }
                }

                int main()
                {
                    SqlQuery q = sql"SELECT * FROM users";
                    if (q.query[0] == 'S' && q.query[6] == ' ')
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
        public void CustomStringPrefix_ConstEvaluation_Execution()
        {
            string code = """
                [string_prefix("hash")]
                const uint HashString(readonly char* str)
                {
                    uint h = 0u;
                    for (int i = 0; str[i] != '\0'; i++)
                    {
                        h = h * 31u + (uint)str[i];
                    }
                    return h;
                }

                int main()
                {
                    const uint H = hash"hello";
                    int[H % 10u + 1u] buffer;
                    if (sizeof(buffer) > 0 && H > 0u)
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
        public void CustomStringPrefix_QualifiedDisambiguation_Execution()
        {
            string code = """
                namespace Postgres
                {
                    [string_prefix("sql")]
                    struct PgQuery
                    {
                        int dbType;
                        const PgQuery(readonly char* raw)
                        {
                            this.dbType = 1;
                        }
                    }
                }

                namespace Sqlite
                {
                    [string_prefix("sql")]
                    struct LiteQuery
                    {
                        int dbType;
                        const LiteQuery(readonly char* raw)
                        {
                            this.dbType = 2;
                        }
                    }
                }

                int main()
                {
                    Postgres::PgQuery p = Postgres::sql"SELECT 1";
                    Sqlite::LiteQuery s = Sqlite::sql"SELECT 2";
                    if (p.dbType == 1 && s.dbType == 2)
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
        public void GenericStruct_Monomorphization_Executes()
        {
            string code = """
                struct Pair<T, U>
                {
                    T first;
                    U second;
                }

                int main()
                {
                    Pair<int, int> p;
                    p.first = 30;
                    p.second = 12;
                    return p.first + p.second;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void GenericFunction_ExplicitTypeArgs_Executes()
        {
            string code = """
                T Add<T>(T a, T b)
                {
                    return a + b;
                }

                int main()
                {
                    return Add<int>(20, 22);
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void GenericFunction_InferredTypeArgs_Executes()
        {
            string code = """
                T Max<T>(T a, T b)
                {
                    if (a > b) return a;
                    return b;
                }

                int main()
                {
                    return Max(15, 42);
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void GenericConstraint_Interface_Executes()
        {
            string code = """
                interface HasValue
                {
                    int GetValue();
                }

                struct Item : HasValue
                {
                    int val;
                    int GetValue()
                    {
                        return this.val;
                    }
                }

                int Extract<T : HasValue>(T item)
                {
                    return item.GetValue();
                }

                int main()
                {
                    Item it;
                    it.val = 42;
                    return Extract(it);
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void GenericClass_Monomorphization_Executes()
        {
            string code = """
                class Box<T>
                {
                    public T value;
                    public void Set(T v)
                    {
                        this.value = v;
                    }
                    public T Get()
                    {
                        return this.value;
                    }
                }

                int main()
                {
                    Box<int> b = new Box<int>();
                    b.Set(42);
                    return b.Get();
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void GenericConstraint_BaseClass_Executes()
        {
            string code = """
                class Entity
                {
                    public int id;
                }

                class Player : Entity
                {
                    public int score;
                }

                int GetEntityId<T : Entity>(T e)
                {
                    return e.id;
                }

                int main()
                {
                    Player p = new Player();
                    p.id = 42;
                    p.score = 100;
                    return GetEntityId(p);
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ConstPointer_Struct_Executes()
        {
            string code = """
                struct Point
                {
                    public int x;
                    public int y;
                    public Point(int a, int b) { x = a; y = b; }
                }

                const Point* p = new* Point(10, 25);

                int main()
                {
                    return p.x + p.y;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(35, result.ExitCode);
        }

        [Fact]
        public void ConstPointer_Class_VirtualDispatch_Executes()
        {
            string code = """
                class Exception
                {
                    public readonly char* message;
                    public int code;
                    public Exception(readonly char* msg, int c)
                    {
                        message = msg;
                        code = c;
                    }
                    public readonly virtual int GetCode()
                    {
                        return code;
                    }
                }

                const Exception* OutOfMemory = new* Exception(c"Out of memory", 42);

                int main()
                {
                    return OutOfMemory.GetCode();
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ConstPointer_AddressOf_Executes()
        {
            string code = """
                struct Point
                {
                    public int x;
                    public int y;
                    public Point(int a, int b) { x = a; y = b; }
                }

                const Point Origin = new Point(15, 27);
                const Point* pOrigin = &Origin;

                int main()
                {
                    return pOrigin.x + pOrigin.y;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void ConstPointer_Dereference_Executes()
        {
            string code = """
                struct Point
                {
                    public int x;
                    public int y;
                    public Point(int a, int b) { x = a; y = b; }
                }

                const Point* p = new* Point(10, 20);

                int main()
                {
                    Point pt = *p;
                    return pt.x + pt.y;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(30, result.ExitCode);
        }

        [Fact]
        public void ConstPointer_ClassInheritance_Executes()
        {
            string code = """
                class BaseException
                {
                    public int code;
                    public BaseException(int c) { code = c; }
                    public readonly virtual int GetCode() { return code; }
                }

                class CustomException : BaseException
                {
                    public int extra;
                    public CustomException(int c, int e) : base(c) { extra = e; }
                    public readonly override int GetCode() { return code + extra; }
                }

                const CustomException* oom = new* CustomException(10, 32);

                int main()
                {
                    const BaseException* basePtr = oom;
                    return basePtr.GetCode();
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void Execution_ThrowAndCatch_Basic_ReturnsExitCode()
        {
            string code = """
                int Thrower()
                {
                    throw new* Exception(c"Error occurred", 42);
                    return 0;
                }

                int main()
                {
                    try
                    {
                        Thrower();
                    }
                    catch (Exception* ex)
                    {
                        return ex.GetCode();
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void Execution_ThrowAndCatch_Polymorphic_DispatchesCorrectCatch()
        {
            string code = """
                class CustomException : Exception
                {
                    public int extra;
                    public CustomException(readonly char* msg, int c, int e) : base(msg, c)
                    {
                        extra = e;
                    }
                    public readonly override int GetCode()
                    {
                        return code + extra;
                    }
                }

                int Thrower()
                {
                    throw new* CustomException(c"Custom error", 10, 32);
                    return 0;
                }

                int main()
                {
                    try
                    {
                        Thrower();
                    }
                    catch (Exception* ex)
                    {
                        return ex.GetCode();
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void Execution_MultipleCatchBlocks_SelectsSpecificDerivedHandler()
        {
            string code = """
                class SpecificException : Exception
                {
                    public SpecificException(int c) : base(c"", c) {}
                }

                class AnotherException : Exception
                {
                    public AnotherException(int c) : base(c"", c) {}
                }

                int Run(int kind)
                {
                    try
                    {
                        if (kind == 1)
                        {
                            throw new* SpecificException(10);
                        }
                        else
                        {
                            throw new* AnotherException(20);
                        }
                    }
                    catch (SpecificException* se)
                    {
                        return se.GetCode() + 1;
                    }
                    catch (AnotherException* ae)
                    {
                        return ae.GetCode() + 2;
                    }
                    return 0;
                }

                int main()
                {
                    int r1 = Run(1);
                    int r2 = Run(2);
                    return r1 + r2;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(33, result.ExitCode);
        }

        [Fact]
        public void Execution_ConstException_NotFreed_Executes()
        {
            string code = """
                const Exception* OutOfMemory = new* Exception(c"Out of memory", 99);

                int ThrowConst()
                {
                    throw OutOfMemory;
                    return 0;
                }

                int main()
                {
                    try
                    {
                        ThrowConst();
                    }
                    catch (Exception* ex)
                    {
                        return ex.GetCode();
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(99, result.ExitCode);
        }

        [Fact]
        public void Execution_Defer_ExecutesOnThrowUnwinding()
        {
            string code = """
                void Throwing(int* flag)
                {
                    defer *flag += 10;
                    defer *flag += 20;
                    throw new* Exception(c"Fail", 1);
                }

                int main()
                {
                    int flag = 0;
                    try
                    {
                        Throwing(&flag);
                    }
                    catch (Exception* ex)
                    {
                        return flag;
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(30, result.ExitCode);
        }

        [Fact]
        public void Execution_CatchAndRethrow_PropagatesToOuterCatch()
        {
            string code = """
                int Sub()
                {
                    try
                    {
                        throw new* Exception(c"Original", 55);
                    }
                    catch (Exception* ex)
                    {
                        throw ex;
                    }
                    return 0;
                }

                int main()
                {
                    try
                    {
                        Sub();
                    }
                    catch (Exception* ex)
                    {
                        return ex.GetCode();
                    }
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(55, result.ExitCode);
        }

        [Fact]
        public void Execution_UnhandledException_PrintsAndExitsWith1()
        {
            string code = """
                int main()
                {
                    throw new* Exception(c"Fatal error", 123);
                    return 0;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Fatal error", result.StandardOutput);
        }

        [Fact]
        public void Execution_NestedClass_Basic()
        {
            string code = """
                class Outer
                {
                    public int x;

                    public class Inner
                    {
                        public int y;
                        public Inner(int y)
                        {
                            this.y = y;
                        }
                        public int GetDouble()
                        {
                            return this.y * 2;
                        }
                    }

                    public Outer(int x)
                    {
                        this.x = x;
                    }
                }

                int main()
                {
                    Outer* o = new* Outer(10);
                    Outer.Inner* i = new* Outer.Inner(16);
                    int res = o.x + i.GetDouble(); // 10 + 32 = 42
                    i->free();
                    o->free();
                    return res;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void Execution_NestedStruct_Basic()
        {
            string code = """
                class Collection
                {
                    public struct Iterator
                    {
                        public int index;
                        public int step;

                        public Iterator(int start, int step)
                        {
                            this.index = start;
                            this.step = step;
                        }

                        public int Next()
                        {
                            int cur = this.index;
                            this.index = this.index + this.step;
                            return cur;
                        }
                    }
                }

                int main()
                {
                    Collection.Iterator it = new Collection.Iterator(10, 5);
                    int a = it.Next(); // 10
                    int b = it.Next(); // 15
                    int c = it.Next(); // 20
                    return a + b + c; // 45
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(45, result.ExitCode);
        }

        [Fact]
        public void Execution_NestedClass_UnqualifiedInsideOuter()
        {
            string code = """
                class Calculator
                {
                    public class Worker
                    {
                        public int baseVal;
                        public Worker(int b)
                        {
                            this.baseVal = b;
                        }
                        public int Compute(int x)
                        {
                            return this.baseVal + x;
                        }
                    }

                    public Worker* CreateWorker(int b)
                    {
                        return new* Worker(b);
                    }
                }

                int main()
                {
                    Calculator* calc = new* Calculator();
                    Calculator.Worker* w = calc.CreateWorker(30);
                    int ans = w.Compute(12); // 42
                    w->free();
                    calc->free();
                    return ans;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void Execution_NestedEnumeratorPattern()
        {
            string code = """
                class IntList
                {
                    public struct Enumerator
                    {
                        public int current;
                        public int limit;

                        public Enumerator(int start, int limit)
                        {
                            this.current = start - 1;
                            this.limit = limit;
                        }

                        public bool MoveNext()
                        {
                            this.current = this.current + 1;
                            return this.current < this.limit;
                        }

                        public int Current()
                        {
                            return this.current;
                        }
                    }

                    public int count;

                    public IntList(int c)
                    {
                        this.count = c;
                    }

                    public Enumerator GetEnumerator()
                    {
                        return new Enumerator(0, this.count);
                    }
                }

                int main()
                {
                    IntList* list = new* IntList(5);
                    IntList.Enumerator it = list.GetEnumerator();
                    int sum = 0;
                    while (it.MoveNext())
                    {
                        sum = sum + it.Current(); // 0 + 1 + 2 + 3 + 4 = 10
                    }
                    list->free();
                    return sum;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(10, result.ExitCode);
        }

        [Fact]
        public void Execution_Foreach_Array_Sum()
        {
            string code = """
                int main()
                {
                    int[5] arr;
                    arr[0] = 1;
                    arr[1] = 2;
                    arr[2] = 3;
                    arr[3] = 4;
                    arr[4] = 5;

                    int sum = 0;
                    foreach (int x in arr)
                    {
                        sum = sum + x;
                    }
                    return sum;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(15, result.ExitCode);
        }

        [Fact]
        public void Execution_Foreach_Array_BreakAndContinue()
        {
            string code = """
                int main()
                {
                    int[10] arr;
                    int i = 0;
                    while (i < 10)
                    {
                        arr[i] = i + 1;
                        i = i + 1;
                    }

                    int sum = 0;
                    foreach (int x in arr)
                    {
                        if (x == 3)
                        {
                            continue;
                        }
                        if (x == 7)
                        {
                            break;
                        }
                        sum = sum + x; // 1 + 2 + 4 + 5 + 6 = 18
                    }
                    return sum;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(18, result.ExitCode);
        }

        [Fact]
        public void Execution_Foreach_StringLiteral()
        {
            string code = """
                int main()
                {
                    int count = 0;
                    foreach (char c in "Hello")
                    {
                        if (c != '\0')
                        {
                            count = count + 1;
                        }
                    }
                    return count;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(5, result.ExitCode);
        }

        [Fact]
        public void Execution_Foreach_NestedLoops()
        {
            string code = """
                int main()
                {
                    int[3] a;
                    a[0] = 1; a[1] = 2; a[2] = 3;

                    int[2] b;
                    b[0] = 10; b[1] = 20;

                    int total = 0;
                    foreach (int x in a)
                    {
                        foreach (int y in b)
                        {
                            total = total + x * y;
                        }
                    }
                    // (1+2+3) * (10+20) = 6 * 30 = 180
                    return total;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(180, result.ExitCode);
        }

        [Fact]
        public void Execution_Foreach_CustomCollection_Struct()
        {
            string code = """
                struct RangeEnumerator
                {
                    int current;
                    int max;

                    bool MoveNext()
                    {
                        this.current = this.current + 1;
                        return this.current <= this.max;
                    }

                    int Current()
                    {
                        return this.current;
                    }
                }

                struct Range
                {
                    int max;

                    RangeEnumerator GetEnumerator()
                    {
                        RangeEnumerator it;
                        it.current = 0;
                        it.max = this.max;
                        return it;
                    }
                }

                int main()
                {
                    Range r;
                    r.max = 5;
                    int sum = 0;
                    foreach (int x in r)
                    {
                        sum = sum + x; // 1 + 2 + 3 + 4 + 5 = 15
                    }
                    return sum;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(15, result.ExitCode);
        }

        [Fact]
        public void Execution_Foreach_CustomCollection_ClassWithNestedStruct()
        {
            string code = """
                class IntList
                {
                    public struct Enumerator
                    {
                        public int current;
                        public int limit;

                        public bool MoveNext()
                        {
                            this.current = this.current + 1;
                            return this.current < this.limit;
                        }

                        public int Current()
                        {
                            return this.current;
                        }
                    }

                    public int count;

                    public IntList(int c)
                    {
                        this.count = c;
                    }

                    public Enumerator GetEnumerator()
                    {
                        Enumerator it;
                        it.current = -1;
                        it.limit = this.count;
                        return it;
                    }
                }

                int main()
                {
                    IntList* list = new* IntList(5);
                    int sum = 0;
                    foreach (int x in list)
                    {
                        sum = sum + x; // 0 + 1 + 2 + 3 + 4 = 10
                    }
                    list->free();
                    return sum;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(10, result.ExitCode);
        }

        [Fact]
        public void Execution_Foreach_CustomCollection_CurrentField()
        {
            string code = """
                struct StepEnumerator
                {
                    public int Current;
                    public int max;

                    public bool MoveNext()
                    {
                        this.Current = this.Current + 2;
                        return this.Current <= this.max;
                    }
                }

                struct Stepper
                {
                    public int max;

                    public StepEnumerator GetEnumerator()
                    {
                        StepEnumerator it;
                        it.Current = 0;
                        it.max = this.max;
                        return it;
                    }
                }

                int main()
                {
                    Stepper s;
                    s.max = 8;
                    int sum = 0;
                    foreach (int x in s)
                    {
                        sum = sum + x; // 2 + 4 + 6 + 8 = 20
                    }
                    return sum;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(20, result.ExitCode);
        }

        [Fact]
        public void Execution_GenericStruct_DirectNestedTypeAccess()
        {
            string code = """
                struct Wrapper<T>
                {
                    public struct Box
                    {
                        public T item;
                    }
                }

                int main()
                {
                    Wrapper<int>.Box b;
                    b.item = 77;
                    return b.item;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(77, result.ExitCode);
        }

        [Fact]
        public void Execution_GenericCollection_WithNestedEnumerator()
        {
            string code = """
                class List<T>
                {
                    public struct Enumerator
                    {
                        public int current;
                        public int limit;

                        public bool MoveNext()
                        {
                            this.current = this.current + 1;
                            return this.current < this.limit;
                        }

                        public int Current()
                        {
                            return this.current;
                        }
                    }

                    public int count;

                    public List(int c)
                    {
                        this.count = c;
                    }

                    public Enumerator GetEnumerator()
                    {
                        Enumerator it;
                        it.current = -1;
                        it.limit = this.count;
                        return it;
                    }
                }

                int main()
                {
                    List<int>* listInt = new* List<int>(4);
                    int sum = 0;
                    foreach (int x in listInt)
                    {
                        sum = sum + x; // 0 + 1 + 2 + 3 = 6
                    }
                    listInt->free();

                    List<double>* listDbl = new* List<double>(5);
                    foreach (int y in listDbl)
                    {
                        sum = sum + y; // 6 + 0 + 1 + 2 + 3 + 4 = 16
                    }
                    listDbl->free();

                    return sum;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(16, result.ExitCode);
        }

        [Fact]
        public void Execution_Struct_Destructor_Free()
        {
            string code = """
                struct Tracker
                {
                    public int* pState;

                    public ~Tracker()
                    {
                        *this.pState = *this.pState + 10;
                    }
                }

                int main()
                {
                    int state = 5;
                    Tracker* t = new* Tracker();
                    t.pState = &state;
                    t->free();
                    return state; // 5 + 10 = 15
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(15, result.ExitCode);
        }

        [Fact]
        public void Execution_Struct_AutomaticScopeDestructor_BlockExit()
        {
            string code = """
                struct Guard
                {
                    public int* pVal;

                    public ~Guard()
                    {
                        *this.pVal = *this.pVal * 2;
                    }
                }

                int main()
                {
                    int val = 21;
                    {
                        Guard g;
                        g.pVal = &val;
                    }
                    return val; // 21 * 2 = 42
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void Execution_Struct_AutomaticScopeDestructor_EarlyReturn()
        {
            string code = """
                struct Guard
                {
                    public int* pVal;

                    public ~Guard()
                    {
                        *this.pVal = *this.pVal + 7;
                    }
                }

                int test(int* pVal)
                {
                    Guard g;
                    g.pVal = pVal;
                    return 100;
                }

                int main()
                {
                    int val = 3;
                    int res = test(&val);
                    return val; // 3 + 7 = 10
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(10, result.ExitCode);
        }

        [Fact]
        public void Execution_Struct_AutomaticScopeDestructor_LIFOOrder()
        {
            string code = """
                struct Stepper
                {
                    public int* pState;
                    public int add;

                    public ~Stepper()
                    {
                        *this.pState = *this.pState * 10 + this.add;
                    }
                }

                int main()
                {
                    int state = 0;
                    {
                        Stepper s1;
                        s1.pState = &state;
                        s1.add = 1;

                        defer state = state * 10 + 2;

                        Stepper s3;
                        s3.pState = &state;
                        s3.add = 3;
                    }
                    // LIFO order: s3 (add 3), then defer (add 2), then s1 (add 1)
                    // 0 -> *10 + 3 = 3 -> *10 + 2 = 32 -> *10 + 1 = 321
                    return state;
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(321, result.ExitCode);
        }

        [Fact]
        public void Execution_Foreach_DisposeCalled()
        {
            string code = """
                struct DisposingEnumerator
                {
                    public int current;
                    public int limit;
                    public int* pDisposed;

                    public bool MoveNext()
                    {
                        this.current = this.current + 1;
                        return this.current < this.limit;
                    }

                    public int Current()
                    {
                        return this.current;
                    }

                    public void Dispose()
                    {
                        *this.pDisposed = *this.pDisposed + 50;
                    }
                }

                struct DisposingCollection
                {
                    public int* pDisposed;

                    public DisposingEnumerator GetEnumerator()
                    {
                        DisposingEnumerator it;
                        it.current = -1;
                        it.limit = 3;
                        it.pDisposed = this.pDisposed;
                        return it;
                    }
                }

                int main()
                {
                    int disposedState = 0;
                    DisposingCollection col;
                    col.pDisposed = &disposedState;

                    int sum = 0;
                    foreach (int x in col)
                    {
                        sum = sum + x; // 0 + 1 + 2 = 3
                    }

                    return sum + disposedState; // 3 + 50 = 53
                }
                """;

            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(53, result.ExitCode);
        }
    }
}

