using System.Text;
using gflat.ast;
using gflat.comptime;

namespace gflat;

public partial class LlvmEmitter : IVisitor
{
    private static string InlineHint(AstNode? node) => node?.IsInline == true ? " inlinehint" : "";
    private string RuntimeMemorySymbol(string name) => "gflat$Memory$" + name +
        (_typeChecker.GetMemoryFunction(name) is { } method ? _typeChecker.GetOverloadSuffix(method) : "");
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
            case ConstValue.Array array when type is ArrayTypeExpression arrayType:
                return $"{typeStr} [{string.Join(", ", array.Elements.Select(e => EmitConstInitializer(e, arrayType.ElementType)))}]";
            case ConstValue.Integer { Value: 0 } when type is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression:
                return $"{typeStr} null";
            case ConstValue.Integer i:
                return $"{typeStr} {i.Value}";
            case ConstValue.UInteger u:
                return $"{typeStr} {u.Value}";
            case ConstValue.Float f:
                return $"{typeStr} {FloatConstant(f.Value, type)}";
            case ConstValue.Boolean b:
                return $"i1 {(b.Value ? "1" : "0")}";
            case ConstValue.Char c:
                return $"i8 {(int)c.Value}";
            case ConstValue.String s:
                {
                    string raw = s.Value;
                    byte[] escaped = StringLiteralEncoding.Bytes(raw);
                    string globalName = NewGlobal();
                    int len = escaped.Length + 1;
                    string llvmStr = StringLiteralEncoding.LlvmBytes(escaped);
                    if (type is ArrayTypeExpression { Size: not null } buffer)
                        return $"{typeStr} [{string.Join(", ", Enumerable.Range(0, buffer.Size.Value).Select(i => "i8 " + (i < escaped.Length ? escaped[i] : 0)))}]";
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
            case ConstValue.ArrayPointer arrayPointer:
                return $"{typeStr} {ConstArrayAddress(arrayPointer, typeStr)}";
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

    private string FloatConstant(double value, TypeExpression type)
    {
        if (_typeChecker.ResolveAlias(type) is NamedTypeExpression { Name: "float" }) value = (double)(float)value;
        return "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16");
    }

    private bool TryEmitConstValue(ConstValue constVal, TypeExpression type)
    {
        if (constVal is ConstValue.Integer { Value: 0 } &&
            _typeChecker.ResolveAlias(type) is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression)
        {
            Push("null");
            return true;
        }
        switch (constVal)
        {
            case ConstValue.Array array when _typeChecker.ResolveAlias(type) is ArrayTypeExpression arrayType:
                string global = NewGlobal();
                string llvmType = EmitType(arrayType);
                EmitGlobal($"{global} = private constant {EmitConstInitializer(array, arrayType)}");
                string first = NewTemp();
                Emit($"    {first} = getelementptr {llvmType}, {llvmType}* {global}, i32 0, i32 0");
                Push(first);
                return true;
            case ConstValue.Integer i:
                Push(i.Value.ToString());
                return true;
            case ConstValue.UInteger u:
                Push(u.Value.ToString());
                return true;
            case ConstValue.Float f:
                Push(FloatConstant(f.Value, type));
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
                    byte[] escaped = StringLiteralEncoding.Bytes(raw);
                    string globalName = NewGlobal();
                    int len = escaped.Length + 1;
                    string llvmStr = StringLiteralEncoding.LlvmBytes(escaped);
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
            case ConstValue.ArrayPointer arrayPointer:
                Push(ConstArrayAddress(arrayPointer, EmitType(type)));
                return true;
            case ConstValue.Struct structVal:
                {
                    TypeChecker.StructInfo? sInfo = _typeChecker.GetStruct(structVal.StructName);
                    if (sInfo != null)
                    {
                        string temp = NewTemp();
                        EmitAlloca($"%{structVal.StructName}", temp);
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
    private bool _isInsideMain = false;

    private record TryBlockInfo(TryStatement Statement, string OwnedSlot, string DispatchLabel, int DeferDepth, string UnwindLabel);
    private readonly Stack<TryBlockInfo> _emitterTryStack = new();

    private interface IDeferAction
    {
        void Execute(LlvmEmitter emitter);
    }

    private sealed class CatchCleanupAction(string handleSlot) : IDeferAction
    {
        public void Execute(LlvmEmitter emitter)
        {
            string handle = emitter.NewTemp();
            emitter.Emit($"    {handle} = load i8*, i8** {handleSlot}");
            emitter.Emit($"    store i8* null, i8** {handleSlot}");
            emitter.Emit($"    call void @__gflat_eh_release(i8* {handle})");
        }
    }

    private class AstNodeDeferAction : IDeferAction
    {
        public AstNode Node { get; }
        private readonly Dictionary<string, string> _bindings;
        public AstNodeDeferAction(AstNode node, Dictionary<string, string> bindings)
        {
            Node = node;
            _bindings = new(bindings);
        }
        public void Execute(LlvmEmitter emitter)
        {
            var saved = new Dictionary<string, string>(emitter._locals);
            emitter._locals.Clear();
            foreach (var binding in _bindings) emitter._locals.Add(binding.Key, binding.Value);
            Node.Accept(emitter);
            emitter._locals.Clear();
            foreach (var binding in saved) emitter._locals.Add(binding.Key, binding.Value);
        }
    }

    private class StructDestructorDeferAction : IDeferAction
    {
        public string StructName { get; }
        public string LocalPtr { get; }
        public string? Namespace { get; }

        public StructDestructorDeferAction(string structName, string localPtr, string? ns)
        {
            StructName = structName;
            LocalPtr = localPtr;
            Namespace = ns;
        }

        public void Execute(LlvmEmitter emitter)
        {
            string dtorNs = Namespace ?? "";
            string dtorMangled = dtorNs.Length > 0
                ? $"gflat${dtorNs}${StructName}$dtor"
                : $"gflat${StructName}$dtor";
            emitter.Emit($"    call void @{dtorMangled}(%{StructName}* {LocalPtr})");
        }
    }

    private class ClassDestructorDeferAction : IDeferAction
    {
        public TypeChecker.ClassInfo Class { get; }
        public string LocalPtr { get; }
        public bool IsVirtual { get; }
        public int SlotIndex { get; }

        public ClassDestructorDeferAction(TypeChecker.ClassInfo cls, string localPtr, bool isVirtual, int slotIndex)
        {
            Class = cls;
            LocalPtr = localPtr;
            IsVirtual = isVirtual;
            SlotIndex = slotIndex;
        }

        public void Execute(LlvmEmitter emitter)
        {
            string vtableSlot = emitter.NewTemp();
            emitter.Emit($"    {vtableSlot} = getelementptr %{Class.Name}, %{Class.Name}* {LocalPtr}, i32 0, i32 0");
            string vtablePtr = emitter.NewTemp();
            emitter.Emit($"    {vtablePtr} = load i8**, i8*** {vtableSlot}");
            string isNonNull = emitter.NewTemp();
            emitter.Emit($"    {isNonNull} = icmp ne i8** {vtablePtr}, null");
            string dtorCallLbl = emitter.NewLabel("dtor.call");
            string dtorDoneLbl = emitter.NewLabel("dtor.done");
            emitter.Emit($"    br i1 {isNonNull}, label %{dtorCallLbl}, label %{dtorDoneLbl}");
            emitter.Emit($"{dtorCallLbl}:");

            if (IsVirtual && SlotIndex >= 0)
            {
                string slotPtr = emitter.NewTemp();
                emitter.Emit($"    {slotPtr} = getelementptr i8*, i8** {vtablePtr}, i32 {SlotIndex}");
                string rawFnPtr = emitter.NewTemp();
                emitter.Emit($"    {rawFnPtr} = load i8*, i8** {slotPtr}");
                string dtorFn = emitter.NewTemp();
                emitter.Emit($"    {dtorFn} = bitcast i8* {rawFnPtr} to void (%{Class.Name}*)*");
                emitter.Emit($"    call void {dtorFn}(%{Class.Name}* {LocalPtr})");
            }
            else
            {
                string dtorNs = Class.Namespace;
                string dtorMangled = dtorNs.Length > 0
                    ? $"gflat${dtorNs}${Class.Name}$dtor"
                    : $"gflat${Class.Name}$dtor";
                emitter.Emit($"    call void @{dtorMangled}(%{Class.Name}* {LocalPtr})");
            }

            emitter.Emit($"    store i8** null, i8*** {vtableSlot}");
            emitter.Emit($"    br label %{dtorDoneLbl}");
            emitter.Emit($"{dtorDoneLbl}:");
        }
    }

    private readonly List<List<IDeferAction>> _deferScopes = new();
    private readonly Stack<int> _loopDeferDepths = new();
    private bool _hasTerminated = false;
    private string? _destructorExitLabel;
    private int _expressionDepth;
    private StringBuilder? _temporaryPrologue;

    private sealed class ConditionalTemporaryCleanup(TypeExpression type, string address, string liveSlot) : IDeferAction
    {
        public void Execute(LlvmEmitter emitter)
        {
            string live = emitter.NewTemp();
            string destroy = emitter.NewLabel("temporary_destroy");
            string done = emitter.NewLabel("temporary_done");
            emitter.Emit($"    {live} = load i1, i1* {liveSlot}");
            emitter.Emit($"    br i1 {live}, label %{destroy}, label %{done}");
            emitter.Emit($"{destroy}:");
            emitter.Emit($"    store i1 false, i1* {liveSlot}");
            emitter.EmitDestroy(type, address);
            emitter.Emit($"    br label %{done}");
            emitter.Emit($"{done}:");
        }
    }

    private sealed class TemporaryCleanup(TypeExpression type, string address) : IDeferAction
    {
        public string Address => address;
        public void Execute(LlvmEmitter emitter) => emitter.EmitDestroy(type, address);
    }

    private void EmitExpressionWithCleanup(Action emit)
    {
        bool outermost = _expressionDepth++ == 0;
        StringBuilder? previousPrologue = _temporaryPrologue;
        int prologuePosition = _output.Length;
        if (outermost)
        {
            _deferScopes.Add(new());
            _temporaryPrologue = new();
        }
        try
        {
            emit();
            if (outermost)
            {
                // Storage and live flags must dominate both sides of short-circuit
                // branches. Construction still happens only on the evaluated path.
                _output.Insert(prologuePosition, _temporaryPrologue!.ToString());
                if (!_hasTerminated) EmitDefersDownTo(_deferScopes.Count - 1);
            }
        }
        finally
        {
            if (outermost)
            {
                _deferScopes.RemoveAt(_deferScopes.Count - 1);
                _temporaryPrologue = previousPrologue;
            }
            _expressionDepth--;
        }
    }

    private void EmitDestroy(TypeExpression type, string address)
    {
        type = _typeChecker.ResolveAlias(type);
        if (type is ArrayTypeExpression { Size: not null } array && NeedsCleanup(array.ElementType))
        {
            string llvmType = EmitType(array);
            string entry = NewLabel("array_destroy_entry");
            string loop = NewLabel("array_destroy_loop");
            string body = NewLabel("array_destroy_body");
            string advance = NewLabel("array_destroy_advance");
            string done = NewLabel("array_destroy_done");
            string remaining = NewTemp();
            string index = NewTemp();
            string empty = NewTemp();
            string element = NewTemp();
            Emit($"    br label %{entry}");
            Emit($"{entry}:");
            Emit($"    br label %{loop}");
            Emit($"{loop}:");
            Emit($"    {remaining} = phi i64 [{array.Size.Value}, %{entry}], [{index}, %{advance}]");
            Emit($"    {empty} = icmp eq i64 {remaining}, 0");
            Emit($"    br i1 {empty}, label %{done}, label %{body}");
            Emit($"{body}:");
            Emit($"    {index} = sub i64 {remaining}, 1");
            Emit($"    {element} = getelementptr {llvmType}, {llvmType}* {address}, i32 0, i64 {index}");
            EmitDestroy(array.ElementType, element);
            Emit($"    br label %{advance}");
            Emit($"{advance}:");
            Emit($"    br label %{loop}");
            Emit($"{done}:");
            return;
        }
        if (!_typeChecker.HasDestructor(type) && _typeChecker.UsesCollector && HasManagedReferences(type))
        {
            EmitManagedReferences(type, address, false);
            return;
        }
        if (type is not NamedTypeExpression named || (!_typeChecker.HasDestructor(type) && !_typeChecker.IsClass(named.Name))) return;
        if (_typeChecker.GetStruct(named.Name) is { } structure)
            new StructDestructorDeferAction(structure.Name, address, structure.Namespace).Execute(this);
        else if (_typeChecker.GetClass(named.Name) is { } cls)
            new ClassDestructorDeferAction(cls, address, cls.DestructorSlot >= 0, cls.DestructorSlot).Execute(this);
    }

    private void EmitFieldDestruction(string owner, IEnumerable<(int Index, TypeExpression Type)> fields)
    {
        foreach (var field in fields.Reverse())
        {
            if (!NeedsCleanup(field.Type)) continue;
            string address = NewTemp();
            Emit($"    {address} = getelementptr %{owner}, %{owner}* %this, i32 0, i32 {field.Index}");
            EmitDestroy(field.Type, address);
        }
    }

    private readonly HashSet<IDeferAction> _activeCleanupActions = new();

    private void EmitDefersDownTo(int targetDepth, string? skipLocalPtr = null, IDeferAction? skipAction = null)
    {
        List<IDeferAction> toEmit = new();
        for (int scopeIdx = _deferScopes.Count - 1; scopeIdx >= targetDepth; scopeIdx--)
        {
            List<IDeferAction> scope = _deferScopes[scopeIdx];
            for (int i = scope.Count - 1; i >= 0; i--)
            {
                IDeferAction action = scope[i];
                if (ReferenceEquals(action, skipAction)) continue;
                if (skipLocalPtr != null)
                {
                    if (action is TemporaryCleanup cleanup && cleanup.Address == skipLocalPtr) continue;
                    if (action is StructDestructorDeferAction sAction && sAction.LocalPtr == skipLocalPtr)
                    {
                        continue;
                    }
                    if (action is ClassDestructorDeferAction cAction && cAction.LocalPtr == skipLocalPtr)
                    {
                        continue;
                    }
                }
                toEmit.Add(action);
            }
        }
        var added = new List<IDeferAction>();
        try
        {
            foreach (IDeferAction action in toEmit)
            {
                if (!_activeCleanupActions.Add(action)) continue;
                added.Add(action);
                action.Execute(this);
                if (_hasTerminated) break;
            }
        }
        finally { foreach (var action in added) _activeCleanupActions.Remove(action); }
    }

    private string NewTemp() => $"%t{_tempCounter++}";
    private string NewGlobal() => $"@str{_stringCounter++}";

    private void Push(string value) => _valueStack.Push(value);
    private string Pop() => _valueStack.Pop();

    private void Emit(string line) => EmitNativeLine(line);
    private void EmitGlobal(string line) => _globals.AppendLine(line);

    private void GuardNonNull(string value, string type)
    {
        if (type == "{ i8*, i8** }")
        {
            string instance = NewTemp();
            Emit($"    {instance} = extractvalue {type} {value}, 0");
            value = instance;
            type = "i8*";
        }
        string ok = NewLabel("nonnull");
        string fail = NewLabel("null_failure");
        string test = NewTemp();
        Emit($"    {test} = icmp ne {type} {value}, null");
        Emit($"    br i1 {test}, label %{ok}, label %{fail}");
        Emit($"{fail}:");
        Emit("    call void @llvm.trap()");
        Emit("    unreachable");
        Emit($"{ok}:");
    }

    private string OrderedGlobals()
    {
        string[] lines = _globals.ToString().Split('\n');
        // Named aggregate types must be defined before static aggregate
        // initializers, including recursively generated type descriptors.
        return string.Join('\n', lines.Where(l => l.StartsWith('%')).Concat(lines.Where(l => !l.StartsWith('%'))));
    }

    public string GetOutput()
    {
        string body = OrderedGlobals() + "\n" + llvmFunctions + "\n" + _lambdaFunctions + "\n" + _output;
        bool needsExceptionRuntime = body.Contains("@__gflat_eh_", StringComparison.Ordinal);
        if (!needsExceptionRuntime)
            body = body.Replace("personality i32 (...)* @__CxxFrameHandler3", "").Replace("personality i32 (...)* @__gxx_personality_v0", "");
        return "target datalayout = \"" + TargetInfo.Default.DataLayout + "\"\n" +
            "target triple = \"" + TargetInfo.Default.Triple + "\"\n" + "declare void @llvm.trap()\n" +
            (needsExceptionRuntime ? NativeDeclarations : "") + body;
    }

    private enum EmitMode { RValue, LValue }

    private void EmitAddress(AstNode node)
    {
        if (TryEmitStaticAddress(node)) return;
        if (_typeChecker.GetPropertyRead(node) is CallExpression getter) { if (_typeChecker.IsReferenceRead(node)) getter.Accept(this); else EmitAddress(getter); return; }
        if (node is IdentifierExpression ident)
        {
            if (_locals.TryGetValue(ident.Name, out string? ptr))
            {
                Push(ptr);
                return;
            }

            if (_typeChecker.TryGetConstValue(ident, out ConstValue? constVal) && constVal != null)
            {
                if (constVal is ConstValue.Array array)
                {
                    string global = NewGlobal();
                    EmitGlobal($"{global} = private constant {EmitConstInitializer(array, _typeChecker.GetType(node))}");
                    Push(global);
                    return;
                }
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
        else if (node is NewExpression or CallExpression or ArrayLiteralExpression or MoveExpression or TypeofExpression)
        {
            node.Accept(this);
            string value = Pop();
            TypeExpression type = _typeChecker.GetType(node);
            string llvmType = EmitType(type);
            if (_typeChecker.UsesCollector && HasManagedReferences(type) && !_typeChecker.HasDestructor(type))
            {
                string managedAddress = CreateManagedTemporary(type);
                EmitManagedStore(type, value, managedAddress);
                Push(managedAddress);
                return;
            }
            string address = NewTemp();
            bool needsCleanup = _typeChecker.HasDestructor(type);
            string? liveSlot = null;
            if (needsCleanup && _temporaryPrologue != null)
            {
                liveSlot = NewTemp();
                _temporaryPrologue.AppendLine($"    {address} = alloca {llvmType}");
                _temporaryPrologue.AppendLine($"    {liveSlot} = alloca i1");
                _temporaryPrologue.AppendLine($"    store i1 false, i1* {liveSlot}");
            }
            else EmitAlloca($"{llvmType}", address);
            EmitManagedStore(type, value, address, initialize: true);
            if (liveSlot != null)
            {
                Emit($"    store i1 true, i1* {liveSlot}");
                _deferScopes[^1].Add(new ConditionalTemporaryCleanup(type, address, liveSlot));
            }
            else if (needsCleanup) _deferScopes[^1].Add(new TemporaryCleanup(type, address));
            Push(address);
        }
        else
            throw new NotImplementedException($"Cannot take address of {node.GetType().Name}");
    }

    private void EmitIndexAddress(IndexExpression node)
    {
        TypeExpression targetType = _typeChecker.ResolveAlias(_typeChecker.GetType(node.Target));
        string targetPtr;
        if (targetType is ArrayTypeExpression { Size: not null } && node.Target is (UnaryExpression { Operator: TokenKind.Star } or ArrayLiteralExpression or CallExpression))
        {
            EmitAddress(node.Target);
            string arrayAddress = Pop();
            string arrayType = EmitType(targetType);
            targetPtr = NewTemp();
            Emit($"    {targetPtr} = getelementptr {arrayType}, {arrayType}* {arrayAddress}, i32 0, i32 0");
        }
        else
        {
            node.Target.Accept(this);
            targetPtr = Pop();
        }
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
        TypeExpression objType = _typeChecker.ResolveAlias(_typeChecker.GetType(node.Object));

        if (node.Object is IdentifierExpression ident && _locals.TryGetValue(ident.Name, out string? ptr))
        {
            if (objType is PointerTypeExpression or ManagedTypeExpression)
            {
                string pointerType = EmitType(objType);
                string loadedPtr = NewTemp();
                Emit($"    {loadedPtr} = load {pointerType}, {pointerType}* {ptr}");
                objPtr = loadedPtr;
            }
            else
                objPtr = ptr;
        }
        else
        {
            if (objType is NamedTypeExpression) EmitAddress(node.Object);
            else node.Object.Accept(this);
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
        if (_typeChecker.ResolveAlias(type) is ReferenceTypeExpression reference) return EmitType(reference.Inner) + "*";
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
                "long" or "ulong" => "i64",
                "nint" or "nuint" => "i" + TargetInfo.Default.PointerBits,
                "extralong" => "i128",
                "float" => "float",
                "double" => "double",
                "bool" => "i1",
                "char" => "i8",
                "void" => "void",
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
            string ret = EmitFunctionReturnType(fnPtr.ReturnType);
            string paramTypes = string.Join(", ", fnPtr.ParameterTypes.Select(EmitParamType));
            paramTypes = WithManagedResultParameter(fnPtr.ReturnType, paramTypes);
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
                "long" or "ulong" => 64,
                "nint" or "nuint" => TargetInfo.Default.PointerBits,
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
                    int methodCount = (ifaceInfo?.Methods.Count ?? 0) + 1;
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

    private string EmitImplicitCast(string val, TypeExpression fromType, TypeExpression toType, AstNode? expression = null)
    {
        // A contextually converted literal is already a value of its target
        // width. Casting it through the token's default width first can lose
        // high bits (for example int-token 0xffffffff widened to nuint).
        if (expression != null && TryContextualIntegerLiteral(expression, toType, out string literal)) return literal;
        return EmitCast(val, fromType, toType);
    }

    public void Visit(CompilationUnit node)
    {
        _currentNamespacePath = "";
        foreach (AstNode member in node.Members)
            member.Accept(this);

        foreach (NamespaceDeclaration ns in node.Namespaces)
            ns.Accept(this);

        EmitStaticStorage();
        EmitIsInstanceHelper();
        EmitExceptionDestroyHelper();
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
            if (node.IsStatic)
            {
                // A static class is a declaration scope, not an object layout.
                foreach (AstNode member in node.Members) member.Accept(this);
                return;
            }
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

                        string retType = EmitFunctionReturnType(vm.ReturnType);

                        List<string> paramTypes = new() { $"%{declaringClass}*" };
                        foreach (Parameter p in vm.Parameters)
                        {
                            paramTypes.Add(EmitParamType(p.Type));
                        }
                        AddManagedResultParameter(vm.ReturnType, paramTypes);
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
                if (ifaceInfo == null || ifaceInfo.IsStaticContract)
                {
                    continue;
                }

                {
                    List<string> entries = new();
                    foreach (MethodDeclaration ifaceMethod in ifaceInfo.Methods)
                    {
                        (MethodDeclaration Method, string DeclaringClass) mEntry = _currentClass.Methods[ifaceMethod.Name];
                        MethodDeclaration classMethod = _typeChecker.InterfaceImplementation(mEntry.Method, ifaceMethod);
                        string declaringClass = mEntry.DeclaringClass;
                        TypeChecker.ClassInfo declaringInfo = _typeChecker.GetClass(declaringClass)!;
                        string methodNs = declaringInfo.Namespace;
                        string mangled = methodNs.Length > 0
                            ? $"gflat${methodNs}${declaringClass}${classMethod.Name}"
                            : $"gflat${declaringClass}${classMethod.Name}";

                        mangled += _typeChecker.GetOverloadSuffix(classMethod);
                        string retType = EmitFunctionReturnType(classMethod.ReturnType);

                        List<string> paramTypes = new() { $"%{declaringClass}*" };
                        foreach (Parameter p in classMethod.Parameters)
                        {
                            paramTypes.Add(EmitParamType(p.Type));
                        }
                        AddManagedResultParameter(classMethod.ReturnType, paramTypes);
                        if (_currentClass.VTableSlots.TryGetValue(classMethod.Name, out int virtualSlot))
                            mangled = EmitInterfaceVirtualThunk(_currentClass, ifaceName, classMethod, virtualSlot, retType, paramTypes);
                        string fnSig = $"{retType} ({string.Join(", ", paramTypes)})*";
                        entries.Add($"i8* bitcast ({fnSig} @{mangled} to i8*)");
                    }
                    entries.Add(EmitInterfaceDestructorEntry(node.Name));
                    EmitGlobal($"@{node.Name}${ifaceName}$vtable = internal constant [{entries.Count} x i8*] [ {string.Join(", ", entries)} ]");
                }
            }

            // 4. Emit methods, constructors, and destructors
            foreach (AstNode member in node.Members)
            {
                if (member is MethodDeclaration method)
                {
                    if (!method.IsAbstract)
                    {
                        if (method.IsStatic) Visit(method);
                        else EmitClassMethod(node.Name, method);
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
            if (_currentClass.Destructor == null)
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
        _parameterOwners.Clear();
        _ownerLiveFlags.Clear();
        _tempCounter = 0;
        _hasTerminated = false;

        string baseReturnType = EmitType(node.ReturnType);
        bool isVoid = baseReturnType == "void";
        string returnType = baseReturnType;

        _currentFunctionReturnType = returnType;
        _currentFunctionExpectedType = node.ReturnType;
        _isInsideMain = false;

        string ns = _currentNamespacePath;
        string mangledName = GetMethodMangledName(className, node, ns, isClass: true);

        List<string> paramList = new() { $"%{className}* %this" };
        foreach (Parameter p in node.Parameters)
            paramList.Add($"{EmitParamType(p.Type)} %{p.Name}");

        AddManagedResultParameter(node.ReturnType, paramList, named: true);
        string parameters = string.Join(", ", paramList);

        Emit($"define {EmitFunctionReturnType(node.ReturnType)} @{mangledName}({parameters}){InlineHint(node)} {{");
        _ehBoundary |= (AstNode)node is MethodDeclaration { IsNoThrow: true };
        Emit("entry:");

        string thisPtr = NewTemp();
        EmitAlloca($"%{className}*", thisPtr);
        Emit($"    store %{className}* %this, %{className}** {thisPtr}");
        _locals["this"] = thisPtr;

        foreach (Parameter p in node.Parameters)
        {
            if (p.Type is ReferenceTypeExpression) { _locals[p.Name] = $"%{p.Name}"; continue; }
            string type = EmitParamType(p.Type);
            string ptr = NewTemp();
            EmitAlloca($"{type}", ptr);
            Emit($"    store {type} %{p.Name}, {type}* {ptr}");
            _locals[p.Name] = ptr;
            RegisterManagedParameter(p, ptr);
            if (_typeChecker.HasDestructor(p.Type)) _parameterOwners.Add(RegisterOwner(p.Type, ptr, true));
        }

        TypeChecker.ClassInfo? prevClass = _currentClass;
        _currentClass = _typeChecker.GetClass(className);

        if (node.Body != null)
            node.Body.Accept(this);

        _currentClass = prevClass;

        if (!_hasTerminated)
        {
            if (returnType == "void")
            {
                Emit("    ret void");
            }
            else
                Emit("    unreachable");
        }

        Emit("}");
        Emit("");
    }

    private void EmitClassFieldInitializers(string className, string thisPtr)
    {
        if (_currentClass == null) return;
        foreach (FieldDeclaration field in _currentClass.FieldDeclarations.Where(f => !f.IsStatic))
        {
            if (field.Initializer == null) continue;
            int fieldIdx = _currentClass.FieldIndex(field.Name);
            string llvmFieldType = EmitType(field.Type);

            string loadedThis = NewTemp();
            Emit($"    {loadedThis} = load %{className}*, %{className}** {thisPtr}");
            string fieldPtr = NewTemp();
            Emit($"    {fieldPtr} = getelementptr %{className}, %{className}* {loadedThis}, i32 0, i32 {fieldIdx + 1}");

            EmitValueForTarget(field.Initializer, field.Type);
            string val = Pop();
            TypeExpression initType = _typeChecker.GetType(field.Initializer);
            val = EmitImplicitCast(val, initType, field.Type, field.Initializer);
            EmitManagedStore(field.Type, val, fieldPtr);
            if (_constructionFields.TryGetValue(field.Name, out string? flag)) Emit($"    store i1 true, i1* {flag}");
        }
    }

    private void EmitClassConstructor(string className, ConstructorDeclaration node)
    {
        _isInsideMain = false;
        _locals.Clear();
        _parameterOwners.Clear();
        _ownerLiveFlags.Clear();
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

        Emit($"define {_currentFunctionReturnType} @{mangledName}({parameters}){InlineHint(node)} {{");
        _ehBoundary |= node.IsNoThrow;
        Emit("entry:");
        EmitStaticLifetimePrologue();

        string thisPtr = NewTemp();
        EmitAlloca($"%{className}*", thisPtr);
        Emit($"    store %{className}* %this, %{className}** {thisPtr}");
        _locals["this"] = thisPtr;

        foreach (Parameter p in node.Parameters)
        {
            if (p.Type is ReferenceTypeExpression) { _locals[p.Name] = $"%{p.Name}"; continue; }
            string type = EmitParamType(p.Type);
            string ptr = NewTemp();
            EmitAlloca($"{type}", ptr);
            Emit($"    store {type} %{p.Name}, {type}* {ptr}");
            _locals[p.Name] = ptr;
            RegisterManagedParameter(p, ptr);
            if (_typeChecker.HasDestructor(p.Type)) _parameterOwners.Add(RegisterOwner(p.Type, ptr, true));
        }

        BeginConstruction(className, node);

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
                EmitExpressionWithCleanup(() => WithArgumentOwnership(() =>
                {
                List<string> baseArgs = new() { $"%{baseInfo.Name}* {baseThis}" };
                for (int i = 0; i < node.BaseArguments.Count; i++)
                {
                    AstNode arg = node.BaseArguments[i];
                    EmitArgument(arg, resolvedBaseCtor.Parameters[i].Type);
                    string val = Pop();
                    TypeExpression argType = _typeChecker.GetType(arg);
                    val = EmitImplicitCast(val, argType, resolvedBaseCtor.Parameters[i].Type, arg);
                    string llvmType = EmitParamType(resolvedBaseCtor.Parameters[i].Type);
                    baseArgs.Add($"{llvmType} {val}");
                }
                string baseCtorName = GetConstructorMangledName(baseInfo.Name, resolvedBaseCtor, baseInfo.Namespace);
                TransferArguments();
                EmitConstructorCall(baseCtorName, resolvedBaseCtor, string.Join(", ", baseArgs));
                }));
            }
            else
            {
                string baseCtorName = GetConstructorMangledName(baseInfo.Name, null, baseInfo.Namespace);
                EmitConstructorCall(baseCtorName, baseInfo.Constructors.FirstOrDefault(c => c.Parameters.Count == 0), $"%{baseInfo.Name}* {baseThis}");
            }
            // Register only after the base constructor succeeds.
            _deferScopes[^1].Insert(0, new ExceptionOnlyCleanup(new BaseCopyCleanup(baseInfo.Name, baseThis)));
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

        CompleteConstruction();
        if (!_hasTerminated)
            Emit("    ret void");

        Emit("}");
        Emit("");
    }

    private void EmitClassDefaultConstructor(string className)
    {
        _isInsideMain = false;
        _locals.Clear();
        _parameterOwners.Clear();
        _ownerLiveFlags.Clear();
        _tempCounter = 0;
        _hasTerminated = false;

        _currentFunctionReturnType = "void";
        _currentFunctionExpectedType = new NamedTypeExpression("void", null, 0);
        string ns = _currentNamespacePath;
        string mangledName = GetConstructorMangledName(className, null, ns);

        Emit($"define void @{mangledName}(%{className}* %this) {{");
        Emit("entry:");

        string thisPtr = NewTemp();
        EmitAlloca($"%{className}*", thisPtr);
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
            EmitImplicitBaseConstruction(className, baseInfo, baseThis);
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
        _parameterOwners.Clear();
        _ownerLiveFlags.Clear();
        _tempCounter = 0;
        _hasTerminated = false;
        _isInsideMain = false;
        string? previousExit = _destructorExitLabel;
        _destructorExitLabel = NewLabel("destructor_exit");

        _currentFunctionReturnType = "void";
        _currentFunctionExpectedType = new NamedTypeExpression("void", null, node?.Line ?? 0);
        string ns = _currentNamespacePath;
        string mangledName = ns.Length > 0 ? $"gflat${ns}${className}$dtor" : $"gflat${className}$dtor";

        Emit($"define void @{mangledName}(%{className}* %this){InlineHint(node)} {{");
        Emit("entry:");

        string thisPtr = NewTemp();
        EmitAlloca($"%{className}*", thisPtr);
        Emit($"    store %{className}* %this, %{className}** {thisPtr}");
        _locals["this"] = thisPtr;

        TypeChecker.ClassInfo? prevClass = _currentClass;
        _currentClass = _typeChecker.GetClass(className);

        if (node?.Body != null)
        {
            node.Body.Accept(this);
        }

        if (!_hasTerminated) Emit($"    br label %{_destructorExitLabel}");
        Emit($"{_destructorExitLabel}:");
        _hasTerminated = false;
        EmitFieldDestruction(className, _currentClass!.Fields.Select((f, i) => (Field: f, Index: i + 1))
            .Where(f => f.Field.DeclaringClass == className).Select(f => (f.Index, f.Field.Type)));

        if (_currentClass!.BaseClass != null)
        {
            TypeChecker.ClassInfo baseInfo = _typeChecker.GetClass(_currentClass.BaseClass)!;
            if (_typeChecker.HasAnyDestructor(baseInfo) || NeedsCleanup(new NamedTypeExpression(baseInfo.Name, null, 0)))
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
        _destructorExitLabel = previousExit;
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
            .Where(f => !f.IsStatic)
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
                if (ifaceInfo == null || ifaceInfo.IsStaticContract)
                {
                    continue;
                }

                {
                    List<string> entries = new();
                    foreach (MethodDeclaration ifaceMethod in ifaceInfo.Methods)
                    {
                        MethodDeclaration structMethod = _typeChecker.InterfaceImplementation(_currentStruct!.Methods[ifaceMethod.Name], ifaceMethod);
                        string retType = EmitFunctionReturnType(structMethod.ReturnType);
                        List<string> paramTypes = new() { $"%{node.Name}*" };
                        foreach (Parameter p in structMethod.Parameters)
                        {
                            paramTypes.Add(EmitParamType(p.Type));
                        }
                        AddManagedResultParameter(structMethod.ReturnType, paramTypes);
                        string fnSig = $"{retType} ({string.Join(", ", paramTypes)})*";
                        string methodNs = _typeChecker.GetFunctionNamespace(structMethod);
                        string mangled = methodNs.Length > 0
                            ? $"gflat${methodNs}${node.Name}${structMethod.Name}"
                            : $"gflat${node.Name}${structMethod.Name}";
                        mangled += _typeChecker.GetOverloadSuffix(structMethod);
                        entries.Add($"i8* bitcast ({fnSig} @{mangled} to i8*)");
                    }
                    entries.Add(EmitInterfaceDestructorEntry(node.Name));
                    string vtableContent = string.Join(", ", entries);
                    EmitGlobal($"@{node.Name}${ifaceName}$vtable = internal constant [{entries.Count} x i8*] [ {vtableContent} ]");
                }
            }

            foreach (AstNode member in node.Members)
            {
                if (member is MethodDeclaration method)
                {
                    if (method.IsStatic) Visit(method);
                    else EmitStructMethod(node.Name, method);
                }
                else if (member is OperatorDeclaration op)
                {
                    EmitStructOperator(node.Name, op);
                }
                else if (member is ConstructorDeclaration ctor)
                {
                    EmitStructConstructor(node.Name, ctor);
                }
                else if (member is DestructorDeclaration dtor)
                {
                    EmitStructDestructor(node.Name, dtor);
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
            if (_currentStruct.Destructor == null && NeedsCleanup(new NamedTypeExpression(node.Name, null, node.Line)))
                EmitStructDestructor(node.Name, null);
        }
        finally
        {
            _currentStruct = prevStruct;
            _currentClass = prevClass;
        }
    }

    private string EmitParamType(TypeExpression type) => EmitType(type);

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
        if (type.IsReadOnlyValue)
            return "ReadOnly$" + GetMangleTypeName(type.WithReadOnlyValue(false));
        if (type is NamedTypeExpression named)
            return named.Name;
        if (type is PointerTypeExpression ptr)
            return GetMangleTypeName(ptr.Inner) + "Ptr" + (ptr.IsNullable ? "Nullable" : "");
        if (type is ManagedTypeExpression managed)
            return GetMangleTypeName(managed.Inner) + "Managed" + (managed.IsNullable ? "Nullable" : "");
        if (type is ArrayTypeExpression arr)
            return GetMangleTypeName(arr.ElementType) + "Arr" + arr.Size;
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

    private string GetMethodMangledName(string typeName, MethodDeclaration node, string ns, bool isClass = false)
    {
        string baseName = ns.Length > 0 ? $"gflat${ns}${typeName}${node.Name}" : $"gflat${typeName}${node.Name}";
        return baseName + _typeChecker.GetOverloadSuffix(node);
    }

    private void EmitStructOperator(string structName, OperatorDeclaration node)
    {
        _locals.Clear();
        _parameterOwners.Clear();
        _ownerLiveFlags.Clear();
        _tempCounter = 0;
        _hasTerminated = false;

        string returnType = EmitType(node.ReturnType);
        _currentFunctionReturnType = returnType;
        _currentFunctionExpectedType = node.ReturnType;
        string mangledName = GetOperatorMangledName(structName, node);

        List<string> paramList = new();
        foreach (Parameter p in node.Parameters)
            paramList.Add($"{EmitParamType(p.Type)} %{p.Name}");

        AddManagedResultParameter(node.ReturnType, paramList, named: true);
        string parameters = string.Join(", ", paramList);

        Emit($"define {EmitFunctionReturnType(node.ReturnType)} @{mangledName}({parameters}){InlineHint(node)} {{");
        _ehBoundary |= (AstNode)node is MethodDeclaration { IsNoThrow: true };
        Emit("entry:");

        foreach (Parameter p in node.Parameters)
        {
            if (p.Type is ReferenceTypeExpression) { _locals[p.Name] = $"%{p.Name}"; continue; }
            string type = EmitParamType(p.Type);
            string ptr = NewTemp();
            EmitAlloca($"{type}", ptr);
            Emit($"    store {type} %{p.Name}, {type}* {ptr}");
            _locals[p.Name] = ptr;
            RegisterManagedParameter(p, ptr);
            if (_typeChecker.HasDestructor(p.Type)) _parameterOwners.Add(RegisterOwner(p.Type, ptr, true));
        }

        node.Body.Accept(this);

        // Non-void fallthrough has already been rejected by control-flow analysis.
        // Loops may still leave a structurally present, unreachable exit block.
        if (!_hasTerminated)
            Emit(returnType == "void" ? "    ret void" : "    unreachable");

        Emit("}");
        Emit("");
    }

    private void EmitStructMethod(string structName, MethodDeclaration node)
    {
        _locals.Clear();
        _parameterOwners.Clear();
        _ownerLiveFlags.Clear();
        _tempCounter = 0;
        _hasTerminated = false;

        string baseReturnType = EmitType(node.ReturnType);
        bool isVoid = baseReturnType == "void";
        string returnType = baseReturnType;

        _currentFunctionReturnType = returnType;
        _currentFunctionExpectedType = node.ReturnType;
        _isInsideMain = false;

        string ns = _currentNamespacePath;
        string mangledName = GetMethodMangledName(structName, node, ns, isClass: false);

        List<string> paramList = new() { $"%{structName}* %this" };
        foreach (Parameter p in node.Parameters)
            paramList.Add($"{EmitParamType(p.Type)} %{p.Name}");

        AddManagedResultParameter(node.ReturnType, paramList, named: true);
        string parameters = string.Join(", ", paramList);

        Emit($"define {EmitFunctionReturnType(node.ReturnType)} @{mangledName}({parameters}){InlineHint(node)} {{");
        _ehBoundary |= (AstNode)node is MethodDeclaration { IsNoThrow: true };
        Emit("entry:");

        string thisPtr = NewTemp();
        EmitAlloca($"%{structName}*", thisPtr);
        Emit($"    store %{structName}* %this, %{structName}** {thisPtr}");
        _locals["this"] = thisPtr;

        foreach (Parameter p in node.Parameters)
        {
            if (p.Type is ReferenceTypeExpression) { _locals[p.Name] = $"%{p.Name}"; continue; }
            string type = EmitParamType(p.Type);
            string ptr = NewTemp();
            EmitAlloca($"{type}", ptr);
            Emit($"    store {type} %{p.Name}, {type}* {ptr}");
            _locals[p.Name] = ptr;
            RegisterManagedParameter(p, ptr);
            if (_typeChecker.HasDestructor(p.Type)) _parameterOwners.Add(RegisterOwner(p.Type, ptr, true));
        }

        if (node.Body != null)
            node.Body.Accept(this);

        if (!_hasTerminated)
        {
            if (returnType == "void")
            {
                Emit("    ret void");
            }
            else
                Emit("    unreachable");
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
        foreach (FieldDeclaration field in _currentStruct.FieldDeclarations.Where(f => !f.IsStatic))
        {
            if (field.Initializer == null) continue;
            int fieldIdx = _currentStruct.FieldIndex(field.Name);
            string llvmFieldType = EmitType(field.Type);

            string loadedThis = NewTemp();
            Emit($"    {loadedThis} = load %{structName}*, %{structName}** {thisPtr}");
            string fieldPtr = NewTemp();
            Emit($"    {fieldPtr} = getelementptr %{structName}, %{structName}* {loadedThis}, i32 0, i32 {fieldIdx}");

            EmitValueForTarget(field.Initializer, field.Type);
            string val = Pop();
            TypeExpression initType = _typeChecker.GetType(field.Initializer);
            val = EmitImplicitCast(val, initType, field.Type, field.Initializer);
            EmitManagedStore(field.Type, val, fieldPtr);
            if (_constructionFields.TryGetValue(field.Name, out string? flag)) Emit($"    store i1 true, i1* {flag}");
        }
    }

    private void EmitStructConstructor(string structName, ConstructorDeclaration node)
    {
        _isInsideMain = false;
        _locals.Clear();
        _parameterOwners.Clear();
        _ownerLiveFlags.Clear();
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

        Emit($"define {_currentFunctionReturnType} @{mangledName}({parameters}){InlineHint(node)} {{");
        _ehBoundary |= node.IsNoThrow;
        Emit("entry:");
        EmitStaticLifetimePrologue();

        string thisPtr = NewTemp();
        EmitAlloca($"%{structName}*", thisPtr);
        Emit($"    store %{structName}* %this, %{structName}** {thisPtr}");
        _locals["this"] = thisPtr;

        foreach (Parameter p in node.Parameters)
        {
            if (p.Type is ReferenceTypeExpression) { _locals[p.Name] = $"%{p.Name}"; continue; }
            string type = EmitParamType(p.Type);
            string ptr = NewTemp();
            EmitAlloca($"{type}", ptr);
            Emit($"    store {type} %{p.Name}, {type}* {ptr}");
            _locals[p.Name] = ptr;
            RegisterManagedParameter(p, ptr);
            if (_typeChecker.HasDestructor(p.Type)) _parameterOwners.Add(RegisterOwner(p.Type, ptr, true));
        }

        BeginConstruction(structName, node);

        EmitFieldInitializers(structName, thisPtr);


        if (node.Body != null)
            node.Body.Accept(this);

        CompleteConstruction();
        if (!_hasTerminated)
            Emit("    ret void");

        Emit("}");
        Emit("");
    }

    private void EmitStructDefaultConstructor(string structName)
    {
        _isInsideMain = false;
        _locals.Clear();
        _parameterOwners.Clear();
        _ownerLiveFlags.Clear();
        _tempCounter = 0;
        _hasTerminated = false;

        _currentFunctionReturnType = "void";
        _currentFunctionExpectedType = new NamedTypeExpression("void", null, 0);
        string ns = _currentNamespacePath;
        string mangledName = GetConstructorMangledName(structName, null, ns);

        Emit($"define void @{mangledName}(%{structName}* %this) {{");
        Emit("entry:");

        string thisPtr = NewTemp();
        EmitAlloca($"%{structName}*", thisPtr);
        Emit($"    store %{structName}* %this, %{structName}** {thisPtr}");
        _locals["this"] = thisPtr;

        EmitFieldInitializers(structName, thisPtr);

        Emit("    ret void");
        Emit("}");
        Emit("");
    }

    private void EmitStructDestructor(string structName, DestructorDeclaration? node)
    {
        _locals.Clear();
        _parameterOwners.Clear();
        _ownerLiveFlags.Clear();
        _tempCounter = 0;
        _hasTerminated = false;
        _isInsideMain = false;
        string? previousExit = _destructorExitLabel;
        _destructorExitLabel = NewLabel("destructor_exit");

        _currentFunctionReturnType = "void";
        _currentFunctionExpectedType = new NamedTypeExpression("void", null, node?.Line ?? 0);
        string ns = _currentNamespacePath;
        string mangledName = ns.Length > 0 ? $"gflat${ns}${structName}$dtor" : $"gflat${structName}$dtor";

        Emit($"define void @{mangledName}(%{structName}* %this){InlineHint(node)} {{");
        Emit("entry:");

        string thisPtr = NewTemp();
        EmitAlloca($"%{structName}*", thisPtr);
        Emit($"    store %{structName}* %this, %{structName}** {thisPtr}");
        _locals["this"] = thisPtr;

        TypeChecker.StructInfo? prevStruct = _currentStruct;
        _currentStruct = _typeChecker.GetStruct(structName);

        if (node?.Body != null)
        {
            node.Body.Accept(this);
        }

        if (!_hasTerminated) Emit($"    br label %{_destructorExitLabel}");
        Emit($"{_destructorExitLabel}:");
        _hasTerminated = false;
        EmitFieldDestruction(structName, _currentStruct!.Fields.Select((f, i) => (i, f.Type)));
        _destructorExitLabel = previousExit;

        _currentStruct = prevStruct;
        if (!_hasTerminated)
        {
            Emit("    ret void");
        }

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
        if (_staticStoragePhase != 0) EmitStaticFieldLifetime(node);
    }

    public void Visit(OperatorDeclaration node) => throw new NotImplementedException();

    private readonly Dictionary<string, string> _nativeExternDeclarations = new();

    public void Visit(ExternDeclaration node)
    {
        _externNames.Add(node.Name);
        string returnType = EmitType(node.ReturnType);
        string parameters = string.Join(", ", node.Parameters.Select(p => EmitParamType(p.Type)));
        if (node.IsVariadic)
            parameters = parameters.Length > 0 ? parameters + ", ..." : "...";
        string declaration = $"declare {returnType} @{node.Name}({parameters})";
        if (_nativeExternDeclarations.TryGetValue(node.Name, out string? existing))
        {
            if (existing != declaration)
                throw new gflat.CompileExceptions.TypeCheckException($"External symbol '{node.Name}' has incompatible native signatures", node.Span);
            return;
        }
        _nativeExternDeclarations[node.Name] = declaration;
        EmitGlobal(declaration);
    }

    public void Visit(MethodDeclaration node)
    {
        if (node.IsGeneric) return;

        _locals.Clear();
        _parameterOwners.Clear();
        _ownerLiveFlags.Clear();
        _tempCounter = 0;

        bool isMain = node.Name == "main" && !_typeChecker.TryGetMethodOwner(node, out _, out _);
        string baseReturnType = EmitType(node.ReturnType);
        bool isVoid = baseReturnType == "void";
        string returnType = baseReturnType;

        _currentFunctionReturnType = returnType;
        _currentFunctionExpectedType = node.ReturnType;
        _isInsideMain = isMain;

        string name;
        if (node.StringLiteralPrefix != null)
            name = _typeChecker.GetStringLiteralOperatorName(node);
        else if (node.IsStatic && _typeChecker.TryGetMethodOwner(node, out string owner, out bool ownerIsClass))
            name = GetMethodMangledName(owner, node, _typeChecker.GetFunctionNamespace(node), ownerIsClass);
        else if (isMain)
            name = "main";
        else if (_currentNamespacePath.Length > 0)
            name = $"gflat${_currentNamespacePath}${node.Name}" + _typeChecker.GetOverloadSuffix(node);
        else
            name = $"gflat${node.Name}" + _typeChecker.GetOverloadSuffix(node);

        string parameters = string.Join(", ", node.Parameters.Select(p =>
            $"{EmitParamType(p.Type)} %{p.Name}"));
        parameters = WithManagedResultParameter(node.ReturnType, parameters, named: true);

        Emit($"define {EmitFunctionReturnType(node.ReturnType)} @{name}({parameters}){InlineHint(node)} {{");
        _ehBoundary |= node.IsNoThrow;
        Emit("entry:");
        EmitStaticLifetimePrologue();

        foreach (Parameter p in node.Parameters)
        {
            if (p.Type is ReferenceTypeExpression) { _locals[p.Name] = $"%{p.Name}"; continue; }
            string type = EmitParamType(p.Type);
            string ptr = NewTemp();
            EmitAlloca($"{type}", ptr);
            Emit($"    store {type} %{p.Name}, {type}* {ptr}");
            _locals[p.Name] = ptr;
            RegisterManagedParameter(p, ptr);
            if (_typeChecker.HasDestructor(p.Type)) _parameterOwners.Add(RegisterOwner(p.Type, ptr, true));
        }

        _hasTerminated = false;
        if (node.Body != null)
            node.Body.Accept(this);

        if (!_hasTerminated)
            Emit(isMain ? "    ret i32 0" : isVoid ? "    ret void" : "    unreachable");

        Emit("}");
        Emit("");
    }

    public void Visit(ConstructorDeclaration node) { }
    public void Visit(DestructorDeclaration node) { }
    public void Visit(Parameter node) => throw new NotImplementedException();

    public void Visit(BlockStatement node)
    {
        var outerLocals = new Dictionary<string, string>(_locals);
        _deferScopes.Add(new List<IDeferAction>());
        _deferScopes[^1].AddRange(_parameterOwners);
        _parameterOwners.Clear();
        bool terminated = false;

        foreach (AstNode statement in node.Statements)
        {
            if (terminated)
                break;

            statement.Accept(this);

            if (_hasTerminated)
                terminated = true;
        }

        if (!terminated)
        {
            EmitDefersDownTo(_deferScopes.Count - 1);
        }
        _deferScopes.RemoveAt(_deferScopes.Count - 1);
        _locals.Clear();
        foreach (var binding in outerLocals) _locals.Add(binding.Key, binding.Value);
    }

    public void Visit(ReturnStatement node) => EmitExpressionWithCleanup(() => EmitReturn(node));

    private void EmitReturn(ReturnStatement node)
    {
        AstNode? returned = _typeChecker.GetReferenceReturn(node) ?? node.Value;
        string? val = null;
        string? llvmReturnType = null;

        if (returned != null)
        {
            if (_typeChecker.GetTransferredLocal(node) == null && _currentFunctionExpectedType != null && _typeChecker.CopiesValue(returned, _currentFunctionExpectedType))
                EmitValueForTarget(returned, _currentFunctionExpectedType);
            else if (_typeChecker.ResolveAlias(_typeChecker.GetType(returned)) is ArrayTypeExpression &&
                returned is IdentifierExpression or MemberAccessExpression or IndexExpression)
            {
                EmitAddress(returned);
                string address = Pop();
                string arrayType = EmitType(_typeChecker.GetType(returned));
                string value = NewTemp();
                Emit($"    {value} = load {arrayType}, {arrayType}* {address}");
                Push(value);
            }
            else if (_typeChecker.GetTransferredLocal(node) != null) returned.Accept(this);
            else if (_currentFunctionExpectedType != null) EmitValueForTarget(returned, _currentFunctionExpectedType);
            else returned.Accept(this);
            // Void calls (e.g. expression-body `void f() => voidCall();`) don't push a value
            TypeExpression returnType = _typeChecker.GetType(returned);
            string valueRetType = EmitType(returnType);
            bool valueIsVoid = valueRetType == "void";

            if (!valueIsVoid)
            {
                val = Pop();
                llvmReturnType = valueRetType;

                string targetRet = _currentFunctionReturnType;
                if (targetRet.Length > 0 && llvmReturnType != targetRet)
                {
                    if (_currentFunctionExpectedType != null)
                    {
                        val = EmitImplicitCast(val, returnType, _currentFunctionExpectedType, node.Value);
                        llvmReturnType = targetRet;
                    }
                }
            }
        }

        string? skipLocalPtr = null;
        if (_typeChecker.GetTransferredLocal(node) is string transferred && _locals.TryGetValue(transferred, out string? localSlot))
        {
            skipLocalPtr = localSlot;
            SetOwnerLive(localSlot, false);
        }

        bool managedResult = _currentFunctionExpectedType != null && UsesManagedResultStorage(_currentFunctionExpectedType);
        if (managedResult && val != null)
            EmitManagedStore(_currentFunctionExpectedType!, val, "%gc.result");

        EmitDefersDownTo(0, skipLocalPtr);
        if (_hasTerminated) return;

        if (_destructorExitLabel != null)
        {
            Emit($"    br label %{_destructorExitLabel}");
            _hasTerminated = true;
            return;
        }

        {
            if (val == null || managedResult)
                Emit("    ret void");
            else
            {
                Emit($"    ret {llvmReturnType} {val}");
            }
        }

        _hasTerminated = true;
    }

    public void Visit(IfStatement node)
    {
        string thenLabel = NewLabel("then");
        string elseLabel = NewLabel("else");
        string mergeLabel = NewLabel("merge");

        EmitExpressionWithCleanup(() => node.Condition.Accept(this));
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

        EmitExpressionWithCleanup(() => node.Condition.Accept(this));
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
        _deferScopes.Add(new());
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
            EmitExpressionWithCleanup(() => node.Condition.Accept(this));
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
            EmitExpressionWithCleanup(() => node.Increment.Accept(this));
        Emit($"    br label %{condLabel}");
        Emit($"{exitLabel}:");
        EmitDefersDownTo(_deferScopes.Count - 1);
        _deferScopes.RemoveAt(_deferScopes.Count - 1);
        _breakLabels.Pop();
        _continueLabels.Pop();
        _loopDeferDepths.Pop();
        _hasTerminated = false;
    }

    public void Visit(ForeachStatement node) => EmitExpressionWithCleanup(() => EmitForeach(node));

    private void EmitForeach(ForeachStatement node)
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
        EmitArgument(node.Collection);
        string basePtr = Pop();

        // Allocate index variable
        string idxPtr = NewTemp();
        EmitAlloca("i32", idxPtr);
        Emit($"    store i32 0, i32* {idxPtr}");

        // Allocate loop variable
        string llvmVarType = EmitType(node.ElementType);
        string varPtr = NewTemp();
        EmitAlloca($"{llvmVarType}", varPtr);
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
        _hasTerminated = false;
        _deferScopes.Add(new());
        InitializeManagedLocal(node.ElementType, varPtr, _deferScopes.Count - 1);
        if (_typeChecker.RequiresCopy(node.ElementType)) EmitCopyInto(node.ElementType, elemPtr, varPtr);
        else
        {
            string elemVal = NewTemp();
            Emit($"    {elemVal} = load {llvmElemType}, {llvmElemType}* {elemPtr}");
            string castedVal = EmitImplicitCast(elemVal, elemType, node.ElementType);
            EmitManagedStore(node.ElementType, castedVal, varPtr);
        }
        if (_typeChecker.HasDestructor(node.ElementType)) _deferScopes[^1].Add(RegisterOwner(node.ElementType, varPtr, true));
        node.Body.Accept(this);
        if (!_hasTerminated)
        {
            EmitDefersDownTo(_deferScopes.Count - 1);
            if (!_hasTerminated) Emit($"    br label %{advanceLabel}");
        }
        _deferScopes.RemoveAt(_deferScopes.Count - 1);

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
        int scope = _deferScopes.Count - 1;
        EmitExpressionWithCleanup(() => EmitVariable(node, scope));
    }

    private void EmitVariable(VariableDeclaration node, int scope)
    {
        if (node.IsConst)
        {
            return;
        }

        TypeExpression resolvedVarType = _typeChecker.GetType(node);
        string type = EmitType(resolvedVarType);
        string ptr = NewTemp();
        EmitAlloca($"{type}", ptr);
        _locals[node.Name] = ptr;
        InitializeManagedLocal(resolvedVarType, ptr, scope);

        if (node.Initializer != null)
        {
            EmitValueForTarget(node.Initializer, resolvedVarType);
            string val = Pop();
            TypeExpression initType = _typeChecker.GetType(node.Initializer);
            val = EmitImplicitCast(val, initType, resolvedVarType, node.Initializer);
            EmitManagedStore(resolvedVarType, val, ptr);
        }
        else
        {
            TypeExpression unwrappedInit = _typeChecker.ResolveAlias(resolvedVarType);
            if (unwrappedInit is NamedTypeExpression namedInit && _typeChecker.GetClass(namedInit.Name) != null)
            {
                Emit($"    store {type} zeroinitializer, {type}* {ptr}");
            }
        }

        if (_deferScopes.Count > 0 && _typeChecker.HasDestructor(resolvedVarType))
            _deferScopes[scope].Add(RegisterOwner(resolvedVarType, ptr, node.Initializer != null));
    }

    public void Visit(ExpressionStatement node) => EmitExpressionWithCleanup(() => EmitStatementExpression(node));

    private void EmitStatementExpression(ExpressionStatement node)
    {
        node.Expression.Accept(this);
        if (_valueStack.Count > 0)
        {
            string result = Pop();
            TypeExpression type = _typeChecker.GetType(node.Expression);
            if (_typeChecker.IsFreshValue(node.Expression) && _typeChecker.HasDestructor(type))
            {
                string slot = NewTemp();
                string llvmType = EmitType(type);
                EmitAlloca($"{llvmType}", slot);
                EmitManagedStore(type, result, slot, initialize: true);
                EmitDestroy(type, slot);
            }
        }
    }
    public void Visit(BinaryExpression node)
    {
        if (node.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
        {
            node.Left.Accept(this);
            string condition = Pop();
            string slot = NewTemp();
            string rhs = NewLabel("logical_rhs");
            string done = NewLabel("logical_done");
            EmitAlloca("i1", slot);
            Emit($"    store i1 {condition}, i1* {slot}");
            bool and = node.Operator == TokenKind.AmpersandAmpersand;
            Emit($"    br i1 {condition}, label %{(and ? rhs : done)}, label %{(and ? done : rhs)}");
            Emit($"{rhs}:");
            node.Right.Accept(this);
            string rightValue = Pop();
            Emit($"    store i1 {rightValue}, i1* {slot}");
            Emit($"    br label %{done}");
            Emit($"{done}:");
            string result = NewTemp();
            Emit($"    {result} = load i1, i1* {slot}");
            Push(result);
            return;
        }
        TypeExpression? nativeOperandType = _typeChecker.TryGetNativeIntegerOperands(node, out var nativeType) ? nativeType : null;
        TypeExpression? leftContext = nativeOperandType, rightContext = nativeOperandType;
        if (_typeChecker.TryGetOperatorTarget(node, out var selectedOperator))
        {
            leftContext = selectedOperator.Operator.Parameters[0].Type;
            rightContext = selectedOperator.Operator.Parameters[1].Type;
        }
        if (leftContext != null && TryContextualIntegerLiteral(node.Left, leftContext, out string leftLiteral)) Push(leftLiteral);
        else node.Left.Accept(this);
        string left = Pop();
        if (rightContext != null && TryContextualIntegerLiteral(node.Right, rightContext, out string rightLiteral)) Push(rightLiteral);
        else node.Right.Accept(this);
        string right = Pop();

        if (_typeChecker.TryGetOperatorTarget(node, out (string StructName, OperatorDeclaration Operator) opTarget))
        {
            TypeExpression leftType1 = _typeChecker.GetType(node.Left);
            TypeExpression rightType1 = _typeChecker.GetType(node.Right);
            left = EmitImplicitCast(left, leftType1, opTarget.Operator.Parameters[0].Type, node.Left);
            right = EmitImplicitCast(right, rightType1, opTarget.Operator.Parameters[1].Type, node.Right);

            string mangledName = GetOperatorMangledName(opTarget.StructName, opTarget.Operator);
            string param0Type = EmitParamType(opTarget.Operator.Parameters[0].Type);
            string param1Type = EmitParamType(opTarget.Operator.Parameters[1].Type);

            string temp1 = EmitValueCall(opTarget.Operator.ReturnType, "@" + mangledName,
                $"{param0Type} {left}, {param1Type} {right}", transferArguments: false)!;
            Push(temp1);
            return;
        }

        string temp = NewTemp();

        TypeExpression leftType = _typeChecker.GetType(node.Left);
        TypeExpression rightType = _typeChecker.GetType(node.Right);
        TypeExpression resultType = _typeChecker.GetType(node);
        if (_typeChecker.TryGetNativeIntegerOperands(node, out TypeExpression nativeOperands))
        {
            left = EmitImplicitCast(left, leftType, nativeOperands, node.Left);
            right = EmitImplicitCast(right, rightType, nativeOperands, node.Right);
            leftType = rightType = nativeOperands;
        }
        string llvmType = EmitType(resultType);
        bool isFloat = leftType is NamedTypeExpression { Name: "float" };

        bool isUnsigned = _typeChecker.IsUnsignedInteger(leftType) || _typeChecker.IsUnsignedInteger(rightType);

        bool isComparison = node.Operator is TokenKind.EqualsEquals or TokenKind.NotEquals or
            TokenKind.Less or TokenKind.Greater or TokenKind.LessEquals or TokenKind.GreaterEquals;

        if (isComparison)
        {
            if (_typeChecker.TryGetIntegerComparisonOperands(node, out TypeExpression commonInteger))
            {
                left = EmitImplicitCast(left, leftType, commonInteger, node.Left);
                right = EmitImplicitCast(right, rightType, commonInteger, node.Right);
                leftType = rightType = commonInteger;
                isUnsigned = _typeChecker.IsUnsignedInteger(commonInteger);
            }
            string cmpLlvm = EmitType(leftType);
            string rightLlvm = EmitType(rightType);
            if (_typeChecker.ResolveAlias(leftType) is NamedTypeExpression { Name: "null" })
                cmpLlvm = _typeChecker.ResolveAlias(rightType) is NamedTypeExpression { Name: "null" } ? "i8*" : rightLlvm;
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

    private bool EmitFunctionValue(AstNode node)
    {
        AstNode? fnTarget = _typeChecker.GetFunctionAddressTarget(node);
        if (fnTarget is ExternDeclaration ext)
        {
            Push($"@{ext.Name}");
            return true;
        }
        if (fnTarget is MethodDeclaration method)
        {
            if (method.IsStatic && _typeChecker.TryGetMethodOwner(method, out string owner, out bool isClass))
            {
                Push("@" + GetMethodMangledName(owner, method, _typeChecker.GetFunctionNamespace(method), isClass));
                return true;
            }
            if (method.Name == "main")
            {
                Push("@main");
                return true;
            }
            string ns = _typeChecker.GetFunctionNamespace(method);
            string mangled = (ns.Length > 0 ? $"gflat${ns}${method.Name}" : $"gflat${method.Name}") + _typeChecker.GetOverloadSuffix(method);
            Push($"@{mangled}");
            return true;
        }

        return false;
    }

    public void Visit(UnaryExpression node)
    {
        if (node.Operator == TokenKind.Ref) { EmitAddress(node.Operand); return; }
        if (node.Operator == TokenKind.Bang && !node.IsPrefix)
        {
            // Static nullability suppression only: no guard, assume, or nonnull metadata.
            node.Operand.Accept(this);
            return;
        }
        if (EmitPropertyWrite(node)) return;
        if (node.Operator == TokenKind.Ampersand)
        {
            if (EmitFunctionValue(node)) return;

            EmitAddress(node.Operand);
            return;
        }

        if (node.Operator == TokenKind.Star)
        {
            node.Operand.Accept(this);
            string ptr = Pop();
            TypeExpression ptrType = _typeChecker.ResolveAlias(_typeChecker.GetType(node.Operand));
            TypeExpression pointee = ptrType switch
            {
                PointerTypeExpression raw => raw.Inner,
                ManagedTypeExpression managed => managed.Inner,
                _ => throw new Exception("Cannot dereference non-pointer")
            };
            string innerType = EmitType(pointee);
            string temp = NewTemp();
            Emit($"    {temp} = load {innerType}, {innerType}* {ptr}");
            PushManaged(node, temp);
            return;
        }

        node.Operand.Accept(this);
        string operand = Pop();
        TypeExpression type = _typeChecker.GetType(node.Operand);

        if (_typeChecker.TryGetUnaryOperatorTarget(node, out (string StructName, OperatorDeclaration Operator) opTarget))
        {
            operand = EmitImplicitCast(operand, type, opTarget.Operator.Parameters[0].Type, node.Operand);
            string mangledName = GetOperatorMangledName(opTarget.StructName, opTarget.Operator);
            string param0Type = EmitParamType(opTarget.Operator.Parameters[0].Type);

            string temp1 = EmitValueCall(opTarget.Operator.ReturnType, "@" + mangledName,
                $"{param0Type} {operand}", transferArguments: false)!;
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
                    PushManaged(node, temp);
                    break;
                }
            case TokenKind.Bang:
                {
                    string temp = NewTemp();
                    Emit($"    {temp} = xor i1 {operand}, 1");
                    PushManaged(node, temp);
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
                    byte[] escaped = StringLiteralEncoding.Bytes(raw);
                    string globalName = NewGlobal();
                    int len = escaped.Length + 1; // +1 for null terminator
                    string llvmStr = StringLiteralEncoding.LlvmBytes(escaped);
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
        if (EmitFunctionValue(node)) return;
        if (EmitPropertyRead(node)) return;
        if (TryEmitStaticRead(node)) return;
        if (_typeChecker.TryGetConstValue(node, out ConstValue? constVal) && constVal != null)
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
            PushManaged(node, temp);
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
                PushManaged(node, temp);
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
                PushManaged(node, temp);
                return;
            }
        }

        throw new Exception($"Unknown identifier '{node.Name}'");
    }

    public void Visit(DestructorCallExpression node)
        => EmitExpressionWithCleanup(() =>
        {
            node.Target.Accept(this);
            string address = Pop();
            var pointer = (PointerTypeExpression)_typeChecker.ResolveAlias(_typeChecker.GetType(node.Target));
            GuardNonNull(address, EmitType(pointer));
            if (pointer.Inner is NamedTypeExpression n && _typeChecker.IsInterface(n))
                EmitInterfaceDestruction(address, n.Name);
            else EmitDestroy(pointer.Inner, address);
        });

    public void Visit(CallExpression node)
        => EmitExpressionWithCleanup(() => EmitCall(node));

    private void EmitCall(CallExpression node) => WithArgumentOwnership(() => EmitCallCore(node));

    private void EmitCallCore(CallExpression node)
    {
        if (TryEmitLlvmCall(node)) return;
        if (_typeChecker.GetExtensionCall(node) is { } extensionCall)
        {
            EmitCallCore(extensionCall);
            return;
        }
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
                    TransferArguments();
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
                    TransferArguments();
                    Emit($"    call void @{dtorMangled}(%{dtorCall.Class.Name}* {classPtr})");
                }
            }
            else if (_typeChecker.TryGetStructDestructorCall(node, out TypeChecker.StructInfo? structInfo) && structInfo != null)
            {
                string structPtr = ptrVal;
                if (llvmPtrType != $"%{structInfo.Name}*")
                {
                    structPtr = NewTemp();
                    Emit($"    {structPtr} = bitcast {llvmPtrType} {ptrVal} to %{structInfo.Name}*");
                }
                string dtorNs = structInfo.Namespace;
                string dtorMangled = dtorNs.Length > 0
                    ? $"gflat${dtorNs}${structInfo.Name}$dtor"
                    : $"gflat${structInfo.Name}$dtor";
                TransferArguments();
                Emit($"    call void @{dtorMangled}(%{structInfo.Name}* {structPtr})");
            }

            // gflat$Memory$Free is defined in user code (or std/libc/Memory/Memory.gf), no declare needed
            _externNames.Add(RuntimeMemorySymbol("Free"));

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

            TransferArguments();

            Emit($"    call void @{RuntimeMemorySymbol("Free")}(i8* {castPtr})");
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

            string returnType = EmitFunctionReturnType(ifaceCall.Method.ReturnType);
            List<string> fnParamTypes = new() { "i8*" };
            foreach (Parameter p in ifaceCall.Method.Parameters)
            {
                fnParamTypes.Add(EmitParamType(p.Type));
            }
            AddManagedResultParameter(ifaceCall.Method.ReturnType, fnParamTypes);
            string fnSig = $"{returnType} ({string.Join(", ", fnParamTypes)})*";

            string typedFn = NewTemp();
            Emit($"    {typedFn} = bitcast i8* {rawFnPtr} to {fnSig}");

            List<string> callArgs = new() { $"i8* {dataPtr}" };
            for (int i = 0; i < node.Arguments.Count; i++)
            {
                AstNode arg = node.Arguments[i];
                EmitArgument(arg, ifaceCall.Method.Parameters[i].Type);
                string val = Pop();
                TypeExpression argType = _typeChecker.GetType(arg);
                string llvmArgType = EmitParamType(ifaceCall.Method.Parameters[i].Type);
                val = EmitImplicitCast(val, argType, ifaceCall.Method.Parameters[i].Type, arg);
                callArgs.Add($"{llvmArgType} {val}");
            }

            string argsStr = string.Join(", ", callArgs);
            EmitCallAndPush(ifaceCall.Method.ReturnType, typedFn, argsStr);
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
            string returnType = EmitFunctionReturnType(vcall.Method.ReturnType);
            List<string> fnParamTypes = new() { $"%{declaringClass}*" };
            foreach (Parameter p in vcall.Method.Parameters)
            {
                fnParamTypes.Add(EmitParamType(p.Type));
            }
            AddManagedResultParameter(vcall.Method.ReturnType, fnParamTypes);
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
                EmitArgument(arg, vcall.Method.Parameters[i].Type);
                string val = Pop();
                TypeExpression argType = _typeChecker.GetType(arg);
                string llvmArgType = EmitParamType(vcall.Method.Parameters[i].Type);
                val = EmitImplicitCast(val, argType, vcall.Method.Parameters[i].Type, arg);
                callArgs.Add($"{llvmArgType} {val}");
            }

            string argsStr = string.Join(", ", callArgs);
            EmitCallAndPush(vcall.Method.ReturnType, typedFn, argsStr);
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
                EmitArgument(arg, fnPtr.ParameterTypes[i]);
                string val = Pop();
                TypeExpression argType = _typeChecker.GetType(arg);
                string llvmArgType = argType is ArrayTypeExpression a ? EmitType(a.ElementType) + "*" : EmitType(argType);

                if (i < fnPtr.ParameterTypes.Count)
                {
                    val = EmitImplicitCast(val, argType, fnPtr.ParameterTypes[i], arg);
                    llvmArgType = EmitParamType(fnPtr.ParameterTypes[i]);
                }

                argVals.Add(val);
                argTypesList.Add(llvmArgType);
            }

            TypeExpression indirectCallType = _typeChecker.GetType(node);
            string indirectArgs = string.Join(", ", argVals.Zip(argTypesList, (v, t) => $"{t} {v}"));

            EmitCallAndPush(indirectCallType, fnVal, indirectArgs);
            return;
        }

        List<string> argValues = new();
        List<string> argTypes = new();

        string funcName;
        AstNode? target = _typeChecker.GetResolvedCall(node);

        if (node.Callee is MemberAccessExpression memberAccess && target is MethodDeclaration structMethod && !structMethod.IsStatic)
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
                EmitArgument(arg, structMethod.Parameters[i].Type);
                string val = Pop();
                TypeExpression argType = _typeChecker.GetType(arg);
                string llvmArgType = argType is ArrayTypeExpression a ? EmitType(a.ElementType) + "*" : EmitType(argType);
                if (i < structMethod.Parameters.Count)
                {
                    val = EmitImplicitCast(val, argType, structMethod.Parameters[i].Type, arg);
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
            funcName = GetMethodMangledName(typeName, structMethod, ns, isClass: _typeChecker.IsClass(rawName));
        }
        else if (node.Callee is IdentifierExpression idMethod && target is MethodDeclaration methodMember && _currentClass != null && !methodMember.IsStatic && _currentClass.Methods.ContainsKey(idMethod.Name))
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
                EmitArgument(arg, methodMember.Parameters[i].Type);
                string val = Pop();
                TypeExpression argType = _typeChecker.GetType(arg);
                string llvmArgType = EmitParamType(methodMember.Parameters[i].Type);
                val = EmitImplicitCast(val, argType, methodMember.Parameters[i].Type, arg);
                argValues.Add(val);
                argTypes.Add(llvmArgType);
            }

            string ns = _typeChecker.GetFunctionNamespace(methodMember);
            funcName = GetMethodMangledName(declaringClass, methodMember, ns, isClass: true);
        }
        else
        {
            for (int i = 0; i < node.Arguments.Count; i++)
            {
                AstNode arg = node.Arguments[i];
                EmitArgument(arg, target is MethodDeclaration mt ? mt.Parameters[i].Type : target is ExternDeclaration et && i < et.Parameters.Count ? et.Parameters[i].Type : null);
                string val = Pop();
                TypeExpression argType = _typeChecker.GetType(arg);
                string llvmArgType = argType is ArrayTypeExpression a ? EmitType(a.ElementType) + "*" : EmitType(argType);

                if (target is MethodDeclaration methodTarget && i < methodTarget.Parameters.Count)
                {
                    val = EmitImplicitCast(val, argType, methodTarget.Parameters[i].Type, arg);
                    llvmArgType = EmitParamType(methodTarget.Parameters[i].Type);
                }
                else if (target is ExternDeclaration extDecl)
                {
                    if (i < extDecl.Parameters.Count)
                    {
                        val = EmitImplicitCast(val, argType, extDecl.Parameters[i].Type, arg);
                        llvmArgType = EmitParamType(extDecl.Parameters[i].Type);
                    }
                    else if (extDecl.IsVariadic)
                    {
                        if (llvmArgType == "float")
                        {
                            string promoted = NewTemp();
                            Emit($"    {promoted} = fpext float {val} to double");
                            val = promoted;
                            llvmArgType = "double";
                        }
                        else if (llvmArgType == "i1")
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
                if (method.StringLiteralPrefix != null)
                {
                    funcName = _typeChecker.GetStringLiteralOperatorName(method);
                }
                else if (method.IsStatic && _typeChecker.TryGetMethodOwner(method, out string owner, out bool ownerIsClass))
                {
                    funcName = GetMethodMangledName(owner, method, _typeChecker.GetFunctionNamespace(method), ownerIsClass);
                }
                else if (method.Name == "main")
                {
                    funcName = "main";
                }
                else
                {
                    string ns = _typeChecker.GetFunctionNamespace(method);
                    funcName = (ns.Length > 0 ? $"gflat${ns}${method.Name}" : $"gflat${method.Name}") + _typeChecker.GetOverloadSuffix(method);
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

        if (target is not ExternDeclaration && UsesManagedResultStorage(callType))
        {
            EmitCallAndPush(callType, "@" + funcName, args);
            return;
        }

        string callSignature = retType;
        if (target is ExternDeclaration { IsVariadic: true } variadic)
            callSignature += " (" + string.Join(", ", variadic.Parameters.Select(p => EmitParamType(p.Type)).Append("...")) + ")";
        {
            if (retType == "void")
            {
                TransferArguments();
                Emit($"    call {callSignature} @{funcName}({args})");
            }
            else
            {
                string temp = NewTemp();
                TransferArguments();
                Emit($"    {temp} = call {callSignature} @{funcName}({args})");
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
        => EmitExpressionWithCleanup(() => EmitMember(node));

    private void EmitMember(MemberAccessExpression node)
    {
        if (EmitPropertyRead(node)) return;
        if (_typeChecker.TryGetEnumMember(node, out long enumVal, out _))
        {
            Push(enumVal.ToString());
            return;
        }

        if (node.IsArrow)
        {
            throw new Exception($"The '->' operator is no longer supported on line {node.Line}");
        }

        if (node.Object is IdentifierExpression objIdent &&
            _typeChecker.TryGetConstValue(objIdent, out ConstValue? constObj) && constObj != null)
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
        PushManaged(node, val);
    }

    public void Visit(AssignmentExpression node) => EmitExpressionWithCleanup(() => EmitAssignment(node));

    private void EmitAssignment(AssignmentExpression node)
    {
        if (EmitPropertyWrite(node)) return;
        EmitValueForTarget(node.Value, _typeChecker.GetType(node.Target));
        string val = Pop();

        IDeferAction? pending = null;
        TypeExpression assignedType = _typeChecker.GetType(node.Target);
        if (_typeChecker.HasDestructor(assignedType))
        {
            string temporary = NewTemp(), llvm = EmitType(assignedType);
            EmitAlloca($"{llvm}", temporary);
            Emit($"    store {llvm} {val}, {llvm}* {temporary}");
            pending = new TemporaryCleanup(assignedType, temporary);
            _deferScopes[^1].Add(pending);
        }
        EmitAddress(node.Target);
        string ptr = Pop();
        if (pending != null) _deferScopes[^1].Remove(pending);

        TypeExpression targetType = _typeChecker.GetType(node.Target);
        string llvmType = EmitType(targetType);
        bool isFloat = targetType is NamedTypeExpression { Name: "float" };

        TypeExpression valueType = _typeChecker.GetType(node.Value);
        string finalVal = EmitImplicitCast(val, valueType, targetType, node.Value);

        if (node.Operator != TokenKind.Equals)
        {
            string currentVal = NewTemp();
            Emit($"    {currentVal} = load {llvmType}, {llvmType}* {ptr}");
            string temp = NewTemp();

            if (_typeChecker.TryGetCompoundOperatorTarget(node, out (string StructName, OperatorDeclaration Operator) opTarget))
            {
                currentVal = EmitImplicitCast(currentVal, targetType, opTarget.Operator.Parameters[0].Type);
                val = EmitImplicitCast(val, valueType, opTarget.Operator.Parameters[1].Type, node.Value);

                string mangledName = GetOperatorMangledName(opTarget.StructName, opTarget.Operator);
                string param0Type = EmitParamType(opTarget.Operator.Parameters[0].Type);
                string param1Type = EmitParamType(opTarget.Operator.Parameters[1].Type);

                temp = EmitValueCall(opTarget.Operator.ReturnType, "@" + mangledName,
                    $"{param0Type} {currentVal}, {param1Type} {val}", transferArguments: false)!;
                temp = EmitImplicitCast(temp, opTarget.Operator.ReturnType, targetType);
                EmitManagedStore(targetType, temp, ptr);
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
                Emit($"    {temp} = {op} float {currentVal}, {finalVal}");
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
                Emit($"    {temp} = {op} {llvmType} {currentVal}, {finalVal}");
            }
            finalVal = temp;
        }

        if (_typeChecker.HasDestructor(targetType)) DestroyAssignmentTarget(node, targetType, ptr);
        EmitManagedStore(targetType, finalVal, ptr);
        Push(finalVal);
    }

    public void Visit(InterpolatedStringExpression node) => EmitExpressionWithCleanup(() => EmitInterpolation(node));
    public void Visit(NewExpression node) => EmitExpressionWithCleanup(() => WithArgumentOwnership(() => EmitNew(node)));

    private void EmitNew(NewExpression node)
    {
        if (TypeChecker.IsError(_typeChecker.GetType(node)))
        {
            Push("null");
            return;
        }

        TypeExpression resolvedType = _typeChecker.ResolveAlias(node.Type);
        if (resolvedType is not NamedTypeExpression namedType)
        {
            throw new Exception($"Cannot emit 'new' for non-named type on line {node.Line}");
        }
        string typeName = namedType.Name;
        if (node.Kind == AllocationKind.Managed && TypeChecker.IsPrimitive(typeName))
        {
            string scalarType = EmitType(resolvedType);
            string value = GetDefaultValue(resolvedType);
            if (node.Arguments.Count == 1)
            {
                EmitValueForTarget(node.Arguments[0], resolvedType);
                value = EmitImplicitCast(Pop(), _typeChecker.GetType(node.Arguments[0]), resolvedType, node.Arguments[0]);
            }
            string memory = NewTemp();
            memory = EmitManagedAllocation(node, Math.Max(1, _typeChecker.GetTypeSize(resolvedType)).ToString());
            GuardAllocation(node, memory);
            string address = NewTemp();
            Emit($"    {address} = bitcast i8* {memory} to {scalarType}*");
            Emit($"    store {scalarType} {value}, {scalarType}* {address}");
            PushManaged(node, address);
            return;
        }
        bool isClass = _typeChecker.IsClass(typeName);
        TypeChecker.StructInfo? sInfo = isClass ? null : _typeChecker.GetStruct(typeName);
        TypeChecker.ClassInfo? cInfo = isClass ? _typeChecker.GetClass(typeName) : null;
        if (isClass && cInfo == null)
        {
            throw new Exception($"Cannot emit 'new' for unknown class '{typeName}' on line {node.Line}");
        }
        if (!isClass && sInfo == null)
        {
            throw new Exception($"Cannot emit 'new' for unknown struct '{typeName}' on line {node.Line}");
        }
        string typeNs = isClass ? cInfo!.Namespace : sInfo!.Namespace;
        ConstructorDeclaration? ctor = _typeChecker.GetResolvedConstructor(node);

        List<string> argVals = new();
        foreach (AstNode arg in node.Arguments)
        {
            EmitArgument(arg, ctor?.Parameters[argVals.Count].Type);
            argVals.Add(Pop());
        }

        bool hasCtor = isClass || ctor != null || sInfo!.Constructors.Any(c => c.Parameters.Count == 0) ||
                       (sInfo.Constructors.Count == 0 && sInfo.FieldDeclarations.Any(f => f.Initializer != null));

        if (node.Kind == AllocationKind.Value)
        {
            string temp = NewTemp();
            EmitAlloca($"%{typeName}", temp);
            EmitManagedStore(node.Type, GetDefaultValue(node.Type), temp, initialize: true);
            CleanupManagedStorage(node.Type, temp);

            if (hasCtor)
            {
                string mangledName = GetConstructorMangledName(typeName, ctor, typeNs);
                List<string> callArgs = new() { $"%{typeName}* {temp}" };
                if (ctor != null)
                {
                    for (int i = 0; i < ctor.Parameters.Count; i++)
                    {
                        TypeExpression argType = _typeChecker.GetType(node.Arguments[i]);
                        string castVal = EmitImplicitCast(argVals[i], argType, ctor.Parameters[i].Type, node.Arguments[i]);
                        callArgs.Add($"{EmitParamType(ctor.Parameters[i].Type)} {castVal}");
                    }
                }
                TransferArguments();
                EmitConstructorCall(mangledName, ctor, string.Join(", ", callArgs));
            }

            string val = NewTemp();
            Emit($"    {val} = load %{typeName}, %{typeName}* {temp}");
            PushManaged(node, val);
        }
        else if (node.Kind is AllocationKind.Pointer or AllocationKind.Managed)
        {
            bool adopting = _typeChecker.GetManagedAllocation(node) != null;
            string allocator = RuntimeMemorySymbol("Allocate");
            // gflat$Memory$Allocate is defined in user code (or std/libc/Memory/Memory.gf), no declare needed
            _ = _externNames.Add(allocator);

            string sizePtr = NewTemp();
            Emit($"    {sizePtr} = getelementptr %{typeName}, %{typeName}* null, i32 1");
            string sizeInt = NewTemp();
            Emit($"    {sizeInt} = ptrtoint %{typeName}* {sizePtr} to i{TargetInfo.Default.PointerBits}");
            if (adopting && _typeChecker.GetTypeSize(node.Type) == 0) sizeInt = "1";

            string rawMem = NewTemp();
            if (node.Destination == null && adopting)
                rawMem = EmitManagedAllocation(node, sizeInt);
            else if (node.Destination == null)
                Emit($"    {rawMem} = call i8* @{allocator}(i{TargetInfo.Default.PointerBits} {sizeInt})");
            else
            {
                node.Destination.Accept(this);
                string destination = Pop();
                Emit($"    {rawMem} = bitcast {EmitType(_typeChecker.GetType(node.Destination))} {destination} to i8*");
            }
            GuardAllocation(node, rawMem);
            if (node.Destination != null)
            {
                GuardPlacementAlignment(rawMem, "i8*", node.Type);
            }
            string typedPtr = NewTemp();
            Emit($"    {typedPtr} = bitcast i8* {rawMem} to %{typeName}*");
            EmitManagedStore(node.Type, GetDefaultValue(node.Type), typedPtr, initialize: true);
            if (adopting) ProtectManagedValue(_typeChecker.GetType(node), typedPtr);

            if (hasCtor)
            {
                string mangledName = GetConstructorMangledName(typeName, ctor, typeNs);
                List<string> callArgs = new() { $"%{typeName}* {typedPtr}" };
                if (ctor != null)
                {
                    for (int i = 0; i < ctor.Parameters.Count; i++)
                    {
                        TypeExpression argType = _typeChecker.GetType(node.Arguments[i]);
                        string castVal = EmitImplicitCast(argVals[i], argType, ctor.Parameters[i].Type, node.Arguments[i]);
                        callArgs.Add($"{EmitParamType(ctor.Parameters[i].Type)} {castVal}");
                    }
                }
                IDeferAction? allocationCleanup = null;
                if (node.Kind == AllocationKind.Pointer && node.Destination == null)
                {
                    allocationCleanup = new AllocationCleanup(rawMem);
                    _deferScopes[^1].Add(allocationCleanup);
                }
                TransferArguments();
                EmitConstructorCall(mangledName, ctor, string.Join(", ", callArgs));
                if (allocationCleanup != null) _deferScopes[^1].Remove(allocationCleanup);
            }

            PushManaged(node, typedPtr);
        }
        else
        {
            throw new NotImplementedException($"Allocation kind {node.Kind} not supported");
        }
    }

    public void Visit(DefaultExpression node)
    {
        TypeExpression type = _typeChecker.GetType(node);
        if (_typeChecker.TryGetConstValue(node, out var aggregate) && aggregate is ConstValue.Array or ConstValue.Struct or ConstValue.ClassInstance)
        {
            string initializer = EmitConstInitializer(aggregate, type);
            Push(initializer[(EmitType(type).Length + 1)..]);
            return;
        }
        if (_typeChecker.TryGetConstValue(node, out var value) && value != null && TryEmitConstValue(value, type)) return;
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

    public void Visit(AlignofExpression node)
    {
        if (_typeChecker.TryGetConstValue(node, out ConstValue? constVal) && constVal != null)
        {
            if (TryEmitConstValue(constVal, _typeChecker.GetType(node)))
            {
                return;
            }
        }
        int size = _typeChecker.GetTypeAlignment(node.TargetType);
        Push(size.ToString());
    }

    public void Visit(TypeofExpression node)
    {
        if (!_typeChecker.TryGetConstValue(node, out ConstValue? value) || value == null ||
            !TryEmitConstValue(value, _typeChecker.GetType(node)))
            throw new InvalidOperationException("Missing compile-time type metadata");
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

    public void Visit(PrefixedStringLiteralExpression node) => Visit((CallExpression)node);

    private string GetDefaultValue(TypeExpression type)
    {
        type = _typeChecker.ResolveAlias(type);
        TypeExpression? pointee = type switch { PointerTypeExpression p => p.Inner, ManagedTypeExpression m => m.Inner, _ => null };
        if (pointee != null && _typeChecker.ResolveAlias(pointee) is NamedTypeExpression iface && _typeChecker.IsInterface(iface))
            return "zeroinitializer";
        if (type is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression)
            return "null";
        if (type is ArrayTypeExpression array)
        {
            string element = GetDefaultValue(array.ElementType);
            if (element is "0" or "0.0" or "null" or "zeroinitializer") return "zeroinitializer";
            return "[ " + string.Join(", ", Enumerable.Repeat($"{EmitType(array.ElementType)} {element}", (int)array.Size!)) + " ]";
        }
        if (type is NamedTypeExpression named)
        {
            if (_typeChecker.GetStruct(named.Name) is TypeChecker.StructInfo str)
                return "{ " + string.Join(", ", str.Fields.Select(f => $"{EmitType(f.Type)} {GetDefaultValue(f.Type)}")) + " }";
            if (_typeChecker.GetClass(named.Name) is TypeChecker.ClassInfo cls)
            {
                var fields = new List<string> { $"i8** bitcast ([{cls.VirtualMethods.Count} x i8*]* @{cls.Name}$vtable to i8**)" };
                fields.AddRange(cls.Fields.Select(f => $"{EmitType(f.Type)} {GetDefaultValue(f.Type)}"));
                return "{ " + string.Join(", ", fields) + " }";
            }
            if (_typeChecker.IsInterface(named)) return "zeroinitializer";
            if (named.Name is "float" or "double") return "0.0";
        }
        return "0";
    }
    public void Visit(NamespaceAccessExpression node)
    {
        if (TryEmitStaticRead(node)) return;
        if (_typeChecker.TryGetConstValue(node, out var constant) && constant != null &&
            TryEmitConstValue(constant, _typeChecker.GetType(node))) return;
        if (EmitFunctionValue(node)) return;
        if (EmitPropertyRead(node)) return;
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
    public void Visit(ArrayLiteralExpression node)
        => EmitExpressionWithCleanup(() => EmitArrayLiteral(node));

    private void EmitArrayLiteral(ArrayLiteralExpression node, ArrayTypeExpression? destination = null)
    {
        var type = destination ?? (ArrayTypeExpression)_typeChecker.GetType(node);
        string arrayType = EmitType(type);
        string elementType = EmitType(type.ElementType);
        string storage = NewTemp();
        EmitAlloca($"{arrayType}", storage);
        EmitManagedReferences(type, storage, true, initialize: true);
        CleanupManagedStorage(type, storage);
        var scope = _deferScopes[^1];
        var transferred = new List<IDeferAction>();
        for (int i = 0; i < node.Elements.Count; i++)
        {
            var element = node.Elements[i];
            EmitValueForTarget(element, type.ElementType);
            string value = Pop();
            string address = NewTemp();
            Emit($"    {address} = getelementptr {arrayType}, {arrayType}* {storage}, i32 0, i32 {i}");
            EmitManagedStore(type.ElementType, value, address);
            if (_typeChecker.HasDestructor(type.ElementType))
            {
                var cleanup = new TemporaryCleanup(type.ElementType, address);
                scope.Add(cleanup);
                transferred.Add(cleanup);
            }
        }
        foreach (var cleanup in transferred) scope.Remove(cleanup);
        string result = NewTemp();
        Emit($"    {result} = load {arrayType}, {arrayType}* {storage}");
        ProtectManagedValue(type, result);
        Push(result);
    }

    private void EmitArgument(AstNode argument, TypeExpression? target = null)
    {
        if (target != null && _typeChecker.ResolveAlias(target) is ArrayTypeExpression)
        {
            EmitValueForTarget(argument, target);
        }
        else if (_typeChecker.ResolveAlias(_typeChecker.GetType(argument)) is ArrayTypeExpression)
        {
            EmitAddress(argument);
            string address = Pop();
            string type = EmitType(_typeChecker.GetType(argument));
            string first = NewTemp();
            Emit($"    {first} = getelementptr {type}, {type}* {address}, i32 0, i32 0");
            Push(first);
        }
        else if (target != null) EmitValueForTarget(argument, target);
        else argument.Accept(this);
        if (target != null && _typeChecker.HasDestructor(target))
        {
            string value = Pop();
            ProtectArgument(target, value);
            Push(value);
        }
    }

    private void EmitValueForTarget(AstNode expression, TypeExpression target)
    {
        if (TryContextualIntegerLiteral(expression, target, out string integerLiteral)) Push(integerLiteral);
        else if (_typeChecker.CopiesValue(expression, target)) EmitCopyValue(expression, target);
        else if (_typeChecker.ResolveAlias(target) is ArrayTypeExpression targetArray && expression is ArrayLiteralExpression literalArray &&
            EmitType(targetArray) != EmitType(_typeChecker.GetType(expression)))
            EmitExpressionWithCleanup(() => EmitArrayLiteral(literalArray, targetArray));
        else if (_typeChecker.ResolveAlias(target) is ArrayTypeExpression { Size: not null } array &&
            expression is LiteralExpression { Token.Kind: TokenKind.StringLiteral } literal)
        {
            byte[] bytes = StringLiteralEncoding.Bytes(literal.Token.Text[1..^1]);
            Push("[" + string.Join(", ", Enumerable.Range(0, array.Size.Value).Select(i => "i8 " + (i < bytes.Length ? bytes[i] : 0))) + "]");
        }
        else if (_typeChecker.ResolveAlias(target) is ArrayTypeExpression &&
            expression is IdentifierExpression or MemberAccessExpression or IndexExpression)
        {
            EmitAddress(expression);
            string address = Pop();
            string type = EmitType(target);
            string value = NewTemp();
            Emit($"    {value} = load {type}, {type}* {address}");
            ProtectManagedValue(target, value);
            Push(value);
        }
        else if (expression is ArrayLiteralExpression && _typeChecker.ResolveAlias(target) is PointerTypeExpression)
            EmitArgument(expression);
        else expression.Accept(this);
    }

    public void Visit(IndexExpression node)
        => EmitExpressionWithCleanup(() => EmitIndex(node));

    private void EmitIndex(IndexExpression node)
    {
        if (EmitPropertyRead(node)) return;
        EmitIndexAddress(node);
        string elemPtr = Pop();
        TypeExpression elemType = _typeChecker.GetType(node);
        string llvmElemType = EmitType(elemType);
        string val = NewTemp();
        if (_typeChecker.ResolveAlias(elemType) is ArrayTypeExpression)
            Emit($"    {val} = getelementptr {llvmElemType}, {llvmElemType}* {elemPtr}, i32 0, i32 0");
        else
            Emit($"    {val} = load {llvmElemType}, {llvmElemType}* {elemPtr}");
        PushManaged(node, val);
    }
    public void Visit(DeferStatement node)
    {
        if (_deferScopes.Count == 0)
            throw new Exception($"defer statement outside of block scope on line {node.Line}");

        _deferScopes[^1].Add(new AstNodeDeferAction(node.Statement, _locals));
    }

    public void Visit(DeleteStatement node)
    {
        node.Target.Accept(this);
        string ptrVal = Pop();
        TypeExpression ptrType = _typeChecker.GetType(node.Target);
        if (TypeChecker.IsError(ptrType))
        {
            return;
        }
        string llvmPtrType = EmitType(ptrType);
        if (_typeChecker.ResolveAlias(ptrType) is PointerTypeExpression interfacePointer &&
            _typeChecker.ResolveAlias(interfacePointer.Inner) is NamedTypeExpression iface && _typeChecker.IsInterface(iface))
        {
            GuardNonNull(ptrVal, llvmPtrType);
            string data = EmitInterfaceDestruction(ptrVal, iface.Name);
            Emit($"    call void @{RuntimeMemorySymbol("Free")}(i8* {data})");
            return;
        }

        if (_typeChecker.ResolveAlias(ptrType) is PointerTypeExpression pointer &&
            _typeChecker.ResolveAlias(pointer.Inner) is ArrayTypeExpression array)
        {
            EmitDestroy(array, ptrVal);
        }
        else if (_typeChecker.TryGetClassDestructorCall(node, out (TypeChecker.ClassInfo Class, bool IsVirtual, int SlotIndex) dtorCall))
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
        else if (_typeChecker.TryGetStructDestructorCall(node, out TypeChecker.StructInfo? structInfo) && structInfo != null)
        {
            string structPtr = ptrVal;
            if (llvmPtrType != $"%{structInfo.Name}*")
            {
                structPtr = NewTemp();
                Emit($"    {structPtr} = bitcast {llvmPtrType} {ptrVal} to %{structInfo.Name}*");
            }
            string dtorNs = structInfo.Namespace;
            string dtorMangled = dtorNs.Length > 0
                ? $"gflat${dtorNs}${structInfo.Name}$dtor"
                : $"gflat${structInfo.Name}$dtor";
            Emit($"    call void @{dtorMangled}(%{structInfo.Name}* {structPtr})");
        }

        if (_typeChecker.ResolveAlias(ptrType) is PointerTypeExpression rawPointer &&
            !_typeChecker.HasDestructor(rawPointer.Inner) && HasManagedReferences(rawPointer.Inner) &&
            rawPointer.Inner is not ArrayTypeExpression)
            EmitManagedReferences(rawPointer.Inner, ptrVal, false);

        // gflat$Memory$Free is defined in user code (or std/libc/Memory/Memory.gf), no declare needed
        _externNames.Add(RuntimeMemorySymbol("Free"));

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

        Emit($"    call void @{RuntimeMemorySymbol("Free")}(i8* {castPtr})");
    }
    public void Visit(BreakStatement node)
    {
        if (_breakLabels.Count == 0)
            throw new Exception("break outside of loop");
        int targetDepth = _loopDeferDepths.Peek();
        EmitDefersDownTo(targetDepth);
        if (_hasTerminated) return;
        Emit($"    br label %{_breakLabels.Peek()}");
        _hasTerminated = true;
    }
    public void Visit(ContinueStatement node)
    {
        if (_continueLabels.Count == 0)
            throw new Exception("continue outside of loop");
        int targetDepth = _loopDeferDepths.Peek();
        EmitDefersDownTo(targetDepth);
        if (_hasTerminated) return;
        Emit($"    br label %{_continueLabels.Peek()}");
        _hasTerminated = true;
    }
    public void Visit(AttributeNode node) { }
    public void Visit(GlobalExpression node) { }
    public void Visit(FunctionPointerTypeExpression node) { }
    public void Visit(ReferenceTypeExpression node) { }
    public void Visit(AliasDeclaration node) { }
    public void Visit(EnumDeclaration node) { }
    public void Visit(EnumMemberDeclaration node) { }

    public void Visit(CastExpression node)
    {
        EmitValueForTarget(node.Operand, node.TargetType);
        string val = Pop();
        TypeExpression srcType = _typeChecker.GetType(node.Operand);
        TypeExpression dstType = node.TargetType;
        string castVal = EmitCast(val, srcType, dstType);
        if (_typeChecker.ResolveAlias(dstType) is PointerTypeExpression { IsNullable: false } or FunctionPointerTypeExpression { IsNullable: false } or ManagedTypeExpression { IsNullable: false })
            GuardNonNull(castVal, EmitType(dstType));
        PushManaged(node, castVal);
    }

    public void Visit(LambdaExpression node)
    {
        int id = _lambdaCounter++;
        string funcName = $"gflat$lambda${id}";

        TypeExpression returnType = _typeChecker.GetLambdaReturnType(node);
        string llvmReturnType = EmitType(returnType);

        // Save current emitter state
        bool savedEhFunction = _ehFunction, savedEhBoundary = _ehBoundary, savedEhCleanup = _ehCleanup;
        int savedEhCleanupTryDepth = _ehCleanupTryDepth;
        _ehCleanup = false;
        _ehCleanupTryDepth = 0;
        StringBuilder savedOutput = _output;
        Dictionary<string, string> savedLocals = new(_locals);
        int savedTempCounter = _tempCounter;
        string savedReturnType = _currentFunctionReturnType;
        TypeExpression? savedExpectedType = _currentFunctionExpectedType;
        bool savedTerminated = _hasTerminated;
        string? savedDestructorExit = _destructorExitLabel;
        int savedExpressionDepth = _expressionDepth;
        StringBuilder? savedTemporaryPrologue = _temporaryPrologue;
        bool savedInsideMain = _isInsideMain;
        var savedTryStack = _emitterTryStack.ToArray();
        List<List<IDeferAction>> savedDeferScopes = new(_deferScopes);
        var savedParameterOwners = _parameterOwners.ToArray();
        var savedOwnerLiveFlags = new Dictionary<string, string>(_ownerLiveFlags);

        // Prepare lambda context
        _output = _lambdaFunctions;
        _locals.Clear();
        _parameterOwners.Clear();
        _ownerLiveFlags.Clear();
        _tempCounter = 0;
        _currentFunctionReturnType = llvmReturnType;
        _currentFunctionExpectedType = returnType;
        _hasTerminated = false;
        _destructorExitLabel = null;
        _expressionDepth = 0;
        _temporaryPrologue = null;
        _isInsideMain = false;
        _emitterTryStack.Clear();
        _deferScopes.Clear();

        string parameters = string.Join(", ", node.Parameters.Select(p => $"{EmitParamType(p.Type)} %{p.Name}"));
        parameters = WithManagedResultParameter(returnType, parameters, named: true);
        Emit($"define {EmitFunctionReturnType(returnType)} @{funcName}({parameters}){InlineHint(node)} {{");
        Emit("entry:");

        foreach (Parameter p in node.Parameters)
        {
            if (p.Type is ReferenceTypeExpression) { _locals[p.Name] = $"%{p.Name}"; continue; }
            string pType = EmitType(p.Type);
            string allocaPtr = NewTemp();
            EmitAlloca($"{pType}", allocaPtr);
            Emit($"    store {pType} %{p.Name}, {pType}* {allocaPtr}");
            RegisterManagedParameter(p, allocaPtr);
            _locals[p.Name] = allocaPtr;
            if (_typeChecker.HasDestructor(p.Type)) _parameterOwners.Add(RegisterOwner(p.Type, allocaPtr, true));
        }

        if (node.IsExpressionBody)
        {
            _deferScopes.Add(new(_parameterOwners));
            _parameterOwners.Clear();
            EmitExpressionWithCleanup(() =>
            {
                EmitValueForTarget(node.Body, returnType);
                string? res = null;
                if (llvmReturnType != "void")
                {
                    res = Pop();
                    TypeExpression bodyType = _typeChecker.GetType(node.Body);
                    res = EmitImplicitCast(res, bodyType, returnType, node.Body);
                    if (UsesManagedResultStorage(returnType))
                        EmitManagedStore(returnType, res, "%gc.result");
                }
                EmitDefersDownTo(0);
                if (res == null || UsesManagedResultStorage(returnType))
                    Emit("    ret void");
                else Emit($"    ret {llvmReturnType} {res}");
                _hasTerminated = true;
            });
            _deferScopes.RemoveAt(_deferScopes.Count - 1);
        }
        else
        {
            node.Body.Accept(this);
            if (!_hasTerminated)
            {
                if (llvmReturnType == "void" || UsesManagedResultStorage(returnType))
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
        _ehFunction = savedEhFunction; _ehBoundary = savedEhBoundary;
        _locals.Clear();
        foreach (KeyValuePair<string, string> kvp in savedLocals)
        {
            _locals[kvp.Key] = kvp.Value;
        }
        _tempCounter = savedTempCounter;
        _currentFunctionReturnType = savedReturnType;
        _currentFunctionExpectedType = savedExpectedType;
        _hasTerminated = savedTerminated;
        _destructorExitLabel = savedDestructorExit;
        _expressionDepth = savedExpressionDepth;
        _temporaryPrologue = savedTemporaryPrologue;
        _isInsideMain = savedInsideMain;
        _emitterTryStack.Clear();
        foreach (var handler in savedTryStack.Reverse()) _emitterTryStack.Push(handler);
        _deferScopes.Clear();
        _deferScopes.AddRange(savedDeferScopes);
        _parameterOwners.Clear();
        _parameterOwners.AddRange(savedParameterOwners);
        _ownerLiveFlags.Clear();
        foreach (var flag in savedOwnerLiveFlags) _ownerLiveFlags.Add(flag.Key, flag.Value);

        // Push the function address as a function pointer
        Push($"@{funcName}");
    }

    private string EmitCheckAndHandleCallException(string callRes, bool isVoid, string retType)
        => throw new InvalidOperationException("Legacy exception-return lowering is disabled");

    private void EmitExceptionDestroyHelper()
    {
        if (_typeChecker.GetClass("Exception") == null || _typeChecker.GetMemoryFunction("Free") == null) return;
        _tempCounter = 0;
        Emit("define void @gflat_destroy_exception(%Exception* %exception) {");
        Emit("entry:");
        Emit("    %vt_slot = getelementptr %Exception, %Exception* %exception, i32 0, i32 0");
        Emit("    %vt = load i8**, i8*** %vt_slot");
        foreach (var cls in _typeChecker.GetAllClasses().Where(c => _typeChecker.HasAnyDestructor(c) ||
            (_typeChecker.UsesCollector && HasManagedReferences(new NamedTypeExpression(c.Name, null, 0)))))
        {
            string next = NewLabel("exception_next");
            string destroy = NewLabel("exception_destroy");
            string table = NewTemp();
            string match = NewTemp();
            Emit($"    {table} = bitcast [{cls.VirtualMethods.Count} x i8*]* @{cls.Name}$vtable to i8**");
            Emit($"    {match} = icmp eq i8** %vt, {table}");
            Emit($"    br i1 {match}, label %{destroy}, label %{next}");
            Emit($"{destroy}:");
            string typed = NewTemp();
            Emit($"    {typed} = bitcast %Exception* %exception to %{cls.Name}*");
            EmitDestroy(new NamedTypeExpression(cls.Name, null, 0), typed);
            Emit("    br label %release");
            Emit($"{next}:");
        }
        Emit("    br label %release");
        Emit("release:");
        Emit("    %raw = bitcast %Exception* %exception to i8*");
        EmitExceptionFree("%raw");
        Emit("    ret void");
        Emit("}");
    }

    private void EmitIsInstanceHelper()
    {
        _tempCounter = 0;
        Emit("define i8** @gflat_get_parent_vtable(i8** %vt) {");
        Emit("entry:");
        int idx = 0;
        foreach (TypeChecker.ClassInfo cls in _typeChecker.GetAllClasses().Where(c => !c.IsStatic))
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

    public void Visit(ThrowStatement node) => EmitNativeThrow(node);
    public void Visit(TryStatement node) => EmitNativeTry(node);

    public void Visit(CatchClause node)
    {
    }
}
