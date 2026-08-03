using System.Text;
using gflat.ast;

namespace gflat;

public class LlvmEmitter : IVisitor
{
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

    public void Visit(IfStatement node) => throw new NotImplementedException();
    public void Visit(WhileStatement node) => throw new NotImplementedException();
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
    public void Visit(ExpressionStatement node) => throw new NotImplementedException();

    public void Visit(BinaryExpression node)
    {
        node.Left.Accept(this);
        string left = Pop();
        node.Right.Accept(this);
        string right = Pop();
        string temp = NewTemp();

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
            default:
                throw new NotImplementedException($"Literal type {node.Token.Kind} not yet supported");
        }
    }

    public void Visit(IdentifierExpression node)
    {
        if (_locals.TryGetValue(node.Name, out string? ptr))
        {
            string temp = NewTemp();
            // we need the type here which is a problem...
            Emit($"    {temp} = load i32, i32* {ptr}");
            Push(temp);
            return;
        }
        throw new Exception($"Unknown identifier '{node.Name}'");
    }
    public void Visit(CallExpression node) => throw new NotImplementedException();
    public void Visit(MemberAccessExpression node) => throw new NotImplementedException();
    public void Visit(AssignmentExpression node) => throw new NotImplementedException();
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