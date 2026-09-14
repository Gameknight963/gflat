using gflat.ast;
using gflat.CompileExceptions;
using gflat.comptime;

namespace gflat
{
    public class TypeChecker : IVisitor
    {
        private readonly Dictionary<AstNode, TypeExpression> _types = new();
        private readonly Stack<Dictionary<string, TypeExpression>> _scopes = new();

        private readonly ConstEvaluator _constEvaluator;
        private readonly Dictionary<string, ConstValue> _constVariablesByName = new();
        private readonly Dictionary<AstNode, ConstValue> _constValues = new();
        private readonly HashSet<string> _constVariableNames = new();

        public TypeChecker()
        {
            _constEvaluator = new ConstEvaluator(this);
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
        private NamespaceScope _currentNamespace = null!;
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
        private readonly HashSet<AstNode> _canThrowFunctions = new();
        private readonly HashSet<AstNode> _throwingCalls = new();
        private readonly List<(AstNode Call, AstNode? Caller, List<TryStatement> Tries)> _callSites = new();
        private readonly List<(ThrowStatement Throw, AstNode? Caller, List<TryStatement> Tries, string ThrownTypeName)> _throwSites = new();

        public bool CanFunctionThrow(AstNode func) => _canThrowFunctions.Contains(func);
        public bool CanCallThrow(AstNode call) => _throwingCalls.Contains(call);

        public bool TryGetOperatorTarget(BinaryExpression node, out (string StructName, OperatorDeclaration Operator) target) =>
            _operatorTargets.TryGetValue(node, out target);

        public bool TryGetUnaryOperatorTarget(UnaryExpression node, out (string StructName, OperatorDeclaration Operator) target) =>
            _unaryOperatorTargets.TryGetValue(node, out target);

        public bool TryGetCompoundOperatorTarget(AssignmentExpression node, out (string StructName, OperatorDeclaration Operator) target) =>
            _compoundOperatorTargets.TryGetValue(node, out target);

        public string GetOperatorNamespace(OperatorDeclaration op) =>
            _operatorNamespaces.TryGetValue(op, out string? ns) ? ns : "";

        public TypeExpression GetLambdaReturnType(LambdaExpression node) => _lambdaReturnTypes[node];

        public class StringPrefixHandler
        {
            public string Prefix { get; }
            public string FullName { get; }
            public string Namespace { get; }
            public string? EnclosingTypeName { get; }
            public bool IsConstructor { get; }
            public ConstructorDeclaration? Constructor { get; }
            public MethodDeclaration? Method { get; }
            public TypeExpression ReturnType { get; }
            public bool IsConst { get; }
            public Parameter Parameter { get; }

            public StringPrefixHandler(
                string prefix,
                string fullName,
                string ns,
                string? enclosingTypeName,
                bool isConstructor,
                ConstructorDeclaration? constructor,
                MethodDeclaration? method,
                TypeExpression returnType,
                bool isConst,
                Parameter parameter)
            {
                Prefix = prefix;
                FullName = fullName;
                Namespace = ns;
                EnclosingTypeName = enclosingTypeName;
                IsConstructor = isConstructor;
                Constructor = constructor;
                Method = method;
                ReturnType = returnType;
                IsConst = isConst;
                Parameter = parameter;
            }
        }

        private readonly List<StringPrefixHandler> _prefixHandlers = new();
        private readonly Dictionary<PrefixedStringLiteralExpression, StringPrefixHandler> _resolvedPrefixHandlers = new();
        private readonly HashSet<string> _usingNamespaces = new();
        private string _currentNamespacePath = "";
        private readonly Dictionary<string, StructDeclaration> _genericStructs = new();
        private readonly Dictionary<string, ClassDeclaration> _genericClasses = new();
        private readonly Dictionary<string, MethodDeclaration> _genericMethods = new();
        private CompilationUnit? _compilationUnit = null;

        public StringPrefixHandler? GetResolvedPrefixHandler(PrefixedStringLiteralExpression node) =>
            _resolvedPrefixHandlers.TryGetValue(node, out StringPrefixHandler? handler) ? handler : null;

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

        public StructInfo? GetStruct(string name) =>
            _structs.TryGetValue(name, out StructInfo? info) ? info : null;

        public EnumInfo? GetEnum(string name) =>
            _enums.TryGetValue(name, out EnumInfo? info) ? info : null;

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
            public List<OperatorDeclaration> Operators = new();
            public List<string> Interfaces = new();
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
            public List<MethodDeclaration> VirtualMethods = new();
            public Dictionary<string, int> VTableSlots = new(); // Method name -> slot index
            public DestructorDeclaration? Destructor = null;
            public int DestructorSlot = -1; // -1 if non-virtual or no destructor
            public int Line = 0;
            public int FieldIndex(string name) => Fields.FindIndex(f => f.Name == name);
        }

        private readonly Dictionary<string, ClassInfo> _classes = new();
        public ClassInfo? GetClass(string name) => _classes.TryGetValue(name, out ClassInfo? info) ? info : null;
        public bool IsClass(string name) => _classes.ContainsKey(name);
        private ClassInfo? _currentClass = null;
        private ConstructorDeclaration? _currentConstructor = null;

        private readonly Dictionary<ConstructorDeclaration, ConstructorDeclaration> _resolvedBaseConstructors = new();
        public ConstructorDeclaration? GetResolvedBaseConstructor(ConstructorDeclaration ctor) =>
            _resolvedBaseConstructors.TryGetValue(ctor, out ConstructorDeclaration? baseCtor) ? baseCtor : null;

        private readonly Dictionary<CallExpression, (ClassInfo Class, int SlotIndex, MethodDeclaration Method)> _virtualMethodCalls = new();
        public bool TryGetVirtualMethodCall(CallExpression node, out (ClassInfo Class, int SlotIndex, MethodDeclaration Method) call) =>
            _virtualMethodCalls.TryGetValue(node, out call);

        private readonly Dictionary<CallExpression, (ClassInfo Class, bool IsVirtual, int SlotIndex)> _classDestructorCalls = new();
        public bool TryGetClassDestructorCall(CallExpression node, out (ClassInfo Class, bool IsVirtual, int SlotIndex) call) =>
            _classDestructorCalls.TryGetValue(node, out call);

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

        public class EnumInfo
        {
            public string Name = "";
            public string Namespace = "";
            public TypeExpression UnderlyingType = new NamedTypeExpression("int", null, 0);
            public Dictionary<string, long> Members = new();
            public TokenKind Accessibility = TokenKind.Public;
        }

        private class NamespaceScope
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
                return type;
            throw new Exception($"No type recorded for node {node.GetType().Name}");
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
                throw new TypeCheckException($"Variable '{name}' already declared in this scope", line);
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
                throw new TypeCheckException($"Cannot use function '{name}' as a value without '&'. Did you mean '&{name}'?", line);

            throw new TypeCheckException($"Unknown variable '{name}'", line);
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

        private static bool IsNumeric(TypeExpression type) =>
            type is NamedTypeExpression n && n.Name is "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "nint" or "nuint" or "float" or "double" or "extralong" or "char";

        private static bool IsNullable(TypeExpression type) =>
            type is PointerTypeExpression { IsNullable: true } or
            ManagedTypeExpression { IsNullable: true } or
            FunctionPointerTypeExpression { IsNullable: true };

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
                ResolveClassHierarchy(clsInfo, new HashSet<string>(), new HashSet<string>());
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
                }
            }
            else if (type is PointerTypeExpression ptr)
            {
                var resolvedInner = ResolveAlias(ptr.Inner);
                if (resolvedInner != ptr.Inner)
                    return new PointerTypeExpression(resolvedInner, ptr.IsNullable, ptr.Line);
            }
            else if (type is ManagedTypeExpression mgd)
            {
                var resolvedInner = ResolveAlias(mgd.Inner);
                if (resolvedInner != mgd.Inner)
                    return new ManagedTypeExpression(resolvedInner, mgd.IsNullable, mgd.Line);
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
            if (resolved is NamedTypeExpression named && IsInterface(named))
            {
                throw new TypeCheckException($"Cannot use interface '{named.Name}' as a value type. Interfaces must be used as pointers ('{named.Name}*')", line);
            }
            else if (resolved is ArrayTypeExpression arr)
            {
                ValidateTypeUsage(arr.ElementType, line);
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

        private bool TypesMatch(TypeExpression a, TypeExpression b)
        {
            a = ResolveAlias(a);
            b = ResolveAlias(b);

            if (a is NamedTypeExpression na && b is NamedTypeExpression nb)
                return na.Name == nb.Name;
            if (a is PointerTypeExpression pa && b is PointerTypeExpression pb)
                return pa.IsNullable == pb.IsNullable && pa.IsReadOnly == pb.IsReadOnly && TypesMatch(pa.Inner, pb.Inner);
            if (a is ManagedTypeExpression ma && b is ManagedTypeExpression mb)
                return ma.IsNullable == mb.IsNullable && ma.IsReadOnly == mb.IsReadOnly && TypesMatch(ma.Inner, mb.Inner);
            if (a is ArrayTypeExpression aa && b is ArrayTypeExpression ab)
                return TypesMatch(aa.ElementType, ab.ElementType) && (aa.Size == ab.Size || aa.Size == null || ab.Size == null);
            if (a is FunctionPointerTypeExpression fa && b is FunctionPointerTypeExpression fb)
            {
                if (fa.IsManaged != fb.IsManaged || fa.IsNullable != fb.IsNullable)
                    return false;
                if (!TypesMatch(fa.ReturnType, fb.ReturnType))
                    return false;
                if (fa.ParameterTypes.Count != fb.ParameterTypes.Count)
                    return false;
                for (int i = 0; i < fa.ParameterTypes.Count; i++)
                {
                    if (!TypesMatch(fa.ParameterTypes[i], fb.ParameterTypes[i]))
                        return false;
                }
                return true;
            }

            return false;
        }

        private bool IsAssignable(TypeExpression target, TypeExpression source, AstNode? valueNode = null)
        {
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

            // default is assignable to any non-void type
            if (source is NamedTypeExpression { Name: "default" })
            {
                if (target is not NamedTypeExpression { Name: "void" })
                {
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
            _currentNamespacePath = "";
            _usingNamespaces.Clear();
            foreach (UsingDirective u in node.Usings)
                _usingNamespaces.Add(u.Name);

            // first pass: register top-level members in global scope
            foreach (AstNode member in node.Members)
                RegisterMemberInScope(member, _globalScope, "");

            // and nested namespaces
            foreach (NamespaceDeclaration ns in node.Namespaces)
                BuildNamespaceScope(ns, _globalScope, "");

            // resolve class hierarchies and vtable layouts
            ResolveClassHierarchies();

            // second pass: type check bodies
            _currentNamespace = _globalScope;
            for (int i = 0; i < node.Members.Count; i++)
                node.Members[i].Accept(this);

            foreach (NamespaceDeclaration ns in node.Namespaces)
                ns.Accept(this);

            PropagateCanThrow();
        }

        private void ResolveClassHierarchies()
        {
            // First partition base class vs interfaces for all classes
            foreach (ClassInfo cls in _classes.Values)
            {
                List<string> remainingInterfaces = new();
                foreach (string item in cls.Interfaces)
                {
                    if (_classes.ContainsKey(item))
                    {
                        if (cls.BaseClass != null)
                        {
                            throw new TypeCheckException($"Class '{cls.Name}' cannot inherit from multiple classes ('{cls.BaseClass}' and '{item}')", cls.Line);
                        }
                        cls.BaseClass = item;
                    }
                    else
                    {
                        remainingInterfaces.Add(item);
                    }
                }
                cls.Interfaces = remainingInterfaces;
            }

            HashSet<string> visited = new();
            HashSet<string> visiting = new();

            foreach (ClassInfo cls in _classes.Values)
            {
                ResolveClassHierarchy(cls, visiting, visited);
            }
        }

        private void ResolveClassHierarchy(ClassInfo cls, HashSet<string> visiting, HashSet<string> visited)
        {
            if (visited.Contains(cls.Name)) return;
            if (visiting.Contains(cls.Name))
                throw new TypeCheckException($"Circular inheritance detected involving class '{cls.Name}'", cls.Line);

            visiting.Add(cls.Name);

            if (cls.BaseClass != null)
            {
                if (!_classes.TryGetValue(cls.BaseClass, out ClassInfo? baseInfo))
                {
                    throw new TypeCheckException($"Class '{cls.Name}' inherits from unknown class '{cls.BaseClass}'", cls.Line);
                }

                ResolveClassHierarchy(baseInfo, visiting, visited);

                // Inherit base fields in prefix order
                cls.Fields.AddRange(baseInfo.Fields);
                foreach (KeyValuePair<string, FieldDeclaration> kvp in baseInfo.FieldDeclarationsByName)
                {
                    cls.FieldDeclarationsByName[kvp.Key] = kvp.Value;
                }

                // Inherit base vtable slots
                cls.VirtualMethods.AddRange(baseInfo.VirtualMethods);
                foreach (var kvp in baseInfo.VTableSlots)
                {
                    cls.VTableSlots[kvp.Key] = kvp.Value;
                }

                // Inherit base methods (not overridden)
                foreach (var kvp in baseInfo.Methods)
                {
                    if (!cls.Methods.ContainsKey(kvp.Key))
                    {
                        cls.Methods[kvp.Key] = kvp.Value;
                    }
                }
                // Inherit base interfaces
                foreach (string baseIface in baseInfo.Interfaces)
                {
                    if (!cls.Interfaces.Contains(baseIface))
                    {
                        cls.Interfaces.Add(baseIface);
                    }
                }

                // Inherit base destructor slot
                if (baseInfo.DestructorSlot >= 0)
                {
                    cls.DestructorSlot = baseInfo.DestructorSlot;
                }
            }

            // Append cls's own declared fields
            foreach (FieldDeclaration f in cls.FieldDeclarations)
            {
                if (cls.FieldIndex(f.Name) >= 0)
                    throw new TypeCheckException($"Class '{cls.Name}' cannot declare field '{f.Name}' because it is already declared in a base class", f.Line);
                cls.Fields.Add((f.Name, f.Type, f.Accessibility, cls.Name));
                cls.FieldDeclarationsByName[f.Name] = f;
            }

            // Process methods: override, virtual, abstract, normal
            foreach (var kvp in cls.Methods.Where(m => m.Value.DeclaringClass == cls.Name).ToList())
            {
                MethodDeclaration method = kvp.Value.Method;
                if (method.IsOverride)
                {
                    if (!cls.VTableSlots.TryGetValue(method.Name, out int slot))
                    {
                        throw new TypeCheckException($"Method '{method.Name}' in class '{cls.Name}' is marked override but does not override any virtual or abstract method in a base class", method.Line);
                    }
                    MethodDeclaration baseMethod = cls.VirtualMethods[slot];
                    if (baseMethod.IsReadOnly && !method.IsReadOnly)
                        throw new TypeCheckException($"Overriding method '{method.Name}' in class '{cls.Name}' must be marked readonly to match base method", method.Line);
                    if (!TypesMatch(ResolveAlias(method.ReturnType), ResolveAlias(baseMethod.ReturnType)))
                        throw new TypeCheckException($"Overriding method '{method.Name}' in class '{cls.Name}' has return type '{TypeName(method.ReturnType)}' which does not match base method return type '{TypeName(baseMethod.ReturnType)}'", method.Line);
                    if (method.Parameters.Count != baseMethod.Parameters.Count)
                        throw new TypeCheckException($"Overriding method '{method.Name}' in class '{cls.Name}' has {method.Parameters.Count} parameters, but base method has {baseMethod.Parameters.Count}", method.Line);
                    for (int p = 0; p < method.Parameters.Count; p++)
                    {
                        if (!TypesMatch(ResolveAlias(method.Parameters[p].Type), ResolveAlias(baseMethod.Parameters[p].Type)))
                            throw new TypeCheckException($"Parameter '{method.Parameters[p].Name}' of overriding method '{method.Name}' has type '{TypeName(method.Parameters[p].Type)}' which does not match base parameter type '{TypeName(baseMethod.Parameters[p].Type)}'", method.Parameters[p].Line);
                    }
                    if (baseMethod.Accessibility == TokenKind.Public && method.Accessibility != TokenKind.Public)
                        throw new TypeCheckException($"Overriding method '{method.Name}' cannot reduce accessibility of public base method", method.Line);
                    if (baseMethod.Accessibility == TokenKind.Protected && method.Accessibility == TokenKind.Private)
                        throw new TypeCheckException($"Overriding method '{method.Name}' cannot reduce accessibility of protected base method", method.Line);

                    cls.VirtualMethods[slot] = method;
                    cls.Methods[method.Name] = (method, cls.Name);
                }
                else if (method.IsVirtual || method.IsAbstract)
                {
                    if (cls.VTableSlots.ContainsKey(method.Name))
                    {
                        throw new TypeCheckException($"Method '{method.Name}' in class '{cls.Name}' hides base virtual method without 'override' keyword", method.Line);
                    }
                    int slot = cls.VirtualMethods.Count;
                    cls.VirtualMethods.Add(method);
                    cls.VTableSlots[method.Name] = slot;
                    cls.Methods[method.Name] = (method, cls.Name);
                }
            }

            // Handle virtual destructor slot
            bool isVirtualDtor = cls.Destructor?.IsVirtual == true || (cls.Destructor != null && cls.VirtualMethods.Count > 0) || cls.DestructorSlot >= 0;
            if (isVirtualDtor)
            {
                if (cls.DestructorSlot < 0)
                {
                    cls.DestructorSlot = cls.VirtualMethods.Count;
                    cls.VTableSlots["$dtor"] = cls.DestructorSlot;
                    cls.VirtualMethods.Add(new MethodDeclaration("$dtor", Void, new List<Parameter>(), null, TokenKind.Public, false, true, false, false, cls.Destructor?.Line ?? cls.Line));
                }
                else
                {
                    cls.VirtualMethods[cls.DestructorSlot] = new MethodDeclaration("$dtor", Void, new List<Parameter>(), null, TokenKind.Public, false, false, true, false, cls.Destructor?.Line ?? cls.Line);
                }
            }

            // If concrete class, ensure all abstract methods in vtable are implemented
            if (!cls.IsAbstract)
            {
                for (int slot = 0; slot < cls.VirtualMethods.Count; slot++)
                {
                    MethodDeclaration vm = cls.VirtualMethods[slot];
                    if (vm.IsAbstract)
                    {
                        string declaringClass = cls.Methods[vm.Name].DeclaringClass;
                        throw new TypeCheckException($"Class '{cls.Name}' must implement abstract method '{declaringClass}.{vm.Name}' or be declared abstract", cls.Line);
                    }
                }
            }

            visiting.Remove(cls.Name);
            visited.Add(cls.Name);
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
                RegisterPrefixFromAttributes(method.Attributes, nsPath, null, null, method, null, null, method.Line);
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
                RegisterPrefixFromAttributes(str.Attributes, nsPath, str.Name, null, null, str, null, str.Line);
                foreach (AstNode m in str.Members)
                {
                    if (m is FieldDeclaration field)
                    {
                        info.Fields.Add((field.Name, field.Type));
                        info.FieldDeclarations.Add(field);
                        info.FieldDeclarationsByName[field.Name] = field;
                    }
                    else if (m is MethodDeclaration sm)
                    {
                        info.Methods[sm.Name] = sm;
                        _functionNamespaces[sm] = nsPath;
                        RegisterPrefixFromAttributes(sm.Attributes, nsPath, str.Name, null, sm, null, null, sm.Line);
                    }
                    else if (m is ConstructorDeclaration ctor)
                    {
                        info.Constructors.Add(ctor);
                        RegisterPrefixFromAttributes(ctor.Attributes, nsPath, str.Name, ctor, null, null, null, ctor.Line);
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
                RegisterPrefixFromAttributes(cls.Attributes, nsPath, cls.Name, null, null, null, cls, cls.Line);

                foreach (AstNode m in cls.Members)
                {
                    if (m is FieldDeclaration field)
                    {
                        info.FieldDeclarations.Add(field);
                    }
                    else if (m is ConstructorDeclaration ctor)
                    {
                        info.Constructors.Add(ctor);
                        RegisterPrefixFromAttributes(ctor.Attributes, nsPath, cls.Name, ctor, null, null, null, ctor.Line);
                    }
                    else if (m is DestructorDeclaration dtor)
                    {
                        if (info.Destructor != null)
                            throw new TypeCheckException($"Class '{cls.Name}' already defines a destructor", dtor.Line);
                        if (dtor.Name != cls.Name)
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
                        _functionNamespaces[cm] = nsPath;
                        RegisterPrefixFromAttributes(cm.Attributes, nsPath, cls.Name, null, cm, null, null, cm.Line);
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
            _currentClass = _classes[node.Name];

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
                    ValidateTypeUsage(field.Type, field.Line);
                    TypeExpression fieldType = ResolveAlias(field.Type);
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
                                DeclareVariable(p.Name, ResolveAlias(p.Type), p.Line);
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
            }

            _currentClass = prevClass;
        }

        public void Visit(StructDeclaration node)
        {
            if (node.IsGeneric) return;

            StructInfo? previousStruct = _currentStruct;
            _currentStruct = _structs[node.Name];

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
                    ValidateTypeUsage(field.Type, field.Line);
                    TypeExpression fieldType = ResolveAlias(field.Type);
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
                else if (member is MethodDeclaration method)
                {
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
                            DeclareVariable(p.Name, ResolveAlias(p.Type), p.Line);
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
                    op.Accept(this);
                }
            }

            _currentStruct = previousStruct;
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

            AstNode? prevFunc = _currentFunction;
            _currentFunction = node;
            try
            {
                ValidateTypeUsage(node.ReturnType, node.Line);
                PushScope();
                foreach (Parameter p in node.Parameters)
                {
                    ValidateTypeUsage(p.Type, p.Line);
                    DeclareVariable(p.Name, ResolveAlias(p.Type), p.Line);
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
            if (node.Name != ownerName)
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
                    ValidateTypeUsage(p.Type, p.Line);
                    DeclareVariable(p.Name, ResolveAlias(p.Type), p.Line);
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
            if (node.Name != ownerName)
            {
                string kindStr = _currentStruct != null ? "struct" : "class";
                throw new TypeCheckException($"Destructor name '~{node.Name}' does not match {kindStr} name '{ownerName}'", node.Line);
            }

            PushScope();
            NamedTypeExpression typeExpr = new NamedTypeExpression(ownerName, null, node.Line);
            PointerTypeExpression thisType = new PointerTypeExpression(typeExpr, false, node.Line);
            DeclareVariable("this", thisType, node.Line);
            node.Body.Accept(this);
            PopScope();
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
            }
            else
            {
                if (_actualReturnTypes.Count > 0)
                {
                    _actualReturnTypes.Peek().Add(Void);
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
            node.Body.Accept(this);
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
            node.Body.Accept(this);
            PopScope();
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

                // Size inference for inferred arrays: char[] a = "string";
                if (varType is ArrayTypeExpression { Size: null } arr && initType is ArrayTypeExpression { Size: not null } initArr)
                {
                    varType = new ArrayTypeExpression(arr.ElementType, initArr.Size, node.Line, arr.SizeExpression);
                }

                if (!IsAssignable(varType, initType, node.Initializer))
                    throw new TypeCheckException(
                        $"Cannot assign '{TypeName(initType)}' to '{TypeName(varType)}'", node.Line);
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
                    throw new TypeCheckException("Logical operators require bool operands", node.Line);
                RecordType(node, Bool);
            }
            else if (isComparison)
            {
                if (!TypesMatch(left, right) && !IsAssignable(left, right) && !IsAssignable(right, left) && !(IsAssignable(Int, left) && IsAssignable(Int, right)))
                    throw new TypeCheckException(
                        $"Cannot compare '{TypeName(left)}' with '{TypeName(right)}'", node.Line);
                RecordType(node, Bool);
            }
            else if (node.Operator == TokenKind.Plus)
            {
                TypeExpression resolvedLeft = ResolveAlias(left);
                TypeExpression resolvedRight = ResolveAlias(right);

                if ((resolvedLeft is PointerTypeExpression or FunctionPointerTypeExpression or ManagedTypeExpression) &&
                    (resolvedRight is PointerTypeExpression or FunctionPointerTypeExpression or ManagedTypeExpression))
                {
                    throw new TypeCheckException("Cannot add two pointers together", node.Line);
                }

                string? errLeft = null;
                string? errRight = null;
                if (IsValidPointerForArithmetic(resolvedLeft, out errLeft) && IsInteger(resolvedRight))
                {
                    if (errLeft != null)
                        throw new TypeCheckException(errLeft, node.Line);
                    RecordType(node, left);
                    return;
                }
                if (IsInteger(resolvedLeft) && IsValidPointerForArithmetic(resolvedRight, out errRight))
                {
                    if (errRight != null)
                        throw new TypeCheckException(errRight, node.Line);
                    RecordType(node, right);
                    return;
                }
                if (errLeft != null)
                    throw new TypeCheckException(errLeft, node.Line);
                if (errRight != null)
                    throw new TypeCheckException(errRight, node.Line);

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
                    throw new TypeCheckException(
                        $"Cannot apply operator to '{TypeName(left)}' and '{TypeName(right)}'", node.Line);
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
                        throw new TypeCheckException(errLeft, node.Line);
                    RecordType(node, left);
                    return;
                }
                if (IsValidPointerForArithmetic(resolvedLeft, out string? errL) && IsValidPointerForArithmetic(resolvedRight, out string? errR))
                {
                    if (errL != null)
                        throw new TypeCheckException(errL, node.Line);
                    if (errR != null)
                        throw new TypeCheckException(errR, node.Line);

                    if (!TypesMatch(resolvedLeft, resolvedRight))
                        throw new TypeCheckException(
                            $"Cannot subtract pointers of different types '{TypeName(left)}' and '{TypeName(right)}'", node.Line);
                    RecordType(node, Long);
                    return;
                }
                if (errLeft != null)
                    throw new TypeCheckException(errLeft, node.Line);
                if (errRight != null)
                    throw new TypeCheckException(errRight, node.Line);

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
                    throw new TypeCheckException(
                        $"Cannot apply operator to '{TypeName(left)}' and '{TypeName(right)}'", node.Line);
                }
                RecordType(node, left);
            }
            else if (node.Operator is TokenKind.LessLess or TokenKind.GreaterGreater)
            {
                if (!IsInteger(left) || !IsInteger(right))
                    throw new TypeCheckException(
                        $"Bit shift operators require integer operands, got '{TypeName(left)}' and '{TypeName(right)}'", node.Line);
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
                    throw new TypeCheckException(
                        $"Cannot apply operator to '{TypeName(left)}' and '{TypeName(right)}'", node.Line);
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
                        throw new TypeCheckException("Cannot dereference non-pointer", node.Line);
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
                TokenKind.InterpolatedStringSegment => CharPtr,
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
            if (node.Target is not (IdentifierExpression or MemberAccessExpression or UnaryExpression { Operator: TokenKind.Star } or IndexExpression))
                throw new TypeCheckException($"Invalid assignment target '{node.Target.GetType().Name}'", node.Line);

            if (node.Target is MemberAccessExpression { IsArrow: true } arrow)
                throw new TypeCheckException($"Cannot assign to read-only pointer property '->{arrow.Member}'", node.Line);

            node.Target.Accept(this);
            TypeExpression targetType = GetType(node.Target);
            CheckAssignmentTarget(node.Target, node.Line);

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
                throw new TypeCheckException(
                    $"Cannot assign '{TypeName(valueType)}' to '{TypeName(targetType)}'", node.Line);

            RecordType(node, targetType);
        }

        public void Visit(CallExpression node)
        {
            _callSites.Add((node, _currentFunction, _tryStack.ToList()));

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
                    memberAccess.Object.Accept(this);
                    TypeExpression targetObjType = GetType(memberAccess.Object);
                    if (targetObjType is not PointerTypeExpression && targetObjType is not ManagedTypeExpression && targetObjType is not FunctionPointerTypeExpression)
                        throw new TypeCheckException($"Cannot use '->' operator on non-pointer type '{TypeName(targetObjType)}'", node.Line);

                    if (memberAccess.Member == "free")
                    {
                        if (targetObjType is FunctionPointerTypeExpression)
                            throw new TypeCheckException("Cannot call '->free' on a function pointer", node.Line);
                        if ((targetObjType is PointerTypeExpression pFree && pFree.IsReadOnly) || (targetObjType is ManagedTypeExpression mFree && mFree.IsReadOnly))
                            throw new TypeCheckException("Cannot call '->free' on a readonly pointer", node.Line);
                        if (node.Arguments.Count != 0)
                            throw new TypeCheckException("'free()' takes no arguments", node.Line);

                        TypeExpression? innerType = targetObjType is PointerTypeExpression p ? p.Inner : (targetObjType is ManagedTypeExpression m ? m.Inner : null);
                        if (innerType != null)
                        {
                            TypeExpression resInner = ResolveAlias(innerType);
                            if (resInner is NamedTypeExpression namedCls && _classes.TryGetValue(namedCls.Name, out ClassInfo? clsInfo))
                            {
                                bool hasVirtualDtor = clsInfo.DestructorSlot >= 0;
                                bool hasDtor = hasVirtualDtor || HasAnyDestructor(clsInfo);
                                if (hasDtor)
                                {
                                    _classDestructorCalls[node] = (clsInfo, hasVirtualDtor, clsInfo.DestructorSlot);
                                }
                            }
                        }

                        RecordType(node, Void);
                        return;
                    }
                    else if (memberAccess.Member is "address" or "is_null")
                    {
                        throw new TypeCheckException($"'->{memberAccess.Member}' is a property, not a method", node.Line);
                    }
                    else
                    {
                        TypeExpression? innerType = targetObjType is PointerTypeExpression p ? p.Inner : (targetObjType is ManagedTypeExpression m ? m.Inner : null);
                        if (innerType != null)
                        {
                            TypeExpression resInner = ResolveAlias(innerType);
                            if (resInner is NamedTypeExpression namedStr && (_structs.ContainsKey(namedStr.Name) || IsInterface(namedStr)))
                            {
                                throw new TypeCheckException($"The '->' operator is reserved for pointer metadata and lifecycle operations ('address', 'is_null', 'free'). Use '.' to call method '{memberAccess.Member}' on '{namedStr.Name}'.", node.Line);
                            }
                        }
                        throw new TypeCheckException($"Unknown pointer operation '->{memberAccess.Member}'. The '->' operator is reserved for pointer metadata and lifecycle operations ('address', 'is_null', 'free').", node.Line);
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
                        if (field.Accessibility == TokenKind.Private && _currentClass?.Name != field.DeclaringClass)
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
                    if (method.Accessibility == TokenKind.Private && _currentClass?.Name != mEntry.DeclaringClass)
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
                nsAccess.Accept(this);
                RecordType(node, GetType(nsAccess));
                NamespaceScope? scope = ResolveNamespace(nsAccess.Left);
                if (scope != null)
                {
                    if (scope.Functions.TryGetValue(nsAccess.Member, out MethodDeclaration? m))
                        _resolvedCalls[node] = m;
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
            return null;
        }

        public void Visit(NamespaceAccessExpression node)
        {
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
                if (node.IsArrow)
                    throw new TypeCheckException($"Cannot use '->' operator on enum '{enumInfo.Name}'", node.Line);

                if (!enumInfo.Members.TryGetValue(node.Member, out long val))
                    throw new TypeCheckException($"Enum '{enumInfo.Name}' does not contain member '{node.Member}'", node.Line);

                RecordType(node, new NamedTypeExpression(enumInfo.Name, null, node.Line));
                _resolvedEnumMembers[node] = (enumInfo, val);
                return;
            }

            node.Object.Accept(this);
            TypeExpression objType = GetType(node.Object);

            if (node.IsArrow)
            {
                if (objType is not PointerTypeExpression && objType is not ManagedTypeExpression && objType is not FunctionPointerTypeExpression)
                    throw new TypeCheckException($"Cannot use '->' operator on non-pointer type '{TypeName(objType)}'", node.Line);

                if (node.Member == "address")
                {
                    RecordType(node, Long);
                    return;
                }
                else if (node.Member == "is_null")
                {
                    RecordType(node, Bool);
                    return;
                }
                else if (node.Member == "free")
                {
                    if (objType is FunctionPointerTypeExpression)
                        throw new TypeCheckException("Cannot call '->free' on a function pointer", node.Line);
                    if ((objType is PointerTypeExpression pFree && pFree.IsReadOnly) || (objType is ManagedTypeExpression mFree && mFree.IsReadOnly))
                        throw new TypeCheckException("Cannot call '->free' on a readonly pointer", node.Line);
                    RecordType(node, Void);
                    return;
                }
                else
                {
                    TypeExpression? innerType = objType is PointerTypeExpression p ? p.Inner : (objType is ManagedTypeExpression m ? m.Inner : null);
                    if (innerType != null)
                    {
                        TypeExpression resInner = ResolveAlias(innerType);
                        if (resInner is NamedTypeExpression namedStr && (_structs.ContainsKey(namedStr.Name) || IsInterface(namedStr)))
                        {
                            throw new TypeCheckException($"The '->' operator is reserved for pointer metadata and lifecycle operations ('address', 'is_null', 'free'). Use '.' to access member '{node.Member}' on '{namedStr.Name}'.", node.Line);
                        }
                    }
                    throw new TypeCheckException($"Unknown pointer operation '->{node.Member}'. The '->' operator is reserved for pointer metadata and lifecycle operations ('address', 'is_null', 'free').", node.Line);
                }
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
                    if (field.Accessibility == TokenKind.Private && _currentClass?.Name != field.DeclaringClass)
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
                    if (mEntry.Method.Accessibility == TokenKind.Private && _currentClass?.Name != mEntry.DeclaringClass)
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

        public void Visit(InterpolatedStringExpression node) => throw new NotImplementedException();
        public void Visit(NewExpression node)
        {
            _callSites.Add((node, _currentFunction, _tryStack.ToList()));

            TypeExpression resolvedType = ResolveAlias(node.Type);
            if (resolvedType is not NamedTypeExpression named)
            {
                throw new TypeCheckException($"Cannot instantiate non-struct and non-class type '{TypeName(node.Type)}'", node.Line);
            }

            if (_classes.TryGetValue(named.Name, out ClassInfo? cInfo))
            {
                if (cInfo.IsAbstract)
                {
                    throw new TypeCheckException($"Cannot instantiate abstract class '{cInfo.Name}'", node.Line);
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
                            if (arg is DefaultExpression def && def.TargetType == null)
                            {
                                RecordType(def, paramType);
                            }
                            else
                            {
                                arg.Accept(this);
                            }
                            TypeExpression argType = GetType(arg);
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
                throw new TypeCheckException($"Cannot instantiate non-struct and non-class type '{TypeName(node.Type)}'", node.Line);
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
                        if (arg is DefaultExpression def && def.TargetType == null)
                        {
                            RecordType(def, paramType);
                        }
                        else
                        {
                            arg.Accept(this);
                        }
                        TypeExpression argType = GetType(arg);
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
                RecordType(node, ResolveAlias(node.TargetType));
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
        public void Visit(PointerTypeExpression node) { }
        public void Visit(ManagedTypeExpression node) { }
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
        public void Visit(BreakStatement node) { }
        public void Visit(ContinueStatement node) { }

        public void Visit(AttributeNode node) { }
        public void Visit(ExternDeclaration node) { }

        private static string TypeName(TypeExpression type) => type switch
        {
            NamedTypeExpression n => n.Name,
            PointerTypeExpression p => (p.IsReadOnly ? "readonly " : "") + TypeName(p.Inner) + "*",
            ManagedTypeExpression m => (m.IsReadOnly ? "readonly " : "") + TypeName(m.Inner) + "^",
            ArrayTypeExpression a => TypeName(a.ElementType) + (a.Size.HasValue ? $"[{a.Size}]" : "[]"),
            FunctionPointerTypeExpression f => $"{TypeName(f.ReturnType)}({string.Join(", ", f.ParameterTypes.Select(TypeName))}){(f.IsManaged ? "^" : "*")}{(f.IsNullable ? "?" : "")}",
            _ => "unknown"
        };

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
            _lambdaScopeBoundaries.Push(_scopes.Count);
            _lambdaStaticStack.Push(node.IsStatic);
            _actualReturnTypes.Push(new List<TypeExpression>());

            PushScope();
            List<TypeExpression> paramTypes = new();
            foreach (Parameter p in node.Parameters)
            {
                ValidateTypeUsage(p.Type, p.Line);
                TypeExpression pType = ResolveAlias(p.Type);
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

        private void RegisterPrefixFromAttributes(List<AttributeNode> attributes, string nsPath, string? enclosingTypeName, ConstructorDeclaration? ctor, MethodDeclaration? method, StructDeclaration? strDecl, ClassDeclaration? clsDecl, int line)
        {
            foreach (AttributeNode attr in attributes)
            {
                if (attr.Name != "string_prefix")
                    continue;

                if (attr.Arguments.Count != 1)
                {
                    throw new TypeCheckException($"Attribute [string_prefix] requires exactly 1 argument (the prefix name)", line);
                }

                string prefix = attr.Arguments[0].Trim('\"');

                if (ctor != null)
                {
                    if (ctor.Parameters.Count != 1)
                    {
                        throw new TypeCheckException($"Constructor marked with [string_prefix] must take exactly 1 parameter", ctor.Line);
                    }
                    string typeName = enclosingTypeName!;
                    string fullName = nsPath.Length > 0 ? $"{nsPath}::{typeName}" : typeName;
                    if (!_prefixHandlers.Any(h => h.Prefix == prefix && h.FullName == fullName))
                    {
                        _prefixHandlers.Add(new StringPrefixHandler(prefix, fullName, nsPath, typeName, true, ctor, null, new NamedTypeExpression(typeName, null, ctor.Line), ctor.IsConst, ctor.Parameters[0]));
                    }
                }
                else if (method != null)
                {
                    if (method.Parameters.Count != 1)
                    {
                        throw new TypeCheckException($"Method '{method.Name}' marked with [string_prefix] must take exactly 1 parameter", method.Line);
                    }
                    string fullName = nsPath.Length > 0
                        ? (enclosingTypeName != null ? $"{nsPath}::{enclosingTypeName}::{method.Name}" : $"{nsPath}::{method.Name}")
                        : (enclosingTypeName != null ? $"{enclosingTypeName}::{method.Name}" : method.Name);
                    if (!_prefixHandlers.Any(h => h.Prefix == prefix && h.FullName == fullName))
                    {
                        _prefixHandlers.Add(new StringPrefixHandler(prefix, fullName, nsPath, enclosingTypeName, false, null, method, method.ReturnType, method.IsConst, method.Parameters[0]));
                    }
                }
                else if (strDecl != null)
                {
                    ConstructorDeclaration? matchingCtor = strDecl.Members.OfType<ConstructorDeclaration>().FirstOrDefault(c => c.Parameters.Count == 1);
                    if (matchingCtor == null)
                    {
                        throw new TypeCheckException($"Struct '{strDecl.Name}' marked with [string_prefix] must declare a constructor taking 1 parameter", strDecl.Line);
                    }
                    string fullName = nsPath.Length > 0 ? $"{nsPath}::{strDecl.Name}" : strDecl.Name;
                    if (!_prefixHandlers.Any(h => h.Prefix == prefix && h.FullName == fullName))
                    {
                        _prefixHandlers.Add(new StringPrefixHandler(prefix, fullName, nsPath, strDecl.Name, true, matchingCtor, null, new NamedTypeExpression(strDecl.Name, null, strDecl.Line), matchingCtor.IsConst, matchingCtor.Parameters[0]));
                    }
                }
                else if (clsDecl != null)
                {
                    ConstructorDeclaration? matchingCtor = clsDecl.Members.OfType<ConstructorDeclaration>().FirstOrDefault(c => c.Parameters.Count == 1);
                    if (matchingCtor == null)
                    {
                        throw new TypeCheckException($"Class '{clsDecl.Name}' marked with [string_prefix] must declare a constructor taking 1 parameter", clsDecl.Line);
                    }
                    string fullName = nsPath.Length > 0 ? $"{nsPath}::{clsDecl.Name}" : clsDecl.Name;
                    if (!_prefixHandlers.Any(h => h.Prefix == prefix && h.FullName == fullName))
                    {
                        _prefixHandlers.Add(new StringPrefixHandler(prefix, fullName, nsPath, clsDecl.Name, true, matchingCtor, null, new NamedTypeExpression(clsDecl.Name, null, clsDecl.Line), matchingCtor.IsConst, matchingCtor.Parameters[0]));
                    }
                }
            }
        }

        private string ExtractScopePath(AstNode scope)
        {
            if (scope is IdentifierExpression ident)
                return ident.Name;
            if (scope is NamespaceAccessExpression nsAccess)
                return $"{ExtractScopePath(nsAccess.Left)}::{nsAccess.Member}";
            throw new TypeCheckException($"Invalid scope qualifier for string prefix on line {scope.Line}", scope.Line);
        }

        private bool IsValidStringParameterType(TypeExpression type)
        {
            type = ResolveAlias(type);
            if (type is PointerTypeExpression ptr)
            {
                TypeExpression inner = ResolveAlias(ptr.Inner);
                return inner is NamedTypeExpression { Name: "char" };
            }
            if (type is ArrayTypeExpression arr)
            {
                TypeExpression inner = ResolveAlias(arr.ElementType);
                return inner is NamedTypeExpression { Name: "char" };
            }
            if (type is NamedTypeExpression named)
            {
                return named.Name is "string" or "char*";
            }
            return false;
        }

        private List<StringPrefixHandler> FindAccessiblePrefixHandlers(string prefix)
        {
            List<StringPrefixHandler> matching = new();
            foreach (StringPrefixHandler handler in _prefixHandlers)
            {
                if (handler.Prefix != prefix)
                    continue;

                string normNs = handler.Namespace.Replace('$', ':');
                string normCur = _currentNamespacePath.Replace('$', ':');

                if (handler.Namespace == "" ||
                    normNs == normCur ||
                    normCur.StartsWith(normNs + "::") ||
                    _usingNamespaces.Any(u => u.Replace('$', ':') == normNs))
                {
                    matching.Add(handler);
                }
            }
            return matching;
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

            List<StringPrefixHandler> candidates;
            if (node.Scope != null)
            {
                string scopeName = ExtractScopePath(node.Scope);
                string normScope = scopeName.Replace('$', ':');
                candidates = _prefixHandlers.Where(h =>
                {
                    if (h.Prefix != node.Prefix)
                        return false;

                    string normFull = h.FullName.Replace('$', ':');
                    string normNs = h.Namespace.Replace('$', ':');

                    return normFull == $"{normScope}::{node.Prefix}" ||
                           normFull == normScope ||
                           h.EnclosingTypeName == scopeName ||
                           normNs == normScope ||
                           normNs.EndsWith($"::{normScope}");
                }).ToList();
            }
            else
            {
                candidates = FindAccessiblePrefixHandlers(node.Prefix);
            }

            if (candidates.Count == 0)
            {
                throw new TypeCheckException($"Unknown string prefix '{node.Prefix}' on line {node.Line}", node.Line);
            }
            if (candidates.Count > 1)
            {
                string candList = string.Join(", ", candidates.Select(c => c.FullName));
                throw new TypeCheckException($"Ambiguous string prefix '{node.Prefix}' on line {node.Line}. Candidate handlers: {candList}", node.Line);
            }

            StringPrefixHandler handler = candidates[0];
            _resolvedPrefixHandlers[node] = handler;

            TypeExpression paramType = ResolveAlias(handler.Parameter.Type);
            if (!IsValidStringParameterType(paramType))
            {
                throw new TypeCheckException($"String prefix handler for '{node.Prefix}' on line {node.Line} must accept a string parameter (e.g. 'readonly char*'), but accepts '{paramType}'", node.Line);
            }

            _types[node] = handler.ReturnType;

            if (handler.IsConst)
            {
                if (_constEvaluator.TryEvaluate(node, out ConstValue? constVal, out string? _))
                {
                    _constValues[node] = constVal!;
                }
            }
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

            _throwSites.Add((node, _currentFunction, _tryStack.ToList(), named.Name));
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

        private void PropagateCanThrow()
        {
            foreach (var (throwStmt, caller, tries, thrownType) in _throwSites)
            {
                if (caller == null)
                {
                    continue;
                }
                bool caught = false;
                foreach (TryStatement tryStmt in tries)
                {
                    if (TryCatchesType(tryStmt, thrownType))
                    {
                        caught = true;
                        break;
                    }
                }
                if (!caught)
                {
                    _canThrowFunctions.Add(caller);
                }
            }

            bool changed = true;
            while (changed)
            {
                changed = false;

                // Propagate through virtual overrides
                foreach (ClassInfo cls in _classes.Values)
                {
                    if (cls.BaseClass != null && _classes.TryGetValue(cls.BaseClass, out ClassInfo? baseCls))
                    {
                        foreach (var kvp in cls.Methods)
                        {
                            if (baseCls.Methods.TryGetValue(kvp.Key, out var baseMethodEntry))
                            {
                                MethodDeclaration derivedMethod = kvp.Value.Method;
                                MethodDeclaration baseMethod = baseMethodEntry.Method;

                                if (_canThrowFunctions.Contains(derivedMethod) && _canThrowFunctions.Add(baseMethod))
                                {
                                    changed = true;
                                }
                                if (_canThrowFunctions.Contains(baseMethod) && _canThrowFunctions.Add(derivedMethod))
                                {
                                    changed = true;
                                }
                            }
                        }
                    }
                }

                // Propagate through calls
                foreach (var (callNode, caller, tries) in _callSites)
                {
                    AstNode? callee = null;
                    if (callNode is CallExpression call)
                    {
                        _resolvedCalls.TryGetValue(call, out callee);
                    }
                    else if (callNode is NewExpression newExpr)
                    {
                        _resolvedConstructors.TryGetValue(newExpr, out ConstructorDeclaration? ctor);
                        callee = ctor;
                    }

                    if (callee != null && _canThrowFunctions.Contains(callee))
                    {
                        if (_throwingCalls.Add(callNode))
                        {
                            changed = true;
                        }

                        if (caller != null)
                        {
                            bool caught = false;
                            foreach (TryStatement tryStmt in tries)
                            {
                                if (TryCatchesType(tryStmt, "Exception"))
                                {
                                    caught = true;
                                    break;
                                }
                            }

                            if (!caught && _canThrowFunctions.Add(caller))
                            {
                                changed = true;
                            }
                        }
                    }
                }
            }

            // main() is always the top-level entry point with standard C ABI (i32 main())
            _canThrowFunctions.RemoveWhere(f => f is MethodDeclaration m && m.Name == "main");
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

