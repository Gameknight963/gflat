using gflat.ast;
using gflat.comptime;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly HashSet<List<Parameter>> expandingParameterLists = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AstNode, Dictionary<Parameter, Parameter>> splicedParameterOrigins = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SpliceExpression, ConstValue.Reflection> argumentSpliceValues = new();

    private void CountListExpansion(int count, AstNode site)
    {
        staticExpansionElements += count;
        if (staticExpansionElements > 100000) throw new TypeCheckException("Compile-time expansion exceeded 100000 elements", site.Span);
    }

    private ConstValue EvaluateListSplice(AstNode expression)
    {
        expression.Accept(this);
        if (!_constEvaluator.TryEvaluate(expression, out var value, out var error) || value == null)
            throw new TypeCheckException("A list splice requires a compile-time value" + (error == null ? "" : ": " + error), expression.Span);
        return value;
    }

    private void ExpandParameterLists(AstNode owner, List<Parameter> parameters)
    {
        if (!parameters.Any(parameter => parameter.IsListSplice)) return;
        if (!expandingParameterLists.Add(parameters))
            throw new TypeCheckException("Circular parameter-list splice", owner.Span);
        try
        {
            var expanded = new List<Parameter>();
            var origins = new Dictionary<Parameter, Parameter>(ReferenceEqualityComparer.Instance);
            foreach (var parameter in parameters)
            {
                if (!parameter.IsListSplice) { expanded.Add(parameter); continue; }
                var operand = ((SpliceTypeExpression)parameter.Type).Reflection;
                if (EvaluateListSplice(operand) is not ConstValue.Array list)
                    throw new TypeCheckException("A parameter-list splice requires an array of parameter reflections", operand.Span);
                CountListExpansion(list.Elements.Count, operand);
                foreach (var element in list.Elements)
                {
                    if (element is not ConstValue.Reflection { Kind: "Parameter", Declaration: Parameter original, Type: { } type })
                        throw new TypeCheckException("A parameter-list splice requires parameter reflections", operand.Span);
                    var copy = new Parameter(original.Name, type, parameter.Line, original.IsConst, original.DefaultValue) { Span = parameter.Span };
                    if (original.DefaultValue != null) parameterDefaults[copy] = ParameterDefault(original);
                    if (!origins.TryAdd(original, copy)) throw new TypeCheckException("A parameter declaration cannot be spliced twice into the same signature", parameter.Span);
                    expanded.Add(copy);
                }
            }
            parameters.Clear();
            parameters.AddRange(expanded);
            splicedParameterOrigins[owner] = origins;
        }
        finally { expandingParameterLists.Remove(parameters); }
    }

    private void ExpandArgumentLists(List<AstNode> arguments)
    {
        if (!arguments.Any(argument => argument is SpliceExpression { Receiver: null } or MoveExpression { Destination: null, Operand: SpliceExpression { Receiver: null } })) return;
        for (int index = 0; index < arguments.Count; index++)
        {
            bool move = arguments[index] is MoveExpression { Destination: null };
            var candidate = move ? ((MoveExpression)arguments[index]).Operand : arguments[index];
            if (candidate is not SpliceExpression { Receiver: null } splice) continue;
            var value = EvaluateListSplice(splice.Reflection);
            if (value is not ConstValue.Array list)
            {
                if (value is ConstValue.Reflection scalar) argumentSpliceValues[splice] = scalar;
                continue;
            }
            CountListExpansion(list.Elements.Count, splice);
            var expanded = new List<AstNode>();
            foreach (var element in list.Elements)
            {
                if (element is not ConstValue.Reflection reflection)
                    throw new TypeCheckException("An argument-list splice requires reflection elements", splice.Span);
                if (reflection.Declaration is Parameter original && (_currentFunction ?? (AstNode?)_currentConstructor) is { } owner &&
                    splicedParameterOrigins.TryGetValue(owner, out var origins) && origins.TryGetValue(original, out var copy))
                {
                    string identity = ReflectedDeclarationIdentity(copy) + ":" + copy.Name;
                    reflection = reflection with { Identity = identity, Name = copy.Name, Declaration = copy, Type = copy.Type };
                }
                var expression = new SpliceExpression(new ConstantExpression(reflection, ReflectionType(splice.Line), splice.Line), null, splice.Line) { Span = splice.Span };
                AstNode argument = reflection.Declaration is Parameter { Type: ReferenceTypeExpression }
                    ? new UnaryExpression(expression, TokenKind.Ref, true, splice.Line) { Span = splice.Span } : expression;
                if (move)
                {
                    if (argument is UnaryExpression { Operator: TokenKind.Ref })
                        throw new TypeCheckException("A ref parameter cannot be forwarded with move", splice.Span);
                    expression.Accept(this);
                    argument = new MoveExpression(expression.Resolved!, splice.Line) { Span = splice.Span };
                }
                expanded.Add(argument);
            }
            arguments.RemoveAt(index);
            arguments.InsertRange(index, expanded);
            index += expanded.Count - 1;
        }
    }
}
