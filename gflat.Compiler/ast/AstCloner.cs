using System;
using System.Collections.Generic;
using System.Linq;

namespace gflat.ast
{
    public class AstCloner
    {
        private readonly Dictionary<string, TypeExpression> _typeMap;
        private readonly string? _oldName;
        private readonly string? _newName;
        private readonly bool _canCloseAppliedSelf;

        public AstCloner(Dictionary<string, TypeExpression> typeMap, string? oldName = null, string? newName = null)
            : this(typeMap, oldName, newName, true)
        {
        }

        private AstCloner(Dictionary<string, TypeExpression> typeMap, string? oldName, string? newName, bool canCloseAppliedSelf)
        {
            _typeMap = typeMap;
            _oldName = oldName;
            _newName = newName;
            _canCloseAppliedSelf = canCloseAppliedSelf;
        }

        private AstCloner DeclarationScope(string name, List<GenericParameter> parameters)
        {
            if (name == _oldName || parameters.Count == 0 ||
                !parameters.Any(p => _typeMap.ContainsKey(p.Name))) return this;
            var names = parameters.Select(p => p.Name).ToHashSet();
            var substitutions = _typeMap.Where(pair => !names.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            // A reduced map is not the original owner's generic parameter list.
            // Resolve explicit applied owners normally instead of closing them.
            return new AstCloner(substitutions, _oldName, _newName,
                _canCloseAppliedSelf && substitutions.Count == _typeMap.Count);
        }

        private List<GenericParameter>? CloneGenericParameters(string name, List<GenericParameter> parameters)
            => name == _oldName ? null : parameters.Select(p =>
                new GenericParameter(p.Name, p.Constraint == null ? null : CloneType(p.Constraint), p.Line)).ToList();

        public StructDeclaration CloneStruct(StructDeclaration s)
        {
            using var context = SourceContext.Enter(s.Span);
            var result = CloneStructLocated(s);
            result.Span = s.Span;
            return result;
        }

        private StructDeclaration CloneStructLocated(StructDeclaration s)
        {
            var scope = DeclarationScope(s.Name, s.GenericParameters);
            string structName = s.Name;
            if (_newName != null && _oldName != null)
            {
                if (s.Name == _oldName)
                {
                    structName = _newName;
                }
                else if (s.Name.StartsWith(_oldName + "."))
                {
                    structName = _newName + s.Name.Substring(_oldName.Length);
                }
            }
            List<AstNode> clonedMembers = s.Members.Select(scope.CloneNode).ToList();
            var genericParams = scope.CloneGenericParameters(s.Name, s.GenericParameters);
            return new StructDeclaration(structName, s.Interfaces.ToList(), clonedMembers, s.Accessibility, s.Line, s.Attributes.Select(scope.CloneAttribute).ToList(), genericParameters: genericParams);
        }

        public ClassDeclaration CloneClass(ClassDeclaration c)
        {
            using var context = SourceContext.Enter(c.Span);
            var result = CloneClassLocated(c);
            result.Span = c.Span;
            return result;
        }

        private ClassDeclaration CloneClassLocated(ClassDeclaration c)
        {
            var scope = DeclarationScope(c.Name, c.GenericParameters);
            string className = c.Name;
            if (_newName != null && _oldName != null)
            {
                if (c.Name == _oldName)
                {
                    className = _newName;
                }
                else if (c.Name.StartsWith(_oldName + "."))
                {
                    className = _newName + c.Name.Substring(_oldName.Length);
                }
            }
            List<AstNode> clonedMembers = c.Members.Select(scope.CloneNode).ToList();
            var genericParams = scope.CloneGenericParameters(c.Name, c.GenericParameters);
            return new ClassDeclaration(className, c.BaseClass, c.Interfaces.ToList(), clonedMembers, c.Accessibility, c.IsAbstract, c.Line, c.Attributes.Select(scope.CloneAttribute).ToList(), genericParameters: genericParams) { IsStatic = c.IsStatic };
        }

        public MethodDeclaration CloneMethod(MethodDeclaration m)
        {
            using var context = SourceContext.Enter(m.Span);
            var result = CloneMethodLocated(m);
            result.Span = m.Span;
            return result;
        }

        private MethodDeclaration CloneMethodLocated(MethodDeclaration m)
        {
            var scope = DeclarationScope(m.Name, m.GenericParameters);
            string methodName = m.Name;
            if (_newName != null && _oldName != null)
            {
                if (m.Name == _oldName)
                {
                    methodName = _newName;
                }
                else if (m.Name.StartsWith(_oldName + "."))
                {
                    methodName = _newName + m.Name.Substring(_oldName.Length);
                }
            }
            TypeExpression retType = scope.CloneType(m.ReturnType);
            List<Parameter> parameters = m.Parameters.Select(scope.CloneParameter).ToList();
            BlockStatement? body = m.Body != null ? (BlockStatement)scope.CloneNode(m.Body) : null;
            var genericParams = scope.CloneGenericParameters(m.Name, m.GenericParameters);
            return new MethodDeclaration(methodName, retType, parameters, body, m.Accessibility, m.IsStatic, m.IsVirtual, m.IsOverride, m.IsAbstract, m.Line, m.IsReadOnly, m.IsConst, m.Attributes.Select(scope.CloneAttribute).ToList(), genericParameters: genericParams, isNoThrow: m.IsNoThrow) { InferAutoGetterReadOnly = m.InferAutoGetterReadOnly, IsInline = m.IsInline, StringLiteralPrefix = m.StringLiteralPrefix, ImplementationKind = m.ImplementationKind, PropertyName = m.PropertyName, StaticConstructorName = m.StaticConstructorName, IsPropertyInitializer = m.IsPropertyInitializer, IsRefReturn = m.IsRefReturn };
        }

        public TypeExpression CloneType(TypeExpression type)
        {
            using var context = SourceContext.Enter(type.Span);
            var result = CloneTypeLocated(type);
            if (type.IsReadOnlyValue) result = TypeQualifiers.ReadOnly(result);
            result.Span = type.Span;
            return result;
        }

        private Parameter CloneParameter(Parameter p) => new(p.Name, CloneType(p.Type), p.Line, p.IsConst,
            p.DefaultValue == null ? null : CloneNode(p.DefaultValue)) { Span = p.Span, IsExtensionReceiver = p.IsExtensionReceiver };

        private TypeExpression CloneTypeLocated(TypeExpression type)
        {
            if (type is NestedTypeExpression nested)
            {
                TypeExpression parent = CloneType(nested.Parent);
                List<TypeExpression> typeArgs = nested.TypeArguments.Select(CloneType).ToList();
                return new NestedTypeExpression(parent, nested.Member, typeArgs, nested.Line) { NameSpan = nested.NameSpan };
            }
            if (type is NamedTypeExpression named)
            {
                // A semantic identity is no longer a template name. Keep its
                // per-use contract metadata and identity when cloning syntax.
                if (named.IsResolvedNominal) return named;
                if (named.Namespace == null && _typeMap.TryGetValue(named.Name, out TypeExpression? mapped))
                {
                    return mapped;
                }
                if (_canCloseAppliedSelf && _oldName != null && _newName != null && named.Namespace == null &&
                    (named.Name == _oldName || named.Name == _oldName.Split('$').Last()) &&
                    named.TypeArguments.Count == _typeMap.Count &&
                    named.TypeArguments.Zip(_typeMap.Keys).All(pair => pair.First is NamedTypeExpression argument && !argument.IsReadOnlyValue && argument.Name == pair.Second))
                {
                    var identity = NamedTypeExpression.Resolved(_newName, named.Line);
                    identity.NameSpan = named.NameSpan;
                    return identity;
                }
                string typeName = named.Name;
                if (_newName != null && _oldName != null && named.Namespace == null)
                {
                    if (named.Name == _oldName && named.TypeArguments.Count == 0)
                    {
                        typeName = _newName;
                    }
                    else if (named.Name.StartsWith(_oldName + "."))
                    {
                        typeName = _newName + named.Name.Substring(_oldName.Length);
                    }
                }
                List<TypeExpression> typeArgs = named.TypeArguments.Select(CloneType).ToList();
                return new NamedTypeExpression(typeName, named.Namespace, named.Line, typeArgs) { NameSpan = named.NameSpan };
            }
            if (type is ReferenceTypeExpression reference)
                return new ReferenceTypeExpression(CloneType(reference.Inner), reference.Line);
            if (type is PointerTypeExpression ptr)
            {
                return new PointerTypeExpression(CloneType(ptr.Inner), ptr.IsNullable, ptr.Line, ptr.IsReadOnly);
            }
            if (type is ManagedTypeExpression mgd)
            {
                return new ManagedTypeExpression(CloneType(mgd.Inner), mgd.IsNullable, mgd.Line, mgd.IsReadOnly, mgd.Collector == null ? null : CloneType(mgd.Collector)) { UsesDefaultCollector = mgd.UsesDefaultCollector };
            }
            if (type is ArrayTypeExpression arr)
            {
                return new ArrayTypeExpression(CloneType(arr.ElementType), arr.Size, arr.Line, arr.SizeExpression != null ? CloneNode(arr.SizeExpression) : null);
            }
            if (type is FunctionPointerTypeExpression fnPtr)
            {
                return new FunctionPointerTypeExpression(CloneType(fnPtr.ReturnType), fnPtr.ParameterTypes.Select(CloneType).ToList(), fnPtr.IsManaged, fnPtr.IsNullable, fnPtr.Line, fnPtr.IsSignature);
            }
            return type;
        }

        private AttributeNode CloneAttribute(AttributeNode node) => new(node.Name, node.Arguments.Select(CloneNode).ToList(), node.Line);

        public AstNode CloneNode(AstNode node)
        {
            using var context = SourceContext.Enter(node.Span);
            var result = CloneNodeLocated(node);
            result.Span = node.Span;
            return result;
        }

        private AstNode CloneNodeLocated(AstNode node)
        {
            if (node is AliasDeclaration alias)
            {
                var aliasClone = new AliasDeclaration(alias.Name, alias.TargetType == null ? null! : CloneType(alias.TargetType), alias.Accessibility, alias.Line) { ImplementationKind = alias.ImplementationKind };
                aliasClone.Constraints.AddRange(alias.Constraints.Select(CloneType));
                return aliasClone;
            }
            if (node == null) return null!;
            AstNode clone = CloneNodeCore(node);
            clone.Attributes = node.Attributes.Select(CloneAttribute).ToList();
            clone.IsInline = node.IsInline;
            return clone;
        }

        private AstNode CloneNodeCore(AstNode node)
        {
            if (node is FieldDeclaration f)
            {
                return new FieldDeclaration(f.Name, CloneType(f.Type), f.Initializer != null ? CloneNode(f.Initializer) : null, f.Accessibility, f.IsConst, f.IsStatic, f.Line, f.IsReadOnly) { PropertyName = f.PropertyName };
            }
            if (node is ConstructorDeclaration ctor)
            {
                string ctorName = ctor.Name;
                if (_newName != null && _oldName != null)
                {
                    if (ctor.Name == _oldName)
                    {
                        ctorName = _newName;
                    }
                    else if (ctor.Name.StartsWith(_oldName + "."))
                    {
                        ctorName = _newName + ctor.Name.Substring(_oldName.Length);
                    }
                }
                List<Parameter> parameters = ctor.Parameters.Select(CloneParameter).ToList();
                BlockStatement body = (BlockStatement)CloneNode(ctor.Body);
                List<AstNode>? baseArgs = ctor.BaseArguments?.Select(CloneNode).ToList();
                return new ConstructorDeclaration(ctorName, parameters, body, ctor.Accessibility, ctor.Line, baseArgs, ctor.Attributes.Select(CloneAttribute).ToList(), ctor.IsConst) { IsCopy = ctor.IsCopy, IsNoThrow = ctor.IsNoThrow };
            }
            if (node is DestructorDeclaration dtor)
            {
                string dtorName = dtor.Name;
                if (_newName != null && _oldName != null)
                {
                    if (dtor.Name == _oldName)
                    {
                        dtorName = _newName;
                    }
                    else if (dtor.Name.StartsWith(_oldName + "."))
                    {
                        dtorName = _newName + dtor.Name.Substring(_oldName.Length);
                    }
                }
                return new DestructorDeclaration(dtorName, (BlockStatement)CloneNode(dtor.Body), dtor.IsVirtual, dtor.Line);
            }
            if (node is OperatorDeclaration op)
            {
                TypeExpression retType = CloneType(op.ReturnType);
                List<Parameter> parameters = op.Parameters.Select(CloneParameter).ToList();
                BlockStatement body = (BlockStatement)CloneNode(op.Body);
                return new OperatorDeclaration(op.OperatorKind, op.OperatorSymbol, retType, parameters, body, op.Accessibility, op.IsStatic, op.Line);
            }
            if (node is PropertyDeclaration property)
                return new PropertyDeclaration(property.Name, CloneType(property.Type),
                    property.Getter != null ? CloneMethod(property.Getter) : null,
                    property.Setter != null ? CloneMethod(property.Setter) : null,
                    property.Initializer != null ? CloneNode(property.Initializer) : null, property.Line)
                { IsRef = property.IsRef, Expanded = property.Expanded, Attributes = property.Attributes.Select(CloneAttribute).ToList() };
            if (node is MethodDeclaration m)
            {
                return CloneMethod(m);
            }
            if (node is BlockStatement block)
            {
                return new BlockStatement(block.Statements.Select(CloneNode).ToList(), block.Line);
            }
            if (node is ExpressionStatement exprStmt)
            {
                return new ExpressionStatement(CloneNode(exprStmt.Expression), exprStmt.Line);
            }
            if (node is VariableDeclaration varDecl)
            {
                TypeExpression varType = varDecl.Type != null ? CloneType(varDecl.Type) : null!;
                AstNode? init = varDecl.Initializer != null ? CloneNode(varDecl.Initializer) : null;
                return new VariableDeclaration(varDecl.Name, varType, init, varDecl.Line, varDecl.IsConst);
            }
            if (node is IfStatement ifStmt)
            {
                AstNode cond = CloneNode(ifStmt.Condition);
                BlockStatement thenBranch = (BlockStatement)CloneNode(ifStmt.Then);
                BlockStatement? elseBranch = ifStmt.Else != null ? (BlockStatement)CloneNode(ifStmt.Else) : null;
                return new IfStatement(cond, thenBranch, elseBranch, ifStmt.Line);
            }
            if (node is WhileStatement whileStmt)
            {
                return new WhileStatement(CloneNode(whileStmt.Condition), (BlockStatement)CloneNode(whileStmt.Body), whileStmt.Line);
            }
            if (node is ForStatement forStmt)
            {
                AstNode? init = forStmt.Initializer != null ? CloneNode(forStmt.Initializer) : null;
                AstNode? cond = forStmt.Condition != null ? CloneNode(forStmt.Condition) : null;
                AstNode? inc = forStmt.Increment != null ? CloneNode(forStmt.Increment) : null;
                BlockStatement body = (BlockStatement)CloneNode(forStmt.Body);
                return new ForStatement(init, cond, inc, body, forStmt.Line);
            }
            if (node is ForeachStatement foreachStmt)
            {
                TypeExpression elemType = CloneType(foreachStmt.ElementType);
                AstNode col = CloneNode(foreachStmt.Collection);
                BlockStatement body = (BlockStatement)CloneNode(foreachStmt.Body);
                return new ForeachStatement(elemType, foreachStmt.VariableName, col, body, foreachStmt.Line);
            }
            if (node is ReturnStatement retStmt)
            {
                return new ReturnStatement(retStmt.Value != null ? CloneNode(retStmt.Value) : null, retStmt.Line);
            }
            if (node is BreakStatement b)
            {
                return new BreakStatement(b.Line);
            }
            if (node is ContinueStatement c)
            {
                return new ContinueStatement(c.Line);
            }
            if (node is DeferStatement def)
            {
                return new DeferStatement(CloneNode(def.Statement), def.Line);
            }
            if (node is DeleteStatement del)
            {
                return new DeleteStatement(CloneNode(del.Target), del.Line);
            }
            if (node is BinaryExpression bin)
            {
                return new BinaryExpression(CloneNode(bin.Left), CloneNode(bin.Right), bin.Operator, bin.Line);
            }
            if (node is UnaryExpression un)
            {
                return new UnaryExpression(CloneNode(un.Operand), un.Operator, un.IsPrefix, un.Line);
            }
            if (node is LiteralExpression lit)
            {
                return new LiteralExpression(lit.Token, lit.Line);
            }
            if (node is IdentifierExpression ident)
            {
                return new IdentifierExpression(ident.Name, ident.Line);
            }
            if (node is InterpolatedStringExpression interpolation)
                return new InterpolatedStringExpression(interpolation.Parts.Select(CloneNode).ToList(), interpolation.Line, interpolation.Prefix, interpolation.Scope != null ? CloneNode(interpolation.Scope) : null);
            if (node is PrefixedStringLiteralExpression pref)
            {
                return new PrefixedStringLiteralExpression(pref.Scope != null ? CloneNode(pref.Scope) : null, pref.Prefix, (LiteralExpression)CloneNode(pref.Literal), pref.Line);
            }
            if (node is CallExpression call)
            {
                AstNode callee = CloneNode(call.Callee);
                List<AstNode> args = call.Arguments.Select(CloneNode).ToList();
                List<TypeExpression> typeArgs = call.TypeArguments.Select(CloneType).ToList();
                return new CallExpression(callee, args, call.Line, typeArgs);
            }
            if (node is DestructorCallExpression destruction)
                return new DestructorCallExpression(CloneNode(destruction.Target), CloneType(destruction.TargetType), destruction.Line);
            if (node is MoveExpression move) return new MoveExpression(CloneNode(move.Operand), move.Line, move.Destination == null ? null : CloneNode(move.Destination));
            if (node is MemberAccessExpression mem)
            {
                return new MemberAccessExpression(CloneNode(mem.Object), mem.Member, mem.IsArrow, mem.Line);
            }
            if (node is IndexExpression idx)
            {
                return new IndexExpression(CloneNode(idx.Target), idx.Indices.Select(CloneNode).ToList(), idx.Line);
            }
            if (node is ArrayLiteralExpression array)
                return new ArrayLiteralExpression(array.Elements.Select(CloneNode).ToList(), array.Line);
            if (node is AssignmentExpression assign)
            {
                return new AssignmentExpression(CloneNode(assign.Target), CloneNode(assign.Value), assign.Operator, assign.Line);
            }
            if (node is NewExpression newExpr)
            {
                TypeExpression newType = CloneType(newExpr.Type);
                List<AstNode> args = newExpr.Arguments.Select(CloneNode).ToList();
                return new NewExpression(newType, args, newExpr.Kind, newExpr.Line, newExpr.Destination == null ? null : CloneNode(newExpr.Destination)) { PlacementKeywordSpan = newExpr.PlacementKeywordSpan, Collector = newExpr.Collector == null ? null : CloneType(newExpr.Collector) };
            }
            if (node is DefaultExpression defExpr)
            {
                TypeExpression? targetType = defExpr.TargetType != null ? CloneType(defExpr.TargetType) : null;
                return new DefaultExpression(targetType, defExpr.Line);
            }
            if (node is CastExpression cast)
            {
                return new CastExpression(CloneType(cast.TargetType), CloneNode(cast.Operand), cast.Line);
            }
            if (node is AlignofExpression alignment)
                return new AlignofExpression(CloneType(alignment.TargetType), alignment.Line);
            if (node is TypeofExpression reflection)
                return new TypeofExpression(CloneType(reflection.TargetType), reflection.Line);

            if (node is SizeofExpression sz)
            {
                return new SizeofExpression(CloneType(sz.TargetType), sz.Line);
            }
            if (node is NameofExpression no)
            {
                return new NameofExpression(CloneNode(no.Target), no.Line) {
                    TypeBindings = no.TypeBindings.Concat(_typeMap).GroupBy(pair => pair.Key)
                        .ToDictionary(group => group.Key, group => CloneType(group.Last().Value))
                };
            }
            if (node is NamespaceAccessExpression nsAcc)
            {
                if (nsAcc.Left is IdentifierExpression id && _typeMap.TryGetValue(id.Name, out var provider))
                    return new NamespaceAccessExpression(provider, nsAcc.Member, nsAcc.Line);
                return new NamespaceAccessExpression(CloneNode(nsAcc.Left), nsAcc.Member, nsAcc.Line);
            }
            if (node is ThrowStatement throwStmt)
            {
                return new ThrowStatement(throwStmt.Expression == null ? null : CloneNode(throwStmt.Expression), throwStmt.Line);
            }
            if (node is CatchClause catchClause)
            {
                TypeExpression? exType = catchClause.ExceptionType != null ? CloneType(catchClause.ExceptionType) : null;
                BlockStatement body = (BlockStatement)CloneNode(catchClause.Body);
                return new CatchClause(exType, catchClause.VariableName, body, catchClause.Line);
            }
            if (node is TryStatement tryStmt)
            {
                BlockStatement tryBlock = (BlockStatement)CloneNode(tryStmt.TryBlock);
                List<CatchClause> catches = tryStmt.CatchClauses.Select(c => (CatchClause)CloneNode(c)).ToList();
                return new TryStatement(tryBlock, catches, tryStmt.Line);
            }

            if (node is StructDeclaration strDecl)
            {
                return CloneStruct(strDecl);
            }
            if (node is ClassDeclaration clsDecl)
            {
                return CloneClass(clsDecl);
            }

            return node;
        }
    }
}
