using System.Text;
using gflat.ast;

namespace gflat;

public class LlvmEmitter : IVisitor
{
    private TypeChecker _typeChecker;

    public LlvmEmitter(TypeChecker typeChecker)
    {
        _typeChecker = typeChecker;
    }

    private StringBuilder _output = new();
    private StringBuilder _globals = new();
    private int _tempCounter = 0;
    private int _stringCounter = 0;
    private Stack<string> _valueStack = new();
    private Dictionary<string, string> _locals = new();

    private string NewTemp() => $"%t{_tempCounter++}";
    private string NewGlobal() => $"@str{_stringCounter++}";

    private void Push(string value) => _valueStack.Push(value);
    private string Pop() => _valueStack.Pop();

    private void Emit(string line) => _output.AppendLine(line);
    private void EmitGlobal(string line) => _globals.AppendLine(line);

    public string GetOutput() => _globals.ToString() + "\n" + _output.ToString();

    private int _labelCounter = 0;
    private string NewLabel(string prefix) => $"{prefix}_{_labelCounter++}";


    private string EmitType(TypeExpression type)
    {
        if (type is NamedTypeExpression named)
        {
            return named.Name switch
            {
                "int" => "i32",
                "long" => "i64",
                "extralong" => "i128",
                "float" => "float",
                "bool" => "i1",
                "char" => "i8",
                "void" => "void",
                "string" => "i8*",
                _ => $"%{named.Name}"
            };
        }
        if (type is PointerTypeExpression ptr)
            return EmitType(ptr.Inner) + "*";
        if (type is ArrayTypeExpression arr)
            return EmitType(arr.ElementType) + "*";

        throw new NotImplementedException($"Type {type.GetType().Name} not yet supported");
    }

    public void Visit(CompilationUnit node)
    {
        EmitGlobal("declare i32 @puts(i8*)");
        EmitGlobal("declare i32 @printf(i8*, ...)");
        foreach (NamespaceDeclaration ns in node.Namespaces)
            ns.Accept(this);
    }

    public void Visit(UsingDirective node) { }

    public void Visit(NamespaceDeclaration node)
    {
        foreach (AstNode member in node.Members)
            member.Accept(this);
    }

    public void Visit(ClassDeclaration node)
    {
        foreach (AstNode member in node.Members)
            member.Accept(this);
    }

    public void Visit(StructDeclaration node) => throw new NotImplementedException();
    public void Visit(InterfaceDeclaration node) => throw new NotImplementedException();

    public void Visit(FieldDeclaration node) => throw new NotImplementedException();

    public void Visit(MethodDeclaration node)
    {
        string returnType = EmitType(node.ReturnType);
        string name = node.Name == "Main" ? "main" : node.Name;
        string parameters = string.Join(", ", node.Parameters.Select(p =>
            $"{EmitType(p.Type)} %{p.Name}"));

        Emit($"define {returnType} @{name}({parameters}) {{");
        Emit("entry:");
        node.Body.Accept(this);
        Emit("}");
        Emit("");
    }

    public void Visit(ConstructorDeclaration node) => throw new NotImplementedException();
    public void Visit(Parameter node) => throw new NotImplementedException();

    public void Visit(BlockStatement node)
    {
        foreach (AstNode statement in node.Statements)
            statement.Accept(this);
    }

    public void Visit(ReturnStatement node)
    {
        if (node.Value == null)
        {
            Emit("    ret void");
            return;
        }
        node.Value.Accept(this);
        string val = Pop();
        Emit($"    ret i32 {val}");
    }

    public void Visit(IfStatement node)
    {
        string thenLabel = NewLabel("then");
        string elseLabel = NewLabel("else");
        string mergeLabel = NewLabel("merge");

        node.Condition.Accept(this);
        string cond = Pop();

        string condBit;
        TypeExpression condType = _typeChecker.GetType(node.Condition);
        if (condType is NamedTypeExpression { Name: "bool" })
            condBit = cond;
        else
        {
            condBit = NewTemp();
            Emit($"    {condBit} = icmp ne i32 {cond}, 0");
        }

        if (node.Else != null)
            Emit($"    br i1 {condBit}, label %{thenLabel}, label %{elseLabel}");
        else
            Emit($"    br i1 {condBit}, label %{thenLabel}, label %{mergeLabel}");

        Emit($"{thenLabel}:");
        node.Then.Accept(this);
        Emit($"    br label %{mergeLabel}");

        if (node.Else != null)
        {
            Emit($"{elseLabel}:");
            node.Else.Accept(this);
            Emit($"    br label %{mergeLabel}");
        }

        Emit($"{mergeLabel}:");
    }

    public void Visit(WhileStatement node)
    {
        string condLabel = NewLabel("while_cond");
        string bodyLabel = NewLabel("while_body");
        string exitLabel = NewLabel("while_exit");

        Emit($"    br label %{condLabel}");
        Emit($"{condLabel}:");

        node.Condition.Accept(this);
        string cond = Pop();

        string condBit;
        TypeExpression condType = _typeChecker.GetType(node.Condition);
        if (condType is NamedTypeExpression { Name: "bool" })
            condBit = cond;
        else
        {
            condBit = NewTemp();
            Emit($"    {condBit} = icmp ne i32 {cond}, 0");
        }

        Emit($"    br i1 {condBit}, label %{bodyLabel}, label %{exitLabel}");
        Emit($"{bodyLabel}:");
        node.Body.Accept(this);
        Emit($"    br label %{condLabel}");
        Emit($"{exitLabel}:");
    }

    public void Visit(ForStatement node) => throw new NotImplementedException();

    public void Visit(VariableDeclaration node)
    {
        string type = EmitType(node.Type);
        string ptr = NewTemp();
        Emit($"    {ptr} = alloca {type}");
        _locals[node.Name] = ptr;

        if (node.Initializer != null)
        {
            node.Initializer.Accept(this);
            string val = Pop();
            Emit($"    store {type} {val}, {type}* {ptr}");
        }
    }
    public void Visit(ExpressionStatement node)
    {
        node.Expression.Accept(this);
        if (_valueStack.Count > 0) Pop(); // discard result
    }
    public void Visit(BinaryExpression node)
    {
        node.Left.Accept(this);
        string left = Pop();
        node.Right.Accept(this);
        string right = Pop();
        string temp = NewTemp();

        bool isComparison = node.Operator is TokenKind.EqualsEquals or TokenKind.NotEquals or
            TokenKind.Less or TokenKind.Greater or TokenKind.LessEquals or TokenKind.GreaterEquals;

        if (isComparison)
        {
            string op = node.Operator switch
            {
                TokenKind.EqualsEquals => "eq",
                TokenKind.NotEquals => "ne",
                TokenKind.Less => "slt",
                TokenKind.Greater => "sgt",
                TokenKind.LessEquals => "sle",
                TokenKind.GreaterEquals => "sge",
                _ => throw new NotImplementedException()
            };
            Emit($"    {temp} = icmp {op} i32 {left}, {right}");
            Push(temp);
        }
        else
        {
            string op = node.Operator switch
            {
                TokenKind.Plus => "add",
                TokenKind.Minus => "sub",
                TokenKind.Star => "mul",
                TokenKind.Slash => "sdiv",
                TokenKind.Percent => "srem",
                _ => throw new NotImplementedException($"Operator {node.Operator} not yet supported")
            };
            Emit($"    {temp} = {op} i32 {left}, {right}");
            Push(temp);
        }
    }
    public void Visit(UnaryExpression node) => throw new NotImplementedException();

    public void Visit(LiteralExpression node)
    {
        switch (node.Token.Kind)
        {
            case TokenKind.IntLiteral:
                Push(node.Token.Text);
                break;
            case TokenKind.True:
                Push("1");
                break;
            case TokenKind.False:
                Push("0");
                break;
            case TokenKind.Null:
                Push("null");
                break;
            case TokenKind.StringLiteral:
                {
                    string raw = node.Token.Text[1..^1]; // strip quotes
                    string escaped = raw.Replace("\\n", "\n").Replace("\\t", "\t");
                    string globalName = NewGlobal();
                    int len = escaped.Length + 1; // +1 for null terminator
                    string llvmStr = escaped.Replace("\n", "\\0A").Replace("\t", "\\09");
                    EmitGlobal($"{globalName} = private constant [{len} x i8] c\"{llvmStr}\\00\"");
                    string ptr = NewTemp();
                    Emit($"    {ptr} = getelementptr [{len} x i8], [{len} x i8]* {globalName}, i32 0, i32 0");
                    Push(ptr);
                    break;
                }
            default:
                throw new NotImplementedException($"Literal type {node.Token.Kind} not yet supported");
        }
    }

    public void Visit(IdentifierExpression node)
    {
        if (_locals.TryGetValue(node.Name, out string? ptr))
        {
            TypeExpression type = _typeChecker.GetType(node);
            string llvmType = EmitType(type);
            string temp = NewTemp();
            Emit($"    {temp} = load {llvmType}, {llvmType}* {ptr}");
            Push(temp);
            return;
        }
        throw new Exception($"Unknown identifier '{node.Name}'");
    }
    public void Visit(CallExpression node)
    {
        // evaluate all arguments first
        List<string> argValues = new();
        List<string> argTypes = new();
        foreach (AstNode arg in node.Arguments)
        {
            arg.Accept(this);
            argValues.Add(Pop());
            TypeExpression argType = _typeChecker.GetType(arg);
            argTypes.Add(EmitType(argType));
        }

        // get the function name
        string funcName;
        if (node.Callee is IdentifierExpression ident)
            funcName = ident.Name;
        else if (node.Callee is MemberAccessExpression member)
            funcName = member.Member; // simplified for now
        else
            throw new NotImplementedException("Complex callee not supported");

        string args = string.Join(", ", argValues.Zip(argTypes, (v, t) => $"{t} {v}"));

        // handle void vs non-void
        string temp = NewTemp();
        Emit($"    {temp} = call i32 @{funcName}({args})");
        Push(temp);
    }
    public void Visit(MemberAccessExpression node) => throw new NotImplementedException();
    public void Visit(AssignmentExpression node)
    {
        node.Value.Accept(this);
        string val = Pop();

        if (node.Target is IdentifierExpression ident)
        {
            if (!_locals.TryGetValue(ident.Name, out string? ptr))
                throw new Exception($"Unknown variable '{ident.Name}'");
            Emit($"    store i32 {val}, i32* {ptr}");
            Push(val); // assignment is an expression, push the value back
        }
        else
        {
            throw new NotImplementedException("Complex assignment targets not yet supported");
        }
    }
    public void Visit(InterpolatedStringExpression node) => throw new NotImplementedException();
    public void Visit(NewExpression node) => throw new NotImplementedException();
    public void Visit(NamespaceAccessExpression node) => throw new NotImplementedException();
    public void Visit(NamedTypeExpression node) => throw new NotImplementedException();
    public void Visit(PointerTypeExpression node) => throw new NotImplementedException();
    public void Visit(ManagedTypeExpression node) => throw new NotImplementedException();
    public void Visit(ArrayTypeExpression node) => throw new NotImplementedException();
    public void Visit(BreakStatement node) => throw new NotImplementedException();
    public void Visit(ContinueStatement node) => throw new NotImplementedException();
}