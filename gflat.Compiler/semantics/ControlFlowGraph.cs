using gflat.ast;

namespace gflat.semantics;

public enum FlowExit { Normal, Return, Throw, Break, Continue }

/// <summary>A checked, structured control-flow graph shared by semantic analyses.</summary>
public sealed class ControlFlowGraph
{
    public sealed class Block(AstNode? operation = null, FlowExit? exit = null)
    {
        public AstNode? Operation { get; } = operation;
        public FlowExit? Exit { get; } = exit;
        public List<Block> Successors { get; } = new();
    }

    public Block Entry { get; }
    public IReadOnlySet<FlowExit> ReachableExits { get; }
    public bool FallsThrough => ReachableExits.Contains(FlowExit.Normal);

    public ControlFlowGraph(AstNode statement, Func<AstNode, bool?> constantCondition)
    {
        var normal = new Block(exit: FlowExit.Normal);
        var returned = new Block(exit: FlowExit.Return);
        var thrown = new Block(exit: FlowExit.Throw);
        var broken = new Block(exit: FlowExit.Break);
        var continued = new Block(exit: FlowExit.Continue);

        Block Build(AstNode node, Block next, Block breakTarget, Block continueTarget, Block throwTarget)
        {
            Block Link(params Block[] targets)
            {
                var block = new Block(node);
                block.Successors.AddRange(targets);
                return block;
            }
            switch (node)
            {
                case ReturnStatement: return Link(returned);
                case ThrowStatement: return Link(throwTarget);
                case BreakStatement: return Link(breakTarget);
                case ContinueStatement: return Link(continueTarget);
                case BlockStatement body:
                    for (int i = body.Statements.Count - 1; i >= 0; --i)
                        next = Build(body.Statements[i], next, breakTarget, continueTarget, throwTarget);
                    return next;
                case IfStatement branch:
                    var then = Build(branch.Then, next, breakTarget, continueTarget, throwTarget);
                    var otherwise = branch.Else == null ? next : Build(branch.Else, next, breakTarget, continueTarget, throwTarget);
                    return constantCondition(branch.Condition) switch { true => Link(then), false => Link(otherwise), _ => Link(then, otherwise) };
                case WhileStatement loop:
                    var test = new Block(loop.Condition);
                    var bodyEntry = Build(loop.Body, test, next, test, throwTarget);
                    if (constantCondition(loop.Condition) != false) test.Successors.Add(bodyEntry);
                    if (constantCondition(loop.Condition) != true) test.Successors.Add(next);
                    return test;
                case ForStatement loop:
                    var condition = new Block(loop.Condition);
                    var increment = loop.Increment == null ? condition : Build(loop.Increment, condition, next, condition, throwTarget);
                    var iteration = Build(loop.Body, increment, next, increment, throwTarget);
                    bool? value = loop.Condition == null ? true : constantCondition(loop.Condition);
                    if (value != false) condition.Successors.Add(iteration);
                    if (value != true) condition.Successors.Add(next);
                    return loop.Initializer == null ? condition : Build(loop.Initializer, condition, next, increment, throwTarget);
                case ForeachStatement loop:
                    var cursor = new Block(loop.Collection);
                    cursor.Successors.Add(next);
                    cursor.Successors.Add(Build(loop.Body, cursor, next, cursor, throwTarget));
                    return cursor;
                case TryStatement guarded:
                    var handler = new Block();
                    foreach (var clause in guarded.CatchClauses)
                        handler.Successors.Add(Build(clause.Body, next, breakTarget, continueTarget, throwTarget));
                    handler.Successors.Add(throwTarget); // typed catches may not match
                    var protectedEntry = Build(guarded.TryBlock, next, breakTarget, continueTarget, handler);
                    // Calls can raise exceptions too; keep catch paths conservatively reachable.
                    return Link(protectedEntry, handler);
                default: return Link(next);
            }
        }
        Entry = Build(statement, normal, broken, continued, thrown);
        var seen = new HashSet<Block>();
        var exits = new HashSet<FlowExit>();
        var work = new Stack<Block>();
        work.Push(Entry);
        while (work.TryPop(out var block))
        {
            if (!seen.Add(block)) continue;
            if (block.Exit is { } exit) exits.Add(exit);
            foreach (var successor in block.Successors) work.Push(successor);
        }
        ReachableExits = exits;
    }
}
