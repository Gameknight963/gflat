using System.Linq;
using gflat.diagnostics;
using Xunit;

namespace gflat.Tests
{
    public class DiagnosticTests
    {
        [Fact]
        public void Warning_UnreachableCode_ReportedAfterReturn()
        {
            string code = """
                int main()
                {
                    return 42;
                    int dead = 10;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.Check(code);

            Assert.True(checker.Diagnostics.HasWarnings);
            Assert.False(checker.Diagnostics.HasErrors);
            Assert.Contains(checker.Diagnostics.Items, d => d.Descriptor.Id == DiagnosticRules.GF2001_UnreachableCode.Id);
        }

        [Fact]
        public void Warning_UnreachableCode_ReportedAfterBreak()
        {
            string code = """
                int main()
                {
                    while (true)
                    {
                        break;
                        int dead = 1;
                    }
                    return 0;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.Check(code);

            Assert.True(checker.Diagnostics.HasWarnings);
            Assert.Contains(checker.Diagnostics.Items, d => d.Descriptor.Id == DiagnosticRules.GF2001_UnreachableCode.Id);
        }

        [Fact]
        public void Warning_UnreachableCode_ReportedAfterContinue()
        {
            string code = """
                int main()
                {
                    while (true)
                    {
                        continue;
                        int dead = 1;
                    }
                    return 0;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.Check(code);

            Assert.True(checker.Diagnostics.HasWarnings);
            Assert.Contains(checker.Diagnostics.Items, d => d.Descriptor.Id == DiagnosticRules.GF2001_UnreachableCode.Id);
        }

        [Fact]
        public void Warning_UnreachableCode_ReportedInVoidFunction()
        {
            string code = """
                void helper()
                {
                    return;
                    int dead = 5;
                }

                int main()
                {
                    helper();
                    return 0;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.Check(code);

            Assert.True(checker.Diagnostics.HasWarnings);
            Assert.Contains(checker.Diagnostics.Items, d => d.Descriptor.Id == DiagnosticRules.GF2001_UnreachableCode.Id);
        }

        [Fact]
        public void Warning_UnreachableCode_ReportedInConstantFalseWhile()
        {
            string code = """
                int main()
                {
                    while (false)
                    {
                        int dead = 1;
                    }
                    return 0;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.Check(code);

            Assert.True(checker.Diagnostics.HasWarnings);
            Assert.Contains(checker.Diagnostics.Items, d => d.Descriptor.Id == DiagnosticRules.GF2001_UnreachableCode.Id);
        }

        [Fact]
        public void Warning_UnreachableCode_ReportedInConstantFalseIfBranch()
        {
            string code = """
                int main()
                {
                    if (false)
                    {
                        int dead = 1;
                    }
                    else
                    {
                        int alive = 2;
                    }
                    return 0;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.Check(code);

            Assert.True(checker.Diagnostics.HasWarnings);
            Assert.Contains(checker.Diagnostics.Items, d => d.Descriptor.Id == DiagnosticRules.GF2001_UnreachableCode.Id);
        }

        [Fact]
        public void Warning_UnreachableCode_ReportedInConstantTrueElseBranch()
        {
            string code = """
                int main()
                {
                    if (true)
                    {
                        int alive = 1;
                    }
                    else
                    {
                        int dead = 2;
                    }
                    return 0;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.Check(code);

            Assert.True(checker.Diagnostics.HasWarnings);
            Assert.Contains(checker.Diagnostics.Items, d => d.Descriptor.Id == DiagnosticRules.GF2001_UnreachableCode.Id);
        }

        [Fact]
        public void Warning_UnreachableCode_InsideIfWithoutElse()
        {
            string code = """
                int main()
                {
                    int x = 10;
                    if (x > 5)
                    {
                        return 1;
                        int dead = 2;
                    }
                    return 0;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.Check(code);

            Assert.True(checker.Diagnostics.HasWarnings);
            Assert.Contains(checker.Diagnostics.Items, d => d.Descriptor.Id == DiagnosticRules.GF2001_UnreachableCode.Id);
        }

        [Fact]
        public void Warning_UnreachableCode_StillExecutesSuccessfully()
        {
            string code = """
                int main()
                {
                    return 42;
                    int dead = 99;
                }
                """;
            ExecutionResult result = CompilerTestHelper.Run(code);
            Assert.Equal(42, result.ExitCode);
        }

        [Fact]
        public void NoWarning_WhenCodeIsReachable()
        {
            string code = """
                int main()
                {
                    int x = 10;
                    if (x > 5)
                    {
                        x = x + 1;
                    }
                    return x;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.Check(code);

            Assert.Equal(0, checker.Diagnostics.Count);
            Assert.False(checker.Diagnostics.HasWarnings);
            Assert.False(checker.Diagnostics.HasErrors);
        }

        [Fact]
        public void Diagnostic_PlainTextFormatting_WithLocation()
        {
            Diagnostic diag = new Diagnostic(DiagnosticRules.GF2001_UnreachableCode, 15, 8, "test.gf");
            string formatted = diag.ToString(useColor: false);

            Assert.Equal("test.gf(15,8): warning GF2001: Unreachable code detected", formatted);
        }

        [Fact]
        public void Diagnostic_PlainTextFormatting_WithoutFile()
        {
            Diagnostic diag = new Diagnostic(DiagnosticRules.GF2001_UnreachableCode, 15);
            string formatted = diag.ToString(useColor: false);

            Assert.Equal("line 15: warning GF2001: Unreachable code detected", formatted);
        }

        [Fact]
        public void Diagnostic_ColorFormatting_ContainsAnsiCodes()
        {
            Diagnostic warnDiag = new Diagnostic(DiagnosticRules.GF2001_UnreachableCode, 10, 5);
            string formattedWarn = warnDiag.ToString(useColor: true);

            // Yellow warning ansi code and bold identifier
            Assert.Contains("\u001b[33;1mwarning\u001b[0m", formattedWarn);
            Assert.Contains("\u001b[1mGF2001:\u001b[0m", formattedWarn);

            Diagnostic errDiag = new Diagnostic(DiagnosticRules.GF1001_TypeMismatch, 20, 1, null, "int", "string");
            string formattedErr = errDiag.ToString(useColor: true);

            // Red error ansi code and bold identifier
            Assert.Contains("\u001b[31;1merror\u001b[0m", formattedErr);
            Assert.Contains("\u001b[1mGF1001:\u001b[0m", formattedErr);
        }

        [Fact]
        public void DiagnosticBag_FormatAll_FormatsMultipleDiagnostics()
        {
            DiagnosticBag bag = new DiagnosticBag();
            bag.Report(DiagnosticRules.GF2001_UnreachableCode, 5);
            bag.Report(DiagnosticRules.GF1001_TypeMismatch, 10, 0, null, "int", "bool");

            Assert.Equal(2, bag.Count);
            Assert.True(bag.HasWarnings);
            Assert.True(bag.HasErrors);
            Assert.Equal(1, bag.WarningCount);
            Assert.Equal(1, bag.ErrorCount);

            string output = bag.FormatAll(useColor: false);
            string[] lines = output.Split('\n');
            Assert.True(lines.Length >= 2);
        }

        [Fact]
        public void MultipleUndeclaredVariables_ReportedInDiagnosticBag()
        {
            string code = """
                int main()
                {
                    int a = unknownVar1;
                    int b = unknownVar2;
                    return 0;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.CheckDiagnostics(code);

            Assert.True(checker.Diagnostics.HasErrors);
            Assert.Equal(2, checker.Diagnostics.ErrorCount);
            Assert.Contains(checker.Diagnostics.Items, d => d.Message.Contains("unknownVar1"));
            Assert.Contains(checker.Diagnostics.Items, d => d.Message.Contains("unknownVar2"));

            // Compatibility: CompilerTestHelper.Check still throws TypeCheckException
            Assert.Throws<CompileExceptions.TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void MultipleTypeMismatches_ReportedInDiagnosticBag()
        {
            string code = """
                int main()
                {
                    int a = false;
                    bool b = 123;
                    return 0;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.CheckDiagnostics(code);

            Assert.True(checker.Diagnostics.HasErrors);
            Assert.Equal(2, checker.Diagnostics.ErrorCount);
            Assert.All(checker.Diagnostics.Items, d => Assert.Equal(DiagnosticRules.GF1001_TypeMismatch.Id, d.Descriptor.Id));
        }

        [Fact]
        public void CascadeErrors_SuppressedWhenUsingErrorType()
        {
            string code = """
                int main()
                {
                    int a = unknownVariable + 5;
                    return 0;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.CheckDiagnostics(code);

            Assert.True(checker.Diagnostics.HasErrors);
            // Exactly 1 error for unknownVariable; no secondary binary operator mismatch
            Assert.Equal(1, checker.Diagnostics.ErrorCount);
            Assert.Equal(DiagnosticRules.GF1003_UndeclaredIdentifier.Id, checker.Diagnostics.Items[0].Descriptor.Id);
        }

        [Fact]
        public void DuplicateVariableDeclaration_ReportedInDiagnosticBag()
        {
            string code = """
                int main()
                {
                    int x = 10;
                    int x = 20;
                    return x;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.CheckDiagnostics(code);

            Assert.True(checker.Diagnostics.HasErrors);
            Assert.Contains(checker.Diagnostics.Items, d => d.Descriptor.Id == DiagnosticRules.GF3002_VariableAlreadyDeclared.Id);
        }

        [Fact]
        public void Parser_MissingSemicolon_ReportsDiagnosticInsteadOfCrashing()
        {
            string code = """
                int main()
                {
                    int a = 10
                    return a;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.CheckDiagnostics(code);

            Assert.True(checker.Diagnostics.HasErrors);
            Assert.Contains(checker.Diagnostics.Items, d => d.Descriptor.Id == DiagnosticRules.GF0005_ExpectedToken.Id);
            Assert.Contains(checker.Diagnostics.Items, d => d.Message.Contains("Semicolon"));

            // Compatibility: CompilerTestHelper.Check still throws TypeCheckException
            Assert.Throws<CompileExceptions.TypeCheckException>(() => CompilerTestHelper.Check(code));
        }

        [Fact]
        public void Parser_MultipleMissingSemicolons_ReportsAllDiagnostics()
        {
            string code = """
                int main()
                {
                    int a = 10
                    int b = 20
                    return a + b;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.CheckDiagnostics(code);

            Assert.True(checker.Diagnostics.HasErrors);
            Assert.True(checker.Diagnostics.ErrorCount >= 2);
            Assert.True(checker.Diagnostics.Items.Count(d => d.Descriptor.Id == DiagnosticRules.GF0005_ExpectedToken.Id) >= 2);
        }

        [Fact]
        public void UndefinedTypeInNewExpression_DoesNotThrowNullReferenceException()
        {
            string code = """
                extern int printf(readonly char* fmt, ...);

                struct Cool
                {
                    int x;
                    ~Cool()
                    {
                        printf("destructor ran");
                    }
                }
                int main()
                {
                    Cool* l = new* Col();
                    defer delete l;
                    return 0;
                    l.x = 2;
                    printf("The number is: %d\n", 2);
                    return 0;
                }
                """;
            (ast.CompilationUnit _, TypeChecker checker) = CompilerTestHelper.CheckDiagnostics(code);
            Assert.True(checker.Diagnostics.HasErrors);
        }
    }
}
