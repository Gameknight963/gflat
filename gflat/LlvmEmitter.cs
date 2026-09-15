using System.Text;
using gflat.ast;
using gflat.comptime;

namespace gflat;

public class LlvmEmitter : IVisitor
{
    private readonly TypeChecker _typeChecker;

    public LlvmEmitter(TypeChecker typeChecker)
    {
        _typeChecker = typeChecker;
    }

    private int _constCounter = 0;
    private readonly Dictionary<ConstValue, string> _emittedConstGlobals = new();
    private readonly HashSet<string> _usedGlobalNames = new();

    private string EmitConstInitializer(ConstValue val, TypeExpression type)
    {
        type = _typeChecker.ResolveAlias(type);
        string typeStr = EmitType(type);

        switch (val)
        {
            case ConstValue.Integer i:
                return $"{typeStr} {i.Value}";
            case ConstValue.UInteger u:
                return $"{typeStr} {u.Value}";
            case ConstValue.Float f:
                {
                    string str = f.Value.ToString("0.0######", System.Globalization.CultureInfo.InvariantCulture);
                    if (!str.Contains('.')) str += ".0";
                    return $"{typeStr} {str}";
                }
            case ConstValue.Boolean b:
                return $"i1 {(b.Value ? "1" : "0")}";
            case ConstValue.Char c:
                return $"i8 {(int)c.Value}";
            case ConstValue.String s:
                {
                    string raw = s.Value;
                    string escaped = raw.Replace("\\n", "\n").Replace("\\t", "\t");
                    string globalName = NewGlobal();
                    int len = escaped.Length + 1;
                    string llvmStr = escaped.Replace("\n", "\\0A").Replace("\t", "\\09");
                    EmitGlobal($"{globalName} = private constant [{len} x i8] c\"{llvmStr}\\00\"");
                    return $"i8* bitcast ([{len} x i8]* {globalName} to i8*)";
                }
            case ConstValue.Pointer ptrVal:
                {
                    string targetGlobal = GetOrCreateConstGlobal(ptrVal.Target, ptrVal.GlobalName);
                    string targetLlvmType = $"%{ptrVal.TypeName}";
                    if (typeStr == $"{targetLlvmType}*" || typeStr == targetLlvmType)
                    {
                        return $"{typeStr} {targetGlobal}";
                    }
                    else
                    {
                        return $"{typeStr} bitcast ({targetLlvmType}* {targetGlobal} to {typeStr})";
                    }
                }
            case ConstValue.Struct structVal:
                {
                    TypeChecker.StructInfo? sInfo = _typeChecker.GetStruct(structVal.StructName);
                    if (sInfo == null) throw new Exception($"Unknown struct {structVal.StructName}");
                    List<string> fieldInits = new();
                    foreach ((string Name, TypeExpression Type) fld in sInfo.Fields)
                    {
                        if (structVal.Fields.TryGetValue(fld.Name, out ConstValue? fldVal))
                        {
                            fieldInits.Add(EmitConstInitializer(fldVal, fld.Type));
                        }
                        else
                        {
                            fieldInits.Add($"{EmitType(fld.Type)} zeroinitializer");
                        }
                    }
                    return $"%{structVal.StructName} {{ {string.Join(", ", fieldInits)} }}";
                }
            case ConstValue.ClassInstance classVal:
                {
                    TypeChecker.ClassInfo? cInfo = _typeChecker.GetClass(classVal.ClassName);
                    if (cInfo == null) throw new Exception($"Unknown class {classVal.ClassName}");
                    int vtableSize = cInfo.VirtualMethods.Count;
                    string vtableCast = vtableSize == 0
                        ? $"i8** bitcast ([0 x i8*]* @{classVal.ClassName}$vtable to i8**)"
                        : $"i8** bitcast ([{vtableSize} x i8*]* @{classVal.ClassName}$vtable to i8**)";
                    List<string> fieldInits = new() { vtableCast };
                    foreach ((string Name, TypeExpression Type, TokenKind Accessibility, string DeclaringClass) fld in cInfo.Fields)
                    {
                        if (classVal.Fields.TryGetValue(fld.Name, out ConstValue? fldVal))
                        {
                            fieldInits.Add(EmitConstInitializer(fldVal, fld.Type));
                        }
                        else
                        {
                            fieldInits.Add($"{EmitType(fld.Type)} zeroinitializer");
                        }
                    }
                    return $"%{classVal.ClassName} {{ {string.Join(", ", fieldInits)} }}";
                }
            default:
                throw new Exception($"Cannot create constant initializer for {val.GetType().Name}");
        }
    }

    private string GetOrCreateConstGlobal(ConstValue target, string? preferredName = null)
    {
        if (_emittedConstGlobals.TryGetValue(target, out string? existing))
        {
            return existing;
        }

        string candidate;
        if (!string.IsNullOrEmpty(preferredName))
        {
            candidate = $"@{preferredName}$data";
            if (_usedGlobalNames.Contains(candidate))
            {
                candidate = $"@{preferredName}${_constCounter++}$data";
            }
        }
        else
        {
            candidate = $"@const${_constCounter++}$data";
        }

        _usedGlobalNames.Add(candidate);
        _emittedConstGlobals[target] = candidate;

        if (target is ConstValue.Struct sVal)
        {
            TypeChecker.StructInfo? sInfo = _typeChecker.GetStruct(sVal.StructName);
            if (sInfo == null) throw new Exception($"Unknown struct {sVal.StructName}");
            string init = EmitConstInitializer(sVal, new NamedTypeExpression(sVal.StructName, null, 0));
            EmitGlobal($"{candidate} = internal constant {init}");
            return candidate;
        }
        else if (target is ConstValue.ClassInstance cVal)
        {
            TypeChecker.ClassInfo? cInfo = _typeChecker.GetClass(cVal.ClassName);
            if (cInfo == null) throw new Exception($"Unknown class {cVal.ClassName}");
            string init = EmitConstInitializer(cVal, new NamedTypeExpression(cVal.ClassName, null, 0));
            EmitGlobal($"{candidate} = internal constant {init}");
            return candidate;
        }
        else
        {
            throw new Exception($"Cannot create constant global for {target.GetType().Name}");
        }
    }

    private bool TryEmitConstValue(ConstValue constVal, TypeExpression type)
    {
        switch (constVal)
        {
            case ConstValue.Integer i:
                Push(i.Value.ToString());
                return true;
            case ConstValue.UInteger u:
                Push(u.Value.ToString());
                return true;
            case ConstValue.Float f:
                Push(f.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return true;
            case ConstValue.Boolean b:
                Push(b.Value ? "1" : "0");
                return true;
            case ConstValue.Char c:
                Push(((int)c.Value).ToString());
                return true;
            case ConstValue.String s:
                {
                    string raw = s.Value;
                    string escaped = raw.Replace("\\n", "\n").Replace("\\t", "\t");
                    string globalName = NewGlobal();
                    int len = escaped.Length + 1;
                    string llvmStr = escaped.Replace("\n", "\\0A").Replace("\t", "\\09");
                    EmitGlobal($"{globalName} = private constant [{len} x i8] c\"{llvmStr}\\00\"");
                    string ptr = NewTemp();
                    Emit($"    {ptr} = getelementptr [{len} x i8], [{len} x i8]* {globalName}, i32 0, i32 0");
                    Push(ptr);
                    return true;
                }
            case ConstValue.Pointer ptrVal:
                {
                    string globalName = GetOrCreateConstGlobal(ptrVal.Target, ptrVal.GlobalName);
                    string targetLlvmType = $"%{ptrVal.TypeName}";
                    string expectedLlvmType = EmitType(type);
                    if (expectedLlvmType == $"{targetLlvmType}*" || expectedLlvmType == targetLlvmType)
                    {
                        Push(globalName);
                        return true;
                    }
                    else
                    {
                        string castTemp = NewTemp();
                        Emit($"    {castTemp} = bitcast {targetLlvmType}* {globalName} to {expectedLlvmType}");
                        Push(castTemp);
                        return true;
                    }
                }
            case ConstValue.Struct structVal:
                {
                    TypeChecker.StructInfo? sInfo = _typeChecker.GetStruct(structVal.StructName);
                    if (sInfo != null)
                    {
                        string temp = NewTemp();
                        Emit($"    {temp} = alloca %{structVal.StructName}");
                        Emit($"    store %{structVal.StructName} zeroinitializer, %{structVal.StructName}* {temp}");
                        for (int fIdx = 0; fIdx < sInfo.Fields.Count; fIdx++)
                        {
                            (string Name, TypeExpression Type) fld = sInfo.Fields[fIdx];
                            if (structVal.Fields.TryGetValue(fld.Name, out ConstValue? fldVal))
                            {
                                if (TryEmitConstValue(fldVal, fld.Type))
                                {
                                    string v = Pop();
                                    string fPtr = NewTemp();
                                    Emit($"    {fPtr} = getelementptr %{structVal.StructName}, %{structVal.StructName}* {temp}, i32 0, i32 {fIdx}");
                                    string fType = EmitType(fld.Type);
                                    Emit($"    store {fType} {v}, {fType}* {fPtr}");
                                }
                            }
                        }
                        string val = NewTemp();
                        Emit($"    {val} = load %{structVal.StructName}, %{structVal.StructName}* {temp}");
                        Push(val);
                        return true;
                    }
                    return false;
                }
            default:
                return false;
        }
    }

    private StringBuilder _output = new();
    private readonly StringBuilder _globals = new();
    private readonly StringBuilder _lambdaFunctions = new();
    private int _lambdaCounter = 0;
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
    private TypeChecker.ClassInfo? _currentClass = null;
    private string _currentFunctionReturnType = "";
    private TypeExpression? _currentFunctionExpectedType = null;
    private bool _currentFunctionIsThrowing = false;
    private bool _currentFunctionIsVoid = false;
    private string _currentFunctionBaseReturnType = "";
    private bool _isInsideMain = false;
    private string? _mainErrSlot = null;
    private string? _mainOwnedSlot = null;
    private string? _mainUnhandledLabel = null;

    private record TryBlockInfo(TryStatement Statement, string ErrSlot, string OwnedSlot, string DispatchLabel, int DeferDepth);
    private record ActiveCatchInfo(string Ex, string Owned);
    private readonly Stack<TryBlockInfo> _emitterTryStack = new();
    private readonly Stack<ActiveCatchInfo> _activeCatchStack = new();
    private readonly Dictionary<string, string> _catchVariableOwnedSlots = new();

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

    public string GetOutput() => _globals.ToString() + "\n" + _lambdaFunctions.ToString() + "\n" + _output.ToString();

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

            if (_typeChecker.TryGetConstValueByName(ident.Name, out ConstValue? constVal) && constVal != null)
            {
                if (constVal is ConstValue.Pointer ptrVal)
                {
                    string globalData = GetOrCreateConstGlobal(ptrVal.Target, ptrVal.GlobalName ?? ident.Name);
                    Push(globalData);
                    return;
                }
                if (constVal is ConstValue.Struct or ConstValue.ClassInstance)
                {
                    string globalData = GetOrCreateConstGlobal(constVal, ident.Name);
                    Push(globalData);
                    return;
                }
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

            if (_currentClass != null && _locals.TryGetValue("this", out string? classThisPtr))
            {
                int idx = _currentClass.FieldIndex(ident.Name);
                if (idx >= 0)
                {
                    string loadedThis = NewTemp();
                    Emit($"    {loadedThis} = load %{_currentClass.Name}*, %{_currentClass.Name}** {classThisPtr}");
                    string fieldPtr = NewTemp();
                    Emit($"    {fieldPtr} = getelementptr %{_currentClass.Name}, %{_currentClass.Name}* {loadedThis}, i32 0, i32 {idx + 1}");
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
        if (objType is ManagedTypeExpression mgdType)
            objType = mgdType.Inner;

        string typeName = ((NamedTypeExpression)objType).Name;
        if (_typeChecker.IsClass(typeName))
        {
            TypeChecker.ClassInfo cInfo = _typeChecker.GetClass(typeName)!;
            int fieldIdx = cInfo.FieldIndex(node.Member);
            TypeExpression fieldType = cInfo.Fields[fieldIdx].Type;
            string llvmFieldType = EmitType(fieldType);

            string fieldPtr = NewTemp();
            Emit($"    {fieldPtr} = getelementptr %{typeName}, %{typeName}* {objPtr}, i32 0, i32 {fieldIdx + 1}");
            Push(fieldPtr);
            return;
        }

        string structName = typeName;
        TypeChecker.StructInfo info = _typeChecker.GetStruct(structName)!;
        int sFieldIdx = info.FieldIndex(node.Member);
        TypeExpression sFieldType = info.Fields[sFieldIdx].Type;
        string llvmSFieldType = EmitType(sFieldType);

        string sFieldPtr = NewTemp();
        Emit($"    {sFieldPtr} = getelementptr %{structName}, %{structName}* {objPtr}, i32 0, i32 {sFieldIdx}");
        Push(sFieldPtr);
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
                "byte" or "sbyte" => "i8",
                "short" or "ushort" => "i16",
                "int" or "uint" => "i32",
                "long" or "ulong" or "nint" or "nuint" => "i64",
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
            TypeExpression inner = _typeChecker.ResolveAlias(ptr.Inner);
            if (inner is NamedTypeExpression namedInner && _typeChecker.IsInterface(namedInner))
            {
                return "{ i8*, i8** }";
            }

            // LLVM does not allow void*, use i8* instead
            if (ptr.Inner is NamedTypeExpression { Name: "void" })
                return "i8*";

            return EmitType(ptr.Inner) + "*";
        }
        if (type is ManagedTypeExpression mgd)
        {
            TypeExpression inner = _typeChecker.ResolveAlias(mgd.Inner);
            if (inner is NamedTypeExpression namedInner && _typeChecker.IsInterface(namedInner))
            {
                return "{ i8*, i8** }";
            }

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

    private int GetIntegerBitWidth(TypeExpression type)
    {
        type = _typeChecker.ResolveAlias(type);
        if (type is NamedTypeExpression named)
        {
            TypeChecker.EnumInfo? enumInfo = _typeChecker.ResolveEnum(named);
            if (enumInfo != null)
            {
                return GetIntegerBitWidth(enumInfo.UnderlyingType);
            }

            return named.Name switch
            {
                "bool" => 1,
                "byte" or "sbyte" or "char" => 8,
                "short" or "ushort" => 16,
                "int" or "uint" => 32,
                "long" or "ulong" or "nint" or "nuint" => 64,
                "extralong" => 128,
                _ => 32
            };
        }
        return 32;
    }

    private string EmitCast(string val, TypeExpression srcType, TypeExpression dstType)
    {
        srcType = _typeChecker.ResolveAlias(srcType);
        dstType = _typeChecker.ResolveAlias(dstType);

        if (srcType is NamedTypeExpression srcNamed && _typeChecker.ResolveEnum(srcNamed) is { } srcEnum)
        {
            srcType = srcEnum.UnderlyingType;
        }
        if (dstType is NamedTypeExpression dstNamed && _typeChecker.ResolveEnum(dstNamed) is { } dstEnum)
        {
            dstType = dstEnum.UnderlyingType;
        }

        string srcLlvm = EmitType(srcType);
        string dstLlvm = EmitType(dstType);

        if (srcLlvm == dstLlvm)
        {
            return val;
        }

        if ((val == "null" || val == "zeroinitializer") && dstLlvm == "{ i8*, i8** }")
        {
            return "zeroinitializer";
        }

        // Struct pointer (or Interface pointer) to Interface pointer
        if (dstType is PointerTypeExpression or ManagedTypeExpression)
        {
            TypeExpression dstInner = _typeChecker.ResolveAlias(dstType is PointerTypeExpression dp ? dp.Inner : ((ManagedTypeExpression)dstType).Inner);
            if (dstInner is NamedTypeExpression dstNamedIface && _typeChecker.IsInterface(dstNamedIface))
            {
                TypeExpression srcInner = _typeChecker.ResolveAlias(srcType is PointerTypeExpression sp ? sp.Inner : ((ManagedTypeExpression)srcType).Inner);
                if (srcInner is NamedTypeExpression srcNamedStruct)
                {
                    if (srcNamedStruct.Name == dstNamedIface.Name)
                    {
                        return val;
                    }

                    string dataPtr = NewTemp();
                    Emit($"    {dataPtr} = bitcast {srcLlvm} {val} to i8*");

                    string fat1 = NewTemp();
                    Emit($"    {fat1} = insertvalue {{ i8*, i8** }} undef, i8* {dataPtr}, 0");

                    string vtableGlobal = $"@{srcNamedStruct.Name}${dstNamedIface.Name}$vtable";
                    TypeChecker.InterfaceInfo? ifaceInfo = _typeChecker.GetInterface(dstNamedIface.Name);
                    int methodCount = ifaceInfo != null ? ifaceInfo.Methods.Count : 0;
                    string vtablePtr = NewTemp();
                    if (methodCount > 0)
                    {
                        Emit($"    {vtablePtr} = bitcast [{methodCount} x i8*]* {vtableGlobal} to i8**");
                    }
                    else
                    {
                        Emit($"    {vtablePtr} = bitcast [0 x i8*]* {vtableGlobal} to i8**");
                    }

                    string fat2 = NewTemp();
                    Emit($"    {fat2} = insertvalue {{ i8*, i8** }} {fat1}, i8** {vtablePtr}, 1");
                    return fat2;
                }
            }
        }

        // Pointer to Pointer (or Array to Pointer)
        if ((srcType is PointerTypeExpression or FunctionPointerTypeExpression or ManagedTypeExpression || srcLlvm.EndsWith("*")) &&
            (dstType is PointerTypeExpression or FunctionPointerTypeExpression or ManagedTypeExpression || dstLlvm.EndsWith("*")))
        {
            string temp = NewTemp();
            Emit($"    {temp} = bitcast {srcLlvm} {val} to {dstLlvm}");
            return temp;
        }

        // Pointer to Integer
        if ((srcType is PointerTypeExpression or FunctionPointerTypeExpression or ManagedTypeExpression || srcLlvm.EndsWith("*")) &&
            _typeChecker.IsInteger(dstType))
        {
            string temp = NewTemp();
            Emit($"    {temp} = ptrtoint {srcLlvm} {val} to {dstLlvm}");
            return temp;
        }

        // Integer to Pointer
        if (_typeChecker.IsInteger(srcType) &&
            (dstType is PointerTypeExpression or FunctionPointerTypeExpression or ManagedTypeExpression || dstLlvm.EndsWith("*")))
        {
            string temp = NewTemp();
            Emit($"    {temp} = inttoptr {srcLlvm} {val} to {dstLlvm}");
            return temp;
        }

        // Integer to Integer
        if (_typeChecker.IsInteger(srcType) && _typeChecker.IsInteger(dstType))
        {
            int srcBits = GetIntegerBitWidth(srcType);
            int dstBits = GetIntegerBitWidth(dstType);

            if (dstBits < srcBits)
            {
                string temp = NewTemp();
                Emit($"    {temp} = trunc {srcLlvm} {val} to {dstLlvm}");
                return temp;
            }
            if (dstBits > srcBits)
            {
                string temp = NewTemp();
                string extOp = _typeChecker.IsUnsignedInteger(srcType) ? "zext" : "sext";
                Emit($"    {temp} = {extOp} {srcLlvm} {val} to {dstLlvm}");
                return temp;
            }
            return val;
        }

        // Integer to Float/Double
        if (_typeChecker.IsInteger(srcType) && dstType is NamedTypeExpression { Name: "float" or "double" })
        {
            string temp = NewTemp();
            string convOp = _typeChecker.IsUnsignedInteger(srcType) ? "uitofp" : "sitofp";
            Emit($"    {temp} = {convOp} {srcLlvm} {val} to {dstLlvm}");
            return temp;
        }

        // Float/Double to Integer
        if (srcType is NamedTypeExpression { Name: "float" or "double" } && _typeChecker.IsInteger(dstType))
        {
            string temp = NewTemp();
            string convOp = _typeChecker.IsUnsignedInteger(dstType) ? "fptoui" : "fptosi";
            Emit($"    {temp} = {convOp} {srcLlvm} {val} to {dstLlvm}");
            return temp;
        }

        // Float to Double
        if (srcType is NamedTypeExpression { Name: "float" } && dstType is NamedTypeExpression { Name: "double" })
        {
            string temp = NewTemp();
            Emit($"    {temp} = fpext {srcLlvm} {val} to {dstLlvm}");
            return temp;
        }

        // Double to Float
        if (srcType is NamedTypeExpression { Name: "double" } && dstType is NamedTypeExpression { Name: "float" })
        {
            string temp = NewTemp();
            Emit($"    {temp} = fptrunc {srcLlvm} {val} to {dstLlvm}");
            return temp;
        }

        return val;
    }

    private string EmitImplicitCast(string val, TypeExpression fromType, TypeExpression toType)
    {
        return EmitCast(val, fromType, toType);
    }

    public void Visit(CompilationUnit node)
    {
        _currentNamespacePath = "";
        foreach (AstNode member in node.Members)
            member.Accept(this);

        foreach (NamespaceDeclaration ns in node.Namespaces)
            ns.Accept(this);

        EmitIsInstanceHelper();
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
        if (node.IsGeneric) return;

        TypeChecker.ClassInfo? prevClass = _currentClass;
        TypeChecker.StructInfo? prevStruct = _currentStruct;
        _currentClass = _typeChecker.GetClass(node.Name);
        _currentStruct = null;

        try
        {
            // 1. Emit class struct layout: %ClassName = type { i8**, fields... }
            List<string> llvmFieldTypes = new() { "i8**" };
            foreach (var field in _currentClass!.Fields)
            {
                llvmFieldTypes.Add(EmitType(field.Type));
            }
            EmitGlobal($"%{node.Name} = type {{ {string.Join(", ", llvmFieldTypes)} }}");

            // 2. Emit class vtable: @ClassName$vtable = internal constant [N x i8*] [ ... ]
            int vtableSize = _currentClass.VirtualMethods.Count;
            if (vtableSize == 0)
            {
                EmitGlobal($"@{node.Name}$vtable = internal constant [0 x i8*] zeroinitializer");
            }
            else
            {
                List<string> entries = new();
                for (int i = 0; i < vtableSize; i++)
                {
                    if (i == _currentClass.DestructorSlot)
                    {
                        string dtorNs = _currentClass.Namespace;
                        string dtorMangled = dtorNs.Length > 0
                            ? $"gflat${dtorNs}${node.Name}$dtor"
                            : $"gflat${node.Name}$dtor";
                        entries.Add($"i8* bitcast (void (%{node.Name}*)* @{dtorMangled} to i8*)");
                        continue;
                    }

                    MethodDeclaration vm = _currentClass.VirtualMethods[i];
                    if (vm.IsAbstract)
                    {
                        entries.Add("i8* null");
                    }
                    else
                    {
                        string declaringClass = _currentClass.Methods[vm.Name].DeclaringClass;
                        TypeChecker.ClassInfo declaringClassInfo = _typeChecker.GetClass(declaringClass)!;
                        string methodNs = declaringClassInfo.Namespace;
                        string mangled = methodNs.Length > 0
                            ? $"gflat${methodNs}${declaringClass}${vm.Name}"
                            : $"gflat${declaringClass}${vm.Name}";

                        bool isThrowing = _typeChecker.CanFunctionThrow(vm);
                        string baseRet = EmitType(vm.ReturnType);
                        string retType = isThrowing
                            ? (baseRet == "void" ? "{ %Exception*, i1 }" : $"{{ {baseRet}, %Exception*, i1 }}")
                            : baseRet;

                        List<string> paramTypes = new() { $"%{declaringClass}*" };
                        foreach (Parameter p in vm.Parameters)
                        {
                            paramTypes.Add(EmitParamType(p.Type));
                        }
                        string fnSig = $"{retType} ({string.Join(", ", paramTypes)})*";
                        entries.Add($"i8* bitcast ({fnSig} @{mangled} to i8*)");
                    }
                }
                string vtableContent = string.Join(", ", entries);
                EmitGlobal($"@{node.Name}$vtable = internal constant [{vtableSize} x i8*] [ {vtableContent} ]");
            }

            // 3. Emit interface vtables for this class
            foreach (string ifaceName in _currentClass.Interfaces)
            {
                TypeChecker.InterfaceInfo? ifaceInfo = _typeChecker.GetInterface(ifaceName);
                if (ifaceInfo == null)
                {
                    continue;
                }

                if (ifaceInfo.Methods.Count == 0)
                {
                    EmitGlobal($"@{node.Name}${ifaceName}$vtable = internal constant [0 x i8*] zeroinitializer");
                }
                else
                {
                    List<string> entries = new();
                    foreach (MethodDeclaration ifaceMethod in ifaceInfo.Methods)
                    {
                        (MethodDeclaration Method, string DeclaringClass) mEntry = _currentClass.Methods[ifaceMethod.Name];
                        MethodDeclaration classMethod = mEntry.Method;
                        string declaringClass = mEntry.DeclaringClass;
                        TypeChecker.ClassInfo declaringInfo = _typeChecker.GetClass(declaringClass)!;
                        string methodNs = declaringInfo.Namespace;
                        string mangled = methodNs.Length > 0
                            ? $"gflat${methodNs}${declaringClass}${classMethod.Name}"
                            : $"gflat${declaringClass}${classMethod.Name}";

                        bool isThrowing = _typeChecker.CanFunctionThrow(classMethod);
                        string baseRet = EmitType(classMethod.ReturnType);
                        string retType = isThrowing
                            ? (baseRet == "void" ? "{ %Exception*, i1 }" : $"{{ {baseRet}, %Exception*, i1 }}")
                            : baseRet;

                        List<string> paramTypes = new() { $"%{declaringClass}*" };
                        foreach (Parameter p in classMethod.Parameters)
                        {
                            paramTypes.Add(EmitParamType(p.Type));
                        }
                        string fnSig = $"{retType} ({string.Join(", ", paramTypes)})*";
                        entries.Add($"i8* bitcast ({fnSig} @{mangled} to i8*)");
                    }
                    EmitGlobal($"@{node.Name}${ifaceName}$vtable = internal constant [{ifaceInfo.Methods.Count} x i8*] [ {string.Join(", ", entries)} ]");
                }
            }

            // 4. Emit methods, constructors, and destructors
            foreach (AstNode member in node.Members)
            {
                if (member is MethodDeclaration method)
                {
                    if (!method.IsAbstract)
                    {
                        EmitClassMethod(node.Name, method);
                    }
                }
                else if (member is ConstructorDeclaration ctor)
                {
                    EmitClassConstructor(node.Name, ctor);
                }
                else if (member is DestructorDeclaration dtor)
                {
                    EmitClassDestructor(node.Name, dtor);
                }
                else if (member is ClassDeclaration nestedCls)
                {
                    nestedCls.Accept(this);
                }
                else if (member is StructDeclaration nestedStruct)
                {
                    nestedStruct.Accept(this);
                }
            }

            // If class didn't declare a destructor but its hierarchy has one, emit default chained destructor
            if (_currentClass.Destructor == null && _typeChecker.HasAnyDestructor(_currentClass))
            {
                EmitClassDestructor(node.Name, null);
            }

            // 5. Emit default constructor if none defined
            bool hasEmptyCtor = _currentClass.Constructors.Any(c => c.Parameters.Count == 0);
            if (!hasEmptyCtor)
            {
                EmitClassDefaultConstructor(node.Name);
            }
        }
        finally
        {
            _currentClass = prevClass;
            _currentStruct = prevStruct;
        }
    }

    private void EmitClassMethod(string className, MethodDeclaration node)
    {
        _locals.Clear();
        _tempCounter = 0;
        _hasTerminated = false;

        bool isThrowing = _typeChecker.CanFunctionThrow(node);
        string baseReturnType = EmitType(node.ReturnType);
        bool isVoid = baseReturnType == "void";
        string returnType = isThrowing
            ? (isVoid ? "{ %Exception*, i1 }" : $"{{ {baseReturnType}, %Exception*, i1 }}")
            : baseReturnType;

        _currentFunctionReturnType = returnType;
        _currentFunctionExpectedType = node.ReturnType;
        _currentFunctionIsThrowing = isThrowing;
        _currentFunctionIsVoid = isVoid;
        _currentFunctionBaseReturnType = baseReturnType;
        _isInsideMain = false;

        string ns = _currentNamespacePath;
        string mangledName = ns.Length > 0
            ? $"gflat${ns}${className}${node.Name}"
            : $"gflat${className}${node.Name}";

        List<string> paramList = new() { $"%{className}* %this" };
        foreach (Parameter p in node.Parameters)
            paramList.Add($"{EmitParamType(p.Type)} %{p.Name}");

        string parameters = string.Join(", ", paramList);

        Emit($"define {returnType} @{mangledName}({parameters}) {{");
        Emit("entry:");

        string thisPtr = NewTemp();
        Emit($"    {thisPtr} = alloca %{className}*");
        Emit($"    store %{className}* %this, %{className}** {thisPtr}");
        _locals["this"] = thisPtr;

        foreach (Parameter p in node.Parameters)
        {
            string type = EmitParamType(p.Type);
            string ptr = NewTemp();
            Emit($"    {ptr} = alloca {type}");
            Emit($"    store {type} %{p.Name}, {type}* {ptr}");
            _locals[p.Name] = ptr;
        }

        TypeChecker.ClassInfo? prevClass = _currentClass;
        _currentClass = _typeChecker.GetClass(className);

        if (node.Body != null)
            node.Body.Accept(this);

        _currentClass = prevClass;

        if (!_hasTerminated)
        {
            if (isThrowing)
            {
                if (isVoid)
                    Emit("    ret { %Exception*, i1 } zeroinitializer");
            }
            else if (returnType == "void")
            {
                Emit("    ret void");
            }
        }

        Emit("}");
        Emit("");
    }

    private void EmitClassFieldInitializers(string className, string thisPtr)
    {
        if (_currentClass == null) return;
        foreach (FieldDeclaration field in _currentClass.FieldDeclarations)
        {
            if (field.Initializer == null) continue;
            int fieldIdx = _currentClass.FieldIndex(field.Name);
            string llvmFieldType = EmitType(field.Type);

            string loadedThis = NewTemp();
            Emit($"    {loadedThis} = load %{className}*, %{className}** {thisPtr}");
            string fieldPtr = NewTemp();
            Emit($"    {fieldPtr} = getelementptr %{className}, %{className}* {loadedThis}, i32 0, i32 {fieldIdx + 1}");

            field.Initializer.Accept(this);
            string val = Pop();
            TypeExpression initType = _typeChecker.GetType(field.Initializer);
            val = EmitImplicitCast(val, initType, field.Type);
            Emit($"    store {llvmFieldType} {val}, {llvmFieldType}* {fieldPtr}");
        }
    }

    private void EmitClassConstructor(string className, ConstructorDeclaration node)
    {
        _locals.Clear();
        _tempCounter = 0;
        _hasTerminated = false;

        _currentFunctionReturnType = "void";
        _currentFunctionExpectedType = new NamedTypeExpression("void", null, node.Line);
        string ns = _currentNamespacePath;
        string mangledName = GetConstructorMangledName(className, node, ns);

        List<string> paramList = new() { $"%{className}* %this" };
        foreach (Parameter p in node.Parameters)
            paramList.Add($"{EmitParamType(p.Type)} %{p.Name}");

        string parameters = string.Join(", ", paramList);

        Emit($"define void @{mangledName}({parameters}) {{");
        Emit("entry:");

        string thisPtr = NewTemp();
        Emit($"    {thisPtr} = alloca %{className}*");
        Emit($"    store %{className}* %this, %{className}** {thisPtr}");
        _locals["this"] = thisPtr;

        foreach (Parameter p in node.Parameters)
        {
            string type = EmitParamType(p.Type);
            string ptr = NewTemp();
            Emit($"    {ptr} = alloca {type}");
            Emit($"    store {type} %{p.Name}, {type}* {ptr}");
            _locals[p.Name] = ptr;
        }

        TypeChecker.ClassInfo? prevClass = _currentClass;
        _currentClass = _typeChecker.GetClass(className);

        // 1. Call base class constructor if base class exists
        if (_currentClass!.BaseClass != null)
        {
            TypeChecker.ClassInfo baseInfo = _typeChecker.GetClass(_currentClass.BaseClass)!;
            string loadedThis = NewTemp();
            Emit($"    {loadedThis} = load %{className}*, %{className}** {thisPtr}");
            string baseThis = NewTemp();
            Emit($"    {baseThis} = bitcast %{className}* {loadedThis} to %{baseInfo.Name}*");

            ConstructorDeclaration? resolvedBaseCtor = _typeChecker.GetResolvedBaseConstructor(node);
            if (node.BaseArguments != null && resolvedBaseCtor != null)
            {
                List<string> baseArgs = new() { $"%{baseInfo.Name}* {baseThis}" };
                for (int i = 0; i < node.BaseArguments.Count; i++)
                {
                    AstNode arg = node.BaseArguments[i];
                    arg.Accept(this);
                    string val = Pop();
                    TypeExpression argType = _typeChecker.GetType(arg);
                    val = EmitImplicitCast(val, argType, resolvedBaseCtor.Parameters[i].Type);
                    string llvmType = EmitParamType(resolvedBaseCtor.Parameters[i].Type);
                    baseArgs.Add($"{llvmType} {val}");
                }
                string baseCtorName = GetConstructorMangledName(baseInfo.Name, resolvedBaseCtor, baseInfo.Namespace);
                Emit($"    call void @{baseCtorName}({string.Join(", ", baseArgs)})");
            }
            else
            {
                string baseCtorName = GetConstructorMangledName(baseInfo.Name, null, baseInfo.Namespace);
                Emit($"    call void @{baseCtorName}(%{baseInfo.Name}* {baseThis})");
            }
        }

        // 2. Setup vtable pointer
        int vtableSize = _currentClass.VirtualMethods.Count;
        string loadedThisForVtable = NewTemp();
        Emit($"    {loadedThisForVtable} = load %{className}*, %{className}** {thisPtr}");
        string vtableSlot = NewTemp();
        Emit($"    {vtableSlot} = getelementptr %{className}, %{className}* {loadedThisForVtable}, i32 0, i32 0");
        string vtablePtr = NewTemp();
        if (vtableSize > 0)
            Emit($"    {vtablePtr} = bitcast [{vtableSize} x i8*]* @{className}$vtable to i8**");
        else
            Emit($"    {vtablePtr} = bitcast [0 x i8*]* @{className}$vtable to i8**");
        Emit($"    store i8** {vtablePtr}, i8*** {vtableSlot}");

        // 3. Field initializers for this class
        EmitClassFieldInitializers(className, thisPtr);

        // 4. Constructor body
        if (node.Body != null)
            node.Body.Accept(this);

        _currentClass = prevClass;

        if (!_hasTerminated)
            Emit("    ret void");

        Emit("}");
        Emit("");
    }

    private void EmitClassDefaultConstructor(string className)
    {
        _locals.Clear();
        _tempCounter = 0;
        _hasTerminated = false;

        _currentFunctionReturnType = "void";
        _currentFunctionExpectedType = new NamedTypeExpression("void", null, 0);
        string ns = _currentNamespacePath;
        string mangledName = GetConstructorMangledName(className, null, ns);

        Emit($"define void @{mangledName}(%{className}* %this) {{");
        Emit("entry:");

        string thisPtr = NewTemp();
        Emit($"    {thisPtr} = alloca %{className}*");
        Emit($"    store %{className}* %this, %{className}** {thisPtr}");
        _locals["this"] = thisPtr;

        TypeChecker.ClassInfo? prevClass = _currentClass;
        _currentClass = _typeChecker.GetClass(className);

        // 1. Call base class constructor if base class exists
        if (_currentClass!.BaseClass != null)
        {
            TypeChecker.ClassInfo baseInfo = _typeChecker.GetClass(_currentClass.BaseClass)!;
            string loadedThis = NewTemp();
            Emit($"    {loadedThis} = load %{className}*, %{className}** {thisPtr}");
            string baseThis = NewTemp();
            Emit($"    {baseThis} = bitcast %{className}* {loadedThis} to %{baseInfo.Name}*");
            string baseCtorName = GetConstructorMangledName(baseInfo.Name, null, baseInfo.Namespace);
            Emit($"    call void @{baseCtorName}(%{baseInfo.Name}* {baseThis})");
        }

        // 2. Setup vtable pointer
        int vtableSize = _currentClass.VirtualMethods.Count;
        string loadedThisForVtable = NewTemp();
        Emit($"    {loadedThisForVtable} = load %{className}*, %{className}** {thisPtr}");
        string vtableSlot = NewTemp();
        Emit($"    {vtableSlot} = getelementptr %{className}, %{className}* {loadedThisForVtable}, i32 0, i32 0");
        string vtablePtr = NewTemp();
        if (vtableSize > 0)
            Emit($"    {vtablePtr} = bitcast [{vtableSize} x i8*]* @{className}$vtable to i8**");
        else
            Emit($"    {vtablePtr} = bitcast [0 x i8*]* @{className}$vtable to i8**");
        Emit($"    store i8** {vtablePtr}, i8*** {vtableSlot}");

        // 3. Field initializers for this class
        EmitClassFieldInitializers(className, thisPtr);

        _currentClass = prevClass;

        Emit("    ret void");
        Emit("}");
        Emit("");
    }

    private void EmitClassDestructor(string className, DestructorDeclaration? node)
    {
        _locals.Clear();
        _tempCounter = 0;
        _hasTerminated = false;

        _currentFunctionReturnType = "void";
        _currentFunctionExpectedType = new NamedTypeExpression("void", null, node?.Line ?? 0);
        string ns = _currentNamespacePath;
        string mangledName = ns.Length > 0 ? $"gflat${ns}${className}$dtor" : $"gflat${className}$dtor";

        Emit($"define void @{mangledName}(%{className}* %this) {{");
        Emit("entry:");

        string thisPtr = NewTemp();
        Emit($"    {thisPtr} = alloca %{className}*");
        Emit($"    store %{className}* %this, %{className}** {thisPtr}");
        _locals["this"] = thisPtr;

        TypeChecker.ClassInfo? prevClass = _currentClass;
        _currentClass = _typeChecker.GetClass(className);

        if (node?.Body != null)
        {
            node.Body.Accept(this);
        }

        if (_currentClass!.BaseClass != null)
        {
            TypeChecker.ClassInfo baseInfo = _typeChecker.GetClass(_currentClass.BaseClass)!;
            if (_typeChecker.HasAnyDestructor(baseInfo))
            {
                string loadedThis = NewTemp();
                Emit($"    {loadedThis} = load %{className}*, %{className}** {thisPtr}");
                string baseThis = NewTemp();
                Emit($"    {baseThis} = bitcast %{className}* {loadedThis} to %{baseInfo.Name}*");
                string baseDtorName = baseInfo.Namespace.Length > 0
                    ? $"gflat${baseInfo.Namespace}${baseInfo.Name}$dtor"
                    : $"gflat${baseInfo.Name}$dtor";
                Emit($"    call void @{baseDtorName}(%{baseInfo.Name}* {baseThis})");
            }
        }

        _currentClass = prevClass;
        if (!_hasTerminated)
        {
            Emit("    ret void");
        }
        Emit("}");
        Emit("");
    }

    public void Visit(StructDeclaration node)
    {
        if (node.IsGeneric) return;

        string fields = string.Join(", ", node.Members
            .OfType<FieldDeclaration>()
            .Select(f => EmitType(f.Type)));
        EmitGlobal($"%{node.Name} = type {{ {fields} }}");

        TypeChecker.StructInfo? prevStruct = _currentStruct;
        TypeChecker.ClassInfo? prevClass = _currentClass;
        _currentStruct = _typeChecker.GetStruct(node.Name);
        _currentClass = null;

        try
        {
            // Emit vtables for implemented interfaces
            foreach (string ifaceName in node.Interfaces)
            {
                TypeChecker.InterfaceInfo? ifaceInfo = _typeChecker.GetInterface(ifaceName);
                if (ifaceInfo == null)
                {
                    continue;
                }

                if (ifaceInfo.Methods.Count == 0)
                {
                    EmitGlobal($"@{node.Name}${ifaceName}$vtable = internal constant [0 x i8*] zeroinitializer");
                }
                else
                {
                    List<string> entries = new();
                    foreach (MethodDeclaration ifaceMethod in ifaceInfo.Methods)
                    {
                        MethodDeclaration structMethod = _currentStruct!.Methods[ifaceMethod.Name];
                        string retType = EmitType(structMethod.ReturnType);
                        List<string> paramTypes = new() { $"%{node.Name}*" };
                        foreach (Parameter p in structMethod.Parameters)
                        {
                            paramTypes.Add(EmitParamType(p.Type));
                        }
                        string fnSig = $"{retType} ({string.Join(", ", paramTypes)})*";
                        string methodNs = _typeChecker.GetFunctionNamespace(structMethod);
                        string mangled = methodNs.Length > 0
                            ? $"gflat${methodNs}${node.Name}${structMethod.Name}"
                            : $"gflat${node.Name}${structMethod.Name}";
                        entries.Add($"i8* bitcast ({fnSig} @{mangled} to i8*)");
                    }
                    string vtableContent = string.Join(", ", entries);
                    EmitGlobal($"@{node.Name}${ifaceName}$vtable = internal constant [{ifaceInfo.Methods.Count} x i8*] [ {vtableContent} ]");
                }
            }

            foreach (AstNode member in node.Members)
            {
                if (member is MethodDeclaration method)
                {
                    EmitStructMethod(node.Name, method);
                }
                else if (member is OperatorDeclaration op)
                {
                    EmitStructOperator(node.Name, op);
                }
                else if (member is ConstructorDeclaration ctor)
                {
                    EmitStructConstructor(node.Name, ctor);
                }
                else if (member is ClassDeclaration nestedCls)
                {
                    nestedCls.Accept(this);
                }
                else if (member is StructDeclaration nestedStruct)
                {
                    nestedStruct.Accept(this);
                }
            }

            bool hasEmptyCtor = _currentStruct!.Constructors.Any(c => c.Parameters.Count == 0);
            if (!hasEmptyCtor && (_currentStruct.Constructors.Count == 0 || _currentStruct.FieldDeclarations.Any(f => f.Initializer != null)))
            {
                EmitStructDefaultConstructor(node.Name);
            }
        }
        finally
        {
            _currentStruct = prevStruct;
            _currentClass = prevClass;
        }
    }

    private string EmitParamType(TypeExpression type) =>
        type is ArrayTypeExpression a ? EmitType(a.ElementType) + "*" : EmitType(type);

    private static string GetOperatorMethodName(TokenKind kind, int paramCount) => kind switch
    {
        TokenKind.Plus => "op_Addition",
        TokenKind.Minus => paramCount == 1 ? "op_UnaryNegation" : "op_Subtraction",
        TokenKind.Star => "op_Multiply",
        TokenKind.Slash => "op_Division",
        TokenKind.Percent => "op_Modulus",
        TokenKind.EqualsEquals => "op_Equality",
        TokenKind.NotEquals => "op_Inequality",
        TokenKind.Less => "op_LessThan",
        TokenKind.LessEquals => "op_LessThanOrEqual",
        TokenKind.Greater => "op_GreaterThan",
        TokenKind.GreaterEquals => "op_GreaterThanOrEqual",
        TokenKind.Bang => "op_LogicalNot",
        TokenKind.Ampersand => "op_BitwiseAnd",
        TokenKind.Pipe => "op_BitwiseOr",
        TokenKind.Caret => "op_ExclusiveOr",
        TokenKind.LessLess => "op_LeftShift",
        TokenKind.GreaterGreater => "op_RightShift",
        _ => throw new NotImplementedException($"Operator {kind} not supported")
    };

    private string GetMangleTypeName(TypeExpression type)
    {
        type = _typeChecker.ResolveAlias(type);
        if (type is NamedTypeExpression named)
            return named.Name;
        if (type is PointerTypeExpression ptr)
            return GetMangleTypeName(ptr.Inner) + "Ptr";
        if (type is ArrayTypeExpression arr)
            return GetMangleTypeName(arr.ElementType) + "Arr";
        return "val";
    }

    private string GetOperatorMangledName(string structName, OperatorDeclaration node)
    {
        string ns = _typeChecker.GetOperatorNamespace(node);
        string baseOpName = GetOperatorMethodName(node.OperatorKind, node.Parameters.Count);
        string paramTypes = string.Join("$", node.Parameters.Select(p => GetMangleTypeName(p.Type)));
        return ns.Length > 0
            ? $"gflat${ns}${structName}${baseOpName}${paramTypes}"
            : $"gflat${structName}${baseOpName}${paramTypes}";
    }

    private void EmitStructOperator(string structName, OperatorDeclaration node)
    {
        _locals.Clear();
        _tempCounter = 0;
        _hasTerminated = false;

        string returnType = EmitType(node.ReturnType);
        _currentFunctionReturnType = returnType;
        _currentFunctionExpectedType = node.ReturnType;
        string mangledName = GetOperatorMangledName(structName, node);

        List<string> paramList = new();
        foreach (Parameter p in node.Parameters)
            paramList.Add($"{EmitParamType(p.Type)} %{p.Name}");

        string parameters = string.Join(", ", paramList);

        Emit($"define {returnType} @{mangledName}({parameters}) {{");
        Emit("entry:");

        foreach (Parameter p in node.Parameters)
        {
            string type = EmitParamType(p.Type);
            string ptr = NewTemp();
            Emit($"    {ptr} = alloca {type}");
            Emit($"    store {type} %{p.Name}, {type}* {ptr}");
            _locals[p.Name] = ptr;
        }

        node.Body.Accept(this);

        if (returnType == "void" && !_hasTerminated)
            Emit("    ret void");

        Emit("}");
        Emit("");
    }

    private void EmitStructMethod(string structName, MethodDeclaration node)
    {
        _locals.Clear();
        _tempCounter = 0;
        _hasTerminated = false;

        bool isThrowing = _typeChecker.CanFunctionThrow(node);
        string baseReturnType = EmitType(node.ReturnType);
        bool isVoid = baseReturnType == "void";
        string returnType = isThrowing
            ? (isVoid ? "{ %Exception*, i1 }" : $"{{ {baseReturnType}, %Exception*, i1 }}")
            : baseReturnType;

        _currentFunctionReturnType = returnType;
        _currentFunctionExpectedType = node.ReturnType;
        _currentFunctionIsThrowing = isThrowing;
        _currentFunctionIsVoid = isVoid;
        _currentFunctionBaseReturnType = baseReturnType;
        _isInsideMain = false;

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

        if (node.Body != null)
            node.Body.Accept(this);

        if (!_hasTerminated)
        {
            if (isThrowing)
            {
                if (isVoid)
                    Emit("    ret { %Exception*, i1 } zeroinitializer");
            }
            else if (returnType == "void")
            {
                Emit("    ret void");
            }
        }

        Emit("}");
        Emit("");
    }

    private string GetConstructorMangledName(string structName, ConstructorDeclaration? node, string ns)
    {
        if (node == null || node.Parameters.Count == 0)
        {
            return ns.Length > 0
                ? $"gflat${ns}${structName}$ctor$default"
                : $"gflat${structName}$ctor$default";
        }

        string paramTypes = string.Join("$", node.Parameters.Select(p => GetMangleTypeName(p.Type)));
        return ns.Length > 0
            ? $"gflat${ns}${structName}$ctor${paramTypes}"
            : $"gflat${structName}$ctor${paramTypes}";
    }

    private void EmitFieldInitializers(string structName, string thisPtr)
    {
        if (_currentStruct == null) return;
        foreach (FieldDeclaration field in _currentStruct.FieldDeclarations)
        {
            if (field.Initializer == null) continue;
            int fieldIdx = _currentStruct.FieldIndex(field.Name);
            string llvmFieldType = EmitType(field.Type);

            string loadedThis = NewTemp();
            Emit($"    {loadedThis} = load %{structName}*, %{structName}** {thisPtr}");
            string fieldPtr = NewTemp();
            Emit($"    {fieldPtr} = getelementptr %{structName}, %{structName}* {loadedThis}, i32 0, i32 {fieldIdx}");

            field.Initializer.Accept(this);
            string val = Pop();
            TypeExpression initType = _typeChecker.GetType(field.Initializer);
            val = EmitImplicitCast(val, initType, field.Type);
            Emit($"    store {llvmFieldType} {val}, {llvmFieldType}* {fieldPtr}");
        }
    }

    private void EmitStructConstructor(string structName, ConstructorDeclaration node)
    {
        _locals.Clear();
        _tempCounter = 0;
        _hasTerminated = false;

        _currentFunctionReturnType = "void";
        _currentFunctionExpectedType = new NamedTypeExpression("void", null, node.Line);
        string ns = _currentNamespacePath;
        string mangledName = GetConstructorMangledName(structName, node, ns);

        List<string> paramList = new() { $"%{structName}* %this" };
        foreach (Parameter p in node.Parameters)
            paramList.Add($"{EmitParamType(p.Type)} %{p.Name}");

        string parameters = string.Join(", ", paramList);

        Emit($"define void @{mangledName}({parameters}) {{");
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

        EmitFieldInitializers(structName, thisPtr);

        if (node.Body != null)
            node.Body.Accept(this);

        if (!_hasTerminated)
            Emit("    ret void");

        Emit("}");
        Emit("");
    }

    private void EmitStructDefaultConstructor(string structName)
    {
        _locals.Clear();
        _tempCounter = 0;
        _hasTerminated = false;

        _currentFunctionReturnType = "void";
        _currentFunctionExpectedType = new NamedTypeExpression("void", null, 0);
        string ns = _currentNamespacePath;
        string mangledName = GetConstructorMangledName(structName, null, ns);

        Emit($"define void @{mangledName}(%{structName}* %this) {{");
        Emit("entry:");

        string thisPtr = NewTemp();
        Emit($"    {thisPtr} = alloca %{structName}*");
        Emit($"    store %{structName}* %this, %{structName}** {thisPtr}");
        _locals["this"] = thisPtr;

        EmitFieldInitializers(structName, thisPtr);

        Emit("    ret void");
        Emit("}");
        Emit("");
    }

    public void Visit(InterfaceDeclaration node) { }

    public void Visit(FieldDeclaration node)
    {
        if (node.IsConst)
        {
            return;
        }
        throw new NotImplementedException("Mutable global fields are not yet supported");
    }

    public void Visit(OperatorDeclaration node) => throw new NotImplementedException();

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
        if (node.IsGeneric) return;

        _locals.Clear();
        _tempCounter = 0;

        bool isMain = (node.Name == "main");
        bool isThrowing = _typeChecker.CanFunctionThrow(node);
        string baseReturnType = EmitType(node.ReturnType);
        bool isVoid = baseReturnType == "void";
        string returnType = (isThrowing && !isMain)
            ? (isVoid ? "{ %Exception*, i1 }" : $"{{ {baseReturnType}, %Exception*, i1 }}")
            : baseReturnType;

        _currentFunctionReturnType = returnType;
        _currentFunctionExpectedType = node.ReturnType;
        _currentFunctionIsThrowing = isThrowing && !isMain;
        _currentFunctionIsVoid = isVoid;
        _currentFunctionBaseReturnType = baseReturnType;
        _isInsideMain = isMain;

        string name;
        if (isMain)
            name = "main";
        else if (_currentNamespacePath.Length > 0)
            name = $"gflat${_currentNamespacePath}${node.Name}";
        else
            name = $"gflat${node.Name}";

        string parameters = string.Join(", ", node.Parameters.Select(p =>
            $"{EmitParamType(p.Type)} %{p.Name}"));

        Emit($"define {returnType} @{name}({parameters}) {{");
        Emit("entry:");

        if (isMain)
        {
            _mainErrSlot = NewTemp();
            Emit($"    {_mainErrSlot} = alloca %Exception*");
            Emit($"    store %Exception* null, %Exception** {_mainErrSlot}");
            _mainOwnedSlot = NewTemp();
            Emit($"    {_mainOwnedSlot} = alloca i1");
            Emit($"    store i1 0, i1* {_mainOwnedSlot}");
            _mainUnhandledLabel = NewLabel("main_unhandled");
        }

        foreach (Parameter p in node.Parameters)
        {
            string type = EmitParamType(p.Type);
            string ptr = NewTemp();
            Emit($"    {ptr} = alloca {type}");
            Emit($"    store {type} %{p.Name}, {type}* {ptr}");
            _locals[p.Name] = ptr;
        }

        _hasTerminated = false;
        if (node.Body != null)
            node.Body.Accept(this);

        if (isMain)
        {
            if (!_hasTerminated)
            {
                Emit("    ret i32 0");
            }
            Emit($"{_mainUnhandledLabel}:");
            _hasTerminated = false;
            string ex = NewTemp();
            Emit($"    {ex} = load %Exception*, %Exception** {_mainErrSlot}");
            string owned = NewTemp();
            Emit($"    {owned} = load i1, i1* {_mainOwnedSlot}");

            if (!_externNames.Contains("puts"))
            {
                _externNames.Add("puts");
                EmitGlobal("declare i32 @puts(i8*)");
            }
            if (!_externNames.Contains("free"))
            {
                _externNames.Add("free");
                EmitGlobal("declare void @free(i8*)");
            }

            string msgSlot = NewTemp();
            Emit($"    {msgSlot} = getelementptr %Exception, %Exception* {ex}, i32 0, i32 1");
            string msg = NewTemp();
            Emit($"    {msg} = load i8*, i8** {msgSlot}");
            string msgNull = NewTemp();
            Emit($"    {msgNull} = icmp eq i8* {msg}, null");

            string printMsgLbl = NewLabel("main_print_msg");
            string afterPrintLbl = NewLabel("main_after_print");
            Emit($"    br i1 {msgNull}, label %{afterPrintLbl}, label %{printMsgLbl}");

            Emit($"{printMsgLbl}:");
            Emit($"    call i32 @puts(i8* {msg})");
            Emit($"    br label %{afterPrintLbl}");

            Emit($"{afterPrintLbl}:");
            string freeLbl = NewLabel("main_free");
            string exitLbl = NewLabel("main_exit");
            Emit($"    br i1 {owned}, label %{freeLbl}, label %{exitLbl}");

            Emit($"{freeLbl}:");
            string rawEx = NewTemp();
            Emit($"    {rawEx} = bitcast %Exception* {ex} to i8*");
            Emit($"    call void @free(i8* {rawEx})");
            Emit($"    br label %{exitLbl}");

            Emit($"{exitLbl}:");
            Emit("    ret i32 1");
        }
        else
        {
            if (!_hasTerminated)
            {
                if (isThrowing)
                {
                    if (isVoid)
                        Emit("    ret { %Exception*, i1 } zeroinitializer");
                }
                else if (returnType == "void")
                {
                    Emit("    ret void");
                }
            }
        }

        Emit("}");
        Emit("");
    }

    public void Visit(ConstructorDeclaration node) { }
    public void Visit(DestructorDeclaration node) { }
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

            string targetRet = _currentFunctionIsThrowing ? _currentFunctionBaseReturnType : _currentFunctionReturnType;
            if (targetRet.Length > 0 && llvmReturnType != targetRet)
            {
                if (_currentFunctionExpectedType != null)
                {
                    val = EmitImplicitCast(val, returnType, _currentFunctionExpectedType);
                    llvmReturnType = targetRet;
                }
            }
        }

        // Clean up active catches if inside catch blocks
        foreach (ActiveCatchInfo activeCatch in _activeCatchStack)
        {
            string freeLbl = NewLabel("ret_free_catch");
            string afterFreeLbl = NewLabel("ret_after_free_catch");
            Emit($"    br i1 {activeCatch.Owned}, label %{freeLbl}, label %{afterFreeLbl}");
            Emit($"{freeLbl}:");
            string raw = NewTemp();
            Emit($"    {raw} = bitcast %Exception* {activeCatch.Ex} to i8*");
            Emit($"    call void @free(i8* {raw})");
            Emit($"    br label %{afterFreeLbl}");
            Emit($"{afterFreeLbl}:");
        }

        EmitDefersDownTo(0);

        if (_currentFunctionIsThrowing)
        {
            if (val == null)
            {
                Emit("    ret { %Exception*, i1 } zeroinitializer");
            }
            else
            {
                string r0 = NewTemp();
                Emit($"    {r0} = insertvalue {{ {_currentFunctionBaseReturnType}, %Exception*, i1 }} zeroinitializer, {_currentFunctionBaseReturnType} {val}, 0");
                Emit($"    ret {{ {_currentFunctionBaseReturnType}, %Exception*, i1 }} {r0}");
            }
        }
        else
        {
            if (val == null)
                Emit("    ret void");
            else
                Emit($"    ret {llvmReturnType} {val}");
        }

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

        if (node.Else != null && thenTerminated && elseTerminated)
        {
            _hasTerminated = true;
        }
        else
        {
            Emit($"{mergeLabel}:");
            _hasTerminated = false;
        }
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

    public void Visit(ForeachStatement node)
    {
        if (node.Desugared != null)
        {
            node.Desugared.Accept(this);
            return;
        }

        // Array iteration
        TypeExpression colType = _typeChecker.GetType(node.Collection);
        ArrayTypeExpression arr = (ArrayTypeExpression)_typeChecker.ResolveAlias(colType);
        int count = arr.Size!.Value;

        string condLabel = NewLabel("foreach_cond");
        string bodyLabel = NewLabel("foreach_body");
        string advanceLabel = NewLabel("foreach_adv");
        string exitLabel = NewLabel("foreach_exit");

        _breakLabels.Push(exitLabel);
        _continueLabels.Push(advanceLabel);
        _loopDeferDepths.Push(_deferScopes.Count);

        // Evaluate collection to get base pointer
        node.Collection.Accept(this);
        string basePtr = Pop();

        // Allocate index variable
        string idxPtr = NewTemp();
        Emit($"    {idxPtr} = alloca i32");
        Emit($"    store i32 0, i32* {idxPtr}");

        // Allocate loop variable
        string llvmVarType = EmitType(node.ElementType);
        string varPtr = NewTemp();
        Emit($"    {varPtr} = alloca {llvmVarType}");
        _locals.TryGetValue(node.VariableName, out string? prevLocal);
        _locals[node.VariableName] = varPtr;

        Emit($"    br label %{condLabel}");
        Emit($"{condLabel}:");

        string curIdx = NewTemp();
        Emit($"    {curIdx} = load i32, i32* {idxPtr}");
        string cond = NewTemp();
        Emit($"    {cond} = icmp slt i32 {curIdx}, {count}");
        Emit($"    br i1 {cond}, label %{bodyLabel}, label %{exitLabel}");

        Emit($"{bodyLabel}:");
        TypeExpression elemType = _typeChecker.ResolveAlias(arr.ElementType);
        string llvmElemType = EmitType(elemType);
        string elemPtr = NewTemp();
        Emit($"    {elemPtr} = getelementptr {llvmElemType}, {llvmElemType}* {basePtr}, i32 {curIdx}");
        string elemVal = NewTemp();
        Emit($"    {elemVal} = load {llvmElemType}, {llvmElemType}* {elemPtr}");
        string castedVal = EmitImplicitCast(elemVal, elemType, node.ElementType);
        Emit($"    store {llvmVarType} {castedVal}, {llvmVarType}* {varPtr}");

        _hasTerminated = false;
        node.Body.Accept(this);
        if (!_hasTerminated)
        {
            Emit($"    br label %{advanceLabel}");
        }

        Emit($"{advanceLabel}:");
        string idxToInc = NewTemp();
        Emit($"    {idxToInc} = load i32, i32* {idxPtr}");
        string nextIdx = NewTemp();
        Emit($"    {nextIdx} = add i32 {idxToInc}, 1");
        Emit($"    store i32 {nextIdx}, i32* {idxPtr}");
        Emit($"    br label %{condLabel}");

        Emit($"{exitLabel}:");
        if (prevLocal != null)
        {
            _locals[node.VariableName] = prevLocal;
        }
        else
        {
            _locals.Remove(node.VariableName);
        }

        _breakLabels.Pop();
        _continueLabels.Pop();
        _loopDeferDepths.Pop();
        _hasTerminated = false;
    }

    public void Visit(VariableDeclaration node)
    {
        if (node.IsConst)
        {
            return;
        }

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
            val = EmitImplicitCast(val, initType, resolvedVarType);
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

        if (_typeChecker.TryGetOperatorTarget(node, out (string StructName, OperatorDeclaration Operator) opTarget))
        {
            TypeExpression leftType1 = _typeChecker.GetType(node.Left);
            TypeExpression rightType1 = _typeChecker.GetType(node.Right);
            left = EmitImplicitCast(left, leftType1, opTarget.Operator.Parameters[0].Type);
            right = EmitImplicitCast(right, rightType1, opTarget.Operator.Parameters[1].Type);

            string mangledName = GetOperatorMangledName(opTarget.StructName, opTarget.Operator);
            string retLlvmType = EmitType(opTarget.Operator.ReturnType);
            string param0Type = EmitParamType(opTarget.Operator.Parameters[0].Type);
            string param1Type = EmitParamType(opTarget.Operator.Parameters[1].Type);

            string temp1 = NewTemp();
            Emit($"    {temp1} = call {retLlvmType} @{mangledName}({param0Type} {left}, {param1Type} {right})");
            Push(temp1);
            return;
        }

        string temp = NewTemp();

        TypeExpression leftType = _typeChecker.GetType(node.Left);
        TypeExpression rightType = _typeChecker.GetType(node.Right);
        TypeExpression resultType = _typeChecker.GetType(node);
        string llvmType = EmitType(resultType);
        bool isFloat = leftType is NamedTypeExpression { Name: "float" };

        bool isUnsigned = _typeChecker.IsUnsignedInteger(leftType) || _typeChecker.IsUnsignedInteger(rightType);

        bool isComparison = node.Operator is TokenKind.EqualsEquals or TokenKind.NotEquals or
            TokenKind.Less or TokenKind.Greater or TokenKind.LessEquals or TokenKind.GreaterEquals;

        if (isComparison)
        {
            string cmpLlvm = EmitType(leftType);
            string rightLlvm = EmitType(rightType);
            if (cmpLlvm != rightLlvm && !isFloat)
            {
                int leftBits = GetIntegerBitWidth(leftType);
                int rightBits = GetIntegerBitWidth(rightType);
                if (leftBits < rightBits)
                {
                    left = EmitImplicitCast(left, leftType, rightType);
                    cmpLlvm = rightLlvm;
                }
                else if (rightBits < leftBits)
                {
                    right = EmitImplicitCast(right, rightType, leftType);
                }
            }
            else if (_typeChecker.IsUnsignedInteger(leftType) != _typeChecker.IsUnsignedInteger(rightType) && !isFloat)
            {
                left = EmitImplicitCast(left, leftType, new NamedTypeExpression("int", null, 0));
                right = EmitImplicitCast(right, rightType, new NamedTypeExpression("int", null, 0));
                cmpLlvm = "i32";
            }

            if (cmpLlvm == "{ i8*, i8** }" || rightLlvm == "{ i8*, i8** }")
            {
                string leftData;
                if (left == "null" || left == "zeroinitializer")
                {
                    leftData = "null";
                }
                else
                {
                    leftData = NewTemp();
                    Emit($"    {leftData} = extractvalue {{ i8*, i8** }} {left}, 0");
                }

                string rightData;
                if (right == "null" || right == "zeroinitializer")
                {
                    rightData = "null";
                }
                else
                {
                    rightData = NewTemp();
                    Emit($"    {rightData} = extractvalue {{ i8*, i8** }} {right}, 0");
                }

                string cmpOp = node.Operator switch
                {
                    TokenKind.EqualsEquals => "eq",
                    TokenKind.NotEquals => "ne",
                    _ => throw new NotImplementedException()
                };
                Emit($"    {temp} = icmp {cmpOp} i8* {leftData}, {rightData}");
                Push(temp);
                return;
            }

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
                    TokenKind.Less => isUnsigned ? "ult" : "slt",
                    TokenKind.Greater => isUnsigned ? "ugt" : "sgt",
                    TokenKind.LessEquals => isUnsigned ? "ule" : "sle",
                    TokenKind.GreaterEquals => isUnsigned ? "uge" : "sge",
                    _ => throw new NotImplementedException()
                };
                Emit($"    {temp} = icmp {op} {cmpLlvm} {left}, {right}");
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
                if (node.Operator is TokenKind.LessLess or TokenKind.GreaterGreater)
                {
                    string rightLlvm = EmitType(rightType);
                    if (rightLlvm != llvmType)
                    {
                        right = EmitImplicitCast(right, rightType, leftType);
                    }
                    string shiftOp = node.Operator == TokenKind.LessLess ? "shl" : (_typeChecker.IsUnsignedInteger(leftType) ? "lshr" : "ashr");
                    Emit($"    {temp} = {shiftOp} {llvmType} {left}, {right}");
                }
                else
                {
                    left = EmitImplicitCast(left, leftType, resultType);
                    right = EmitImplicitCast(right, rightType, resultType);
                    llvmType = EmitType(resultType);
                    bool isUnsignedOp = _typeChecker.IsUnsignedInteger(resultType);

                    if (node.Operator == TokenKind.Slash)
                    {
                        string divOp = isUnsignedOp ? "udiv" : "sdiv";
                        Emit($"    {temp} = {divOp} {llvmType} {left}, {right}");
                    }
                    else if (node.Operator == TokenKind.Percent)
                    {
                        string remOp = isUnsignedOp ? "urem" : "srem";
                        Emit($"    {temp} = {remOp} {llvmType} {left}, {right}");
                    }
                    else
                    {
                        string op = node.Operator switch
                        {
                            TokenKind.Plus => "add",
                            TokenKind.Minus => "sub",
                            TokenKind.Star => "mul",
                            TokenKind.Pipe => "or",
                            TokenKind.Ampersand => "and",
                            TokenKind.Caret => "xor",
                            _ => throw new NotImplementedException($"Operator {node.Operator} not yet supported")
                        };
                        Emit($"    {temp} = {op} {llvmType} {left}, {right}");
                    }
                }
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

        if (_typeChecker.TryGetUnaryOperatorTarget(node, out (string StructName, OperatorDeclaration Operator) opTarget))
        {
            operand = EmitImplicitCast(operand, type, opTarget.Operator.Parameters[0].Type);
            string mangledName = GetOperatorMangledName(opTarget.StructName, opTarget.Operator);
            string retLlvmType = EmitType(opTarget.Operator.ReturnType);
            string param0Type = EmitParamType(opTarget.Operator.Parameters[0].Type);

            string temp1 = NewTemp();
            Emit($"    {temp1} = call {retLlvmType} @{mangledName}({param0Type} {operand})");
            Push(temp1);
            return;
        }

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
            case TokenKind.UIntLiteral:
                {
                    string txt = node.Token.Text.TrimEnd('u', 'U');
                    if (txt.StartsWith("0x") || txt.StartsWith("0X"))
                    {
                        Push(Convert.ToUInt32(txt[2..], 16).ToString());
                    }
                    else
                    {
                        Push(uint.Parse(txt).ToString());
                    }
                    break;
                }
            case TokenKind.HexInt:
                Push(Convert.ToInt64(node.Token.Text, 16).ToString());
                break;
            case TokenKind.LongLiteral:
                {
                    string txt = node.Token.Text.TrimEnd('l', 'L');
                    if (txt.StartsWith("0x") || txt.StartsWith("0X"))
                    {
                        Push(Convert.ToInt64(txt[2..], 16).ToString());
                    }
                    else
                    {
                        Push(long.Parse(txt).ToString());
                    }
                    break;
                }
            case TokenKind.ULongLiteral:
                {
                    string txt = node.Token.Text.TrimEnd('u', 'U', 'l', 'L');
                    if (txt.StartsWith("0x") || txt.StartsWith("0X"))
                    {
                        Push(Convert.ToUInt64(txt[2..], 16).ToString());
                    }
                    else
                    {
                        Push(ulong.Parse(txt).ToString());
                    }
                    break;
                }
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
        if (_typeChecker.TryGetConstValueByName(node.Name, out ConstValue? constVal) && constVal != null)
        {
            if (TryEmitConstValue(constVal, _typeChecker.GetType(node)))
            {
                return;
            }
        }

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

        if (_currentClass != null && _locals.TryGetValue("this", out string? classThisPtr))
        {
            int idx = _currentClass.FieldIndex(node.Name);
            if (idx >= 0)
            {
                string loadedThis = NewTemp();
                Emit($"    {loadedThis} = load %{_currentClass.Name}*, %{_currentClass.Name}** {classThisPtr}");
                string fieldPtr = NewTemp();
                Emit($"    {fieldPtr} = getelementptr %{_currentClass.Name}, %{_currentClass.Name}* {loadedThis}, i32 0, i32 {idx + 1}");
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
        if (_typeChecker.TryGetConstValue(node, out ConstValue? constVal) && constVal != null)
        {
            if (TryEmitConstValue(constVal, _typeChecker.GetType(node)))
            {
                return;
            }
        }

        if (node.Callee is MemberAccessExpression { IsArrow: true, Member: "free" } freeAccess)
        {
            freeAccess.Object.Accept(this);
            string ptrVal = Pop();
            TypeExpression ptrType = _typeChecker.GetType(freeAccess.Object);
            string llvmPtrType = EmitType(ptrType);

            if (_typeChecker.TryGetClassDestructorCall(node, out (TypeChecker.ClassInfo Class, bool IsVirtual, int SlotIndex) dtorCall))
            {
                if (dtorCall.IsVirtual)
                {
                    string classPtr = ptrVal;
                    if (llvmPtrType != $"%{dtorCall.Class.Name}*")
                    {
                        classPtr = NewTemp();
                        Emit($"    {classPtr} = bitcast {llvmPtrType} {ptrVal} to %{dtorCall.Class.Name}*");
                    }
                    string vtableSlot = NewTemp();
                    Emit($"    {vtableSlot} = getelementptr %{dtorCall.Class.Name}, %{dtorCall.Class.Name}* {classPtr}, i32 0, i32 0");
                    string vtablePtr = NewTemp();
                    Emit($"    {vtablePtr} = load i8**, i8*** {vtableSlot}");
                    string slotPtr = NewTemp();
                    Emit($"    {slotPtr} = getelementptr i8*, i8** {vtablePtr}, i32 {dtorCall.SlotIndex}");
                    string rawFnPtr = NewTemp();
                    Emit($"    {rawFnPtr} = load i8*, i8** {slotPtr}");
                    string dtorFn = NewTemp();
                    Emit($"    {dtorFn} = bitcast i8* {rawFnPtr} to void (%{dtorCall.Class.Name}*)*");
                    Emit($"    call void {dtorFn}(%{dtorCall.Class.Name}* {classPtr})");
                }
                else
                {
                    string classPtr = ptrVal;
                    if (llvmPtrType != $"%{dtorCall.Class.Name}*")
                    {
                        classPtr = NewTemp();
                        Emit($"    {classPtr} = bitcast {llvmPtrType} {ptrVal} to %{dtorCall.Class.Name}*");
                    }
                    string dtorNs = dtorCall.Class.Namespace;
                    string dtorMangled = dtorNs.Length > 0
                        ? $"gflat${dtorNs}${dtorCall.Class.Name}$dtor"
                        : $"gflat${dtorCall.Class.Name}$dtor";
                    Emit($"    call void @{dtorMangled}(%{dtorCall.Class.Name}* {classPtr})");
                }
            }

            if (!_externNames.Contains("free"))
            {
                _externNames.Add("free");
                EmitGlobal("declare void @free(i8*)");
            }

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

        if (_typeChecker.TryGetInterfaceCall(node, out (TypeChecker.InterfaceInfo Interface, int SlotIndex, MethodDeclaration Method) ifaceCall))
        {
            MemberAccessExpression ifaceMemberAccess = (MemberAccessExpression)node.Callee;
            ifaceMemberAccess.Object.Accept(this);
            string fatPtr = Pop();

            string dataPtr = NewTemp();
            Emit($"    {dataPtr} = extractvalue {{ i8*, i8** }} {fatPtr}, 0");

            string vtablePtr = NewTemp();
            Emit($"    {vtablePtr} = extractvalue {{ i8*, i8** }} {fatPtr}, 1");

            string slotPtr = NewTemp();
            Emit($"    {slotPtr} = getelementptr i8*, i8** {vtablePtr}, i32 {ifaceCall.SlotIndex}");

            string rawFnPtr = NewTemp();
            Emit($"    {rawFnPtr} = load i8*, i8** {slotPtr}");

            string returnType = EmitType(ifaceCall.Method.ReturnType);
            List<string> fnParamTypes = new() { "i8*" };
            foreach (Parameter p in ifaceCall.Method.Parameters)
            {
                fnParamTypes.Add(EmitParamType(p.Type));
            }
            string fnSig = $"{returnType} ({string.Join(", ", fnParamTypes)})*";

            string typedFn = NewTemp();
            Emit($"    {typedFn} = bitcast i8* {rawFnPtr} to {fnSig}");

            List<string> callArgs = new() { $"i8* {dataPtr}" };
            for (int i = 0; i < node.Arguments.Count; i++)
            {
                AstNode arg = node.Arguments[i];
                arg.Accept(this);
                string val = Pop();
                TypeExpression argType = _typeChecker.GetType(arg);
                string llvmArgType = EmitParamType(ifaceCall.Method.Parameters[i].Type);
                val = EmitImplicitCast(val, argType, ifaceCall.Method.Parameters[i].Type);
                callArgs.Add($"{llvmArgType} {val}");
            }

            string argsStr = string.Join(", ", callArgs);
            if (returnType == "void")
            {
                Emit($"    call void {typedFn}({argsStr})");
            }
            else
            {
                string temp = NewTemp();
                Emit($"    {temp} = call {returnType} {typedFn}({argsStr})");
                Push(temp);
            }
            return;
        }

        if (_typeChecker.TryGetVirtualMethodCall(node, out (TypeChecker.ClassInfo Class, int SlotIndex, MethodDeclaration Method) vcall))
        {
            string thisVal;
            string thisType;

            if (node.Callee is MemberAccessExpression vMemberAccess)
            {
                TypeExpression objType = _typeChecker.GetType(vMemberAccess.Object);
                if (objType is PointerTypeExpression ptrType)
                {
                    vMemberAccess.Object.Accept(this);
                    thisVal = Pop();
                    thisType = EmitType(ptrType);
                }
                else if (objType is ManagedTypeExpression mgdType)
                {
                    vMemberAccess.Object.Accept(this);
                    thisVal = Pop();
                    thisType = EmitType(mgdType);
                }
                else
                {
                    EmitAddress(vMemberAccess.Object);
                    thisVal = Pop();
                    thisType = EmitType(objType) + "*";
                }
            }
            else
            {
                string loadedThis = NewTemp();
                Emit($"    {loadedThis} = load %{vcall.Class.Name}*, %{vcall.Class.Name}** {_locals["this"]}");
                thisVal = loadedThis;
                thisType = $"%{vcall.Class.Name}*";
            }

            string basePtr = thisVal;
            if (thisType != $"%{vcall.Class.Name}*")
            {
                basePtr = NewTemp();
                Emit($"    {basePtr} = bitcast {thisType} {thisVal} to %{vcall.Class.Name}*");
            }

            string vtableSlot = NewTemp();
            Emit($"    {vtableSlot} = getelementptr %{vcall.Class.Name}, %{vcall.Class.Name}* {basePtr}, i32 0, i32 0");
            string vtablePtr = NewTemp();
            Emit($"    {vtablePtr} = load i8**, i8*** {vtableSlot}");

            string slotPtr = NewTemp();
            Emit($"    {slotPtr} = getelementptr i8*, i8** {vtablePtr}, i32 {vcall.SlotIndex}");
            string rawFnPtr = NewTemp();
            Emit($"    {rawFnPtr} = load i8*, i8** {slotPtr}");

            string declaringClass = vcall.Class.Methods[vcall.Method.Name].DeclaringClass;
            bool isVirtualThrowing = _typeChecker.CanCallThrow(node) || _typeChecker.CanFunctionThrow(vcall.Method);
            string baseReturnType = EmitType(vcall.Method.ReturnType);
            string returnType = isVirtualThrowing
                ? (baseReturnType == "void" ? "{ %Exception*, i1 }" : $"{{ {baseReturnType}, %Exception*, i1 }}")
                : baseReturnType;
            List<string> fnParamTypes = new() { $"%{declaringClass}*" };
            foreach (Parameter p in vcall.Method.Parameters)
            {
                fnParamTypes.Add(EmitParamType(p.Type));
            }
            string fnSig = $"{returnType} ({string.Join(", ", fnParamTypes)})*";

            string typedFn = NewTemp();
            Emit($"    {typedFn} = bitcast i8* {rawFnPtr} to {fnSig}");

            string passedThis = basePtr;
            if (vcall.Class.Name != declaringClass)
            {
                passedThis = NewTemp();
                Emit($"    {passedThis} = bitcast %{vcall.Class.Name}* {basePtr} to %{declaringClass}*");
            }

            List<string> callArgs = new() { $"%{declaringClass}* {passedThis}" };
            for (int i = 0; i < node.Arguments.Count; i++)
            {
                AstNode arg = node.Arguments[i];
                arg.Accept(this);
                string val = Pop();
                TypeExpression argType = _typeChecker.GetType(arg);
                string llvmArgType = EmitParamType(vcall.Method.Parameters[i].Type);
                val = EmitImplicitCast(val, argType, vcall.Method.Parameters[i].Type);
                callArgs.Add($"{llvmArgType} {val}");
            }

            string argsStr = string.Join(", ", callArgs);
            if (isVirtualThrowing)
            {
                if (baseReturnType == "void")
                {
                    string callRes = NewTemp();
                    Emit($"    {callRes} = call {{ %Exception*, i1 }} {typedFn}({argsStr})");
                    EmitCheckAndHandleCallException(callRes, isVoid: true, baseReturnType);
                }
                else
                {
                    string callRes = NewTemp();
                    Emit($"    {callRes} = call {{ {baseReturnType}, %Exception*, i1 }} {typedFn}({argsStr})");
                    string valTemp = EmitCheckAndHandleCallException(callRes, isVoid: false, baseReturnType);
                    Push(valTemp);
                }
            }
            else
            {
                if (returnType == "void")
                {
                    Emit($"    call void {typedFn}({argsStr})");
                }
                else
                {
                    string temp = NewTemp();
                    Emit($"    {temp} = call {returnType} {typedFn}({argsStr})");
                    Push(temp);
                }
            }
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
                    val = EmitImplicitCast(val, argType, fnPtr.ParameterTypes[i]);
                    llvmArgType = EmitParamType(fnPtr.ParameterTypes[i]);
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
            else if (objType is ManagedTypeExpression mgdType)
            {
                memberAccess.Object.Accept(this);
                thisVal = Pop();
                thisType = EmitType(mgdType);
            }
            else
            {
                EmitAddress(memberAccess.Object);
                thisVal = Pop();
                thisType = EmitType(objType) + "*";
            }

            argValues.Add(thisVal);
            argTypes.Add(thisType);

            for (int i = 0; i < node.Arguments.Count; i++)
            {
                AstNode arg = node.Arguments[i];
                arg.Accept(this);
                string val = Pop();
                TypeExpression argType = _typeChecker.GetType(arg);
                string llvmArgType = argType is ArrayTypeExpression a ? EmitType(a.ElementType) + "*" : EmitType(argType);
                if (i < structMethod.Parameters.Count)
                {
                    val = EmitImplicitCast(val, argType, structMethod.Parameters[i].Type);
                    llvmArgType = EmitParamType(structMethod.Parameters[i].Type);
                }
                argValues.Add(val);
                argTypes.Add(llvmArgType);
            }

            string rawName = ((NamedTypeExpression)(objType is PointerTypeExpression p ? p.Inner : (objType is ManagedTypeExpression m ? m.Inner : objType))).Name;
            string typeName = rawName;
            if (_typeChecker.IsClass(rawName))
            {
                TypeChecker.ClassInfo cInfo = _typeChecker.GetClass(rawName)!;
                string declaringClass = cInfo.Methods[structMethod.Name].DeclaringClass;
                typeName = declaringClass;
                if (rawName != declaringClass)
                {
                    string castThis = NewTemp();
                    Emit($"    {castThis} = bitcast {thisType} {thisVal} to %{declaringClass}*");
                    argValues[0] = castThis;
                    argTypes[0] = $"%{declaringClass}*";
                }
            }

            string ns = _typeChecker.GetFunctionNamespace(structMethod);
            funcName = ns.Length > 0 
                ? $"gflat${ns}${typeName}${structMethod.Name}" 
                : $"gflat${typeName}${structMethod.Name}";
        }
        else if (node.Callee is IdentifierExpression idMethod && target is MethodDeclaration methodMember && _currentClass != null && _currentClass.Methods.ContainsKey(idMethod.Name))
        {
            string declaringClass = _currentClass.Methods[idMethod.Name].DeclaringClass;
            string loadedThis = NewTemp();
            Emit($"    {loadedThis} = load %{_currentClass.Name}*, %{_currentClass.Name}** {_locals["this"]}");
            string passedThis = loadedThis;
            if (_currentClass.Name != declaringClass)
            {
                passedThis = NewTemp();
                Emit($"    {passedThis} = bitcast %{_currentClass.Name}* {loadedThis} to %{declaringClass}*");
            }
            argValues.Add(passedThis);
            argTypes.Add($"%{declaringClass}*");

            for (int i = 0; i < node.Arguments.Count; i++)
            {
                AstNode arg = node.Arguments[i];
                arg.Accept(this);
                string val = Pop();
                TypeExpression argType = _typeChecker.GetType(arg);
                string llvmArgType = EmitParamType(methodMember.Parameters[i].Type);
                val = EmitImplicitCast(val, argType, methodMember.Parameters[i].Type);
                argValues.Add(val);
                argTypes.Add(llvmArgType);
            }

            string ns = _typeChecker.GetFunctionNamespace(methodMember);
            funcName = ns.Length > 0
                ? $"gflat${ns}${declaringClass}${methodMember.Name}"
                : $"gflat${declaringClass}${methodMember.Name}";
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
                    val = EmitImplicitCast(val, argType, methodTarget.Parameters[i].Type);
                    llvmArgType = EmitParamType(methodTarget.Parameters[i].Type);
                }
                else if (target is ExternDeclaration extDecl)
                {
                    if (i < extDecl.Parameters.Count)
                    {
                        val = EmitImplicitCast(val, argType, extDecl.Parameters[i].Type);
                        llvmArgType = EmitParamType(extDecl.Parameters[i].Type);
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
                            string extOp = _typeChecker.IsUnsignedInteger(argType) ? "zext" : "sext";
                            Emit($"    {promoted} = {extOp} i8 {val} to i32");
                            val = promoted;
                            llvmArgType = "i32";
                        }
                        else if (llvmArgType == "i16")
                        {
                            string promoted = NewTemp();
                            string extOp = _typeChecker.IsUnsignedInteger(argType) ? "zext" : "sext";
                            Emit($"    {promoted} = {extOp} i16 {val} to i32");
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

        bool isThrowing = _typeChecker.CanCallThrow(node) || (target is MethodDeclaration tm && _typeChecker.CanFunctionThrow(tm));
        TypeExpression callType = _typeChecker.GetType(node);
        string retType = EmitType(callType);
        string args = string.Join(", ", argValues.Zip(argTypes, (v, t) => $"{t} {v}"));

        if (isThrowing)
        {
            if (retType == "void")
            {
                string callRes = NewTemp();
                Emit($"    {callRes} = call {{ %Exception*, i1 }} @{funcName}({args})");
                EmitCheckAndHandleCallException(callRes, isVoid: true, retType);
            }
            else
            {
                string callRes = NewTemp();
                Emit($"    {callRes} = call {{ {retType}, %Exception*, i1 }} @{funcName}({args})");
                string valTemp = EmitCheckAndHandleCallException(callRes, isVoid: false, retType);
                Push(valTemp);
            }
        }
        else
        {
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

        if (node.Object is IdentifierExpression objIdent &&
            _typeChecker.TryGetConstValueByName(objIdent.Name, out ConstValue? constObj) && constObj != null)
        {
            if (constObj is ConstValue.Pointer ptrObj)
            {
                constObj = ptrObj.Target;
            }
            if (constObj is ConstValue.Struct constStruct && constStruct.Fields.TryGetValue(node.Member, out ConstValue? fieldVal))
            {
                if (TryEmitConstValue(fieldVal, _typeChecker.GetType(node)))
                {
                    return;
                }
            }
            else if (constObj is ConstValue.ClassInstance constClass && constClass.Fields.TryGetValue(node.Member, out ConstValue? cFieldVal))
            {
                if (TryEmitConstValue(cFieldVal, _typeChecker.GetType(node)))
                {
                    return;
                }
            }
        }

        EmitMemberAddress(node);
        string fieldPtr = Pop();
        TypeExpression fieldType = _typeChecker.GetType(node);
        if (fieldType is ArrayTypeExpression arr && arr.Size.HasValue)
        {
            string arrType = EmitType(fieldType);
            string decayed = NewTemp();
            Emit($"    {decayed} = getelementptr {arrType}, {arrType}* {fieldPtr}, i32 0, i32 0");
            Push(decayed);
            return;
        }

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

        TypeExpression valueType = _typeChecker.GetType(node.Value);
        string finalVal = EmitImplicitCast(val, valueType, targetType);

        if (node.Operator != TokenKind.Equals)
        {
            string currentVal = NewTemp();
            Emit($"    {currentVal} = load {llvmType}, {llvmType}* {ptr}");
            string temp = NewTemp();

            if (_typeChecker.TryGetCompoundOperatorTarget(node, out (string StructName, OperatorDeclaration Operator) opTarget))
            {
                currentVal = EmitImplicitCast(currentVal, targetType, opTarget.Operator.Parameters[0].Type);
                val = EmitImplicitCast(val, valueType, opTarget.Operator.Parameters[1].Type);

                string mangledName = GetOperatorMangledName(opTarget.StructName, opTarget.Operator);
                string retLlvmType = EmitType(opTarget.Operator.ReturnType);
                string param0Type = EmitParamType(opTarget.Operator.Parameters[0].Type);
                string param1Type = EmitParamType(opTarget.Operator.Parameters[1].Type);

                Emit($"    {temp} = call {retLlvmType} @{mangledName}({param0Type} {currentVal}, {param1Type} {val})");
                temp = EmitImplicitCast(temp, opTarget.Operator.ReturnType, targetType);
                Emit($"    store {llvmType} {temp}, {llvmType}* {ptr}");
                Push(temp);
                return;
            }

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
                bool isUnsigned = _typeChecker.IsUnsignedInteger(targetType);
                string op = node.Operator switch
                {
                    TokenKind.PlusEquals => "add",
                    TokenKind.MinusEquals => "sub",
                    TokenKind.StarEquals => "mul",
                    TokenKind.SlashEquals => isUnsigned ? "udiv" : "sdiv",
                    TokenKind.PercentEquals => isUnsigned ? "urem" : "srem",
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
    public void Visit(NewExpression node)
    {
        TypeExpression resolvedType = _typeChecker.ResolveAlias(node.Type);
        string typeName = ((NamedTypeExpression)resolvedType).Name;
        bool isClass = _typeChecker.IsClass(typeName);
        TypeChecker.StructInfo? sInfo = isClass ? null : _typeChecker.GetStruct(typeName)!;
        TypeChecker.ClassInfo? cInfo = isClass ? _typeChecker.GetClass(typeName)! : null;
        string typeNs = isClass ? cInfo!.Namespace : sInfo!.Namespace;
        ConstructorDeclaration? ctor = _typeChecker.GetResolvedConstructor(node);

        List<string> argVals = new();
        foreach (AstNode arg in node.Arguments)
        {
            arg.Accept(this);
            argVals.Add(Pop());
        }

        bool hasCtor = isClass || ctor != null || sInfo!.Constructors.Any(c => c.Parameters.Count == 0) ||
                       (sInfo.Constructors.Count == 0 && sInfo.FieldDeclarations.Any(f => f.Initializer != null));

        if (node.Kind == AllocationKind.Value)
        {
            string temp = NewTemp();
            Emit($"    {temp} = alloca %{typeName}");
            Emit($"    store %{typeName} zeroinitializer, %{typeName}* {temp}");

            if (hasCtor)
            {
                string mangledName = GetConstructorMangledName(typeName, ctor, typeNs);
                List<string> callArgs = new() { $"%{typeName}* {temp}" };
                if (ctor != null)
                {
                    for (int i = 0; i < ctor.Parameters.Count; i++)
                    {
                        TypeExpression argType = _typeChecker.GetType(node.Arguments[i]);
                        string castVal = EmitImplicitCast(argVals[i], argType, ctor.Parameters[i].Type);
                        callArgs.Add($"{EmitParamType(ctor.Parameters[i].Type)} {castVal}");
                    }
                }
                Emit($"    call void @{mangledName}({string.Join(", ", callArgs)})");
            }

            string val = NewTemp();
            Emit($"    {val} = load %{typeName}, %{typeName}* {temp}");
            Push(val);
        }
        else if (node.Kind == AllocationKind.Pointer)
        {
            if (!_externNames.Contains("malloc"))
            {
                _externNames.Add("malloc");
                EmitGlobal("declare i8* @malloc(i64)");
            }

            string sizePtr = NewTemp();
            Emit($"    {sizePtr} = getelementptr %{typeName}, %{typeName}* null, i32 1");
            string sizeInt = NewTemp();
            Emit($"    {sizeInt} = ptrtoint %{typeName}* {sizePtr} to i64");

            string rawMem = NewTemp();
            Emit($"    {rawMem} = call i8* @malloc(i64 {sizeInt})");
            string typedPtr = NewTemp();
            Emit($"    {typedPtr} = bitcast i8* {rawMem} to %{typeName}*");
            Emit($"    store %{typeName} zeroinitializer, %{typeName}* {typedPtr}");

            if (hasCtor)
            {
                string mangledName = GetConstructorMangledName(typeName, ctor, typeNs);
                List<string> callArgs = new() { $"%{typeName}* {typedPtr}" };
                if (ctor != null)
                {
                    for (int i = 0; i < ctor.Parameters.Count; i++)
                    {
                        TypeExpression argType = _typeChecker.GetType(node.Arguments[i]);
                        string castVal = EmitImplicitCast(argVals[i], argType, ctor.Parameters[i].Type);
                        callArgs.Add($"{EmitParamType(ctor.Parameters[i].Type)} {castVal}");
                    }
                }
                Emit($"    call void @{mangledName}({string.Join(", ", callArgs)})");
            }

            Push(typedPtr);
        }
        else
        {
            throw new NotImplementedException($"Allocation kind {node.Kind} not supported");
        }
    }

    public void Visit(DefaultExpression node)
    {
        TypeExpression type = _typeChecker.GetType(node);
        Push(GetDefaultValue(type));
    }

    public void Visit(SizeofExpression node)
    {
        if (_typeChecker.TryGetConstValue(node, out ConstValue? constVal) && constVal != null)
        {
            if (TryEmitConstValue(constVal, _typeChecker.GetType(node)))
            {
                return;
            }
        }
        int size = _typeChecker.GetTypeSize(node.TargetType);
        Push(size.ToString());
    }

    public void Visit(NameofExpression node)
    {
        if (_typeChecker.TryGetConstValue(node, out ConstValue? constVal) && constVal != null)
        {
            if (TryEmitConstValue(constVal, _typeChecker.GetType(node)))
            {
                return;
            }
        }
        string name = _typeChecker.ExtractName(node.Target);
        TryEmitConstValue(new ConstValue.String(name), _typeChecker.GetType(node));
    }

    public void Visit(PrefixedStringLiteralExpression node)
    {
        if (node.Prefix == "c")
        {
            node.Literal.Accept(this);
            return;
        }

        if (_typeChecker.TryGetConstValue(node, out ConstValue? constVal) && constVal != null)
        {
            if (TryEmitConstValue(constVal, _typeChecker.GetType(node)))
            {
                return;
            }
        }

        TypeChecker.StringPrefixHandler handler = _typeChecker.GetResolvedPrefixHandler(node)!;
        node.Literal.Accept(this);
        string strArg = Pop();

        if (handler.IsConstructor)
        {
            string typeName = handler.EnclosingTypeName!;
            string temp = NewTemp();
            Emit($"    {temp} = alloca %{typeName}");
            Emit($"    store %{typeName} zeroinitializer, %{typeName}* {temp}");

            string mangledName = GetConstructorMangledName(typeName, handler.Constructor, handler.Namespace);
            Emit($"    call void @{mangledName}(%{typeName}* {temp}, i8* {strArg})");

            string val = NewTemp();
            Emit($"    {val} = load %{typeName}, %{typeName}* {temp}");
            Push(val);
        }
        else if (handler.Method != null)
        {
            string ns = handler.Namespace;
            string funcName;
            if (ns.Length > 0)
            {
                funcName = handler.EnclosingTypeName != null
                    ? $"gflat${ns}${handler.EnclosingTypeName}${handler.Method.Name}"
                    : $"gflat${ns}${handler.Method.Name}";
            }
            else
            {
                funcName = handler.EnclosingTypeName != null
                    ? $"gflat${handler.EnclosingTypeName}${handler.Method.Name}"
                    : $"gflat${handler.Method.Name}";
            }
            string retType = EmitType(handler.ReturnType);
            if (retType == "void")
            {
                Emit($"    call void @{funcName}(i8* {strArg})");
            }
            else
            {
                string temp = NewTemp();
                Emit($"    {temp} = call {retType} @{funcName}(i8* {strArg})");
                Push(temp);
            }
        }
        else
        {
            throw new NotImplementedException("Unsupported string prefix handler kind");
        }
    }

    private string GetDefaultValue(TypeExpression type)
    {
        type = _typeChecker.ResolveAlias(type);
        if (type is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression)
            return "null";
        if (type is ArrayTypeExpression)
            return "zeroinitializer";
        if (type is NamedTypeExpression named)
        {
            if (_typeChecker.GetStruct(named.Name) != null || _typeChecker.IsInterface(named.Name))
                return "zeroinitializer";
            if (_typeChecker.GetEnum(named.Name) != null)
                return "0";
            if (named.Name is "float" or "double")
                return "0.0";
            return "0";
        }
        return "0";
    }
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
    public void Visit(NestedTypeExpression node) => throw new NotImplementedException();
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

    public void Visit(CastExpression node)
    {
        node.Operand.Accept(this);
        string val = Pop();
        TypeExpression srcType = _typeChecker.GetType(node.Operand);
        TypeExpression dstType = node.TargetType;
        string castVal = EmitCast(val, srcType, dstType);
        Push(castVal);
    }

    public void Visit(LambdaExpression node)
    {
        int id = _lambdaCounter++;
        string funcName = $"gflat$lambda${id}";

        TypeExpression returnType = _typeChecker.GetLambdaReturnType(node);
        string llvmReturnType = EmitType(returnType);

        // Save current emitter state
        StringBuilder savedOutput = _output;
        Dictionary<string, string> savedLocals = new(_locals);
        int savedTempCounter = _tempCounter;
        string savedReturnType = _currentFunctionReturnType;
        TypeExpression? savedExpectedType = _currentFunctionExpectedType;
        bool savedTerminated = _hasTerminated;
        List<List<AstNode>> savedDeferScopes = new(_deferScopes);

        // Prepare lambda context
        _output = _lambdaFunctions;
        _locals.Clear();
        _tempCounter = 0;
        _currentFunctionReturnType = llvmReturnType;
        _currentFunctionExpectedType = returnType;
        _hasTerminated = false;
        _deferScopes.Clear();

        string parameters = string.Join(", ", node.Parameters.Select(p => $"{EmitParamType(p.Type)} %{p.Name}"));
        Emit($"define {llvmReturnType} @{funcName}({parameters}) {{");
        Emit("entry:");

        foreach (Parameter p in node.Parameters)
        {
            string pType = EmitType(p.Type);
            string allocaPtr = NewTemp();
            Emit($"    {allocaPtr} = alloca {pType}");
            Emit($"    store {pType} %{p.Name}, {pType}* {allocaPtr}");
            _locals[p.Name] = allocaPtr;
        }

        if (node.IsExpressionBody)
        {
            node.Body.Accept(this);
            if (llvmReturnType == "void")
            {
                Emit("    ret void");
            }
            else
            {
                string res = Pop();
                TypeExpression bodyType = _typeChecker.GetType(node.Body);
                res = EmitImplicitCast(res, bodyType, returnType);
                Emit($"    ret {llvmReturnType} {res}");
            }
            _hasTerminated = true;
        }
        else
        {
            node.Body.Accept(this);
            if (!_hasTerminated)
            {
                if (llvmReturnType == "void")
                {
                    Emit("    ret void");
                }
                else
                {
                    Emit($"    ret {llvmReturnType} zeroinitializer");
                }
            }
        }

        Emit("}");
        Emit("");

        // Restore enclosing emitter state
        _output = savedOutput;
        _locals.Clear();
        foreach (KeyValuePair<string, string> kvp in savedLocals)
        {
            _locals[kvp.Key] = kvp.Value;
        }
        _tempCounter = savedTempCounter;
        _currentFunctionReturnType = savedReturnType;
        _currentFunctionExpectedType = savedExpectedType;
        _hasTerminated = savedTerminated;
        _deferScopes.Clear();
        _deferScopes.AddRange(savedDeferScopes);

        // Push the function address as a function pointer
        Push($"@{funcName}");
    }

    private void HandleException(string err, string owned)
    {
        if (_emitterTryStack.Count > 0)
        {
            TryBlockInfo tryInfo = _emitterTryStack.Peek();
            EmitDefersDownTo(tryInfo.DeferDepth);
            Emit($"    store %Exception* {err}, %Exception** {tryInfo.ErrSlot}");
            Emit($"    store i1 {owned}, i1* {tryInfo.OwnedSlot}");
            Emit($"    br label %{tryInfo.DispatchLabel}");
            _hasTerminated = true;
        }
        else if (_isInsideMain)
        {
            EmitDefersDownTo(0);
            Emit($"    store %Exception* {err}, %Exception** {_mainErrSlot!}");
            Emit($"    store i1 {owned}, i1* {_mainOwnedSlot!}");
            Emit($"    br label %{_mainUnhandledLabel!}");
            _hasTerminated = true;
        }
        else if (!_currentFunctionIsThrowing)
        {
            Emit("    unreachable");
            _hasTerminated = true;
        }
        else
        {
            // Propagate through current function return
            EmitDefersDownTo(0);
            if (_currentFunctionIsVoid)
            {
                string retVal = NewTemp();
                string retVal2 = NewTemp();
                Emit($"    {retVal} = insertvalue {{ %Exception*, i1 }} zeroinitializer, %Exception* {err}, 0");
                Emit($"    {retVal2} = insertvalue {{ %Exception*, i1 }} {retVal}, i1 {owned}, 1");
                Emit($"    ret {{ %Exception*, i1 }} {retVal2}");
            }
            else
            {
                string retVal = NewTemp();
                string retVal2 = NewTemp();
                Emit($"    {retVal} = insertvalue {{ {_currentFunctionBaseReturnType}, %Exception*, i1 }} zeroinitializer, %Exception* {err}, 1");
                Emit($"    {retVal2} = insertvalue {{ {_currentFunctionBaseReturnType}, %Exception*, i1 }} {retVal}, i1 {owned}, 2");
                Emit($"    ret {{ {_currentFunctionBaseReturnType}, %Exception*, i1 }} {retVal2}");
            }
            _hasTerminated = true;
        }
    }

    private string EmitCheckAndHandleCallException(string callRes, bool isVoid, string retType)
    {
        string err;
        string owned;
        string val = "";
        if (isVoid)
        {
            err = NewTemp();
            Emit($"    {err} = extractvalue {{ %Exception*, i1 }} {callRes}, 0");
            owned = NewTemp();
            Emit($"    {owned} = extractvalue {{ %Exception*, i1 }} {callRes}, 1");
        }
        else
        {
            val = NewTemp();
            Emit($"    {val} = extractvalue {{ {retType}, %Exception*, i1 }} {callRes}, 0");
            err = NewTemp();
            Emit($"    {err} = extractvalue {{ {retType}, %Exception*, i1 }} {callRes}, 1");
            owned = NewTemp();
            Emit($"    {owned} = extractvalue {{ {retType}, %Exception*, i1 }} {callRes}, 2");
        }

        string hasErr = NewTemp();
        Emit($"    {hasErr} = icmp ne %Exception* {err}, null");
        string callErrLbl = NewLabel("call_err");
        string callNextLbl = NewLabel("call_next");
        Emit($"    br i1 {hasErr}, label %{callErrLbl}, label %{callNextLbl}");

        Emit($"{callErrLbl}:");
        HandleException(err, owned);

        Emit($"{callNextLbl}:");
        _hasTerminated = false;
        return val;
    }

    private void EmitIsInstanceHelper()
    {
        _tempCounter = 0;
        Emit("define i8** @gflat_get_parent_vtable(i8** %vt) {");
        Emit("entry:");
        int idx = 0;
        foreach (TypeChecker.ClassInfo cls in _typeChecker.GetAllClasses())
        {
            string checkNext = $"check_{idx++}";
            string matchLbl = $"match_{idx++}";
            int vtSize = cls.VirtualMethods.Count;
            string vtGlobal = NewTemp();
            if (vtSize > 0)
            {
                Emit($"    {vtGlobal} = bitcast [{vtSize} x i8*]* @{cls.Name}$vtable to i8**");
            }
            else
            {
                Emit($"    {vtGlobal} = bitcast [0 x i8*]* @{cls.Name}$vtable to i8**");
            }
            string cmp = NewTemp();
            Emit($"    {cmp} = icmp eq i8** %vt, {vtGlobal}");
            Emit($"    br i1 {cmp}, label %{matchLbl}, label %{checkNext}");
            Emit($"{matchLbl}:");
            if (cls.BaseClass != null)
            {
                TypeChecker.ClassInfo? baseCls = _typeChecker.GetClass(cls.BaseClass);
                int baseVtSize = baseCls != null ? baseCls.VirtualMethods.Count : 0;
                string baseGlobal = NewTemp();
                if (baseVtSize > 0)
                {
                    Emit($"    {baseGlobal} = bitcast [{baseVtSize} x i8*]* @{cls.BaseClass}$vtable to i8**");
                }
                else
                {
                    Emit($"    {baseGlobal} = bitcast [0 x i8*]* @{cls.BaseClass}$vtable to i8**");
                }
                Emit($"    ret i8** {baseGlobal}");
            }
            else
            {
                Emit("    ret i8** null");
            }
            Emit($"{checkNext}:");
        }
        Emit("    ret i8** null");
        Emit("}");
        Emit("");

        _tempCounter = 0;
        Emit("define i1 @gflat_is_instance(i8** %obj_vt, i8** %target_vt) {");
        Emit("entry:");
        Emit("    %is_target_null = icmp eq i8** %target_vt, null");
        Emit("    br i1 %is_target_null, label %found, label %loop_entry");
        Emit("loop_entry:");
        Emit("    br label %loop");
        Emit("loop:");
        Emit("    %curr = phi i8** [ %obj_vt, %loop_entry ], [ %parent, %next ]");
        Emit("    %is_null = icmp eq i8** %curr, null");
        Emit("    br i1 %is_null, label %not_found, label %check");
        Emit("check:");
        Emit("    %is_match = icmp eq i8** %curr, %target_vt");
        Emit("    br i1 %is_match, label %found, label %next");
        Emit("next:");
        Emit("    %parent = call i8** @gflat_get_parent_vtable(i8** %curr)");
        Emit("    br label %loop");
        Emit("found:");
        Emit("    ret i1 1");
        Emit("not_found:");
        Emit("    ret i1 0");
        Emit("}");
        Emit("");
    }

    public void Visit(ThrowStatement node)
    {
        node.Expression.Accept(this);
        string thrownVal = Pop();
        TypeExpression thrownType = _typeChecker.GetType(node.Expression);
        string llvmThrownType = EmitType(thrownType);

        string err = NewTemp();
        Emit($"    {err} = bitcast {llvmThrownType} {thrownVal} to %Exception*");

        string owned;
        if (node.Expression is IdentifierExpression ident && _catchVariableOwnedSlots.TryGetValue(ident.Name, out string? catchOwnedSlot))
        {
            owned = NewTemp();
            Emit($"    {owned} = load i1, i1* {catchOwnedSlot}");
            Emit($"    store i1 0, i1* {catchOwnedSlot}");
        }
        else if (node.Expression is NewExpression { Kind: AllocationKind.Pointer })
        {
            owned = "1";
        }
        else
        {
            owned = "0";
        }

        HandleException(err, owned);
    }

    public void Visit(TryStatement node)
    {
        string errSlot = NewTemp();
        Emit($"    {errSlot} = alloca %Exception*");
        Emit($"    store %Exception* null, %Exception** {errSlot}");

        string ownedSlot = NewTemp();
        Emit($"    {ownedSlot} = alloca i1");
        Emit($"    store i1 0, i1* {ownedSlot}");

        string dispatchLbl = NewLabel("catch_dispatch");
        string tryEndLbl = NewLabel("try_end");

        _emitterTryStack.Push(new TryBlockInfo(node, errSlot, ownedSlot, dispatchLbl, _deferScopes.Count));

        node.TryBlock.Accept(this);

        _emitterTryStack.Pop();

        if (!_hasTerminated)
        {
            Emit($"    br label %{tryEndLbl}");
        }

        Emit($"{dispatchLbl}:");
        _hasTerminated = false;

        string caughtErr = NewTemp();
        Emit($"    {caughtErr} = load %Exception*, %Exception** {errSlot}");
        string caughtOwned = NewTemp();
        Emit($"    {caughtOwned} = load i1, i1* {ownedSlot}");

        string vtSlot = NewTemp();
        Emit($"    {vtSlot} = getelementptr %Exception, %Exception* {caughtErr}, i32 0, i32 0");
        string caughtVt = NewTemp();
        Emit($"    {caughtVt} = load i8**, i8*** {vtSlot}");

        string unmatchedLbl = NewLabel("catch_unmatched");
        bool hasUnmatchedPath = false;

        for (int i = 0; i < node.CatchClauses.Count; i++)
        {
            CatchClause clause = node.CatchClauses[i];
            string clauseBodyLbl = NewLabel($"catch_body_{i}");
            string? nextCheckLbl = (i + 1 < node.CatchClauses.Count)
                ? NewLabel($"catch_check_{i + 1}")
                : null;

            if (clause.ExceptionType == null)
            {
                // Catch-all: always matches
                Emit($"    br label %{clauseBodyLbl}");
            }
            else
            {
                TypeExpression resolvedType = _typeChecker.ResolveAlias(clause.ExceptionType);
                TypeExpression innerType = resolvedType is PointerTypeExpression pt
                    ? pt.Inner
                    : (resolvedType is ManagedTypeExpression mt ? mt.Inner : resolvedType);
                string targetClassName = ((NamedTypeExpression)innerType).Name;
                TypeChecker.ClassInfo? targetClass = _typeChecker.GetClass(targetClassName);
                int targetVtSize = targetClass != null ? targetClass.VirtualMethods.Count : 0;

                string targetVtPtr = NewTemp();
                if (targetVtSize > 0)
                {
                    Emit($"    {targetVtPtr} = bitcast [{targetVtSize} x i8*]* @{targetClassName}$vtable to i8**");
                }
                else
                {
                    Emit($"    {targetVtPtr} = bitcast [0 x i8*]* @{targetClassName}$vtable to i8**");
                }

                string isMatch = NewTemp();
                Emit($"    {isMatch} = call i1 @gflat_is_instance(i8** {caughtVt}, i8** {targetVtPtr})");

                string targetFalse = nextCheckLbl ?? unmatchedLbl;
                if (nextCheckLbl == null)
                {
                    hasUnmatchedPath = true;
                }
                Emit($"    br i1 {isMatch}, label %{clauseBodyLbl}, label %{targetFalse}");
            }

            Emit($"{clauseBodyLbl}:");
            _hasTerminated = false;

            string? prevLocal = null;
            if (clause.VariableName != null)
            {
                _locals.TryGetValue(clause.VariableName, out prevLocal);
                string varType = EmitType(clause.ExceptionType!);
                string typedEx = NewTemp();
                Emit($"    {typedEx} = bitcast %Exception* {caughtErr} to {varType}");
                string exSlot = NewTemp();
                Emit($"    {exSlot} = alloca {varType}");
                Emit($"    store {varType} {typedEx}, {varType}* {exSlot}");
                _locals[clause.VariableName] = exSlot;

                string varOwnedSlot = NewTemp();
                Emit($"    {varOwnedSlot} = alloca i1");
                Emit($"    store i1 {caughtOwned}, i1* {varOwnedSlot}");
                _catchVariableOwnedSlots[clause.VariableName] = varOwnedSlot;

                _activeCatchStack.Push(new ActiveCatchInfo(caughtErr, caughtOwned));
            }

            clause.Body.Accept(this);

            if (clause.VariableName != null)
            {
                _activeCatchStack.Pop();
                _catchVariableOwnedSlots.Remove(clause.VariableName);
                if (prevLocal != null)
                {
                    _locals[clause.VariableName] = prevLocal;
                }
                else
                {
                    _locals.Remove(clause.VariableName);
                }
            }

            if (!_hasTerminated)
            {
                if (!_externNames.Contains("free"))
                {
                    _externNames.Add("free");
                    EmitGlobal("declare void @free(i8*)");
                }
                string freeLbl = NewLabel("catch_free");
                string afterFreeLbl = NewLabel("catch_after_free");
                Emit($"    br i1 {caughtOwned}, label %{freeLbl}, label %{afterFreeLbl}");
                Emit($"{freeLbl}:");
                string raw = NewTemp();
                Emit($"    {raw} = bitcast %Exception* {caughtErr} to i8*");
                Emit($"    call void @free(i8* {raw})");
                Emit($"    br label %{afterFreeLbl}");
                Emit($"{afterFreeLbl}:");
                Emit($"    br label %{tryEndLbl}");
            }

            if (nextCheckLbl != null)
            {
                Emit($"{nextCheckLbl}:");
                _hasTerminated = false;
            }
        }

        if (hasUnmatchedPath)
        {
            Emit($"{unmatchedLbl}:");
            _hasTerminated = false;
            HandleException(caughtErr, caughtOwned);
        }

        Emit($"{tryEndLbl}:");
        _hasTerminated = false;
    }

    public void Visit(CatchClause node)
    {
    }
}