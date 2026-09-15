using gflat.ast;
using gflat.CompileExceptions;
using gflat.comptime;
using Xunit;

namespace gflat.Tests
{
    public class TypeCheckerTests
    {
        [Fact]
        public void TopLevelFunctionsAndStructsTypeCheckCleanly()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                }

                int Add(int a, int b)
                {
                    return a + b;
                }

                int main()
                {
                    Point p;
                    p.x = 10;
                    p.y = 20;
                    return Add(p.x, p.y);
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void MismatchedAssignmentThrowsTypeCheckException()
        {
            string code = """
                int main()
                {
                    int x = "not a number";
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void UnknownFunctionCallThrowsTypeCheckException()
        {
            string code = """
                int main()
                {
                    int x = UnknownFunction(1, 2);
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void IfConditionMustBeBool()
        {
            string code = """
                int main()
                {
                    if (42)
                    {
                        return 1;
                    }
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void DereferenceNonPointerThrowsTypeCheckException()
        {
            string code = """
                int main()
                {
                    int x = 10;
                    int y = *x;
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void NonExistentStructMemberThrowsTypeCheckException()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                }

                int main()
                {
                    Point p;
                    p.z = 99;
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void ArrowOperatorOnNonPointerThrows()
        {
            string code = """
                struct Point
                {
                    int x;
                }

                int main()
                {
                    Point p;
                    long a = p->address;
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot use '->' operator on non-pointer type", ex.Message);
        }

        [Fact]
        public void ArrowOperatorForStructMemberSuggestsDot()
        {
            string code = """
                struct Point
                {
                    int x;
                }

                int main()
                {
                    Point p;
                    Point* ptr = &p;
                    int val = ptr->x;
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("reserved for pointer metadata and lifecycle operations", ex.Message);
            Assert.Contains("Use '.' to access member 'x'", ex.Message);
        }

        [Fact]
        public void ArrowOperatorForStructMethodSuggestsDot()
        {
            string code = """
                struct Point
                {
                    int x;

                    void Reset()
                    {
                        this.x = 0;
                    }
                }

                int main()
                {
                    Point p;
                    Point* ptr = &p;
                    ptr->Reset();
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("reserved for pointer metadata and lifecycle operations", ex.Message);
            Assert.Contains("Use '.' to call method 'Reset'", ex.Message);
        }

        [Fact]
        public void ArrowPropertyAssignmentThrows()
        {
            string code = """
                int main()
                {
                    int x = 10;
                    int* ptr = &x;
                    ptr->address = 100L;
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot assign to read-only pointer property", ex.Message);
        }

        [Fact]
        public void ArrowPropertyWithParenthesesThrows()
        {
            string code = """
                int main()
                {
                    int x = 10;
                    int* ptr = &x;
                    if (ptr->is_null())
                    {
                        return 1;
                    }
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("is a property, not a method", ex.Message);
        }

        [Fact]
        public void FixedSizeArrayDeclarationAndAssignment()
        {
            string code = """
                int main()
                {
                    char[7] a = "string";
                    char* p = a;
                    char first = a[0];
                    a[0] = 'S';
                    return 0;
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void InferredArrayDeclarationTypeChecks()
        {
            string code = """
                int main()
                {
                    char[] a = "string";
                    char* s = a;
                    return 0;
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void ArrayBufferTooSmallThrowsTypeCheckException()
        {
            string code = """
                int main()
                {
                    char[3] a = "string";
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot assign", ex.Message);
        }

        [Fact]
        public void NonIntegerIndexThrowsTypeCheckException()
        {
            string code = """
                int main()
                {
                    int[5] arr;
                    int val = arr["invalid"];
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Array index must be an integer", ex.Message);
        }

        [Fact]
        public void IndexNonArrayThrowsTypeCheckException()
        {
            string code = """
                int main()
                {
                    int x = 10;
                    int y = x[0];
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot index non-array and non-pointer type", ex.Message);
        }

        [Fact]
        public void DeferStatementValidStatementPasses()
        {
            string code = """
                int main()
                {
                    int x = 1;
                    defer x = 2;
                    return x;
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
        }

        [Fact]
        public void DeferStatementReturnInsideDeferThrows()
        {
            string code = """
                int main()
                {
                    defer return 1;
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot return from within a defer statement", ex.Message);
        }

        [Fact]
        public void DeferStatementReturnInsideNestedBlockInDeferThrows()
        {
            string code = """
                int main()
                {
                    defer
                    {
                        return 1;
                    }
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot return from within a defer statement", ex.Message);
        }

        [Fact]
        public void FunctionPointerValidAssignmentPasses()
        {
            string code = """
                int Add(int a, int b)
                {
                    return a + b;
                }

                int main()
                {
                    int(int, int)* f = &Add;
                    return f(10, 20);
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
        }

        [Fact]
        public void FunctionUsedAsValueWithoutAmpersandThrowsHelpfulDiagnostic()
        {
            string code = """
                int Add(int a, int b)
                {
                    return a + b;
                }

                int main()
                {
                    int(int, int)* f = Add;
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot use function 'Add' as a value without '&'. Did you mean '&Add'?", ex.Message);
        }

        [Fact]
        public void FunctionPointerArgumentMismatchThrows()
        {
            string code = """
                int Add(int a, int b)
                {
                    return a + b;
                }

                int main()
                {
                    int(int, int)* f = &Add;
                    return f(10, "string");
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("cannot pass", ex.Message);
        }

        [Fact]
        public void FunctionPointerArgumentCountMismatchThrows()
        {
            string code = """
                int Add(int a, int b)
                {
                    return a + b;
                }

                int main()
                {
                    int(int, int)* f = &Add;
                    return f(10);
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("expects 2 arguments but got 1", ex.Message);
        }

        [Fact]
        public void FunctionPointerCannotCallFreeThrows()
        {
            string code = """
                int Add(int a, int b)
                {
                    return a + b;
                }

                int main()
                {
                    int(int, int)* f = &Add;
                    f->free();
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot call '->free' on a function pointer", ex.Message);
        }

        [Fact]
        public void TypeAliasDeclarationAndUsagePasses()
        {
            string code = """
                alias BinaryOp = int(int, int)*;

                int Add(int a, int b)
                {
                    return a + b;
                }

                int main()
                {
                    BinaryOp op = &Add;
                    return op(3, 4);
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
        }

        [Fact]
        public void TypeAliasAcrossNamespacePasses()
        {
            string code = """
                namespace Math
                {
                    public alias BinaryOp = int(int, int)*;

                    public int Multiply(int a, int b)
                    {
                        return a * b;
                    }
                }

                int main()
                {
                    Math::BinaryOp op = &Math::Multiply;
                    return op(5, 6);
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
        }

        [Fact]
        public void PointerAdditionAndSubtractionTypeCheckCleanly()
        {
            string code = """
                int main()
                {
                    int x = 10;
                    int* p = &x;
                    int* p2 = p + 1;
                    int* p3 = 2 + p;
                    int* p4 = p - 1;
                    long diff = p2 - p;
                    return 0;
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
        }

        [Fact]
        public void FunctionPointerArithmeticTypeChecksCleanly()
        {
            string code = """
                alias Fn = int(int, int)*;

                int Add(int a, int b)
                {
                    return a + b;
                }

                int main()
                {
                    Fn f = &Add;
                    Fn f2 = f + 1;
                    Fn f3 = f - 1;
                    long diff = f2 - f;
                    return 0;
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
        }

        [Fact]
        public void PointerArithmeticOnVoidPointerThrows()
        {
            string code = """
                extern void* malloc(long size);

                int main()
                {
                    void* p = malloc(10L);
                    void* p2 = p + 1;
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Pointer arithmetic is not allowed on 'void*'", ex.Message);
        }

        [Fact]
        public void PointerArithmeticOnManagedPointerThrows()
        {
            string code = """
                int main()
                {
                    int^? p = null;
                    int^? p2 = p + 1;
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Pointer arithmetic is not allowed on managed pointers ('^')", ex.Message);
        }

        [Fact]
        public void AddingTwoPointersThrows()
        {
            string code = """
                int main()
                {
                    int a = 1;
                    int b = 2;
                    int* p1 = &a;
                    int* p2 = &b;
                    int* p3 = p1 + p2;
                    return 0;
                }
                """;

            var ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot add two pointers together", ex.Message);
        }

        [Fact]
        public void SubtractingDifferentPointerTypesThrows()
        {
            string code = """
                int main()
                {
                    int a = 1;
                    long b = 2L;
                    int* p1 = &a;
                    long* p2 = &b;
                    long diff = p1 - p2;
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot subtract pointers of different types", ex.Message);
        }

        [Fact]
        public void EnumBasicTypeCheckCleanly()
        {
            string code = """
                enum Color
                {
                    Red,
                    Green,
                    Blue = 10,
                    Yellow
                }

                int main()
                {
                    Color c = Color::Green;
                    Color c2 = Color.Blue;
                    int x = Color::Yellow;
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
            TypeChecker.EnumInfo? info = checker.GetEnum("Color");
            Assert.NotNull(info);
            Assert.Equal(0L, info.Members["Red"]);
            Assert.Equal(1L, info.Members["Green"]);
            Assert.Equal(10L, info.Members["Blue"]);
            Assert.Equal(11L, info.Members["Yellow"]);
        }

        [Fact]
        public void EnumCustomBackingTypeCheckCleanly()
        {
            string code = """
                enum ByteCode : char
                {
                    Start = 1,
                    Stop = 2
                }

                enum BigVal : long
                {
                    Huge = 1000L
                }

                int main()
                {
                    ByteCode b = ByteCode::Start;
                    BigVal bg = BigVal::Huge;
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void EnumMemberNotFoundThrows()
        {
            string code = """
                enum Color
                {
                    Red,
                    Green
                }

                int main()
                {
                    Color c = Color::Purple;
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Enum 'Color' does not contain member 'Purple'", ex.Message);
        }

        [Fact]
        public void EnumInvalidUnderlyingTypeThrows()
        {
            string code = """
                enum FloatEnum : float
                {
                    A = 1
                }

                int main()
                {
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Enum underlying type must be an integral type", ex.Message);
        }

        [Fact]
        public void EnumDuplicateMemberThrows()
        {
            string code = """
                enum DuplicateEnum
                {
                    First = 1,
                    First = 2
                }

                int main()
                {
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("already contains a member named 'First'", ex.Message);
        }

        [Fact]
        public void UIntAndULongTypeCheckCleanly()
        {
            string code = """
                int main()
                {
                    uint u = 100u;
                    ulong ul = 200ul;
                    int i = u;
                    long l = ul;
                    uint shifted = u << 2;
                    uint rshifted = u >> 1;
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void IntToUIntImplicitConversionThrows()
        {
            string code = """
                int main()
                {
                    int i = 10;
                    uint u = i;
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void PointerSizedNIntNUIntTypeCheckCleanly()
        {
            string code = """
                int main()
                {
                    nint ni = 10;
                    nuint nui = 20u;
                    long l = ni;
                    ulong ul = nui;
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void CastExpressionTypeCheckCleanly()
        {
            string code = """
                int main()
                {
                    int i = 10;
                    uint u = (uint)i;
                    int* ptr = (int*)0;
                    nuint addr = (nuint)ptr;
                    void* vp = (void*)ptr;
                    int* ptr2 = (int*)vp;
                    int truncated = (int)100L;
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void InvalidCastThrows()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                }

                int main()
                {
                    Point p;
                    int x = (int)p;
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot cast 'Point' to 'int'", ex.Message);
        }

        [Fact]
        public void LambdaExpressionTypeCheckCleanly()
        {
            string code = """
                int main()
                {
                    int(int, int)* add = (int a, int b) => a + b;
                    int(int, int)* mul = static (int a, int b) => a * b;
                    int()* answer = () => 42;
                    int(int, int)* max = (int a, int b) =>
                    {
                        if (a > b) return a;
                        return b;
                    };
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void LambdaCapturingOuterLocalThrows()
        {
            string code = """
                int main()
                {
                    int x = 10;
                    int(int)* f = (int y) => x + y;
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Capturing outer local variable 'x' is not currently supported", ex.Message);
        }

        [Fact]
        public void StaticLambdaCapturingOuterLocalThrows()
        {
            string code = """
                int main()
                {
                    int x = 10;
                    int(int)* f = static (int y) => x + y;
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("A static lambda cannot reference outer local variable 'x'", ex.Message);
        }

        [Fact]
        public void LambdaCanAccessGlobalsAndFunctions()
        {
            string code = """
                int DoubleIt(int x)
                {
                    return x * 2;
                }

                int main()
                {
                    int(int)* f = (int x) => DoubleIt(x);
                    return f(21);
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void BinaryOperatorOverloadTypeChecksCleanly()
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
                    p1.x = 10;
                    p1.y = 20;
                    Point p2;
                    p2.x = 5;
                    p2.y = 5;
                    Point p3 = p1 + p2;
                    return p3.x + p3.y;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void UnaryOperatorOverloadTypeChecksCleanly()
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
                    Point p1;
                    p1.x = 10;
                    p1.y = 20;
                    Point p2 = -p1;
                    return p2.x;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void ComparisonOperatorOverloadTypeChecksCleanly()
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
                    if (p1 == p2)
                    {
                        return 42;
                    }
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void CompoundAssignmentWithOperatorOverloadTypeChecksCleanly()
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
                    step.x = 5;
                    step.y = 5;
                    p += step;
                    return p.x;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void OperatorWithoutContainingTypeParameterThrows()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;

                    public static int operator +(int a, int b)
                    {
                        return a + b;
                    }
                }

                int main()
                {
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("One of the parameters of a user-defined operator must be the containing type 'Point'", ex.Message);
        }

        [Fact]
        public void OperatorWithInvalidParameterCountThrows()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;

                    public static bool operator !(Point a, Point b)
                    {
                        return false;
                    }
                }

                int main()
                {
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Unary operator '!' must have exactly 1 parameter", ex.Message);
        }

        [Fact]
        public void SmallPrimitivesTypeCheckCleanly()
        {
            string code = """
                int main()
                {
                    byte b = 250;
                    sbyte sb = -100;
                    short s = 30000;
                    ushort u = 60000;

                    byte b2 = 5;
                    byte sumB = b + b2;

                    short s2 = 100;
                    short sumS = s + s2;

                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void SmallPrimitivesWideningImplicitConversions()
        {
            string code = """
                int main()
                {
                    byte b = 100;
                    short s1 = b;
                    ushort u1 = b;
                    int i1 = b;
                    uint ui1 = b;
                    long l1 = b;
                    ulong ul1 = b;

                    sbyte sb = -50;
                    short s2 = sb;
                    int i2 = sb;
                    long l2 = sb;

                    short s3 = 1000;
                    int i3 = s3;
                    long l3 = s3;

                    ushort u2 = 50000;
                    int i4 = u2;
                    uint ui2 = u2;
                    long l4 = u2;
                    ulong ul2 = u2;

                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void OutOfRangeLiteralAssignmentThrows()
        {
            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { byte b = 300; return 0; }"));
            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { sbyte sb = 200; return 0; }"));
            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { sbyte sb = -200; return 0; }"));
            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { short s = 70000; return 0; }"));
            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { short s = -50000; return 0; }"));
            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { ushort u = 70000; return 0; }"));
            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { ushort u = -1; return 0; }"));
        }

        [Fact]
        public void NarrowingWithoutCastThrows()
        {
            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { int x = 10; byte b = x; return 0; }"));
            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { int x = 10; sbyte sb = x; return 0; }"));
            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { int x = 10; short s = x; return 0; }"));
            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { int x = 10; ushort u = x; return 0; }"));
            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { short s = 10; byte b = s; return 0; }"));
        }

        [Fact]
        public void StructImplementsInterfaceCleanly()
        {
            string code = """
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
                    Rect r;
                    r.w = 5;
                    r.h = 4;
                    IShape* s = &r;
                    return s.Area();
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void StructMultipleInterfacesCleanly()
        {
            string code = """
                interface IShape
                {
                    int Area();
                }

                interface IDrawable
                {
                    void Draw();
                }

                struct Canvas : IShape, IDrawable
                {
                    int Area()
                    {
                        return 100;
                    }

                    void Draw()
                    {
                    }
                }

                int main()
                {
                    Canvas c;
                    IShape* s = &c;
                    IDrawable* d = &c;
                    return s.Area();
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void MissingInterfaceMethodThrows()
        {
            string code = """
                interface IShape
                {
                    int Area();
                }

                struct Rect : IShape
                {
                    int w;
                }

                int main()
                {
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void InterfaceMethodSignatureMismatchThrows()
        {
            string code = """
                interface IShape
                {
                    int Area();
                }

                struct Rect : IShape
                {
                    void Area()
                    {
                    }
                }

                int main()
                {
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void InterfaceByValueThrows()
        {
            string codeVar = """
                interface IShape
                {
                    int Area();
                }

                int main()
                {
                    IShape s;
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(codeVar));

            string codeParam = """
                interface IShape
                {
                    int Area();
                }

                void Foo(IShape s)
                {
                }

                int main()
                {
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(codeParam));
        }

        [Fact]
        public void NonImplementingStructCastThrows()
        {
            string code = """
                interface IShape
                {
                    int Area();
                }

                struct Other
                {
                    int x;
                }

                int main()
                {
                    Other o;
                    IShape* s = (IShape*)&o;
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void ArrowOnInterfacePointerThrows()
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
                    Rect r;
                    IShape* s = &r;
                    return s->Area();
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Use '.' to call method 'Area' on 'IShape'", ex.Message);
        }

        [Fact]
        public void DefaultExpressionsTypeCheckCleanly()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                }

                Point GetPoint()
                {
                    return default;
                }

                int main()
                {
                    Point p1 = default;
                    Point p2 = default(Point);
                    int x1 = default;
                    int x2 = default(int);
                    Point* ptr = default;
                    p1 = default;
                    return 0;
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void StructWithFieldInitializersAndConstructorsTypeChecks()
        {
            string code = """
                struct Point
                {
                    int x = 10;
                    int y = 20;

                    Point(int x, int y)
                    {
                        this.x = x;
                        this.y = y;
                    }
                }

                int main()
                {
                    Point p1 = new Point(1, 2);
                    Point* p2 = new* Point(3, 4);
                    return 0;
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void InvalidFieldInitializerThrows()
        {
            string code = """
                struct Point
                {
                    int x = "not an int";
                }

                int main()
                {
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void ConstructorNameMismatchThrows()
        {
            string code = """
                struct Point
                {
                    WrongName(int x)
                    {
                    }
                }

                int main()
                {
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Constructor name 'WrongName' does not match struct name 'Point'", ex.Message);
        }

        [Fact]
        public void ConstructorArgumentMismatchThrows()
        {
            string code = """
                struct Point
                {
                    int x;
                    Point(int x)
                    {
                        this.x = x;
                    }
                }

                int main()
                {
                    Point p = new Point("string instead of int");
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void ClassInheritanceTypeChecksCleanly()
        {
            string code = """
                class Animal
                {
                    public int age;
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
                    public int barkVolume;
                    public Dog(int a, int v) : base(a)
                    {
                        this.barkVolume = v;
                    }
                }

                int main()
                {
                    Dog* d = new* Dog(3, 10);
                    Animal* a = d;
                    return a.GetAge();
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void ClassVirtualAndOverrideTypeChecksCleanly()
        {
            string code = """
                class Base
                {
                    public virtual int Value() { return 1; }
                }

                class Derived : Base
                {
                    public override int Value() { return 2; }
                }

                int main()
                {
                    Derived* d = new* Derived();
                    Base* b = d;
                    return b.Value();
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void ClassCircularInheritanceThrows()
        {
            string code = """
                class A : B { }
                class B : A { }
                int main() { return 0; }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Circular inheritance", ex.Message);
        }

        [Fact]
        public void ClassAbstractMethodInNonAbstractClassThrows()
        {
            string code = """
                class NormalClass
                {
                    public abstract int Foo();
                }
                int main() { return 0; }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Abstract method 'Foo' can only be declared in an abstract class", ex.Message);
        }

        [Fact]
        public void ClassUnimplementedAbstractMethodThrows()
        {
            string code = """
                abstract class Base
                {
                    public abstract int Foo();
                }

                class Derived : Base
                {
                }

                int main() { return 0; }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("must implement abstract method 'Base.Foo'", ex.Message);
        }

        [Fact]
        public void ClassAbstractInstantiationThrows()
        {
            string code = """
                abstract class Base
                {
                    public abstract int Foo();
                }
                int main()
                {
                    Base* b = new* Base();
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot instantiate abstract class 'Base'", ex.Message);
        }

        [Fact]
        public void ClassPrivateMemberAccessThrows()
        {
            string code = """
                class Secret
                {
                    private int secretVal = 42;
                }

                int main()
                {
                    Secret* s = new* Secret();
                    return s.secretVal;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot access private field 'secretVal'", ex.Message);
        }

        [Fact]
        public void ClassProtectedMemberAccessAllowedInSubclass()
        {
            string code = """
                class Base
                {
                    protected int protVal = 10;
                }

                class Derived : Base
                {
                    public int GetVal()
                    {
                        return this.protVal;
                    }
                }

                int main()
                {
                    Derived* d = new* Derived();
                    return d.GetVal();
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void ClassProtectedMemberAccessOutsideThrows()
        {
            string code = """
                class Base
                {
                    protected int protVal = 10;
                }

                int main()
                {
                    Base* b = new* Base();
                    return b.protVal;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot access protected field 'protVal'", ex.Message);
        }

        [Fact]
        public void ClassBaseConstructorMissingThrows()
        {
            string code = """
                class Base
                {
                    public Base(int x) { }
                }

                class Derived : Base
                {
                    public Derived(int y) { }
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("must explicitly call a base constructor", ex.Message);
        }

        [Fact]
        public void ClassBaseConstructorTypeMismatchThrows()
        {
            string code = """
                class Base
                {
                    public Base(int x) { }
                }

                class Derived : Base
                {
                    public Derived() : base("hello") { }
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("No matching base constructor found", ex.Message);
        }

        [Fact]
        public void ClassDestructorMismatchedNameThrows()
        {
            string code = """
                class Animal
                {
                    public ~Dog() { }
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("does not match class name", ex.Message);
        }

        [Fact]
        public void ClassDestructorDuplicateThrows()
        {
            string code = """
                class Animal
                {
                    public ~Animal() { }
                    public ~Animal() { }
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("already defines a destructor", ex.Message);
        }

        [Fact]
        public void ClassImplementsInterfaceTypeChecksCleanly()
        {
            string code = """
                interface IGreeter
                {
                    int Greet();
                }

                class Person : IGreeter
                {
                    public int Greet() { return 42; }
                }

                int main()
                {
                    Person* p = new* Person();
                    IGreeter* g = p;
                    return g.Greet();
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void ReadonlyPointerAssignmentRejection()
        {
            string code = """
                int main()
                {
                    int x = 42;
                    readonly int* p = &x;
                    *p = 10;
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot assign to dereference of readonly pointer", ex.Message);
        }

        [Fact]
        public void ReadonlyPointerIndexAssignmentRejection()
        {
            string code = """
                int main()
                {
                    int x = 42;
                    readonly int* p = &x;
                    p[0] = 10;
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot assign to dereference of readonly pointer", ex.Message);
        }

        [Fact]
        public void ReadonlyPointerIncrementRejection()
        {
            string code = """
                int main()
                {
                    int x = 42;
                    readonly int* p = &x;
                    (*p)++;
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot assign to dereference of readonly pointer", ex.Message);
        }

        [Fact]
        public void ReadonlyPointerPassingToWritablePointerRejection()
        {
            string code = """
                void Modify(int* ptr)
                {
                    *ptr = 100;
                }

                int main()
                {
                    int x = 42;
                    readonly int* p = &x;
                    Modify(p);
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("cannot pass 'readonly int*' as 'int*'", ex.Message);
        }

        [Fact]
        public void WritablePointerToReadonlyPointerConversionAllowed()
        {
            string code = """
                int ReadVal(readonly int* ptr)
                {
                    return *ptr;
                }

                int main()
                {
                    int x = 42;
                    int* p = &x;
                    readonly int* ro = p;
                    return ReadVal(p);
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void ReadonlyFieldModificationOutsideConstructorRejection()
        {
            string code = """
                struct User
                {
                    readonly int id;
                    int age;

                    public User(int i, int a)
                    {
                        this.id = i;
                        this.age = a;
                    }
                }

                int main()
                {
                    User u = new User(1, 20);
                    u.id = 2;
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot assign to readonly field 'id' outside constructor", ex.Message);
        }

        [Fact]
        public void ReadonlyFieldAssignmentInsideConstructorAllowed()
        {
            string code = """
                class User
                {
                    readonly int id;

                    public User(int i)
                    {
                        this.id = i;
                    }

                    public int GetId()
                    {
                        return this.id;
                    }
                }

                int main()
                {
                    User* u = new* User(99);
                    return u.GetId();
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void ReadonlyMethodMutatingThisRejection()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;

                    public readonly void Reset()
                    {
                        this.x = 0;
                    }
                }

                int main()
                {
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot assign to field 'x' on readonly instance", ex.Message);
        }

        [Fact]
        public void NonReadonlyMethodOnReadonlyReceiverRejection()
        {
            string code = """
                struct Counter
                {
                    int count;

                    public void Increment()
                    {
                        this.count = this.count + 1;
                    }

                    public readonly int GetCount()
                    {
                        return this.count;
                    }
                }

                int main()
                {
                    Counter c;
                    readonly Counter* ptr = &c;
                    ptr.Increment();
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot call non-readonly method 'Increment' on readonly instance", ex.Message);
        }

        [Fact]
        public void ReadonlyMethodOnReadonlyReceiverAllowed()
        {
            string code = """
                struct Counter
                {
                    int count;

                    public readonly int GetCount()
                    {
                        return this.count;
                    }
                }

                int main()
                {
                    Counter c;
                    readonly Counter* ptr = &c;
                    return ptr.GetCount();
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void ExplicitCastAwayReadonlyAllowed()
        {
            string code = """
                int main()
                {
                    int x = 42;
                    readonly int* ro = &x;
                    int* w = (int*)ro;
                    *w = 100;
                    return *w;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void ConstVariable_RequiresInitializer()
        {
            string code = """
                int main()
                {
                    const int x;
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void ConstVariable_RequiresConstantInitializer()
        {
            string code = """
                int main()
                {
                    int y = 5;
                    const int x = y;
                    return x;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void ConstVariable_CannotBeReassigned()
        {
            string code = """
                int main()
                {
                    const int x = 10;
                    x = 20;
                    return x;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void ConstField_CannotBeReassigned()
        {
            string code = """
                struct Config
                {
                    const int Max = 100;
                }

                int main()
                {
                    Config c;
                    c.Max = 200;
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void ConstParameter_RequiresConstantArgument()
        {
            string code = """
                void SetBuffer(const int size, int* buf)
                {
                }

                int main()
                {
                    int runtimeSize = 10;
                    int x = 0;
                    SetBuffer(runtimeSize, &x);
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void ConstParameter_WithConstantArgumentAllowed()
        {
            string code = """
                void SetBuffer(const int size, int* buf)
                {
                }

                int main()
                {
                    const int C = 10;
                    int x = 0;
                    SetBuffer(C, &x);
                    SetBuffer(20, &x);
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void ConstMethod_EvaluatesAtCompileTime()
        {
            string code = """
                const int Add(int a, int b)
                {
                    return a + b;
                }

                const int Res = Add(10, 20);

                int main()
                {
                    return Res;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
            Assert.True(checker.TryGetConstValueByName("Res", out ConstValue? val));
            Assert.IsType<ConstValue.Integer>(val);
            Assert.Equal(30, ((ConstValue.Integer)val!).Value);
        }

        [Fact]
        public void ConstMethod_NonConstCalleeCannotBeEvaluated()
        {
            string code = """
                int RuntimeAdd(int a, int b)
                {
                    return a + b;
                }

                const int Res = RuntimeAdd(1, 2);

                int main()
                {
                    return Res;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void ConstMethod_RecursionAndControlFlow()
        {
            string code = """
                const int Factorial(int n)
                {
                    if (n <= 1)
                    {
                        return 1;
                    }
                    return n * Factorial(n - 1);
                }

                const int F5 = Factorial(5);

                int main()
                {
                    return F5;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
            Assert.True(checker.TryGetConstValueByName("F5", out ConstValue? val));
            Assert.IsType<ConstValue.Integer>(val);
            Assert.Equal(120, ((ConstValue.Integer)val!).Value);
        }

        [Fact]
        public void ConstMethod_StepLimitEnforced()
        {
            string code = """
                const int Infinite()
                {
                    while (true)
                    {
                    }
                    return 0;
                }

                const int X = Infinite();

                int main()
                {
                    return X;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void ConstString_Operations()
        {
            string code = """
                const char[] Greeting = "hello";
                const char Second = Greeting[1];

                int main()
                {
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);

            Assert.True(checker.TryGetConstValueByName("Second", out ConstValue? charVal));
            Assert.Equal('e', ((ConstValue.Char)charVal!).Value);
        }

        [Fact]
        public void ConstStruct_ValueConstructionAndFieldAccess()
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

                const Point P = new Point(10, 20);
                const int Px = P.x;
                const int Py = P.y;

                int main()
                {
                    return Px + Py;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);

            Assert.True(checker.TryGetConstValueByName("Px", out ConstValue? xVal));
            Assert.Equal(10, ((ConstValue.Integer)xVal!).Value);

            Assert.True(checker.TryGetConstValueByName("Py", out ConstValue? yVal));
            Assert.Equal(20, ((ConstValue.Integer)yVal!).Value);
        }

        [Fact]
        public void ConstArray_Sizing()
        {
            string code = """
                const int Size = 16;

                int main()
                {
                    int[Size] buffer;
                    return buffer[0];
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void ConstArray_SizingWithExpression()
        {
            string code = """
                const int Base = 8;

                int main()
                {
                    int[Base * 2] buffer;
                    return buffer[0];
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void Sizeof_Primitives()
        {
            string code = """
                const int S_Int = sizeof(int);
                const int S_Char = sizeof(char);
                const int S_Short = sizeof(short);
                const int S_Long = sizeof(long);
                const int S_Bool = sizeof(bool);
                const int S_Float = sizeof(float);
                const int S_Ptr = sizeof(void*);

                int main()
                {
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);

            Assert.True(checker.TryGetConstValueByName("S_Int", out ConstValue? vInt));
            Assert.Equal(4, ((ConstValue.Integer)vInt!).Value);

            Assert.True(checker.TryGetConstValueByName("S_Char", out ConstValue? vChar));
            Assert.Equal(1, ((ConstValue.Integer)vChar!).Value);

            Assert.True(checker.TryGetConstValueByName("S_Short", out ConstValue? vShort));
            Assert.Equal(2, ((ConstValue.Integer)vShort!).Value);

            Assert.True(checker.TryGetConstValueByName("S_Long", out ConstValue? vLong));
            Assert.Equal(8, ((ConstValue.Integer)vLong!).Value);

            Assert.True(checker.TryGetConstValueByName("S_Bool", out ConstValue? vBool));
            Assert.Equal(1, ((ConstValue.Integer)vBool!).Value);

            Assert.True(checker.TryGetConstValueByName("S_Float", out ConstValue? vFloat));
            Assert.Equal(4, ((ConstValue.Integer)vFloat!).Value);

            Assert.True(checker.TryGetConstValueByName("S_Ptr", out ConstValue? vPtr));
            Assert.Equal(8, ((ConstValue.Integer)vPtr!).Value);
        }

        [Fact]
        public void Sizeof_StructsAndPadding()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                }

                struct Mixed
                {
                    char c;
                    int i;
                }

                struct Quad
                {
                    char c1;
                    short s;
                    int i;
                    long l;
                }

                const int S_Point = sizeof(Point);
                const int S_Mixed = sizeof(Mixed);
                const int S_Quad = sizeof(Quad);

                int main()
                {
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);

            Assert.True(checker.TryGetConstValueByName("S_Point", out ConstValue? vPoint));
            Assert.Equal(8, ((ConstValue.Integer)vPoint!).Value);

            Assert.True(checker.TryGetConstValueByName("S_Mixed", out ConstValue? vMixed));
            Assert.Equal(8, ((ConstValue.Integer)vMixed!).Value);

            Assert.True(checker.TryGetConstValueByName("S_Quad", out ConstValue? vQuad));
            Assert.Equal(16, ((ConstValue.Integer)vQuad!).Value);
        }

        [Fact]
        public void Sizeof_FixedArray()
        {
            string code = """
                const int S_ArrInt = sizeof(int[10]);
                const int S_ArrChar = sizeof(char[16]);

                int main()
                {
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);

            Assert.True(checker.TryGetConstValueByName("S_ArrInt", out ConstValue? vArrInt));
            Assert.Equal(40, ((ConstValue.Integer)vArrInt!).Value);

            Assert.True(checker.TryGetConstValueByName("S_ArrChar", out ConstValue? vArrChar));
            Assert.Equal(16, ((ConstValue.Integer)vArrChar!).Value);
        }

        [Fact]
        public void Sizeof_InConstVariableAndArraySizing()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                }

                const int S = sizeof(Point);

                int main()
                {
                    int[sizeof(Point)] buffer;
                    return buffer[0];
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);

            Assert.True(checker.TryGetConstValueByName("S", out ConstValue? vS));
            Assert.Equal(8, ((ConstValue.Integer)vS!).Value);
        }

        [Fact]
        public void Nameof_IdentifierAndType()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                }

                const char[] N_Point = nameof(Point);
                const char[] N_Int = nameof(int);

                int main()
                {
                    int myVariable = 42;
                    const char[] N_Var = nameof(myVariable);
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);

            Assert.True(checker.TryGetConstValueByName("N_Point", out ConstValue? vPoint));
            Assert.Equal("Point", ((ConstValue.String)vPoint!).Value);

            Assert.True(checker.TryGetConstValueByName("N_Int", out ConstValue? vInt));
            Assert.Equal("int", ((ConstValue.String)vInt!).Value);
        }

        [Fact]
        public void Nameof_MemberAccess()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                }

                int main()
                {
                    Point pt;
                    const char[] N_Field = nameof(pt.x);
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void Nameof_NonExistentSymbol_Throws()
        {
            string code = """
                int main()
                {
                    const char[] N = nameof(nonExistentSymbol);
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void CStringPrefix_TypeChecks()
        {
            string code = """
                int main()
                {
                    readonly char* s = c"hello";
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void CustomStringPrefix_StructAttribute_TypeChecks()
        {
            string code = """
                [string_prefix("sql")]
                struct SqlQuery
                {
                    readonly char* text;
                    const SqlQuery(readonly char* raw)
                    {
                        this.text = raw;
                    }
                }

                int main()
                {
                    SqlQuery q = sql"SELECT * FROM users";
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void CustomStringPrefix_ConstructorAttribute_TypeChecks()
        {
            string code = """
                struct SqlQuery
                {
                    readonly char* text;
                    [string_prefix("sql")]
                    const SqlQuery(readonly char* raw)
                    {
                        this.text = raw;
                    }
                }

                int main()
                {
                    SqlQuery q = sql"SELECT * FROM users";
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void CustomStringPrefix_FreeFunction_TypeChecks()
        {
            string code = """
                [string_prefix("hash")]
                const uint Hash(readonly char* str)
                {
                    return 123u;
                }

                int main()
                {
                    uint h = hash"test";
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void CustomStringPrefix_QualifiedScope_TypeChecks()
        {
            string code = """
                namespace Postgres
                {
                    [string_prefix("sql")]
                    struct SqlQuery
                    {
                        readonly char* text;
                        const SqlQuery(readonly char* raw) { this.text = raw; }
                    }
                }

                namespace Sqlite
                {
                    [string_prefix("sql")]
                    struct SqlQuery
                    {
                        readonly char* text;
                        const SqlQuery(readonly char* raw) { this.text = raw; }
                    }
                }

                int main()
                {
                    Postgres::SqlQuery q1 = Postgres::sql"SELECT 1";
                    Sqlite::SqlQuery q2 = Sqlite::sql"SELECT 2";
                    return 0;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void CustomStringPrefix_Ambiguity_Throws()
        {
            string code = """
                [string_prefix("test")]
                struct S1
                {
                    const S1(readonly char* s) {}
                }

                [string_prefix("test")]
                struct S2
                {
                    const S2(readonly char* s) {}
                }

                int main()
                {
                    test"hello";
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Ambiguous string prefix 'test'", ex.Message);
        }

        [Fact]
        public void CustomStringPrefix_UnknownPrefix_Throws()
        {
            string code = """
                int main()
                {
                    unknown"hello";
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Unknown string prefix 'unknown'", ex.Message);
        }

        [Fact]
        public void GenericStruct_TypeChecksCleanly()
        {
            string code = """
                struct Pair<T, U>
                {
                    T first;
                    U second;
                }

                int main()
                {
                    Pair<int, float> p;
                    p.first = 42;
                    p.second = 3.14f;
                    return p.first;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void GenericFunction_Inference_TypeChecksCleanly()
        {
            string code = """
                T Max<T>(T a, T b)
                {
                    if (a > b) return a;
                    return b;
                }

                int main()
                {
                    int m = Max(10, 20);
                    return m;
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void GenericConstraint_Interface_Satisfied()
        {
            string code = """
                interface Describable
                {
                    int Describe();
                }

                struct Widget : Describable
                {
                    int id;
                    int Describe()
                    {
                        return this.id;
                    }
                }

                int Process<T : Describable>(T w)
                {
                    return w.Describe();
                }

                int main()
                {
                    Widget w;
                    w.id = 100;
                    return Process(w);
                }
                """;

            (CompilationUnit ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void GenericConstraint_Interface_Violated_Throws()
        {
            string code = """
                interface Describable
                {
                    int Describe();
                }

                struct Plain
                {
                    int id;
                }

                int Process<T : Describable>(T w)
                {
                    return 0;
                }

                int main()
                {
                    Plain p;
                    return Process(p);
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("does not satisfy constraint 'Describable'", ex.Message);
        }

        [Fact]
        public void GenericConstraint_BaseClass_Violated_Throws()
        {
            string code = """
                class BaseObj
                {
                    int tag;
                }

                class OtherObj
                {
                    int tag;
                }

                int Inspect<T : BaseObj>(T item)
                {
                    return 0;
                }

                int main()
                {
                    OtherObj o = new OtherObj();
                    return Inspect(o);
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("does not satisfy constraint 'BaseObj'", ex.Message);
        }

        [Fact]
        public void ConstPointer_Struct_TypeChecksCleanly()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                    Point(int a, int b) { x = a; y = b; }
                }

                const Point* p = new* Point(1, 2);

                int main()
                {
                    return p.x + p.y;
                }
                """;

            CompilerTestHelper.Check(code);
        }

        [Fact]
        public void ConstPointer_Mutation_Throws()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                    Point(int a, int b) { x = a; y = b; }
                }

                const Point* p = new* Point(1, 2);

                int main()
                {
                    p.x = 10;
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("readonly", ex.Message);
        }

        [Fact]
        public void ConstPointer_Free_Throws()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                    Point(int a, int b) { x = a; y = b; }
                }

                const Point* p = new* Point(1, 2);

                int main()
                {
                    p->free();
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("readonly", ex.Message);
        }

        [Fact]
        public void ConstPointer_AddressOfConst_TypeChecksCleanly()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                    Point(int a, int b) { x = a; y = b; }
                }

                const Point Origin = new Point(0, 0);
                const Point* pOrigin = &Origin;

                int main()
                {
                    return pOrigin.x;
                }
                """;

            CompilerTestHelper.Check(code);
        }

        [Fact]
        public void ConstPointer_AddressOfConst_AssignToMutable_Throws()
        {
            string code = """
                struct Point
                {
                    int x;
                    int y;
                    Point(int a, int b) { x = a; y = b; }
                }

                const Point Origin = new Point(0, 0);

                int main()
                {
                    Point* p = &Origin;
                    return 0;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot assign", ex.Message);
        }

        [Fact]
        public void Parser_ThrowAndTryCatch_ParsesSuccessfully()
        {
            string code = """
                int foo()
                {
                    try
                    {
                        throw new* Exception(c"test", 1);
                    }
                    catch (Exception* ex)
                    {
                        return ex.GetCode();
                    }
                    catch (readonly Exception* e)
                    {
                        return 2;
                    }
                    catch (Exception*)
                    {
                        return 3;
                    }
                    catch
                    {
                        return -1;
                    }
                    return 0;
                }
                """;

            List<Token> tokens = Lexer.Tokenize(code);
            CompilationUnit ast = Parser.Parse(tokens);
            Assert.NotNull(ast);
            Assert.Contains(ast.Members, m => m is ClassDeclaration c && c.Name == "Exception");

            MethodDeclaration? foo = ast.Members.OfType<MethodDeclaration>().FirstOrDefault(m => m.Name == "foo");
            Assert.NotNull(foo);
            Assert.NotNull(foo.Body);
            TryStatement? tryStmt = foo.Body.Statements.OfType<TryStatement>().FirstOrDefault();
            Assert.NotNull(tryStmt);
            Assert.Equal(4, tryStmt.CatchClauses.Count);
            Assert.True(tryStmt.TryBlock.Statements[0] is ThrowStatement);
            Assert.Equal("ex", tryStmt.CatchClauses[0].VariableName);
            Assert.Equal("e", tryStmt.CatchClauses[1].VariableName);
            Assert.Null(tryStmt.CatchClauses[2].VariableName);
            Assert.Null(tryStmt.CatchClauses[3].ExceptionType);
        }

        [Fact]
        public void TypeChecker_ThrowException_ValidHierarchy_Passes()
        {
            string code = """
                class CustomException : Exception
                {
                    public CustomException(readonly char* msg) : base(msg, 100)
                    {
                    }
                }

                void ThrowCustom()
                {
                    throw new* CustomException(c"custom error");
                }

                int main()
                {
                    try
                    {
                        ThrowCustom();
                    }
                    catch (CustomException* ce)
                    {
                        return ce.GetCode();
                    }
                    catch (Exception* ex)
                    {
                        return ex.GetCode();
                    }
                    return 0;
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            MethodDeclaration? throwCustom = ast.Members.OfType<MethodDeclaration>().FirstOrDefault(m => m.Name == "ThrowCustom");
            Assert.NotNull(throwCustom);
            Assert.True(checker.CanFunctionThrow(throwCustom));

            MethodDeclaration? main = ast.Members.OfType<MethodDeclaration>().FirstOrDefault(m => m.Name == "main");
            Assert.NotNull(main);
            Assert.False(checker.CanFunctionThrow(main));
        }

        [Fact]
        public void TypeChecker_ThrowNonException_Throws()
        {
            string code = """
                void Bad()
                {
                    throw 42;
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Cannot throw non-exception type", ex.Message);
        }

        [Fact]
        public void TypeChecker_CatchNonException_Throws()
        {
            string code = """
                void Bad()
                {
                    try
                    {
                    }
                    catch (int x)
                    {
                    }
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("must be 'Exception' or a subclass", ex.Message);
        }

        [Fact]
        public void TypeChecker_UnreachableCatchClause_Throws()
        {
            string code = """
                class CustomException : Exception
                {
                }

                void Bad()
                {
                    try
                    {
                    }
                    catch (Exception* e1)
                    {
                    }
                    catch (CustomException* e2)
                    {
                    }
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("unreachable", ex.Message);
        }

        [Fact]
        public void TypeChecker_CatchAllNotLast_Throws()
        {
            string code = """
                void Bad()
                {
                    try
                    {
                    }
                    catch
                    {
                    }
                    catch (Exception* e)
                    {
                    }
                }
                """;

            TypeCheckException ex = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
            Assert.Contains("must be the last catch clause", ex.Message);
        }

        [Fact]
        public void TypeChecker_InterproceduralCanThrow_Propagates()
        {
            string code = """
                void Level1()
                {
                    throw new* Exception(c"error");
                }

                void Level2()
                {
                    Level1();
                }

                void Safe()
                {
                    try
                    {
                        Level2();
                    }
                    catch
                    {
                    }
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            MethodDeclaration? l1 = ast.Members.OfType<MethodDeclaration>().FirstOrDefault(m => m.Name == "Level1");
            MethodDeclaration? l2 = ast.Members.OfType<MethodDeclaration>().FirstOrDefault(m => m.Name == "Level2");
            MethodDeclaration? safe = ast.Members.OfType<MethodDeclaration>().FirstOrDefault(m => m.Name == "Safe");

            Assert.NotNull(l1);
            Assert.NotNull(l2);
            Assert.NotNull(safe);

            Assert.True(checker.CanFunctionThrow(l1));
            Assert.True(checker.CanFunctionThrow(l2));
            Assert.False(checker.CanFunctionThrow(safe));
        }

        [Fact]
        public void Parser_NestedClassAndStruct_ParsesSuccessfully()
        {
            string code = """
                class Outer
                {
                    public int x;

                    public class NestedClass
                    {
                        public int y;
                    }

                    public struct NestedStruct
                    {
                        public int z;
                    }
                }

                int main()
                {
                    Outer.NestedClass* nc = null;
                    Outer.NestedStruct ns;
                    return 0;
                }
                """;

            var tokens = Lexer.Tokenize(code);
            CompilationUnit ast = Parser.Parse(tokens);
            ClassDeclaration? outer = ast.Members.OfType<ClassDeclaration>().FirstOrDefault(c => c.Name == "Outer");
            Assert.NotNull(outer);
            Assert.Contains(outer.Members, m => m is ClassDeclaration { Name: "NestedClass" });
            Assert.Contains(outer.Members, m => m is StructDeclaration { Name: "NestedStruct" });
        }

        [Fact]
        public void TypeCheck_NestedClass_InstantiatesAndCallsMethod()
        {
            string code = """
                class Outer
                {
                    public class Inner
                    {
                        public int Value;
                        public Inner(int v)
                        {
                            this.Value = v;
                        }
                        public int GetValue()
                        {
                            return this.Value;
                        }
                    }

                    public Inner* CreateInner(int v)
                    {
                        return new* Inner(v);
                    }
                }

                int main()
                {
                    Outer* o = new* Outer();
                    Outer.Inner* inner1 = o.CreateInner(42);
                    Outer.Inner* inner2 = new* Outer.Inner(100);
                    return inner1.GetValue() + inner2.GetValue();
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
            Assert.NotNull(checker.GetClass("Outer.Inner"));
        }

        [Fact]
        public void TypeCheck_NestedStruct_InstantiatesAndAccessesFields()
        {
            string code = """
                struct Container
                {
                    public struct Item
                    {
                        public int Id;
                        public Item(int id)
                        {
                            this.Id = id;
                        }
                    }
                }

                int main()
                {
                    Container.Item item = new Container.Item(5);
                    return item.Id;
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
            Assert.NotNull(checker.GetStruct("Container.Item"));
        }

        [Fact]
        public void TypeCheck_NestedClass_CanAccessOuterPrivateMember()
        {
            string code = """
                class Outer
                {
                    private int secret;

                    public class Inner
                    {
                        public int ReadSecret(Outer* o)
                        {
                            return o.secret;
                        }
                    }
                }

                int main()
                {
                    return 0;
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void TypeCheck_Foreach_Array_Success()
        {
            string code = """
                int main()
                {
                    int[3] arr;
                    int sum = 0;
                    foreach (int x in arr)
                    {
                        sum = sum + x;
                    }
                    return sum;
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void TypeCheck_Foreach_Array_AssignableType_Success()
        {
            string code = """
                int main()
                {
                    int[3] arr;
                    long sum = 0;
                    foreach (long x in arr)
                    {
                        sum = sum + x;
                    }
                    return 0;
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void TypeCheck_Foreach_Array_MismatchedType_Fails()
        {
            string code = """
                int main()
                {
                    int[3] arr;
                    foreach (string x in arr)
                    {
                    }
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void TypeCheck_Foreach_RequiresExplicitType_Fails()
        {
            string code = """
                int main()
                {
                    int[3] arr;
                    foreach (x in arr)
                    {
                    }
                    return 0;
                }
                """;

            Exception ex = Assert.ThrowsAny<Exception>(() => CompilerTestHelper.Check(code));
            Assert.Contains("Foreach loop requires an explicit type", ex.Message);
        }

        [Fact]
        public void TypeCheck_Foreach_NonIterableType_Fails()
        {
            string code = """
                int main()
                {
                    int num = 42;
                    foreach (int x in num)
                    {
                    }
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void TypeCheck_Foreach_MissingGetEnumerator_Fails()
        {
            string code = """
                struct Foo
                {
                    int x;
                }

                int main()
                {
                    Foo f;
                    foreach (int x in f)
                    {
                    }
                    return 0;
                }
                """;

            Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void TypeCheck_Foreach_CustomCollection_Success()
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
                    foreach (int val in r)
                    {
                        sum = sum + val;
                    }
                    return sum;
                }
                """;

            var (ast, checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
        }

        [Fact]
        public void TypeCheck_GenericNestedType_DotAccess()
        {
            string code = """
                struct Container<T>
                {
                    public struct Node
                    {
                        public T value;
                    }
                }

                int main()
                {
                    Container<int>.Node node1;
                    node1.value = 10;
                    Container<double>.Node node2;
                    node2.value = 3.14;
                    return node1.value;
                }
                """;

            (AstNode ast, TypeChecker checker) = CompilerTestHelper.Check(code);
            Assert.NotNull(ast);
            Assert.NotNull(checker);
            Assert.NotNull(checker.GetStruct("Container$int.Node"));
            Assert.NotNull(checker.GetStruct("Container$double.Node"));
        }
    }
}

