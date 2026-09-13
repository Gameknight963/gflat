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
    }
}
