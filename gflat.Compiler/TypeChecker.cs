using gflat.ast;
using gflat.CompileExceptions;
using gflat.comptime;
using gflat.symbols;
using gflat.semantics;
using gflat.diagnostics;

namespace gflat
{
    public partial class TypeChecker : IVisitor, IConstEvaluationContext
    {
        private readonly Dictionary<AstNode, TypeExpression> _types = new();
        private readonly Stack<Dictionary<string, TypeExpression>> _scopes = new();

        private readonly ConstEvaluator _constEvaluator;
        private readonly Dictionary<string, ConstValue> _constVariablesByName = new();
        private readonly Dictionary<AstNode, ConstValue> _constValues = new();
        private readonly HashSet<string> _constVariableNames = new();
        private readonly SymbolTable _symbols;
        public SymbolTable Symbols => _symbols;
        private readonly DiagnosticBag _diagnostics;
        public DiagnosticBag Diagnostics => _diagnostics;
        private int _loopDepth = 0;

        private readonly bool _recoverErrors;
        public TypeChecker(DiagnosticBag? diagnostics = null, bool recoverErrors = false)
        {
            _recoverErrors = recoverErrors;
            _diagnostics = diagnostics ?? new DiagnosticBag();
            _constEvaluator = new ConstEvaluator(this);
            _symbols = new SymbolTable(
                _classes,
                _structs,
                _interfaces,
                _enums,
                _genericClasses,
                _genericStructs,
                _genericMethods,
                _namespaceScopes,
                _globalScope,
                _functionNamespaces,
                _operatorNamespaces,
                _scopes,
                _localAliases,
                _localEnums,
                _usingNamespaces
            );
            _symbols.EnclosingTypeNameProvider = () => _currentClass?.Name ?? _currentStruct?.Name;
            _symbols.AliasResolver = ResolveAlias;
            _symbols.NameFormatter = DisplayName;
            _symbols.TypeFormatter = DisplayTypeName;
            _symbols.MethodTypeResolver = ResolveHierarchyMethodType;
        }

        public bool TryGetConstValueByName(string name, out ConstValue? value) => checkingDefaults.Count > 0
            ? TryGetDefaultConstant(name, out value) : BoundConstant(name, out value);

        public bool TryGetConstValue(AstNode node, out ConstValue? value) =>
            _constValues.TryGetValue(node, out value);

        public bool IsConstVariable(string name) =>
            BoundConstantIsReadOnly(name);

        public MethodDeclaration? ResolveFunctionForComptime(string name) =>
            ResolveFunction(name);

        private readonly NamespaceScope _globalScope = new();
        private NamespaceScope _currentNamespace
        {
            get => _symbols.CurrentNamespace;
            set => _symbols.CurrentNamespace = value;
        }
        private readonly Dictionary<NamespaceDeclaration, NamespaceScope> _namespaceScopes = new();

        private Dictionary<string, StructInfo> _structs = new();
        private Dictionary<string, EnumInfo> _enums = new();
        private Dictionary<string, InterfaceInfo> _interfaces = new();
        private readonly Dictionary<CallExpression, (InterfaceInfo Interface, int SlotIndex, MethodDeclaration Method)> _interfaceMethodCalls = new();
        private readonly Dictionary<MethodDeclaration, string> _functionNamespaces = new();
        private readonly Dictionary<CallExpression, AstNode> _resolvedCalls = new();
        private readonly Dictionary<ReturnStatement, string> _ownedReturns = new();
        public string? GetTransferredLocal(ReturnStatement statement) => _ownedReturns.GetValueOrDefault(statement);
        private readonly Dictionary<AstNode, AstNode> _functionAddressTargets = new();
        private readonly HashSet<CallExpression> _indirectCalls = new();
        private readonly Stack<Dictionary<string, TypeExpression>> _localAliases = new();
        private readonly Stack<Dictionary<string, EnumInfo>> _localEnums = new();
        private int _localEnumCounter;
        private readonly Dictionary<AstNode, (EnumInfo Enum, long Value)> _resolvedEnumMembers = new();
        private readonly Stack<int> _lambdaScopeBoundaries = new();
        private readonly Stack<bool> _lambdaStaticStack = new();
        private readonly Stack<List<TypeExpression>> _actualReturnTypes = new();
        private readonly Dictionary<LambdaExpression, TypeExpression> _lambdaReturnTypes = new();
        private readonly Dictionary<BinaryExpression, (string StructName, OperatorDeclaration Operator)> _operatorTargets = new();
        private readonly Dictionary<UnaryExpression, (string StructName, OperatorDeclaration Operator)> _unaryOperatorTargets = new();
        private readonly Dictionary<AssignmentExpression, (string StructName, OperatorDeclaration Operator)> _compoundOperatorTargets = new();
        private readonly Dictionary<OperatorDeclaration, string> _operatorNamespaces = new();
        private AstNode? _currentFunction = null;
        private readonly Stack<TryStatement> _tryStack = new();
        private readonly HashSet<AstNode> _throwingCalls = new();
        private int _foreachCounter = 0;
        private readonly HashSet<VariableDeclaration> precheckedForeachInitializers = new();
        private readonly HashSet<CallExpression> precheckedForeachCalls = new();

        public bool CanFunctionThrow(AstNode func) => func is MethodDeclaration method && !method.IsNoThrow && !IsEntryPoint(method) ||
            func is ConstructorDeclaration { IsNoThrow: false };
        public bool CanCallThrow(AstNode call) => call is CallExpression or NewExpression || _throwingCalls.Contains(call);
        public IEnumerable<ClassInfo> GetAllClasses() => _classes.Values;

        public AstNode? GetTypeDeclaration(string identity)
        {
            if (_structs.TryGetValue(identity, out var structure)) return structure.Declaration;
            if (_classes.TryGetValue(identity, out var cls)) return cls.Declaration;
            return _interfaces.GetValueOrDefault(identity)?.Declaration;
        }

        public bool TryGetOperatorTarget(BinaryExpression node, out (string StructName, OperatorDeclaration Operator) target) =>
            _operatorTargets.TryGetValue(node, out target);

        public bool TryGetUnaryOperatorTarget(UnaryExpression node, out (string StructName, OperatorDeclaration Operator) target) =>
            _unaryOperatorTargets.TryGetValue(node, out target);

        public bool TryGetCompoundOperatorTarget(AssignmentExpression node, out (string StructName, OperatorDeclaration Operator) target) =>
            _compoundOperatorTargets.TryGetValue(node, out target);

        public string GetOperatorNamespace(OperatorDeclaration op) =>
            _operatorNamespaces.TryGetValue(op, out string? ns) ? ns : "";

        public TypeExpression GetLambdaReturnType(LambdaExpression node) => _lambdaReturnTypes[node];


        private readonly HashSet<string> _usingNamespaces = new();
        private IEnumerable<string> CurrentUsings => SourceContext.Current?.SourceId is SourceId id &&
            _compilationUnit?.FileUsings.TryGetValue(id, out var usings) == true ? usings : _usingNamespaces;
        private string _currentNamespacePath
        {
            get => _symbols.CurrentNamespacePath;
            set => _symbols.CurrentNamespacePath = value;
        }
        private readonly Dictionary<string, StructDeclaration> _genericStructs = new();
        private readonly Dictionary<string, ClassDeclaration> _genericClasses = new();
        private readonly Dictionary<string, MethodDeclaration> _genericMethods = new();
        private CompilationUnit? _compilationUnit = null;

        public string GetFunctionNamespace(MethodDeclaration method) =>
            _functionNamespaces.TryGetValue(method, out string? ns) ? ns : "";

        public AstNode? GetResolvedCall(CallExpression call) =>
            extensionCalls.TryGetValue(call, out var lowered) ? GetResolvedCall(lowered) :
            _resolvedCalls.TryGetValue(call, out AstNode? target) ? target : null;

        public AstNode? GetFunctionAddressTarget(AstNode unary) =>
            _functionAddressTargets.TryGetValue(unary, out AstNode? target) ? target : null;

        public bool IsIndirectCall(CallExpression call) => _indirectCalls.Contains(call);

        public bool TryGetInterfaceCall(CallExpression node, out (InterfaceInfo Interface, int SlotIndex, MethodDeclaration Method) call) =>
            _interfaceMethodCalls.TryGetValue(node, out call);

        public InterfaceInfo? GetInterface(string name) =>
            _interfaces.TryGetValue(name, out InterfaceInfo? info) ? info : null;

        public bool IsInterface(string name) => _interfaces.ContainsKey(name);

        public StructInfo? GetStructByIdentity(string identity) => _structs.GetValueOrDefault(identity);
        TypeChecker.StructInfo? IConstEvaluationContext.GetStruct(string identity) => GetStructByIdentity(identity);

        public StructInfo? GetStruct(string name)
        {
            name = ResolveTypeIdentity(name, null) ?? name;
            if (name.Contains("::"))
            {
                name = name.Replace("::", ".");
            }

            if (_structs.TryGetValue(name, out StructInfo? info))
            {
                return info;
            }

            string? nested = ResolveNestedTypeName(name);
            if (nested != null && _structs.TryGetValue(nested, out StructInfo? nestedInfo))
            {
                return nestedInfo;
            }

            return null;
        }

        public EnumInfo? GetEnum(string name)
        {
            if (_enums.TryGetValue(name, out EnumInfo? info))
            {
                return info;
            }
            if (name.Contains("::") && _enums.TryGetValue(name.Replace("::", "."), out EnumInfo? dotInfo))
            {
                return dotInfo;
            }
            return null;
        }

        public bool TryGetEnumMember(AstNode node, out long value, out TypeExpression? underlyingType)
        {
            if (_resolvedEnumMembers.TryGetValue(node, out (EnumInfo Enum, long Value) info))
            {
                value = info.Value;
                underlyingType = info.Enum.UnderlyingType;
                return true;
            }
            value = 0;
            underlyingType = null;
            return false;
        }

        private StructInfo? _currentStruct = null;
        private bool _inDefer = false;

        public class StructInfo
        {
            public StructDeclaration? Declaration;
            public string Name = "";
            public string Namespace = "";
            public List<(string Name, TypeExpression Type)> Fields = new();
            public List<FieldDeclaration> FieldDeclarations = new();
            public Dictionary<string, FieldDeclaration> FieldDeclarationsByName = new();
            public List<ConstructorDeclaration> Constructors = new();
            public Dictionary<string, MethodDeclaration> Methods = new();
            public List<MethodDeclaration> AllMethods = new();
            public List<OperatorDeclaration> Operators = new();
            public List<string> Interfaces = new();
            public DestructorDeclaration? Destructor = null;
            public int FieldIndex(string name) => Fields.FindIndex(f => f.Name == name);
        }

        private readonly Dictionary<NewExpression, ConstructorDeclaration?> _resolvedConstructors = new();
        public ConstructorDeclaration? GetResolvedConstructor(NewExpression node) =>
            _resolvedConstructors.TryGetValue(node, out ConstructorDeclaration? ctor) ? ctor : null;

        public class InterfaceInfo
        {
            public InterfaceDeclaration? Declaration;
            public bool IsDeclaredStatic;
            public bool IsStaticContract => IsDeclaredStatic || Methods.Count > 0 && Methods.All(m => m.IsStatic);
            public string Name = "";
            public string Namespace = "";
            public List<MethodDeclaration> Methods = new();
            public Dictionary<string, MethodDeclaration> MethodsByName = new();
            public Dictionary<string, int> MethodIndices = new();
            public TokenKind Accessibility = TokenKind.Public;
        }

        public class ClassInfo
        {
            public ClassDeclaration? Declaration;
            public string Name = "";
            public string Namespace = "";
            public string? BaseClass = null;
            public bool IsAbstract = false;
            public bool IsStatic;
            internal bool HierarchyResolved;
            public TokenKind Accessibility = TokenKind.Public;
            public List<string> Interfaces = new();
            public List<(string Name, TypeExpression Type, TokenKind Accessibility, string DeclaringClass)> Fields = new();
            public List<FieldDeclaration> FieldDeclarations = new();
            public Dictionary<string, FieldDeclaration> FieldDeclarationsByName = new();
            public List<ConstructorDeclaration> Constructors = new();
            public Dictionary<string, (MethodDeclaration Method, string DeclaringClass)> Methods = new();
            public List<MethodDeclaration> AllMethods = new();
            public List<MethodDeclaration> VirtualMethods = new();
            public Dictionary<string, int> VTableSlots = new(); // Method name -> slot index
            public DestructorDeclaration? Destructor = null;
            public int DestructorSlot = -1; // -1 if non-virtual or no destructor
            public int Line = 0;
            public SourceSpan Span;
            public int FieldIndex(string name) => Fields.FindIndex(f => f.Name == name);
        }

        private readonly Dictionary<string, ClassInfo> _classes = new();
        public ClassInfo? GetClassByIdentity(string identity) => _classes.GetValueOrDefault(identity);
        public bool IsClassByIdentity(string identity) => _classes.ContainsKey(identity);
        TypeChecker.ClassInfo? IConstEvaluationContext.GetClass(string identity) => GetClassByIdentity(identity);

        public ClassInfo? GetClass(string name)
        {
            name = ResolveTypeIdentity(name, null) ?? name;
            if (name.Contains("::"))
            {
                name = name.Replace("::", ".");
            }

            if (_classes.TryGetValue(name, out ClassInfo? info))
            {
                return info;
            }

            string? nested = ResolveNestedTypeName(name);
            if (nested != null && _classes.TryGetValue(nested, out ClassInfo? nestedInfo))
            {
                return nestedInfo;
            }

            return null;
        }

        public bool IsClass(string name) => GetClass(name) != null;
        private ClassInfo? _currentClass = null;
        private ConstructorDeclaration? _currentConstructor = null;

        private string? ResolveNestedTypeName(string name)
        {
            if (name.Contains("::"))
            {
                name = name.Replace("::", ".");
            }

            if (name.Contains('.'))
            {
                if (_classes.ContainsKey(name) || _structs.ContainsKey(name))
                {
                    return name;
                }
            }

            string? currentName = _currentClass?.Name ?? _currentStruct?.Name;
            while (currentName != null)
            {
                string candidate = $"{currentName}.{name}";
                if (_classes.ContainsKey(candidate) || _structs.ContainsKey(candidate))
                {
                    return candidate;
                }

                int lastDot = currentName.LastIndexOf('.');
                if (lastDot >= 0)
                {
                    currentName = currentName.Substring(0, lastDot);
                }
                else
                {
                    break;
                }
            }

            if (_classes.ContainsKey(name) || _structs.ContainsKey(name))
            {
                return name;
            }

            return null;
        }

        private bool CanAccessPrivate(string? accessorName, string declaringClass)
        {
            if (accessorName == null)
            {
                return false;
            }
            if (accessorName == declaringClass)
            {
                return true;
            }
            if (accessorName.StartsWith(declaringClass + "."))
            {
                return true;
            }
            if (declaringClass.StartsWith(accessorName + "."))
            {
                return true;
            }
            return false;
        }

        private readonly Dictionary<ConstructorDeclaration, ConstructorDeclaration> _resolvedBaseConstructors = new();
        public ConstructorDeclaration? GetResolvedBaseConstructor(ConstructorDeclaration ctor) =>
            _resolvedBaseConstructors.TryGetValue(ctor, out ConstructorDeclaration? baseCtor) ? baseCtor : null;

        private readonly Dictionary<CallExpression, (ClassInfo Class, int SlotIndex, MethodDeclaration Method)> _virtualMethodCalls = new();
        public bool TryGetVirtualMethodCall(CallExpression node, out (ClassInfo Class, int SlotIndex, MethodDeclaration Method) call) =>
            _virtualMethodCalls.TryGetValue(node, out call);

        private readonly Dictionary<AstNode, (ClassInfo Class, bool IsVirtual, int SlotIndex)> _classDestructorCalls = new();
        public bool TryGetClassDestructorCall(AstNode node, out (ClassInfo Class, bool IsVirtual, int SlotIndex) call) =>
            _classDestructorCalls.TryGetValue(node, out call);

        private readonly Dictionary<AstNode, StructInfo> _structDestructorCalls = new();
        public bool TryGetStructDestructorCall(AstNode node, out StructInfo? sInfo) =>
            _structDestructorCalls.TryGetValue(node, out sInfo);

        public bool IsSubclassOf(string derivedName, string baseName)
        {
            if (derivedName == baseName) return true;
            if (_classes.TryGetValue(derivedName, out ClassInfo? info))
            {
                string? cur = info.BaseClass;
                while (cur != null)
                {
                    if (cur == baseName) return true;
                    if (_classes.TryGetValue(cur, out ClassInfo? parent))
                        cur = parent.BaseClass;
                    else
                        break;
                }
            }
            return false;
        }

        public bool ClassImplementsInterface(ClassInfo cInfo, string ifaceName)
        {
            if (cInfo.Interfaces.Contains(ifaceName)) return true;
            if (cInfo.BaseClass != null && _classes.TryGetValue(cInfo.BaseClass, out ClassInfo? baseInfo))
                return ClassImplementsInterface(baseInfo, ifaceName);
            return false;
        }

        public bool HasAnyDestructor(ClassInfo c)
        {
            return HasDestructor(NamedTypeExpression.Resolved(c.Name, 0));
        }

        public bool HasDestructor(TypeExpression type)
        {
            return HasDestructorInternal(type, new HashSet<string>());
        }

        private bool HasDestructorInternal(TypeExpression type, HashSet<string> visited)
        {
            type = ResolveAlias(type);
            if (type is ArrayTypeExpression array)
                return HasDestructorInternal(array.ElementType, visited);
            if (type is NamedTypeExpression named)
            {
                if (!visited.Add(named.Name))
                {
                    return false;
                }

                StructInfo? sInfo = GetStructByIdentity(named.Name);
                if (sInfo != null)
                {
                    if (sInfo.Destructor != null)
                    {
                        return true;
                    }
                    foreach ((string Name, TypeExpression Type) f in sInfo.Fields)
                    {
                        if (HasDestructorInternal(f.Type, visited))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                ClassInfo? cInfo = GetClassByIdentity(named.Name);
                if (cInfo != null)
                {
                    if (cInfo.Destructor != null || (cInfo.BaseClass != null &&
                        HasDestructorInternal(NamedTypeExpression.Resolved(cInfo.BaseClass, 0), visited)))
                    {
                        return true;
                    }
                    foreach ((string Name, TypeExpression Type, TokenKind Accessibility, string DeclaringClass) f in cInfo.Fields)
                    {
                        if (HasDestructorInternal(f.Type, visited))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
            return false;
        }

        public class EnumInfo
        {
            public string Name = "";
            public string Namespace = "";
            public TypeExpression UnderlyingType = new NamedTypeExpression("int", null, 0);
            public Dictionary<string, long> Members = new();
            public TokenKind Accessibility = TokenKind.Public;
        }

        public class NamespaceScope
        {
            public Dictionary<string, MethodDeclaration> Functions = new();
            public Dictionary<string, MethodDeclaration> GenericFunctions = new();
            public Dictionary<string, string> TypeNames = new();
            public Dictionary<string, ExternDeclaration> Externs = new();
            public Dictionary<string, TypeExpression> Aliases = new();
            public Dictionary<string, EnumInfo> Enums = new();
            public Dictionary<string, InterfaceInfo> Interfaces = new();
            public Dictionary<string, ClassInfo> Classes = new();
            public Dictionary<string, StructInfo> Structs = new();
            public Dictionary<string, FieldDeclaration> Fields = new();
            public Dictionary<string, NamespaceScope> Children = new();
            public NamespaceScope? Parent;
        }

        private FieldDeclaration? ResolveGlobalField(string name)
        {
            NamespaceScope? scope = _currentNamespace;
            while (scope != null)
            {
                if (scope.Fields.TryGetValue(name, out FieldDeclaration? field))
                    return field;
                scope = scope.Parent;
            }
            FieldDeclaration? found = null;
            foreach (string imported in CurrentUsings.Distinct())
            {
                var candidate = ResolveNamespaceByName(imported)?.Fields.GetValueOrDefault(name);
                if (candidate == null) continue;
                if (found != null && found != candidate)
                    throw new TypeCheckException($"Global field '{name}' is ambiguous between namespaces", SourceContext.Current ?? SourceContext.ForLine(0));
                found = candidate;
            }
            return found;
        }

        public TypeExpression GetType(AstNode node)
        {
            if (_types.TryGetValue(node, out TypeExpression? type))
            {
                return type;
            }
            return Error;
        }

        private void ReportError(DiagnosticDescriptor descriptor, int line, int column = 0, params object[] args)
        {
            _diagnostics.Report(descriptor, line, column, null, args);
        }

        private void ReportError(string message, int line, int column = 0)
        {
            _diagnostics.ReportError(message, line, column);
        }

        private void RecordType(AstNode node, TypeExpression type)
        {
            _types[node] = ResolveAlias(type);
            if (node is MemberAccessExpression or IdentifierExpression && IsExpressionReadOnly(node))
                _types[node] = TypeQualifiers.ReadOnly(_types[node]);
            if (extensions.Count != 0 && node is IdentifierExpression receiver && TryLookupVariable(receiver.Name, out var receiverSlotType))
            {
                extensionReceiverVariables.Add(receiver);
                extensionReceiverSlotTypes[receiver] = ParameterValueType(receiverSlotType!);
            }
        }

        private void PushScope()
        {
            _scopes.Push(new Dictionary<string, TypeExpression>());
            _localAliases.Push(new Dictionary<string, TypeExpression>());
            _localEnums.Push(new Dictionary<string, EnumInfo>());
            localConstants.Push(new Dictionary<string, (ConstValue Value, VariableDeclaration Declaration)>());
        }

        private void PopScope()
        {
            NullableScopeExit(_scopes.Peek());
            _scopes.Pop();
            _localAliases.Pop();
            _localEnums.Pop();
            localConstants.Pop();
        }

        private void DeclareVariable(string name, TypeExpression type, int line)
        {
            if (_scopes.Peek().ContainsKey(name))
            {
                ReportError(DiagnosticRules.GF3002_VariableAlreadyDeclared, line, 0, name);
                return;
            }
            _scopes.Peek()[name] = type;
        }

        private TypeExpression LookupVariable(string name, int line)
        {
            int depth = _scopes.Count;
            foreach (Dictionary<string, TypeExpression> scope in _scopes)
            {
                if (scope.TryGetValue(name, out TypeExpression? type))
                {
                    if (_lambdaScopeBoundaries.Count > 0 && depth <= _lambdaScopeBoundaries.Peek())
                    {
                        bool isStatic = _lambdaStaticStack.Peek();
                        if (isStatic)
                        {
                            throw new TypeCheckException($"A static lambda cannot reference outer local variable '{name}'", line);
                        }
                        else
                        {
                            throw new TypeCheckException($"Capturing outer local variable '{name}' is not currently supported", line);
                        }
                    }
                    return type;
                }
                depth--;
            }

            if (FindStaticStorage(name) is { } staticField) return ResolveAlias(staticField.Type);

            if (_currentStruct != null && !IsStaticFunctionContext)
            {
                int idx = _currentStruct.FieldIndex(name);
                if (idx >= 0)
                {
                    if (_lambdaScopeBoundaries.Count > 0)
                    {
                        bool isStatic = _lambdaStaticStack.Peek();
                        if (isStatic)
                        {
                            throw new TypeCheckException($"A static lambda cannot reference 'this'", line);
                        }
                        else
                        {
                            throw new TypeCheckException($"Capturing 'this' in a lambda is not currently supported", line);
                        }
                    }
                    return _currentStruct.Fields[idx].Type;
                }
            }

            if (_currentClass != null && !IsStaticFunctionContext)
            {
                int idx = _currentClass.FieldIndex(name);
                if (idx >= 0)
                {
                    if (_lambdaScopeBoundaries.Count > 0)
                    {
                        bool isStatic = _lambdaStaticStack.Peek();
                        if (isStatic)
                        {
                            throw new TypeCheckException($"A static lambda cannot reference 'this'", line);
                        }
                        else
                        {
                            throw new TypeCheckException($"Capturing 'this' in a lambda is not currently supported", line);
                        }
                    }
                    return _currentClass.Fields[idx].Type;
                }
            }

            FieldDeclaration? globalField = ResolveGlobalField(name);
            if (globalField != null)
            {
                TypeExpression gType = ResolveAlias(globalField.Type);
                if (globalField.IsConst && gType is PointerTypeExpression gPtr && !gPtr.IsReadOnly)
                {
                    gType = new PointerTypeExpression(gPtr.Inner, gPtr.IsNullable, gPtr.Line, isReadOnly: true);
                }
                return gType;
            }

            if (ResolveFunction(name) != null || ResolveExtern(name) != null)
            {
                ReportError($"Cannot use function '{name}' as a value without '&'. Did you mean '&{name}'?", line);
                return Error;
            }

            ReportError(DiagnosticRules.GF1003_UndeclaredIdentifier, line, 0, name);
            return Error;
        }

        public bool TryLookupVariable(string name, out TypeExpression? type)
        {
            foreach (Dictionary<string, TypeExpression> scope in _scopes)
            {
                if (scope.TryGetValue(name, out type))
                    return true;
            }

            if (FindStaticStorage(name) is { } staticField) { type = ResolveAlias(staticField.Type); return true; }

            if (_currentStruct != null && !IsStaticFunctionContext)
            {
                int idx = _currentStruct.FieldIndex(name);
                if (idx >= 0)
                {
                    type = _currentStruct.Fields[idx].Type;
                    return true;
                }
            }

            if (_currentClass != null && !IsStaticFunctionContext)
            {
                int idx = _currentClass.FieldIndex(name);
                if (idx >= 0)
                {
                    type = _currentClass.Fields[idx].Type;
                    return true;
                }
            }

            FieldDeclaration? globalField = ResolveGlobalField(name);
            if (globalField != null)
            {
                TypeExpression gType = ResolveAlias(globalField.Type);
                if (globalField.IsConst && gType is PointerTypeExpression gPtr && !gPtr.IsReadOnly)
                {
                    gType = new PointerTypeExpression(gPtr.Inner, gPtr.IsNullable, gPtr.Line, isReadOnly: true);
                }
                type = gType;
                return true;
            }

            type = null;
            return false;
        }

        private static NamedTypeExpression Byte => new NamedTypeExpression("byte", null, 0);
        private static NamedTypeExpression SByte => new NamedTypeExpression("sbyte", null, 0);
        private static NamedTypeExpression Short => new NamedTypeExpression("short", null, 0);
        private static NamedTypeExpression UShort => new NamedTypeExpression("ushort", null, 0);
        private static NamedTypeExpression Int => new NamedTypeExpression("int", null, 0);
        private static NamedTypeExpression UInt => new NamedTypeExpression("uint", null, 0);
        private static NamedTypeExpression Long => new NamedTypeExpression("long", null, 0);
        private static NamedTypeExpression ULong => new NamedTypeExpression("ulong", null, 0);
        private static NamedTypeExpression NInt => new NamedTypeExpression("nint", null, 0);
        private static NamedTypeExpression NUInt => new NamedTypeExpression("nuint", null, 0);
        private static NamedTypeExpression Float => new NamedTypeExpression("float", null, 0);
        private static NamedTypeExpression Double => new NamedTypeExpression("double", null, 0);
        private static NamedTypeExpression Bool => new NamedTypeExpression("bool", null, 0);
        private static NamedTypeExpression Char => new NamedTypeExpression("char", null, 0);
        private static PointerTypeExpression CharPtr => new PointerTypeExpression(Char, false, 0);

        private static NamedTypeExpression Void => new NamedTypeExpression("void", null, 0);
        private static NamedTypeExpression Null => new NamedTypeExpression("null", null, 0);
        public static NamedTypeExpression Error => new NamedTypeExpression("<error>", null, 0);

        public static bool IsError(TypeExpression? type)
        {
            return type is NamedTypeExpression { Name: "<error>" };
        }

        private static bool IsNumeric(TypeExpression type) =>
            type is NamedTypeExpression n && n.Name is "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "nint" or "nuint" or "float" or "double" or "extralong" or "char";

        private static bool IsNullable(TypeExpression type) =>
            type is PointerTypeExpression { IsNullable: true } or
            ManagedTypeExpression { IsNullable: true } or
            FunctionPointerTypeExpression { IsNullable: true };

        private static string OperatorText(TokenKind kind) => kind switch
        {
            TokenKind.Plus => "+",
            TokenKind.Minus => "-",
            TokenKind.Star => "*",
            TokenKind.Slash => "/",
            TokenKind.Percent => "%",
            TokenKind.EqualsEquals => "==",
            TokenKind.NotEquals => "!=",
            TokenKind.Less => "<",
            TokenKind.Greater => ">",
            TokenKind.LessEquals => "<=",
            TokenKind.GreaterEquals => ">=",
            TokenKind.AmpersandAmpersand => "&&",
            TokenKind.PipePipe => "||",
            TokenKind.LessLess => "<<",
            TokenKind.GreaterGreater => ">>",
            TokenKind.Ampersand => "&",
            TokenKind.Pipe => "|",
            TokenKind.Caret => "^",
            _ => kind.ToString()
        };

        public string GetTypeMangledName(TypeExpression type)
        {
            if (type is ReferenceTypeExpression reference) return "Ref$" + GetTypeMangledName(reference.Inner);
            type = ResolveAlias(type);
            if (type.IsReadOnlyValue)
                return "ReadOnly$" + GetTypeMangledName(type.WithReadOnlyValue(false));
            if (type is NamedTypeExpression named)
            {
                string baseName = named.Name;
                if (named.TypeArguments.Count > 0)
                {
                    string args = string.Join("$", named.TypeArguments.Select(GetTypeMangledName));
                    return $"{baseName}${args}";
                }
                return baseName;
            }
            if (type is PointerTypeExpression ptr)
            {
                return $"{GetTypeMangledName(ptr.Inner)}Ptr";
            }
            if (type is ManagedTypeExpression mgd)
            {
                return $"{GetTypeMangledName(mgd.Inner)}Managed${GetTypeMangledName(ResolveCollectorType(mgd.Collector, mgd.Line))}${(mgd.IsNullable ? "Nullable" : "NonNull")}";
            }
            if (type is ArrayTypeExpression arr)
            {
                return $"{GetTypeMangledName(arr.ElementType)}Arr";
            }
            if (type is FunctionPointerTypeExpression function)
                return (function.IsSignature ? "Signature$" : "FunctionPointer$") + GetTypeMangledName(function.ReturnType) +
                    "$Params" + function.ParameterTypes.Count + "$" + string.Join("$", function.ParameterTypes.Select(GetTypeMangledName)) +
                    (function.IsNullable ? "$Nullable" : "");
            return type.ToString() ?? "Unknown";
        }

        private bool TypeSatisfiesConstraint(TypeExpression argType, TypeExpression constraint, Action<TypeExpression>? constraintBinding = null)
        {
            argType = ResolveAlias(argType);
            constraint = ResolveAlias(constraint);
            constraintBinding?.Invoke(constraint);
            if (!AliasExposesConstraint(argType, constraint)) return false;

            if (constraint is NamedTypeExpression namedConstraint)
            {
                string constraintName = namedConstraint.Name;
                if (_interfaces.ContainsKey(constraintName))
                {
                    return TypeImplementsInterface(argType, constraintName);
                }
                if (_classes.ContainsKey(constraintName))
                {
                    return TypeDerivesFromClass(argType, constraintName);
                }
            }
            return false;
        }

        private bool TypeImplementsInterface(TypeExpression type, string ifaceName)
        {
            type = ResolveAlias(type);
            if (type is PointerTypeExpression ptr)
                type = ResolveAlias(ptr.Inner);
            if (type is ManagedTypeExpression mgd)
                type = ResolveAlias(mgd.Inner);

            if (type is NamedTypeExpression named)
            {
                if (_structs.TryGetValue(named.Name, out StructInfo? strInfo))
                {
                    return strInfo.Interfaces.Contains(ifaceName);
                }
                if (_classes.TryGetValue(named.Name, out ClassInfo? clsInfo))
                {
                    return ClassImplementsInterface(clsInfo, ifaceName);
                }
            }
            return false;
        }

        private bool TypeDerivesFromClass(TypeExpression type, string baseClassName)
        {
            type = ResolveAlias(type);
            if (type is PointerTypeExpression ptr)
                type = ResolveAlias(ptr.Inner);
            if (type is ManagedTypeExpression mgd)
                type = ResolveAlias(mgd.Inner);

            if (type is NamedTypeExpression named)
            {
                if (named.Name == baseClassName)
                    return true;
                if (_classes.TryGetValue(named.Name, out ClassInfo? clsInfo))
                {
                    string? currentBase = clsInfo.BaseClass;
                    while (currentBase != null)
                    {
                        if (currentBase == baseClassName)
                            return true;
                        if (_classes.TryGetValue(currentBase, out ClassInfo? nextBase))
                            currentBase = nextBase.BaseClass;
                        else
                            break;
                    }
                }
            }
            return false;
        }

        private NamedTypeExpression ResolveGenericType(string name, string? ns, List<TypeExpression> typeArgs, int line, Action<TypeExpression> publish)
        {
            name = ResolveTypeIdentity(name, ns) ?? name;
            return ResolveGenericTypeByIdentity(name, typeArgs, line, publish);
        }

        private NamedTypeExpression ResolveGenericTypeByIdentity(string name, List<TypeExpression> typeArgs, int line, Action<TypeExpression> publish)
        {
            if (_genericStructs.TryGetValue(name, out StructDeclaration? genericStruct))
            {
                return MonomorphizeStruct(genericStruct, typeArgs, line, publish);
            }
            if (_genericClasses.TryGetValue(name, out ClassDeclaration? genericClass))
            {
                return MonomorphizeClass(genericClass, typeArgs, line, publish);
            }
            throw new TypeCheckException($"Unknown generic type '{DisplayName(name)}'", line);
        }

        private NamedTypeExpression MonomorphizeStruct(StructDeclaration genericDef, List<TypeExpression> typeArgs, int line, Action<TypeExpression> publish)
        {
            ValidateAliasSpecialization(genericDef, genericDef.GenericParameters, typeArgs);
            if (typeArgs.Count != genericDef.GenericParameters.Count)
            {
                throw new TypeCheckException($"Generic struct '{DisplayName(genericDef.Name)}' expects {genericDef.GenericParameters.Count} type arguments, but got {typeArgs.Count}", line);
            }

            for (int i = 0; i < genericDef.GenericParameters.Count; i++)
            {
                GenericParameter param = genericDef.GenericParameters[i];
                TypeExpression arg = typeArgs[i];
                if (param.Constraint != null && !TypeSatisfiesDeclaredConstraint(genericDef, arg, param.Constraint))
                {
                    throw new TypeCheckException($"Type '{DisplayTypeName(arg)}' does not satisfy constraint '{DisplayTypeName(param.Constraint)}' for type parameter '{param.Name}' on struct '{DisplayName(genericDef.Name)}'", line);
                }
            }

            string mangledName = $"{genericDef.Name}${string.Join("$", typeArgs.Select(GetTypeMangledName))}";
            extensionGenericTypeOrigins[mangledName] = new NamedTypeExpression(genericDef.Name, null, line, typeArgs);
            _displayNames[mangledName] = DisplayName(genericDef.Name) + "<" + string.Join(", ", typeArgs.Select(DisplayTypeName)) + ">";
            CheckFailedTypeSpecialization(mangledName, line);
            if (_structs.ContainsKey(mangledName))
            {
                var identity = NamedTypeExpression.Resolved(mangledName, line);
                publish(identity);
                return identity;
            }
            using var specialization = EnterTypeSpecialization(mangledName, line);

            Dictionary<string, TypeExpression> typeMap = new();
            for (int i = 0; i < genericDef.GenericParameters.Count; i++)
            {
                typeMap[genericDef.GenericParameters[i].Name] = typeArgs[i];
            }

            AstCloner cloner = new AstCloner(typeMap, genericDef.Name, mangledName);
            StructDeclaration specialized = cloner.CloneStruct(genericDef);

            _compilationUnit?.Members.Add(specialized);
            RegisterMemberInScope(specialized, _globalScope, "", namespaceVisible: false);
            ResolveSpecializedBaseTypes(genericDef, specialized);
            publish(NamedTypeExpression.Resolved(mangledName, line));
            if (_compilationUnit != null) PrepareOverloads(_compilationUnit);
            CheckSpecialization(genericDef, specialized);

            return NamedTypeExpression.Resolved(mangledName, line);
        }

        private NamedTypeExpression MonomorphizeClass(ClassDeclaration genericDef, List<TypeExpression> typeArgs, int line, Action<TypeExpression> publish)
        {
            ValidateAliasSpecialization(genericDef, genericDef.GenericParameters, typeArgs);
            if (typeArgs.Count != genericDef.GenericParameters.Count)
            {
                throw new TypeCheckException($"Generic class '{DisplayName(genericDef.Name)}' expects {genericDef.GenericParameters.Count} type arguments, but got {typeArgs.Count}", line);
            }

            for (int i = 0; i < genericDef.GenericParameters.Count; i++)
            {
                GenericParameter param = genericDef.GenericParameters[i];
                TypeExpression arg = typeArgs[i];
                if (param.Constraint != null && !TypeSatisfiesDeclaredConstraint(genericDef, arg, param.Constraint))
                {
                    throw new TypeCheckException($"Type '{DisplayTypeName(arg)}' does not satisfy constraint '{DisplayTypeName(param.Constraint)}' for type parameter '{param.Name}' on class '{DisplayName(genericDef.Name)}'", line);
                }
            }

            string mangledName = $"{genericDef.Name}${string.Join("$", typeArgs.Select(GetTypeMangledName))}";
            extensionGenericTypeOrigins[mangledName] = new NamedTypeExpression(genericDef.Name, null, line, typeArgs);
            _displayNames[mangledName] = DisplayName(genericDef.Name) + "<" + string.Join(", ", typeArgs.Select(DisplayTypeName)) + ">";
            CheckFailedTypeSpecialization(mangledName, line);
            if (_classes.ContainsKey(mangledName))
            {
                var identity = NamedTypeExpression.Resolved(mangledName, line);
                publish(identity);
                return identity;
            }
            using var specialization = EnterTypeSpecialization(mangledName, line);

            Dictionary<string, TypeExpression> typeMap = new();
            for (int i = 0; i < genericDef.GenericParameters.Count; i++)
            {
                typeMap[genericDef.GenericParameters[i].Name] = typeArgs[i];
            }

            AstCloner cloner = new AstCloner(typeMap, genericDef.Name, mangledName);
            ClassDeclaration specialized = cloner.CloneClass(genericDef);

            _compilationUnit?.Members.Add(specialized);
            RegisterMemberInScope(specialized, _globalScope, "", namespaceVisible: false);
            ResolveSpecializedBaseTypes(genericDef, specialized);
            publish(NamedTypeExpression.Resolved(mangledName, line));
            if (_compilationUnit != null) PrepareOverloads(_compilationUnit);
            CheckSpecialization(genericDef, specialized);

            return NamedTypeExpression.Resolved(mangledName, line);
        }

        private MethodDeclaration MonomorphizeFunction(MethodDeclaration genericDef, List<TypeExpression> typeArgs, int line)
        {
            ValidateAliasSpecialization(genericDef, genericDef.GenericParameters, typeArgs);
            if (typeArgs.Count != genericDef.GenericParameters.Count)
            {
                throw new TypeCheckException($"Generic function '{DisplayName(genericDef.Name)}' expects {genericDef.GenericParameters.Count} type arguments, but got {typeArgs.Count}", line);
            }

            for (int i = 0; i < genericDef.GenericParameters.Count; i++)
            {
                GenericParameter param = genericDef.GenericParameters[i];
                TypeExpression arg = typeArgs[i];
                if (param.Constraint != null && !FunctionConstraintSatisfied(genericDef, arg, param.Constraint))
                {
                    throw new TypeCheckException($"Type '{DisplayTypeName(arg)}' does not satisfy constraint '{DisplayTypeName(param.Constraint)}' for type parameter '{param.Name}' on function '{DisplayName(genericDef.Name)}'", line);
                }
            }

            string definitionNamespace = GetFunctionNamespace(genericDef);
            bool ownedMethod = TryGetMethodOwner(genericDef, out string methodOwner, out bool ownerIsClass);
            string ownerPrefix = ownedMethod ? methodOwner + "$" : "";
            string mangledName = (definitionNamespace.Length > 0 ? definitionNamespace + "$" : "") + ownerPrefix + $"{genericDef.Name}${string.Join("$", typeArgs.Select(GetTypeMangledName))}";
            _displayNames[mangledName] = (definitionNamespace.Length > 0 ? definitionNamespace + "::" : "") + DisplayName(genericDef.Name) + "<" + string.Join(", ", typeArgs.Select(DisplayTypeName)) + ">";
            CheckFailedFunctionSpecialization(mangledName, line);
            if (_globalScope.Functions.TryGetValue(mangledName, out MethodDeclaration? existing))
            {
                return existing;
            }
            using var specialization = EnterFunctionSpecialization(mangledName, line);

            Dictionary<string, TypeExpression> typeMap = new();
            for (int i = 0; i < genericDef.GenericParameters.Count; i++)
            {
                typeMap[genericDef.GenericParameters[i].Name] = typeArgs[i];
            }

            AstCloner cloner = new AstCloner(typeMap, genericDef.Name, mangledName);
            MethodDeclaration specialized = cloner.CloneMethod(genericDef);

            _compilationUnit?.Members.Add(specialized);
            RegisterMemberInScope(specialized, _globalScope, "", namespaceVisible: false);
            if (ownedMethod) specializedMethodOwners[specialized] = (methodOwner, ownerIsClass);
            CheckSpecialization(genericDef, specialized);

            return specialized;
        }

        private bool InferTypeParameter(TypeExpression paramType, TypeExpression argType, ISet<string> genericParamNames, Dictionary<string, TypeExpression> inferred)
        {
            if (paramType is ReferenceTypeExpression rp && argType is ReferenceTypeExpression ra)
                return InferTypeParameter(rp.Inner, ra.Inner, genericParamNames, inferred);

            if (paramType is NamedTypeExpression namedParam && !namedParam.IsResolvedNominal && genericParamNames.Contains(namedParam.Name) && namedParam.TypeArguments.Count == 0)
            {
                TypeExpression resolvedArg = ResolveAlias(argType);
                if (inferred.TryGetValue(namedParam.Name, out TypeExpression? existing))
                {
                    return TypesMatch(existing, resolvedArg);
                }
                inferred[namedParam.Name] = resolvedArg;
                return true;
            }

            if (paramType is PointerTypeExpression ptrParam && argType is PointerTypeExpression ptrArg)
            {
                return InferTypeParameter(ptrParam.Inner, ptrArg.Inner, genericParamNames, inferred);
            }
            if (paramType is ManagedTypeExpression mgdParam && argType is ManagedTypeExpression mgdArg)
            {
                bool inner = InferTypeParameter(mgdParam.Inner, mgdArg.Inner, genericParamNames, inferred);
                bool collector = InferTypeParameter(ResolveCollectorType(mgdParam.Collector, mgdParam.Line), ResolveCollectorType(mgdArg.Collector, mgdArg.Line), genericParamNames, inferred);
                return inner && collector;
            }
            if (paramType is ArrayTypeExpression arrParam && argType is ArrayTypeExpression arrArg)
            {
                return InferTypeParameter(arrParam.ElementType, arrArg.ElementType, genericParamNames, inferred);
            }
            if (paramType is FunctionPointerTypeExpression function && argType is FunctionPointerTypeExpression argument)
            {
                if (function.IsManaged != argument.IsManaged || function.ParameterTypes.Count != argument.ParameterTypes.Count)
                    return false;
                var bindings = new Dictionary<string, TypeExpression>(inferred);
                bool Child(TypeExpression formal, TypeExpression actual) =>
                    InferTypeParameter(formal, actual, genericParamNames, bindings) ||
                    (!HasUnboundExtensionType(formal, genericParamNames) && TypesMatch(formal, actual));
                if (!Child(function.ReturnType, argument.ReturnType)) return false;
                foreach (var pair in function.ParameterTypes.Zip(argument.ParameterTypes))
                    if (!Child(pair.First, pair.Second)) return false;
                foreach (var binding in bindings) inferred[binding.Key] = binding.Value;
                return true;
            }
            return false;
        }

        private readonly Dictionary<TypeExpression, TypeExpression> resolvedSourceTypes = new();
        public TypeExpression ResolveAlias(TypeExpression type) => ResolveAlias(type, null);

        private TypeExpression ResolveAliasCore(TypeExpression type, Action<TypeExpression> publish)
        {
            if (type is NamedTypeExpression { IsResolvedNominal: true }) return type;
            if (type is ReferenceTypeExpression reference)
            {
                TypeExpression Project(TypeExpression inner) => new ReferenceTypeExpression(inner, reference.Line) { Span = reference.Span };
                return Project(ResolveAlias(reference.Inner, inner => publish(Project(inner))));
            }
            if (resolvedSourceTypes.TryGetValue(type, out var bound)) return bound;
            if (type is NestedTypeExpression nested)
            {
                ValidateAliasMember(new NamespaceAccessExpression(nested.Parent, nested.Member, nested.Line) { Span = nested.Span });
                TypeExpression resolvedParent = ResolveAlias(nested.Parent, parent =>
                {
                    if (TryProjectRegisteredNestedType(nested, parent) is { } identity)
                        publish(identity);
                });
                if (resolvedParent is NamedTypeExpression namedParent)
                {
                    string parentNs = namedParent.Namespace != null ? $"{namedParent.Namespace}::{namedParent.Name}" : namedParent.Name;
                    NamespaceScope? ns = namedParent.IsResolvedNominal ? null : ResolveNamespaceByName(parentNs);
                    if (ns != null)
                    {
                        if (ns.Aliases.TryGetValue(nested.Member, out TypeExpression? target))
                        {
                            return ResolveAliasBindingTarget(target, publish);
                        }

                        if (nested.TypeArguments.Count > 0)
                        {
                            List<TypeExpression> resolvedArgs = nested.TypeArguments.Select(ResolveAlias).ToList();
                            return ResolveGenericType(nested.Member, parentNs, resolvedArgs, nested.Line, publish);
                        }
                        return ResolveAlias(new NamedTypeExpression(nested.Member, parentNs, nested.Line), publish);
                    }

                    string candidate = $"{namedParent.Name}.{nested.Member}";
                    if (nested.TypeArguments.Count > 0)
                    {
                        List<TypeExpression> resolvedArgs = nested.TypeArguments.Select(ResolveAlias).ToList();
                        return ResolveGenericTypeByIdentity(candidate, resolvedArgs, nested.Line, publish);
                    }
                    return ResolveAlias(new NamedTypeExpression(candidate, namedParent.Namespace, nested.Line), publish);
                }
                throw new TypeCheckException($"Cannot resolve nested type member '{nested.Member}' on non-named type '{TypeName(resolvedParent)}'", nested.Line);
            }
            if (type is NamedTypeExpression named)
            {
                if (named.Namespace == null && named.TypeArguments.Count == 0 &&
                    !_localEnums.Any(local => local.ContainsKey(named.Name)))
                    foreach (var local in _localAliases)
                        if (local.TryGetValue(named.Name, out var localTarget))
                            return ResolveAliasBindingTarget(localTarget, publish);
                string? identity = ResolveTypeIdentity(named.Name, named.Namespace);
                if (named.TypeArguments.Count > 0)
                {
                    List<TypeExpression> resolvedArgs = named.TypeArguments.Select(ResolveAlias).ToList();
                    return identity != null ? ResolveGenericTypeByIdentity(identity, resolvedArgs, named.Line, publish)
                        : ResolveGenericType(named.Name, named.Namespace, resolvedArgs, named.Line, publish);
                }
                if (identity != null && (identity != named.Name || named.Namespace != null))
                    return resolvedSourceTypes[type] = MarkResolvedNominal(new NamedTypeExpression(identity, null, named.Line) { Span = named.Span, NameSpan = named.NameSpan });
                if (named.Namespace != null)
                {
                    NamespaceScope? ns = ResolveNamespaceByName(named.Namespace);
                    if (ns != null && ns.Aliases.TryGetValue(named.Name, out var target))
                        return ResolveAliasBindingTarget(target, publish);
                }
                else
                {
                    foreach (var localScope in _localAliases)
                    {
                        if (localScope.TryGetValue(named.Name, out var target))
                            return ResolveAliasBindingTarget(target, publish);
                    }
                    var cur = _currentNamespace;
                    while (cur != null)
                    {
                        if (cur.Aliases.TryGetValue(named.Name, out var target))
                            return ResolveAliasBindingTarget(target, publish);
                        cur = cur.Parent;
                    }
                    if (_globalScope.Aliases.TryGetValue(named.Name, out var gTarget))
                        return ResolveAliasBindingTarget(gTarget, publish);

                    TypeExpression? importedAlias = null;
                    NamespaceScope? aliasScope = null;
                    foreach (string imported in CurrentUsings)
                    {
                        var importedScope = ResolveNamespaceByName(imported);
                        if (importedScope?.Aliases.TryGetValue(named.Name, out var alias) != true) continue;
                        if (importedAlias != null && importedAlias != alias)
                            throw new TypeCheckException($"Alias '{named.Name}' is ambiguous between namespaces", named.Line);
                        importedAlias = alias;
                        aliasScope = importedScope;
                    }
                    if (importedAlias != null)
                    {
                        var previousScope = _currentNamespace;
                        _currentNamespace = aliasScope!;
                        try { return resolvedSourceTypes[type] = ResolveAliasBindingTarget(importedAlias, publish); }
                        finally { _currentNamespace = previousScope; }
                    }

                    string? resolvedNested = ResolveNestedTypeName(named.Name);
                    if (resolvedNested != null && resolvedNested != named.Name)
                    {
                        return resolvedSourceTypes[type] = new NamedTypeExpression(resolvedNested, named.Namespace, named.Line) { Span = named.Span, NameSpan = named.NameSpan };
                    }
                }
            }
            else if (type is PointerTypeExpression ptr)
            {
                TypeExpression Project(TypeExpression inner) => inner is FunctionPointerTypeExpression { IsSignature: true } signature
                    ? new FunctionPointerTypeExpression(signature.ReturnType, signature.ParameterTypes, false, ptr.IsNullable, ptr.Line) { Span = ptr.Span }
                    : new PointerTypeExpression(inner, ptr.IsNullable, ptr.Line, isReadOnly: ptr.IsReadOnly) { Span = ptr.Span };
                TypeExpression resolvedInner = ResolveAlias(ptr.Inner, inner => publish(Project(inner)));
                if (resolvedInner is FunctionPointerTypeExpression { IsSignature: true } signature)
                    return new FunctionPointerTypeExpression(signature.ReturnType, signature.ParameterTypes, false, ptr.IsNullable, ptr.Line);
                if (resolvedInner != ptr.Inner)
                    return new PointerTypeExpression(resolvedInner, ptr.IsNullable, ptr.Line, isReadOnly: ptr.IsReadOnly);
            }
            else if (type is ManagedTypeExpression mgd)
            {
                TypeExpression collector = ResolveCollectorType(mgd.Collector, mgd.Line);
                TypeExpression Project(TypeExpression inner) => new ManagedTypeExpression(inner, mgd.IsNullable, mgd.Line, mgd.IsReadOnly, collector)
                    { Span = mgd.Span, UsesDefaultCollector = mgd.UsesDefaultCollector };
                TypeExpression resolvedInner = ResolveAlias(mgd.Inner, inner => publish(Project(inner)));
                return new ManagedTypeExpression(resolvedInner, mgd.IsNullable, mgd.Line, mgd.IsReadOnly, collector) { UsesDefaultCollector = mgd.UsesDefaultCollector };
            }
            else if (type is ArrayTypeExpression arr)
            {
                // Complete the bound before the pure element-to-array publication.
                int? size = ResolveArrayLength(arr);
                TypeExpression Project(TypeExpression element) => new ArrayTypeExpression(element, size, arr.Line, arr.SizeExpression) { Span = arr.Span };
                TypeExpression resolvedElem = ResolveAlias(arr.ElementType, element => publish(Project(element)));
                if (resolvedElem != arr.ElementType || size != arr.Size)
                    return new ArrayTypeExpression(resolvedElem, size, arr.Line, arr.SizeExpression);
            }
            else if (type is FunctionPointerTypeExpression fnPtr)
            {
                var resolvedRet = ResolveAlias(fnPtr.ReturnType);
                var resolvedParams = fnPtr.ParameterTypes.Select(ResolveAlias).ToList();
                return new FunctionPointerTypeExpression(resolvedRet, resolvedParams, fnPtr.IsManaged, fnPtr.IsNullable, fnPtr.Line, fnPtr.IsSignature);
            }

            return type;
        }

        private NamespaceScope? ResolveNamespaceByName(string nsName)
        {
            if (nsName.Contains("::"))
            {
                string[] parts = nsName.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
                NamespaceScope? current = ResolveNamespaceByName(parts[0]);
                for (int i = 1; i < parts.Length && current != null; i++)
                {
                    if (!current.Children.TryGetValue(parts[i], out current))
                    {
                        return null;
                    }
                }
                return current;
            }

            NamespaceScope? scope = _currentNamespace;
            while (scope != null)
            {
                if (scope.Children.TryGetValue(nsName, out NamespaceScope? child))
                    return child;
                scope = scope.Parent;
            }
            if (_globalScope.Children.TryGetValue(nsName, out NamespaceScope? gChild))
                return gChild;
            return null;
        }

        public InterfaceInfo? ResolveInterface(NamedTypeExpression named)
        {
            if (named.Namespace != null)
            {
                NamespaceScope? ns = ResolveNamespaceByName(named.Namespace);
                if (ns != null && ns.Interfaces.TryGetValue(named.Name, out InterfaceInfo? info))
                    return info;
            }
            else
            {
                NamespaceScope? cur = _currentNamespace;
                while (cur != null)
                {
                    if (cur.Interfaces.TryGetValue(named.Name, out InterfaceInfo? info))
                        return info;
                    cur = cur.Parent;
                }
                if (_globalScope.Interfaces.TryGetValue(named.Name, out InterfaceInfo? gInfo))
                    return gInfo;
            }
            return _interfaces.TryGetValue(named.Name, out InterfaceInfo? fallback) ? fallback : null;
        }

        public bool IsInterface(NamedTypeExpression named) => ResolveInterface(named) != null;

        private void ValidateValueType(TypeExpression type, int line)
        {
            ValidateTypeUsage(type, line);
            if (ResolveAlias(type) is NamedTypeExpression { Name: "void" })
                throw new TypeCheckException("void has no value storage; use it as a return type or through an opaque pointer", line);
        }

        private void ValidateTypeUsage(TypeExpression type, int line)
        {
            TypeExpression resolved = ResolveAlias(type);
            if (resolved is NamedTypeExpression staticType && GetClassByIdentity(staticType.Name) is { IsStatic: true })
                throw new TypeCheckException($"Static class '{DisplayName(staticType.Name)}' cannot be used as an instance type", type.Span.Source != null ? type.Span : SourceContext.ForLine(line));
            if (resolved is NamedTypeExpression unknown && !IsPrimitive(unknown.Name) && !IsError(unknown) &&
                !_classes.ContainsKey(unknown.Name) && !_structs.ContainsKey(unknown.Name) &&
                !_interfaces.ContainsKey(unknown.Name) && ResolveEnum(unknown) == null)
                throw new TypeCheckException($"Unknown type '{unknown.Name}'", line);
            if (resolved is ReferenceTypeExpression reference)
            {
                ValidateTypeUsage(reference.Inner, line);
                if (ResolveAlias(reference.Inner) is NamedTypeExpression { Name: "void" } or ReferenceTypeExpression)
                    throw new TypeCheckException("ref requires a concrete value type", line);
                return;
            }
            if (resolved is ManagedTypeExpression managed)
            {
                ValidateInterfaceReference(managed.Inner, line);
                if (_globalScope.Aliases.ContainsKey("GC") || managed.Collector is not NamedTypeExpression { Name: "GC" })
                    GetCollectorBinding(managed);
                ValidateManagedGraph(managed);
                if (HasDestructor(managed.Inner))
                    throw new TypeCheckException("Managed pointers cannot reference types with destructors (including base classes and inline fields)", line);
                if (ResolveAlias(managed.Inner) is not NamedTypeExpression namedInner || !IsInterface(namedInner))
                    ValidateTypeUsage(managed.Inner, line);
                return;
            }
            if (resolved is NamedTypeExpression named && IsInterface(named))
            {
                if (GetInterface(named.Name) is { IsStaticContract: true })
                    throw new TypeCheckException($"Static interface '{DisplayName(named.Name)}' can only be used as a type constraint", line);
                throw new TypeCheckException($"Cannot use interface '{DisplayName(named.Name)}' as a value type. Interfaces must be used as pointers ('{DisplayName(named.Name)}*')", line);
            }
            else if (resolved is ArrayTypeExpression arr)
            {
                ValidateValueType(arr.ElementType, line);
            }
            else if (resolved is PointerTypeExpression ptr)
            {
                ValidateInterfaceReference(ptr.Inner, line);
                if (ResolveAlias(ptr.Inner) is not NamedTypeExpression inner || !IsInterface(inner))
                    ValidateTypeUsage(ptr.Inner, line);
            }
            else if (resolved is FunctionPointerTypeExpression function)
            {
                if (function.IsSignature) throw new TypeCheckException("A function signature has no value storage; use a function pointer", line);
                if (function.IsManaged)
                    throw new TypeCheckException("Managed function pointers are not supported", line);
                ValidateReturnType(function.ReturnType, line);
                foreach (var parameter in function.ParameterTypes) ValidateParameterType(parameter, line);
            }
        }

        private static int GetStringLiteralLength(string raw, int line) => StringLiteralEncoding.Bytes(raw, line).Length + 1;

        public static bool IsPrimitive(string name) =>
            name is "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or
                    "long" or "ulong" or "nint" or "nuint" or "float" or "double" or
                    "bool" or "char" or "extralong" or "void";

        public int GetTypeAlignment(TypeExpression type)
        {
            type = ResolveAlias(type);
            if (type is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression)
            {
                return TargetInfo.Default.PointerBytes;
            }
            if (type is ArrayTypeExpression arr)
            {
                return arr.Size == null ? TargetInfo.Default.PointerBytes : GetTypeAlignment(arr.ElementType);
            }
            if (type is NamedTypeExpression named)
            {
                EnumInfo? enumInfo = ResolveEnum(named);
                if (enumInfo != null)
                {
                    return GetTypeAlignment(enumInfo.UnderlyingType);
                }
                if (IsInterface(named))
                {
                    return TargetInfo.Default.PointerBytes;
                }
                StructInfo? structInfo = GetStructByIdentity(named.Name);
                if (structInfo != null)
                {
                    return GetStructAlignment(structInfo);
                }
                if (GetClassByIdentity(named.Name) is ClassInfo classInfo)
                {
                    return GetClassAlignment(classInfo);
                }
                return named.Name switch
                {
                    "bool" or "byte" or "sbyte" or "char" => 1,
                    "short" or "ushort" => 2,
                    "int" or "uint" or "float" => 4,
                    "long" or "ulong" or "double" => 8,
                    "nint" or "nuint" => TargetInfo.Default.PointerBytes,
                    "extralong" => 16,
                    "void" => 1,
                    _ => 4
                };
            }
            return TargetInfo.Default.PointerBytes;
        }

        public int GetStructAlignment(StructInfo structInfo)
        {
            using var layout = EnterLayoutQuery(structInfo.Name, SourceContext.Current?.Line ?? 0);
            int maxAlign = 1;
            foreach ((string Name, TypeExpression Type) field in structInfo.Fields)
            {
                using var context = EnterFieldLayoutContext(structInfo.Name, field.Name);
                int align = GetTypeAlignment(ResolveFieldLayoutType(field.Type));
                if (align > maxAlign)
                {
                    maxAlign = align;
                }
            }
            return maxAlign;
        }

        public int GetStructSize(StructInfo structInfo)
        {
            using var layout = EnterLayoutQuery(structInfo.Name, SourceContext.Current?.Line ?? 0);
            long offset = 0;
            int maxAlign = 1;
            SourceSpan span = default;
            foreach ((string Name, TypeExpression Type) field in structInfo.Fields)
            {
                using var context = EnterFieldLayoutContext(structInfo.Name, field.Name);
                var type = ResolveFieldLayoutType(field.Type);
                int fieldSize = GetTypeSize(type);
                int fieldAlign = GetTypeAlignment(type);
                if (fieldAlign > maxAlign)
                {
                    maxAlign = fieldAlign;
                }
                span = field.Type.Span;
                offset = (offset + fieldAlign - 1) & ~((long)fieldAlign - 1);
                offset += fieldSize;
                CheckAggregateLayoutSize(offset, span);
            }
            offset = (offset + maxAlign - 1) & ~((long)maxAlign - 1);
            return CheckAggregateLayoutSize(offset, span);
        }

        public int GetClassSize(ClassInfo classInfo)
        {
            using var layout = EnterLayoutQuery(classInfo.Name, SourceContext.Current?.Line ?? 0);
            long offset = TargetInfo.Default.PointerBytes; // vtable pointer
            int maxAlign = 8;
            SourceSpan span = classInfo.Span;
            foreach ((string Name, TypeExpression Type, TokenKind Accessibility, string DeclaringClass) field in classInfo.Fields)
            {
                using var context = EnterFieldLayoutContext(field.DeclaringClass, field.Name);
                var type = ResolveFieldLayoutType(field.Type);
                int fieldSize = GetTypeSize(type);
                int fieldAlign = GetTypeAlignment(type);
                if (fieldAlign > maxAlign)
                {
                    maxAlign = fieldAlign;
                }
                span = field.Type.Span;
                offset = (offset + fieldAlign - 1) & ~((long)fieldAlign - 1);
                offset += fieldSize;
                CheckAggregateLayoutSize(offset, span);
            }
            offset = (offset + maxAlign - 1) & ~((long)maxAlign - 1);
            return CheckAggregateLayoutSize(offset, span);
        }

        private static int CheckAggregateLayoutSize(long size, SourceSpan span)
        {
            if (size > int.MaxValue)
                throw new TypeCheckException("Aggregate layout exceeds the supported size of 2147483647 bytes", span);
            return (int)size;
        }

        public int GetTypeSize(TypeExpression type)
        {
            type = ResolveAlias(type);
            if (type is PointerTypeExpression ptr)
            {
                TypeExpression inner = ResolveAlias(ptr.Inner);
                if (inner is NamedTypeExpression namedInner && IsInterface(namedInner))
                {
                    return 16;
                }
                return TargetInfo.Default.PointerBytes;
            }
            if (type is ManagedTypeExpression mgd)
            {
                TypeExpression inner = ResolveAlias(mgd.Inner);
                if (inner is NamedTypeExpression namedInner && IsInterface(namedInner))
                {
                    return 16;
                }
                return TargetInfo.Default.PointerBytes;
            }
            if (type is FunctionPointerTypeExpression)
            {
                return TargetInfo.Default.PointerBytes;
            }
            if (type is ArrayTypeExpression arr)
            {
                int elemSize = GetTypeSize(arr.ElementType);
                if (arr.Size.HasValue)
                {
                    long bytes = (long)elemSize * arr.Size.Value;
                    if (bytes > int.MaxValue) throw new TypeCheckException("Array layout exceeds the supported size", arr.Line);
                    return (int)bytes;
                }
                return TargetInfo.Default.PointerBytes;
            }
            if (type is NamedTypeExpression named)
            {
                EnumInfo? enumInfo = ResolveEnum(named);
                if (enumInfo != null)
                {
                    return GetTypeSize(enumInfo.UnderlyingType);
                }
                if (IsInterface(named))
                {
                    return 16;
                }
                StructInfo? structInfo = GetStructByIdentity(named.Name);
                if (structInfo != null)
                {
                    return GetStructSize(structInfo);
                }
                ClassInfo? classInfo = GetClassByIdentity(named.Name);
                if (classInfo != null)
                {
                    return GetClassSize(classInfo);
                }
                return named.Name switch
                {
                    "bool" or "byte" or "sbyte" or "char" => 1,
                    "short" or "ushort" => 2,
                    "int" or "uint" or "float" => 4,
                    "long" or "ulong" or "double" => 8,
                    "nint" or "nuint" => TargetInfo.Default.PointerBytes,
                    "extralong" => 16,
                    "void" => 0,
                    _ => throw new TypeCheckException($"Cannot determine size of unknown type '{named.Name}'", named.Line)
                };
            }
            throw new TypeCheckException($"Cannot determine size of type '{TypeName(type)}'", type.Line);
        }

        public string ExtractName(AstNode target)
        {
            if (target is NamedTypeExpression named)
            {
                return named.Name;
            }
            if (target is IdentifierExpression ident)
            {
                return ident.Name;
            }
            if (target is MemberAccessExpression member)
            {
                return member.Member;
            }
            if (target is NamespaceAccessExpression nsAccess)
            {
                return nsAccess.Member;
            }
            if (target is PointerTypeExpression ptr)
            {
                return ExtractName(ptr.Inner);
            }
            if (target is ManagedTypeExpression mgd)
            {
                return ExtractName(mgd.Inner);
            }
            if (target is ArrayTypeExpression arr)
            {
                return ExtractName(arr.ElementType);
            }
            throw new TypeCheckException($"Cannot extract name from expression of type '{target.GetType().Name}'", target.Line);
        }

        public bool IsUnsignedInteger(TypeExpression type)
        {
            type = ResolveAlias(type);
            if (type is NamedTypeExpression n)
            {
                if (n.Name is "byte" or "ushort" or "uint" or "ulong" or "nuint")
                    return true;
                EnumInfo? enumInfo = ResolveEnum(n);
                if (enumInfo != null)
                    return IsUnsignedInteger(enumInfo.UnderlyingType);
            }
            return false;
        }

        public bool IsSignedInteger(TypeExpression type)
        {
            type = ResolveAlias(type);
            if (type is NamedTypeExpression n)
            {
                if (n.Name is "sbyte" or "short" or "int" or "long" or "extralong" or "char" or "nint")
                    return true;
                EnumInfo? enumInfo = ResolveEnum(n);
                if (enumInfo != null)
                    return IsSignedInteger(enumInfo.UnderlyingType);
            }
            return false;
        }

        public bool IsInteger(TypeExpression type) => IsSignedInteger(type) || IsUnsignedInteger(type);

        private bool IsValidPointerForArithmetic(TypeExpression type, out string? error)
        {
            type = ResolveAlias(type);
            if (IsNullable(type) && type is not ManagedTypeExpression)
            {
                error = "Cast a nullable pointer to non-null before pointer arithmetic";
                return false;
            }
            if (type is ManagedTypeExpression)
            {
                error = "Pointer arithmetic is not allowed on managed pointers ('^')";
                return false;
            }
            if (type is PointerTypeExpression ptr)
            {
                if (HasIncompleteArrayValueShape(ptr.Inner))
                {
                    error = "Pointer arithmetic requires array element types with fixed sizes";
                    return false;
                }
                if (ptr.Inner is NamedTypeExpression { Name: "void" })
                {
                    error = "Pointer arithmetic is not allowed on 'void*'";
                    return false;
                }
                error = null;
                return true;
            }
            if (type is FunctionPointerTypeExpression)
            {
                error = "Pointer arithmetic is not allowed on function pointers; use a pointer to function-pointer storage for array traversal";
                return false;
            }
            error = null;
            return false;
        }

        public static bool TypesMatchPublic(TypeExpression a, TypeExpression b, SymbolTable? symbols = null)
        {
            if (IsError(a) || IsError(b))
            {
                return true;
            }

            if (symbols != null)
            {
                a = symbols.ResolveAlias(a);
                b = symbols.ResolveAlias(b);
            }

            if (a.IsReadOnlyValue != b.IsReadOnlyValue) return false;
            if (a is NamedTypeExpression na && b is NamedTypeExpression nb)
                return na.Name == nb.Name;
            if (a is ReferenceTypeExpression ra && b is ReferenceTypeExpression rb)
                return TypesMatchPublic(ra.Inner, rb.Inner, symbols);
            if (a is PointerTypeExpression pa && b is PointerTypeExpression pb)
                return pa.IsNullable == pb.IsNullable && pa.IsReadOnly == pb.IsReadOnly && TypesMatchPublic(pa.Inner, pb.Inner, symbols);
            if (a is ManagedTypeExpression ma && b is ManagedTypeExpression mb)
                return ma.IsNullable == mb.IsNullable && ma.IsReadOnly == mb.IsReadOnly &&
                    (ma.Collector == null && mb.Collector == null || ma.Collector != null && mb.Collector != null && TypesMatchPublic(ma.Collector, mb.Collector, symbols)) && TypesMatchPublic(ma.Inner, mb.Inner, symbols);
            if (a is ArrayTypeExpression aa && b is ArrayTypeExpression ab)
                return TypesMatchPublic(aa.ElementType, ab.ElementType, symbols) && (aa.Size == ab.Size || aa.Size == null || ab.Size == null);
            if (a is FunctionPointerTypeExpression fa && b is FunctionPointerTypeExpression fb)
            {
                if (fa.IsSignature != fb.IsSignature || fa.IsManaged != fb.IsManaged || fa.IsNullable != fb.IsNullable)
                    return false;
                if (!TypesMatchPublic(fa.ReturnType, fb.ReturnType, symbols))
                    return false;
                if (fa.ParameterTypes.Count != fb.ParameterTypes.Count)
                    return false;
                for (int i = 0; i < fa.ParameterTypes.Count; i++)
                {
                    if (!TypesMatchPublic(fa.ParameterTypes[i], fb.ParameterTypes[i], symbols))
                        return false;
                }
                return true;
            }

            return false;
        }

        private bool TypesMatch(TypeExpression a, TypeExpression b) => TypesMatchPublic(
            ResolveAlias(a).WithReadOnlyValue(false), ResolveAlias(b).WithReadOnlyValue(false), _symbols);

        private bool IsAssignable(TypeExpression target, TypeExpression source, AstNode? valueNode = null)
        {
            if (ResolveAlias(target) is ReferenceTypeExpression targetRef)
            {
                if (ResolveAlias(source) is not ReferenceTypeExpression sourceRef) return false;
                return targetRef.Inner.IsReadOnlyValue ? CanReadOnlyView(targetRef.Inner, sourceRef.Inner)
                    : TypesMatchPublic(targetRef.Inner, sourceRef.Inner, _symbols);
            }
            if (ResolveAlias(source) is ReferenceTypeExpression) return false;
            if (valueNode != null && !ValidateCopyUse(valueNode, target)) return false;
            if (ResolveAlias(source) is NamedTypeExpression && ResolveAlias(target) is NamedTypeExpression &&
                (RequiresCopy(source) || RequiresCopy(target)) && !TypesMatch(target, source)) return false;
            if (IsError(target) || IsError(source))
            {
                return true;
            }

            target = ResolveAlias(target);
            if (valueNode != null && target is FunctionPointerTypeExpression { IsSignature: false } expected &&
                _functionAddressTargets.ContainsKey(valueNode))
            {
                if (!SelectFunctionValue(valueNode, expected)) return false;
                source = GetType(valueNode);
            }
            source = ResolveAlias(source);
            if (target is ManagedTypeExpression targetCollector && source is ManagedTypeExpression sourceCollector &&
                !TypesMatch(ResolveCollectorType(targetCollector.Collector, targetCollector.Line), ResolveCollectorType(sourceCollector.Collector, sourceCollector.Line))) return false;

            if (source.IsReadOnlyValue && !target.IsReadOnlyValue && !CanCopyReadOnlyValue(source))
                return false;
            target = target.WithReadOnlyValue(false);
            source = source.WithReadOnlyValue(false);
            if (target is ArrayTypeExpression literalArray &&
                valueNode is LiteralExpression { Token.Kind: TokenKind.StringLiteral } literal)
                return TypesMatch(ResolveAlias(literalArray.ElementType), Char) &&
                    (literalArray.Size != null && literalArray.Size >= GetStringLiteralLength(literal.Token.Text[1..^1], literal.Line));
            if (TypesMatch(target, source))
                return true;

            if (target is ArrayTypeExpression destinationArray && valueNode is ArrayLiteralExpression arrayLiteral &&
                ResolveAlias(destinationArray.ElementType) is ArrayTypeExpression &&
                arrayLiteral.Elements.Any(element => element is LiteralExpression { Token.Kind: TokenKind.StringLiteral } or ArrayLiteralExpression))
                return (destinationArray.Size == null || destinationArray.Size == arrayLiteral.Elements.Count) &&
                    arrayLiteral.Elements.All(element => IsAssignable(destinationArray.ElementType, GetType(element), element));

            if (target is ArrayTypeExpression ta && source is ArrayTypeExpression sa)
                return (ta.Size == null || sa.Size == null || ta.Size == sa.Size ||
                    valueNode is LiteralExpression { Token.Kind: TokenKind.StringLiteral } && ta.Size >= sa.Size) &&
                    CanReadOnlyView(ta.ElementType, sa.ElementType) && IsAssignable(ta.ElementType, sa.ElementType);

            // Pointer to pointer assignability (handles readonly conversion: T* to readonly(T)*)
            if (target is PointerTypeExpression ptExact && source is PointerTypeExpression psExact)
            {
                if (psExact.IsReadOnly && !ptExact.IsReadOnly)
                    return false;
                if (psExact.IsNullable && !ptExact.IsNullable)
                    return false;
                if (ptExact.IsReadOnly ? CanReadOnlyView(ptExact.Inner, psExact.Inner) : TypesMatchPublic(ptExact.Inner, psExact.Inner, _symbols))
                    return true;
            }

            // Managed ref to managed ref assignability (handles readonly conversion: T^ to readonly(T)^)
            if (target is ManagedTypeExpression mtExact && source is ManagedTypeExpression msExact)
            {
                if (msExact.IsReadOnly && !mtExact.IsReadOnly)
                    return false;
                if (msExact.IsNullable && !mtExact.IsNullable)
                    return false;
                if (mtExact.IsReadOnly ? CanReadOnlyView(mtExact.Inner, msExact.Inner) : TypesMatchPublic(mtExact.Inner, msExact.Inner, _symbols))
                    return true;
            }

            // Enum identities survive assignments and calls. Default remains contextual.
            if (source is not NamedTypeExpression { Name: "default" } &&
                (target is NamedTypeExpression enumTarget && ResolveEnum(enumTarget) != null ||
                 source is NamedTypeExpression enumSource && ResolveEnum(enumSource) != null))
                return false;

            // Integer constant literal in-range assignment
            if (valueNode != null && IsInteger(source) && target is NamedTypeExpression targetNamed && TryGetIntegerLiteralValue(valueNode, out var constVal))
            {
                if (FitsIntegerLiteral(targetNamed.Name, constVal))
                    return true;
            }

            // default is assignable to any non-void type (except non-nullable pointers/references)
            if (source is NamedTypeExpression { Name: "default" })
            {
                if (target is not NamedTypeExpression { Name: "void" })
                {
                    TypeExpression resolvedTarget = ResolveAlias(target);
                    if (!CanDefaultInitialize(resolvedTarget))
                        return false;

                    if (valueNode is DefaultExpression def && def.TargetType == null)
                        RecordType(def, target);
                    return true;
                }
                return false;
            }

            // null is assignable to any nullable type
            if (source is NamedTypeExpression { Name: "null" } && IsNullable(target))
                return true;

            // Struct pointer to interface pointer assignability
            if (target is PointerTypeExpression ptIface && source is PointerTypeExpression psStruct)
            {
                if (psStruct.IsReadOnly && !ptIface.IsReadOnly)
                    return false;
                TypeExpression targetInner = ResolveAlias(ptIface.Inner);
                TypeExpression sourceInner = ResolveAlias(psStruct.Inner);
                if (targetInner is NamedTypeExpression targetIfaceNamed && ResolveInterface(targetIfaceNamed) is InterfaceInfo targetIface)
                {
                    if (sourceInner is NamedTypeExpression sourceNamed && _structs.TryGetValue(sourceNamed.Name, out StructInfo? sInfo))
                    {
                        if (sInfo.Interfaces.Contains(targetIface.Name))
                        {
                            if (psStruct.IsNullable && !ptIface.IsNullable)
                                return false;
                            return true;
                        }
                    }
                    if (sourceInner is NamedTypeExpression sourceClassNamed && _classes.TryGetValue(sourceClassNamed.Name, out ClassInfo? cInfo))
                    {
                        if (ClassImplementsInterface(cInfo, targetIface.Name))
                        {
                            if (psStruct.IsNullable && !ptIface.IsNullable)
                                return false;
                            return true;
                        }
                    }
                }
            }

            // Struct or class managed ref to interface managed ref assignability
            if (target is ManagedTypeExpression mtIface && source is ManagedTypeExpression msStruct)
            {
                if (msStruct.IsReadOnly && !mtIface.IsReadOnly)
                    return false;
                TypeExpression targetInner = ResolveAlias(mtIface.Inner);
                TypeExpression sourceInner = ResolveAlias(msStruct.Inner);
                if (targetInner is NamedTypeExpression targetIfaceNamed && ResolveInterface(targetIfaceNamed) is InterfaceInfo targetIface)
                {
                    if (sourceInner is NamedTypeExpression sourceNamed && _structs.TryGetValue(sourceNamed.Name, out StructInfo? sInfo))
                    {
                        if (sInfo.Interfaces.Contains(targetIface.Name))
                        {
                            if (msStruct.IsNullable && !mtIface.IsNullable)
                                return false;
                            return true;
                        }
                    }
                    if (sourceInner is NamedTypeExpression sourceClassNamed && _classes.TryGetValue(sourceClassNamed.Name, out ClassInfo? cInfo))
                    {
                        if (ClassImplementsInterface(cInfo, targetIface.Name))
                        {
                            if (msStruct.IsNullable && !mtIface.IsNullable)
                                return false;
                            return true;
                        }
                    }
                }
            }

            // Class pointer inheritance assignability (Derived* to Base*)
            if (target is PointerTypeExpression ptBase && source is PointerTypeExpression psDerived)
            {
                if (psDerived.IsReadOnly && !ptBase.IsReadOnly)
                    return false;
                TypeExpression targetInner = ResolveAlias(ptBase.Inner);
                TypeExpression sourceInner = ResolveAlias(psDerived.Inner);
                if (targetInner is NamedTypeExpression tNamed && sourceInner is NamedTypeExpression sNamed)
                {
                    if (IsSubclassOf(sNamed.Name, tNamed.Name))
                    {
                        if (psDerived.IsNullable && !ptBase.IsNullable)
                            return false;
                        return true;
                    }
                }
            }

            // Class managed ref inheritance assignability (Derived^ to Base^)
            if (target is ManagedTypeExpression mtBase && source is ManagedTypeExpression msDerived)
            {
                if (msDerived.IsReadOnly && !mtBase.IsReadOnly)
                    return false;
                TypeExpression targetInner = ResolveAlias(mtBase.Inner);
                TypeExpression sourceInner = ResolveAlias(msDerived.Inner);
                if (targetInner is NamedTypeExpression tNamed && sourceInner is NamedTypeExpression sNamed)
                {
                    if (IsSubclassOf(sNamed.Name, tNamed.Name))
                    {
                        if (msDerived.IsNullable && !mtBase.IsNullable)
                            return false;
                        return true;
                    }
                }
            }

            // void* is implicitly convertible to/from any pointer type (except interface fat pointer)
            if (target is PointerTypeExpression pt && source is PointerTypeExpression ps)
            {
                if (ps.IsReadOnly && !pt.IsReadOnly)
                    return false;
                bool targetIsIface = ResolveAlias(pt.Inner) is NamedTypeExpression tNamed && IsInterface(tNamed);
                bool sourceIsIface = ResolveAlias(ps.Inner) is NamedTypeExpression sNamed && IsInterface(sNamed);
                if (!targetIsIface && !sourceIsIface)
                {
                    if (pt.Inner is NamedTypeExpression { Name: "void" } || ps.Inner is NamedTypeExpression { Name: "void" })
                        return true;
                }
            }

            // Function pointer assignability
            if (target is FunctionPointerTypeExpression ft && source is FunctionPointerTypeExpression fs)
            {
                if (ft.IsManaged != fs.IsManaged)
                    return false;
                if (fs.IsNullable && !ft.IsNullable)
                    return false;
                if (!TypesMatchPublic(ft.ReturnType, fs.ReturnType, _symbols))
                    return false;
                if (ft.ParameterTypes.Count != fs.ParameterTypes.Count)
                    return false;
                for (int i = 0; i < ft.ParameterTypes.Count; i++)
                {
                    if (!TypesMatchPublic(ft.ParameterTypes[i], fs.ParameterTypes[i], _symbols))
                        return false;
                }
                return true;
            }

            // Nullability must survive conversions between raw and function pointers.
            if (IsNullable(source) && !IsNullable(target) &&
                target is PointerTypeExpression or FunctionPointerTypeExpression)
                return false;

            // void* is implicitly convertible to/from unmanaged function pointers
            if (target is PointerTypeExpression { Inner: NamedTypeExpression { Name: "void" } } && source is FunctionPointerTypeExpression { IsManaged: false })
                return true;
            if (source is PointerTypeExpression { IsReadOnly: false, Inner: NamedTypeExpression { Name: "void" } } && target is FunctionPointerTypeExpression { IsManaged: false })
                return true;

            // Array-to-pointer decay: T[N] or T[] can be assigned to T*
            if (target is PointerTypeExpression ptrTarget && source is ArrayTypeExpression arrSource)
            {
                if (valueNode is LiteralExpression { Token.Kind: TokenKind.StringLiteral } && !ptrTarget.IsReadOnly)
                    return false;

                if (ptrTarget.IsReadOnly ? CanReadOnlyView(ptrTarget.Inner, arrSource.ElementType) :
                    TypesMatchPublic(ptrTarget.Inner, arrSource.ElementType, _symbols))
                    return true;
            }

            // Implicit numeric conversions
            if (source is NamedTypeExpression s && target is NamedTypeExpression t)
            {
                if (s.Name == "byte" && t.Name is "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "nint" or "nuint" or "extralong")
                    return true;
                if (s.Name == "sbyte" && t.Name is "short" or "int" or "long" or "nint" or "extralong")
                    return true;
                if (s.Name == "short" && t.Name is "int" or "long" or "nint" or "extralong")
                    return true;
                if (s.Name == "ushort" && t.Name is "int" or "uint" or "long" or "ulong" or "nint" or "nuint" or "extralong")
                    return true;
                if (s.Name == "uint" && t.Name is "long" or "ulong" or "nint" or "nuint" or "extralong")
                    return true;
                if (s.Name == "int" && t.Name is "long" or "nint" or "extralong")
                    return true;
                if (s.Name == "char" && t.Name is "int" or "uint" or "long" or "ulong" or "nint" or "nuint" or "extralong")
                    return true;
                if (s.Name == "ulong" && t.Name is "nuint" or "extralong")
                    return true;
                if (s.Name == "nint" && t.Name is "long" or "extralong")
                    return true;
                if (s.Name == "nuint" && t.Name is "ulong" or "extralong")
                    return true;
                if (s.Name == "float" && t.Name == "double")
                    return true;
            }

            return false;
        }

        public static void Check(CompilationUnit root)
        {
            TypeChecker checker = new TypeChecker();
            root.Accept(checker);
        }

        public void Visit(CompilationUnit node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            _compilationUnit = node;
            BindQualifiedDeclarations(node);
            SelectAliasBindings(node);
            ExpandProperties(node);

            CheckDeclarationConflicts(node);
            QualifyTypeDeclarations(node);

            // Pass 1: Symbol collection
            SymbolCollectionPass collectionPass = new SymbolCollectionPass(_symbols, IsNamespaceVisibleDeclaration);
            collectionPass.Execute(node);
            ResolveFunctionReplacements(node);
            collectionPass.Execute(node);

            ResolveDeclaredBaseTypes(node);
            // Signature resolution can specialize generic bodies. Their exception
            // and constraint checks need the class inheritance edges already bound.
            new HierarchyResolutionPass(_symbols).ResolveBaseTypes();
            ResolveDeclarationSignatures(node);
            PrepareOverloads(node);
            ValidateAllocatorPair();
            ValidateUnhandledExceptionHook();
            RegisterStaticStorage(node);

            // Pass 2: Hierarchy resolution
            HierarchyResolutionPass hierarchyPass = new HierarchyResolutionPass(_symbols);
            hierarchyPass.Execute();
            ValidateCollectedInlineLayouts();
            ValidateAliasContracts();

            // Resolve literal operators before checking uses, including forward references.
            RegisterStringLiteralOperators(node);
            RegisterExtensions(node);

            // Pass 3: Body type checking
            _currentNamespace = _globalScope;
            for (int i = 0; i < node.Members.Count; i++)
            {
                CheckSpecialization(node.Members[i], node.Members[i]);
            }

            foreach (NamespaceDeclaration ns in node.Namespaces)
            {
                ns.Accept(this);
            }

            // Attribute constructors run after all declaration bodies have been checked.
            foreach (NamespaceDeclaration ns in node.Namespaces) CheckDeclarationAttributes(ns);
            for (int i = 0; i < node.Members.Count; i++) CheckDeclarationAttributes(node.Members[i]);
            // Attribute evaluation has its own safety checks. Preserve those
            // diagnostics before reporting unavailable runtime storage forms.
            ValidateStaticStorage();
            AnalyzeSafely(ValidateFunctionValueSelections);

            if (_recoverErrors && _diagnostics.HasErrors) return;

            // Pass 4: Control flow & Return path analysis
            ControlFlowPass controlFlowPass = new ControlFlowPass(this, _constEvaluator, _diagnostics);
            controlFlowPass.Execute(node);

            // Pass 5: Definite assignment analysis
            DefiniteAssignmentPass definiteAssignmentPass = new DefiniteAssignmentPass(this, controlFlowPass);
            definiteAssignmentPass.Execute(node);

            if (_diagnostics.HasErrors)
            {
                Diagnostic firstError = _diagnostics.Items.First(d => d.Severity == DiagnosticSeverity.Error);
                throw new TypeCheckException(firstError.Message, firstError.Span);
            }
        }

        private void ResolveClassHierarchies()
        {
            new HierarchyResolutionPass(_symbols).Execute();
        }

        private void RegisterMemberInScope(AstNode member, NamespaceScope scope, string nsPath, bool namespaceVisible = true)
        {
            if (!namespaceVisible) namespaceHiddenDeclarations.Add(member);
            if (member is MethodDeclaration method)
            {
                _functionNamespaces[method] = nsPath;
                if (method.IsGeneric)
                {
                    scope.GenericFunctions[method.Name] = method;
                    _genericMethods[nsPath.Length > 0 ? nsPath + "$" + method.Name : method.Name] = method;
                    return;
                }
                scope.Functions[method.Name] = method;
                _functionNamespaces[method] = nsPath;
            }
            else if (member is ExternDeclaration ext)
            {
                scope.Externs[ext.Name] = ext;
            }
            else if (member is InterfaceDeclaration iface)
            {
                if (namespaceVisible) scope.TypeNames[iface.SourceName] = iface.Name;
                InterfaceInfo info = new InterfaceInfo
                {
                    Name = iface.Name,
                    Declaration = iface,
                    IsDeclaredStatic = iface.IsStatic,
                    Namespace = nsPath,
                    Accessibility = iface.Accessibility
                };
                int slotIndex = 0;
                foreach (AstNode m in iface.Members)
                {
                    if (m is MethodDeclaration sm)
                    {
                        if (info.MethodsByName.ContainsKey(sm.Name))
                            throw new TypeCheckException($"Interface '{iface.Name}' already contains a method named '{sm.Name}'", sm.Line);
                        info.Methods.Add(sm);
                        info.MethodsByName[sm.Name] = sm;
                        info.MethodIndices[sm.Name] = slotIndex++;
                    }
                    else
                    {
                        throw new TypeCheckException($"Interface '{iface.Name}' can only contain method declarations", m.Line);
                    }
                }
                _interfaces[iface.Name] = info;
                if (namespaceVisible) scope.Interfaces[iface.SourceName] = info;
            }
            else if (member is StructDeclaration str)
            {
                if (namespaceVisible) scope.TypeNames[str.SourceName] = str.Name;
                if (str.IsGeneric)
                {
                    _genericStructs[str.Name] = str;
                    return;
                }
                StructInfo info = new StructInfo
                {
                    Name = str.Name,
                    Declaration = str,
                    Namespace = nsPath,
                    Interfaces = new List<string>(str.Interfaces)
                };
                for (int i = 0; i < str.Members.Count; i++)
                {
                    AstNode m = str.Members[i];
                    if (m is ClassDeclaration nestedCls)
                    {
                        ClassDeclaration qualifiedCls = new ClassDeclaration(
                            nestedCls.Name.StartsWith(str.Name + ".") ? nestedCls.Name : $"{str.Name}.{nestedCls.Name}",
                            nestedCls.BaseClass,
                            nestedCls.Interfaces,
                            nestedCls.Members,
                            nestedCls.Accessibility,
                            nestedCls.IsAbstract,
                            nestedCls.Line,
                            nestedCls.Attributes,
                            nestedCls.GenericParameters
                        );
                        qualifiedCls.IsStatic = nestedCls.IsStatic;
                        qualifiedCls.Span = nestedCls.Span;
                        str.Members[i] = qualifiedCls;
                        RegisterMemberInScope(qualifiedCls, scope, nsPath, namespaceVisible: false);
                    }
                    else if (m is StructDeclaration nestedStruct)
                    {
                        StructDeclaration qualifiedStruct = new StructDeclaration(
                            nestedStruct.Name.StartsWith(str.Name + ".") ? nestedStruct.Name : $"{str.Name}.{nestedStruct.Name}",
                            nestedStruct.Interfaces,
                            nestedStruct.Members,
                            nestedStruct.Accessibility,
                            nestedStruct.Line,
                            nestedStruct.Attributes,
                            nestedStruct.GenericParameters
                        );
                        qualifiedStruct.Span = nestedStruct.Span;
                        str.Members[i] = qualifiedStruct;
                        RegisterMemberInScope(qualifiedStruct, scope, nsPath, namespaceVisible: false);
                    }
                    else if (m is FieldDeclaration field)
                    {
                        if (!field.IsStatic) info.Fields.Add((field.Name, field.Type));
                        info.FieldDeclarations.Add(field);
                        info.FieldDeclarationsByName[field.Name] = field;
                    }
                    else if (m is MethodDeclaration sm)
                    {
                        info.Methods[sm.Name] = sm;
                        info.AllMethods.Add(sm);
                        _functionNamespaces[sm] = nsPath;
                    }
                    else if (m is ConstructorDeclaration ctor)
                    {
                        info.Constructors.Add(ctor);
                    }
                    else if (m is ExternDeclaration external)
                        throw new TypeCheckException("Extern functions must be declared at namespace scope", external.Span);
                    else if (m is DestructorDeclaration dtor)
                    {
                        if (info.Destructor != null)
                            throw new TypeCheckException($"Struct '{str.Name}' already defines a destructor", dtor.Line);
                        if (dtor.IsVirtual)
                            throw new TypeCheckException($"Struct '{str.Name}' destructor cannot be virtual", dtor.Line);
                        string shortStrName = str.Name.Split('.').Last().Split('$').Last();
                        if (dtor.Name != str.Name && dtor.Name != shortStrName)
                            throw new TypeCheckException($"Destructor name '~{dtor.Name}' does not match struct name '{str.Name}'", dtor.Line);
                        info.Destructor = dtor;
                    }
                    else if (m is OperatorDeclaration op)
                    {
                        info.Operators.Add(op);
                        _operatorNamespaces[op] = nsPath;
                    }
                    else if (m is EnumDeclaration enumDecl)
                    {
                        RegisterEnum(enumDecl, scope, nsPath.Length > 0 ? $"{nsPath}::{str.Name}" : str.Name, namespaceVisible: false);
                    }
                }
                _structs[str.Name] = info;
                if (namespaceVisible) scope.Structs[str.SourceName] = info;
            }
            else if (member is ClassDeclaration cls)
            {
                if (namespaceVisible) scope.TypeNames[cls.SourceName] = cls.Name;
                if (cls.IsGeneric)
                {
                    _genericClasses[cls.Name] = cls;
                    return;
                }
                ClassInfo info = new ClassInfo
                {
                    Name = cls.Name,
                    Declaration = cls,
                    Namespace = nsPath,
                    BaseClass = cls.BaseClass,
                    IsAbstract = cls.IsAbstract,
                    IsStatic = cls.IsStatic,
                    Accessibility = cls.Accessibility,
                    Interfaces = new List<string>(cls.Interfaces),
                    Line = cls.Line,
                    Span = cls.Span
                };

                for (int i = 0; i < cls.Members.Count; i++)
                {
                    AstNode m = cls.Members[i];
                    if (m is ClassDeclaration nestedCls)
                    {
                        ClassDeclaration qualifiedCls = new ClassDeclaration(
                            nestedCls.Name.StartsWith(cls.Name + ".") ? nestedCls.Name : $"{cls.Name}.{nestedCls.Name}",
                            nestedCls.BaseClass,
                            nestedCls.Interfaces,
                            nestedCls.Members,
                            nestedCls.Accessibility,
                            nestedCls.IsAbstract,
                            nestedCls.Line,
                            nestedCls.Attributes,
                            nestedCls.GenericParameters
                        );
                        qualifiedCls.IsStatic = nestedCls.IsStatic;
                        qualifiedCls.Span = nestedCls.Span;
                        cls.Members[i] = qualifiedCls;
                        RegisterMemberInScope(qualifiedCls, scope, nsPath, namespaceVisible: false);
                    }
                    else if (m is StructDeclaration nestedStruct)
                    {
                        StructDeclaration qualifiedStruct = new StructDeclaration(
                            nestedStruct.Name.StartsWith(cls.Name + ".") ? nestedStruct.Name : $"{cls.Name}.{nestedStruct.Name}",
                            nestedStruct.Interfaces,
                            nestedStruct.Members,
                            nestedStruct.Accessibility,
                            nestedStruct.Line,
                            nestedStruct.Attributes,
                            nestedStruct.GenericParameters
                        );
                        qualifiedStruct.Span = nestedStruct.Span;
                        cls.Members[i] = qualifiedStruct;
                        RegisterMemberInScope(qualifiedStruct, scope, nsPath, namespaceVisible: false);
                    }
                    else if (m is FieldDeclaration field)
                    {
                        info.FieldDeclarations.Add(field);
                    }
                    else if (m is ConstructorDeclaration ctor)
                    {
                        info.Constructors.Add(ctor);
                    }
                    else if (m is ExternDeclaration external)
                        throw new TypeCheckException("Extern functions must be declared at namespace scope", external.Span);
                    else if (m is DestructorDeclaration dtor)
                    {
                        if (info.Destructor != null)
                            throw new TypeCheckException($"Class '{cls.Name}' already defines a destructor", dtor.Line);
                        string shortClsName = cls.Name.Contains('.') ? cls.Name.Substring(cls.Name.LastIndexOf('.') + 1) : cls.Name;
                        if (dtor.Name != cls.Name && dtor.Name != shortClsName)
                            throw new TypeCheckException($"Destructor name '~{dtor.Name}' does not match class name '{cls.Name}'", dtor.Line);
                        info.Destructor = dtor;
                    }
                    else if (m is MethodDeclaration cm)
                    {
                        if (cm.IsAbstract && !cls.IsAbstract)
                            throw new TypeCheckException($"Abstract method '{cm.Name}' can only be declared in an abstract class", cm.Line);
                        if (cm.IsAbstract && cm.Body != null)
                            throw new TypeCheckException($"Abstract method '{cm.Name}' cannot have a body", cm.Line);
                        if (!cm.IsAbstract && cm.Body == null && cm.ImplementationKind != FunctionImplementationKind.Expect)
                            throw new TypeCheckException($"Method '{cm.Name}' must declare a body unless marked abstract", cm.Line);

                        info.Methods[cm.Name] = (cm, cls.Name);
                        info.AllMethods.Add(cm);
                        _functionNamespaces[cm] = nsPath;
                    }
                }

                _classes[cls.Name] = info;
                if (namespaceVisible) scope.Classes[cls.SourceName] = info;
            }
            else if (member is NamespaceDeclaration nested)
            {
                BuildNamespaceScope(nested, scope, nsPath);
            }
            else if (member is AliasDeclaration alias)
            {
                scope.Aliases[alias.Name] = alias.TargetType;
            }
            else if (member is EnumDeclaration enumDecl)
            {
                RegisterEnum(enumDecl, scope, nsPath, namespaceVisible);
            }
            else if (member is FieldDeclaration field)
            {
                scope.Fields[field.Name] = field;
            }
        }

        private void BuildNamespaceScope(NamespaceDeclaration ns, NamespaceScope parent, string parentPath)
        {
            string nsPath = parentPath.Length > 0 ? $"{parentPath}${ns.Name}" : ns.Name;
            if (!parent.Children.TryGetValue(ns.Name, out NamespaceScope? scope))
            {
                scope = new NamespaceScope { Parent = parent };
                parent.Children[ns.Name] = scope;
            }
            _namespaceScopes[ns] = scope;

            foreach (AstNode member in ns.Members)
                RegisterMemberInScope(member, scope, nsPath);
        }

        public void Visit(UsingDirective node) {
            using var sourceContext = SourceContext.Enter(node.Span); }

        public void Visit(NamespaceDeclaration node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            NamespaceScope previous = _currentNamespace;
            _currentNamespace = _namespaceScopes[node];
            string prevPath = _currentNamespacePath;
            _currentNamespacePath = _currentNamespacePath.Length > 0 ? $"{_currentNamespacePath}::{node.Name}" : node.Name;

            foreach (AstNode member in node.Members)
                CheckSpecialization(member, member);

            _currentNamespace = previous;
            _currentNamespacePath = prevPath;
        }

        public void Visit(ClassDeclaration node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            ValidateStaticClass(node);
            if (node.IsGeneric) return;
            ValidateInlineLayout(node.Name);

            ClassInfo? prevClass = _currentClass;
            StructInfo? prevStruct = _currentStruct;
            RegisterStaticTypeStorage(node.Members, node.Name);
            _currentClass = _classes[node.Name];
            _currentStruct = null;

            try
            {
                // Validate implemented interfaces
                foreach (string ifaceName in _currentClass.Interfaces)
                {
                    InterfaceInfo? ifaceInfo = ResolveInterface(NamedTypeExpression.Resolved(ifaceName, node.Line));
                    if (ifaceInfo == null)
                    {
                        throw new TypeCheckException($"Class '{DisplayName(node.Name)}' implements unknown interface '{DisplayName(ifaceName)}'", node.Span);
                    }

                    foreach (MethodDeclaration ifaceMethod in ifaceInfo.Methods)
                    {
                        if (!_currentClass.Methods.TryGetValue(ifaceMethod.Name, out (MethodDeclaration Method, string DeclaringClass) mEntry))
                        {
                            throw new TypeCheckException($"Class '{DisplayName(node.Name)}' does not implement interface method '{DisplayName(ifaceName)}.{ifaceMethod.Name}'", node.Span);
                        }

                        MethodDeclaration classMethod = InterfaceImplementation(mEntry.Method, ifaceMethod);
                        ValidateStaticContractImplementation(ifaceMethod, classMethod);
                        if (classMethod.PropertyName != null && classMethod.Accessibility != TokenKind.Public)
                            throw new TypeCheckException($"Property accessor '{classMethod.PropertyName}' must be public to implement an interface", classMethod.Span);
                        if (ifaceMethod.IsNoThrow != classMethod.IsNoThrow)
                        {
                            throw new TypeCheckException(
                                $"Method '{classMethod.Name}' in class '{DisplayName(node.Name)}' must match nothrow specification of interface method '{DisplayName(ifaceName)}.{ifaceMethod.Name}'",
                                classMethod.Span);
                        }
                        if (ifaceMethod.IsReadOnly && !classMethod.IsReadOnly)
                        {
                            throw new TypeCheckException(
                                $"Method '{classMethod.Name}' in class '{DisplayName(node.Name)}' must be marked readonly to implement interface method '{DisplayName(ifaceName)}.{ifaceMethod.Name}'",
                                classMethod.Span);
                        }
                        TypeExpression classReturnType = ResolveAlias(classMethod.ReturnType);
                        TypeExpression ifaceReturnType = ResolveAlias(ifaceMethod.ReturnType);
                        if (!TypesMatchPublic(classReturnType, ifaceReturnType))
                        {
                            throw new TypeCheckException(
                                $"Method '{classMethod.Name}' in class '{DisplayName(node.Name)}' has return type '{DisplayTypeName(classReturnType)}', but interface '{DisplayName(ifaceName)}' requires '{DisplayTypeName(ifaceReturnType)}'",
                                classMethod.Span);
                        }

                        if (classMethod.Parameters.Count != ifaceMethod.Parameters.Count)
                        {
                            throw new TypeCheckException(
                                $"Method '{classMethod.Name}' in class '{DisplayName(node.Name)}' has {classMethod.Parameters.Count} parameters, but interface '{DisplayName(ifaceName)}' expects {ifaceMethod.Parameters.Count}",
                                classMethod.Span);
                        }

                        for (int i = 0; i < ifaceMethod.Parameters.Count; i++)
                        {
                            TypeExpression classParamType = ResolveAlias(classMethod.Parameters[i].Type);
                            TypeExpression ifaceParamType = ResolveAlias(ifaceMethod.Parameters[i].Type);
                            if (!TypesMatchPublic(classParamType, ifaceParamType) || classMethod.Parameters[i].IsConst != ifaceMethod.Parameters[i].IsConst)
                            {
                                throw new TypeCheckException(
                                    $"Parameter '{classMethod.Parameters[i].Name}' of method '{classMethod.Name}' in class '{DisplayName(node.Name)}' has type '{DisplayTypeName(classParamType)}', but interface '{DisplayName(ifaceName)}' expects '{DisplayTypeName(ifaceParamType)}'",
                                    classMethod.Parameters[i].Span);
                            }
                        }
                    }
                }

                foreach (AstNode member in node.Members)
                {
                    if (member is FieldDeclaration field)
                    {
                        if (field.IsStatic) { CheckStaticField(field); continue; }
                        field.Type = ResolveAlias(field.Type);
                        int fIdx = _currentClass.FieldIndex(field.Name);
                        if (fIdx >= 0)
                        {
                            _currentClass.Fields[fIdx] = (field.Name, field.Type, field.Accessibility, _currentClass.Name);
                        }
                        ValidateValueType(field.Type, field.Line);
                        TypeExpression fieldType = field.Type;
                        if (field.IsConst && fieldType is PointerTypeExpression cPtr && !cPtr.IsReadOnly)
                        {
                            fieldType = new PointerTypeExpression(cPtr.Inner, cPtr.IsNullable, cPtr.Line, isReadOnly: true);
                        }
                        if (field.Initializer != null)
                        {
                            field.Initializer.Accept(this);
                            TypeExpression initType = ResolveAlias(GetType(field.Initializer));
                            fieldType = InferArrayStorageType(fieldType, field.Initializer, initType);
                            if (fieldType is ArrayTypeExpression)
                            {
                                field.Type = fieldType;
                                UpdateInferredClassField(_currentClass.Name, field, fieldType);
                            }
                            if (!IsAssignable(fieldType, initType, field.Initializer))
                            {
                                throw new TypeCheckException($"Cannot assign expression of type '{DisplayTypeName(initType)}' to field '{field.Name}' of type '{DisplayTypeName(fieldType)}'", field.Initializer.Span);
                            }
                        }
                        ValidateArrayValueShape(fieldType, field.Line);
                        if (field.IsConst)
                        {
                            if (field.Initializer == null)
                            {
                                throw new TypeCheckException($"Const variable '{field.Name}' must have an initializer", field.Line);
                            }
                            if (!_constEvaluator.TryEvaluate(field.Initializer, fieldType, out ConstValue? constVal, out string? err))
                            {
                                throw new TypeCheckException($"Const variable '{field.Name}' initializer must be a compile-time constant: {err}", field.Line);
                            }
                            _constVariablesByName[$"{node.Name}::{field.Name}"] = constVal!;
                            _constVariablesByName[field.Name] = constVal!;
                            _constValues[field] = constVal!;
                            _constValues[field.Initializer] = constVal!;
                            _constVariableNames.Add(field.Name);
                        }
                    }
                    else if (member is ConstructorDeclaration ctor)
                    {
                        ctor.Accept(this);
                    }
                    else if (member is DestructorDeclaration dtor)
                    {
                        dtor.Accept(this);
                    }
                    else if (member is MethodDeclaration method)
                    {
                        ValidatePropertyAccessor(method);
                        if (method.IsStatic)
                        {
                            Visit(method); // Static methods have no implicit this parameter.
                            continue;
                        }
                        method.ReturnType = ResolveAlias(method.ReturnType);
                        foreach (Parameter p in method.Parameters)
                        {
                            p.Type = ResolveAlias(p.Type);
                        }
                        AstNode? prevFunc = _currentFunction;
                        _currentFunction = method;
                        using var exceptionScope = IsolateExceptionHandlers();
                        using var nullableScope = IsolateNullableFlow();
                        if (method.Body != null) { _exceptionMethods.Add(method); _exceptionSummaries = null; }
                        try
                        {
                            ValidateReturnType(method.ReturnType, method.Line);
                            if (!method.IsAbstract)
                            {
                                PushScope();
                                NamedTypeExpression classType = NamedTypeExpression.Resolved(node.Name, method.Line);
                                PointerTypeExpression thisType = new PointerTypeExpression(classType, false, method.Line, isReadOnly: method.IsReadOnly);
                                DeclareVariable("this", thisType, method.Line);

                                foreach (Parameter p in method.Parameters)
                                {
                                    ValidateParameterType(p.Type, p.Line);
                                    DeclareVariable(p.Name, ParameterValueType(p.Type), p.Line);
                                    NullableParameter(p);
                                }

                                method.Body?.Accept(this);
                                PopScope();
                            }
                        }
                        finally
                        {
                            _currentFunction = prevFunc;
                        }
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
            }
            finally
            {
                _currentClass = prevClass;
                _currentStruct = prevStruct;
            }
        }

        public void Visit(StructDeclaration node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (node.IsGeneric) return;
            ValidateInlineLayout(node.Name);

            StructInfo? previousStruct = _currentStruct;
            ClassInfo? prevClass = _currentClass;
            RegisterStaticTypeStorage(node.Members, node.Name);
            _currentStruct = _structs[node.Name];
            _currentClass = null;

            try
            {
                // Validate implemented interfaces
                foreach (string ifaceName in _currentStruct.Interfaces)
                {
                    InterfaceInfo? ifaceInfo = ResolveInterface(NamedTypeExpression.Resolved(ifaceName, node.Line));
                    if (ifaceInfo == null)
                    {
                        throw new TypeCheckException($"Struct '{DisplayName(node.Name)}' implements unknown interface '{DisplayName(ifaceName)}'", node.Span);
                    }

                    foreach (MethodDeclaration ifaceMethod in ifaceInfo.Methods)
                    {
                        if (!_currentStruct.Methods.TryGetValue(ifaceMethod.Name, out MethodDeclaration? structMethod))
                        {
                            throw new TypeCheckException($"Struct '{DisplayName(node.Name)}' does not implement interface method '{DisplayName(ifaceName)}.{ifaceMethod.Name}'", node.Span);
                        }

                        structMethod = InterfaceImplementation(structMethod, ifaceMethod);
                        ValidateStaticContractImplementation(ifaceMethod, structMethod);
                        if (structMethod.PropertyName != null && structMethod.Accessibility != TokenKind.Public)
                            throw new TypeCheckException($"Property accessor '{structMethod.PropertyName}' must be public to implement an interface", structMethod.Span);
                        if (ifaceMethod.IsNoThrow != structMethod.IsNoThrow)
                        {
                            throw new TypeCheckException(
                                $"Method '{structMethod.Name}' in struct '{DisplayName(node.Name)}' must match nothrow specification of interface method '{DisplayName(ifaceName)}.{ifaceMethod.Name}'",
                                structMethod.Span);
                        }

                        if (ifaceMethod.IsReadOnly && !structMethod.IsReadOnly)
                        {
                            throw new TypeCheckException(
                                $"Method '{structMethod.Name}' in struct '{DisplayName(node.Name)}' must be marked readonly to implement interface method '{DisplayName(ifaceName)}.{ifaceMethod.Name}'",
                                structMethod.Span);
                        }

                        TypeExpression structReturnType = ResolveAlias(structMethod.ReturnType);
                        TypeExpression ifaceReturnType = ResolveAlias(ifaceMethod.ReturnType);
                        if (!TypesMatchPublic(structReturnType, ifaceReturnType))
                        {
                            throw new TypeCheckException(
                                $"Method '{structMethod.Name}' in struct '{DisplayName(node.Name)}' has return type '{DisplayTypeName(structReturnType)}', but interface '{DisplayName(ifaceName)}' requires '{DisplayTypeName(ifaceReturnType)}'",
                                structMethod.Span);
                        }

                        if (structMethod.Parameters.Count != ifaceMethod.Parameters.Count)
                        {
                            throw new TypeCheckException(
                                $"Method '{structMethod.Name}' in struct '{DisplayName(node.Name)}' has {structMethod.Parameters.Count} parameters, but interface '{DisplayName(ifaceName)}' expects {ifaceMethod.Parameters.Count}",
                                structMethod.Span);
                        }

                        for (int i = 0; i < ifaceMethod.Parameters.Count; i++)
                        {
                            TypeExpression structParamType = ResolveAlias(structMethod.Parameters[i].Type);
                            TypeExpression ifaceParamType = ResolveAlias(ifaceMethod.Parameters[i].Type);
                            if (!TypesMatchPublic(structParamType, ifaceParamType) || structMethod.Parameters[i].IsConst != ifaceMethod.Parameters[i].IsConst)
                            {
                                throw new TypeCheckException(
                                    $"Parameter '{structMethod.Parameters[i].Name}' of method '{structMethod.Name}' in struct '{DisplayName(node.Name)}' has type '{DisplayTypeName(structParamType)}', but interface '{DisplayName(ifaceName)}' expects '{DisplayTypeName(ifaceParamType)}'",
                                    structMethod.Parameters[i].Span);
                            }
                        }
                    }
                }

                foreach (AstNode member in node.Members)
                {
                    if (member is FieldDeclaration field)
                    {
                        if (field.IsStatic) { CheckStaticField(field); continue; }
                        field.Type = ResolveAlias(field.Type);
                        int fIdx = _currentStruct.Fields.FindIndex(f => f.Name == field.Name);
                        if (fIdx >= 0)
                        {
                            _currentStruct.Fields[fIdx] = (field.Name, field.Type);
                        }
                        ValidateValueType(field.Type, field.Line);
                        TypeExpression fieldType = field.Type;
                        if (field.IsConst && fieldType is PointerTypeExpression sPtr && !sPtr.IsReadOnly)
                        {
                            fieldType = new PointerTypeExpression(sPtr.Inner, sPtr.IsNullable, sPtr.Line, isReadOnly: true);
                        }
                        if (field.Initializer != null)
                        {
                            field.Initializer.Accept(this);
                            TypeExpression initType = ResolveAlias(GetType(field.Initializer));
                            fieldType = InferArrayStorageType(fieldType, field.Initializer, initType);
                            if (fieldType is ArrayTypeExpression)
                            {
                                field.Type = fieldType;
                                if (fIdx >= 0) _currentStruct.Fields[fIdx] = (field.Name, fieldType);
                            }
                            if (!IsAssignable(fieldType, initType, field.Initializer))
                            {
                                throw new TypeCheckException($"Cannot assign expression of type '{DisplayTypeName(initType)}' to field '{field.Name}' of type '{DisplayTypeName(fieldType)}'", field.Initializer.Span);
                            }
                        }
                        ValidateArrayValueShape(fieldType, field.Line);
                        if (field.IsConst)
                        {
                            if (field.Initializer == null)
                            {
                                throw new TypeCheckException($"Const variable '{field.Name}' must have an initializer", field.Line);
                            }
                            if (!_constEvaluator.TryEvaluate(field.Initializer, fieldType, out ConstValue? constVal, out string? err))
                            {
                                throw new TypeCheckException($"Const variable '{field.Name}' initializer must be a compile-time constant: {err}", field.Line);
                            }
                            _constVariablesByName[$"{node.Name}::{field.Name}"] = constVal!;
                            _constVariablesByName[field.Name] = constVal!;
                            _constValues[field] = constVal!;
                            _constValues[field.Initializer] = constVal!;
                            _constVariableNames.Add(field.Name);
                        }
                    }
                    else if (member is ConstructorDeclaration ctor)
                    {
                        ctor.Accept(this);
                    }
                    else if (member is DestructorDeclaration dtor)
                    {
                        dtor.Accept(this);
                    }
                    else if (member is MethodDeclaration method)
                    {
                        ValidatePropertyAccessor(method);
                        if (method.IsStatic)
                        {
                            Visit(method); // Static methods have no implicit this parameter.
                            continue;
                        }
                        method.ReturnType = ResolveAlias(method.ReturnType);
                        foreach (Parameter p in method.Parameters)
                        {
                            p.Type = ResolveAlias(p.Type);
                        }
                        AstNode? prevFunc = _currentFunction;
                        _currentFunction = method;
                        using var exceptionScope = IsolateExceptionHandlers();
                        using var nullableScope = IsolateNullableFlow();
                        if (method.Body != null) { _exceptionMethods.Add(method); _exceptionSummaries = null; }
                        try
                        {
                            ValidateReturnType(method.ReturnType, method.Line);
                            PushScope();
                            NamedTypeExpression structType = NamedTypeExpression.Resolved(node.Name, method.Line);
                            PointerTypeExpression thisType = new PointerTypeExpression(structType, false, method.Line, isReadOnly: method.IsReadOnly);
                            DeclareVariable("this", thisType, method.Line);

                            foreach (Parameter p in method.Parameters)
                            {
                                ValidateParameterType(p.Type, p.Line);
                                DeclareVariable(p.Name, ParameterValueType(p.Type), p.Line);
                                NullableParameter(p);
                            }

                            method.Body?.Accept(this);
                            PopScope();
                        }
                        finally
                        {
                            _currentFunction = prevFunc;
                        }
                    }
                    else if (member is OperatorDeclaration op)
                    {
                        op.ReturnType = ResolveAlias(op.ReturnType);
                        foreach (Parameter p in op.Parameters)
                        {
                            p.Type = ResolveAlias(p.Type);
                        }
                        op.Accept(this);
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
            }
            finally
            {
                _currentStruct = previousStruct;
                _currentClass = prevClass;
            }
        }

        public void Visit(OperatorDeclaration node)
        {
            using var nullableScope = IsolateNullableFlow();
            using var sourceContext = SourceContext.Enter(node.Span);
            ValidateParameterDefaults(node, node.Parameters);
            if (_currentStruct == null)
            {
                throw new TypeCheckException("Operator overloads must be declared inside a struct", node.Line);
            }

            ValidateReturnType(node.ReturnType, node.Line);
            if (RequiresCopy(node.ReturnType) || node.Parameters.Any(p => RequiresCopy(p.Type)))
                throw new TypeCheckException("Owning operator operands and results are not yet supported; use an explicit method", node.Line);

            if (node.OperatorKind == TokenKind.Bang)
            {
                if (node.Parameters.Count != 1)
                {
                    throw new TypeCheckException($"Unary operator '{node.OperatorSymbol}' must have exactly 1 parameter", node.Line);
                }
            }
            else if (node.OperatorKind == TokenKind.Minus)
            {
                if (node.Parameters.Count != 1 && node.Parameters.Count != 2)
                    throw new TypeCheckException($"Operator '{node.OperatorSymbol}' must have 1 or 2 parameters", node.Line);
            }
            else if (node.Parameters.Count != 2)
                throw new TypeCheckException($"Binary operator '{node.OperatorSymbol}' must have exactly 2 parameters", node.Line);

            // At least one parameter must be the enclosing struct type
            bool hasContainingType = false;
            foreach (Parameter p in node.Parameters)
            {
                ValidateParameterType(p.Type, p.Line);
                TypeExpression resolvedParam = ResolveAlias(p.Type);
                if (resolvedParam is NamedTypeExpression named && named.Name == _currentStruct.Name)
                {
                    hasContainingType = true;
                }
            }

            if (!hasContainingType)
            {
                throw new TypeCheckException($"One of the parameters of a user-defined operator must be the containing type '{_currentStruct.Name}'", node.Line);
            }

            ValidateOperatorContract(node);

            AstNode? prevFunc = _currentFunction;
            _currentFunction = node;
            try
            {
                PushScope();
                foreach (Parameter p in node.Parameters)
                {
                    DeclareVariable(p.Name, ParameterValueType(p.Type), p.Line);
                    NullableParameter(p);
                }
                node.Body.Accept(this);
                PopScope();
            }
            finally
            {
                _currentFunction = prevFunc;
            }
        }

        public void Visit(InterfaceDeclaration node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            var methods = node.Members.OfType<MethodDeclaration>().ToArray();
            if (methods.Any(m => m.IsStatic) && methods.Any(m => !m.IsStatic))
                throw new TypeCheckException("An interface cannot yet mix static and instance contracts", node.Span);
            foreach (AstNode member in node.Members)
            {
                if (member is PropertyDeclaration) continue;
                if (member is MethodDeclaration method)
                {
                    if (method.IsStatic && (method.IsReadOnly || method.IsVirtual || method.IsOverride || method.IsGeneric))
                        throw new TypeCheckException("Static interface contracts cannot be readonly, virtual, override, or generic", method.Span);
                    ValidatePropertyAccessor(method);
                    if (method.Body != null)
                    {
                        throw new TypeCheckException($"Interface method '{node.Name}.{method.Name}' cannot have a body", method.Line);
                    }
                    ValidateReturnType(method.ReturnType, method.Line);
                    ValidateParameterDefaults(method, method.Parameters);
                    foreach (Parameter p in method.Parameters)
                    {
                        ValidateParameterType(p.Type, p.Line);
                    }
                }
                else
                {
                    throw new TypeCheckException($"Interface '{node.Name}' can only contain method declarations", member.Line);
                }
            }
        }

        public void Visit(FieldDeclaration node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            ValidateValueType(node.Type, node.Line);
            TypeExpression fieldType = ResolveAlias(node.Type);
            if (node.IsConst && RequiresCopy(fieldType))
                throw new TypeCheckException("Constants cannot contain values with nontrivial copying or destruction", node.Line);
            if (node.IsConst && node.Initializer is ArrayLiteralExpression && HasDestructor(fieldType))
                throw new TypeCheckException("Constant arrays cannot contain destructor-bearing elements", node.Line);
            if (node.IsConst && fieldType is PointerTypeExpression fPtr && !fPtr.IsReadOnly)
            {
                fieldType = new PointerTypeExpression(fPtr.Inner, fPtr.IsNullable, fPtr.Line, isReadOnly: true);
            }
            if (node.Initializer != null)
            {
                node.Initializer.Accept(this);
                TypeExpression initType = ResolveAlias(GetType(node.Initializer));
                fieldType = InferArrayStorageType(fieldType, node.Initializer, initType);
                if (fieldType is ArrayTypeExpression) node.Type = fieldType;
                if (!IsAssignable(fieldType, initType, node.Initializer))
                {
                    throw new TypeCheckException($"Cannot assign expression of type '{DisplayTypeName(initType)}' to field '{node.Name}' of type '{DisplayTypeName(fieldType)}'", node.Initializer.Span);
                }
            }
            else if (node.IsConst)
            {
                throw new TypeCheckException($"Const variable '{node.Name}' must have an initializer", node.Line);
            }

            ValidateArrayValueShape(fieldType, node.Line);
            if (node.IsConst)
            {
                if (node.Initializer == null)
                {
                    throw new TypeCheckException($"Const variable '{node.Name}' must have an initializer", node.Line);
                }
                if (!_constEvaluator.TryEvaluate(node.Initializer, fieldType, out ConstValue? constVal, out string? err))
                {
                    throw new TypeCheckException($"Const variable '{node.Name}' initializer must be a compile-time constant: {err}", node.Line);
                }
                constVal = NormalizeStringBufferConstant(constVal!, fieldType);
                _constVariablesByName[node.Name] = constVal!;
                _constValues[node] = constVal!;
                _constValues[node.Initializer] = constVal!;
                _constVariableNames.Add(node.Name);
            }

            RecordType(node, fieldType);
            RecordStaticInitializer(node);
        }

        private static ConstValue NormalizeStringBufferConstant(ConstValue value, TypeExpression type)
        {
            if (value is not ConstValue.String text || type is not ArrayTypeExpression { Size: not null } array) return value;
            byte[] bytes = text.BytesFromOffset();
            return new ConstValue.Array(Enumerable.Range(0, array.Size.Value)
                .Select(i => (ConstValue)new ConstValue.Char((char)(i < bytes.Length ? bytes[i] : 0))).ToList());
        }

        public void Visit(MethodDeclaration node)
        {
            using var exceptionScope = IsolateExceptionHandlers();
            using var nullableScope = IsolateNullableFlow();
            using var sourceContext = SourceContext.Enter(node.Span);
            ValidatePropertyAccessor(node);
            ValidateStaticConstructor(node);
            if (node.Body == null)
                throw new TypeCheckException($"Function '{node.Name}' must declare a body; use extern for a function implemented elsewhere", node.Span);
            if (node.IsGeneric) return;
            BindMethodSignature(node);
            ValidateEntryPoint(node);
            ValidateParameterDefaults(node, node.Parameters);
            if (node.Body != null) { _exceptionMethods.Add(node); _exceptionSummaries = null; }



            AstNode? prevFunc = _currentFunction;
            _currentFunction = node;
            try
            {
                ValidateReturnType(node.ReturnType, node.Line);
                PushScope();
                foreach (Parameter p in node.Parameters)
                {
                    ValidateParameterType(p.Type, p.Line);
                    TypeExpression pType = ResolveAlias(p.Type);
                    DeclareVariable(p.Name, ParameterValueType(pType), p.Line);
                    NullableParameter(p);
                }
                node.Body?.Accept(this);
                PopScope();
            }
            finally
            {
                _currentFunction = prevFunc;
            }
        }

        public void Visit(ConstructorDeclaration node)
        {
            using var exceptionScope = IsolateExceptionHandlers();
            using var nullableScope = IsolateNullableFlow();
            using var sourceContext = SourceContext.Enter(node.Span);
            if (_currentStruct == null && _currentClass == null)
            {
                throw new TypeCheckException("Constructor must be declared inside a struct or class", node.Line);
            }
            string ownerName = _currentStruct?.Name ?? _currentClass!.Name;
            string shortOwnerName = ownerName.Split('.').Last().Split('$').Last();
            ValidateParameterDefaults(node, node.Parameters);
            if (node.Name != ownerName && node.Name != shortOwnerName)
            {
                string kindStr = _currentStruct != null ? "struct" : "class";
                throw new TypeCheckException($"Constructor name '{node.Name}' does not match {kindStr} name '{ownerName}'", node.Line);
            }
            if (node.IsCopy)
            {
                if (node.Parameters.Count != 1 || ResolveAlias(node.Parameters[0].Type) is not PointerTypeExpression { IsNullable: false, IsReadOnly: true } source ||
                    ResolveAlias(source.Inner) is not NamedTypeExpression owner || owner.Name != ownerName || node.IsConst)
                    throw new TypeCheckException("A copy constructor takes exactly one readonly, non-null pointer to its own type", node.Line);
                if ((_currentStruct?.Constructors ?? _currentClass!.Constructors).Count(c => c.IsCopy) != 1)
                    throw new TypeCheckException("A type has exactly one copy constructor", node.Line);
            }
            _currentConstructor = node;
            AstNode? prevFunc = _currentFunction;
            _currentFunction = node;
            try
            {
                PushScope();
                NamedTypeExpression typeExpr = NamedTypeExpression.Resolved(ownerName, node.Line);
                PointerTypeExpression thisType = new PointerTypeExpression(typeExpr, false, node.Line);
                DeclareVariable("this", thisType, node.Line);

                foreach (Parameter p in node.Parameters)
                {
                    p.Type = ResolveAlias(p.Type);
                    ValidateParameterType(p.Type, p.Line);
                    DeclareVariable(p.Name, ParameterValueType(p.Type), p.Line);
                    NullableParameter(p);
                }

                if (node.BaseArguments != null)
                {
                    if (_currentClass == null || _currentClass.BaseClass == null)
                    {
                        throw new TypeCheckException($"Cannot call base constructor because '{ownerName}' does not inherit from a base class", node.Line);
                    }

                    ClassInfo baseClass = _classes[_currentClass.BaseClass];
                    foreach (AstNode arg in node.BaseArguments)
                    {
                        CheckNullableArgument(arg);

                    }

                    List<ConstructorDeclaration> matches = new();
                    foreach (ConstructorDeclaration baseCtor in baseClass.Constructors)
                    {
                        if (!AcceptsArgumentCount(baseCtor.Parameters, node.BaseArguments.Count))
                            continue;

                        bool matchesParams = true;
                        for (int i = 0; i < node.BaseArguments.Count; i++)
                        {
                            TypeExpression paramType = ResolveAlias(baseCtor.Parameters[i].Type);
                            TypeExpression argType = GetType(node.BaseArguments[i]);
                            if (!IsAssignable(paramType, argType, node.BaseArguments[i]))
                            {
                                matchesParams = false;
                                break;
                            }
                        }

                        if (matchesParams)
                            matches.Add(baseCtor);
                    }

                    if (matches.Count == 0)
                    {
                        string argTypes = string.Join(", ", node.BaseArguments.Select(a => TypeName(GetType(a))));
                        throw new TypeCheckException($"No matching base constructor found for '{baseClass.Name}' with arguments ({argTypes})", node.Line);
                    }
                    var selectedBase = SelectBestOverload(matches, c => c.Parameters, node.BaseArguments, baseClass.Name, node.Line);
                    _resolvedBaseConstructors[node] = selectedBase;
                    AddDefaultArguments(node.BaseArguments, selectedBase.Parameters, node);
                }
                else if (_currentClass != null && _currentClass.BaseClass != null)
                {
                    ClassInfo baseClass = _classes[_currentClass.BaseClass];
                    if (baseClass.Constructors.Count == 0) ValidateImplicitConstruction(baseClass.Name, node.Line);
                    if (baseClass.Constructors.Count > 0 && !baseClass.Constructors.Any(c => AcceptsArgumentCount(c.Parameters, 0)))
                    {
                        throw new TypeCheckException($"Class '{ownerName}' must explicitly call a base constructor because base class '{baseClass.Name}' does not define a parameterless constructor", node.Line);
                    }
                    if (baseClass.Constructors.Count > 0)
                    {
                        var arguments = new List<AstNode>();
                        var selectedBase = SelectBestOverload(baseClass.Constructors, c => c.Parameters, arguments, baseClass.Name, node.Line);
                        _resolvedBaseConstructors[node] = selectedBase;
                        AddDefaultArguments(arguments, selectedBase.Parameters, node);
                        node.BaseArguments = arguments;
                    }
                }

                node.Body.Accept(this);
                PopScope();
            }
            finally
            {
                _currentFunction = prevFunc;
                _currentConstructor = null;
            }
        }

        public void Visit(DestructorDeclaration node)
        {
            using var exceptionScope = IsolateExceptionHandlers();
            using var nullableScope = IsolateNullableFlow();
            using var sourceContext = SourceContext.Enter(node.Span);
            if (_currentClass == null && _currentStruct == null)
            {
                throw new TypeCheckException("Destructor must be declared inside a class or struct", node.Line);
            }
            string ownerName = _currentStruct?.Name ?? _currentClass!.Name;
            string shortOwnerName = ownerName.Split('.').Last().Split('$').Last();
            if (node.Name != ownerName && node.Name != shortOwnerName)
            {
                string kindStr = _currentStruct != null ? "struct" : "class";
                throw new TypeCheckException($"Destructor name '~{node.Name}' does not match {kindStr} name '{ownerName}'", node.Line);
            }

            AstNode? prevFunc = _currentFunction;
            _currentFunction = node;
            try
            {
                PushScope();
                NamedTypeExpression typeExpr = NamedTypeExpression.Resolved(ownerName, node.Line);
                PointerTypeExpression thisType = new PointerTypeExpression(typeExpr, false, node.Line);
                DeclareVariable("this", thisType, node.Line);
                node.Body.Accept(this);
                PopScope();
            }
            finally
            {
                _currentFunction = prevFunc;
            }
        }

        public void Visit(Parameter node) {
            using var sourceContext = SourceContext.Enter(node.Span); }

        public void Visit(BlockStatement node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            PushScope();
            using var nullableBlock = NullableBlock();
            foreach (AstNode statement in node.Statements)
                AnalyzeSafely(() => statement.Accept(this));
            PopScope();
        }

        public void Visit(DeferStatement node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (node.Statement is ReturnStatement)
                throw new TypeCheckException("Cannot return from within a defer statement", node.Line);

            bool prevInDefer = _inDefer;
            int previousLoopDepth = _loopDepth;
            _inDefer = true;
            _loopDepth = 0;
            try
            {
                CheckNullableDefer(node.Statement);
            }
            finally
            {
                _inDefer = prevInDefer;
                _loopDepth = previousLoopDepth;
            }
        }

        public void Visit(DeleteStatement node)
        {
            using var nullableCall = NullableCall();
            using var sourceContext = SourceContext.Enter(node.Span);
            node.Target.Accept(this);
            TypeExpression targetType = ResolveAlias(GetType(node.Target));
            if (IsError(targetType))
            {
                return;
            }
            if (targetType is FunctionPointerTypeExpression)
            {
                throw new TypeCheckException("Cannot delete a function pointer", node.Line);
            }
            if (targetType is not PointerTypeExpression ptr)
            {
                throw new TypeCheckException($"Cannot delete non-pointer type '{TypeName(targetType)}'", node.Line);
            }
            if (ptr.IsReadOnly)
            {
                throw new TypeCheckException("Cannot delete a readonly pointer", node.Line);
            }
            if (ptr.Inner is NamedTypeExpression { Name: "void" })
            {
                throw new TypeCheckException("Cannot delete 'void*'", node.Line);
            }

            TypeExpression inner = ResolveAlias(ptr.Inner);
            RequireMemoryFunction("Free", node);
            if (inner is NamedTypeExpression namedCls && _classes.TryGetValue(namedCls.Name, out ClassInfo? clsInfo))
            {
                bool hasVirtualDtor = clsInfo.DestructorSlot >= 0;
                bool hasDtor = hasVirtualDtor || HasAnyDestructor(clsInfo);
                if (hasDtor)
                {
                    _classDestructorCalls[node] = (clsInfo, hasVirtualDtor, clsInfo.DestructorSlot);
                }
            }
            else if (inner is NamedTypeExpression namedStr && _structs.TryGetValue(namedStr.Name, out StructInfo? structInfo))
            {
                if (HasDestructor(inner))
                {
                    _structDestructorCalls[node] = structInfo;
                }
            }
        }

        public void Visit(ReturnStatement node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (_inDefer)
                throw new TypeCheckException("Cannot return from within a defer statement", node.Line);

            if (node.Value != null)
            {
                node.Value.Accept(this);
                if (_actualReturnTypes.Count > 0)
                {
                    _actualReturnTypes.Peek().Add(GetType(node.Value));
                }

                if (_currentFunction is MethodDeclaration method)
                {
                    TypeExpression retType = ResolveAlias(method.ReturnType);
                    TypeExpression valType = GetType(node.Value);
                    if (method.IsRefReturn)
                    {
                        if (node.Value is not (IdentifierExpression or MemberAccessExpression or IndexExpression or UnaryExpression { Operator: TokenKind.Star }) ||
                            IsLocalOrTemporaryReferenceStorage(node.Value))
                            throw new TypeCheckException("A ref getter must return existing storage, not a local or temporary", node.Line);
                        var address = new UnaryExpression(node.Value, TokenKind.Ampersand, true, node.Line) { Span = node.Span };
                        address.Accept(this);
                        _referenceReturns[node] = address;
                        valType = GetType(address);
                    }
                    if (HasDestructor(retType) && node.Value is not IdentifierExpression && !ValidateCopyUse(node.Value, retType))
                        throw new TypeCheckException("Cannot transfer a destructor-bearing value from borrowed storage", node.Line);
                    if (HasDestructor(retType) && node.Value is IdentifierExpression owned)
                    {
                        if (_scopes.Any(scope => scope.ContainsKey(owned.Name)) && owned.Name != "this")
                            _ownedReturns[node] = owned.Name;
                        else if (!ValidateCopyUse(node.Value, retType))
                            throw new TypeCheckException("Returning this field requires a copy constructor", node.Line);
                    }
                    if (!IsAssignable(retType, valType, _ownedReturns.ContainsKey(node) ? null : node.Value))
                    {
                        throw new TypeCheckException($"Cannot return '{TypeName(valType)}' from function returning '{TypeName(retType)}'", node.Line);
                    }
                }
                else if (_currentFunction is OperatorDeclaration operation)
                {
                    TypeExpression result = ResolveAlias(operation.ReturnType);
                    TypeExpression value = GetType(node.Value);
                    if (!IsAssignable(result, value, node.Value))
                        throw new TypeCheckException($"Cannot return '{TypeName(value)}' from operator returning '{TypeName(result)}'", node.Value.Span);
                }
                else if (_currentFunction is ConstructorDeclaration or DestructorDeclaration)
                {
                    throw new TypeCheckException("Cannot return a value from a constructor or destructor", node.Line);
                }
            }
            else
            {
                if (_actualReturnTypes.Count > 0)
                {
                    _actualReturnTypes.Peek().Add(Void);
                }

                if (_currentFunction is MethodDeclaration method)
                {
                    TypeExpression retType = ResolveAlias(method.ReturnType);
                    if (retType is not NamedTypeExpression { Name: "void" })
                    {
                        throw new TypeCheckException($"Cannot return void from function returning '{TypeName(retType)}'", node.Line);
                    }
                }
                else if (_currentFunction is OperatorDeclaration operation &&
                    ResolveAlias(operation.ReturnType) is not NamedTypeExpression { Name: "void" })
                    throw new TypeCheckException($"Cannot return void from operator returning '{TypeName(operation.ReturnType)}'", node.Span);
            }
        }

        public void Visit(IfStatement node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            node.Condition.Accept(this);
            TypeExpression condType = GetType(node.Condition);
            if (!TypesMatch(condType, Bool))
                throw new TypeCheckException("If condition must be bool", node.Line);
            NullableBranch(node);
        }

        public void Visit(WhileStatement node)
        {
            NullableLoopEntry();
            using var sourceContext = SourceContext.Enter(node.Span);
            node.Condition.Accept(this);
            TypeExpression condType = GetType(node.Condition);
            if (!TypesMatch(condType, Bool))
                throw new TypeCheckException("While condition must be bool", node.Line);
            _loopDepth++;
            try
            {
                NullableLoopBody(node.Condition);
                node.Body.Accept(this);
            }
            finally
            {
                NullableLoopEntry();
                _loopDepth--;
            }
        }

        public void Visit(ForStatement node)
        {
            NullableLoopEntry();
            using var sourceContext = SourceContext.Enter(node.Span);
            PushScope();
            node.Initializer?.Accept(this);
            NullableLoopEntry(); // The initializer executes once, not once per iteration.
            if (node.Condition != null)
            {
                node.Condition.Accept(this);
                TypeExpression condType = GetType(node.Condition);
                if (!TypesMatch(condType, Bool))
                    throw new TypeCheckException("For condition must be bool", node.Line);
            }
            _loopDepth++;
            try
            {
                NullableLoopBody(node.Condition);
                node.Body.Accept(this);
                NullableLoopEntry();
                node.Increment?.Accept(this);
            }
            finally
            {
                NullableLoopEntry();
                _loopDepth--;
            }
            PopScope();
        }

        public void Visit(ForeachStatement node)
        {
            NullableLoopEntry();
            using var sourceContext = SourceContext.Enter(node.Span);
            node.ElementType = ResolveAlias(node.ElementType);
            ValidateValueType(node.ElementType, node.Line);
            if (!CanCopy(node.ElementType))
                throw new TypeCheckException($"Foreach iteration variable '{node.VariableName}' requires a copyable element type; borrow elements through an indexer instead", node.Line);

            node.Collection.Accept(this);
            TypeExpression rawColType = ResolveAlias(GetType(node.Collection));

            if (rawColType is ArrayTypeExpression arr)
            {
                if (!arr.Size.HasValue)
                {
                    throw new TypeCheckException("Cannot iterate over an unsized array in foreach", node.Line);
                }

                if (!IsAssignable(node.ElementType, arr.ElementType, node.Collection))
                {
                    throw new TypeCheckException($"Cannot assign array element type '{TypeName(arr.ElementType)}' to foreach variable of type '{TypeName(node.ElementType)}'", node.Line);
                }

                node.IsArrayIteration = true;
                PushScope();
                DeclareVariable(node.VariableName, node.ElementType, node.Line);
                _loopDepth++;
                try
                {
                    NullableLoopEntry();
                    node.Body.Accept(this);
                }
                finally
                {
                    NullableLoopEntry();
                    _loopDepth--;
                }
                PopScope();
                return;
            }

            // Bind the ordinary call against an anchored receiver. A temporary
            // collection must remain alive until the iterator has been destroyed.
            AstNode targetCollection = node.Collection;
            VariableDeclaration? collectionTemporary = null;
            if (node.Collection is not IdentifierExpression)
            {
                string name = $"__col_{node.Line}_{_foreachCounter++}";
                collectionTemporary = new VariableDeclaration(name, rawColType, node.Collection, node.Line);
                targetCollection = new IdentifierExpression(name, node.Line);
                precheckedForeachInitializers.Add(collectionTemporary);
            }
            var getEnumerator = new CallExpression(new MemberAccessExpression(targetCollection,
                "GetEnumerator", false, node.Line), [], node.Line);
            TypeExpression enumeratorType;
            bool currentIsMethod;
            PushScope();
            try
            {
                if (collectionTemporary != null) DeclareVariable(collectionTemporary.Name, rawColType, node.Line);
                getEnumerator.Accept(this);
                enumeratorType = ResolveAlias(GetType(getEnumerator));
                var valueType = enumeratorType switch {
                    PointerTypeExpression pointer => ResolveAlias(pointer.Inner),
                    ManagedTypeExpression managed => ResolveAlias(managed.Inner),
                    _ => enumeratorType
                };
                if (valueType is not NamedTypeExpression named)
                    throw new TypeCheckException("GetEnumerator() must return a struct, class, or interface iterator", node.Span);
                currentIsMethod = _classes.TryGetValue(named.Name, out var cls) ? cls.Methods.ContainsKey("Current") :
                    _structs.TryGetValue(named.Name, out var str) ? str.Methods.ContainsKey("Current") :
                    ResolveInterface(named)?.MethodsByName.ContainsKey("Current") == true;
            }
            finally { PopScope(); }
            precheckedForeachCalls.Add(getEnumerator);

            string iteratorName = $"__iter_{node.Line}_{_foreachCounter++}";
            var iteratorDeclaration = new VariableDeclaration(iteratorName, enumeratorType, getEnumerator, node.Line);
            var iterator = new IdentifierExpression(iteratorName, node.Line);
            var moveNext = new CallExpression(new MemberAccessExpression(iterator, "MoveNext", false, node.Line), [], node.Line);
            AstNode current = new MemberAccessExpression(new IdentifierExpression(iteratorName, node.Line), "Current", false, node.Line);
            if (currentIsMethod) current = new CallExpression(current, [], node.Line);
            var element = new VariableDeclaration(node.VariableName, node.ElementType, current, node.Line);
            var body = new BlockStatement(new[] { (AstNode)element }.Concat(node.Body.Statements).ToList(), node.Line);
            var loop = new WhileStatement(moveNext, body, node.Line);
            var statements = new List<AstNode>();
            if (collectionTemporary != null) statements.Add(collectionTemporary);
            statements.Add(iteratorDeclaration);
            statements.Add(loop);
            var lowered = new BlockStatement(statements, node.Line);
            lowered.Accept(this);
            node.Desugared = lowered;
        }
        public void Visit(VariableDeclaration node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            TypeExpression varType = ResolveAlias(node.Type);
            if (node.IsConst && RequiresCopy(varType))
                throw new TypeCheckException("Constants cannot contain values with nontrivial copying or destruction", node.Line);
            if (node.IsConst && node.Initializer is ArrayLiteralExpression && HasDestructor(varType))
                throw new TypeCheckException("Constant arrays cannot contain destructor-bearing elements", node.Line);
            if (node.IsConst && varType is PointerTypeExpression ptr && !ptr.IsReadOnly)
            {
                varType = new PointerTypeExpression(ptr.Inner, ptr.IsNullable, ptr.Line, isReadOnly: true);
            }
            ValidateValueType(varType, node.Line);
            if (varType is ArrayTypeExpression && HasDestructor(varType) && node.Initializer == null)
                throw new TypeCheckException("An array with destructor-bearing elements requires an initializer; use default(Type) for zero-initialized elements", node.Line);
            if (node.Initializer != null)
            {
                if (!precheckedForeachInitializers.Remove(node)) node.Initializer.Accept(this);
                TypeExpression initType = ResolveAlias(GetType(node.Initializer));

                varType = InferArrayStorageType(varType, node.Initializer, initType);

                if (!ValidateCopyUse(node.Initializer, varType))
                {
                    throw new TypeCheckException($"Cannot copy value of type '{DisplayTypeName(varType)}' because it has no accessible copy constructor. Borrow through a pointer or move a local.", node.Initializer.Span);
                }

                if (!IsAssignable(varType, initType, node.Initializer))
                {
                    _diagnostics.Report(DiagnosticRules.GF1001_TypeMismatch, node.Initializer.Span, TypeName(initType), TypeName(varType));
                }
            }
            else if (node.IsConst)
            {
                throw new TypeCheckException($"Const variable '{node.Name}' must have an initializer", node.Line);
            }

            ValidateArrayValueShape(varType, node.Line);
            if (node.IsConst)
            {
                if (node.Initializer == null)
                {
                    throw new TypeCheckException($"Const variable '{node.Name}' must have an initializer", node.Line);
                }
                if (!_constEvaluator.TryEvaluate(node.Initializer, varType, out ConstValue? constVal, out string? err))
                {
                    throw new TypeCheckException($"Const variable '{node.Name}' initializer must be a compile-time constant: {err}", node.Line);
                }
                constVal = NormalizeStringBufferConstant(constVal!, varType);
                _constVariablesByName[node.Name] = constVal!;
                _constValues[node] = constVal!;
                _constValues[node.Initializer] = constVal!;
                _constVariableNames.Add(node.Name);
            }

            RecordType(node, varType);
            DeclareVariable(node.Name, varType, node.Line);
            if (node.IsConst) localConstants.Peek()[node.Name] = (_constValues[node], node);
            NullableAssignment(new IdentifierExpression(node.Name, node.Line), node.Initializer);
            if (node.Initializer != null && CopiesValue(node.Initializer, varType)) InvalidateNullableEscapes();
        }

        public void Visit(ExpressionStatement node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            node.Expression.Accept(this);
            if (IsFreshValue(node.Expression) && HasDestructor(GetType(node.Expression))) InvalidateNullableEscapes();
        }

        public void Visit(BinaryExpression node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            using var nullableResult = NullableBinaryResult(node);
            NullableBinaryOperands(node);
            TypeExpression left = GetType(node.Left);
            TypeExpression right = GetType(node.Right);

            if (IsError(left) || IsError(right))
            {
                RecordType(node, Error);
                return;
            }

            if (TryResolveBinaryOperatorForTypes(node.Operator, left, right, node, out string opStruct, out OperatorDeclaration opDecl))
            {
                ValidateOperatorAccess(opDecl, opStruct, node);
                _operatorTargets[node] = (opStruct, opDecl);
                RecordType(node, ResolveAlias(opDecl.ReturnType));
                return;
            }

            if (CheckFloatingBinary(node, left, right)) return;
            if (!CheckBuiltinOperatorOperands(node, node.Operator, left, right)) return;

            bool isComparison = node.Operator is
                TokenKind.EqualsEquals or TokenKind.NotEquals or
                TokenKind.Less or TokenKind.Greater or
                TokenKind.LessEquals or TokenKind.GreaterEquals;

            ContextualizeNativeIntegerOperands(node, ref left, ref right);

            bool isLogical = node.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe;

            if (isLogical)
            {
                if (!TypesMatch(left, Bool) || !TypesMatch(right, Bool))
                {
                    ReportError(DiagnosticRules.GF1002_BinaryOperatorMismatch, node.Line, 0, OperatorText(node.Operator), TypeName(left), TypeName(right));
                    RecordType(node, Error);
                    return;
                }
                RecordType(node, Bool);
            }
            else if (isComparison)
            {
                PlanIntegerComparison(node, left, right);
                if (!TypesMatch(left, right) && !IsAssignable(left, right) && !IsAssignable(right, left) && !(IsAssignable(Int, left) && IsAssignable(Int, right)))
                {
                    ReportError(DiagnosticRules.GF1002_BinaryOperatorMismatch, node.Line, 0, OperatorText(node.Operator), TypeName(left), TypeName(right));
                    RecordType(node, Error);
                    return;
                }
                RecordType(node, Bool);
            }
            else if (node.Operator == TokenKind.Plus)
            {
                TypeExpression resolvedLeft = ResolveAlias(left);
                TypeExpression resolvedRight = ResolveAlias(right);

                if ((resolvedLeft is PointerTypeExpression or FunctionPointerTypeExpression or ManagedTypeExpression) &&
                    (resolvedRight is PointerTypeExpression or FunctionPointerTypeExpression or ManagedTypeExpression))
                {
                    ReportError("Cannot add two pointers together", node.Line);
                    RecordType(node, Error);
                    return;
                }

                string? errLeft = null;
                string? errRight = null;
                if (IsValidPointerForArithmetic(resolvedLeft, out errLeft) && IsInteger(resolvedRight))
                {
                    if (errLeft != null)
                    {
                        ReportError(errLeft, node.Line);
                        RecordType(node, Error);
                        return;
                    }
                    RecordType(node, left);
                    return;
                }
                if (IsInteger(resolvedLeft) && IsValidPointerForArithmetic(resolvedRight, out errRight))
                {
                    if (errRight != null)
                    {
                        ReportError(errRight, node.Line);
                        RecordType(node, Error);
                        return;
                    }
                    RecordType(node, right);
                    return;
                }
                if (errLeft != null)
                {
                    ReportError(errLeft, node.Line);
                    RecordType(node, Error);
                    return;
                }
                if (errRight != null)
                {
                    ReportError(errRight, node.Line);
                    RecordType(node, Error);
                    return;
                }

                if (!TypesMatch(left, right))
                {
                    if (IsInteger(left) && IsInteger(right))
                    {
                        if (IsAssignable(left, right))
                        {
                            RecordType(node, left);
                            return;
                        }
                        if (IsAssignable(right, left))
                        {
                            RecordType(node, right);
                            return;
                        }
                        if (IsAssignable(Int, left) && IsAssignable(Int, right))
                        {
                            RecordType(node, Int);
                            return;
                        }
                    }
                    ReportError(DiagnosticRules.GF1002_BinaryOperatorMismatch, node.Line, 0, "+", TypeName(left), TypeName(right));
                    RecordType(node, Error);
                    return;
                }
                RecordType(node, left);
            }
            else if (node.Operator == TokenKind.Minus)
            {
                TypeExpression resolvedLeft = ResolveAlias(left);
                TypeExpression resolvedRight = ResolveAlias(right);

                string? errLeft = null;
                string? errRight = null;
                if (IsValidPointerForArithmetic(resolvedLeft, out errLeft) && IsInteger(resolvedRight))
                {
                    if (errLeft != null)
                    {
                        ReportError(errLeft, node.Line);
                        RecordType(node, Error);
                        return;
                    }
                    RecordType(node, left);
                    return;
                }
                if (IsValidPointerForArithmetic(resolvedLeft, out string? errL) && IsValidPointerForArithmetic(resolvedRight, out string? errR))
                {
                    if (errL != null)
                    {
                        ReportError(errL, node.Line);
                        RecordType(node, Error);
                        return;
                    }
                    if (errR != null)
                    {
                        ReportError(errR, node.Line);
                        RecordType(node, Error);
                        return;
                    }

                    if (!TypesMatch(resolvedLeft, resolvedRight))
                    {
                        ReportError($"Cannot subtract pointers of different types '{TypeName(left)}' and '{TypeName(right)}'", node.Line);
                        RecordType(node, Error);
                        return;
                    }
                    RecordType(node, Long);
                    return;
                }
                if (errLeft != null)
                {
                    ReportError(errLeft, node.Line);
                    RecordType(node, Error);
                    return;
                }
                if (errRight != null)
                {
                    ReportError(errRight, node.Line);
                    RecordType(node, Error);
                    return;
                }

                if (!TypesMatch(left, right))
                {
                    if (IsInteger(left) && IsInteger(right))
                    {
                        if (IsAssignable(left, right))
                        {
                            RecordType(node, left);
                            return;
                        }
                        if (IsAssignable(right, left))
                        {
                            RecordType(node, right);
                            return;
                        }
                        if (IsAssignable(Int, left) && IsAssignable(Int, right))
                        {
                            RecordType(node, Int);
                            return;
                        }
                    }
                    ReportError(DiagnosticRules.GF1002_BinaryOperatorMismatch, node.Line, 0, "-", TypeName(left), TypeName(right));
                    RecordType(node, Error);
                    return;
                }
                RecordType(node, left);
            }
            else if (node.Operator is TokenKind.LessLess or TokenKind.GreaterGreater)
            {
                if (!IsInteger(left) || !IsInteger(right))
                {
                    ReportError(DiagnosticRules.GF1002_BinaryOperatorMismatch, node.Line, 0, OperatorText(node.Operator), TypeName(left), TypeName(right));
                    RecordType(node, Error);
                    return;
                }
                RecordType(node, left);
            }
            else
            {
                if (!TypesMatch(left, right))
                {
                    if (IsInteger(left) && IsInteger(right))
                    {
                        if (IsAssignable(left, right))
                        {
                            RecordType(node, left);
                            return;
                        }
                        if (IsAssignable(right, left))
                        {
                            RecordType(node, right);
                            return;
                        }
                        if (IsAssignable(Int, left) && IsAssignable(Int, right))
                        {
                            RecordType(node, Int);
                            return;
                        }
                    }
                    ReportError(DiagnosticRules.GF1002_BinaryOperatorMismatch, node.Line, 0, OperatorText(node.Operator), TypeName(left), TypeName(right));
                    RecordType(node, Error);
                    return;
                }
                RecordType(node, left);
            }
        }

        public void Visit(UnaryExpression node)
        {
            using var nullableResult = NullableUnaryResult(node);
            if (node.Operator == TokenKind.Ref)
            {
                CheckReferenceArgument(node);
                return;
            }

            using var sourceContext = SourceContext.Enter(node.Span);
            if (node.Operator == TokenKind.Bang && !node.IsPrefix)
            {
                node.Operand.Accept(this);
                TypeExpression source = ResolveAlias(GetType(node.Operand));
                TypeExpression result = source switch
                {
                    PointerTypeExpression p => new PointerTypeExpression(p.Inner, false, node.Line, p.IsReadOnly),
                    ManagedTypeExpression m => new ManagedTypeExpression(m.Inner, false, node.Line, m.IsReadOnly, m.Collector) { UsesDefaultCollector = m.UsesDefaultCollector },
                    FunctionPointerTypeExpression f => new FunctionPointerTypeExpression(f.ReturnType, f.ParameterTypes, f.IsManaged, false, node.Line),
                    _ => throw new TypeCheckException("Postfix '!' requires a pointer, managed reference, or function pointer", node.Line)
                };
                RecordType(node, result.WithReadOnlyValue(source.IsReadOnlyValue));
                return;
            }
            if (node.Operator is TokenKind.PlusPlus or TokenKind.MinusMinus &&
                CheckPropertyWrite(node, node.Operand, node.Operand, null, !node.IsPrefix, node.Operator)) return;
            if (node.Operator == TokenKind.Ampersand && FindProperty(node.Operand) is { Getter.IsRefReturn: false })
                throw new TypeCheckException("Cannot take the address of a property", node.Line);
            if (node.Operator == TokenKind.Ampersand)
            {
                if (node.Operand is IdentifierExpression identOperand && !TryLookupVariable(identOperand.Name, out _))
                {
                    MethodDeclaration? method = ResolveFunction(identOperand.Name);
                    ExternDeclaration? ext = method == null ? ResolveExtern(identOperand.Name) : null;
                    if (method != null)
                    {
                        var paramTypes = method.Parameters.Select(p => ResolveAlias(p.Type)).ToList();
                        var fnType = new FunctionPointerTypeExpression(ResolveAlias(method.ReturnType), paramTypes, false, false, node.Line);
                        RecordType(node, fnType);
                        ValidateFunctionAddress(method, node.Line);
                        _functionAddressTargets[node] = method;
                        return;
                    }
                    if (ext != null)
                    {
                        var paramTypes = ext.Parameters.Select(p => ResolveAlias(p.Type)).ToList();
                        var fnType = new FunctionPointerTypeExpression(ResolveAlias(ext.ReturnType), paramTypes, false, false, node.Line);
                        RecordType(node, fnType);
                        ValidateFunctionAddress(ext, node.Line);
                        _functionAddressTargets[node] = ext;
                        return;
                    }
                }
                else if (node.Operand is NamespaceAccessExpression nsAccess)
                {
                    MethodDeclaration? staticMethod = ResolveStaticMethod(nsAccess, deferSelection: true);
                    if (staticMethod != null)
                    {
                        var parameters = staticMethod.Parameters.Select(p => ResolveAlias(p.Type)).ToList();
                        RecordType(node, new FunctionPointerTypeExpression(ResolveAlias(staticMethod.ReturnType), parameters, false, false, node.Line));
                        ValidateFunctionAddress(staticMethod, node.Line);
                        _functionAddressTargets[node] = staticMethod;
                        return;
                    }
                    NamespaceScope? scope = ResolveNamespace(nsAccess.Left);
                    if (scope != null)
                    {
                        if (scope.Functions.TryGetValue(nsAccess.Member, out MethodDeclaration? m))
                        {
                            var paramTypes = m.Parameters.Select(p => ResolveAlias(p.Type)).ToList();
                            var fnType = new FunctionPointerTypeExpression(ResolveAlias(m.ReturnType), paramTypes, false, false, node.Line);
                            RecordType(node, fnType);
                            ValidateFunctionAddress(m, node.Line);
                        _functionAddressTargets[node] = m;
                            return;
                        }
                        if (scope.Externs.TryGetValue(nsAccess.Member, out ExternDeclaration? e))
                        {
                            var paramTypes = e.Parameters.Select(p => ResolveAlias(p.Type)).ToList();
                            var fnType = new FunctionPointerTypeExpression(ResolveAlias(e.ReturnType), paramTypes, false, false, node.Line);
                            RecordType(node, fnType);
                            ValidateFunctionAddress(e, node.Line);
                        _functionAddressTargets[node] = e;
                            return;
                        }
                    }
                }

                CheckNullableStorage(node.Operand, escape: true);
                TypeExpression operandType = GetType(node.Operand);
                if (IsPropertyValueStorage(node.Operand))
                    throw new TypeCheckException("Cannot take the address of a value returned by a property", node.Line);
                if (IsManagedStorage(node.Operand))
                    throw new TypeCheckException("Cannot take a raw address into managed storage", node.Line);
                if (IsError(operandType))
                {
                    RecordType(node, Error);
                    return;
                }
                if (ResolveAlias(operandType) is NamedTypeExpression { Name: "null" })
                    throw new TypeCheckException("null has no value storage; cast it to a nullable pointer type before taking its address", node.Span);
                ValidateValueType(operandType, node.Line);
                bool isReadOnly = IsExpressionReadOnly(node.Operand);
                RecordType(node, new PointerTypeExpression(operandType, false, node.Line, isReadOnly: isReadOnly));
                if (_constEvaluator.TryEvaluate(node, out ConstValue? constPtr, out _))
                {
                    _constValues[node] = constPtr!;
                }
                return;
            }

            CheckUnaryLiteralOperand(node);
            TypeExpression operand = GetType(node.Operand);

            if (IsError(operand))
            {
                RecordType(node, Error);
                return;
            }

            if (TryResolveUnaryOperatorForType(node.Operator, operand, out string opStruct, out OperatorDeclaration opDecl))
            {
                ValidateOperatorAccess(opDecl, opStruct, node);
                _unaryOperatorTargets[node] = (opStruct, opDecl);
                RecordType(node, ResolveAlias(opDecl.ReturnType));
                return;
            }

            switch (node.Operator)
            {
                case TokenKind.Bang:
                    if (!TypesMatch(operand, Bool))
                        throw new TypeCheckException("'!' requires bool operand", node.Line);
                    RecordType(node, Bool);
                    break;
                case TokenKind.Minus:
                    if (!IsNumeric(operand))
                        throw new TypeCheckException("'-' requires numeric operand", node.Line);
                    RecordType(node, operand);
                    break;
                case TokenKind.PlusPlus:
                case TokenKind.MinusMinus:
                    CheckAssignmentTarget(node.Operand, node.Line);
                    if (!IsNumeric(operand))
                    {
                        if (IsValidPointerForArithmetic(operand, out string? errPtr))
                        {
                            RecordType(node, operand);
                            break;
                        }
                        if (errPtr != null)
                            throw new TypeCheckException(errPtr, node.Line);
                        throw new TypeCheckException("'++/--' requires numeric or pointer operand", node.Line);
                    }
                    RecordType(node, operand);
                    break;
                case TokenKind.Star:
                    TypeExpression? inner = ResolveAlias(operand) switch
                    {
                        PointerTypeExpression { IsNullable: false } pointer => pointer.Inner,
                        PointerTypeExpression => throw new TypeCheckException("Cast a nullable pointer to non-null before dereferencing", node.Line),
                        ManagedTypeExpression { IsNullable: false } managed => managed.Inner,
                        ManagedTypeExpression => throw new TypeCheckException("Cast a nullable managed pointer to non-null before dereferencing", node.Line),
                        _ => null
                    };
                    if (inner == null)
                        throw new TypeCheckException("Cannot dereference non-pointer", node.Line);
                    TypeExpression resolvedInner = ResolveAlias(inner);
                    if (resolvedInner is NamedTypeExpression namedInner && namedInner.Name == "void")
                    {
                        throw new TypeCheckException("Cannot dereference 'void*'", node.Line);
                    }
                    ValidateTypeUsage(inner, node.Line);
                    ValidateArrayValueShape(inner, node.Line);
                    RecordType(node, inner);
                    break;
                default:
                    throw new NotImplementedException($"Unary operator {node.Operator} not yet supported");
            }
        }

        public void Visit(LiteralExpression node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            ValidateIntegerLiteralSuffix(node, signedMinimumOperands.Contains(node));
            if (node.Token.Kind == TokenKind.StringLiteral) StringLiteralEncoding.Bytes(node.Token.Text[1..^1], node.Line);
            if (node.Token.Kind == TokenKind.CharLiteral) CharacterLiteralEncoding.Value(node.Token.Text, node.Line);
            TypeExpression type = node.Token.Kind switch
            {
                TokenKind.IntLiteral => Int,
                TokenKind.UIntLiteral => UInt,
                TokenKind.HexInt => node.Token.Text.EndsWith("ul", StringComparison.OrdinalIgnoreCase) || node.Token.Text.EndsWith("lu", StringComparison.OrdinalIgnoreCase) ? ULong :
                                    node.Token.Text.EndsWith("u", StringComparison.OrdinalIgnoreCase) ? UInt :
                                    node.Token.Text.EndsWith("l", StringComparison.OrdinalIgnoreCase) ? Long : Int,
                TokenKind.FloatLiteral => Float,
                TokenKind.DoubleLiteral => Double,
                TokenKind.LongLiteral => Long,
                TokenKind.ULongLiteral => ULong,
                TokenKind.StringLiteral => new PointerTypeExpression(Char, false, node.Line, isReadOnly: true),
                TokenKind.CharLiteral => Char,
                TokenKind.True => Bool,
                TokenKind.False => Bool,
                TokenKind.Null => Null,
                TokenKind.InterpolatedStringSegment => new PointerTypeExpression(Char, false, node.Line, isReadOnly: true),
                _ => throw new TypeCheckException($"Unknown literal type {node.Token.Kind}", node.Line)
            };
            RecordType(node, type);
        }

        public void Visit(IdentifierExpression node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (CheckPropertyRead(node)) return;
            if (TryBindStaticStorage(node)) return;
            if (!TryLookupVariable(node.Name, out _) && TryBindFunctionValue(node)) return;
            TypeExpression type = LookupVariable(node.Name, node.Line);
            RecordType(node, NullableReadType(node, type));
            if (TryGetConstValueByName(node.Name, out ConstValue? cv) && cv != null)
            {
                _constValues[node] = cv;
                BindConstantDeclaration(node);
            }
        }

        private bool IsExpressionReadOnly(AstNode node)
        {
            if (ResolveAlias(GetType(node)).IsReadOnlyValue) return true;
            // A property produces the getter's result, not a view of receiver storage.
            if (_propertyReads.ContainsKey(node)) return false;
            if (node is IdentifierExpression ident)
            {
                if (IsConstVariable(ident.Name))
                {
                    return true;
                }
                if (ident.Name == "this")
                {
                    if (TryLookupVariable("this", out TypeExpression? thisType) && thisType != null)
                    {
                        TypeExpression resThis = ResolveAlias(thisType);
                        return (resThis is PointerTypeExpression p && p.IsReadOnly) || (resThis is ManagedTypeExpression m && m.IsReadOnly);
                    }
                }
                else
                {
                    bool isLocal = false;
                    foreach (Dictionary<string, TypeExpression> scope in _scopes)
                    {
                        if (scope.ContainsKey(ident.Name))
                        {
                            isLocal = true;
                            break;
                        }
                    }

                    if (!isLocal)
                    {
                        if ((_currentStruct != null && _currentStruct.FieldIndex(ident.Name) >= 0) ||
                            (_currentClass != null && _currentClass.FieldIndex(ident.Name) >= 0))
                        {
                            if (TryLookupVariable("this", out TypeExpression? thisType) && thisType != null)
                            {
                                TypeExpression resThis = ResolveAlias(thisType);
                                return (resThis is PointerTypeExpression p && p.IsReadOnly) || (resThis is ManagedTypeExpression m && m.IsReadOnly);
                            }
                        }
                    }
                }
                return false;
            }

            if (node is UnaryExpression deref && deref.Operator == TokenKind.Star)
            {
                TypeExpression opType = ResolveAlias(GetType(deref.Operand));
                return (opType is PointerTypeExpression p && p.IsReadOnly) || (opType is ManagedTypeExpression m && m.IsReadOnly);
            }

            if (node is IndexExpression idx)
            {
                TypeExpression targetType = ResolveAlias(GetType(idx.Target));
                return (targetType is PointerTypeExpression p && p.IsReadOnly) || (targetType is ManagedTypeExpression m && m.IsReadOnly);
            }

            if (node is MemberAccessExpression member)
            {
                TypeExpression objType = ResolveAlias(GetType(member.Object));
                if ((objType is PointerTypeExpression p && p.IsReadOnly) || (objType is ManagedTypeExpression m && m.IsReadOnly))
                {
                    return true;
                }

                TypeExpression unwrapped = objType;
                while (unwrapped is PointerTypeExpression ptr)
                {
                    unwrapped = ResolveAlias(ptr.Inner);
                }
                while (unwrapped is ManagedTypeExpression mgd)
                {
                    unwrapped = ResolveAlias(mgd.Inner);
                }

                if (unwrapped is NamedTypeExpression named)
                {
                    if (_structs.TryGetValue(named.Name, out StructInfo? sInfo) &&
                        sInfo.FieldDeclarationsByName.TryGetValue(member.Member, out FieldDeclaration? sField) &&
                        sField.IsReadOnly)
                    {
                        return true;
                    }
                    if (_classes.TryGetValue(named.Name, out ClassInfo? cInfo) &&
                        cInfo.FieldDeclarationsByName.TryGetValue(member.Member, out FieldDeclaration? cField) &&
                        cField.IsReadOnly)
                    {
                        return true;
                    }
                }

                return IsExpressionReadOnly(member.Object);
            }

            return false;
        }

        private bool IsManagedStorage(AstNode node) => node switch
        {
            UnaryExpression { Operator: TokenKind.Star } dereference => ResolveAlias(GetType(dereference.Operand)) is ManagedTypeExpression,
            MemberAccessExpression member => ResolveAlias(GetType(member.Object)) is ManagedTypeExpression ||
                (ResolveAlias(GetType(member.Object)) is not PointerTypeExpression && IsManagedStorage(member.Object)),
            IndexExpression index => IsManagedStorage(index.Target),
            _ => false
        };

        private void CheckAssignmentTarget(AstNode target, int line)
        {
            CheckAssignmentTargetCore(target, line);
            if (ResolveAlias(GetType(target)).IsReadOnlyValue && !CanInitializeReadOnlyField(target))
                throw new TypeCheckException("Cannot assign to readonly storage", line);
        }

        private void CheckAssignmentTargetCore(AstNode target, int line)
        {
            if (GetStaticStorage(target) is { } storage)
            {
                if ((storage.IsConst || storage.IsReadOnly) && !CanInitializeReadOnlyField(target)) throw new TypeCheckException("Cannot assign to readonly static or global storage", line);
                return;
            }
            if (IsPropertyValueStorage(target))
                throw new TypeCheckException("Cannot modify a value returned by a property", line);
            if (target is UnaryExpression deref && deref.Operator == TokenKind.Star)
            {
                TypeExpression opType = ResolveAlias(GetType(deref.Operand));
                if (opType is PointerTypeExpression { IsReadOnly: true } or ManagedTypeExpression { IsReadOnly: true })
                {
                    throw new TypeCheckException("Cannot assign to dereference of readonly pointer", line);
                }
                return;
            }

            if (target is IndexExpression idx)
            {
                TypeExpression targetType = ResolveAlias(GetType(idx.Target));
                if (targetType is PointerTypeExpression { IsReadOnly: true })
                {
                    throw new TypeCheckException("Cannot assign to dereference of readonly pointer", line);
                }
                return;
            }

            if (target is MemberAccessExpression memberAccess)
            {
                TypeExpression rawObjType = ResolveAlias(GetType(memberAccess.Object));
                if ((rawObjType is PointerTypeExpression p && p.IsReadOnly) ||
                    (rawObjType is ManagedTypeExpression m && m.IsReadOnly) ||
                    IsExpressionReadOnly(memberAccess.Object))
                {
                    throw new TypeCheckException($"Cannot assign to field '{memberAccess.Member}' on readonly instance", line);
                }

                TypeExpression unwrapped = rawObjType;
                while (unwrapped is PointerTypeExpression ptr)
                {
                    unwrapped = ResolveAlias(ptr.Inner);
                }
                while (unwrapped is ManagedTypeExpression mgd)
                {
                    unwrapped = ResolveAlias(mgd.Inner);
                }

                if (unwrapped is NamedTypeExpression named)
                {
                    if (_structs.TryGetValue(named.Name, out StructInfo? sInfo))
                    {
                        if (sInfo.FieldDeclarationsByName.TryGetValue(memberAccess.Member, out FieldDeclaration? sField))
                        {
                            if (sField.IsConst)
                            {
                                throw new TypeCheckException($"Cannot assign to const field '{memberAccess.Member}'", line);
                            }
                            if (sField.IsReadOnly)
                            {
                                bool allowedInCtor = _currentConstructor != null &&
                                                     memberAccess.Object is IdentifierExpression { Name: "this" } &&
                                                     _currentStruct != null &&
                                                     _currentStruct.Name == sInfo.Name;
                                if (!allowedInCtor)
                                {
                                    throw new TypeCheckException($"Cannot assign to readonly field '{memberAccess.Member}' outside constructor", line);
                                }
                            }
                        }
                    }
                    else if (_classes.TryGetValue(named.Name, out ClassInfo? cInfo))
                    {
                        if (cInfo.FieldDeclarationsByName.TryGetValue(memberAccess.Member, out FieldDeclaration? cField))
                        {
                            if (cField.IsConst)
                            {
                                throw new TypeCheckException($"Cannot assign to const field '{memberAccess.Member}'", line);
                            }
                            if (cField.IsReadOnly)
                            {
                                bool allowedInCtor = _currentConstructor != null &&
                                                     memberAccess.Object is IdentifierExpression { Name: "this" } &&
                                                     _currentClass != null &&
                                                     _currentClass.FieldDeclarations.Contains(cField);
                                if (!allowedInCtor)
                                {
                                    throw new TypeCheckException($"Cannot assign to readonly field '{memberAccess.Member}' outside constructor", line);
                                }
                            }
                        }
                    }
                }
                return;
            }

            if (target is IdentifierExpression ident)
            {
                if (IsConstVariable(ident.Name))
                {
                    throw new TypeCheckException($"Cannot assign to const variable '{ident.Name}'", line);
                }

                bool isLocal = false;
                foreach (Dictionary<string, TypeExpression> scope in _scopes)
                {
                    if (scope.ContainsKey(ident.Name))
                    {
                        isLocal = true;
                        break;
                    }
                }

                if (!isLocal)
                {
                    if (_currentStruct != null && _currentStruct.FieldIndex(ident.Name) >= 0)
                    {
                        if (TryLookupVariable("this", out TypeExpression? thisType) && thisType != null)
                        {
                            TypeExpression resThis = ResolveAlias(thisType);
                            if ((resThis is PointerTypeExpression p && p.IsReadOnly) || (resThis is ManagedTypeExpression m && m.IsReadOnly))
                            {
                                throw new TypeCheckException($"Cannot assign to field '{ident.Name}' on readonly instance", line);
                            }
                        }

                        if (_currentStruct.FieldDeclarationsByName.TryGetValue(ident.Name, out FieldDeclaration? sField))
                        {
                            if (sField.IsConst)
                            {
                                throw new TypeCheckException($"Cannot assign to const field '{ident.Name}'", line);
                            }
                            if (sField.IsReadOnly && _currentConstructor == null)
                            {
                                throw new TypeCheckException($"Cannot assign to readonly field '{ident.Name}' outside constructor", line);
                            }
                        }
                    }
                    else if (_currentClass != null && _currentClass.FieldIndex(ident.Name) >= 0)
                    {
                        if (TryLookupVariable("this", out TypeExpression? thisType) && thisType != null)
                        {
                            TypeExpression resThis = ResolveAlias(thisType);
                            if ((resThis is PointerTypeExpression p && p.IsReadOnly) || (resThis is ManagedTypeExpression m && m.IsReadOnly))
                            {
                                throw new TypeCheckException($"Cannot assign to field '{ident.Name}' on readonly instance", line);
                            }
                        }

                        if (_currentClass.FieldDeclarationsByName.TryGetValue(ident.Name, out FieldDeclaration? cField))
                        {
                            if (cField.IsConst)
                            {
                                throw new TypeCheckException($"Cannot assign to const field '{ident.Name}'", line);
                            }
                            if (cField.IsReadOnly)
                            {
                                if (_currentConstructor == null || !_currentClass.FieldDeclarations.Contains(cField))
                                {
                                    throw new TypeCheckException($"Cannot assign to readonly field '{ident.Name}' outside constructor", line);
                                }
                            }
                        }
                    }
                }
            }
        }

        public void Visit(AssignmentExpression node)
        {
            using var nullableWrite = NullableAssignmentResult(node);
            using var sourceContext = SourceContext.Enter(node.Span);
            if (CheckPropertyWrite(node, node.Target, node.Value, GetBinaryOperatorForCompound(node.Operator), false)) return;
            if (node.Target is NamespaceAccessExpression nsTarget)
            {
                if (nsTarget.Left is IdentifierExpression id && TryLookupVariable(id.Name, out _))
                {
                    throw new TypeCheckException($"Instance member '{nsTarget.Member}' must be accessed with '.', not '::'", node.Line);
                }
                if (nsTarget.Left is not IdentifierExpression && nsTarget.Left is not GlobalExpression && nsTarget.Left is not NamespaceAccessExpression)
                {
                    throw new TypeCheckException($"Instance member '{nsTarget.Member}' must be accessed with '.', not '::'", node.Line);
                }
            }

            if (node.Target is not (IdentifierExpression or NamespaceAccessExpression or MemberAccessExpression or UnaryExpression { Operator: TokenKind.Star } or IndexExpression))
                throw new TypeCheckException($"Invalid assignment target '{node.Target.GetType().Name}'", node.Line);

            if (node.Target is MemberAccessExpression { IsArrow: true })
                throw new TypeCheckException("Cannot assign using '->'. Use '.' for member access.", node.Line);

            CheckNullableStorage(node.Target);
            TypeExpression targetType = GetType(node.Target);
            CheckAssignmentTarget(node.Target, node.Line);

            node.Value.Accept(this);
            TypeExpression valueType = GetType(node.Value);
            if (RequiresCopy(targetType) && (node.Operator != TokenKind.Equals || !ValidateCopyUse(node.Value, targetType)))
                throw new TypeCheckException("Assignment requires a copyable value, a fresh value, or 'move value'", node.Line);

            if (node.Operator != TokenKind.Equals)
            {
                TokenKind? binOp = GetBinaryOperatorForCompound(node.Operator);
                if (binOp.HasValue && TryResolveBinaryOperatorForTypes(binOp.Value, targetType, valueType, node, out string opStruct, out OperatorDeclaration opDecl))
                {
                    ValidateOperatorAccess(opDecl, opStruct, node);
                    TypeExpression returnType = ResolveAlias(opDecl.ReturnType);
                    if (!IsAssignable(targetType, returnType))
                    {
                        throw new TypeCheckException(
                            $"Cannot assign result of operator '{opDecl.OperatorSymbol}' ('{TypeName(returnType)}') to '{TypeName(targetType)}'", node.Line);
                    }
                    _compoundOperatorTargets[node] = (opStruct, opDecl);
                    RecordType(node, targetType);
                    return;
                }
            }

            if (node.Operator is TokenKind.PlusEquals or TokenKind.MinusEquals)
            {
                var arithmeticType = node.Target is IdentifierExpression identifier ? NullableReadType(identifier, targetType) : targetType;
                if (IsValidPointerForArithmetic(arithmeticType, out string? errPtr) && IsInteger(valueType))
                {
                    if (errPtr != null)
                        throw new TypeCheckException(errPtr, node.Line);
                    RecordType(node, arithmeticType);
                    return;
                }
                if (errPtr != null)
                    throw new TypeCheckException(errPtr, node.Line);
            }

            if (GetBinaryOperatorForCompound(node.Operator) is { } builtinOperator &&
                !CheckBuiltinOperatorOperands(node, builtinOperator, targetType, valueType)) return;

            if (!IsAssignable(targetType, valueType, node.Value))
            {
                _diagnostics.Report(DiagnosticRules.GF1001_TypeMismatch, node.Value.Span, TypeName(valueType), TypeName(targetType));
                RecordType(node, Error);
                return;
            }

            RecordType(node, targetType);
        }

        public void Visit(DestructorCallExpression node)
        {
            using var nullableCall = NullableCall();
            using var sourceContext = SourceContext.Enter(node.Span);
            node.Target.Accept(this);
            TypeExpression receiver = ResolveAlias(GetType(node.Target));
            TypeExpression target = ResolveAlias(node.TargetType);
            ValidateTypeUsage(new PointerTypeExpression(target, false, node.Line), node.Line);
            if (IsError(receiver)) { RecordType(node, Void); return; }
            if (receiver is not PointerTypeExpression { IsNullable: false, IsReadOnly: false } pointer)
                throw new TypeCheckException("Explicit destructor calls require a non-null, mutable raw pointer", node.Line);
            if (target.IsReadOnlyValue || ResolveAlias(pointer.Inner).IsReadOnlyValue)
                throw new TypeCheckException("Cannot explicitly destroy a readonly object", node.Line);
            if (!TypesMatch(ResolveAlias(pointer.Inner), target))
                throw new TypeCheckException($"Destructor type '{DisplayTypeName(target)}' must match the pointed-to type '{DisplayTypeName(pointer.Inner)}'", node.Line);
            if (target is NamedTypeExpression { Name: "void" })
                throw new TypeCheckException("Explicit destructor calls require a concrete object type", node.Line);
            RecordType(node, Void);
        }

        public void Visit(CallExpression node)
        {
            if (precheckedForeachCalls.Remove(node)) return;
            using var nullableCall = NullableCall();
            using var sourceContext = SourceContext.Enter(node.Span);
            if (TryCheckLlvmCall(node)) return;
            if (TryCheckExtensionCall(node)) return;
            RecordExceptionCall(node);
            bool argumentsChecked = checkedExtensionArguments.Contains(node);
            void CheckArguments()
            {
                if (argumentsChecked) return;
                argumentsChecked = true;
                foreach (AstNode argument in node.Arguments) CheckNullableArgument(argument);
            }

            if (node.Callee is NamespaceAccessExpression && TryBindStaticStorage(node.Callee) &&
                ResolveAlias(GetType(node.Callee)) is FunctionPointerTypeExpression staticCallback)
            {
                CheckArguments();
                CheckIndirectCall(node, staticCallback);
                return;
            }

            // Check if callee is a local variable or struct member holding a function pointer
            if (!extensionTargets.ContainsKey(node) && node.Callee is IdentifierExpression identVar && TryLookupVariable(identVar.Name, out TypeExpression? varType))
            {
                varType = ResolveAlias(varType!);
                if (varType is FunctionPointerTypeExpression fnPtr)
                {
                    identVar.Accept(this);
                    CheckArguments();
                    CheckIndirectCall(node, (FunctionPointerTypeExpression)ResolveAlias(GetType(identVar)));
                    return;
                }
                identVar.Accept(this);
                throw new TypeCheckException($"Expression of type '{DisplayTypeName(GetType(identVar))}' is not callable", identVar.Span);
            }

            // Check if callee is an expression evaluating to a function pointer (e.g. member access, index, call)
            if (node.Callee is not IdentifierExpression and not NamespaceAccessExpression)
            {
                if (node.Callee is not MemberAccessExpression { IsArrow: true })
                {
                    node.Callee.Accept(this);
                    TypeExpression calleeType = ResolveAlias(GetType(node.Callee));
                    if (calleeType is FunctionPointerTypeExpression fnPtr &&
                        (node.Callee is not MemberAccessExpression || _propertyReads.ContainsKey(node.Callee)))
                    {
                        CheckArguments();
                        CheckIndirectCall(node, fnPtr);
                        return;
                    }
                }
            }

            CheckArguments();
            string? funcName = null;
            MethodDeclaration? method = null;
            ExternDeclaration? ext = null;

            if (node.Callee is MemberAccessExpression memberAccess)
            {
                if (memberAccess.IsArrow)
                {
                    if (memberAccess.Member == "free")
                    {
                        throw new TypeCheckException("The '->free()' syntax has been removed. Use 'delete ptr;' for objects allocated with 'new*', or 'free(ptr)' for raw malloc pointers.", node.Line);
                    }
                    throw new TypeCheckException("The '->' operator is no longer supported. Use '.' for member access.", node.Line);
                }

                EnumInfo? calleeEnum = ResolveEnum(memberAccess.Object);
                if (calleeEnum != null)
                {
                    throw new TypeCheckException($"Enum member '{DisplayName(calleeEnum.Name)}::{memberAccess.Member}' must be accessed with '::', not '.'", node.Line);
                }

                NamespaceScope? calleeNs = ResolveNamespace(memberAccess.Object);
                if (calleeNs != null)
                {
                    throw new TypeCheckException($"Namespace member '{memberAccess.Member}' must be accessed with '::', not '.'", node.Line);
                }

                if (memberAccess.Object is IdentifierExpression calleeId && !TryLookupVariable(calleeId.Name, out _))
                {
                    if (GetClass(calleeId.Name) != null || GetStruct(calleeId.Name) != null || IsInterface(calleeId.Name))
                    {
                        throw new TypeCheckException($"Static member '{calleeId.Name}::{memberAccess.Member}' must be accessed with '::', not '.'", node.Line);
                    }
                }

                // The callee visit above captured this receiver before arguments.
                TypeExpression rawObjType = GetType(memberAccess.Object);
                if (IsNullable(ResolveAlias(rawObjType)))
                    throw new TypeCheckException("Cast a nullable pointer to non-null before calling a method", node.Line);
                bool isReceiverReadOnly = (rawObjType is PointerTypeExpression pRec && pRec.IsReadOnly)
                                       || (rawObjType is ManagedTypeExpression mRec && mRec.IsReadOnly)
                                       || IsExpressionReadOnly(memberAccess.Object);

                TypeExpression objType = rawObjType;
                if (objType is PointerTypeExpression ptr)
                    objType = ptr.Inner;
                if (objType is ManagedTypeExpression mgd)
                    objType = mgd.Inner;

                objType = ResolveAlias(objType);

                if (objType is NamedTypeExpression namedIface && ResolveInterface(namedIface) is InterfaceInfo ifaceInfo)
                {
                    if (!ifaceInfo.MethodsByName.TryGetValue(memberAccess.Member, out MethodDeclaration? ifaceMethod))
                        throw new TypeCheckException($"Interface '{DisplayName(namedIface.Name)}' has no method '{memberAccess.Member}'", node.Span);

                    if (isReceiverReadOnly && !ifaceMethod.IsReadOnly)
                        throw new TypeCheckException($"Cannot call non-readonly method '{ifaceMethod.Name}' on readonly instance", node.Line);

                    if (!ifaceMethod.IsNoThrow)
                    {
                        CheckThrowingCall(node, $"{namedIface.Name}.{memberAccess.Member}");
                    }

                    int slotIdx = ifaceInfo.MethodIndices[memberAccess.Member];
                    _interfaceMethodCalls[node] = (ifaceInfo, slotIdx, ifaceMethod);
                    AddDefaultArguments(node.Arguments, ifaceMethod.Parameters, node);

                    if (node.Arguments.Count != ifaceMethod.Parameters.Count)
                        throw new TypeCheckException(
                            $"Method '{DisplayName(namedIface.Name)}.{memberAccess.Member}' expects {ifaceMethod.Parameters.Count} arguments but got {node.Arguments.Count}",
                            node.Line);

                    for (int i = 0; i < node.Arguments.Count; i++)
                    {
                        TypeExpression argType = GetType(node.Arguments[i]);
                        TypeExpression paramType = ifaceMethod.Parameters[i].Type;
                        if (!IsAssignable(paramType, argType, node.Arguments[i]))
                            throw new TypeCheckException(
                                $"Argument {i + 1} of '{DisplayName(namedIface.Name)}.{memberAccess.Member}': cannot pass '{DisplayTypeName(argType)}' as '{DisplayTypeName(paramType)}'",
                                node.Arguments[i].Span);

                        if (ifaceMethod.Parameters[i].IsConst)
                        {
                            if (!_constEvaluator.TryEvaluate(node.Arguments[i], out ConstValue? constVal, out string? err))
                            {
                                throw new TypeCheckException($"Argument {i + 1} for const parameter '{ifaceMethod.Parameters[i].Name}' must be a compile-time constant: {err}", node.Line);
                            }
                            _constValues[node.Arguments[i]] = constVal!;
                        }
                    }

                    RecordType(node, ifaceMethod.ReturnType);
                    return;
                }

                if (objType is not NamedTypeExpression named)
                    throw new TypeCheckException("Cannot call method on non-struct and non-class type", node.Line);

                if (_classes.TryGetValue(named.Name, out ClassInfo? classInfo))
                {
                    int fieldIdx = classInfo.FieldIndex(memberAccess.Member);
                    if (fieldIdx >= 0)
                    {
                        (string Name, TypeExpression Type, TokenKind Accessibility, string DeclaringClass) field = classInfo.Fields[fieldIdx];
                        if (field.Accessibility == TokenKind.Private && !CanAccessPrivate(_currentClass?.Name, field.DeclaringClass))
                            throw new TypeCheckException($"Cannot access private field '{memberAccess.Member}' of class '{DisplayName(field.DeclaringClass)}'", node.Span);
                        if (field.Accessibility == TokenKind.Protected && (_currentClass == null || !IsSubclassOf(_currentClass.Name, field.DeclaringClass)))
                            throw new TypeCheckException($"Cannot access protected field '{memberAccess.Member}' of class '{DisplayName(field.DeclaringClass)}'", node.Span);

                        TypeExpression fieldType = ResolveAlias(field.Type);
                        if (fieldType is FunctionPointerTypeExpression fnPtrField)
                        {
                            RecordType(memberAccess, fieldType);
                            CheckIndirectCall(node, fnPtrField);
                            return;
                        }
                    }

                    if (!classInfo.Methods.TryGetValue(memberAccess.Member, out (MethodDeclaration Method, string DeclaringClass) mEntry))
                        throw new TypeCheckException($"Class '{DisplayName(named.Name)}' has no method '{memberAccess.Member}'", node.Span);

                    method = SelectOverload(mEntry.Method, node);
                    if (isReceiverReadOnly && !method.IsReadOnly)
                        throw new TypeCheckException($"Cannot call non-readonly method '{method.Name}' on readonly instance", node.Line);
                    if (method.Accessibility == TokenKind.Private && !CanAccessPrivate(_currentClass?.Name, mEntry.DeclaringClass))
                        throw new TypeCheckException($"Cannot access private method '{memberAccess.Member}' of class '{DisplayName(mEntry.DeclaringClass)}'", node.Span);
                    if (method.Accessibility == TokenKind.Protected && (_currentClass == null || !IsSubclassOf(_currentClass.Name, mEntry.DeclaringClass)))
                        throw new TypeCheckException($"Cannot access protected method '{memberAccess.Member}' of class '{DisplayName(mEntry.DeclaringClass)}'", node.Span);

                    if (classInfo.VTableSlots.TryGetValue(memberAccess.Member, out int slotIndex))
                    {
                        _virtualMethodCalls[node] = (classInfo, slotIndex, method);
                    }

                    funcName = $"{classInfo.Name}.{memberAccess.Member}";
                    _resolvedCalls[node] = method;
                }
                else if (_structs.TryGetValue(named.Name, out StructInfo? sInfo))
                {
                    int fieldIdx = sInfo.FieldIndex(memberAccess.Member);
                    if (fieldIdx >= 0)
                    {
                        TypeExpression fieldType = ResolveAlias(sInfo.Fields[fieldIdx].Type);
                        if (fieldType is FunctionPointerTypeExpression fnPtrField)
                        {
                            RecordType(memberAccess, fieldType);
                            CheckIndirectCall(node, fnPtrField);
                            return;
                        }
                    }

                    if (!sInfo.Methods.TryGetValue(memberAccess.Member, out method))
                        throw new TypeCheckException($"'{DisplayName(named.Name)}' has no method '{memberAccess.Member}'", node.Span);

                    method = SelectOverload(method, node);
                    if (isReceiverReadOnly && !method.IsReadOnly)
                        throw new TypeCheckException($"Cannot call non-readonly method '{method.Name}' on readonly instance", node.Line);

                    funcName = $"{named.Name}.{memberAccess.Member}";
                    _resolvedCalls[node] = method;
                }
                else
                {
                    throw new TypeCheckException($"'{named.Name}' is not a struct or class", node.Line);
                }
            }
            else if (extensionTargets.TryGetValue(node, out var extensionTarget))
            {
                method = extensionTarget;
                funcName = method.Name;
            }
            else if (node.Callee is IdentifierExpression ident)
            {
                funcName = ident.Name;
                if (IsStaticFunctionContext &&
                    ((_currentClass?.Methods.TryGetValue(funcName, out var instance) == true && !instance.Method.IsStatic) ||
                     (_currentStruct?.Methods.TryGetValue(funcName, out var structInstance) == true && !structInstance.IsStatic)))
                    throw new TypeCheckException("Instance methods require an explicit receiver in a static method", node.Line);
                if (_currentClass != null && _currentClass.Methods.TryGetValue(funcName, out (MethodDeclaration Method, string DeclaringClass) mEntry))
                {
                    method = SelectOverload(mEntry.Method, node);
                    if (TryLookupVariable("this", out TypeExpression? thisType) && thisType != null)
                    {
                        TypeExpression resThisType = ResolveAlias(thisType);
                        if (((resThisType is PointerTypeExpression p && p.IsReadOnly) || (resThisType is ManagedTypeExpression m && m.IsReadOnly)) && !method.IsReadOnly)
                        {
                            throw new TypeCheckException($"Cannot call non-readonly method '{method.Name}' on readonly instance", node.Line);
                        }
                    }
                    if (_currentClass.VTableSlots.TryGetValue(funcName, out int slotIndex))
                    {
                        _virtualMethodCalls[node] = (_currentClass, slotIndex, method);
                    }
                    _resolvedCalls[node] = method;
                }
                else if (_currentStruct != null && _currentStruct.Methods.TryGetValue(funcName, out method))
                {
                    method = SelectOverload(method, node);
                    if (TryLookupVariable("this", out TypeExpression? thisType) && thisType != null)
                    {
                        TypeExpression resThisType = ResolveAlias(thisType);
                        if (((resThisType is PointerTypeExpression p && p.IsReadOnly) || (resThisType is ManagedTypeExpression m && m.IsReadOnly)) && !method.IsReadOnly)
                        {
                            throw new TypeCheckException($"Cannot call non-readonly method '{method.Name}' on readonly instance", node.Line);
                        }
                    }
                    _resolvedCalls[node] = method;
                }
                else if (ResolveGenericFunction(funcName) is MethodDeclaration genericMethod)
                {
                    List<TypeExpression> typeArgs = node.TypeArguments.Select(ResolveAlias).ToList();
                    if (typeArgs.Count == 0)
                    {
                        Dictionary<string, TypeExpression> inferred = new();
                        HashSet<string> genericParamNames = genericMethod.GenericParameters.Select(p => p.Name).ToHashSet();
                        for (int i = 0; i < Math.Min(node.Arguments.Count, genericMethod.Parameters.Count); i++)
                        {
                            InferTypeParameter(genericMethod.Parameters[i].Type, GetType(node.Arguments[i]), genericParamNames, inferred);
                        }
                        typeArgs = genericMethod.GenericParameters.Select(p => inferred.TryGetValue(p.Name, out TypeExpression? t) ? t : throw new TypeCheckException($"Could not infer type parameter '{p.Name}' for generic function '{funcName}'", node.Line)).ToList();
                    }
                    method = MonomorphizeFunction(genericMethod, typeArgs, node.Line);
                }
                else
                {
                    method = ResolveFunction(funcName);
                    if (method != null && node.TypeArguments.Count > 0)
                    {
                        throw new TypeCheckException($"Function '{funcName}' is not generic", node.Line);
                    }
                    ext = method == null ? ResolveExtern(funcName) : null;
                    if (ext != null && node.TypeArguments.Count > 0)
                    {
                        throw new TypeCheckException($"Extern function '{funcName}' is not generic", node.Line);
                    }
                }
            }
            else if (node.Callee is NamespaceAccessExpression nsAccess)
            {
                if (nsAccess.Left is IdentifierExpression id && TryLookupVariable(id.Name, out _))
                {
                    throw new TypeCheckException($"Instance member '{nsAccess.Member}' must be accessed with '.', not '::'", node.Line);
                }
                if (nsAccess.Left is not IdentifierExpression && nsAccess.Left is not GlobalExpression && nsAccess.Left is not NamespaceAccessExpression && nsAccess.Left is not TypeExpression)
                {
                    throw new TypeCheckException($"Instance member '{nsAccess.Member}' must be accessed with '.', not '::'", node.Line);
                }
                method = ResolveStaticMethod(nsAccess, node);
                funcName = nsAccess.Member;
                if (method == null)
                {
                    NamespaceScope? scope = ResolveNamespace(nsAccess.Left);
                    if (scope == null) throw new TypeCheckException($"Unknown namespace or type for '{nsAccess.Member}'", node.Line);
                    scope.Functions.TryGetValue(nsAccess.Member, out method);
                    method ??= scope.GenericFunctions.GetValueOrDefault(nsAccess.Member);
                    if (method == null) scope.Externs.TryGetValue(nsAccess.Member, out ext);
                }

            }
            else
            {
                throw new TypeCheckException($"Expression of type '{DisplayTypeName(GetType(node.Callee))}' is not callable", node.Callee.Span);
            }

            if (method != null && !extensionTargets.ContainsKey(node)) method = SelectOverload(method, node);

            if (method?.IsStatic == true && node.Callee is MemberAccessExpression)
                throw new TypeCheckException($"Static method '{method.Name}' must be accessed through its type with '::'", node.Line);

            if (ext != null)
            {
                ValidateExternAccess(ext, node.Span);
                _resolvedCalls[node] = ext;
                AddDefaultArguments(node.Arguments, ext.Parameters, node);
                if (node.Arguments.Count < ext.Parameters.Count || !ext.IsVariadic && node.Arguments.Count > ext.Parameters.Count)
                    throw new TypeCheckException($"Function '{funcName}' expects {ext.Parameters.Count} arguments but got {node.Arguments.Count}", node.Line);
                for (int i = 0; i < Math.Min(node.Arguments.Count, ext.Parameters.Count); i++)
                {
                    TypeExpression argType = GetType(node.Arguments[i]);
                    TypeExpression paramType = ext.Parameters[i].Type;
                    if (!IsAssignable(paramType, argType, node.Arguments[i]))
                        throw new TypeCheckException(
                            $"Argument {i + 1} of '{funcName}': cannot pass '{DisplayTypeName(argType)}' as '{DisplayTypeName(paramType)}'",
                            node.Arguments[i].Span);

                    if (ext.Parameters[i].IsConst)
                    {
                        if (!_constEvaluator.TryEvaluate(node.Arguments[i], out ConstValue? constVal, out string? err))
                        {
                            throw new TypeCheckException($"Argument {i + 1} for const parameter '{ext.Parameters[i].Name}' must be a compile-time constant: {err}", node.Line);
                        }
                        _constValues[node.Arguments[i]] = constVal!;
                    }
                }
                RecordType(node, ext.ReturnType);
                return;
            }

            if (method == null)
                throw new TypeCheckException($"Unknown function '{funcName}'", node.Line);

            if (method.IsGeneric)
            {
                var arguments = node.TypeArguments.Select(ResolveAlias).ToList();
                if (arguments.Count == 0)
                {
                    var inferred = new Dictionary<string, TypeExpression>();
                    var names = method.GenericParameters.Select(p => p.Name).ToHashSet();
                    for (int i = 0; i < Math.Min(node.Arguments.Count, method.Parameters.Count); i++)
                        InferTypeParameter(method.Parameters[i].Type, GetType(node.Arguments[i]), names, inferred);
                    arguments = method.GenericParameters.Select(p => inferred.TryGetValue(p.Name, out var type) ? type : throw new TypeCheckException($"Could not infer type parameter '{p.Name}'", node.Line)).ToList();
                }
                method = MonomorphizeFunction(method, arguments, node.Line);
            }
            _resolvedCalls[node] = method;
            AddDefaultArguments(node.Arguments, method.Parameters, node);

            if (!method.IsNoThrow)
            {
                CheckThrowingCall(node, funcName ?? method.Name);
            }

            if (node.Arguments.Count != method.Parameters.Count)
                throw new TypeCheckException(
                    $"Function '{funcName}' expects {method.Parameters.Count} arguments but got {node.Arguments.Count}",
                    node.Line);

            for (int i = 0; i < node.Arguments.Count; i++)
            {
                TypeExpression argType = GetType(node.Arguments[i]);
                TypeExpression paramType = method.Parameters[i].Type;
                if (!IsAssignable(paramType, argType, node.Arguments[i]))
                    throw new TypeCheckException(
                        $"Argument {i + 1} of '{funcName}': cannot pass '{DisplayTypeName(argType)}' as '{DisplayTypeName(paramType)}'",
                        node.Arguments[i].Span);

                if (method.Parameters[i].IsConst)
                {
                    if (!_constEvaluator.TryEvaluate(node.Arguments[i], out ConstValue? constVal, out string? err))
                    {
                        throw new TypeCheckException($"Argument {i + 1} for const parameter '{method.Parameters[i].Name}' must be a compile-time constant: {err}", node.Line);
                    }
                    _constValues[node.Arguments[i]] = constVal!;
                }
            }

            if (method.IsConst)
            {
                if (_constEvaluator.TryEvaluate(node, out ConstValue? constResult, out string? _))
                {
                    _constValues[node] = constResult!;
                }
            }

            RecordType(node, method.ReturnType);
        }

        private void CheckIndirectCall(CallExpression node, FunctionPointerTypeExpression fnPtr)
        {
            if (fnPtr.IsNullable)
                throw new TypeCheckException("Cast a nullable function pointer to non-null before calling", node.Line);
            _indirectCalls.Add(node);

            if (node.Arguments.Count != fnPtr.ParameterTypes.Count)
                throw new TypeCheckException(
                    $"Function pointer expects {fnPtr.ParameterTypes.Count} arguments but got {node.Arguments.Count}",
                    node.Line);

            for (int i = 0; i < node.Arguments.Count; i++)
            {
                TypeExpression argType = GetType(node.Arguments[i]);
                TypeExpression paramType = fnPtr.ParameterTypes[i];
                if (!IsAssignable(paramType, argType, node.Arguments[i]))
                    throw new TypeCheckException(
                        $"Argument {i + 1} of indirect call: cannot pass '{DisplayTypeName(argType)}' as '{DisplayTypeName(paramType)}'",
                        node.Arguments[i].Span);
            }

            RecordType(node, fnPtr.ReturnType);
        }

        private MethodDeclaration? ResolveFunction(string name)
        {
            NamespaceScope? scope = _currentNamespace;
            while (scope != null)
            {
                if (scope.Functions.TryGetValue(name, out MethodDeclaration? method))
                    return method;
                scope = scope.Parent;
            }

            MethodDeclaration? foundInUsing = null;
            foreach (string usingNs in CurrentUsings)
            {
                NamespaceScope? nsScope = ResolveNamespaceByName(usingNs);
                if (nsScope != null && nsScope.Functions.TryGetValue(name, out MethodDeclaration? candidate))
                {
                    if (foundInUsing != null && foundInUsing != candidate)
                    {
                        throw new TypeCheckException($"Call to function '{name}' is ambiguous between namespaces", 0);
                    }
                    foundInUsing = candidate;
                }
            }
            if (foundInUsing != null)
                return foundInUsing;

            return null;
        }

        private ExternDeclaration? ResolveExtern(string name)
        {
            NamespaceScope? scope = _currentNamespace;
            while (scope != null)
            {
                if (scope.Externs.TryGetValue(name, out ExternDeclaration? ext))
                    return ext;
                scope = scope.Parent;
            }

            foreach (string usingNs in CurrentUsings)
            {
                NamespaceScope? nsScope = ResolveNamespaceByName(usingNs);
                if (nsScope != null && nsScope.Externs.TryGetValue(name, out ExternDeclaration? candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        public void Visit(NamespaceAccessExpression node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (TryBindFunctionValue(node)) return;
            if (CheckPropertyRead(node)) return;
            if (TryBindStaticStorage(node)) return;
            if (node.Left is IdentifierExpression id && TryLookupVariable(id.Name, out _))
            {
                throw new TypeCheckException($"Instance member '{node.Member}' must be accessed with '.', not '::'", node.Line);
            }
            if (node.Left is not IdentifierExpression && node.Left is not GlobalExpression && node.Left is not NamespaceAccessExpression)
            {
                throw new TypeCheckException($"Instance member '{node.Member}' must be accessed with '.', not '::'", node.Line);
            }
            if (node.Left is IdentifierExpression typeId && !TryLookupVariable(typeId.Name, out _))
            {
                ClassInfo? cInfo = GetClass(typeId.Name);
                if (cInfo != null && (cInfo.FieldIndex(node.Member) >= 0 || cInfo.Methods.ContainsKey(node.Member)))
                {
                    throw new TypeCheckException($"Instance member '{node.Member}' must be accessed with '.', not '::'", node.Line);
                }
                StructInfo? sInfo = GetStruct(typeId.Name);
                if (sInfo != null && (sInfo.FieldIndex(node.Member) >= 0 || sInfo.Methods.ContainsKey(node.Member)))
                {
                    throw new TypeCheckException($"Instance member '{node.Member}' must be accessed with '.', not '::'", node.Line);
                }
            }

            EnumInfo? enumInfo = ResolveEnum(node.Left);
            if (enumInfo != null)
            {
                if (!enumInfo.Members.TryGetValue(node.Member, out long val))
                    throw new TypeCheckException($"Enum '{DisplayName(enumInfo.Name)}' does not contain member '{node.Member}'", node.Line);

                RecordType(node, NamedTypeExpression.Resolved(enumInfo.Name, node.Line));
                _resolvedEnumMembers[node] = (enumInfo, val);
                return;
            }

            // resolve the left side to a namespace scope
            NamespaceScope? scope = ResolveNamespace(node.Left);
            if (scope == null)
                throw new TypeCheckException($"Could not resolve namespace", node.Line);

            if (scope.Functions.TryGetValue(node.Member, out MethodDeclaration? method))
            {
                RecordType(node, method.ReturnType);
                return;
            }
            if (scope.Externs.TryGetValue(node.Member, out ExternDeclaration? ext))
            {
                ValidateExternAccess(ext, node.Span);
                RecordType(node, ext.ReturnType);
                return;
            }
            if (checkingDefaults.Count > 0 && scope.Fields.TryGetValue(node.Member, out var constant) &&
                EvaluateDefaultConstant(constant, out var constantValue))
            {
                RecordType(node, ResolveAlias(constant.Type));
                _constValues[node] = constantValue!;
                return;
            }
            throw new TypeCheckException($"'{node.Member}' not found in namespace", node.Line);
        }

        private NamespaceScope? ResolveNamespace(AstNode node)
        {
            if (node is GlobalExpression)
                return _globalScope;

            if (node is IdentifierExpression ident)
            {
                // look for child namespace relative to current
                NamespaceScope? scope = _currentNamespace;
                while (scope != null)
                {
                    if (scope.Children.TryGetValue(ident.Name, out NamespaceScope? child))
                        return child;
                    scope = scope.Parent;
                }
                if (_globalScope.Children.TryGetValue(ident.Name, out NamespaceScope? gChild))
                    return gChild;
                return null;
            }

            if (node is NamespaceAccessExpression access)
            {
                NamespaceScope? parent = ResolveNamespace(access.Left);
                if (parent == null) return null;
                parent.Children.TryGetValue(access.Member, out NamespaceScope? child);
                return child;
            }

            if (node is MemberAccessExpression accessDot)
            {
                NamespaceScope? parent = ResolveNamespace(accessDot.Object);
                if (parent == null) return null;
                parent.Children.TryGetValue(accessDot.Member, out NamespaceScope? child);
                return child;
            }

            return null;
        }

        public void Visit(MemberAccessExpression node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (CheckPropertyRead(node)) return;
            EnumInfo? enumInfo = ResolveEnum(node.Object);
            if (enumInfo != null)
            {
                throw new TypeCheckException($"Enum member '{DisplayName(enumInfo.Name)}::{node.Member}' must be accessed with '::', not '.'", node.Line);
            }

            NamespaceScope? ns = ResolveNamespace(node.Object);
            if (ns != null)
            {
                throw new TypeCheckException($"Namespace member '{node.Member}' must be accessed with '::', not '.'", node.Line);
            }

            if (node.Object is IdentifierExpression id && !TryLookupVariable(id.Name, out _))
            {
                if (GetClass(id.Name) != null || GetStruct(id.Name) != null || IsInterface(id.Name))
                {
                    throw new TypeCheckException($"Static member '{id.Name}::{node.Member}' must be accessed with '::', not '.'", node.Line);
                }
            }

            node.Object.Accept(this);
            TypeExpression objType = GetType(node.Object);
            if (IsNullable(ResolveAlias(objType)))
                throw new TypeCheckException("Cast a nullable pointer to non-null before accessing members", node.Line);

            if (node.IsArrow)
            {
                throw new TypeCheckException("The '->' operator is no longer supported. Use '.' for member access, 'delete ptr;' to deallocate, and 'ptr == null' to check for null.", node.Line);
            }

            // unwrap pointer if needed
            if (objType is PointerTypeExpression ptr)
                objType = ptr.Inner;
            if (objType is ManagedTypeExpression mgd)
                objType = mgd.Inner;

            objType = ResolveAlias(objType);

            if (objType is NamedTypeExpression namedIface && ResolveInterface(namedIface) is InterfaceInfo ifaceInfo)
            {
                if (ifaceInfo.MethodsByName.TryGetValue(node.Member, out MethodDeclaration? ifaceMethod))
                {
                    RecordType(node, ifaceMethod.ReturnType);
                    return;
                }
                throw new TypeCheckException($"Interface '{DisplayName(namedIface.Name)}' has no method '{node.Member}'", node.Span);
            }

            if (objType is not NamedTypeExpression named)
                throw new TypeCheckException("Member access on non-struct and non-class type", node.Line);

            if (_classes.TryGetValue(named.Name, out ClassInfo? classInfo))
            {
                int fIdx = classInfo.FieldIndex(node.Member);
                if (fIdx >= 0)
                {
                    (string Name, TypeExpression Type, TokenKind Accessibility, string DeclaringClass) field = classInfo.Fields[fIdx];
                    if (field.Accessibility == TokenKind.Private && !CanAccessPrivate(_currentClass?.Name, field.DeclaringClass))
                    {
                        throw new TypeCheckException($"Cannot access private field '{node.Member}' of class '{DisplayName(field.DeclaringClass)}'", node.Span);
                    }
                    if (field.Accessibility == TokenKind.Protected && (_currentClass == null || !IsSubclassOf(_currentClass.Name, field.DeclaringClass)))
                    {
                        throw new TypeCheckException($"Cannot access protected field '{node.Member}' of class '{DisplayName(field.DeclaringClass)}'", node.Span);
                    }
                    RecordType(node, field.Type);
                    return;
                }

                if (classInfo.Methods.TryGetValue(node.Member, out (MethodDeclaration Method, string DeclaringClass) mEntry))
                {
                    if (mEntry.Method.Accessibility == TokenKind.Private && !CanAccessPrivate(_currentClass?.Name, mEntry.DeclaringClass))
                    {
                        throw new TypeCheckException($"Cannot access private method '{node.Member}' of class '{DisplayName(mEntry.DeclaringClass)}'", node.Span);
                    }
                    if (mEntry.Method.Accessibility == TokenKind.Protected && (_currentClass == null || !IsSubclassOf(_currentClass.Name, mEntry.DeclaringClass)))
                    {
                        throw new TypeCheckException($"Cannot access protected method '{node.Member}' of class '{DisplayName(mEntry.DeclaringClass)}'", node.Span);
                    }
                    RecordType(node, mEntry.Method.ReturnType);
                    return;
                }

                throw new TypeCheckException($"Class '{DisplayName(named.Name)}' has no field or method '{node.Member}'", node.Line);
            }

            if (!_structs.TryGetValue(named.Name, out StructInfo? info))
                throw new TypeCheckException($"'{named.Name}' is not a struct or class", node.Line);

            int idx = info.FieldIndex(node.Member);
            if (idx >= 0)
            {
                RecordType(node, info.Fields[idx].Type);
                return;
            }

            if (info.Methods.TryGetValue(node.Member, out MethodDeclaration? method))
            {
                RecordType(node, method.ReturnType);
                return;
            }

            throw new TypeCheckException($"'{DisplayName(named.Name)}' has no field or method '{node.Member}'", node.Line);
        }

        public void Visit(InterpolatedStringExpression node)
        {
            using var nullableCall = NullableCall();
            CheckInterpolation(node);
        }

        private readonly Dictionary<NewExpression, ThrowStatement> _allocationFailures = new();
        public ThrowStatement? GetAllocationFailure(NewExpression node) => _allocationFailures.GetValueOrDefault(node);

        private void CheckAllocationFailure(NewExpression node)
        {
            if (node.Kind != AllocationKind.Pointer || node.Destination != null || _allocationFailures.ContainsKey(node)) return;
            RequireMemoryFunction("Allocate", node);
            RequireMemoryFunction("Free", node);
            if (GetClass("std::BadAllocException") == null)
                throw new TypeCheckException("new* requires std::BadAllocException; a custom Exception root must provide the standard (message, code) constructor or define its own std::BadAllocException", node.Span);
            var value = new NewExpression(new NamedTypeExpression("BadAllocException", "std", node.Line), [], AllocationKind.Value, node.Line) { Span = node.Span };
            var failure = new ThrowStatement(value, node.Line) { Span = node.Span };
            failure.Accept(this);
            _allocationFailures[node] = failure;
        }

        public void Visit(NewExpression node)
        {
            using var nullableCall = NullableCall();
            using var sourceContext = SourceContext.Enter(node.Span);
            TypeExpression resolvedType = ResolveAlias(node.Type);
            ValidateArrayValueShape(resolvedType, node.Line);
            node.Type = resolvedType;
            if (node.Kind == AllocationKind.Managed)
            {
                ValidateTypeUsage(new ManagedTypeExpression(resolvedType, false, node.Line, collector: node.Collector), node.Line);
                CheckManagedAllocation(node);
            }
            if (resolvedType is NamedTypeExpression scalar && IsPrimitive(scalar.Name) && scalar.Name != "void")
            {
                if (node.Arguments.Count > 1)
                    throw new TypeCheckException("Scalar construction accepts zero or one initializer", node.Line);
                if (node.Arguments.Count == 1)
                {
                    CheckNullableArgument(node.Arguments[0]);
                    if (!IsAssignable(resolvedType, GetType(node.Arguments[0]), node.Arguments[0]))
                        throw new TypeCheckException("Invalid scalar initializer", node.Line);
                }
                CheckPlacementDestination(node, resolvedType);
                RecordType(node, node.Kind switch {
                    AllocationKind.Value => resolvedType,
                    AllocationKind.Pointer => new PointerTypeExpression(resolvedType, false, node.Line),
                    _ => new ManagedTypeExpression(resolvedType, false, node.Line, collector: node.Collector)
                });
                CheckAllocationFailure(node);
                return;
            }
            if (resolvedType is not NamedTypeExpression named)
            {
                ReportError(DiagnosticRules.GF1000_GeneralTypeError, node.Line, 0, $"Cannot instantiate non-struct and non-class type '{TypeName(node.Type)}'");
                RecordType(node, Error);
                return;
            }

            if (_classes.TryGetValue(named.Name, out ClassInfo? cInfo))
            {
                if (cInfo.IsStatic)
                    throw new TypeCheckException($"Cannot construct static class '{DisplayName(cInfo.Name)}'", node.Span);
                if (cInfo.IsAbstract)
                {
                    ReportError(DiagnosticRules.GF1000_GeneralTypeError, node.Line, 0, $"Cannot instantiate abstract class '{DisplayName(cInfo.Name)}'");
                    RecordType(node, Error);
                    return;
                }

                GetClassSize(cInfo);
                ConstructorDeclaration? matchedCtor = null;
                if (cInfo.Constructors.Count == 0)
                {
                    ValidateImplicitConstruction(cInfo.Name, node.Line);
                    foreach (AstNode arg in node.Arguments) { CheckNullableArgument(arg); }

                    if (node.Arguments.Count != 0)
                    {
                        throw new TypeCheckException($"Class '{DisplayName(cInfo.Name)}' does not define a constructor taking {node.Arguments.Count} arguments", node.Line);
                    }
                }
                else
                {
                    List<ConstructorDeclaration> matches = new();
                    foreach (ConstructorDeclaration ctor in cInfo.Constructors)
                    {
                        if (!AcceptsArgumentCount(ctor.Parameters, node.Arguments.Count))
                            continue;

                        bool matchesParams = true;
                        for (int i = 0; i < node.Arguments.Count; i++)
                        {
                            AstNode arg = node.Arguments[i];
                            TypeExpression paramType = ResolveAlias(ctor.Parameters[i].Type);
                            TypeExpression argType;
                            if (arg is DefaultExpression def && def.TargetType == null)
                            {
                                argType = new NamedTypeExpression("default", null, arg.Line);
                            }
                            else
                            {
                                if (!_types.ContainsKey(arg))
                                    CheckNullableArgument(arg);

                                argType = GetType(arg);
                            }
                            if (!IsAssignable(paramType, argType, arg))
                            {
                                matchesParams = false;
                                break;
                            }
                        }

                        if (matchesParams)
                            matches.Add(ctor);
                    }

                    if (matches.Count == 0)
                    {
                        foreach (AstNode arg in node.Arguments)
                        {
                            if (!_types.ContainsKey(arg))
                                CheckNullableArgument(arg);

                        }
                        string argTypes = string.Join(", ", node.Arguments.Select(a => DisplayTypeName(GetType(a))));
                        throw new TypeCheckException($"No matching constructor found for '{DisplayName(cInfo.Name)}' with arguments ({argTypes})", node.Line);
                    }
                    matchedCtor = SelectBestOverload(matches, c => c.Parameters, node.Arguments, cInfo.Name, node.Line);
                    _resolvedConstructors[node] = matchedCtor;
                    AddDefaultArguments(node.Arguments, matchedCtor.Parameters, node);
                    if (!matchedCtor.IsNoThrow) { _throwingCalls.Add(node); RecordImplicitException(); }
                    if (matchedCtor.IsCopy && !CanAccessCopy(NamedTypeExpression.Resolved(cInfo.Name, node.Line), _currentClass?.Name ?? _currentStruct?.Name))
                        throw new TypeCheckException("Cannot access copy constructor", node.Line);
                    for (int i = 0; i < node.Arguments.Count; i++)
                    {
                        if (node.Arguments[i] is DefaultExpression def && def.TargetType == null)
                        {
                            RecordType(def, ResolveAlias(matchedCtor.Parameters[i].Type));
                        }
                    }
                }

                CheckPlacementDestination(node, resolvedType);
                TypeExpression resultType = node.Kind switch
                {
                    AllocationKind.Value => NamedTypeExpression.Resolved(cInfo.Name, node.Line),
                    AllocationKind.Pointer => new PointerTypeExpression(NamedTypeExpression.Resolved(cInfo.Name, node.Line), false, node.Line),
                    AllocationKind.Managed => new ManagedTypeExpression(NamedTypeExpression.Resolved(cInfo.Name, node.Line), false, node.Line, collector: node.Collector),
                    _ => throw new Exception($"Unknown allocation kind {node.Kind}")
                };

                RecordType(node, resultType);
                CheckAllocationFailure(node);
                return;
            }

            if (!_structs.TryGetValue(named.Name, out StructInfo? sInfo))
            {
                ReportError(DiagnosticRules.GF1004_TypeNotFound, node.Line, 0, named.Name);
                RecordType(node, Error);
                return;
            }

            GetStructSize(sInfo);
            ConstructorDeclaration? matchedStructCtor = null;

            if (sInfo.Constructors.Count == 0)
            {
                ValidateImplicitConstruction(sInfo.Name, node.Line);
                foreach (AstNode arg in node.Arguments) { CheckNullableArgument(arg); }

                if (node.Arguments.Count != 0)
                {
                    throw new TypeCheckException($"Struct '{DisplayName(sInfo.Name)}' does not define a constructor taking {node.Arguments.Count} arguments", node.Line);
                }
            }
            else
            {
                List<ConstructorDeclaration> matches = new();
                foreach (ConstructorDeclaration ctor in sInfo.Constructors)
                {
                    if (!AcceptsArgumentCount(ctor.Parameters, node.Arguments.Count))
                        continue;

                    bool matchesParams = true;
                    for (int i = 0; i < node.Arguments.Count; i++)
                    {
                        AstNode arg = node.Arguments[i];
                        TypeExpression paramType = ResolveAlias(ctor.Parameters[i].Type);
                        TypeExpression argType;
                        if (arg is DefaultExpression def && def.TargetType == null)
                        {
                            argType = new NamedTypeExpression("default", null, arg.Line);
                        }
                        else
                        {
                            if (!_types.ContainsKey(arg))
                                CheckNullableArgument(arg);

                            argType = GetType(arg);
                        }
                        if (!IsAssignable(paramType, argType, arg))
                        {
                            matchesParams = false;
                            break;
                        }
                    }

                    if (matchesParams)
                        matches.Add(ctor);
                }

                if (matches.Count == 0)
                {
                    foreach (AstNode arg in node.Arguments)
                    {
                        if (!_types.ContainsKey(arg))
                            CheckNullableArgument(arg);

                    }
                    string argTypes = string.Join(", ", node.Arguments.Select(a => DisplayTypeName(GetType(a))));
                    throw new TypeCheckException($"No matching constructor found for '{DisplayName(sInfo.Name)}' with arguments ({argTypes})", node.Line);
                }
                matchedStructCtor = SelectBestOverload(matches, c => c.Parameters, node.Arguments, sInfo.Name, node.Line);
                _resolvedConstructors[node] = matchedStructCtor;
                AddDefaultArguments(node.Arguments, matchedStructCtor.Parameters, node);
                if (!matchedStructCtor.IsNoThrow) { _throwingCalls.Add(node); RecordImplicitException(); }
                if (matchedStructCtor.IsCopy && !CanAccessCopy(NamedTypeExpression.Resolved(sInfo.Name, node.Line), _currentClass?.Name ?? _currentStruct?.Name))
                    throw new TypeCheckException("Cannot access copy constructor", node.Line);
                for (int i = 0; i < node.Arguments.Count; i++)
                {
                    if (node.Arguments[i] is DefaultExpression def && def.TargetType == null)
                    {
                        RecordType(def, ResolveAlias(matchedStructCtor.Parameters[i].Type));
                    }
                }
            }

            CheckPlacementDestination(node, resolvedType);
            TypeExpression structResultType = node.Kind switch
            {
                AllocationKind.Value => NamedTypeExpression.Resolved(sInfo.Name, node.Line),
                AllocationKind.Pointer => new PointerTypeExpression(NamedTypeExpression.Resolved(sInfo.Name, node.Line), false, node.Line),
                AllocationKind.Managed => new ManagedTypeExpression(NamedTypeExpression.Resolved(sInfo.Name, node.Line), false, node.Line, collector: node.Collector),
                _ => throw new Exception($"Unknown allocation kind {node.Kind}")
            };

            RecordType(node, structResultType);
            CheckAllocationFailure(node);
        }

        public void Visit(DefaultExpression node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (node.TargetType != null)
            {
                ValidateValueType(node.TargetType, node.Line);
                ValidateArrayValueShape(node.TargetType, node.Line);
                TypeExpression resolved = ResolveAlias(node.TargetType);
                if (!CanDefaultInitialize(resolved))
                {
                    throw new TypeCheckException($"Cannot get default value of type '{TypeName(node.TargetType)}': it requires explicit initialization (non-nullable fields or user constructors)", node.Line);
                }
                RecordType(node, resolved);
            }
            else
            {
                RecordType(node, new NamedTypeExpression("default", null, node.Line));
            }
        }

        public void Visit(SizeofExpression node) => VisitLayout(node, node.TargetType, false);
        public void Visit(AlignofExpression node) => VisitLayout(node, node.TargetType, true);

        private void VisitLayout(AstNode node, TypeExpression target, bool alignment)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            TypeExpression targetType = ResolveAlias(target);
            if (targetType is NamedTypeExpression named &&
                !IsPrimitive(named.Name) &&
                !_structs.ContainsKey(named.Name) &&
                !_classes.ContainsKey(named.Name) &&
                !_interfaces.ContainsKey(named.Name) &&
                ResolveEnum(named) == null &&
                TryLookupVariable(named.Name, out TypeExpression? varType) &&
                varType != null)
            {
                targetType = ResolveAlias(varType);
            }
            ValidateTypeUsage(targetType, node.Line);
            ValidateArrayValueShape(targetType, node.Line);
            int size = alignment ? GetTypeAlignment(targetType) : GetTypeSize(targetType);
            RecordType(node, Int);
            ConstValue.Integer constVal = new ConstValue.Integer(size);
            _constValues[node] = constVal;
        }

        public void Visit(NameofExpression node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            ValidateNameofSyntax(node.Target);
            BindNameof(node.Target, node.TypeBindings);
            string name = ExtractName(node.Target);
            TypeExpression strType = new PointerTypeExpression(Char, false, node.Line, isReadOnly: true);
            RecordType(node, strType);
            ConstValue.String constVal = new ConstValue.String(name);
            _constValues[node] = constVal;
        }
        public void Visit(NamedTypeExpression node) {
            using var sourceContext = SourceContext.Enter(node.Span); }
        public void Visit(NestedTypeExpression node) {
            using var sourceContext = SourceContext.Enter(node.Span); }
        public void Visit(PointerTypeExpression node) {
            using var sourceContext = SourceContext.Enter(node.Span); }
        public void Visit(ManagedTypeExpression node) => ValidateTypeUsage(node, node.Line);


        public void Visit(ArrayTypeExpression node) {
            using var sourceContext = SourceContext.Enter(node.Span); }
        public void Visit(ArrayLiteralExpression node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (node.Elements.Count == 0)
                throw new TypeCheckException("Array literals must contain at least one element", node.Line);
            TypeExpression? elementType = null;
            foreach (var element in node.Elements)
            {
                CheckNullableArgument(element);
                TypeExpression type = ResolveAlias(GetType(element));
                if (type is NamedTypeExpression { Name: "void" })
                    throw new TypeCheckException("Array elements cannot have type void", element.Line);
                elementType ??= type;
                if (TypeName(elementType) != TypeName(type))
                    throw new TypeCheckException("Array literal elements must have matching types and dimensions", element.Line);
                if (!ValidateCopyUse(element, type))
                    throw new TypeCheckException("Array literals cannot copy destructor-bearing elements; use fresh values", element.Line);
            }
            RecordType(node, new ArrayTypeExpression(elementType!, node.Elements.Count, node.Line));
        }
        public void Visit(IndexExpression node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (CheckPropertyRead(node)) return;
            if (node.Indices.Count != 1)
                throw new TypeCheckException("Built-in array and pointer indexing requires exactly one index", node.Line);
            node.Target.Accept(this);
            node.Index.Accept(this);

            TypeExpression targetType = GetType(node.Target);
            TypeExpression indexType = GetType(node.Index);

            if (!IsInteger(indexType))
                throw new TypeCheckException($"Array index must be an integer, got '{TypeName(indexType)}'", node.Line);

            if (targetType is ArrayTypeExpression arr)
            {
                RecordType(node, arr.ElementType);
            }
            else if (targetType is PointerTypeExpression ptr)
            {
                if (ResolveAlias(ptr.Inner) is NamedTypeExpression iface && IsInterface(iface))
                    throw new TypeCheckException($"Interface '{DisplayName(iface.Name)}' has no indexer", node.Line);
                if (ptr.IsNullable)
                    throw new TypeCheckException("Cast a nullable pointer to non-null before indexing", node.Line);
                ValidateValueType(ptr.Inner, node.Line);
                ValidateArrayValueShape(ptr.Inner, node.Line);
                RecordType(node, ptr.Inner);
            }
            else
            {
                throw new TypeCheckException($"Cannot index non-array and non-pointer type '{DisplayTypeName(targetType)}'", node.Line);
            }
        }
        public void Visit(BreakStatement node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (_loopDepth <= 0)
            {
                throw new TypeCheckException(_inDefer ? "Cannot break out of a defer statement" : "Cannot break outside of a loop", node.Line);
            }
        }

        public void Visit(ContinueStatement node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (_loopDepth <= 0)
            {
                throw new TypeCheckException(_inDefer ? "Cannot continue out of a defer statement" : "Cannot continue outside of a loop", node.Line);
            }
        }

        public sealed record EvaluatedAttribute(ClassInfo Type, ConstructorDeclaration? Constructor, ConstValue.ClassInstance Value);
        private readonly Dictionary<AttributeNode, EvaluatedAttribute> _evaluatedAttributes = new();
        public IReadOnlyList<EvaluatedAttribute> GetAttributes(AstNode declaration) =>
            declaration.Attributes.Select(a => _evaluatedAttributes[a]).ToList();

        private void CheckDeclarationAttributes(AstNode node)
        {
            using var context = SourceContext.Enter(node.Span);
            var previousNamespace = _currentNamespace;
            var previousClass = _currentClass;
            var previousStruct = _currentStruct;
            try
            {
                if (node is NamespaceDeclaration ns)
                {
                    _currentNamespace = _namespaceScopes[ns];
                    foreach (AstNode member in ns.Members.ToList()) CheckDeclarationAttributes(member);
                    return;
                }
                foreach (AttributeNode attribute in node.Attributes) attribute.Accept(this);
                IEnumerable<AstNode> members = Array.Empty<AstNode>();
                if (node is ClassDeclaration cls)
                {
                    _currentClass = GetClassByIdentity(cls.Name);
                    _currentStruct = null;
                    members = cls.Members;
                }
                else if (node is StructDeclaration str)
                {
                    _currentStruct = GetStructByIdentity(str.Name);
                    _currentClass = null;
                    members = str.Members;
                }
                else if (node is InterfaceDeclaration iface) members = iface.Members;
                foreach (AstNode member in members.ToList()) CheckDeclarationAttributes(member);
            }
            finally
            {
                _currentNamespace = previousNamespace;
                _currentClass = previousClass;
                _currentStruct = previousStruct;
            }
        }

        public void Visit(AttributeNode node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (_evaluatedAttributes.ContainsKey(node)) return;
            ClassInfo? info = null;
            int separator = node.Name.LastIndexOf("::", StringComparison.Ordinal);
            if (separator >= 0)
            {
                var scope = ResolveNamespaceByName(node.Name[..separator]);
                scope?.Classes.TryGetValue(node.Name[(separator + 2)..], out info);
            }
            else
            {
                for (var scope = _currentNamespace; scope != null && info == null; scope = scope.Parent)
                    scope.Classes.TryGetValue(node.Name, out info);
            }
            info ??= GetClass(node.Name);
            if (info == null || !IsSubclassOf(info.Name, "Attribute") || info.Name == "Attribute")
                throw new TypeCheckException($"Attribute '{node.Name}' must be a class derived from Attribute", node.Line);
            if (info.IsAbstract)
                throw new TypeCheckException($"Cannot instantiate abstract attribute '{node.Name}'", node.Line);
            if (HasAnyDestructor(info))
                throw new TypeCheckException($"Attribute '{node.Name}' cannot contain destructors", node.Line);
            var creation = new NewExpression(NamedTypeExpression.Resolved(info.Name, node.Line), node.Arguments, AllocationKind.Value, node.Line);
            creation.Accept(this);
            ConstructorDeclaration? ctor = GetResolvedConstructor(creation);
            string? accessor = _currentClass?.Name ?? _currentStruct?.Name;
            if (ctor != null && ctor.Accessibility != TokenKind.Public &&
                !CanAccessPrivate(accessor, info.Name) &&
                !(ctor.Accessibility == TokenKind.Protected && accessor != null && IsSubclassOf(accessor, info.Name)))
                throw new TypeCheckException($"Constructor of attribute '{node.Name}' is inaccessible", node.Line);
            if (!_constEvaluator.TryEvaluate(creation, out ConstValue? value, out string? error) || value is not ConstValue.ClassInstance instance)
                throw new TypeCheckException($"Attribute '{node.Name}' must be evaluable at compile time: {error}", node.Line);
            _evaluatedAttributes[node] = new EvaluatedAttribute(info, ctor, instance);
        }
        public void Visit(ExternDeclaration node)
        {
            if (node.Accessibility == TokenKind.Protected)
                throw new TypeCheckException("Namespace extern declarations cannot be protected", node.Span);
            using var sourceContext = SourceContext.Enter(node.Span);
            ValidateReturnType(node.ReturnType, node.Line);
            if (ContainsManagedReference(node.ReturnType) ||
                node.Parameters.Any(p => ContainsManagedReference(p.Type)))
                throw new TypeCheckException("Extern signatures cannot contain managed references or storage containing them; use a raw native boundary", node.Span);
            ValidateParameterDefaults(node, node.Parameters);
            foreach (var parameter in node.Parameters)
            {
                ValidateParameterType(parameter.Type, parameter.Line);
                if (HasDestructor(parameter.Type))
                    throw new TypeCheckException("Extern functions must borrow owning values through pointers", parameter.Line);
            }
            if (HasDestructor(node.ReturnType))
                throw new TypeCheckException("Extern functions cannot return owning values; use an explicit pointer contract", node.Line);
        }

        public static string TypeName(TypeExpression type) => TypeName(type, false);
        private static string TypeName(TypeExpression type, bool inheritedReadOnly) => type.IsReadOnlyValue && !inheritedReadOnly
            ? "readonly(" + TypeNameCore(type, true) + ")" : TypeNameCore(type, inheritedReadOnly);
        private static string TypeNameCore(TypeExpression type, bool inheritedReadOnly) => type switch
        {
            NamedTypeExpression n => n.Name,
            NestedTypeExpression nested => nested.TypeArguments.Count > 0
                ? $"{TypeName(nested.Parent)}::{nested.Member}<{string.Join(", ", nested.TypeArguments.Select(TypeName))}>"
                : $"{TypeName(nested.Parent)}::{nested.Member}",
            ReferenceTypeExpression r => "ref " + TypeName(r.Inner),
            PointerTypeExpression p => TypeName(p.Inner, inheritedReadOnly) + "*" + (p.IsNullable ? "?" : ""),
            ManagedTypeExpression m => (m.Collector == null ? TypeName(m.Inner, inheritedReadOnly) + "^" : "managed<" + TypeName(m.Inner, inheritedReadOnly) + ", " + TypeName(m.Collector) + ">") + (m.IsNullable ? "?" : ""),
            ArrayTypeExpression a => TypeName(a.ElementType, inheritedReadOnly) + (a.Size.HasValue ? $"[{a.Size}]" : "[]"),
            FunctionPointerTypeExpression f => $"{TypeName(f.ReturnType)}({string.Join(", ", f.ParameterTypes.Select(TypeName))}){(f.IsSignature ? "" : f.IsManaged ? "^" : "*")}{(f.IsNullable ? "?" : "")}",
            _ => "unknown"
        };

        public static string TypeNamePublic(TypeExpression type) => TypeName(type);

        public void Visit(GlobalExpression node) {
            using var sourceContext = SourceContext.Enter(node.Span); }
        public void Visit(FunctionPointerTypeExpression node) {
            using var sourceContext = SourceContext.Enter(node.Span); }
        public void Visit(AliasDeclaration node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (_localAliases.Count > 0)
                _localAliases.Peek()[node.Name] = node.TargetType;
            else if (_currentNamespace != null)
                _currentNamespace.Aliases[node.Name] = node.TargetType;
            else
                _globalScope.Aliases[node.Name] = node.TargetType;
        }

        private void RegisterEnum(EnumDeclaration enumDecl, NamespaceScope scope, string nsPath, bool namespaceVisible = true)
        {
            TypeExpression underlying = enumDecl.UnderlyingType != null
                ? ResolveAlias(enumDecl.UnderlyingType)
                : Int;

            if (!IsInteger(underlying))
            {
                throw new TypeCheckException(
                    $"Enum underlying type must be an integral type, but got '{TypeName(underlying)}'",
                    enumDecl.Line);
            }

            EnumInfo info = new EnumInfo
            {
                Name = enumDecl.Name,
                Namespace = nsPath,
                UnderlyingType = underlying,
                Accessibility = enumDecl.Accessibility
            };

            EnumConstantEvaluator.Populate(enumDecl, info, ResolveEnum);

            if (namespaceVisible) scope.Enums[enumDecl.SourceName] = info;
            if (namespaceVisible) scope.TypeNames[enumDecl.SourceName] = enumDecl.Name;
            _enums[enumDecl.Name] = info;
            if (nsPath.Length > 0)
            {
                _enums[$"{nsPath}::{enumDecl.SourceName}"] = info;
            }
        }

        public EnumInfo? ResolveEnum(AstNode node)
        {
            if (StaticOwnerType(node) is { } owner && ResolveAlias(owner) is NamedTypeExpression aliased &&
                ResolveEnum(aliased) is { } selected) return selected;
            if (node is IdentifierExpression ident)
            {
                foreach (Dictionary<string, EnumInfo> localScope in _localEnums)
                {
                    if (localScope.TryGetValue(ident.Name, out EnumInfo? localInfo))
                        return localInfo;
                }
                NamespaceScope? cur = _currentNamespace;
                while (cur != null)
                {
                    if (cur.Enums.TryGetValue(ident.Name, out EnumInfo? nsInfo))
                        return nsInfo;
                    cur = cur.Parent;
                }
                if (_globalScope.Enums.TryGetValue(ident.Name, out EnumInfo? gInfo))
                    return gInfo;
                if (_enums.TryGetValue(ResolveTypeIdentity(ident.Name, null) ?? ident.Name, out EnumInfo? fallback))
                    return fallback;
                return null;
            }

            if (node is NamespaceAccessExpression nsAccess)
            {
                NamespaceScope? scope = ResolveNamespace(nsAccess.Left);
                if (scope != null && scope.Enums.TryGetValue(nsAccess.Member, out EnumInfo? info))
                    return info;

                string fullPath = GetAccessPath(nsAccess);
                if (fullPath.Length > 0 && _enums.TryGetValue(fullPath, out EnumInfo? pathInfo))
                    return pathInfo;

                return null;
            }

            if (node is MemberAccessExpression memberAccess)
            {
                NamespaceScope? scope = ResolveNamespace(memberAccess.Object);
                if (scope != null && scope.Enums.TryGetValue(memberAccess.Member, out EnumInfo? info))
                    return info;

                string fullPath = GetAccessPath(memberAccess);
                if (fullPath.Length > 0 && _enums.TryGetValue(fullPath, out EnumInfo? pathInfo))
                    return pathInfo;

                return null;
            }

            return null;
        }

        public EnumInfo? ResolveEnum(NamedTypeExpression named)
        {
            if (named.Namespace != null)
            {
                NamespaceScope? ns = ResolveNamespaceByName(named.Namespace);
                if (ns != null && ns.Enums.TryGetValue(named.Name, out EnumInfo? info))
                    return info;

                if (_enums.TryGetValue($"{named.Namespace}::{named.Name}", out EnumInfo? namespacedInfo))
                    return namespacedInfo;
            }
            else
            {
                foreach (Dictionary<string, EnumInfo> localScope in _localEnums)
                {
                    if (localScope.TryGetValue(named.Name, out EnumInfo? info))
                        return info;
                }
                NamespaceScope? cur = _currentNamespace;
                while (cur != null)
                {
                    if (cur.Enums.TryGetValue(named.Name, out EnumInfo? info))
                        return info;
                    cur = cur.Parent;
                }
                if (_globalScope.Enums.TryGetValue(named.Name, out EnumInfo? gInfo))
                    return gInfo;
            }

            return _enums.TryGetValue(named.Name, out EnumInfo? fallback) ? fallback : null;
        }

        private string GetAccessPath(AstNode node)
        {
            if (node is IdentifierExpression ident)
                return ident.Name;
            if (node is NamespaceAccessExpression nsAccess)
            {
                string left = GetAccessPath(nsAccess.Left);
                return left.Length > 0 ? $"{left}::{nsAccess.Member}" : nsAccess.Member;
            }
            if (node is MemberAccessExpression memAccess)
            {
                string left = GetAccessPath(memAccess.Object);
                return left.Length > 0 ? $"{left}::{memAccess.Member}" : memAccess.Member;
            }
            return "";
        }

        public void Visit(EnumDeclaration node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (_scopes.Count > 0)
            {
                if (_localEnums.Peek().ContainsKey(node.SourceName))
                    throw new TypeCheckException($"Enum '{node.SourceName}' is already declared in this scope", node.Span);
                node.Name = $"local$enum${_localEnumCounter++}${node.SourceName}";
                _displayNames[node.Name] = node.SourceName;
                var local = new NamespaceScope();
                RegisterEnum(node, local, "");
                _localEnums.Peek()[node.SourceName] = local.Enums[node.SourceName];
                return;
            }
            NamespaceScope scope = _currentNamespace ?? _globalScope;
            if (!scope.Enums.ContainsKey(node.Name))
            {
                RegisterEnum(node, scope, "");
            }
        }

        public void Visit(EnumMemberDeclaration node) {
            using var sourceContext = SourceContext.Enter(node.Span); }

        public void Visit(CastExpression node)
        {
            using var nullableCast = NullableCastResult(node);
            using var sourceContext = SourceContext.Enter(node.Span);
            node.Operand.Accept(this);
            TypeExpression sourceType = ResolveAlias(GetType(node.Operand));
            TypeExpression targetType = ResolveAlias(node.TargetType);
            ValidateTypeUsage(targetType, node.Line);
            ValidateArrayValueShape(targetType, node.Line);

            if (targetType is PointerTypeExpression { IsNullable: false } or FunctionPointerTypeExpression { IsNullable: false } or ManagedTypeExpression { IsNullable: false })
            {
                if (sourceType is NamedTypeExpression { Name: "null" } ||
                    (_constEvaluator.TryEvaluate(node.Operand, out ConstValue? castConstant, out _) &&
                     castConstant != null && ConstValue.TryIntegerValue(castConstant, out var integerConstant) &&
                     integerConstant % (System.Numerics.BigInteger.One << TargetInfo.Default.PointerBits) == 0))
                    throw new TypeCheckException("Cannot cast null or zero to a non-null pointer", node.Line);
            }
            if (!IsValidCast(sourceType, targetType))
            {
                throw new TypeCheckException($"Cannot cast '{DisplayTypeName(sourceType)}' to '{DisplayTypeName(targetType)}'", node.Line);
            }
            if (!ValidateCopyUse(node.Operand, targetType))
                throw new TypeCheckException("Cast requires a copyable value, a fresh value, or 'move value'", node.Span);

            RecordType(node, node.TargetType);
        }

        private bool IsValidCast(TypeExpression src, TypeExpression dst)
        {
            src = ResolveAlias(src);
            dst = ResolveAlias(dst);
            if (src is ManagedTypeExpression sourceCollector && dst is ManagedTypeExpression targetCollector &&
                !TypesMatch(ResolveCollectorType(sourceCollector.Collector, sourceCollector.Line), ResolveCollectorType(targetCollector.Collector, targetCollector.Line))) return false;
            if (src is PointerTypeExpression nativeSource && dst is ManagedTypeExpression rawTarget &&
                (ResolveAlias(nativeSource.Inner) is not NamedTypeExpression nativeSourceInner || !IsInterface(nativeSourceInner)) &&
                (ResolveAlias(rawTarget.Inner) is not NamedTypeExpression rawInner || !IsInterface(rawInner)))
            {
                GetCollectorBinding(rawTarget);
                return true;
            }
            if (src is ManagedTypeExpression rawSource && dst is PointerTypeExpression nativeTarget &&
                (ResolveAlias(nativeTarget.Inner) is not NamedTypeExpression nativeTargetInner || !IsInterface(nativeTargetInner)) &&
                (ResolveAlias(rawSource.Inner) is not NamedTypeExpression rawSourceInner || !IsInterface(rawSourceInner))) return true;
            if (src.IsReadOnlyValue && !dst.IsReadOnlyValue && !CanCopyReadOnlyValue(src))
                return false;
            if (src is ManagedTypeExpression { IsReadOnly: true } && dst is ManagedTypeExpression { IsReadOnly: false })
                return false;

            if (TypesMatch(src, dst))
            {
                return true;
            }

            // Enums: treat as underlying type
            if (src is NamedTypeExpression nSrc && ResolveEnum(nSrc) is { } enumSrc)
            {
                return IsValidCast(enumSrc.UnderlyingType, dst);
            }
            if (dst is NamedTypeExpression nDst && ResolveEnum(nDst) is { } enumDst)
            {
                return IsValidCast(src, enumDst.UnderlyingType);
            }

            // Null to pointer / nullable
            if (src is NamedTypeExpression { Name: "null" } && (dst is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression))
            {
                return true;
            }

            // Numeric to numeric (integers, floats, double, char)
            if (IsNumeric(src) && IsNumeric(dst))
            {
                return true;
            }

            // Pointer to interface pointer
            if (dst is PointerTypeExpression pDst && ResolveAlias(pDst.Inner) is NamedTypeExpression dstNamed && ResolveInterface(dstNamed) is InterfaceInfo dstIface)
            {
                if (src is PointerTypeExpression pSrc && ResolveAlias(pSrc.Inner) is NamedTypeExpression srcNamed)
                {
                    if (srcNamed.Name == dstIface.Name)
                    {
                        return true;
                    }
                    if (_structs.TryGetValue(srcNamed.Name, out StructInfo? sInfo) && sInfo.Interfaces.Contains(dstIface.Name))
                    {
                        return true;
                    }
                    if (_classes.TryGetValue(srcNamed.Name, out ClassInfo? cInfo) && ClassImplementsInterface(cInfo, dstIface.Name))
                    {
                        return true;
                    }
                }
                return false;
            }
            if (src is PointerTypeExpression pSrcIface && ResolveAlias(pSrcIface.Inner) is NamedTypeExpression srcIfaceNamed && ResolveInterface(srcIfaceNamed) != null)
            {
                return false;
            }

            // Managed ref to interface managed ref
            if (dst is ManagedTypeExpression mDst && ResolveAlias(mDst.Inner) is NamedTypeExpression dstMngNamed && ResolveInterface(dstMngNamed) is InterfaceInfo dstMngIface)
            {
                if (src is ManagedTypeExpression mSrc && ResolveAlias(mSrc.Inner) is NamedTypeExpression srcMngNamed)
                {
                    if (srcMngNamed.Name == dstMngIface.Name)
                    {
                        return true;
                    }
                    if (_structs.TryGetValue(srcMngNamed.Name, out StructInfo? sInfo) && sInfo.Interfaces.Contains(dstMngIface.Name))
                    {
                        return true;
                    }
                    if (_classes.TryGetValue(srcMngNamed.Name, out ClassInfo? cInfo) && ClassImplementsInterface(cInfo, dstMngIface.Name))
                    {
                        return true;
                    }
                }
                return false;
            }
            if (src is ManagedTypeExpression mSrcIface && ResolveAlias(mSrcIface.Inner) is NamedTypeExpression srcMngIfaceNamed && ResolveInterface(srcMngIfaceNamed) != null)
            {
                return false;
            }

            // Pointer to pointer
            if (src is PointerTypeExpression && dst is PointerTypeExpression)
            {
                return true;
            }

            // Managed to managed
            if (src is ManagedTypeExpression sourceManaged && dst is ManagedTypeExpression targetManaged)
            {
                // An opaque managed address may be recovered as a concrete managed
                // type. The adoption boundary supplies the storage contract; raw
                // addresses still cannot be cast directly into managed references.
                if (ResolveAlias(sourceManaged.Inner) is NamedTypeExpression { Name: "void" } &&
                    ResolveAlias(targetManaged.Inner) is NamedTypeExpression concrete && !IsInterface(concrete))
                    return true;
                return IsAssignable(new ManagedTypeExpression(targetManaged.Inner, false, targetManaged.Line, targetManaged.IsReadOnly, targetManaged.Collector),
                    new ManagedTypeExpression(sourceManaged.Inner, false, sourceManaged.Line, sourceManaged.IsReadOnly, sourceManaged.Collector));
            }

            // Function pointer to function pointer or void*
            if (src is FunctionPointerTypeExpression && dst is FunctionPointerTypeExpression)
            {
                return true;
            }
            if (src is FunctionPointerTypeExpression && dst is PointerTypeExpression { Inner: NamedTypeExpression { Name: "void" } })
            {
                return true;
            }
            if (src is PointerTypeExpression { Inner: NamedTypeExpression { Name: "void" } } && dst is FunctionPointerTypeExpression)
            {
                return true;
            }

            // Pointer to integer (e.g. nint, nuint, long, ulong, int, uint)
            if ((src is PointerTypeExpression || src is FunctionPointerTypeExpression) && IsInteger(dst))
            {
                return true;
            }

            // Integer to pointer (e.g. (void*)addr, (int*)0)
            if (IsInteger(src) && (dst is PointerTypeExpression || dst is FunctionPointerTypeExpression))
            {
                return true;
            }

            // Array to pointer decay (e.g. (char*)arr)
            if (src is ArrayTypeExpression arr && dst is PointerTypeExpression ptr)
            {
                return IsValidCast(arr.ElementType, ptr.Inner);
            }

            return false;
        }

        public void Visit(LambdaExpression node)
        {
            if (node.Parameters.Any(p => p.IsExtensionReceiver))
                throw new TypeCheckException("Extension receivers are allowed only on namespace functions", node.Span);
            using var exceptionScope = IsolateExceptionHandlers();
            using var nullableScope = IsolateNullableFlow();
            using var sourceContext = SourceContext.Enter(node.Span);
            if (node.Parameters.Any(p => p.DefaultValue != null))
                throw new TypeCheckException("Lambda defaults are not supported; function pointers require every argument", node.Span);
            int prevLoopDepth = _loopDepth;
            bool previousDefer = _inDefer;
            int previousScopeCount = _scopes.Count;
            int previousBoundaryCount = _lambdaScopeBoundaries.Count;
            int previousStaticCount = _lambdaStaticStack.Count;
            int previousReturnCount = _actualReturnTypes.Count;
            AstNode? previousFunction = _currentFunction;
            _currentFunction = node;
            _loopDepth = 0;
            _inDefer = false;
            try
            {
                _lambdaScopeBoundaries.Push(_scopes.Count);
                _lambdaStaticStack.Push(node.IsStatic);
                _actualReturnTypes.Push(new List<TypeExpression>());

                PushScope();
                List<TypeExpression> paramTypes = new();
                foreach (Parameter p in node.Parameters)
                {
                    ValidateParameterType(p.Type, p.Line);
                    if (RequiresCopy(p.Type))
                        throw new TypeCheckException("Lambda parameters must borrow owning values through pointers", p.Line);
                    TypeExpression pType = ResolveAlias(p.Type);
                    paramTypes.Add(pType);
                    DeclareVariable(p.Name, ParameterValueType(pType), p.Line);
                    NullableParameter(p);
                }

                TypeExpression returnType;
                if (node.IsExpressionBody)
                {
                    node.Body.Accept(this);
                    returnType = ResolveAlias(GetType(node.Body));
                }
                else
                {
                    node.Body.Accept(this);
                    List<TypeExpression> returns = _actualReturnTypes.Peek();
                    if (returns.Count == 0)
                    {
                        returnType = Void;
                    }
                    else
                    {
                        returnType = ResolveAlias(returns[0]);
                        for (int i = 1; i < returns.Count; i++)
                        {
                            TypeExpression other = ResolveAlias(returns[i]);
                            if (!TypesMatch(returnType, other) && !IsAssignable(returnType, other))
                            {
                                throw new TypeCheckException($"Inconsistent return types in lambda: '{TypeName(returnType)}' and '{TypeName(other)}'", node.Line);
                            }
                        }
                    }
                }
                ValidateTypeUsage(returnType, node.Line);
                if (RequiresCopy(returnType))
                    throw new TypeCheckException("Owning lambda results are not yet supported; use a named function", node.Line);

                if (returnType is ReferenceTypeExpression)
                    throw new TypeCheckException("Reference-returning lambdas are not supported", node.Line);
                _lambdaReturnTypes[node] = returnType;
                FunctionPointerTypeExpression fnType = new FunctionPointerTypeExpression(returnType, paramTypes, isManaged: false, isNullable: false, node.Line);
                RecordType(node, fnType);
            }
            finally
            {
                while (_scopes.Count > previousScopeCount) PopScope();
                while (_actualReturnTypes.Count > previousReturnCount) _actualReturnTypes.Pop();
                while (_lambdaStaticStack.Count > previousStaticCount) _lambdaStaticStack.Pop();
                while (_lambdaScopeBoundaries.Count > previousBoundaryCount) _lambdaScopeBoundaries.Pop();
                _loopDepth = prevLoopDepth;
                _inDefer = previousDefer;
                _currentFunction = previousFunction;
            }
        }

        private static TokenKind? GetBinaryOperatorForCompound(TokenKind compoundOp) => compoundOp switch
        {
            TokenKind.PlusEquals => TokenKind.Plus,
            TokenKind.MinusEquals => TokenKind.Minus,
            TokenKind.StarEquals => TokenKind.Star,
            TokenKind.SlashEquals => TokenKind.Slash,
            TokenKind.PercentEquals => TokenKind.Percent,
            TokenKind.Ampersand => TokenKind.Ampersand,
            TokenKind.Pipe => TokenKind.Pipe,
            _ => null
        };

        private bool TryResolveBinaryOperatorForTypes(TokenKind opKind, TypeExpression left, TypeExpression right, AstNode use, out string structName, out OperatorDeclaration opDecl)
        {
            structName = "";
            opDecl = null!;

            TypeExpression resLeft = ResolveAlias(left);
            TypeExpression resRight = ResolveAlias(right);

            var owners = new List<StructInfo>();
            if (resLeft is NamedTypeExpression namedLeft && _structs.TryGetValue(namedLeft.Name, out StructInfo? leftInfo))
                owners.Add(leftInfo);
            if (resRight is NamedTypeExpression namedRight && _structs.TryGetValue(namedRight.Name, out StructInfo? rightInfo) &&
                !owners.Contains(rightInfo)) owners.Add(rightInfo);

            foreach (var owner in owners)
            {
                foreach (OperatorDeclaration op in owner.Operators)
                {
                    if (op.OperatorKind == opKind && op.Parameters.Count == 2)
                    {
                        TypeExpression p0 = ResolveAlias(op.Parameters[0].Type);
                        TypeExpression p1 = ResolveAlias(op.Parameters[1].Type);
                        if (TypesMatch(resLeft, p0) && TypesMatch(resRight, p1))
                        {
                            if (opDecl != null)
                                throw new TypeCheckException($"Operator '{OperatorText(opKind)}' is ambiguous between '{TypeName(new NamedTypeExpression(structName, null, use.Line))}' and '{TypeName(new NamedTypeExpression(owner.Name, null, use.Line))}'", use.Span);
                            structName = owner.Name;
                            opDecl = op;
                        }
                    }
                }
            }

            return opDecl != null;
        }

        private bool TryResolveUnaryOperatorForType(TokenKind opKind, TypeExpression operand, out string structName, out OperatorDeclaration opDecl)
        {
            structName = "";
            opDecl = null!;

            TypeExpression resOperand = ResolveAlias(operand);
            if (resOperand is NamedTypeExpression named && _structs.TryGetValue(named.Name, out StructInfo? info))
            {
                foreach (OperatorDeclaration op in info.Operators)
                {
                    if (op.OperatorKind == opKind && op.Parameters.Count == 1)
                    {
                        TypeExpression p0 = ResolveAlias(op.Parameters[0].Type);
                        if (TypesMatch(resOperand, p0))
                        {
                            structName = info.Name;
                            opDecl = op;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public void Visit(PrefixedStringLiteralExpression node)
        {
            using var nullableCall = NullableCall();
            using var sourceContext = SourceContext.Enter(node.Span);
            CheckStringLiteralCall(node);
        }

        private int _catchDepth;

        public void Visit(ThrowStatement node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (node.Expression == null)
            {
                if (_catchDepth == 0) throw new TypeCheckException("Rethrow is only valid inside a catch handler", node.Line);
                RecordImplicitException();
                return;
            }
            node.Expression.Accept(this);
            RequireMemoryFunction("Allocate", node);
            RequireMemoryFunction("Free", node);
            TypeExpression exprType = ResolveAlias(_types[node.Expression]);
            if (exprType is not NamedTypeExpression named || !TypeDerivesFromClass(named, "Exception"))
                throw new TypeCheckException("Throw requires an Exception-derived value; pointers cannot be thrown", node.Line);
            if (!ValidateCopyUse(node.Expression, exprType))
                throw new TypeCheckException("This exception cannot be copied; use 'throw move value' or construct a fresh exception", node.Line);

            bool caught = false;
            foreach (TryStatement tryStmt in _tryStack)
            {
                if (TryCatchesType(tryStmt, named.Name))
                {
                    caught = true;
                    break;
                }
            }

            if (!caught)
            {
                if (_currentFunction is MethodDeclaration method)
                {
                    if (!_directExceptions.TryGetValue(method, out var exceptions))
                        _directExceptions[method] = exceptions = new();
                    exceptions.Add(named.Name);
                    _exceptionSummaries = null;
                }
            }
        }

        public void Visit(TryStatement node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            _tryStack.Push(node);
            try
            {
                node.TryBlock.Accept(this);
            }
            finally
            {
                _tryStack.Pop();
            }

            var nullablePaths = new List<HashSet<NullableBinding>>();
            if (NullableFallsThrough(node.TryBlock)) nullablePaths.Add(new(nullableFacts));
            bool hasCatchAll = false;
            List<string> caughtTypeNames = new();

            foreach (CatchClause clause in node.CatchClauses)
            {
                if (hasCatchAll)
                {
                    throw new TypeCheckException("A catch-all clause must be the last catch clause", clause.Line);
                }

                if (clause.ExceptionType == null)
                {
                    hasCatchAll = true;
                }
                else
                {
                    TypeExpression resolved = ResolveAlias(clause.ExceptionType);
                    if (resolved is ReferenceTypeExpression reference) resolved = reference.Inner;
                    string? typeName = null;
                    if (resolved is NamedTypeExpression named)
                    {
                        typeName = named.Name;
                    }
                    else if (resolved is PointerTypeExpression ptr && ResolveAlias(ptr.Inner) is NamedTypeExpression ptrNamed)
                    {
                        typeName = ptrNamed.Name;
                    }
                    else if (resolved is ManagedTypeExpression mgd && ResolveAlias(mgd.Inner) is NamedTypeExpression mgdNamed)
                    {
                        typeName = mgdNamed.Name;
                    }

                    if (typeName == null || !TypeDerivesFromClass(NamedTypeExpression.Resolved(typeName, clause.Line), "Exception"))
                    {
                        throw new TypeCheckException($"Catch type '{TypeName(clause.ExceptionType)}' must be 'Exception' or a subclass of 'Exception'", clause.Line);
                    }

                    foreach (string prevCaught in caughtTypeNames)
                    {
                        if (prevCaught == "Exception" || (typeName != prevCaught && TypeDerivesFromClass(NamedTypeExpression.Resolved(typeName, clause.Line), prevCaught)) || typeName == prevCaught)
                        {
                            throw new TypeCheckException($"Catch clause for '{typeName}' is unreachable because a previous catch clause catches '{prevCaught}'", clause.Line);
                        }
                    }
                    caughtTypeNames.Add(typeName);
                }

                nullableFacts = new();
                clause.Accept(this);
                if (NullableFallsThrough(clause.Body)) nullablePaths.Add(new(nullableFacts));
            }
            nullableFacts = IntersectNullable(nullablePaths);
        }

        public void Visit(CatchClause node)
        {
            using var sourceContext = SourceContext.Enter(node.Span);
            if (node.ExceptionType != null && ResolveAlias(node.ExceptionType) is not (PointerTypeExpression or ReferenceTypeExpression))
                throw new TypeCheckException("Catch variables must use ref Error or Error* to borrow the exception", node.Line);
            if (node.ExceptionType != null && ResolveAlias(node.ExceptionType) is ManagedTypeExpression)
                throw new TypeCheckException("Managed exception catches are not supported by the current exception ABI", node.Line);
            PushScope();
            try
            {
                if (node.VariableName != null && node.ExceptionType != null)
                {
                    DeclareVariable(node.VariableName, ParameterValueType(node.ExceptionType), node.Line);
                }
                _catchDepth++;
                try { node.Body.Accept(this); }
                finally { _catchDepth--; }
            }
            finally
            {
                PopScope();
            }
        }

        private void CheckThrowingCall(CallExpression node, string calleeName)
        {
            _throwingCalls.Add(node);

        }


        private bool TryCatchesType(TryStatement tryStmt, string thrownTypeName)
        {
            foreach (CatchClause clause in tryStmt.CatchClauses)
            {
                if (clause.ExceptionType == null)
                {
                    return true;
                }

                TypeExpression resolved = ResolveAlias(clause.ExceptionType);
                    if (resolved is ReferenceTypeExpression reference) resolved = reference.Inner;
                string? catchName = null;
                if (resolved is NamedTypeExpression named)
                {
                    catchName = named.Name;
                }
                else if (resolved is PointerTypeExpression ptr && ResolveAlias(ptr.Inner) is NamedTypeExpression ptrNamed)
                {
                    catchName = ptrNamed.Name;
                }
                else if (resolved is ManagedTypeExpression mgd && ResolveAlias(mgd.Inner) is NamedTypeExpression mgdNamed)
                {
                    catchName = mgdNamed.Name;
                }

                if (catchName == "Exception" || catchName == thrownTypeName || TypeDerivesFromClass(NamedTypeExpression.Resolved(thrownTypeName, 0), catchName!))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
