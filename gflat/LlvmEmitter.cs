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
    private TypeChecker.StructInfo? _currentStruct = null;
    private string _currentFunctionReturnType = "";

    private readonly List<List<AstNode>> _deferScopes = new();
    private readonly Stack<int> _loopDeferDepths = new();
    private bool _hasTerminated = false;

    private void EmitDefersDownTo(int targetDepth)
    {
        List<AstNode> toEmit = new();
        for (int scopeIdx = _deferScopes.Count - 1; scopeIdx >= targetDepth; scopeIdx--)
        {
            var scope = _deferScopes[scopeIdx];
            for (int i = scope.Count - 1; i >= 0; i--)
            {
                toEmit.Add(scope[i]);
            }
        }
        foreach (var stmt in toEmit)
        {
            stmt.Accept(this);
        }
    }

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
            if (_locals.TryGetValue(ident.Name, out string? ptr))
            {
                Push(ptr);
                return;
            }

            if (_currentStruct != null && _locals.TryGetValue("this", out string? thisPtr))
            {
                int idx = _currentStruct.FieldIndex(ident.Name);
                if (idx >= 0)
                {
                    string loadedThis = NewTemp();
                    Emit($"    {loadedThis} = load %{_currentStruct.Name}*, %{_currentStruct.Name}** {thisPtr}");
                    string fieldPtr = NewTemp();
                    Emit($"    {fieldPtr} = getelementptr %{_currentStruct.Name}, %{_currentStruct.Name}* {loadedThis}, i32 0, i32 {idx}");
                    Push(fieldPtr);
                    return;
                }
            }

            throw new Exception($"Unknown variable '{ident.Name}'");
        }
        else if (node is MemberAccessExpression member)
        {
            if (member.IsArrow)
                throw new Exception($"Cannot take address of pointer property '->{member.Member}'");
            EmitMemberAddress(member);
        }
        else if (node is IndexExpression idx)
        {
            EmitIndexAddress(idx);
        }
        else if (node is UnaryExpression { Operator: TokenKind.Star } deref)
        {
            deref.Operand.Accept(this);
        }
        else
            throw new NotImplementedException($"Cannot take address of {node.GetType().Name}");
    }

    private void EmitIndexAddress(IndexExpression node)
    {
        node.Target.Accept(this);
        string targetPtr = Pop();
        TypeExpression targetType = _typeChecker.GetType(node.Target);
        TypeExpression elemType = targetType is ArrayTypeExpression a ? a.ElementType : ((PointerTypeExpression)targetType).Inner;
        string llvmElemType = EmitType(elemType);

        node.Index.Accept(this);
        string idxVal = Pop();
        TypeExpression idxType = _typeChecker.GetType(node.Index);
        string llvmIdxType = EmitType(idxType);

        string elemPtr = NewTemp();
        Emit($"    {elemPtr} = getelementptr {llvmElemType}, {llvmElemType}* {targetPtr}, {llvmIdxType} {idxVal}");
        Push(elemPtr);
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
        type = _typeChecker.ResolveAlias(type);
        if (type is NamedTypeExpression named)
        {
            TypeChecker.EnumInfo? enumInfo = _typeChecker.ResolveEnum(named);
            if (enumInfo != null)
                return EmitType(enumInfo.UnderlyingType);

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
        if (type is ManagedTypeExpression mgd)
        {
            if (mgd.Inner is NamedTypeExpression { Name: "void" })
                return "i8*";

            return EmitType(mgd.Inner) + "*";
        }
        if (type is ArrayTypeExpression arr)
        {
            string elemType = EmitType(arr.ElementType);
            if (arr.Size.HasValue)
                return $"[{arr.Size.Value} x {elemType}]";
            return elemType + "*";
        }
        if (type is FunctionPointerTypeExpression fnPtr)
        {
            string ret = EmitType(fnPtr.ReturnType);
            string paramTypes = string.Join(", ", fnPtr.ParameterTypes.Select(EmitParamType));
            return $"{ret} ({paramTypes})*";
        }

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

        TypeChecker.StructInfo? prevStruct = _currentStruct;
        _currentStruct = _typeChecker.GetStruct(node.Name);

        foreach (AstNode member in node.Members)
        {
            if (member is MethodDeclaration method)
            {
                EmitStructMethod(node.Name, method);
            }
        }

        _currentStruct = prevStruct;
    }

    private string EmitParamType(TypeExpression type) =>
        type is ArrayTypeExpression a ? EmitType(a.ElementType) + "*" : EmitType(type);

    private void EmitStructMethod(string structName, MethodDeclaration node)
    {
        _locals.Clear();
        _tempCounter = 0;

        string returnType = EmitType(node.ReturnType);
        string ns = _currentNamespacePath;
        string mangledName = ns.Length > 0
            ? $"gflat${ns}${structName}${node.Name}"
            : $"gflat${structName}${node.Name}";

        List<string> paramList = new() { $"%{structName}* %this" };
        foreach (Parameter p in node.Parameters)
            paramList.Add($"{EmitParamType(p.Type)} %{p.Name}");

        string parameters = string.Join(", ", paramList);

        Emit($"define {returnType} @{mangledName}({parameters}) {{");
        Emit("entry:");

        string thisPtr = NewTemp();
        Emit($"    {thisPtr} = alloca %{structName}*");
        Emit($"    store %{structName}* %this, %{structName}** {thisPtr}");
        _locals["this"] = thisPtr;

        foreach (Parameter p in node.Parameters)
        {
            string type = EmitParamType(p.Type);
            string ptr = NewTemp();
            Emit($"    {ptr} = alloca {type}");
            Emit($"    store {type} %{p.Name}, {type}* {ptr}");
            _locals[p.Name] = ptr;
        }

        node.Body.Accept(this);

        if (returnType == "void")
            Emit("    ret void");

        Emit("}");
        Emit("");
    }

    public void Visit(InterfaceDeclaration node) => throw new NotImplementedException();

    public void Visit(FieldDeclaration node) => throw new NotImplementedException();

    public void Visit(ExternDeclaration node)
    {
        _externNames.Add(node.Name);
        string returnType = EmitType(node.ReturnType);
        string parameters = string.Join(", ", node.Parameters.Select(p => EmitParamType(p.Type)));
        if (node.IsVariadic)
            parameters = parameters.Length > 0 ? parameters + ", ..." : "...";
        EmitGlobal($"declare {returnType} @{node.Name}({parameters})");
    }

    public void Visit(MethodDeclaration node)
    {
        _locals.Clear();
        _tempCounter = 0;

        string returnType = EmitType(node.ReturnType);
        _currentFunctionReturnType = returnType;
        string name;
        if (node.Name == "main")
            name = "main";
        else if (_currentNamespacePath.Length > 0)
            name = $"gflat${_currentNamespacePath}${node.Name}";
        else
            name = $"gflat${node.Name}";

        string parameters = string.Join(", ", node.Parameters.Select(p =>
            $"{EmitParamType(p.Type)} %{p.Name}"));

        Emit($"define {returnType} @{name}({parameters}) {{");
        Emit("entry:");

        foreach (Parameter p in node.Parameters)
        {
            string type = EmitParamType(p.Type);
            string ptr = NewTemp();
            Emit($"    {ptr} = alloca {type}");
            Emit($"    store {type} %{p.Name}, {type}* {ptr}");
            _locals[p.Name] = ptr;
        }

        _hasTerminated = false;
        node.Body.Accept(this);

        // emit ret void if void function has no explicit return
        if (returnType == "void" && !_hasTerminated)
            Emit("    ret void");

        Emit("}");
        Emit("");
    }

    public void Visit(ConstructorDeclaration node) => throw new NotImplementedException();
    public void Visit(Parameter node) => throw new NotImplementedException();

    public void Visit(BlockStatement node)
    {
        _deferScopes.Add(new List<AstNode>());
        bool terminated = false;

        foreach (AstNode statement in node.Statements)
        {
            if (terminated)
                break;

            statement.Accept(this);

            if (_hasTerminated)
                terminated = true;
        }

        var defers = _deferScopes[^1];
        _deferScopes.RemoveAt(_deferScopes.Count - 1);

        if (!terminated)
        {
            for (int i = defers.Count - 1; i >= 0; i--)
            {
                defers[i].Accept(this);
            }
        }
    }

    public void Visit(ReturnStatement node)
    {
        string? val = null;
        string? llvmReturnType = null;

        if (node.Value != null)
        {
            node.Value.Accept(this);
            val = Pop();
            TypeExpression returnType = _typeChecker.GetType(node.Value);
            llvmReturnType = EmitType(returnType);

            if (_currentFunctionReturnType.Length > 0 && llvmReturnType != _currentFunctionReturnType)
            {
                if (llvmReturnType == "i8" && _currentFunctionReturnType == "i32")
                {
                    string promoted = NewTemp();
                    Emit($"    {promoted} = sext i8 {val} to i32");
                    val = promoted;
                    llvmReturnType = "i32";
                }
                else if (llvmReturnType.EndsWith("*") && _currentFunctionReturnType.EndsWith("*"))
                {
                    string castVal = NewTemp();
                    Emit($"    {castVal} = bitcast {llvmReturnType} {val} to {_currentFunctionReturnType}");
                    val = castVal;
                    llvmReturnType = _currentFunctionReturnType;
                }
            }
        }

        EmitDefersDownTo(0);

        if (val == null)
            Emit("    ret void");
        else
            Emit($"    ret {llvmReturnType} {val}");

        _hasTerminated = true;
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
        _hasTerminated = false;
        node.Then.Accept(this);
        bool thenTerminated = _hasTerminated;
        if (!thenTerminated)
            Emit($"    br label %{mergeLabel}");

        bool elseTerminated = false;
        if (node.Else != null)
        {
            Emit($"{elseLabel}:");
            _hasTerminated = false;
            node.Else.Accept(this);
            elseTerminated = _hasTerminated;
            if (!elseTerminated)
                Emit($"    br label %{mergeLabel}");
        }

        Emit($"{mergeLabel}:");
        _hasTerminated = (node.Else != null && thenTerminated && elseTerminated);
    }

    public void Visit(WhileStatement node)
    {
        string condLabel = NewLabel("while_cond");
        string bodyLabel = NewLabel("while_body");
        string exitLabel = NewLabel("while_exit");

        _breakLabels.Push(exitLabel);
        _continueLabels.Push(condLabel);
        _loopDeferDepths.Push(_deferScopes.Count);

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
        _hasTerminated = false;
        node.Body.Accept(this);
        if (!_hasTerminated)
            Emit($"    br label %{condLabel}");
        Emit($"{exitLabel}:");

        _breakLabels.Pop();
        _continueLabels.Pop();
        _loopDeferDepths.Pop();
        _hasTerminated = false;
    }

    public void Visit(ForStatement node)
    {
        string condLabel = NewLabel("for_cond");
        string bodyLabel = NewLabel("for_body");
        string incLabel = NewLabel("for_inc");
        string exitLabel = NewLabel("for_exit");

        _breakLabels.Push(exitLabel);
        _continueLabels.Push(incLabel);
        _loopDeferDepths.Push(_deferScopes.Count);

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
        _hasTerminated = false;
        node.Body.Accept(this);
        if (!_hasTerminated)
            Emit($"    br label %{incLabel}");

        Emit($"{incLabel}:");
        if (node.Increment != null)
            node.Increment.Accept(this);
        Emit($"    br label %{condLabel}");
        Emit($"{exitLabel}:");

        _breakLabels.Pop();
        _continueLabels.Pop();
        _loopDeferDepths.Pop();
        _hasTerminated = false;
    }

    public void Visit(VariableDeclaration node)
    {
        TypeExpression resolvedVarType = _typeChecker.GetType(node);
        string type = EmitType(resolvedVarType);
        string ptr = NewTemp();
        Emit($"    {ptr} = alloca {type}");
        _locals[node.Name] = ptr;

        if (node.Initializer != null)
        {
            if (resolvedVarType is ArrayTypeExpression { Size: not null } targetArr &&
                node.Initializer is LiteralExpression { Token.Kind: TokenKind.StringLiteral } strLit)
            {
                string raw = strLit.Token.Text[1..^1];
                string escaped = raw.Replace("\\n", "\n").Replace("\\t", "\t");
                string globalName = NewGlobal();
                int litLen = escaped.Length + 1;
                string llvmStr = escaped.Replace("\n", "\\0A").Replace("\t", "\\09");
                EmitGlobal($"{globalName} = private constant [{litLen} x i8] c\"{llvmStr}\\00\"");

                if (targetArr.Size.Value == litLen)
                {
                    string loadedVal = NewTemp();
                    Emit($"    {loadedVal} = load [{litLen} x i8], [{litLen} x i8]* {globalName}");
                    Emit($"    store [{litLen} x i8] {loadedVal}, [{litLen} x i8]* {ptr}");
                }
                else
                {
                    if (!_externNames.Contains("llvm.memcpy"))
                    {
                        _externNames.Add("llvm.memcpy");
                        EmitGlobal("declare void @llvm.memcpy.p0i8.p0i8.i64(i8* noalias nocapture writeonly, i8* noalias nocapture readonly, i64, i1 immarg)");
                    }
                    string destPtr = NewTemp();
                    Emit($"    {destPtr} = bitcast {type}* {ptr} to i8*");
                    string srcPtr = NewTemp();
                    Emit($"    {srcPtr} = bitcast [{litLen} x i8]* {globalName} to i8*");
                    Emit($"    call void @llvm.memcpy.p0i8.p0i8.i64(i8* {destPtr}, i8* {srcPtr}, i64 {litLen}, i1 false)");
                }
                return;
            }

            node.Initializer.Accept(this);
            string val = Pop();
            TypeExpression initType = _typeChecker.GetType(node.Initializer);
            string initLlvmType = initType is ArrayTypeExpression arrInit ? EmitType(arrInit.ElementType) + "*" : EmitType(initType);
            if (initLlvmType != type && type.EndsWith("*") && initLlvmType.EndsWith("*"))
            {
                string castVal = NewTemp();
                Emit($"    {castVal} = bitcast {initLlvmType} {val} to {type}");
                val = castVal;
            }
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
        TypeExpression rightType = _typeChecker.GetType(node.Right);
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
            TypeExpression resLeftType = _typeChecker.ResolveAlias(leftType);
            TypeExpression resRightType = _typeChecker.ResolveAlias(rightType);

            if (node.Operator == TokenKind.Plus)
            {
                if (resLeftType is PointerTypeExpression pLeft)
                {
                    string elemType = EmitType(pLeft.Inner);
                    string idxType = EmitType(rightType);
                    Emit($"    {temp} = getelementptr {elemType}, {elemType}* {left}, {idxType} {right}");
                    Push(temp);
                    return;
                }
                if (resRightType is PointerTypeExpression pRight)
                {
                    string elemType = EmitType(pRight.Inner);
                    string idxType = EmitType(leftType);
                    Emit($"    {temp} = getelementptr {elemType}, {elemType}* {right}, {idxType} {left}");
                    Push(temp);
                    return;
                }
                if (resLeftType is FunctionPointerTypeExpression fnLeft)
                {
                    string fnTypeStr = EmitType(fnLeft);
                    string idxType = EmitType(rightType);
                    string castPtr = NewTemp();
                    Emit($"    {castPtr} = bitcast {fnTypeStr} {left} to i8**");
                    string gepPtr = NewTemp();
                    Emit($"    {gepPtr} = getelementptr i8*, i8** {castPtr}, {idxType} {right}");
                    Emit($"    {temp} = bitcast i8** {gepPtr} to {fnTypeStr}");
                    Push(temp);
                    return;
                }
                if (resRightType is FunctionPointerTypeExpression fnRight)
                {
                    string fnTypeStr = EmitType(fnRight);
                    string idxType = EmitType(leftType);
                    string castPtr = NewTemp();
                    Emit($"    {castPtr} = bitcast {fnTypeStr} {right} to i8**");
                    string gepPtr = NewTemp();
                    Emit($"    {gepPtr} = getelementptr i8*, i8** {castPtr}, {idxType} {left}");
                    Emit($"    {temp} = bitcast i8** {gepPtr} to {fnTypeStr}");
                    Push(temp);
                    return;
                }
            }
            else if (node.Operator == TokenKind.Minus)
            {
                if (resLeftType is PointerTypeExpression pLeft && resRightType is PointerTypeExpression pRight)
                {
                    string elemType = EmitType(pLeft.Inner);
                    string p1Int = NewTemp();
                    string p2Int = NewTemp();
                    Emit($"    {p1Int} = ptrtoint {EmitType(pLeft)} {left} to i64");
                    Emit($"    {p2Int} = ptrtoint {EmitType(pRight)} {right} to i64");
                    string diffBytes = NewTemp();
                    Emit($"    {diffBytes} = sub i64 {p1Int}, {p2Int}");
                    string sizePtr = NewTemp();
                    Emit($"    {sizePtr} = getelementptr {elemType}, {elemType}* null, i32 1");
                    string sizeInt = NewTemp();
                    Emit($"    {sizeInt} = ptrtoint {elemType}* {sizePtr} to i64");
                    Emit($"    {temp} = sdiv i64 {diffBytes}, {sizeInt}");
                    Push(temp);
                    return;
                }
                if (resLeftType is FunctionPointerTypeExpression fnLeft && resRightType is FunctionPointerTypeExpression fnRight)
                {
                    string p1Int = NewTemp();
                    string p2Int = NewTemp();
                    Emit($"    {p1Int} = ptrtoint {EmitType(fnLeft)} {left} to i64");
                    Emit($"    {p2Int} = ptrtoint {EmitType(fnRight)} {right} to i64");
                    string diffBytes = NewTemp();
                    Emit($"    {diffBytes} = sub i64 {p1Int}, {p2Int}");
                    Emit($"    {temp} = sdiv i64 {diffBytes}, 8");
                    Push(temp);
                    return;
                }
                if (resLeftType is PointerTypeExpression pLeftSingle)
                {
                    string elemType = EmitType(pLeftSingle.Inner);
                    string idxType = EmitType(rightType);
                    string negIdx = NewTemp();
                    Emit($"    {negIdx} = sub {idxType} 0, {right}");
                    Emit($"    {temp} = getelementptr {elemType}, {elemType}* {left}, {idxType} {negIdx}");
                    Push(temp);
                    return;
                }
                if (resLeftType is FunctionPointerTypeExpression fnLeftSingle)
                {
                    string fnTypeStr = EmitType(fnLeftSingle);
                    string idxType = EmitType(rightType);
                    string negIdx = NewTemp();
                    Emit($"    {negIdx} = sub {idxType} 0, {right}");
                    string castPtr = NewTemp();
                    Emit($"    {castPtr} = bitcast {fnTypeStr} {left} to i8**");
                    string gepPtr = NewTemp();
                    Emit($"    {gepPtr} = getelementptr i8*, i8** {castPtr}, {idxType} {negIdx}");
                    Emit($"    {temp} = bitcast i8** {gepPtr} to {fnTypeStr}");
                    Push(temp);
                    return;
                }
            }

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
                    TokenKind.Pipe => "or",
                    TokenKind.Ampersand => "and",
                    TokenKind.Caret => "xor",
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
            AstNode? fnTarget = _typeChecker.GetFunctionAddressTarget(node);
            if (fnTarget != null)
            {
                if (fnTarget is ExternDeclaration ext)
                {
                    Push($"@{ext.Name}");
                    return;
                }
                if (fnTarget is MethodDeclaration method)
                {
                    if (method.Name == "main")
                    {
                        Push("@main");
                        return;
                    }
                    string ns = _typeChecker.GetFunctionNamespace(method);
                    string mangled = ns.Length > 0 ? $"gflat${ns}${method.Name}" : $"gflat${method.Name}";
                    Push($"@{mangled}");
                    return;
                }
            }

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
                    string temp;
                    int step = node.Operator == TokenKind.PlusPlus ? 1 : -1;
                    TypeExpression resType = _typeChecker.ResolveAlias(type);
                    if (resType is PointerTypeExpression ptrType)
                    {
                        string elemType = EmitType(ptrType.Inner);
                        temp = NewTemp();
                        Emit($"    {temp} = getelementptr {elemType}, {elemType}* {operand}, i32 {step}");
                    }
                    else if (resType is FunctionPointerTypeExpression fnPtrType)
                    {
                        string fnPtrTypeStr = EmitType(fnPtrType);
                        string castPtr = NewTemp();
                        Emit($"    {castPtr} = bitcast {fnPtrTypeStr} {operand} to i8**");
                        string gepPtr = NewTemp();
                        Emit($"    {gepPtr} = getelementptr i8*, i8** {castPtr}, i32 {step}");
                        temp = NewTemp();
                        Emit($"    {temp} = bitcast i8** {gepPtr} to {fnPtrTypeStr}");
                    }
                    else
                    {
                        string op = node.Operator == TokenKind.PlusPlus ? "add" : "sub";
                        temp = NewTemp();
                        Emit($"    {temp} = {op} {llvmType} {operand}, 1");
                    }

                    EmitAddress(node.Operand);
                    string targetSlot = Pop();
                    Emit($"    store {llvmType} {temp}, {llvmType}* {targetSlot}");

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
            if (type is ArrayTypeExpression arr && arr.Size.HasValue)
            {
                string arrType = EmitType(type);
                string decayed = NewTemp();
                Emit($"    {decayed} = getelementptr {arrType}, {arrType}* {ptr}, i32 0, i32 0");
                Push(decayed);
                return;
            }

            string llvmType = EmitType(type);
            string temp = NewTemp();
            Emit($"    {temp} = load {llvmType}, {llvmType}* {ptr}");
            Push(temp);
            return;
        }

        if (_currentStruct != null && _locals.TryGetValue("this", out string? thisPtr))
        {
            int idx = _currentStruct.FieldIndex(node.Name);
            if (idx >= 0)
            {
                string loadedThis = NewTemp();
                Emit($"    {loadedThis} = load %{_currentStruct.Name}*, %{_currentStruct.Name}** {thisPtr}");
                string fieldPtr = NewTemp();
                Emit($"    {fieldPtr} = getelementptr %{_currentStruct.Name}, %{_currentStruct.Name}* {loadedThis}, i32 0, i32 {idx}");
                TypeExpression type = _typeChecker.GetType(node);
                string llvmType = EmitType(type);
                string temp = NewTemp();
                Emit($"    {temp} = load {llvmType}, {llvmType}* {fieldPtr}");
                Push(temp);
                return;
            }
        }

        throw new Exception($"Unknown identifier '{node.Name}'");
    }

    public void Visit(CallExpression node)
    {
        if (node.Callee is MemberAccessExpression { IsArrow: true, Member: "free" } freeAccess)
        {
            if (!_externNames.Contains("free"))
            {
                _externNames.Add("free");
                EmitGlobal("declare void @free(i8*)");
            }

            freeAccess.Object.Accept(this);
            string ptrVal = Pop();
            TypeExpression ptrType = _typeChecker.GetType(freeAccess.Object);
            string llvmPtrType = EmitType(ptrType);

            string castPtr;
            if (llvmPtrType != "i8*")
            {
                castPtr = NewTemp();
                Emit($"    {castPtr} = bitcast {llvmPtrType} {ptrVal} to i8*");
            }
            else
            {
                castPtr = ptrVal;
            }

            Emit($"    call void @free(i8* {castPtr})");
            return;
        }

        if (_typeChecker.IsIndirectCall(node))
        {
            node.Callee.Accept(this);
            string fnVal = Pop();

            TypeExpression calleeType = _typeChecker.ResolveAlias(_typeChecker.GetType(node.Callee));
            FunctionPointerTypeExpression fnPtr = (FunctionPointerTypeExpression)calleeType;

            List<string> argVals = new();
            List<string> argTypesList = new();
            for (int i = 0; i < node.Arguments.Count; i++)
            {
                AstNode arg = node.Arguments[i];
                arg.Accept(this);
                string val = Pop();
                TypeExpression argType = _typeChecker.GetType(arg);
                string llvmArgType = argType is ArrayTypeExpression a ? EmitType(a.ElementType) + "*" : EmitType(argType);

                if (i < fnPtr.ParameterTypes.Count)
                {
                    string paramType = EmitParamType(fnPtr.ParameterTypes[i]);
                    if (paramType != llvmArgType && paramType.EndsWith("*") && llvmArgType.EndsWith("*"))
                    {
                        string castVal = NewTemp();
                        Emit($"    {castVal} = bitcast {llvmArgType} {val} to {paramType}");
                        val = castVal;
                        llvmArgType = paramType;
                    }
                }

                argVals.Add(val);
                argTypesList.Add(llvmArgType);
            }

            TypeExpression indirectCallType = _typeChecker.GetType(node);
            string indirectRetType = EmitType(indirectCallType);
            string indirectArgs = string.Join(", ", argVals.Zip(argTypesList, (v, t) => $"{t} {v}"));

            if (indirectRetType == "void")
            {
                Emit($"    call void {fnVal}({indirectArgs})");
            }
            else
            {
                string temp = NewTemp();
                Emit($"    {temp} = call {indirectRetType} {fnVal}({indirectArgs})");
                Push(temp);
            }
            return;
        }

        List<string> argValues = new();
        List<string> argTypes = new();

        string funcName;
        AstNode? target = _typeChecker.GetResolvedCall(node);

        if (node.Callee is MemberAccessExpression memberAccess && target is MethodDeclaration structMethod)
        {
            TypeExpression objType = _typeChecker.GetType(memberAccess.Object);
            string thisVal;
            string thisType;

            if (objType is PointerTypeExpression ptrType)
            {
                memberAccess.Object.Accept(this);
                thisVal = Pop();
                thisType = EmitType(ptrType);
            }
            else
            {
                EmitAddress(memberAccess.Object);
                thisVal = Pop();
                thisType = EmitType(objType) + "*";
            }

            argValues.Add(thisVal);
            argTypes.Add(thisType);

            foreach (AstNode arg in node.Arguments)
            {
                arg.Accept(this);
                argValues.Add(Pop());
                TypeExpression argType = _typeChecker.GetType(arg);
                string llvmArgType = argType is ArrayTypeExpression a ? EmitType(a.ElementType) + "*" : EmitType(argType);
                argTypes.Add(llvmArgType);
            }

            string structName = ((NamedTypeExpression)(objType is PointerTypeExpression p ? p.Inner : objType)).Name;
            string ns = _typeChecker.GetFunctionNamespace(structMethod);
            funcName = ns.Length > 0 
                ? $"gflat${ns}${structName}${structMethod.Name}" 
                : $"gflat${structName}${structMethod.Name}";
        }
        else
        {
            for (int i = 0; i < node.Arguments.Count; i++)
            {
                AstNode arg = node.Arguments[i];
                arg.Accept(this);
                string val = Pop();
                TypeExpression argType = _typeChecker.GetType(arg);
                string llvmArgType = argType is ArrayTypeExpression a ? EmitType(a.ElementType) + "*" : EmitType(argType);

                if (target is MethodDeclaration methodTarget && i < methodTarget.Parameters.Count)
                {
                    string paramType = EmitParamType(methodTarget.Parameters[i].Type);
                    if (paramType != llvmArgType && paramType.EndsWith("*") && llvmArgType.EndsWith("*"))
                    {
                        string castVal = NewTemp();
                        Emit($"    {castVal} = bitcast {llvmArgType} {val} to {paramType}");
                        val = castVal;
                        llvmArgType = paramType;
                    }
                }
                else if (target is ExternDeclaration extDecl)
                {
                    if (i < extDecl.Parameters.Count)
                    {
                        string paramType = EmitParamType(extDecl.Parameters[i].Type);
                        if (paramType != llvmArgType && paramType.EndsWith("*") && llvmArgType.EndsWith("*"))
                        {
                            string castVal = NewTemp();
                            Emit($"    {castVal} = bitcast {llvmArgType} {val} to {paramType}");
                            val = castVal;
                            llvmArgType = paramType;
                        }
                    }
                    else if (extDecl.IsVariadic)
                    {
                        if (llvmArgType == "i1")
                        {
                            string promoted = NewTemp();
                            Emit($"    {promoted} = zext i1 {val} to i32");
                            val = promoted;
                            llvmArgType = "i32";
                        }
                        else if (llvmArgType == "i8")
                        {
                            string promoted = NewTemp();
                            Emit($"    {promoted} = sext i8 {val} to i32");
                            val = promoted;
                            llvmArgType = "i32";
                        }
                    }
                }

                argValues.Add(val);
                argTypes.Add(llvmArgType);
            }

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
        if (_typeChecker.TryGetEnumMember(node, out long enumVal, out _))
        {
            Push(enumVal.ToString());
            return;
        }

        if (node.IsArrow)
        {
            node.Object.Accept(this);
            string ptrVal = Pop();
            TypeExpression objType = _typeChecker.GetType(node.Object);
            string llvmPtrType = EmitType(objType);

            if (node.Member == "address")
            {
                string intVal = NewTemp();
                Emit($"    {intVal} = ptrtoint {llvmPtrType} {ptrVal} to i64");
                Push(intVal);
                return;
            }
            else if (node.Member == "is_null")
            {
                string boolVal = NewTemp();
                Emit($"    {boolVal} = icmp eq {llvmPtrType} {ptrVal}, null");
                Push(boolVal);
                return;
            }
            else
            {
                throw new NotImplementedException($"Pointer operation '->{node.Member}' not supported as expression");
            }
        }

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
        bool isFloat = targetType is NamedTypeExpression { Name: "float" };

        string finalVal = val;
        TypeExpression valueType = _typeChecker.GetType(node.Value);
        string valueLlvmType = valueType is ArrayTypeExpression arrVal ? EmitType(arrVal.ElementType) + "*" : EmitType(valueType);
        if (valueLlvmType != llvmType && llvmType.EndsWith("*") && valueLlvmType.EndsWith("*"))
        {
            string castVal = NewTemp();
            Emit($"    {castVal} = bitcast {valueLlvmType} {finalVal} to {llvmType}");
            finalVal = castVal;
        }

        if (node.Operator != TokenKind.Equals)
        {
            string currentVal = NewTemp();
            Emit($"    {currentVal} = load {llvmType}, {llvmType}* {ptr}");
            string temp = NewTemp();

            TypeExpression resTargetType = _typeChecker.ResolveAlias(targetType);
            if (resTargetType is PointerTypeExpression ptrType && node.Operator is TokenKind.PlusEquals or TokenKind.MinusEquals)
            {
                string elemType = EmitType(ptrType.Inner);
                string idxType = EmitType(valueType);
                string stepVal = val;
                if (node.Operator == TokenKind.MinusEquals)
                {
                    string negVal = NewTemp();
                    Emit($"    {negVal} = sub {idxType} 0, {val}");
                    stepVal = negVal;
                }
                Emit($"    {temp} = getelementptr {elemType}, {elemType}* {currentVal}, {idxType} {stepVal}");
            }
            else if (resTargetType is FunctionPointerTypeExpression fnPtrType && node.Operator is TokenKind.PlusEquals or TokenKind.MinusEquals)
            {
                string fnPtrTypeStr = EmitType(fnPtrType);
                string idxType = EmitType(valueType);
                string stepVal = val;
                if (node.Operator == TokenKind.MinusEquals)
                {
                    string negVal = NewTemp();
                    Emit($"    {negVal} = sub {idxType} 0, {val}");
                    stepVal = negVal;
                }
                string castPtr = NewTemp();
                Emit($"    {castPtr} = bitcast {fnPtrTypeStr} {currentVal} to i8**");
                string gepPtr = NewTemp();
                Emit($"    {gepPtr} = getelementptr i8*, i8** {castPtr}, {idxType} {stepVal}");
                Emit($"    {temp} = bitcast i8** {gepPtr} to {fnPtrTypeStr}");
            }
            else if (isFloat)
            {
                string op = node.Operator switch
                {
                    TokenKind.PlusEquals => "fadd",
                    TokenKind.MinusEquals => "fsub",
                    TokenKind.StarEquals => "fmul",
                    TokenKind.SlashEquals => "fdiv",
                    _ => throw new NotImplementedException($"Compound assignment {node.Operator} not supported on float")
                };
                Emit($"    {temp} = {op} float {currentVal}, {val}");
            }
            else
            {
                string op = node.Operator switch
                {
                    TokenKind.PlusEquals => "add",
                    TokenKind.MinusEquals => "sub",
                    TokenKind.StarEquals => "mul",
                    TokenKind.SlashEquals => "sdiv",
                    TokenKind.PercentEquals => "srem",
                    _ => throw new NotImplementedException($"Compound assignment {node.Operator} not supported")
                };
                Emit($"    {temp} = {op} {llvmType} {currentVal}, {val}");
            }
            finalVal = temp;
        }

        Emit($"    store {llvmType} {finalVal}, {llvmType}* {ptr}");
        Push(finalVal);
    }

    public void Visit(InterpolatedStringExpression node) => throw new NotImplementedException();
    public void Visit(NewExpression node) => throw new NotImplementedException();
    public void Visit(NamespaceAccessExpression node)
    {
        if (_typeChecker.TryGetEnumMember(node, out long val, out _))
        {
            Push(val.ToString());
            return;
        }
        throw new NotImplementedException();
    }
    public void Visit(NamedTypeExpression node) => throw new NotImplementedException();
    public void Visit(PointerTypeExpression node) => throw new NotImplementedException();
    public void Visit(ManagedTypeExpression node) => throw new NotImplementedException();
    public void Visit(ArrayTypeExpression node) => throw new NotImplementedException();
    public void Visit(IndexExpression node)
    {
        EmitIndexAddress(node);
        string elemPtr = Pop();
        TypeExpression elemType = _typeChecker.GetType(node);
        string llvmElemType = EmitType(elemType);
        string val = NewTemp();
        Emit($"    {val} = load {llvmElemType}, {llvmElemType}* {elemPtr}");
        Push(val);
    }
    public void Visit(DeferStatement node)
    {
        if (_deferScopes.Count == 0)
            throw new Exception($"defer statement outside of block scope on line {node.Line}");

        _deferScopes[^1].Add(node.Statement);
    }
    public void Visit(BreakStatement node)
    {
        if (_breakLabels.Count == 0)
            throw new Exception("break outside of loop");
        int targetDepth = _loopDeferDepths.Peek();
        EmitDefersDownTo(targetDepth);
        Emit($"    br label %{_breakLabels.Peek()}");
        _hasTerminated = true;
    }
    public void Visit(ContinueStatement node)
    {
        if (_continueLabels.Count == 0)
            throw new Exception("continue outside of loop");
        int targetDepth = _loopDeferDepths.Peek();
        EmitDefersDownTo(targetDepth);
        Emit($"    br label %{_continueLabels.Peek()}");
        _hasTerminated = true;
    }
    public void Visit(AttributeNode node) { }
    public void Visit(GlobalExpression node) { }
    public void Visit(FunctionPointerTypeExpression node) { }
    public void Visit(AliasDeclaration node) { }
    public void Visit(EnumDeclaration node) { }
    public void Visit(EnumMemberDeclaration node) { }
}