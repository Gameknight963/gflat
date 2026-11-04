using gflat.ast;
using gflat.comptime;
using gflat.semantics;
using System.Runtime.ExceptionServices;

namespace gflat.Tests;

public sealed class ControlFlowSummaryTests
{
    [Fact]
    public void StructuredSummariesMatchGraphReachabilityAcrossNestedStatements()
    {
        var random = new Random(913804);
        for (int i = 0; i < 400; i++)
        {
            string body = Statement(4);
            var ast = Parser.Parse(Lexer.Tokenize("void Test() { " + body + " }"));
            var method = Assert.IsType<MethodDeclaration>(Assert.Single(ast.Members));
            var checker = new TypeChecker();
            var flow = new ControlFlowPass(checker, new ConstEvaluator(checker));
            foreach (var statement in Statements(method.Body!))
            {
                var graph = new ControlFlowGraph(statement, Condition);
                Assert.True(flow.StatementTerminates(statement) == !graph.FallsThrough, body);
            }
        }

        string Statement(int depth)
        {
            if (depth == 0) return random.Next(6) switch {
                0 => "return;", 1 => "throw value;", 2 => "break;", 3 => "continue;", 4 => "Work();", _ => "{}"
            };
            string condition = random.Next(3) switch { 0 => "true", 1 => "false", _ => "condition" };
            return random.Next(8) switch {
                0 => "{ " + Statement(depth - 1) + Statement(depth - 1) + " }",
                1 => "if (" + condition + ") { " + Statement(depth - 1) + " } else { " + Statement(depth - 1) + " }",
                2 => "while (" + condition + ") { " + Statement(depth - 1) + " }",
                3 => "for (; " + condition + ";) { " + Statement(depth - 1) + " }",
                4 => "try { " + Statement(depth - 1) + " } catch { " + Statement(depth - 1) + " }",
                5 => "if (" + condition + ") { " + Statement(depth - 1) + " }",
                6 => "foreach (int item in items) { " + Statement(depth - 1) + " }",
                _ => "for (;;) { " + Statement(depth - 1) + " }"
            };
        }
    }

    [Fact]
    public void GraphEvaluatesEachWhileConditionOnce()
    {
        var ast = Parser.Parse(Lexer.Tokenize("void Test() { while (condition) { continue; } }"));
        var method = Assert.IsType<MethodDeclaration>(Assert.Single(ast.Members));
        int probes = 0;
        var graph = new ControlFlowGraph(method.Body!, _ => { probes++; return null; });
        Assert.True(graph.FallsThrough);
        Assert.Equal(1, probes);
    }

    [Fact]
    public void ProbingRuntimeConditionsDoesNotThrowInterpreterExceptions()
    {
        var ast = Parser.Parse(Lexer.Tokenize("void Test() { while (runtime < 10) { if (!other) break; } }"));
        var method = Assert.IsType<MethodDeclaration>(Assert.Single(ast.Members));
        var checker = new TypeChecker();
        var flow = new ControlFlowPass(checker, new ConstEvaluator(checker));
        int thread = Environment.CurrentManagedThreadId, exceptions = 0;
        void OnException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (Environment.CurrentManagedThreadId == thread && args.Exception is ConstEvalException) exceptions++;
        }
        AppDomain.CurrentDomain.FirstChanceException += OnException;
        try { Assert.False(flow.StatementTerminates(method.Body!)); }
        finally { AppDomain.CurrentDomain.FirstChanceException -= OnException; }
        Assert.Equal(0, exceptions);
    }

    [Theory]
    [InlineData("Always() || runtime")]
    [InlineData("true || runtime")]
    [InlineData("!(false && runtime)")]
    public void ConstantConditionsPreserveCallsAndShortCircuiting(string condition)
        => Compiler.Check("const bool Always() => true; int Read(bool runtime) { if (" + condition + ") return 42; }",
            options: new() { NoStdLib = true, NoBuiltins = true });

    private static bool? Condition(AstNode node) => node is LiteralExpression literal
        ? literal.Token.Kind switch { TokenKind.True => true, TokenKind.False => false, _ => null } : null;

    private static IEnumerable<AstNode> Statements(AstNode node)
    {
        yield return node;
        IEnumerable<AstNode> children = node switch {
            BlockStatement block => block.Statements,
            IfStatement branch => branch.Else == null ? [branch.Then] : [branch.Then, branch.Else],
            WhileStatement loop => [loop.Body],
            ForStatement loop => [loop.Body],
            ForeachStatement loop => [loop.Body],
            TryStatement guarded => new[] { guarded.TryBlock }.Concat(guarded.CatchClauses.Select(c => c.Body)),
            _ => []
        };
        foreach (var child in children)
        foreach (var nested in Statements(child)) yield return nested;
    }
}
