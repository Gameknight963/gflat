using gflat.ast;
using gflat.CompileExceptions;
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
                    public Dog(int a, int v)
                    {
                        this.age = a;
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
    }
}

