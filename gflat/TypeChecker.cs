using gflat.ast;
using gflat.CompileExceptions;

namespace gflat
{
    public class TypeChecker : IVisitor
    {
        private readonly Dictionary<AstNode, TypeExpression> _types = new();
        private readonly Stack<Dictionary<string, TypeExpression>> _scopes = new();

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

        public bool TryGetOperatorTarget(BinaryExpression node, out (string StructName, OperatorDeclaration Operator) target) =>
            _operatorTargets.TryGetValue(node, out target);

        public bool TryGetUnaryOperatorTarget(UnaryExpression node, out (string StructName, OperatorDeclaration Operator) target) =>
            _unaryOperatorTargets.TryGetValue(node, out target);

        public bool TryGetCompoundOperatorTarget(AssignmentExpression node, out (string StructName, OperatorDeclaration Operator) target) =>
            _compoundOperatorTargets.TryGetValue(node, out target);

        public string GetOperatorNamespace(OperatorDeclaration op) =>
            _operatorNamespaces.TryGetValue(op, out string? ns) ? ns : "";

        public TypeExpression GetLambdaReturnType(LambdaExpression node) => _lambdaReturnTypes[node];

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
            public Dictionary<string, MethodDeclaration> Methods = new();
            public List<OperatorDeclaration> Operators = new();
            public List<string> Interfaces = new();
            public int FieldIndex(string name) => Fields.FindIndex(f => f.Name == name);
        }

        public class InterfaceInfo
        {
            public string Name = "";
            public string Namespace = "";
            public List<MethodDeclaration> Methods = new();
            public Dictionary<string, MethodDeclaration> MethodsByName = new();
            public Dictionary<string, int> MethodIndices = new();
            public TokenKind Accessibility = TokenKind.Public;
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
            public Dictionary<string, NamespaceScope> Children = new();
            public NamespaceScope? Parent;
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

            if (ResolveFunction(name) != null || ResolveExtern(name) != null)
                throw new TypeCheckException($"Cannot use function '{name}' as a value without '&'. Did you mean '&{name}'?", line);

            throw new TypeCheckException($"Unknown variable '{name}'", line);
        }

        private bool TryLookupVariable(string name, out TypeExpression? type)
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

        public TypeExpression ResolveAlias(TypeExpression type)
        {
            if (type is NamedTypeExpression named)
            {
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
                var resolvedElem = ResolveAlias(arr.ElementType);
                if (resolvedElem != arr.ElementType)
                    return new ArrayTypeExpression(resolvedElem, arr.Size, arr.Line);
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
                return pa.IsNullable == pb.IsNullable && TypesMatch(pa.Inner, pb.Inner);
            if (a is ManagedTypeExpression ma && b is ManagedTypeExpression mb)
                return ma.IsNullable == mb.IsNullable && TypesMatch(ma.Inner, mb.Inner);
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

            // null is assignable to any nullable type
            if (source is NamedTypeExpression { Name: "null" } && IsNullable(target))
                return true;

            // Struct pointer to interface pointer assignability
            if (target is PointerTypeExpression ptIface && source is PointerTypeExpression psStruct)
            {
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
                }
            }

            // void* is implicitly convertible to/from any pointer type (except interface fat pointer)
            if (target is PointerTypeExpression pt && source is PointerTypeExpression ps)
            {
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
            // first pass: register top-level members in global scope
            foreach (AstNode member in node.Members)
                RegisterMemberInScope(member, _globalScope, "");

            // and nested namespaces
            foreach (NamespaceDeclaration ns in node.Namespaces)
                BuildNamespaceScope(ns, _globalScope, "");

            // second pass: type check bodies
            _currentNamespace = _globalScope;
            foreach (AstNode member in node.Members)
                member.Accept(this);

            foreach (NamespaceDeclaration ns in node.Namespaces)
                ns.Accept(this);
        }

        private void RegisterMemberInScope(AstNode member, NamespaceScope scope, string nsPath)
        {
            if (member is MethodDeclaration method)
            {
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
                StructInfo info = new StructInfo
                {
                    Name = str.Name,
                    Namespace = nsPath,
                    Interfaces = new List<string>(str.Interfaces)
                };
                foreach (AstNode m in str.Members)
                {
                    if (m is FieldDeclaration field)
                        info.Fields.Add((field.Name, field.Type));
                    else if (m is MethodDeclaration sm)
                    {
                        info.Methods[sm.Name] = sm;
                        _functionNamespaces[sm] = nsPath;
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
                foreach (AstNode m in cls.Members)
                {
                    if (m is MethodDeclaration cm)
                    {
                        scope.Functions[cm.Name] = cm;
                        _functionNamespaces[cm] = nsPath;
                    }
                    if (m is ExternDeclaration ce)
                        scope.Externs[ce.Name] = ce;
                }
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

            foreach (AstNode member in node.Members)
                member.Accept(this);

            _currentNamespace = previous;
        }

        public void Visit(ClassDeclaration node)
        {
            foreach (AstNode member in node.Members)
                member.Accept(this);
        }

        public void Visit(StructDeclaration node)
        {
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
                }
                else if (member is MethodDeclaration method)
                {
                    ValidateTypeUsage(method.ReturnType, method.Line);
                    PushScope();
                    NamedTypeExpression structType = new NamedTypeExpression(node.Name, null, method.Line);
                    PointerTypeExpression thisType = new PointerTypeExpression(structType, false, method.Line);
                    DeclareVariable("this", thisType, method.Line);

                    foreach (Parameter p in method.Parameters)
                    {
                        ValidateTypeUsage(p.Type, p.Line);
                        DeclareVariable(p.Name, ResolveAlias(p.Type), p.Line);
                    }

                    method.Body?.Accept(this);
                    PopScope();
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

            PushScope();
            foreach (Parameter p in node.Parameters)
            {
                DeclareVariable(p.Name, ResolveAlias(p.Type), p.Line);
            }
            node.Body.Accept(this);
            PopScope();
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

        public void Visit(FieldDeclaration node) => throw new NotImplementedException();

        public void Visit(MethodDeclaration node)
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

        public void Visit(ConstructorDeclaration node)
        {
            PushScope();
            foreach (Parameter p in node.Parameters)
            {
                ValidateTypeUsage(p.Type, p.Line);
                DeclareVariable(p.Name, ResolveAlias(p.Type), p.Line);
            }
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
            ValidateTypeUsage(varType, node.Line);
            if (node.Initializer != null)
            {
                node.Initializer.Accept(this);
                TypeExpression initType = GetType(node.Initializer);

                // Size inference for inferred arrays: char[] a = "string";
                if (varType is ArrayTypeExpression { Size: null } arr && initType is ArrayTypeExpression { Size: not null } initArr)
                {
                    varType = new ArrayTypeExpression(arr.ElementType, initArr.Size, node.Line);
                }

                if (!IsAssignable(varType, initType, node.Initializer))
                    throw new TypeCheckException(
                        $"Cannot assign '{TypeName(initType)}' to '{TypeName(varType)}'", node.Line);
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
                RecordType(node, new PointerTypeExpression(operandType, false, node.Line));
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
        }

        public void Visit(AssignmentExpression node)
        {
            if (node.Target is not (IdentifierExpression or MemberAccessExpression or UnaryExpression { Operator: TokenKind.Star } or IndexExpression))
                throw new TypeCheckException($"Invalid assignment target '{node.Target.GetType().Name}'", node.Line);

            if (node.Target is MemberAccessExpression { IsArrow: true } arrow)
                throw new TypeCheckException($"Cannot assign to read-only pointer property '->{arrow.Member}'", node.Line);

            node.Target.Accept(this);
            TypeExpression targetType = GetType(node.Target);

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
                        if (node.Arguments.Count != 0)
                            throw new TypeCheckException("'free()' takes no arguments", node.Line);

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
                TypeExpression objType = GetType(memberAccess.Object);
                if (objType is PointerTypeExpression ptr)
                    objType = ptr.Inner;

                objType = ResolveAlias(objType);

                if (objType is NamedTypeExpression namedIface && ResolveInterface(namedIface) is InterfaceInfo ifaceInfo)
                {
                    if (!ifaceInfo.MethodsByName.TryGetValue(memberAccess.Member, out MethodDeclaration? ifaceMethod))
                        throw new TypeCheckException($"Interface '{namedIface.Name}' has no method '{memberAccess.Member}'", node.Line);

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
                    }

                    RecordType(node, ifaceMethod.ReturnType);
                    return;
                }

                if (objType is not NamedTypeExpression named || !_structs.TryGetValue(named.Name, out StructInfo? sInfo))
                    throw new TypeCheckException("Cannot call method on non-struct type", node.Line);

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

                funcName = $"{named.Name}.{memberAccess.Member}";
                _resolvedCalls[node] = method;
            }
            else if (node.Callee is IdentifierExpression ident)
            {
                funcName = ident.Name;
                method = ResolveFunction(funcName);
                ext = method == null ? ResolveExtern(funcName) : null;
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
                throw new TypeCheckException("Member access on non-struct type", node.Line);

            if (!_structs.TryGetValue(named.Name, out StructInfo? info))
                throw new TypeCheckException($"'{named.Name}' is not a struct", node.Line);

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
        public void Visit(NewExpression node) => throw new NotImplementedException();
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
            PointerTypeExpression p => TypeName(p.Inner) + "*",
            ManagedTypeExpression m => TypeName(m.Inner) + "^",
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
                }
                return false;
            }
            if (src is PointerTypeExpression pSrcIface && ResolveAlias(pSrcIface.Inner) is NamedTypeExpression srcIfaceNamed && ResolveInterface(srcIfaceNamed) != null)
            {
                return false;
            }

            // Pointer to pointer
            if (src is PointerTypeExpression && dst is PointerTypeExpression)
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
    }
}

