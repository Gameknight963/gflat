using System.Text;
using gflat.ast;

namespace gflat;

public class LlvmEmitter : IVisitor
{
    private readonly TypeChecker _typeChecker;

    public LlvmEmitter(TypeChecker typeChecker)
    {
        _typeChecker = typeChecker;
    }

    private readonly StringBuilder _output = new();
    private readonly StringBuilder _globals = new();
    private int _tempCounter = 0;
    private int _stringCounter = 0;
    private readonly Stack<string> _valueStack = new();
    private readonly Dictionary<string, string> _locals = new();

    private readonly Stack<string> _breakLabels = new();
    private readonly Stack<string> _continueLabels = new();

    private readonly HashSet<string> _externNames = new();
    private bool IsExtern(string name) => _externNames.Contains(name);

    private string _currentNamespacePath = "";

    private string NewTemp() => $"%t{_tempCounter++}";
    private string NewGlobal() => $"@str{_stringCounter++}";

    private void Push(string value) => _valueStack.Push(value);
    private string Pop() => _valueStack.Pop();

    private void Emit(string line) => _output.AppendLine(line);
    private void EmitGlobal(string line) => _globals.AppendLine(line);

    public string GetOutput() => _globals.ToString() + "\n" + _output.ToString();

    private enum EmitMode { RValue, LValue }

    private void EmitAddress(AstNode node)
    {
        if (node is IdentifierExpression ident)
        {
            if (!_locals.TryGetValue(ident.Name, out string? ptr))
                throw new Exception($"Unknown variable '{ident.Name}'");
            Push(ptr);
        }
        else if (node is MemberAccessExpression member)
        {
            EmitMemberAddress(member);
        }
        else if (node is UnaryExpression { Operator: TokenKind.Star } deref)
        {
            deref.Operand.Accept(this);
        }
        else
            throw new NotImplementedException($"Cannot take address of {node.GetType().Name}");
    }


    private void EmitMemberAddress(MemberAccessExpression node)
    {
        string objPtr;
        TypeExpression objType = _typeChecker.GetType(node.Object);

        if (node.Object is IdentifierExpression ident && _locals.TryGetValue(ident.Name, out string? ptr))
        {
            if (objType is PointerTypeExpression innerPtrType)
            {
                string innerLlvmType = EmitType(innerPtrType.Inner);
                string loadedPtr = NewTemp();
                Emit($"    {loadedPtr} = load {innerLlvmType}*, {innerLlvmType}** {ptr}");
                objPtr = loadedPtr;
            }
            else
                objPtr = ptr;
        }
        else
        {
            node.Object.Accept(this);
            objPtr = Pop();
        }

        if (objType is PointerTypeExpression ptrType)
            objType = ptrType.Inner;

        string structName = ((NamedTypeExpression)objType).Name;
        TypeChecker.StructInfo info = _typeChecker.GetStruct(structName)!;
        int fieldIdx = info.FieldIndex(node.Member);
        TypeExpression fieldType = info.Fields[fieldIdx].Type;
        string llvmFieldType = EmitType(fieldType);

        string fieldPtr = NewTemp();
        Emit($"    {fieldPtr} = getelementptr %{structName}, %{structName}* {objPtr}, i32 0, i32 {fieldIdx}");
        Push(fieldPtr);
    }

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
                "double" => "double",
                "bool" => "i1",
                "char" => "i8",
                "void" => "void",
                "string" => "i8*",
                _ => $"%{named.Name}"
            };
        }
        if (type is PointerTypeExpression ptr)
        {
            // LLVM does not allow void*, use i8* instead
            if (ptr.Inner is NamedTypeExpression { Name: "void" })
                return "i8*";

            return EmitType(ptr.Inner) + "*";
        }
        if (type is ArrayTypeExpression arr)
            return EmitType(arr.ElementType) + "*";

        throw new NotImplementedException($"Type {type.GetType().Name} not yet supported");
    }

    public void Visit(CompilationUnit node)
    {
        _currentNamespacePath = "";
        foreach (AstNode member in node.Members)
            member.Accept(this);

        foreach (NamespaceDeclaration ns in node.Namespaces)
            ns.Accept(this);
    }

    public void Visit(UsingDirective node) { }

    public void Visit(NamespaceDeclaration node)
    {
        string previous = _currentNamespacePath;
        _currentNamespacePath = _currentNamespacePath.Length > 0
            ? $"{_currentNamespacePath}${node.Name}"
            : node.Name;

        foreach (AstNode member in node.Members)
            member.Accept(this);

        _currentNamespacePath = previous;
    }

    public void Visit(ClassDeclaration node)
    {
        foreach (AstNode member in node.Members)
            member.Accept(this);
    }

    public void Visit(StructDeclaration node)
    {
        string fields = string.Join(", ", node.Members
            .OfType<FieldDeclaration>()
            .Select(f => EmitType(f.Type)));
        EmitGlobal($"%{node.Name} = type {{ {fields} }}");
    }

    public void Visit(InterfaceDeclaration node) => throw new NotImplementedException();

    public void Visit(FieldDeclaration node) => throw new NotImplementedException();

    public void Visit(ExternDeclaration node)
    {
        _externNames.Add(node.Name);
        string returnType = EmitType(node.ReturnType);
        string parameters = string.Join(", ", node.Parameters.Select(p => EmitType(p.Type)));
        if (node.IsVariadic)
            parameters = parameters.Length > 0 ? parameters + ", ..." : "...";
        EmitGlobal($"declare {returnType} @{node.Name}({parameters})");
    }

    public void Visit(MethodDeclaration node)
    {
        _locals.Clear();
        _tempCounter = 0;

        string returnType = EmitType(node.ReturnType);
        string name;
        if (node.Name == "main")
            name = "main";
        else if (_currentNamespacePath.Length > 0)
            name = $"gflat${_currentNamespacePath}${node.Name}";
        else
            name = $"gflat${node.Name}";

        string parameters = string.Join(", ", node.Parameters.Select(p =>
            $"{EmitType(p.Type)} %{p.Name}"));

        Emit($"define {returnType} @{name}({parameters}) {{");
        Emit("entry:");

        foreach (Parameter p in node.Parameters)
        {
            string type = EmitType(p.Type);
            string ptr = NewTemp();
            Emit($"    {ptr} = alloca {type}");
            Emit($"    store {type} %{p.Name}, {type}* {ptr}");
            _locals[p.Name] = ptr;
        }

        node.Body.Accept(this);

        // emit ret void if void function has no explicit return
        if (returnType == "void")
            Emit("    ret void");

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
        TypeExpression returnType = _typeChecker.GetType(node.Value);
        string llvmReturnType = EmitType(returnType);
        Emit($"    ret {llvmReturnType} {val}");
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

        _breakLabels.Push(exitLabel);
        _continueLabels.Push(condLabel);

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

        _breakLabels.Pop();
        _continueLabels.Pop();
    }

    public void Visit(ForStatement node)
    {
        string condLabel = NewLabel("for_cond");
        string bodyLabel = NewLabel("for_body");
        string exitLabel = NewLabel("for_exit");

        _breakLabels.Push(exitLabel);
        _continueLabels.Push(condLabel);

        node.Initializer?.Accept(this);

        Emit($"    br label %{condLabel}");
        Emit($"{condLabel}:");

        if (node.Condition != null)
        {
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
        }
        else
            Emit($"    br label %{bodyLabel}");

        Emit($"{bodyLabel}:");
        node.Body.Accept(this);
        if (node.Increment != null)
            node.Increment.Accept(this);
        Emit($"    br label %{condLabel}");
        Emit($"{exitLabel}:");

        _breakLabels.Pop();
        _continueLabels.Pop();
    }

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

        TypeExpression leftType = _typeChecker.GetType(node.Left);
        string llvmType = EmitType(leftType);
        bool isFloat = leftType is NamedTypeExpression { Name: "float" };

        bool isComparison = node.Operator is TokenKind.EqualsEquals or TokenKind.NotEquals or
            TokenKind.Less or TokenKind.Greater or TokenKind.LessEquals or TokenKind.GreaterEquals;

        if (isComparison)
        {
            if (isFloat)
            {
                string op = node.Operator switch
                {
                    TokenKind.EqualsEquals => "oeq",
                    TokenKind.NotEquals => "one",
                    TokenKind.Less => "olt",
                    TokenKind.Greater => "ogt",
                    TokenKind.LessEquals => "ole",
                    TokenKind.GreaterEquals => "oge",
                    _ => throw new NotImplementedException()
                };
                Emit($"    {temp} = fcmp {op} float {left}, {right}");
            }
            else
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
                Emit($"    {temp} = icmp {op} {llvmType} {left}, {right}");
            }
            Push(temp);
        }
        else if (node.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
        {
            string op = node.Operator == TokenKind.AmpersandAmpersand ? "and" : "or";
            Emit($"    {temp} = {op} i1 {left}, {right}");
            Push(temp);
        }
        else
        {
            if (isFloat)
            {
                string op = node.Operator switch
                {
                    TokenKind.Plus => "fadd",
                    TokenKind.Minus => "fsub",
                    TokenKind.Star => "fmul",
                    TokenKind.Slash => "fdiv",
                    _ => throw new NotImplementedException($"Float operator {node.Operator} not supported")
                };
                Emit($"    {temp} = {op} float {left}, {right}");
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
                Emit($"    {temp} = {op} {llvmType} {left}, {right}");
            }
            Push(temp);
        }
    }

    public void Visit(UnaryExpression node)
    {
        if (node.Operator == TokenKind.Ampersand)
        {
            EmitAddress(node.Operand);
            return;
        }

        if (node.Operator == TokenKind.Star)
        {
            node.Operand.Accept(this);
            string ptr = Pop();
            TypeExpression ptrType = _typeChecker.GetType(node.Operand);
            if (ptrType is not PointerTypeExpression innerPtr)
                throw new Exception("Cannot dereference non-pointer");

            string innerType = EmitType(innerPtr.Inner);
            string temp = NewTemp();
            Emit($"    {temp} = load {innerType}, {innerType}* {ptr}");
            Push(temp);
            return;
        }

        node.Operand.Accept(this);
        string operand = Pop();
        TypeExpression type = _typeChecker.GetType(node.Operand);
        string llvmType = EmitType(type);

        switch (node.Operator)
        {
            case TokenKind.Minus:
                {
                    string temp = NewTemp();
                    Emit($"    {temp} = sub {llvmType} 0, {operand}");
                    Push(temp);
                    break;
                }
            case TokenKind.Bang:
                {
                    string temp = NewTemp();
                    Emit($"    {temp} = xor i1 {operand}, 1");
                    Push(temp);
                    break;
                }
            case TokenKind.PlusPlus:
            case TokenKind.MinusMinus:
                {
                    string op = node.Operator == TokenKind.PlusPlus ? "add" : "sub";
                    string temp = NewTemp();
                    Emit($"    {temp} = {op} {llvmType} {operand}, 1");

                    if (node.Operand is IdentifierExpression ident && _locals.TryGetValue(ident.Name, out string? ptr))
                        Emit($"    store {llvmType} {temp}, {llvmType}* {ptr}");

                    Push(node.IsPrefix ? temp : operand);
                    break;
                }
            default:
                throw new NotImplementedException($"Unary operator {node.Operator} not yet supported");
        }
    }

    public void Visit(LiteralExpression node)
    {
        switch (node.Token.Kind)
        {
            case TokenKind.IntLiteral:
                Push(node.Token.Text);
                break;
            case TokenKind.HexInt:
                Push(Convert.ToInt64(node.Token.Text, 16).ToString());
                break;
            case TokenKind.LongLiteral:
                Push(node.Token.Text.TrimEnd('L', 'l'));
                break;
            case TokenKind.FloatLiteral:
            case TokenKind.DoubleLiteral:
                {
                    string txt = node.Token.Text.TrimEnd('f', 'F', 'd', 'D');
                    if (double.TryParse(txt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
                    {
                        string str = d.ToString("0.0######", System.Globalization.CultureInfo.InvariantCulture);
                        if (!str.Contains('.')) str += ".0";
                        Push(str);
                    }
                    else
                    {
                        Push(txt);
                    }
                    break;
                }
            case TokenKind.CharLiteral:
                {
                    string raw = node.Token.Text;
                    char ch;
                    if (raw.Length >= 3 && raw[1] == '\\')
                    {
                        ch = raw[2] switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            'r' => '\r',
                            '0' => '\0',
                            '\\' => '\\',
                            '\'' => '\'',
                            _ => raw[2]
                        };
                    }
                    else if (raw.Length >= 3)
                    {
                        ch = raw[1];
                    }
                    else
                    {
                        ch = '\0';
                    }
                    Push(((int)ch).ToString());
                    break;
                }
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
        List<string> argValues = new();
        List<string> argTypes = new();
        foreach (AstNode arg in node.Arguments)
        {
            arg.Accept(this);
            argValues.Add(Pop());
            TypeExpression argType = _typeChecker.GetType(arg);
            argTypes.Add(EmitType(argType));
        }

        string funcName;
        AstNode? target = _typeChecker.GetResolvedCall(node);
        if (target is ExternDeclaration ext)
        {
            funcName = ext.Name;
        }
        else if (target is MethodDeclaration method)
        {
            if (method.Name == "main")
            {
                funcName = "main";
            }
            else
            {
                string ns = _typeChecker.GetFunctionNamespace(method);
                funcName = ns.Length > 0 ? $"gflat${ns}${method.Name}" : $"gflat${method.Name}";
            }
        }
        else if (node.Callee is IdentifierExpression ident)
        {
            if (ident.Name == "main")
                funcName = "main";
            else if (IsExtern(ident.Name))
                funcName = ident.Name;
            else if (_currentNamespacePath.Length > 0)
                funcName = $"gflat${_currentNamespacePath}${ident.Name}";
            else
                funcName = $"gflat${ident.Name}";
        }
        else if (node.Callee is NamespaceAccessExpression nsAccess)
        {
            funcName = ResolveCallMangledName(nsAccess);
        }
        else
        {
            throw new NotImplementedException("Complex callee not supported");
        }

        TypeExpression callType = _typeChecker.GetType(node);
        string retType = EmitType(callType);
        string args = string.Join(", ", argValues.Zip(argTypes, (v, t) => $"{t} {v}"));

        if (retType == "void")
        {
            Emit($"    call void @{funcName}({args})");
        }
        else
        {
            string temp = NewTemp();
            Emit($"    {temp} = call {retType} @{funcName}({args})");
            Push(temp);
        }
    }

    private string ResolveCallMangledName(NamespaceAccessExpression node)
    {
        if (node.Left is GlobalExpression)
            return $"gflat${node.Member}";

        string left;
        if (node.Left is IdentifierExpression ident)
            left = $"gflat${_currentNamespacePath}${ident.Name}";
        else if (node.Left is NamespaceAccessExpression nested)
            left = ResolveCallMangledName(nested);
        else
            throw new NotImplementedException();

        return $"{left}${node.Member}";
    }

    public void Visit(MemberAccessExpression node)
    {
        EmitMemberAddress(node);
        string fieldPtr = Pop();
        TypeExpression fieldType = _typeChecker.GetType(node);
        string llvmFieldType = EmitType(fieldType);
        string val = NewTemp();
        Emit($"    {val} = load {llvmFieldType}, {llvmFieldType}* {fieldPtr}");
        Push(val);
    }

    public void Visit(AssignmentExpression node)
    {
        node.Value.Accept(this);
        string val = Pop();

        EmitAddress(node.Target);
        string ptr = Pop();

        TypeExpression targetType = _typeChecker.GetType(node.Target);
        string llvmType = EmitType(targetType);

        Emit($"    store {llvmType} {val}, {llvmType}* {ptr}");
        Push(val);
    }

    public void Visit(InterpolatedStringExpression node) => throw new NotImplementedException();
    public void Visit(NewExpression node) => throw new NotImplementedException();
    public void Visit(NamespaceAccessExpression node) => throw new NotImplementedException();
    public void Visit(NamedTypeExpression node) => throw new NotImplementedException();
    public void Visit(PointerTypeExpression node) => throw new NotImplementedException();
    public void Visit(ManagedTypeExpression node) => throw new NotImplementedException();
    public void Visit(ArrayTypeExpression node) => throw new NotImplementedException();
    public void Visit(BreakStatement node)
    {
        if (_breakLabels.Count == 0)
            throw new Exception("break outside of loop");
        Emit($"    br label %{_breakLabels.Peek()}");
    }
    public void Visit(ContinueStatement node)
    {
        if (_continueLabels.Count == 0)
            throw new Exception("continue outside of loop");
        Emit($"    br label %{_continueLabels.Peek()}");
    }
    public void Visit(AttributeNode node) { }
    public void Visit(GlobalExpression node) { }
}