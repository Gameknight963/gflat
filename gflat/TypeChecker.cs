using gflat.ast;
using gflat.CompileExceptions;
using gflat.comptime;
using gflat.symbols;
using gflat.semantics;
using gflat.diagnostics;

namespace gflat
{
    public class TypeChecker : IVisitor, IConstEvaluationContext
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

        public TypeChecker(DiagnosticBag? diagnostics = null)
        {
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
        }

        public bool TryGetConstValueByName(string name, out ConstValue? value) =>
            _constVariablesByName.TryGetValue(name, out value);

        public bool TryGetConstValue(AstNode node, out ConstValue? value) =>
            _constValues.TryGetValue(node, out value);

        public bool IsConstVariable(string name) =>
            _constVariableNames.Contains(name);

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
        private readonly Dictionary<UnaryExpression, AstNode> _functionAddressTargets = new();
        private readonly HashSet<CallExpression> _indirectCalls = new();
        private readonly Stack<Dictionary<string, TypeExpression>> _localAliases = new();
        private readonly Stack<Dictionary<string, EnumInfo>> _localEnums = new();
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

        public bool CanFunctionThrow(AstNode func) => func is MethodDeclaration m && m.Throws;
        public bool CanCallThrow(AstNode call) => _throwingCalls.Contains(call);
        public IEnumerable<ClassInfo> GetAllClasses() => _classes.Values;

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
            _resolvedCalls.TryGetValue(call, out AstNode? target) ? target : null;

        public AstNode? GetFunctionAddressTarget(UnaryExpression unary) =>
            _functionAddressTargets.TryGetValue(unary, out AstNode? target) ? target : null;

        public bool IsIndirectCall(CallExpression call) => _indirectCalls.Contains(call);

        public bool TryGetInterfaceCall(CallExpression node, out (InterfaceInfo Interface, int SlotIndex, MethodDeclaration Method) call) =>
            _interfaceMethodCalls.TryGetValue(node, out call);

        public InterfaceInfo? GetInterface(string name) =>
            _interfaces.TryGetValue(name, out InterfaceInfo? info) ? info : null;

        public bool IsInterface(string name) => _interfaces.ContainsKey(name);

        public StructInfo? GetStruct(string name)
        {
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
            public string Name = "";
            public string Namespace = "";
            public List<MethodDeclaration> Methods = new();
            public Dictionary<string, MethodDeclaration> MethodsByName = new();
            public Dictionary<string, int> MethodIndices = new();
            public TokenKind Accessibility = TokenKind.Public;
        }

        public class ClassInfo
        {
            public string Name = "";
            public string Namespace = "";
            public string? BaseClass = null;
            public bool IsAbstract = false;
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
            public int FieldIndex(string name) => Fields.FindIndex(f => f.Name == name);
        }

        private readonly Dictionary<string, ClassInfo> _classes = new();
        public ClassInfo? GetClass(string name)
        {
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
            if (c.Destructor != null) return true;
            if (c.BaseClass != null && _classes.TryGetValue(c.BaseClass, out ClassInfo? baseInfo))
                return HasAnyDestructor(baseInfo);
            return false;
        }

        public bool HasDestructor(TypeExpression type)
        {
            return HasDestructorInternal(type, new HashSet<string>());
        }

        private bool HasDestructorInternal(TypeExpression type, HashSet<string> visited)
        {
            type = ResolveAlias(type);
            if (type is NamedTypeExpression named)
            {
                if (!visited.Add(named.Name))
                {
                    return false;
                }

                StructInfo? sInfo = GetStruct(named.Name);
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

                ClassInfo? cInfo = GetClass(named.Name);
                if (cInfo != null)
                {
                    if (HasAnyDestructor(cInfo))
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
            public Dictionary<string, ExternDeclaration> Externs = new();
            public Dictionary<string, TypeExpression> Aliases = new();
            public Dictionary<string, EnumInfo> Enums = new();
            public Dictionary<string, InterfaceInfo> Interfaces = new();
            public Dictionary<string, ClassInfo> Classes = new();
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
            return null;
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

        private void RecordType(AstNode node, TypeExpression type) => _types[node] = type;

        private void PushScope()
        {
            _scopes.Push(new Dictionary<string, TypeExpression>());
            _localAliases.Push(new Dictionary<string, TypeExpression>());
            _localEnums.Push(new Dictionary<string, EnumInfo>());
        }

        private void PopScope()
        {
            _scopes.Pop();
            _localAliases.Pop();
            _localEnums.Pop();
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

            if (_currentStruct != null)
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

            if (_currentClass != null)
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

            if (_currentStruct != null)
            {
                int idx = _currentStruct.FieldIndex(name);
                if (idx >= 0)
                {
                    type = _currentStruct.Fields[idx].Type;
                    return true;
                }
            }

            if (_currentClass != null)
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

        // leaving this commented till std::String is a thing
        //private static NamedTypeExpression String => new NamedTypeExpression("string", null, 0);
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
            type = ResolveAlias(type);
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
                return $"{GetTypeMangledName(mgd.Inner)}Ref";
            }
            if (type is ArrayTypeExpression arr)
            {
                return $"{GetTypeMangledName(arr.ElementType)}Arr";
            }
            return type.ToString() ?? "Unknown";
        }

        private bool TypeSatisfiesConstraint(TypeExpression argType, TypeExpression constraint)
        {
            argType = ResolveAlias(argType);
            constraint = ResolveAlias(constraint);

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

        private NamedTypeExpression ResolveGenericType(string name, string? ns, List<TypeExpression> typeArgs, int line)
        {
            if (_genericStructs.TryGetValue(name, out StructDeclaration? genericStruct))
            {
                return MonomorphizeStruct(genericStruct, typeArgs, line);
            }
            if (_genericClasses.TryGetValue(name, out ClassDeclaration? genericClass))
            {
                return MonomorphizeClass(genericClass, typeArgs, line);
            }
            throw new TypeCheckException($"Unknown generic type '{name}'", line);
        }

        private NamedTypeExpression MonomorphizeStruct(StructDeclaration genericDef, List<TypeExpression> typeArgs, int line)
        {
            if (typeArgs.Count != genericDef.GenericParameters.Count)
            {
                throw new TypeCheckException($"Generic struct '{genericDef.Name}' expects {genericDef.GenericParameters.Count} type arguments, but got {typeArgs.Count}", line);
            }

            for (int i = 0; i < genericDef.GenericParameters.Count; i++)
            {
                GenericParameter param = genericDef.GenericParameters[i];
                TypeExpression arg = typeArgs[i];
                if (param.Constraint != null && !TypeSatisfiesConstraint(arg, param.Constraint))
                {
                    throw new TypeCheckException($"Type '{TypeName(arg)}' does not satisfy constraint '{TypeName(param.Constraint)}' for type parameter '{param.Name}' on struct '{genericDef.Name}'", line);
                }
            }

            string mangledName = $"{genericDef.Name}${string.Join("$", typeArgs.Select(GetTypeMangledName))}";
            if (_structs.ContainsKey(mangledName))
            {
                return new NamedTypeExpression(mangledName, null, line);
            }

            Dictionary<string, TypeExpression> typeMap = new();
            for (int i = 0; i < genericDef.GenericParameters.Count; i++)
            {
                typeMap[genericDef.GenericParameters[i].Name] = typeArgs[i];
            }

            AstCloner cloner = new AstCloner(typeMap, genericDef.Name, mangledName);
            StructDeclaration specialized = cloner.CloneStruct(genericDef);

            _compilationUnit?.Members.Add(specialized);
            RegisterMemberInScope(specialized, _globalScope, "");
            specialized.Accept(this);

            return new NamedTypeExpression(mangledName, null, line);
        }

        private NamedTypeExpression MonomorphizeClass(ClassDeclaration genericDef, List<TypeExpression> typeArgs, int line)
        {
            if (typeArgs.Count != genericDef.GenericParameters.Count)
            {
                throw new TypeCheckException($"Generic class '{genericDef.Name}' expects {genericDef.GenericParameters.Count} type arguments, but got {typeArgs.Count}", line);
            }

            for (int i = 0; i < genericDef.GenericParameters.Count; i++)
            {
                GenericParameter param = genericDef.GenericParameters[i];
                TypeExpression arg = typeArgs[i];
                if (param.Constraint != null && !TypeSatisfiesConstraint(arg, param.Constraint))
                {
                    throw new TypeCheckException($"Type '{TypeName(arg)}' does not satisfy constraint '{TypeName(param.Constraint)}' for type parameter '{param.Name}' on class '{genericDef.Name}'", line);
                }
            }

            string mangledName = $"{genericDef.Name}${string.Join("$", typeArgs.Select(GetTypeMangledName))}";
            if (_classes.ContainsKey(mangledName))
            {
                return new NamedTypeExpression(mangledName, null, line);
            }

            Dictionary<string, TypeExpression> typeMap = new();
            for (int i = 0; i < genericDef.GenericParameters.Count; i++)
            {
                typeMap[genericDef.GenericParameters[i].Name] = typeArgs[i];
            }

            AstCloner cloner = new AstCloner(typeMap, genericDef.Name, mangledName);
            ClassDeclaration specialized = cloner.CloneClass(genericDef);

            _compilationUnit?.Members.Add(specialized);
            RegisterMemberInScope(specialized, _globalScope, "");
            if (_classes.TryGetValue(specialized.Name, out ClassInfo? clsInfo))
            {
                new HierarchyResolutionPass(_symbols).ResolveHierarchy(clsInfo);
            }
            foreach (AstNode m in specialized.Members)
            {
                if (m is ClassDeclaration nestedCls && _classes.TryGetValue(nestedCls.Name, out ClassInfo? nestedClsInfo))
                {
                    new HierarchyResolutionPass(_symbols).ResolveHierarchy(nestedClsInfo);
                }
            }
            specialized.Accept(this);

            return new NamedTypeExpression(mangledName, null, line);
        }

        private MethodDeclaration MonomorphizeFunction(MethodDeclaration genericDef, List<TypeExpression> typeArgs, int line)
        {
            if (typeArgs.Count != genericDef.GenericParameters.Count)
            {
                throw new TypeCheckException($"Generic function '{genericDef.Name}' expects {genericDef.GenericParameters.Count} type arguments, but got {typeArgs.Count}", line);
            }

            for (int i = 0; i < genericDef.GenericParameters.Count; i++)
            {
                GenericParameter param = genericDef.GenericParameters[i];
                TypeExpression arg = typeArgs[i];
                if (param.Constraint != null && !TypeSatisfiesConstraint(arg, param.Constraint))
                {
                    throw new TypeCheckException($"Type '{TypeName(arg)}' does not satisfy constraint '{TypeName(param.Constraint)}' for type parameter '{param.Name}' on function '{genericDef.Name}'", line);
                }
            }

            string mangledName = $"{genericDef.Name}${string.Join("$", typeArgs.Select(GetTypeMangledName))}";
            if (_globalScope.Functions.TryGetValue(mangledName, out MethodDeclaration? existing))
            {
                return existing;
            }

            Dictionary<string, TypeExpression> typeMap = new();
            for (int i = 0; i < genericDef.GenericParameters.Count; i++)
            {
                typeMap[genericDef.GenericParameters[i].Name] = typeArgs[i];
            }

            AstCloner cloner = new AstCloner(typeMap, genericDef.Name, mangledName);
            MethodDeclaration specialized = cloner.CloneMethod(genericDef);

            _compilationUnit?.Members.Add(specialized);
            RegisterMemberInScope(specialized, _globalScope, "");
            specialized.Accept(this);

            return specialized;
        }

        private bool InferTypeParameter(TypeExpression paramType, TypeExpression argType, ISet<string> genericParamNames, Dictionary<string, TypeExpression> inferred)
        {
            if (paramType is NamedTypeExpression namedParam && genericParamNames.Contains(namedParam.Name) && namedParam.TypeArguments.Count == 0)
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
                return InferTypeParameter(mgdParam.Inner, mgdArg.Inner, genericParamNames, inferred);
            }
            if (paramType is ArrayTypeExpression arrParam && argType is ArrayTypeExpression arrArg)
            {
                return InferTypeParameter(arrParam.ElementType, arrArg.ElementType, genericParamNames, inferred);
            }
            return false;
        }

        public TypeExpression ResolveAlias(TypeExpression type)
        {
            if (type is NestedTypeExpression nested)
            {
                TypeExpression resolvedParent = ResolveAlias(nested.Parent);
                if (resolvedParent is NamedTypeExpression namedParent)
                {
                    string parentNs = namedParent.Namespace != null ? $"{namedParent.Namespace}::{namedParent.Name}" : namedParent.Name;
                    NamespaceScope? ns = ResolveNamespaceByName(parentNs);
                    if (ns != null)
                    {
                        if (ns.Aliases.TryGetValue(nested.Member, out TypeExpression? target))
                        {
                            return ResolveAlias(target);
                        }

                        if (nested.TypeArguments.Count > 0)
                        {
                            List<TypeExpression> resolvedArgs = nested.TypeArguments.Select(ResolveAlias).ToList();
                            return ResolveGenericType(nested.Member, parentNs, resolvedArgs, nested.Line);
                        }
                        return ResolveAlias(new NamedTypeExpression(nested.Member, parentNs, nested.Line));
                    }

                    string candidate = $"{namedParent.Name}.{nested.Member}";
                    if (nested.TypeArguments.Count > 0)
                    {
                        List<TypeExpression> resolvedArgs = nested.TypeArguments.Select(ResolveAlias).ToList();
                        return ResolveGenericType(candidate, namedParent.Namespace, resolvedArgs, nested.Line);
                    }
                    return ResolveAlias(new NamedTypeExpression(candidate, namedParent.Namespace, nested.Line));
                }
                throw new TypeCheckException($"Cannot resolve nested type member '{nested.Member}' on non-named type '{TypeName(resolvedParent)}'", nested.Line);
            }
            if (type is NamedTypeExpression named)
            {
                if (named.TypeArguments.Count > 0)
                {
                    List<TypeExpression> resolvedArgs = named.TypeArguments.Select(ResolveAlias).ToList();
                    return ResolveGenericType(named.Name, named.Namespace, resolvedArgs, named.Line);
                }
                if (named.Namespace != null)
                {
                    NamespaceScope? ns = ResolveNamespaceByName(named.Namespace);
                    if (ns != null && ns.Aliases.TryGetValue(named.Name, out var target))
                        return ResolveAlias(target);
                }
                else
                {
                    foreach (var localScope in _localAliases)
                    {
                        if (localScope.TryGetValue(named.Name, out var target))
                            return ResolveAlias(target);
                    }
                    var cur = _currentNamespace;
                    while (cur != null)
                    {
                        if (cur.Aliases.TryGetValue(named.Name, out var target))
                            return ResolveAlias(target);
                        cur = cur.Parent;
                    }
                    if (_globalScope.Aliases.TryGetValue(named.Name, out var gTarget))
                        return ResolveAlias(gTarget);

                    string? resolvedNested = ResolveNestedTypeName(named.Name);
                    if (resolvedNested != null && resolvedNested != named.Name)
                    {
                        return new NamedTypeExpression(resolvedNested, named.Namespace, named.Line);
                    }
                }
            }
            else if (type is PointerTypeExpression ptr)
            {
                TypeExpression resolvedInner = ResolveAlias(ptr.Inner);
                if (resolvedInner != ptr.Inner)
                    return new PointerTypeExpression(resolvedInner, ptr.IsNullable, ptr.Line, isReadOnly: ptr.IsReadOnly);
            }
            else if (type is ManagedTypeExpression mgd)
            {
                TypeExpression resolvedInner = ResolveAlias(mgd.Inner);
                if (resolvedInner != mgd.Inner)
                    return new ManagedTypeExpression(resolvedInner, mgd.IsNullable, mgd.Line, isReadOnly: mgd.IsReadOnly);
            }
            else if (type is ArrayTypeExpression arr)
            {
                TypeExpression resolvedElem = ResolveAlias(arr.ElementType);
                int? size = arr.Size;
                if (!size.HasValue && arr.SizeExpression != null)
                {
                    if (_constEvaluator.TryEvaluate(arr.SizeExpression, out ConstValue? cv, out string? err))
                    {
                        if (cv is ConstValue.Integer ci)
                        {
                            size = (int)ci.Value;
                        }
                        else if (cv is ConstValue.UInteger cui)
                        {
                            size = (int)cui.Value;
                        }
                        else
                        {
                            throw new TypeCheckException($"Array size expression must evaluate to integer, got {cv}", arr.Line);
                        }
                    }
                    else
                    {
                        throw new TypeCheckException($"Array size must be a compile-time constant: {err}", arr.Line);
                    }
                }
                if (resolvedElem != arr.ElementType || size != arr.Size)
                    return new ArrayTypeExpression(resolvedElem, size, arr.Line, arr.SizeExpression);
            }
            else if (type is FunctionPointerTypeExpression fnPtr)
            {
                var resolvedRet = ResolveAlias(fnPtr.ReturnType);
                var resolvedParams = fnPtr.ParameterTypes.Select(ResolveAlias).ToList();
                return new FunctionPointerTypeExpression(resolvedRet, resolvedParams, fnPtr.IsManaged, fnPtr.IsNullable, fnPtr.Line);
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

        private void ValidateTypeUsage(TypeExpression type, int line)
        {
            TypeExpression resolved = ResolveAlias(type);
            if (resolved is ManagedTypeExpression)
            {
                throw new TypeCheckException("Managed pointers ('^') are not yet supported pending GC runtime integration.", line);
            }
            if (resolved is NamedTypeExpression named && IsInterface(named))
            {
                throw new TypeCheckException($"Cannot use interface '{named.Name}' as a value type. Interfaces must be used as pointers ('{named.Name}*')", line);
            }
            else if (resolved is ArrayTypeExpression arr)
            {
                ValidateTypeUsage(arr.ElementType, line);
            }
            else if (resolved is PointerTypeExpression ptr)
            {
                if (ResolveAlias(ptr.Inner) is ManagedTypeExpression)
                {
                    throw new TypeCheckException("Managed pointers ('^') are not yet supported pending GC runtime integration.", line);
                }
            }
        }

        private static int GetStringLiteralLength(string raw)
        {
            int length = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '\\' && i + 1 < raw.Length)
                {
                    i++;
                }
                length++;
            }
            return length + 1; // +1 for null terminator \0
        }

        public static bool IsPrimitive(string name) =>
            name is "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or
                    "long" or "ulong" or "nint" or "nuint" or "float" or "double" or
                    "bool" or "char" or "extralong" or "string" or "void";

        public int GetTypeAlignment(TypeExpression type)
        {
            type = ResolveAlias(type);
            if (type is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression)
            {
                return 8;
            }
            if (type is ArrayTypeExpression arr)
            {
                return GetTypeAlignment(arr.ElementType);
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
                    return 8;
                }
                StructInfo? structInfo = GetStruct(named.Name);
                if (structInfo != null)
                {
                    return GetStructAlignment(structInfo);
                }
                if (GetClass(named.Name) != null)
                {
                    return 8;
                }
                return named.Name switch
                {
                    "bool" or "byte" or "sbyte" or "char" => 1,
                    "short" or "ushort" => 2,
                    "int" or "uint" or "float" => 4,
                    "long" or "ulong" or "double" or "nint" or "nuint" => 8,
                    "extralong" => 16,
                    "void" => 1,
                    _ => 4
                };
            }
            return 8;
        }

        public int GetStructAlignment(StructInfo structInfo)
        {
            int maxAlign = 1;
            foreach ((string Name, TypeExpression Type) field in structInfo.Fields)
            {
                int align = GetTypeAlignment(field.Type);
                if (align > maxAlign)
                {
                    maxAlign = align;
                }
            }
            return maxAlign;
        }

        public int GetStructSize(StructInfo structInfo)
        {
            int offset = 0;
            int maxAlign = 1;
            foreach ((string Name, TypeExpression Type) field in structInfo.Fields)
            {
                int fieldSize = GetTypeSize(field.Type);
                int fieldAlign = GetTypeAlignment(field.Type);
                if (fieldAlign > maxAlign)
                {
                    maxAlign = fieldAlign;
                }
                offset = (offset + fieldAlign - 1) & ~(fieldAlign - 1);
                offset += fieldSize;
            }
            offset = (offset + maxAlign - 1) & ~(maxAlign - 1);
            return offset;
        }

        public int GetClassSize(ClassInfo classInfo)
        {
            int offset = 8; // vtable pointer
            int maxAlign = 8;
            foreach ((string Name, TypeExpression Type, TokenKind Accessibility, string DeclaringClass) field in classInfo.Fields)
            {
                int fieldSize = GetTypeSize(field.Type);
                int fieldAlign = GetTypeAlignment(field.Type);
                if (fieldAlign > maxAlign)
                {
                    maxAlign = fieldAlign;
                }
                offset = (offset + fieldAlign - 1) & ~(fieldAlign - 1);
                offset += fieldSize;
            }
            offset = (offset + maxAlign - 1) & ~(maxAlign - 1);
            return offset;
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
                return 8;
            }
            if (type is ManagedTypeExpression mgd)
            {
                TypeExpression inner = ResolveAlias(mgd.Inner);
                if (inner is NamedTypeExpression namedInner && IsInterface(namedInner))
                {
                    return 16;
                }
                return 8;
            }
            if (type is FunctionPointerTypeExpression)
            {
                return 8;
            }
            if (type is ArrayTypeExpression arr)
            {
                int elemSize = GetTypeSize(arr.ElementType);
                if (arr.Size.HasValue)
                {
                    return elemSize * arr.Size.Value;
                }
                return 8;
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
                StructInfo? structInfo = GetStruct(named.Name);
                if (structInfo != null)
                {
                    return GetStructSize(structInfo);
                }
                ClassInfo? classInfo = GetClass(named.Name);
                if (classInfo != null)
                {
                    return GetClassSize(classInfo);
                }
                return named.Name switch
                {
                    "bool" or "byte" or "sbyte" or "char" => 1,
                    "short" or "ushort" => 2,
                    "int" or "uint" or "float" => 4,
                    "long" or "ulong" or "double" or "nint" or "nuint" => 8,
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

        private void ValidateNameofTarget(AstNode target)
        {
            if (target is NamedTypeExpression named)
            {
                if (!IsPrimitive(named.Name) &&
                    !_structs.ContainsKey(named.Name) &&
                    !_classes.ContainsKey(named.Name) &&
                    !_interfaces.ContainsKey(named.Name) &&
                    ResolveEnum(named) == null &&
                    !TryLookupVariable(named.Name, out _) &&
                    ResolveFunction(named.Name) == null &&
                    ResolveGlobalField(named.Name) == null)
                {
                    throw new TypeCheckException($"The name '{named.Name}' does not exist in the current context", target.Line);
                }
                return;
            }
            if (target is IdentifierExpression ident)
            {
                if (!IsPrimitive(ident.Name) &&
                    !_structs.ContainsKey(ident.Name) &&
                    !_classes.ContainsKey(ident.Name) &&
                    !_interfaces.ContainsKey(ident.Name) &&
                    ResolveEnum(new NamedTypeExpression(ident.Name, null, target.Line)) == null &&
                    !TryLookupVariable(ident.Name, out _) &&
                    ResolveFunction(ident.Name) == null &&
                    ResolveGlobalField(ident.Name) == null)
                {
                    throw new TypeCheckException($"The name '{ident.Name}' does not exist in the current context", target.Line);
                }
                return;
            }
            if (target is MemberAccessExpression)
            {
                target.Accept(this);
                return;
            }
            if (target is NamespaceAccessExpression)
            {
                target.Accept(this);
                return;
            }
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

        private static bool TryGetIntegerConstant(AstNode node, out long value)
        {
            if (node is LiteralExpression lit)
            {
                if (lit.Token.Kind == TokenKind.IntLiteral)
                {
                    return long.TryParse(lit.Token.Text, out value);
                }
                if (lit.Token.Kind == TokenKind.UIntLiteral)
                {
                    string txt = lit.Token.Text.TrimEnd('u', 'U');
                    if (txt.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            value = Convert.ToInt64(txt[2..], 16);
                            return true;
                        }
                        catch
                        {
                            value = 0;
                            return false;
                        }
                    }
                    return long.TryParse(txt, out value);
                }
                if (lit.Token.Kind == TokenKind.HexInt)
                {
                    string txt = lit.Token.Text.TrimEnd('u', 'U', 'l', 'L');
                    try
                    {
                        value = Convert.ToInt64(txt, 16);
                        return true;
                    }
                    catch
                    {
                        value = 0;
                        return false;
                    }
                }
                if (lit.Token.Kind is TokenKind.LongLiteral or TokenKind.ULongLiteral)
                {
                    string txt = lit.Token.Text.TrimEnd('u', 'U', 'l', 'L');
                    if (txt.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            value = Convert.ToInt64(txt[2..], 16);
                            return true;
                        }
                        catch
                        {
                            value = 0;
                            return false;
                        }
                    }
                    return long.TryParse(txt, out value);
                }
            }
            else if (node is UnaryExpression unary)
            {
                if (unary.Operator == TokenKind.Minus)
                {
                    if (TryGetIntegerConstant(unary.Operand, out long innerVal))
                    {
                        value = -innerVal;
                        return true;
                    }
                }
                else if (unary.Operator == TokenKind.Plus)
                {
                    return TryGetIntegerConstant(unary.Operand, out value);
                }
            }

            value = 0;
            return false;
        }

        private static bool FitsInIntegerType(string typeName, long value) => typeName switch
        {
            "byte" => value >= byte.MinValue && value <= byte.MaxValue,
            "sbyte" => value >= sbyte.MinValue && value <= sbyte.MaxValue,
            "short" => value >= short.MinValue && value <= short.MaxValue,
            "ushort" => value >= ushort.MinValue && value <= ushort.MaxValue,
            "int" => value >= int.MinValue && value <= int.MaxValue,
            "uint" => value >= 0 && value <= uint.MaxValue,
            "long" => true,
            "ulong" => value >= 0,
            "nint" => true,
            "nuint" => value >= 0,
            "extralong" => true,
            _ => false
        };

        private bool IsValidPointerForArithmetic(TypeExpression type, out string? error)
        {
            type = ResolveAlias(type);
            if (type is ManagedTypeExpression)
            {
                error = "Pointer arithmetic is not allowed on managed pointers ('^')";
                return false;
            }
            if (type is PointerTypeExpression ptr)
            {
                if (ptr.Inner is NamedTypeExpression { Name: "void" })
                {
                    error = "Pointer arithmetic is not allowed on 'void*'";
                    return false;
                }
                error = null;
                return true;
            }
            if (type is FunctionPointerTypeExpression fnPtr)
            {
                if (fnPtr.IsManaged)
                {
                    error = "Pointer arithmetic is not allowed on managed function pointers ('^')";
                    return false;
                }
                error = null;
                return true;
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

            if (a is NamedTypeExpression na && b is NamedTypeExpression nb)
                return na.Name == nb.Name;
            if (a is PointerTypeExpression pa && b is PointerTypeExpression pb)
                return pa.IsNullable == pb.IsNullable && pa.IsReadOnly == pb.IsReadOnly && TypesMatchPublic(pa.Inner, pb.Inner, symbols);
            if (a is ManagedTypeExpression ma && b is ManagedTypeExpression mb)
                return ma.IsNullable == mb.IsNullable && ma.IsReadOnly == mb.IsReadOnly && TypesMatchPublic(ma.Inner, mb.Inner, symbols);
            if (a is ArrayTypeExpression aa && b is ArrayTypeExpression ab)
                return TypesMatchPublic(aa.ElementType, ab.ElementType, symbols) && (aa.Size == ab.Size || aa.Size == null || ab.Size == null);
            if (a is FunctionPointerTypeExpression fa && b is FunctionPointerTypeExpression fb)
            {
                if (fa.IsManaged != fb.IsManaged || fa.IsNullable != fb.IsNullable)
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

        private bool TypesMatch(TypeExpression a, TypeExpression b) => TypesMatchPublic(a, b, _symbols);

        private bool IsAssignable(TypeExpression target, TypeExpression source, AstNode? valueNode = null)
        {
            if (IsError(target) || IsError(source))
            {
                return true;
            }

            target = ResolveAlias(target);
            source = ResolveAlias(source);

            if (TypesMatch(target, source))
                return true;

            // Pointer to pointer assignability (handles readonly conversion: T* to readonly T*)
            if (target is PointerTypeExpression ptExact && source is PointerTypeExpression psExact)
            {
                if (psExact.IsReadOnly && !ptExact.IsReadOnly)
                    return false;
                if (psExact.IsNullable && !ptExact.IsNullable)
                    return false;
                if (TypesMatch(ptExact.Inner, psExact.Inner))
                    return true;
            }

            // Managed ref to managed ref assignability (handles readonly conversion: T^ to readonly T^)
            if (target is ManagedTypeExpression mtExact && source is ManagedTypeExpression msExact)
            {
                if (msExact.IsReadOnly && !mtExact.IsReadOnly)
                    return false;
                if (msExact.IsNullable && !mtExact.IsNullable)
                    return false;
                if (TypesMatch(mtExact.Inner, msExact.Inner))
                    return true;
            }

            // Integer constant literal in-range assignment
            if (valueNode != null && IsInteger(source) && target is NamedTypeExpression targetNamed && TryGetIntegerConstant(valueNode, out long constVal))
            {
                if (FitsInIntegerType(targetNamed.Name, constVal))
                    return true;
            }

            // Enum assignability with underlying type
            if (target is NamedTypeExpression nt && ResolveEnum(nt) is EnumInfo targetEnum)
            {
                if (IsAssignable(targetEnum.UnderlyingType, source, valueNode))
                    return true;
            }
            if (source is NamedTypeExpression ns && ResolveEnum(ns) is EnumInfo sourceEnum)
            {
                if (IsAssignable(target, sourceEnum.UnderlyingType, valueNode))
                    return true;
            }

            // default is assignable to any non-void type (except non-nullable pointers/references)
            if (source is NamedTypeExpression { Name: "default" })
            {
                if (target is not NamedTypeExpression { Name: "void" })
                {
                    TypeExpression resolvedTarget = ResolveAlias(target);
                    if ((resolvedTarget is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression) && !IsNullable(resolvedTarget))
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
                if (!IsAssignable(ft.ReturnType, fs.ReturnType))
                    return false;
                if (ft.ParameterTypes.Count != fs.ParameterTypes.Count)
                    return false;
                for (int i = 0; i < ft.ParameterTypes.Count; i++)
                {
                    if (!TypesMatch(ft.ParameterTypes[i], fs.ParameterTypes[i]))
                        return false;
                }
                return true;
            }

            // void* is implicitly convertible to/from unmanaged function pointers
            if (target is PointerTypeExpression { Inner: NamedTypeExpression { Name: "void" } } && source is FunctionPointerTypeExpression { IsManaged: false })
                return true;
            if (source is PointerTypeExpression { Inner: NamedTypeExpression { Name: "void" } } && target is FunctionPointerTypeExpression { IsManaged: false })
                return true;

            // Array-to-pointer decay: T[N] or T[] can be assigned to T*
            if (target is PointerTypeExpression ptrTarget && source is ArrayTypeExpression arrSource)
            {
                if (valueNode is LiteralExpression { Token.Kind: TokenKind.StringLiteral } && !ptrTarget.IsReadOnly)
                    return false;

                if (IsAssignable(ptrTarget.Inner, arrSource.ElementType))
                    return true;
            }

            // Array-to-array assignment (e.g. char[10] a = "string" where string is char[7])
            if (target is ArrayTypeExpression arrTarget && source is ArrayTypeExpression arrSrc)
            {
                if (IsAssignable(arrTarget.ElementType, arrSrc.ElementType))
                {
                    if (arrTarget.Size == null || arrSrc.Size == null || arrTarget.Size >= arrSrc.Size)
                        return true;
                }
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
                if (s.Name == "uint" && t.Name is "int" or "long" or "ulong" or "nint" or "nuint" or "extralong")
                    return true;
                if (s.Name == "int" && t.Name is "long" or "nint" or "extralong")
                    return true;
                if (s.Name == "char" && t.Name is "int" or "uint" or "long" or "ulong" or "nint" or "nuint" or "extralong")
                    return true;
                if (s.Name == "ulong" && t.Name is "long" or "nuint" or "extralong")
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
            _compilationUnit = node;

            // Pass 1: Symbol collection
            SymbolCollectionPass collectionPass = new SymbolCollectionPass(_symbols);
            collectionPass.Execute(node);

            // Pass 2: Hierarchy resolution
            HierarchyResolutionPass hierarchyPass = new HierarchyResolutionPass(_symbols);
            hierarchyPass.Execute();

            // Pass 3: Body type checking
            _currentNamespace = _globalScope;
            for (int i = 0; i < node.Members.Count; i++)
            {
                node.Members[i].Accept(this);
            }

            foreach (NamespaceDeclaration ns in node.Namespaces)
            {
                ns.Accept(this);
            }

            // Pass 4: Control flow & Return path analysis
            ControlFlowPass controlFlowPass = new ControlFlowPass(this, _constEvaluator, _diagnostics);
            controlFlowPass.Execute(node);

            // Pass 5: Definite assignment analysis
            DefiniteAssignmentPass definiteAssignmentPass = new DefiniteAssignmentPass(this, controlFlowPass);
            definiteAssignmentPass.Execute(node);

            if (_diagnostics.HasErrors)
            {
                Diagnostic firstError = _diagnostics.Items.First(d => d.Severity == DiagnosticSeverity.Error);
                throw new TypeCheckException(firstError.Message, firstError.Line);
            }
        }

        private void ResolveClassHierarchies()
        {
            new HierarchyResolutionPass(_symbols).Execute();
        }

        private void RegisterMemberInScope(AstNode member, NamespaceScope scope, string nsPath)
        {
            if (member is MethodDeclaration method)
            {
                if (method.IsGeneric)
                {
                    _genericMethods[method.Name] = method;
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
                InterfaceInfo info = new InterfaceInfo
                {
                    Name = iface.Name,
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
                scope.Interfaces[iface.Name] = info;
            }
            else if (member is StructDeclaration str)
            {
                if (str.IsGeneric)
                {
                    _genericStructs[str.Name] = str;
                    return;
                }
                StructInfo info = new StructInfo
                {
                    Name = str.Name,
                    Namespace = nsPath,
                    Interfaces = new List<string>(str.Interfaces)
                };
                for (int i = 0; i < str.Members.Count; i++)
                {
                    AstNode m = str.Members[i];
                    if (m is ClassDeclaration nestedCls)
                    {
                        ClassDeclaration qualifiedCls = new ClassDeclaration(
                            $"{str.Name}.{nestedCls.Name}",
                            nestedCls.BaseClass,
                            nestedCls.Interfaces,
                            nestedCls.Members,
                            nestedCls.Accessibility,
                            nestedCls.IsAbstract,
                            nestedCls.Line,
                            nestedCls.Attributes,
                            nestedCls.GenericParameters
                        );
                        str.Members[i] = qualifiedCls;
                        RegisterMemberInScope(qualifiedCls, scope, nsPath);
                    }
                    else if (m is StructDeclaration nestedStruct)
                    {
                        StructDeclaration qualifiedStruct = new StructDeclaration(
                            $"{str.Name}.{nestedStruct.Name}",
                            nestedStruct.Interfaces,
                            nestedStruct.Members,
                            nestedStruct.Accessibility,
                            nestedStruct.Line,
                            nestedStruct.Attributes,
                            nestedStruct.GenericParameters
                        );
                        str.Members[i] = qualifiedStruct;
                        RegisterMemberInScope(qualifiedStruct, scope, nsPath);
                    }
                    else if (m is FieldDeclaration field)
                    {
                        info.Fields.Add((field.Name, field.Type));
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
                    else if (m is DestructorDeclaration dtor)
                    {
                        if (info.Destructor != null)
                            throw new TypeCheckException($"Struct '{str.Name}' already defines a destructor", dtor.Line);
                        if (dtor.IsVirtual)
                            throw new TypeCheckException($"Struct '{str.Name}' destructor cannot be virtual", dtor.Line);
                        string shortStrName = str.Name.Contains('.') ? str.Name.Substring(str.Name.LastIndexOf('.') + 1) : str.Name;
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
                        RegisterEnum(enumDecl, scope, nsPath.Length > 0 ? $"{nsPath}::{str.Name}" : str.Name);
                    }
                }
                _structs[str.Name] = info;
            }
            else if (member is ClassDeclaration cls)
            {
                if (cls.IsGeneric)
                {
                    _genericClasses[cls.Name] = cls;
                    return;
                }
                ClassInfo info = new ClassInfo
                {
                    Name = cls.Name,
                    Namespace = nsPath,
                    BaseClass = cls.BaseClass,
                    IsAbstract = cls.IsAbstract,
                    Accessibility = cls.Accessibility,
                    Interfaces = new List<string>(cls.Interfaces),
                    Line = cls.Line
                };

                for (int i = 0; i < cls.Members.Count; i++)
                {
                    AstNode m = cls.Members[i];
                    if (m is ClassDeclaration nestedCls)
                    {
                        ClassDeclaration qualifiedCls = new ClassDeclaration(
                            $"{cls.Name}.{nestedCls.Name}",
                            nestedCls.BaseClass,
                            nestedCls.Interfaces,
                            nestedCls.Members,
                            nestedCls.Accessibility,
                            nestedCls.IsAbstract,
                            nestedCls.Line,
                            nestedCls.Attributes,
                            nestedCls.GenericParameters
                        );
                        cls.Members[i] = qualifiedCls;
                        RegisterMemberInScope(qualifiedCls, scope, nsPath);
                    }
                    else if (m is StructDeclaration nestedStruct)
                    {
                        StructDeclaration qualifiedStruct = new StructDeclaration(
                            $"{cls.Name}.{nestedStruct.Name}",
                            nestedStruct.Interfaces,
                            nestedStruct.Members,
                            nestedStruct.Accessibility,
                            nestedStruct.Line,
                            nestedStruct.Attributes,
                            nestedStruct.GenericParameters
                        );
                        cls.Members[i] = qualifiedStruct;
                        RegisterMemberInScope(qualifiedStruct, scope, nsPath);
                    }
                    else if (m is FieldDeclaration field)
                    {
                        info.FieldDeclarations.Add(field);
                    }
                    else if (m is ConstructorDeclaration ctor)
                    {
                        info.Constructors.Add(ctor);
                    }
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
                        if (!cm.IsAbstract && cm.Body == null)
                            throw new TypeCheckException($"Method '{cm.Name}' must declare a body unless marked abstract", cm.Line);

                        info.Methods[cm.Name] = (cm, cls.Name);
                        info.AllMethods.Add(cm);
                        _functionNamespaces[cm] = nsPath;
                    }
                }

                _classes[cls.Name] = info;
                scope.Classes[cls.Name] = info;
            }
            else if (member is NamespaceDeclaration nested)
            {
                BuildNamespaceScope(nested, scope, nsPath.Length > 0 ? $"{nsPath}${nested.Name}" : nested.Name);
            }
            else if (member is AliasDeclaration alias)
            {
                scope.Aliases[alias.Name] = alias.TargetType;
            }
            else if (member is EnumDeclaration enumDecl)
            {
                RegisterEnum(enumDecl, scope, nsPath);
            }
            else if (member is FieldDeclaration field)
            {
                scope.Fields[field.Name] = field;
            }
        }

        private void BuildNamespaceScope(NamespaceDeclaration ns, NamespaceScope parent, string parentPath)
        {
            string nsPath = parentPath.Length > 0 ? $"{parentPath}${ns.Name}" : ns.Name;
            NamespaceScope scope = new NamespaceScope { Parent = parent };
            parent.Children[ns.Name] = scope;
            _namespaceScopes[ns] = scope;

            foreach (AstNode member in ns.Members)
                RegisterMemberInScope(member, scope, nsPath);
        }

        public void Visit(UsingDirective node) { }

        public void Visit(NamespaceDeclaration node)
        {
            NamespaceScope previous = _currentNamespace;
            _currentNamespace = _namespaceScopes[node];
            string prevPath = _currentNamespacePath;
            _currentNamespacePath = _currentNamespacePath.Length > 0 ? $"{_currentNamespacePath}::{node.Name}" : node.Name;

            foreach (AstNode member in node.Members)
                member.Accept(this);

            _currentNamespace = previous;
            _currentNamespacePath = prevPath;
        }

        public void Visit(ClassDeclaration node)
        {
            if (node.IsGeneric) return;

            ClassInfo? prevClass = _currentClass;
            StructInfo? prevStruct = _currentStruct;
            _currentClass = _classes[node.Name];
            _currentStruct = null;

            try
            {
                // Validate implemented interfaces
                foreach (string ifaceName in _currentClass.Interfaces)
                {
                    InterfaceInfo? ifaceInfo = ResolveInterface(new NamedTypeExpression(ifaceName, null, node.Line));
                    if (ifaceInfo == null)
                    {
                        throw new TypeCheckException($"Class '{node.Name}' implements unknown interface '{ifaceName}'", node.Line);
                    }

                    foreach (MethodDeclaration ifaceMethod in ifaceInfo.Methods)
                    {
                        if (!_currentClass.Methods.TryGetValue(ifaceMethod.Name, out (MethodDeclaration Method, string DeclaringClass) mEntry))
                        {
                            throw new TypeCheckException($"Class '{node.Name}' does not implement interface method '{ifaceName}.{ifaceMethod.Name}'", node.Line);
                        }

                        MethodDeclaration classMethod = mEntry.Method;
                        if (ifaceMethod.Throws != classMethod.Throws)
                        {
                            throw new TypeCheckException(
                                $"Method '{classMethod.Name}' in class '{node.Name}' must match throws specification of interface method '{ifaceName}.{ifaceMethod.Name}'",
                                classMethod.Line);
                        }
                        if (ifaceMethod.IsReadOnly && !classMethod.IsReadOnly)
                        {
                            throw new TypeCheckException(
                                $"Method '{classMethod.Name}' in class '{node.Name}' must be marked readonly to implement interface method '{ifaceName}.{ifaceMethod.Name}'",
                                classMethod.Line);
                        }
                        if (!TypesMatch(ResolveAlias(classMethod.ReturnType), ResolveAlias(ifaceMethod.ReturnType)))
                        {
                            throw new TypeCheckException(
                                $"Method '{classMethod.Name}' in class '{node.Name}' has return type '{TypeName(classMethod.ReturnType)}', but interface '{ifaceName}' requires '{TypeName(ifaceMethod.ReturnType)}'",
                                classMethod.Line);
                        }

                        if (classMethod.Parameters.Count != ifaceMethod.Parameters.Count)
                        {
                            throw new TypeCheckException(
                                $"Method '{classMethod.Name}' in class '{node.Name}' has {classMethod.Parameters.Count} parameters, but interface '{ifaceName}' expects {ifaceMethod.Parameters.Count}",
                                classMethod.Line);
                        }

                        for (int i = 0; i < ifaceMethod.Parameters.Count; i++)
                        {
                            TypeExpression classParamType = ResolveAlias(classMethod.Parameters[i].Type);
                            TypeExpression ifaceParamType = ResolveAlias(ifaceMethod.Parameters[i].Type);
                            if (!TypesMatch(classParamType, ifaceParamType))
                            {
                                throw new TypeCheckException(
                                    $"Parameter '{classMethod.Parameters[i].Name}' of method '{classMethod.Name}' in class '{node.Name}' has type '{TypeName(classParamType)}', but interface '{ifaceName}' expects '{TypeName(ifaceParamType)}'",
                                    classMethod.Parameters[i].Line);
                            }
                        }
                    }
                }

                foreach (AstNode member in node.Members)
                {
                    if (member is FieldDeclaration field)
                    {
                        field.Type = ResolveAlias(field.Type);
                        int fIdx = _currentClass.FieldIndex(field.Name);
                        if (fIdx >= 0)
                        {
                            _currentClass.Fields[fIdx] = (field.Name, field.Type, field.Accessibility, _currentClass.Name);
                        }
                        ValidateTypeUsage(field.Type, field.Line);
                        TypeExpression fieldType = field.Type;
                        if (field.IsConst && fieldType is PointerTypeExpression cPtr && !cPtr.IsReadOnly)
                        {
                            fieldType = new PointerTypeExpression(cPtr.Inner, cPtr.IsNullable, cPtr.Line, isReadOnly: true);
                        }
                        if (field.Initializer != null)
                        {
                            field.Initializer.Accept(this);
                            TypeExpression initType = ResolveAlias(GetType(field.Initializer));
                            if (!IsAssignable(fieldType, initType, field.Initializer))
                            {
                                throw new TypeCheckException($"Cannot assign expression of type '{TypeName(initType)}' to field '{field.Name}' of type '{TypeName(fieldType)}'", field.Line);
                            }
                        }
                        if (field.IsConst)
                        {
                            if (field.Initializer == null)
                            {
                                throw new TypeCheckException($"Const variable '{field.Name}' must have an initializer", field.Line);
                            }
                            if (!_constEvaluator.TryEvaluate(field.Initializer, out ConstValue? constVal, out string? err))
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
                        method.ReturnType = ResolveAlias(method.ReturnType);
                        foreach (Parameter p in method.Parameters)
                        {
                            p.Type = ResolveAlias(p.Type);
                        }
                        AstNode? prevFunc = _currentFunction;
                        _currentFunction = method;
                        try
                        {
                            ValidateTypeUsage(method.ReturnType, method.Line);
                            if (!method.IsAbstract)
                            {
                                PushScope();
                                NamedTypeExpression classType = new NamedTypeExpression(node.Name, null, method.Line);
                                PointerTypeExpression thisType = new PointerTypeExpression(classType, false, method.Line, isReadOnly: method.IsReadOnly);
                                DeclareVariable("this", thisType, method.Line);

                                foreach (Parameter p in method.Parameters)
                                {
                                    ValidateTypeUsage(p.Type, p.Line);
                                    DeclareVariable(p.Name, p.Type, p.Line);
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
            if (node.IsGeneric) return;

            StructInfo? previousStruct = _currentStruct;
            ClassInfo? prevClass = _currentClass;
            _currentStruct = _structs[node.Name];
            _currentClass = null;

            try
            {
                // Validate implemented interfaces
                foreach (string ifaceName in node.Interfaces)
                {
                    InterfaceInfo? ifaceInfo = ResolveInterface(new NamedTypeExpression(ifaceName, null, node.Line));
                    if (ifaceInfo == null)
                    {
                        throw new TypeCheckException($"Struct '{node.Name}' implements unknown interface '{ifaceName}'", node.Line);
                    }

                    foreach (MethodDeclaration ifaceMethod in ifaceInfo.Methods)
                    {
                        if (!_currentStruct.Methods.TryGetValue(ifaceMethod.Name, out MethodDeclaration? structMethod))
                        {
                            throw new TypeCheckException($"Struct '{node.Name}' does not implement interface method '{ifaceName}.{ifaceMethod.Name}'", node.Line);
                        }

                        if (ifaceMethod.Throws != structMethod.Throws)
                        {
                            throw new TypeCheckException(
                                $"Method '{structMethod.Name}' in struct '{node.Name}' must match throws specification of interface method '{ifaceName}.{ifaceMethod.Name}'",
                                structMethod.Line);
                        }

                        if (ifaceMethod.IsReadOnly && !structMethod.IsReadOnly)
                        {
                            throw new TypeCheckException(
                                $"Method '{structMethod.Name}' in struct '{node.Name}' must be marked readonly to implement interface method '{ifaceName}.{ifaceMethod.Name}'",
                                structMethod.Line);
                        }

                        if (!TypesMatch(ResolveAlias(structMethod.ReturnType), ResolveAlias(ifaceMethod.ReturnType)))
                        {
                            throw new TypeCheckException(
                                $"Method '{structMethod.Name}' in struct '{node.Name}' has return type '{TypeName(structMethod.ReturnType)}', but interface '{ifaceName}' requires '{TypeName(ifaceMethod.ReturnType)}'",
                                structMethod.Line);
                        }

                        if (structMethod.Parameters.Count != ifaceMethod.Parameters.Count)
                        {
                            throw new TypeCheckException(
                                $"Method '{structMethod.Name}' in struct '{node.Name}' has {structMethod.Parameters.Count} parameters, but interface '{ifaceName}' expects {ifaceMethod.Parameters.Count}",
                                structMethod.Line);
                        }

                        for (int i = 0; i < ifaceMethod.Parameters.Count; i++)
                        {
                            TypeExpression structParamType = ResolveAlias(structMethod.Parameters[i].Type);
                            TypeExpression ifaceParamType = ResolveAlias(ifaceMethod.Parameters[i].Type);
                            if (!TypesMatch(structParamType, ifaceParamType))
                            {
                                throw new TypeCheckException(
                                    $"Parameter '{structMethod.Parameters[i].Name}' of method '{structMethod.Name}' in struct '{node.Name}' has type '{TypeName(structParamType)}', but interface '{ifaceName}' expects '{TypeName(ifaceParamType)}'",
                                    structMethod.Parameters[i].Line);
                            }
                        }
                    }
                }

                foreach (AstNode member in node.Members)
                {
                    if (member is FieldDeclaration field)
                    {
                        field.Type = ResolveAlias(field.Type);
                        int fIdx = _currentStruct.Fields.FindIndex(f => f.Name == field.Name);
                        if (fIdx >= 0)
                        {
                            _currentStruct.Fields[fIdx] = (field.Name, field.Type);
                        }
                        ValidateTypeUsage(field.Type, field.Line);
                        TypeExpression fieldType = field.Type;
                        if (field.IsConst && fieldType is PointerTypeExpression sPtr && !sPtr.IsReadOnly)
                        {
                            fieldType = new PointerTypeExpression(sPtr.Inner, sPtr.IsNullable, sPtr.Line, isReadOnly: true);
                        }
                        if (field.Initializer != null)
                        {
                            field.Initializer.Accept(this);
                            TypeExpression initType = ResolveAlias(GetType(field.Initializer));
                            if (!IsAssignable(fieldType, initType, field.Initializer))
                            {
                                throw new TypeCheckException($"Cannot assign expression of type '{TypeName(initType)}' to field '{field.Name}' of type '{TypeName(fieldType)}'", field.Line);
                            }
                        }
                        if (field.IsConst)
                        {
                            if (field.Initializer == null)
                            {
                                throw new TypeCheckException($"Const variable '{field.Name}' must have an initializer", field.Line);
                            }
                            if (!_constEvaluator.TryEvaluate(field.Initializer, out ConstValue? constVal, out string? err))
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
                        method.ReturnType = ResolveAlias(method.ReturnType);
                        foreach (Parameter p in method.Parameters)
                        {
                            p.Type = ResolveAlias(p.Type);
                        }
                        AstNode? prevFunc = _currentFunction;
                        _currentFunction = method;
                        try
                        {
                            ValidateTypeUsage(method.ReturnType, method.Line);
                            PushScope();
                            NamedTypeExpression structType = new NamedTypeExpression(node.Name, null, method.Line);
                            PointerTypeExpression thisType = new PointerTypeExpression(structType, false, method.Line, isReadOnly: method.IsReadOnly);
                            DeclareVariable("this", thisType, method.Line);

                            foreach (Parameter p in method.Parameters)
                            {
                                ValidateTypeUsage(p.Type, p.Line);
                                DeclareVariable(p.Name, p.Type, p.Line);
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
            if (_currentStruct == null)
            {
                throw new TypeCheckException("Operator overloads must be declared inside a struct", node.Line);
            }

            ValidateTypeUsage(node.ReturnType, node.Line);

            if (node.OperatorKind == TokenKind.Bang)
            {
                if (node.Parameters.Count != 1)
                {
                    throw new TypeCheckException($"Unary operator '{node.OperatorSymbol}' must have exactly 1 parameter", node.Line);
                }
            }
            else if (node.Parameters.Count != 1 && node.Parameters.Count != 2)
            {
                throw new TypeCheckException($"Operator '{node.OperatorSymbol}' must have 1 or 2 parameters", node.Line);
            }

            // At least one parameter must be the enclosing struct type
            bool hasContainingType = false;
            foreach (Parameter p in node.Parameters)
            {
                ValidateTypeUsage(p.Type, p.Line);
                TypeExpression resolvedParam = ResolveAlias(p.Type);
                if (resolvedParam is NamedTypeExpression named && named.Name == _currentStruct.Name)
                {
                    hasContainingType = true;
                    break;
                }
            }

            if (!hasContainingType)
            {
                throw new TypeCheckException($"One of the parameters of a user-defined operator must be the containing type '{_currentStruct.Name}'", node.Line);
            }

            AstNode? prevFunc = _currentFunction;
            _currentFunction = node;
            try
            {
                PushScope();
                foreach (Parameter p in node.Parameters)
                {
                    DeclareVariable(p.Name, ResolveAlias(p.Type), p.Line);
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
            foreach (AstNode member in node.Members)
            {
                if (member is MethodDeclaration method)
                {
                    if (method.Body != null)
                    {
                        throw new TypeCheckException($"Interface method '{node.Name}.{method.Name}' cannot have a body", method.Line);
                    }
                    ValidateTypeUsage(method.ReturnType, method.Line);
                    foreach (Parameter p in method.Parameters)
                    {
                        ValidateTypeUsage(p.Type, p.Line);
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
            ValidateTypeUsage(node.Type, node.Line);
            TypeExpression fieldType = ResolveAlias(node.Type);
            if (node.IsConst && fieldType is PointerTypeExpression fPtr && !fPtr.IsReadOnly)
            {
                fieldType = new PointerTypeExpression(fPtr.Inner, fPtr.IsNullable, fPtr.Line, isReadOnly: true);
            }
            if (node.Initializer != null)
            {
                node.Initializer.Accept(this);
                TypeExpression initType = ResolveAlias(GetType(node.Initializer));
                if (!IsAssignable(fieldType, initType, node.Initializer))
                {
                    throw new TypeCheckException($"Cannot assign expression of type '{TypeName(initType)}' to field '{node.Name}' of type '{TypeName(fieldType)}'", node.Line);
                }
            }
            else if (node.IsConst)
            {
                throw new TypeCheckException($"Const variable '{node.Name}' must have an initializer", node.Line);
            }

            if (node.IsConst)
            {
                if (node.Initializer == null)
                {
                    throw new TypeCheckException($"Const variable '{node.Name}' must have an initializer", node.Line);
                }
                if (!_constEvaluator.TryEvaluate(node.Initializer, out ConstValue? constVal, out string? err))
                {
                    throw new TypeCheckException($"Const variable '{node.Name}' initializer must be a compile-time constant: {err}", node.Line);
                }
                _constVariablesByName[node.Name] = constVal!;
                _constValues[node] = constVal!;
                _constValues[node.Initializer] = constVal!;
                _constVariableNames.Add(node.Name);
            }

            RecordType(node, fieldType);
        }

        public void Visit(MethodDeclaration node)
        {
            if (node.IsGeneric) return;

            if (node.Name == "main" && node.Throws)
            {
                throw new TypeCheckException("'main' function cannot be declared with 'throws'", node.Line);
            }

            if (node.Name == "__gflat_alloc" || node.Name == "__gflat_free")
            {
                ValidateAllocatorHookSignature(node.Name, node.ReturnType, node.Parameters, node.Line);
            }

            AstNode? prevFunc = _currentFunction;
            _currentFunction = node;
            try
            {
                ValidateTypeUsage(node.ReturnType, node.Line);
                PushScope();
                foreach (Parameter p in node.Parameters)
                {
                    ValidateTypeUsage(p.Type, p.Line);
                    TypeExpression pType = ResolveAlias(p.Type);
                    if (HasDestructor(pType))
                    {
                        throw new TypeCheckException($"Parameter '{p.Name}' cannot have type '{TypeName(pType)}' because types with destructors cannot be passed by value. Use a pointer instead ('{TypeName(pType)}*').", p.Line);
                    }
                    DeclareVariable(p.Name, pType, p.Line);
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
            if (_currentStruct == null && _currentClass == null)
            {
                throw new TypeCheckException("Constructor must be declared inside a struct or class", node.Line);
            }
            string ownerName = _currentStruct?.Name ?? _currentClass!.Name;
            string shortOwnerName = ownerName.Contains('.') ? ownerName.Substring(ownerName.LastIndexOf('.') + 1) : ownerName;
            if (node.Name != ownerName && node.Name != shortOwnerName)
            {
                string kindStr = _currentStruct != null ? "struct" : "class";
                throw new TypeCheckException($"Constructor name '{node.Name}' does not match {kindStr} name '{ownerName}'", node.Line);
            }
            _currentConstructor = node;
            AstNode? prevFunc = _currentFunction;
            _currentFunction = node;
            try
            {
                PushScope();
                NamedTypeExpression typeExpr = new NamedTypeExpression(ownerName, null, node.Line);
                PointerTypeExpression thisType = new PointerTypeExpression(typeExpr, false, node.Line);
                DeclareVariable("this", thisType, node.Line);

                foreach (Parameter p in node.Parameters)
                {
                    p.Type = ResolveAlias(p.Type);
                    ValidateTypeUsage(p.Type, p.Line);
                    if (HasDestructor(p.Type))
                    {
                        throw new TypeCheckException($"Parameter '{p.Name}' cannot have type '{TypeName(p.Type)}' because types with destructors cannot be passed by value. Use a pointer instead ('{TypeName(p.Type)}*').", p.Line);
                    }
                    DeclareVariable(p.Name, p.Type, p.Line);
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
                        arg.Accept(this);
                    }

                    List<ConstructorDeclaration> matches = new();
                    foreach (ConstructorDeclaration baseCtor in baseClass.Constructors)
                    {
                        if (baseCtor.Parameters.Count != node.BaseArguments.Count)
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
                    if (matches.Count > 1)
                    {
                        throw new TypeCheckException($"Call to base constructor of '{baseClass.Name}' is ambiguous", node.Line);
                    }

                    _resolvedBaseConstructors[node] = matches[0];
                }
                else if (_currentClass != null && _currentClass.BaseClass != null)
                {
                    ClassInfo baseClass = _classes[_currentClass.BaseClass];
                    if (baseClass.Constructors.Count > 0 && !baseClass.Constructors.Any(c => c.Parameters.Count == 0))
                    {
                        throw new TypeCheckException($"Class '{ownerName}' must explicitly call a base constructor because base class '{baseClass.Name}' does not define a parameterless constructor", node.Line);
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
            if (_currentClass == null && _currentStruct == null)
            {
                throw new TypeCheckException("Destructor must be declared inside a class or struct", node.Line);
            }
            string ownerName = _currentStruct?.Name ?? _currentClass!.Name;
            string shortOwnerName = ownerName.Contains('.') ? ownerName.Substring(ownerName.LastIndexOf('.') + 1) : ownerName;
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
                NamedTypeExpression typeExpr = new NamedTypeExpression(ownerName, null, node.Line);
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

        public void Visit(Parameter node) { }

        public void Visit(BlockStatement node)
        {
            PushScope();
            foreach (AstNode statement in node.Statements)
                statement.Accept(this);
            PopScope();
        }

        public void Visit(DeferStatement node)
        {
            if (node.Statement is ReturnStatement)
                throw new TypeCheckException("Cannot return from within a defer statement", node.Line);

            bool prevInDefer = _inDefer;
            _inDefer = true;
            try
            {
                node.Statement.Accept(this);
            }
            finally
            {
                _inDefer = prevInDefer;
            }
        }

        public void Visit(DeleteStatement node)
        {
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
                if (structInfo.Destructor != null)
                {
                    _structDestructorCalls[node] = structInfo;
                }
            }
        }

        public void Visit(ReturnStatement node)
        {
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
                    if (!IsAssignable(retType, valType, node.Value))
                    {
                        throw new TypeCheckException($"Cannot return '{TypeName(valType)}' from function returning '{TypeName(retType)}'", node.Line);
                    }
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
            }
        }

        public void Visit(IfStatement node)
        {
            node.Condition.Accept(this);
            TypeExpression condType = GetType(node.Condition);
            if (!TypesMatch(condType, Bool))
                throw new TypeCheckException("If condition must be bool", node.Line);
            node.Then.Accept(this);
            node.Else?.Accept(this);
        }

        public void Visit(WhileStatement node)
        {
            node.Condition.Accept(this);
            TypeExpression condType = GetType(node.Condition);
            if (!TypesMatch(condType, Bool))
                throw new TypeCheckException("While condition must be bool", node.Line);
            _loopDepth++;
            try
            {
                node.Body.Accept(this);
            }
            finally
            {
                _loopDepth--;
            }
        }

        public void Visit(ForStatement node)
        {
            PushScope();
            node.Initializer?.Accept(this);
            if (node.Condition != null)
            {
                node.Condition.Accept(this);
                TypeExpression condType = GetType(node.Condition);
                if (!TypesMatch(condType, Bool))
                    throw new TypeCheckException("For condition must be bool", node.Line);
            }
            node.Increment?.Accept(this);
            _loopDepth++;
            try
            {
                node.Body.Accept(this);
            }
            finally
            {
                _loopDepth--;
            }
            PopScope();
        }

        public void Visit(ForeachStatement node)
        {
            node.ElementType = ResolveAlias(node.ElementType);
            ValidateTypeUsage(node.ElementType, node.Line);
            if (HasDestructor(node.ElementType))
            {
                throw new TypeCheckException($"Foreach iteration variable '{node.VariableName}' cannot have type '{TypeName(node.ElementType)}' because types with destructors cannot be copied by value. Use a pointer instead ('{TypeName(node.ElementType)}*').", node.Line);
            }

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
                    node.Body.Accept(this);
                }
                finally
                {
                    _loopDepth--;
                }
                PopScope();
                return;
            }

            // Custom collection
            TypeExpression unwrappedCol = rawColType;
            if (unwrappedCol is PointerTypeExpression pCol)
            {
                unwrappedCol = ResolveAlias(pCol.Inner);
            }
            if (unwrappedCol is ManagedTypeExpression mCol)
            {
                unwrappedCol = ResolveAlias(mCol.Inner);
            }

            if (unwrappedCol is not NamedTypeExpression namedCol)
            {
                throw new TypeCheckException($"Cannot foreach over expression of type '{TypeName(rawColType)}': type is not an array, class, or struct", node.Line);
            }

            // Look up GetEnumerator()
            MethodDeclaration? getEnumMethod = null;
            if (_classes.TryGetValue(namedCol.Name, out ClassInfo? clsInfo))
            {
                if (clsInfo.Methods.TryGetValue("GetEnumerator", out (MethodDeclaration Method, string DeclaringClass) mEntry))
                {
                    getEnumMethod = mEntry.Method;
                }
            }
            else if (_structs.TryGetValue(namedCol.Name, out StructInfo? strInfo))
            {
                if (strInfo.Methods.TryGetValue("GetEnumerator", out MethodDeclaration? sMethod))
                {
                    getEnumMethod = sMethod;
                }
            }
            else if (ResolveInterface(namedCol) is InterfaceInfo ifaceInfo)
            {
                if (ifaceInfo.MethodsByName.TryGetValue("GetEnumerator", out MethodDeclaration? ifaceMethod))
                {
                    getEnumMethod = ifaceMethod;
                }
            }

            if (getEnumMethod == null)
            {
                throw new TypeCheckException($"Type '{namedCol.Name}' does not implement 'GetEnumerator()'", node.Line);
            }

            if (getEnumMethod.Parameters.Count != 0)
            {
                throw new TypeCheckException($"'GetEnumerator()' on type '{namedCol.Name}' must take 0 arguments", node.Line);
            }

            TypeExpression enumRetType = ResolveAlias(getEnumMethod.ReturnType);
            TypeExpression unwrappedEnum = enumRetType;
            if (unwrappedEnum is PointerTypeExpression pe)
            {
                unwrappedEnum = ResolveAlias(pe.Inner);
            }
            if (unwrappedEnum is ManagedTypeExpression me)
            {
                unwrappedEnum = ResolveAlias(me.Inner);
            }

            if (unwrappedEnum is not NamedTypeExpression namedEnum)
            {
                throw new TypeCheckException($"'GetEnumerator()' on '{namedCol.Name}' must return a struct or class, but returned '{TypeName(enumRetType)}'", node.Line);
            }

            // Look up MoveNext() on enumerator type
            MethodDeclaration? moveNextMethod = null;
            ClassInfo? enumClsInfo = null;
            StructInfo? enumStrInfo = null;

            if (_classes.TryGetValue(namedEnum.Name, out enumClsInfo))
            {
                if (enumClsInfo.Methods.TryGetValue("MoveNext", out (MethodDeclaration Method, string DeclaringClass) mEntry))
                {
                    moveNextMethod = mEntry.Method;
                }
            }
            else if (_structs.TryGetValue(namedEnum.Name, out enumStrInfo))
            {
                if (enumStrInfo.Methods.TryGetValue("MoveNext", out MethodDeclaration? sMethod))
                {
                    moveNextMethod = sMethod;
                }
            }
            else if (ResolveInterface(namedEnum) is InterfaceInfo enumIfaceInfo)
            {
                if (enumIfaceInfo.MethodsByName.TryGetValue("MoveNext", out MethodDeclaration? ifaceMethod))
                {
                    moveNextMethod = ifaceMethod;
                }
            }

            if (moveNextMethod == null)
            {
                throw new TypeCheckException($"Enumerator type '{namedEnum.Name}' does not implement 'MoveNext()'", node.Line);
            }

            if (moveNextMethod.Parameters.Count != 0)
            {
                throw new TypeCheckException($"'MoveNext()' on enumerator '{namedEnum.Name}' must take 0 arguments", node.Line);
            }

            if (!TypesMatch(ResolveAlias(moveNextMethod.ReturnType), Bool))
            {
                throw new TypeCheckException($"'MoveNext()' on enumerator '{namedEnum.Name}' must return 'bool'", node.Line);
            }

            // Look up Current on enumerator type (method or field)
            bool isCurrentMethod = false;
            TypeExpression? currentItemType = null;
            if (_classes.TryGetValue(namedEnum.Name, out enumClsInfo))
            {
                if (enumClsInfo.Methods.TryGetValue("Current", out (MethodDeclaration Method, string DeclaringClass) mEntry))
                {
                    isCurrentMethod = true;
                    if (mEntry.Method.Parameters.Count != 0)
                    {
                        throw new TypeCheckException($"'Current()' method on enumerator '{namedEnum.Name}' must take 0 arguments", node.Line);
                    }
                    currentItemType = ResolveAlias(mEntry.Method.ReturnType);
                }
                else
                {
                    int fieldIdx = enumClsInfo.FieldIndex("Current");
                    if (fieldIdx >= 0)
                    {
                        isCurrentMethod = false;
                        currentItemType = ResolveAlias(enumClsInfo.Fields[fieldIdx].Type);
                    }
                }
            }
            else if (_structs.TryGetValue(namedEnum.Name, out enumStrInfo))
            {
                if (enumStrInfo.Methods.TryGetValue("Current", out MethodDeclaration? sMethod))
                {
                    isCurrentMethod = true;
                    if (sMethod.Parameters.Count != 0)
                    {
                        throw new TypeCheckException($"'Current()' method on enumerator '{namedEnum.Name}' must take 0 arguments", node.Line);
                    }
                    currentItemType = ResolveAlias(sMethod.ReturnType);
                }
                else
                {
                    int fieldIdx = enumStrInfo.FieldIndex("Current");
                    if (fieldIdx >= 0)
                    {
                        isCurrentMethod = false;
                        currentItemType = ResolveAlias(enumStrInfo.Fields[fieldIdx].Type);
                    }
                }
            }
            else if (ResolveInterface(namedEnum) is InterfaceInfo enumIfaceInfo)
            {
                if (enumIfaceInfo.MethodsByName.TryGetValue("Current", out MethodDeclaration? ifaceMethod))
                {
                    isCurrentMethod = true;
                    if (ifaceMethod.Parameters.Count != 0)
                    {
                        throw new TypeCheckException($"'Current()' method on enumerator '{namedEnum.Name}' must take 0 arguments", node.Line);
                    }
                    currentItemType = ResolveAlias(ifaceMethod.ReturnType);
                }
            }

            if (currentItemType == null)
            {
                throw new TypeCheckException($"Enumerator type '{namedEnum.Name}' must have a 'Current()' method or 'Current' field", node.Line);
            }

            if (!IsAssignable(node.ElementType, currentItemType, node.Collection))
            {
                throw new TypeCheckException($"Cannot assign collection element of type '{TypeName(currentItemType)}' to foreach variable of type '{TypeName(node.ElementType)}'", node.Line);
            }

            AstNode targetColExpr;
            AstNode? colVarDecl = null;
            if (node.Collection is IdentifierExpression idCol)
            {
                targetColExpr = new IdentifierExpression(idCol.Name, node.Line);
            }
            else
            {
                string colVarName = $"__col_{node.Line}_{_foreachCounter++}";
                colVarDecl = new VariableDeclaration(colVarName, rawColType, node.Collection, node.Line);
                targetColExpr = new IdentifierExpression(colVarName, node.Line);
            }

            string iterVarName = $"__iter_{node.Line}_{_foreachCounter++}";
            AstNode getEnumCall = new CallExpression(new MemberAccessExpression(targetColExpr, "GetEnumerator", isArrow: false, node.Line), new List<AstNode>(), node.Line);
            AstNode iterVarDecl = new VariableDeclaration(iterVarName, enumRetType, getEnumCall, node.Line);

            AstNode moveNextCall = new CallExpression(new MemberAccessExpression(new IdentifierExpression(iterVarName, node.Line), "MoveNext", isArrow: false, node.Line), new List<AstNode>(), node.Line);

            AstNode currentAccess = isCurrentMethod
                ? new CallExpression(new MemberAccessExpression(new IdentifierExpression(iterVarName, node.Line), "Current", isArrow: false, node.Line), new List<AstNode>(), node.Line)
                : new MemberAccessExpression(new IdentifierExpression(iterVarName, node.Line), "Current", isArrow: false, node.Line);

            AstNode elemVarDecl = new VariableDeclaration(node.VariableName, node.ElementType, currentAccess, node.Line);

            List<AstNode> loopBodyStmts = new List<AstNode> { elemVarDecl };
            loopBodyStmts.AddRange(node.Body.Statements);
            BlockStatement loopBody = new BlockStatement(loopBodyStmts, node.Line);
            WhileStatement whileStmt = new WhileStatement(moveNextCall, loopBody, node.Line);

            List<AstNode> outerStmts = new List<AstNode>();
            if (colVarDecl != null)
            {
                outerStmts.Add(colVarDecl);
            }
            outerStmts.Add(iterVarDecl);
            outerStmts.Add(whileStmt);
            BlockStatement desugaredBlock = new BlockStatement(outerStmts, node.Line);

            desugaredBlock.Accept(this);
            node.Desugared = desugaredBlock;
        }

        public void Visit(VariableDeclaration node)
        {
            TypeExpression varType = ResolveAlias(node.Type);
            if (node.IsConst && varType is PointerTypeExpression ptr && !ptr.IsReadOnly)
            {
                varType = new PointerTypeExpression(ptr.Inner, ptr.IsNullable, ptr.Line, isReadOnly: true);
            }
            ValidateTypeUsage(varType, node.Line);
            if (node.Initializer != null)
            {
                node.Initializer.Accept(this);
                TypeExpression initType = GetType(node.Initializer);

                if (HasDestructor(varType) && node.Initializer is not (NewExpression or DefaultExpression or CallExpression))
                {
                    throw new TypeCheckException($"Cannot copy value of type '{TypeName(varType)}' because it defines a destructor. Pass by pointer, or use an explicit method if available.", node.Line);
                }

                // Size inference for inferred arrays: char[] a = "string";
                if (varType is ArrayTypeExpression { Size: null } arr && initType is ArrayTypeExpression { Size: not null } initArr)
                {
                    varType = new ArrayTypeExpression(arr.ElementType, initArr.Size, node.Line, arr.SizeExpression);
                }

                if (!IsAssignable(varType, initType, node.Initializer))
                {
                    ReportError(DiagnosticRules.GF1001_TypeMismatch, node.Line, 0, TypeName(initType), TypeName(varType));
                }
            }
            else if (node.IsConst)
            {
                throw new TypeCheckException($"Const variable '{node.Name}' must have an initializer", node.Line);
            }

            if (node.IsConst)
            {
                if (node.Initializer == null)
                {
                    throw new TypeCheckException($"Const variable '{node.Name}' must have an initializer", node.Line);
                }
                if (!_constEvaluator.TryEvaluate(node.Initializer, out ConstValue? constVal, out string? err))
                {
                    throw new TypeCheckException($"Const variable '{node.Name}' initializer must be a compile-time constant: {err}", node.Line);
                }
                _constVariablesByName[node.Name] = constVal!;
                _constValues[node] = constVal!;
                _constValues[node.Initializer] = constVal!;
                _constVariableNames.Add(node.Name);
            }

            RecordType(node, varType);
            DeclareVariable(node.Name, varType, node.Line);
        }

        public void Visit(ExpressionStatement node)
        {
            node.Expression.Accept(this);
        }

        public void Visit(BinaryExpression node)
        {
            node.Left.Accept(this);
            node.Right.Accept(this);
            TypeExpression left = GetType(node.Left);
            TypeExpression right = GetType(node.Right);

            if (IsError(left) || IsError(right))
            {
                RecordType(node, Error);
                return;
            }

            if (TryResolveBinaryOperatorForTypes(node.Operator, left, right, out string opStruct, out OperatorDeclaration opDecl))
            {
                _operatorTargets[node] = (opStruct, opDecl);
                RecordType(node, ResolveAlias(opDecl.ReturnType));
                return;
            }

            bool isComparison = node.Operator is
                TokenKind.EqualsEquals or TokenKind.NotEquals or
                TokenKind.Less or TokenKind.Greater or
                TokenKind.LessEquals or TokenKind.GreaterEquals;

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
            if (node.Operator == TokenKind.Ampersand)
            {
                if (node.Operand is IdentifierExpression identOperand)
                {
                    MethodDeclaration? method = ResolveFunction(identOperand.Name);
                    ExternDeclaration? ext = method == null ? ResolveExtern(identOperand.Name) : null;
                    if (method != null)
                    {
                        var paramTypes = method.Parameters.Select(p => ResolveAlias(p.Type)).ToList();
                        var fnType = new FunctionPointerTypeExpression(ResolveAlias(method.ReturnType), paramTypes, false, false, node.Line);
                        RecordType(node, fnType);
                        _functionAddressTargets[node] = method;
                        return;
                    }
                    if (ext != null)
                    {
                        var paramTypes = ext.Parameters.Select(p => ResolveAlias(p.Type)).ToList();
                        var fnType = new FunctionPointerTypeExpression(ResolveAlias(ext.ReturnType), paramTypes, false, false, node.Line);
                        RecordType(node, fnType);
                        _functionAddressTargets[node] = ext;
                        return;
                    }
                }
                else if (node.Operand is NamespaceAccessExpression nsAccess)
                {
                    NamespaceScope? scope = ResolveNamespace(nsAccess.Left);
                    if (scope != null)
                    {
                        if (scope.Functions.TryGetValue(nsAccess.Member, out MethodDeclaration? m))
                        {
                            var paramTypes = m.Parameters.Select(p => ResolveAlias(p.Type)).ToList();
                            var fnType = new FunctionPointerTypeExpression(ResolveAlias(m.ReturnType), paramTypes, false, false, node.Line);
                            RecordType(node, fnType);
                            _functionAddressTargets[node] = m;
                            return;
                        }
                        if (scope.Externs.TryGetValue(nsAccess.Member, out ExternDeclaration? e))
                        {
                            var paramTypes = e.Parameters.Select(p => ResolveAlias(p.Type)).ToList();
                            var fnType = new FunctionPointerTypeExpression(ResolveAlias(e.ReturnType), paramTypes, false, false, node.Line);
                            RecordType(node, fnType);
                            _functionAddressTargets[node] = e;
                            return;
                        }
                    }
                }

                node.Operand.Accept(this);
                TypeExpression operandType = GetType(node.Operand);
                if (IsError(operandType))
                {
                    RecordType(node, Error);
                    return;
                }
                bool isReadOnly = IsExpressionReadOnly(node.Operand);
                RecordType(node, new PointerTypeExpression(operandType, false, node.Line, isReadOnly: isReadOnly));
                if (_constEvaluator.TryEvaluate(node, out ConstValue? constPtr, out _))
                {
                    _constValues[node] = constPtr!;
                }
                return;
            }

            node.Operand.Accept(this);
            TypeExpression operand = GetType(node.Operand);

            if (IsError(operand))
            {
                RecordType(node, Error);
                return;
            }

            if (TryResolveUnaryOperatorForType(node.Operator, operand, out string opStruct, out OperatorDeclaration opDecl))
            {
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
                    if (operand is not PointerTypeExpression ptr)
                    {
                        throw new TypeCheckException("Cannot dereference non-pointer", node.Line);
                    }
                    TypeExpression resolvedInner = ResolveAlias(ptr.Inner);
                    if (resolvedInner is NamedTypeExpression namedInner && namedInner.Name == "void")
                    {
                        throw new TypeCheckException("Cannot dereference 'void*'", node.Line);
                    }
                    ValidateTypeUsage(ptr.Inner, node.Line);
                    RecordType(node, ptr.Inner);
                    break;
                default:
                    throw new NotImplementedException($"Unary operator {node.Operator} not yet supported");
            }
        }

        public void Visit(LiteralExpression node)
        {
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
                TokenKind.StringLiteral => new ArrayTypeExpression(Char, GetStringLiteralLength(node.Token.Text[1..^1]), node.Line),
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
            TypeExpression type = LookupVariable(node.Name, node.Line);
            RecordType(node, type);
            if (TryGetConstValueByName(node.Name, out ConstValue? cv) && cv != null)
            {
                _constValues[node] = cv;
            }
        }

        private bool IsExpressionReadOnly(AstNode node)
        {
            if (node is IdentifierExpression ident)
            {
                if (_constVariableNames.Contains(ident.Name))
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

        private void CheckAssignmentTarget(AstNode target, int line)
        {
            if (target is UnaryExpression deref && deref.Operator == TokenKind.Star)
            {
                TypeExpression opType = ResolveAlias(GetType(deref.Operand));
                if (opType is PointerTypeExpression { IsReadOnly: true })
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

                FieldDeclaration? globalField = ResolveGlobalField(ident.Name);
                if (globalField != null && globalField.IsConst)
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

            if (node.Target is not (IdentifierExpression or MemberAccessExpression or UnaryExpression { Operator: TokenKind.Star } or IndexExpression))
                throw new TypeCheckException($"Invalid assignment target '{node.Target.GetType().Name}'", node.Line);

            if (node.Target is MemberAccessExpression { IsArrow: true })
                throw new TypeCheckException("Cannot assign using '->'. Use '.' for member access.", node.Line);

            node.Target.Accept(this);
            TypeExpression targetType = GetType(node.Target);
            CheckAssignmentTarget(node.Target, node.Line);

            if (HasDestructor(targetType))
            {
                throw new TypeCheckException($"Cannot assign to '{TypeName(targetType)}': types with destructors cannot be copied or reassigned by value", node.Line);
            }

            node.Value.Accept(this);
            TypeExpression valueType = GetType(node.Value);

            if (node.Operator != TokenKind.Equals)
            {
                TokenKind? binOp = GetBinaryOperatorForCompound(node.Operator);
                if (binOp.HasValue && TryResolveBinaryOperatorForTypes(binOp.Value, targetType, valueType, out string opStruct, out OperatorDeclaration opDecl))
                {
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
                if (IsValidPointerForArithmetic(targetType, out string? errPtr) && IsInteger(valueType))
                {
                    if (errPtr != null)
                        throw new TypeCheckException(errPtr, node.Line);
                    RecordType(node, targetType);
                    return;
                }
                if (errPtr != null)
                    throw new TypeCheckException(errPtr, node.Line);
            }

            if (!IsAssignable(targetType, valueType, node.Value))
            {
                ReportError(DiagnosticRules.GF1001_TypeMismatch, node.Line, 0, TypeName(valueType), TypeName(targetType));
                RecordType(node, Error);
                return;
            }

            RecordType(node, targetType);
        }

        public void Visit(CallExpression node)
        {
            foreach (AstNode arg in node.Arguments)
                arg.Accept(this);

            // Check if callee is a local variable or struct member holding a function pointer
            if (node.Callee is IdentifierExpression identVar && TryLookupVariable(identVar.Name, out TypeExpression? varType))
            {
                varType = ResolveAlias(varType!);
                if (varType is FunctionPointerTypeExpression fnPtr)
                {
                    identVar.Accept(this);
                    CheckIndirectCall(node, fnPtr);
                    return;
                }
            }

            // Check if callee is an expression evaluating to a function pointer (e.g. member access, index, call)
            if (node.Callee is not IdentifierExpression and not NamespaceAccessExpression)
            {
                if (node.Callee is not MemberAccessExpression { IsArrow: true })
                {
                    node.Callee.Accept(this);
                    TypeExpression calleeType = ResolveAlias(GetType(node.Callee));
                    if (calleeType is FunctionPointerTypeExpression fnPtr)
                    {
                        CheckIndirectCall(node, fnPtr);
                        return;
                    }
                }
            }

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
                    throw new TypeCheckException($"Enum member '{calleeEnum.Name}::{memberAccess.Member}' must be accessed with '::', not '.'", node.Line);
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

                memberAccess.Object.Accept(this);
                TypeExpression rawObjType = GetType(memberAccess.Object);
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
                        throw new TypeCheckException($"Interface '{namedIface.Name}' has no method '{memberAccess.Member}'", node.Line);

                    if (isReceiverReadOnly && !ifaceMethod.IsReadOnly)
                        throw new TypeCheckException($"Cannot call non-readonly method '{ifaceMethod.Name}' on readonly instance", node.Line);

                    if (ifaceMethod.Throws)
                    {
                        CheckThrowingCall(node, $"{namedIface.Name}.{memberAccess.Member}");
                    }

                    int slotIdx = ifaceInfo.MethodIndices[memberAccess.Member];
                    _interfaceMethodCalls[node] = (ifaceInfo, slotIdx, ifaceMethod);

                    if (node.Arguments.Count != ifaceMethod.Parameters.Count)
                        throw new TypeCheckException(
                            $"Method '{namedIface.Name}.{memberAccess.Member}' expects {ifaceMethod.Parameters.Count} arguments but got {node.Arguments.Count}",
                            node.Line);

                    for (int i = 0; i < node.Arguments.Count; i++)
                    {
                        TypeExpression argType = GetType(node.Arguments[i]);
                        TypeExpression paramType = ifaceMethod.Parameters[i].Type;
                        if (!IsAssignable(paramType, argType, node.Arguments[i]))
                            throw new TypeCheckException(
                                $"Argument {i + 1} of '{namedIface.Name}.{memberAccess.Member}': cannot pass '{TypeName(argType)}' as '{TypeName(paramType)}'",
                                node.Line);

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
                            throw new TypeCheckException($"Cannot access private field '{memberAccess.Member}' of class '{field.DeclaringClass}'", node.Line);
                        if (field.Accessibility == TokenKind.Protected && (_currentClass == null || !IsSubclassOf(_currentClass.Name, field.DeclaringClass)))
                            throw new TypeCheckException($"Cannot access protected field '{memberAccess.Member}' of class '{field.DeclaringClass}'", node.Line);

                        TypeExpression fieldType = ResolveAlias(field.Type);
                        if (fieldType is FunctionPointerTypeExpression fnPtrField)
                        {
                            RecordType(memberAccess, fieldType);
                            CheckIndirectCall(node, fnPtrField);
                            return;
                        }
                    }

                    if (!classInfo.Methods.TryGetValue(memberAccess.Member, out (MethodDeclaration Method, string DeclaringClass) mEntry))
                        throw new TypeCheckException($"Class '{named.Name}' has no method '{memberAccess.Member}'", node.Line);

                    method = mEntry.Method;
                    if (isReceiverReadOnly && !method.IsReadOnly)
                        throw new TypeCheckException($"Cannot call non-readonly method '{method.Name}' on readonly instance", node.Line);
                    if (method.Accessibility == TokenKind.Private && !CanAccessPrivate(_currentClass?.Name, mEntry.DeclaringClass))
                        throw new TypeCheckException($"Cannot access private method '{memberAccess.Member}' of class '{mEntry.DeclaringClass}'", node.Line);
                    if (method.Accessibility == TokenKind.Protected && (_currentClass == null || !IsSubclassOf(_currentClass.Name, mEntry.DeclaringClass)))
                        throw new TypeCheckException($"Cannot access protected method '{memberAccess.Member}' of class '{mEntry.DeclaringClass}'", node.Line);

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
                        throw new TypeCheckException($"'{named.Name}' has no method '{memberAccess.Member}'", node.Line);

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
            else if (node.Callee is IdentifierExpression ident)
            {
                funcName = ident.Name;
                if (_currentClass != null && _currentClass.Methods.TryGetValue(funcName, out (MethodDeclaration Method, string DeclaringClass) mEntry))
                {
                    method = mEntry.Method;
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
                else if (_genericMethods.TryGetValue(funcName, out MethodDeclaration? genericMethod))
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
                if (nsAccess.Left is not IdentifierExpression && nsAccess.Left is not GlobalExpression && nsAccess.Left is not NamespaceAccessExpression)
                {
                    throw new TypeCheckException($"Instance member '{nsAccess.Member}' must be accessed with '.', not '::'", node.Line);
                }
                if (nsAccess.Left is IdentifierExpression typeId && !TryLookupVariable(typeId.Name, out _))
                {
                    ClassInfo? cInfo = GetClass(typeId.Name);
                    if (cInfo != null && cInfo.Methods.ContainsKey(nsAccess.Member))
                    {
                        throw new TypeCheckException($"Instance member '{nsAccess.Member}' must be accessed with '.', not '::'", node.Line);
                    }
                    StructInfo? sInfo = GetStruct(typeId.Name);
                    if (sInfo != null && sInfo.Methods.ContainsKey(nsAccess.Member))
                    {
                        throw new TypeCheckException($"Instance member '{nsAccess.Member}' must be accessed with '.', not '::'", node.Line);
                    }
                }

                nsAccess.Accept(this);
                RecordType(node, GetType(nsAccess));
                NamespaceScope? scope = ResolveNamespace(nsAccess.Left);
                if (scope != null)
                {
                    if (scope.Functions.TryGetValue(nsAccess.Member, out MethodDeclaration? m))
                    {
                        _resolvedCalls[node] = m;
                        if (m.Throws)
                        {
                            CheckThrowingCall(node, $"{nsAccess.Left}.{nsAccess.Member}");
                        }
                    }
                    else if (scope.Externs.TryGetValue(nsAccess.Member, out ExternDeclaration? e))
                        _resolvedCalls[node] = e;
                }
                return;
            }
            else
            {
                throw new NotImplementedException("Complex callee not supported");
            }

            if (ext != null)
            {
                _resolvedCalls[node] = ext;
                for (int i = 0; i < Math.Min(node.Arguments.Count, ext.Parameters.Count); i++)
                {
                    TypeExpression argType = GetType(node.Arguments[i]);
                    TypeExpression paramType = ext.Parameters[i].Type;
                    if (!IsAssignable(paramType, argType, node.Arguments[i]))
                        throw new TypeCheckException(
                            $"Argument {i + 1} of '{funcName}': cannot pass '{TypeName(argType)}' as '{TypeName(paramType)}'",
                            node.Line);

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

            _resolvedCalls[node] = method;

            if (method.Throws)
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
                        $"Argument {i + 1} of '{funcName}': cannot pass '{TypeName(argType)}' as '{TypeName(paramType)}'",
                        node.Line);

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
                        $"Argument {i + 1} of indirect call: cannot pass '{TypeName(argType)}' as '{TypeName(paramType)}'",
                        node.Line);
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
            foreach (string usingNs in _usingNamespaces)
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

            foreach (string usingNs in _usingNamespaces)
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
                    throw new TypeCheckException($"Enum '{enumInfo.Name}' does not contain member '{node.Member}'", node.Line);

                RecordType(node, new NamedTypeExpression(enumInfo.Name, null, node.Line));
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
                RecordType(node, ext.ReturnType);
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
            EnumInfo? enumInfo = ResolveEnum(node.Object);
            if (enumInfo != null)
            {
                throw new TypeCheckException($"Enum member '{enumInfo.Name}::{node.Member}' must be accessed with '::', not '.'", node.Line);
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
                throw new TypeCheckException($"Interface '{namedIface.Name}' has no method '{node.Member}'", node.Line);
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
                        throw new TypeCheckException($"Cannot access private field '{node.Member}' of class '{field.DeclaringClass}'", node.Line);
                    }
                    if (field.Accessibility == TokenKind.Protected && (_currentClass == null || !IsSubclassOf(_currentClass.Name, field.DeclaringClass)))
                    {
                        throw new TypeCheckException($"Cannot access protected field '{node.Member}' of class '{field.DeclaringClass}'", node.Line);
                    }
                    RecordType(node, field.Type);
                    return;
                }

                if (classInfo.Methods.TryGetValue(node.Member, out (MethodDeclaration Method, string DeclaringClass) mEntry))
                {
                    if (mEntry.Method.Accessibility == TokenKind.Private && !CanAccessPrivate(_currentClass?.Name, mEntry.DeclaringClass))
                    {
                        throw new TypeCheckException($"Cannot access private method '{node.Member}' of class '{mEntry.DeclaringClass}'", node.Line);
                    }
                    if (mEntry.Method.Accessibility == TokenKind.Protected && (_currentClass == null || !IsSubclassOf(_currentClass.Name, mEntry.DeclaringClass)))
                    {
                        throw new TypeCheckException($"Cannot access protected method '{node.Member}' of class '{mEntry.DeclaringClass}'", node.Line);
                    }
                    RecordType(node, mEntry.Method.ReturnType);
                    return;
                }

                throw new TypeCheckException($"Class '{named.Name}' has no field or method '{node.Member}'", node.Line);
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

            throw new TypeCheckException($"'{named.Name}' has no field or method '{node.Member}'", node.Line);
        }

        public void Visit(InterpolatedStringExpression node) =>
            throw new TypeCheckException("String interpolation is not yet implemented.", node.Line);

        public void Visit(NewExpression node)
        {
            if (node.Kind == AllocationKind.Managed)
            {
                throw new TypeCheckException("Managed pointers ('^') are not yet supported pending GC runtime integration.", node.Line);
            }

            TypeExpression resolvedType = ResolveAlias(node.Type);
            node.Type = resolvedType;
            if (resolvedType is not NamedTypeExpression named)
            {
                ReportError(DiagnosticRules.GF1000_GeneralTypeError, node.Line, 0, $"Cannot instantiate non-struct and non-class type '{TypeName(node.Type)}'");
                RecordType(node, Error);
                return;
            }

            if (_classes.TryGetValue(named.Name, out ClassInfo? cInfo))
            {
                if (cInfo.IsAbstract)
                {
                    ReportError(DiagnosticRules.GF1000_GeneralTypeError, node.Line, 0, $"Cannot instantiate abstract class '{cInfo.Name}'");
                    RecordType(node, Error);
                    return;
                }

                ConstructorDeclaration? matchedCtor = null;
                if (cInfo.Constructors.Count == 0)
                {
                    foreach (AstNode arg in node.Arguments)
                        arg.Accept(this);

                    if (node.Arguments.Count != 0)
                    {
                        throw new TypeCheckException($"Class '{cInfo.Name}' does not define a constructor taking {node.Arguments.Count} arguments", node.Line);
                    }
                }
                else
                {
                    List<ConstructorDeclaration> matches = new();
                    foreach (ConstructorDeclaration ctor in cInfo.Constructors)
                    {
                        if (ctor.Parameters.Count != node.Arguments.Count)
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
                                    arg.Accept(this);
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
                                arg.Accept(this);
                        }
                        string argTypes = string.Join(", ", node.Arguments.Select(a => TypeName(GetType(a))));
                        throw new TypeCheckException($"No matching constructor found for '{cInfo.Name}' with arguments ({argTypes})", node.Line);
                    }
                    if (matches.Count > 1)
                    {
                        throw new TypeCheckException($"Call to constructor of '{cInfo.Name}' is ambiguous", node.Line);
                    }

                    matchedCtor = matches[0];
                    _resolvedConstructors[node] = matchedCtor;
                    for (int i = 0; i < node.Arguments.Count; i++)
                    {
                        if (node.Arguments[i] is DefaultExpression def && def.TargetType == null)
                        {
                            RecordType(def, ResolveAlias(matchedCtor.Parameters[i].Type));
                        }
                    }
                }

                TypeExpression resultType = node.Kind switch
                {
                    AllocationKind.Value => new NamedTypeExpression(cInfo.Name, null, node.Line),
                    AllocationKind.Pointer => new PointerTypeExpression(new NamedTypeExpression(cInfo.Name, null, node.Line), false, node.Line),
                    AllocationKind.Managed => new ManagedTypeExpression(new NamedTypeExpression(cInfo.Name, null, node.Line), false, node.Line),
                    _ => throw new Exception($"Unknown allocation kind {node.Kind}")
                };

                RecordType(node, resultType);
                return;
            }

            if (!_structs.TryGetValue(named.Name, out StructInfo? sInfo))
            {
                ReportError(DiagnosticRules.GF1004_TypeNotFound, node.Line, 0, named.Name);
                RecordType(node, Error);
                return;
            }

            ConstructorDeclaration? matchedStructCtor = null;

            if (sInfo.Constructors.Count == 0)
            {
                foreach (AstNode arg in node.Arguments)
                    arg.Accept(this);

                if (node.Arguments.Count != 0)
                {
                    throw new TypeCheckException($"Struct '{sInfo.Name}' does not define a constructor taking {node.Arguments.Count} arguments", node.Line);
                }
            }
            else
            {
                List<ConstructorDeclaration> matches = new();
                foreach (ConstructorDeclaration ctor in sInfo.Constructors)
                {
                    if (ctor.Parameters.Count != node.Arguments.Count)
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
                                arg.Accept(this);
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
                            arg.Accept(this);
                    }
                    string argTypes = string.Join(", ", node.Arguments.Select(a => TypeName(GetType(a))));
                    throw new TypeCheckException($"No matching constructor found for '{sInfo.Name}' with arguments ({argTypes})", node.Line);
                }
                if (matches.Count > 1)
                {
                    throw new TypeCheckException($"Call to constructor of '{sInfo.Name}' is ambiguous", node.Line);
                }

                matchedStructCtor = matches[0];
                _resolvedConstructors[node] = matchedStructCtor;
                for (int i = 0; i < node.Arguments.Count; i++)
                {
                    if (node.Arguments[i] is DefaultExpression def && def.TargetType == null)
                    {
                        RecordType(def, ResolveAlias(matchedStructCtor.Parameters[i].Type));
                    }
                }
            }

            TypeExpression structResultType = node.Kind switch
            {
                AllocationKind.Value => new NamedTypeExpression(sInfo.Name, null, node.Line),
                AllocationKind.Pointer => new PointerTypeExpression(new NamedTypeExpression(sInfo.Name, null, node.Line), false, node.Line),
                AllocationKind.Managed => new ManagedTypeExpression(new NamedTypeExpression(sInfo.Name, null, node.Line), false, node.Line),
                _ => throw new Exception($"Unknown allocation kind {node.Kind}")
            };

            RecordType(node, structResultType);
        }

        public void Visit(DefaultExpression node)
        {
            if (node.TargetType != null)
            {
                ValidateTypeUsage(node.TargetType, node.Line);
                TypeExpression resolved = ResolveAlias(node.TargetType);
                if ((resolved is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression) && !IsNullable(resolved))
                {
                    throw new TypeCheckException($"Cannot get default value of non-nullable type '{TypeName(node.TargetType)}'", node.Line);
                }
                RecordType(node, resolved);
            }
            else
            {
                RecordType(node, new NamedTypeExpression("default", null, node.Line));
            }
        }

        public void Visit(SizeofExpression node)
        {
            TypeExpression targetType = ResolveAlias(node.TargetType);
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
            int size = GetTypeSize(targetType);
            RecordType(node, Int);
            ConstValue.Integer constVal = new ConstValue.Integer(size);
            _constValues[node] = constVal;
        }

        public void Visit(NameofExpression node)
        {
            ValidateNameofTarget(node.Target);
            string name = ExtractName(node.Target);
            TypeExpression strType = new ArrayTypeExpression(Char, name.Length + 1, node.Line);
            RecordType(node, strType);
            ConstValue.String constVal = new ConstValue.String(name);
            _constValues[node] = constVal;
        }
        public void Visit(NamedTypeExpression node) { }
        public void Visit(NestedTypeExpression node) { }
        public void Visit(PointerTypeExpression node) { }
        public void Visit(ManagedTypeExpression node) =>
            throw new TypeCheckException("Managed pointers ('^') are not yet supported pending GC runtime integration.", node.Line);
        public void Visit(ArrayTypeExpression node) { }
        public void Visit(IndexExpression node)
        {
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
                RecordType(node, ptr.Inner);
            }
            else
            {
                throw new TypeCheckException($"Cannot index non-array and non-pointer type '{TypeName(targetType)}'", node.Line);
            }
        }
        public void Visit(BreakStatement node)
        {
            if (_loopDepth <= 0)
            {
                throw new TypeCheckException("Cannot break outside of a loop", node.Line);
            }
        }

        public void Visit(ContinueStatement node)
        {
            if (_loopDepth <= 0)
            {
                throw new TypeCheckException("Cannot continue outside of a loop", node.Line);
            }
        }

        public void Visit(AttributeNode node) { }
        public void Visit(ExternDeclaration node)
        {
            if (node.Name == "__gflat_alloc" || node.Name == "__gflat_free")
            {
                ValidateAllocatorHookSignature(node.Name, node.ReturnType, node.Parameters, node.Line);
            }
        }

        private void ValidateAllocatorHookSignature(string name, TypeExpression returnType, IReadOnlyList<Parameter> parameters, int line)
        {
            if (name == "__gflat_alloc")
            {
                TypeExpression resolvedRet = ResolveAlias(returnType);
                if (resolvedRet is not PointerTypeExpression)
                {
                    ReportError(DiagnosticRules.GF1011_InvalidAllocatorSignature, line, 0, "__gflat_alloc", "return type must be a pointer type (e.g. 'void*')");
                }
                if (parameters.Count != 1 || !IsInteger(ResolveAlias(parameters[0].Type)))
                {
                    ReportError(DiagnosticRules.GF1011_InvalidAllocatorSignature, line, 0, "__gflat_alloc", "must accept exactly one integer parameter (e.g. 'ulong size')");
                }
            }
            else if (name == "__gflat_free")
            {
                TypeExpression resolvedRet = ResolveAlias(returnType);
                if (resolvedRet is not NamedTypeExpression namedRet || namedRet.Name != "void")
                {
                    ReportError(DiagnosticRules.GF1011_InvalidAllocatorSignature, line, 0, "__gflat_free", "return type must be 'void'");
                }
                if (parameters.Count != 1 || ResolveAlias(parameters[0].Type) is not PointerTypeExpression)
                {
                    ReportError(DiagnosticRules.GF1011_InvalidAllocatorSignature, line, 0, "__gflat_free", "must accept exactly one pointer parameter (e.g. 'void* ptr')");
                }
            }
        }

        public static string TypeName(TypeExpression type) => type switch
        {
            NamedTypeExpression n => n.Name,
            NestedTypeExpression nested => nested.TypeArguments.Count > 0
                ? $"{TypeName(nested.Parent)}::{nested.Member}<{string.Join(", ", nested.TypeArguments.Select(TypeName))}>"
                : $"{TypeName(nested.Parent)}::{nested.Member}",
            PointerTypeExpression p => (p.IsReadOnly ? "readonly " : "") + TypeName(p.Inner) + "*",
            ManagedTypeExpression m => (m.IsReadOnly ? "readonly " : "") + TypeName(m.Inner) + "^",
            ArrayTypeExpression a => TypeName(a.ElementType) + (a.Size.HasValue ? $"[{a.Size}]" : "[]"),
            FunctionPointerTypeExpression f => $"{TypeName(f.ReturnType)}({string.Join(", ", f.ParameterTypes.Select(TypeName))}){(f.IsManaged ? "^" : "*")}{(f.IsNullable ? "?" : "")}",
            _ => "unknown"
        };

        public static string TypeNamePublic(TypeExpression type) => TypeName(type);

        public void Visit(GlobalExpression node) { }
        public void Visit(FunctionPointerTypeExpression node) { }
        public void Visit(AliasDeclaration node)
        {
            if (_localAliases.Count > 0)
                _localAliases.Peek()[node.Name] = node.TargetType;
            else if (_currentNamespace != null)
                _currentNamespace.Aliases[node.Name] = node.TargetType;
            else
                _globalScope.Aliases[node.Name] = node.TargetType;
        }

        private void RegisterEnum(EnumDeclaration enumDecl, NamespaceScope scope, string nsPath)
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

            long nextValue = 0;
            foreach (EnumMemberDeclaration memberDecl in enumDecl.Members)
            {
                if (info.Members.ContainsKey(memberDecl.Name))
                {
                    throw new TypeCheckException(
                        $"Enum '{enumDecl.Name}' already contains a member named '{memberDecl.Name}'",
                        memberDecl.Line);
                }

                if (memberDecl.Value != null)
                {
                    nextValue = EvaluateConstantInt(memberDecl.Value, info);
                }

                info.Members[memberDecl.Name] = nextValue;
                nextValue++;
            }

            scope.Enums[enumDecl.Name] = info;
            _enums[enumDecl.Name] = info;
            if (nsPath.Length > 0)
            {
                _enums[$"{nsPath}::{enumDecl.Name}"] = info;
            }
        }

        private long EvaluateConstantInt(AstNode expr, EnumInfo enumInfo)
        {
            if (expr is LiteralExpression lit)
            {
                if (lit.Token.Kind == TokenKind.IntLiteral)
                    return long.Parse(lit.Token.Text);
                if (lit.Token.Kind == TokenKind.HexInt)
                {
                    string text = lit.Token.Text;
                    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        text = text[2..];
                    return Convert.ToInt64(text, 16);
                }
                if (lit.Token.Kind == TokenKind.LongLiteral)
                {
                    string text = lit.Token.Text.TrimEnd('L', 'l');
                    return long.Parse(text);
                }
                if (lit.Token.Kind == TokenKind.CharLiteral)
                {
                    string text = lit.Token.Text;
                    if (text.Length >= 3 && text[0] == '\'' && text[^1] == '\'')
                    {
                        string inner = text[1..^1];
                        if (inner.StartsWith("\\"))
                        {
                            return inner switch
                            {
                                "\\0" => '\0',
                                "\\n" => '\n',
                                "\\r" => '\r',
                                "\\t" => '\t',
                                "\\\\" => '\\',
                                "\\'" => '\'',
                                _ => inner.Length > 1 ? inner[1] : 0
                            };
                        }
                        return inner.Length > 0 ? inner[0] : 0;
                    }
                }
            }
            else if (expr is UnaryExpression u)
            {
                if (u.Operator == TokenKind.Minus)
                    return -EvaluateConstantInt(u.Operand, enumInfo);
                if (u.Operator == TokenKind.Plus)
                    return EvaluateConstantInt(u.Operand, enumInfo);
                if (u.Operator == TokenKind.Bang)
                    return ~EvaluateConstantInt(u.Operand, enumInfo);
            }
            else if (expr is BinaryExpression bin)
            {
                long left = EvaluateConstantInt(bin.Left, enumInfo);
                long right = EvaluateConstantInt(bin.Right, enumInfo);
                return bin.Operator switch
                {
                    TokenKind.Plus => left + right,
                    TokenKind.Minus => left - right,
                    TokenKind.Star => left * right,
                    TokenKind.Slash => right != 0 ? left / right : throw new TypeCheckException("Division by zero in enum member value", expr.Line),
                    TokenKind.Percent => right != 0 ? left % right : throw new TypeCheckException("Division by zero in enum member value", expr.Line),
                    TokenKind.Pipe => left | right,
                    TokenKind.Ampersand => left & right,
                    TokenKind.Caret => left ^ right,
                    TokenKind.Less => left < right ? 1 : 0,
                    TokenKind.Greater => left > right ? 1 : 0,
                    TokenKind.LessEquals => left <= right ? 1 : 0,
                    TokenKind.GreaterEquals => left >= right ? 1 : 0,
                    TokenKind.EqualsEquals => left == right ? 1 : 0,
                    TokenKind.NotEquals => left != right ? 1 : 0,
                    _ => throw new TypeCheckException($"Operator '{bin.Operator}' not supported in constant expression", expr.Line)
                };
            }
            else if (expr is IdentifierExpression ident)
            {
                if (enumInfo.Members.TryGetValue(ident.Name, out long memberVal))
                    return memberVal;
                EnumInfo? otherEnum = ResolveEnum(ident);
                if (otherEnum != null && otherEnum.Members.TryGetValue(ident.Name, out long otherVal))
                    return otherVal;
                throw new TypeCheckException($"Enum member or constant '{ident.Name}' not found", expr.Line);
            }
            else if (expr is NamespaceAccessExpression nsAccess)
            {
                EnumInfo? otherEnum = ResolveEnum(nsAccess.Left);
                if (otherEnum != null && otherEnum.Members.TryGetValue(nsAccess.Member, out long memberVal))
                    return memberVal;
                throw new TypeCheckException($"Enum member '{nsAccess.Member}' not found", expr.Line);
            }
            else if (expr is MemberAccessExpression memAccess)
            {
                EnumInfo? otherEnum = ResolveEnum(memAccess.Object);
                if (otherEnum != null && otherEnum.Members.TryGetValue(memAccess.Member, out long memberVal))
                    return memberVal;
                throw new TypeCheckException($"Enum member '{memAccess.Member}' not found", expr.Line);
            }

            throw new TypeCheckException("Enum member value must be a constant integer expression", expr.Line);
        }

        public EnumInfo? ResolveEnum(AstNode node)
        {
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
                if (_enums.TryGetValue(ident.Name, out EnumInfo? fallback))
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
            NamespaceScope scope = _currentNamespace ?? _globalScope;
            if (!scope.Enums.ContainsKey(node.Name))
            {
                RegisterEnum(node, scope, "");
            }
        }

        public void Visit(EnumMemberDeclaration node) { }

        public void Visit(CastExpression node)
        {
            node.Operand.Accept(this);
            TypeExpression sourceType = ResolveAlias(GetType(node.Operand));
            TypeExpression targetType = ResolveAlias(node.TargetType);
            ValidateTypeUsage(targetType, node.Line);

            if (!IsValidCast(sourceType, targetType))
            {
                throw new TypeCheckException($"Cannot cast '{TypeName(sourceType)}' to '{TypeName(targetType)}'", node.Line);
            }

            RecordType(node, node.TargetType);
        }

        private bool IsValidCast(TypeExpression src, TypeExpression dst)
        {
            src = ResolveAlias(src);
            dst = ResolveAlias(dst);

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
            if (src is ManagedTypeExpression && dst is ManagedTypeExpression)
            {
                return true;
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
            int prevLoopDepth = _loopDepth;
            _loopDepth = 0;
            try
            {
                _lambdaScopeBoundaries.Push(_scopes.Count);
                _lambdaStaticStack.Push(node.IsStatic);
                _actualReturnTypes.Push(new List<TypeExpression>());

                PushScope();
                List<TypeExpression> paramTypes = new();
                foreach (Parameter p in node.Parameters)
                {
                    ValidateTypeUsage(p.Type, p.Line);
                    TypeExpression pType = ResolveAlias(p.Type);
                    if (HasDestructor(pType))
                    {
                        throw new TypeCheckException($"Parameter '{p.Name}' cannot have type '{TypeName(pType)}' because types with destructors cannot be passed by value. Use a pointer instead ('{TypeName(pType)}*').", p.Line);
                    }
                    paramTypes.Add(pType);
                    DeclareVariable(p.Name, pType, p.Line);
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

                PopScope();
                _actualReturnTypes.Pop();
                _lambdaStaticStack.Pop();
                _lambdaScopeBoundaries.Pop();

                _lambdaReturnTypes[node] = returnType;
                FunctionPointerTypeExpression fnType = new FunctionPointerTypeExpression(returnType, paramTypes, isManaged: false, isNullable: false, node.Line);
                RecordType(node, fnType);
            }
            finally
            {
                _loopDepth = prevLoopDepth;
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

        private bool TryResolveBinaryOperatorForTypes(TokenKind opKind, TypeExpression left, TypeExpression right, out string structName, out OperatorDeclaration opDecl)
        {
            structName = "";
            opDecl = null!;

            TypeExpression resLeft = ResolveAlias(left);
            TypeExpression resRight = ResolveAlias(right);

            if (resLeft is NamedTypeExpression namedLeft && _structs.TryGetValue(namedLeft.Name, out StructInfo? leftInfo))
            {
                foreach (OperatorDeclaration op in leftInfo.Operators)
                {
                    if (op.OperatorKind == opKind && op.Parameters.Count == 2)
                    {
                        TypeExpression p0 = ResolveAlias(op.Parameters[0].Type);
                        TypeExpression p1 = ResolveAlias(op.Parameters[1].Type);
                        if (TypesMatch(resLeft, p0) && TypesMatch(resRight, p1))
                        {
                            structName = leftInfo.Name;
                            opDecl = op;
                            return true;
                        }
                    }
                }
            }

            if (resRight is NamedTypeExpression namedRight && _structs.TryGetValue(namedRight.Name, out StructInfo? rightInfo))
            {
                if (rightInfo.Name != structName)
                {
                    foreach (OperatorDeclaration op in rightInfo.Operators)
                    {
                        if (op.OperatorKind == opKind && op.Parameters.Count == 2)
                        {
                            TypeExpression p0 = ResolveAlias(op.Parameters[0].Type);
                            TypeExpression p1 = ResolveAlias(op.Parameters[1].Type);
                            if (TypesMatch(resLeft, p0) && TypesMatch(resRight, p1))
                            {
                                structName = rightInfo.Name;
                                opDecl = op;
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
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
            if (node.Prefix == "c")
            {
                if (node.Scope != null)
                {
                    throw new TypeCheckException($"Built-in C-string prefix 'c' cannot be qualified with a scope", node.Line);
                }
                TypeExpression charPtrType = new PointerTypeExpression(Char, false, node.Line, isReadOnly: true);
                _types[node] = charPtrType;
                _constValues[node] = new ConstValue.String(node.Literal.Token.Text[1..^1]);
                return;
            }

            throw new TypeCheckException($"Unknown string prefix '{node.Prefix}' on line {node.Line}", node.Line);
        }

        public void Visit(ThrowStatement node)
        {
            node.Expression.Accept(this);
            TypeExpression exprType = ResolveAlias(_types[node.Expression]);

            TypeExpression? innerType = null;
            if (exprType is PointerTypeExpression ptr)
            {
                innerType = ResolveAlias(ptr.Inner);
            }
            else if (exprType is ManagedTypeExpression mgd)
            {
                innerType = ResolveAlias(mgd.Inner);
            }

            if (innerType is not NamedTypeExpression named || !TypeDerivesFromClass(named, "Exception"))
            {
                throw new TypeCheckException($"Cannot throw non-exception type '{TypeName(exprType)}'. Thrown expressions must be a pointer to 'Exception' or a subclass of 'Exception'", node.Line);
            }

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
                    if (method.Name != "main" && !method.Throws)
                    {
                        throw new TypeCheckException("Unhandled exception: function must be marked 'throws' or exception must be caught in a try/catch block", node.Line);
                    }
                }
                else
                {
                    throw new TypeCheckException("Unhandled exception: exception must be caught in a try/catch block", node.Line);
                }
            }
        }

        public void Visit(TryStatement node)
        {
            _tryStack.Push(node);
            try
            {
                node.TryBlock.Accept(this);
            }
            finally
            {
                _tryStack.Pop();
            }

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

                    if (typeName == null || !TypeDerivesFromClass(new NamedTypeExpression(typeName, null, clause.Line), "Exception"))
                    {
                        throw new TypeCheckException($"Catch type '{TypeName(clause.ExceptionType)}' must be 'Exception' or a subclass of 'Exception'", clause.Line);
                    }

                    foreach (string prevCaught in caughtTypeNames)
                    {
                        if (prevCaught == "Exception" || (typeName != prevCaught && TypeDerivesFromClass(new NamedTypeExpression(typeName, null, clause.Line), prevCaught)) || typeName == prevCaught)
                        {
                            throw new TypeCheckException($"Catch clause for '{typeName}' is unreachable because a previous catch clause catches '{prevCaught}'", clause.Line);
                        }
                    }
                    caughtTypeNames.Add(typeName);
                }

                clause.Accept(this);
            }
        }

        public void Visit(CatchClause node)
        {
            PushScope();
            try
            {
                if (node.VariableName != null && node.ExceptionType != null)
                {
                    TypeExpression varType = node.ExceptionType;
                    if (varType is not PointerTypeExpression && varType is not ManagedTypeExpression)
                    {
                        varType = new PointerTypeExpression(varType, false, node.Line);
                    }
                    DeclareVariable(node.VariableName, varType, node.Line);
                }
                node.Body.Accept(this);
            }
            finally
            {
                PopScope();
            }
        }

        private void CheckThrowingCall(CallExpression node, string calleeName)
        {
            _throwingCalls.Add(node);

            bool caught = false;
            foreach (TryStatement tryStmt in _tryStack)
            {
                if (TryCatchesType(tryStmt, "Exception"))
                {
                    caught = true;
                    break;
                }
            }

            if (!caught)
            {
                if (_currentFunction is MethodDeclaration currentMethod)
                {
                    if (currentMethod.Name != "main" && !currentMethod.Throws)
                    {
                        throw new TypeCheckException($"Call to throwing function '{calleeName}' must be enclosed in a try/catch or the calling function must be marked 'throws'", node.Line);
                    }
                }
                else if (_currentFunction != null)
                {
                    throw new TypeCheckException($"Call to throwing function '{calleeName}' must be enclosed in a try/catch block", node.Line);
                }
            }
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

                if (catchName == "Exception" || catchName == thrownTypeName || TypeDerivesFromClass(new NamedTypeExpression(thrownTypeName, null, 0), catchName!))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

