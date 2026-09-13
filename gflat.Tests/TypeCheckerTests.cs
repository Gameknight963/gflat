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
    }
}
