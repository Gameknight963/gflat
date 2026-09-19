using System;
using System.Collections.Generic;
using System.Linq;
using gflat.ast;
using gflat.CompileExceptions;
using gflat.comptime;

namespace gflat.semantics
{
    public class DefiniteAssignmentPass
    {
        private readonly TypeChecker _typeChecker;
        private readonly ControlFlowPass _controlFlow;

        public class VariableAssignment
        {
            public string Name = "";
            public TypeExpression Type = null!;
            public bool IsFullyAssigned;
            public HashSet<string> AssignedFields = new();
            public TypeChecker.StructInfo? StructInfo;

            public VariableAssignment Clone()
            {
                return new VariableAssignment
                {
                    Name = this.Name,
                    Type = this.Type,
                    IsFullyAssigned = this.IsFullyAssigned,
                    AssignedFields = new HashSet<string>(this.AssignedFields),
                    StructInfo = this.StructInfo
                };
            }
        }

        private Dictionary<string, Stack<VariableAssignment>> _state = new();
        private readonly Stack<List<string>> _scopeVars = new();
        private sealed record DeferredRead(AstNode Body, Dictionary<string, int> Bindings);
        private readonly List<List<DeferredRead>> _cleanupScopes = new();
        private readonly Stack<int> _loopCleanupDepths = new();
        private readonly Stack<List<Dictionary<string, Stack<VariableAssignment>>>> _breakStates = new();
        private readonly Stack<List<Dictionary<string, Stack<VariableAssignment>>>> _continueStates = new();
        private readonly Stack<int> _exceptionCleanupDepths = new();
        private int _functionCleanupDepth;
        private bool _checkingCleanup;

        private void CheckCleanups(int depth)
        {
            if (_checkingCleanup) return;
            _checkingCleanup = true;
            try
            {
                for (int i = _cleanupScopes.Count - 1; i >= depth; i--)
                foreach (var action in _cleanupScopes[i].AsEnumerable().Reverse().ToArray())
                {
                    var visible = _state;
                    // Bind names as they were at registration, even across later shadowing.
                    _state = action.Bindings.Where(b => visible.ContainsKey(b.Key)).ToDictionary(
                        b => b.Key, b => new Stack<VariableAssignment>(visible[b.Key].Reverse().Take(b.Value)));
                    CheckStatement(action.Body);
                    foreach (var binding in action.Bindings)
                    {
                        if (!visible.TryGetValue(binding.Key, out var stack) || !_state.TryGetValue(binding.Key, out var updated)) continue;
                        var values = stack.Reverse().ToArray();
                        var changes = updated.Reverse().ToArray();
                        Array.Copy(changes, values, Math.Min(changes.Length, binding.Value));
                        visible[binding.Key] = new Stack<VariableAssignment>(values);
                    }
                    _state = visible;
                }
            }
            finally { _checkingCleanup = false; }
        }
        private ClassDeclaration? _currentClass = null;
        private StructDeclaration? _currentStruct = null;

        public DefiniteAssignmentPass(TypeChecker typeChecker, ControlFlowPass controlFlow)
        {
            _typeChecker = typeChecker;
            _controlFlow = controlFlow;
        }

        public void Execute(CompilationUnit cu)
        {
            for (int i = 0; i < cu.Members.Count; i++)
            {
                CheckMember(cu.Members[i]);
            }

            foreach (NamespaceDeclaration ns in cu.Namespaces)
            {
                CheckNamespace(ns);
            }
        }

        private void CheckNamespace(NamespaceDeclaration ns)
        {
            for (int i = 0; i < ns.Members.Count; i++)
            {
                CheckMember(ns.Members[i]);
            }
        }

        private void CheckMember(AstNode member)
        {
            if (member is MethodDeclaration method)
            {
                CheckMethod(method);
            }
            else if (member is ClassDeclaration classDecl)
            {
                ClassDeclaration? prevClass = _currentClass;
                _currentClass = classDecl;
                try
                {
                    for (int i = 0; i < classDecl.Members.Count; i++)
                    {
                        CheckMember(classDecl.Members[i]);
                    }
                }
                finally
                {
                    _currentClass = prevClass;
                }
            }
            else if (member is StructDeclaration structDecl)
            {
                StructDeclaration? prevStruct = _currentStruct;
                _currentStruct = structDecl;
                try
                {
                    for (int i = 0; i < structDecl.Members.Count; i++)
                    {
                        CheckMember(structDecl.Members[i]);
                    }
                }
                finally
                {
                    _currentStruct = prevStruct;
                }
            }
            else if (member is OperatorDeclaration op)
            {
                CheckOperator(op);
            }
            else if (member is ConstructorDeclaration ctor)
            {
                CheckConstructor(ctor);
            }
            else if (member is DestructorDeclaration dtor)
            {
                CheckDestructor(dtor);
            }
        }

        public void CheckMethod(MethodDeclaration method)
        {
            if (method.Body == null || method.IsAbstract)
            {
                return;
            }

            ResetState();
            PushScope();

            if (!method.IsStatic)
            {
                DeclareLocal("this", method.ReturnType, isAssigned: true);
            }

            foreach (Parameter p in method.Parameters)
            {
                DeclareLocal(p.Name, p.Type, isAssigned: true);
            }

            CheckStatement(method.Body);
            PopScope();
        }

        public void CheckOperator(OperatorDeclaration op)
        {
            if (op.Body == null)
            {
                return;
            }

            ResetState();
            PushScope();

            foreach (Parameter p in op.Parameters)
            {
                DeclareLocal(p.Name, p.Type, isAssigned: true);
            }

            CheckStatement(op.Body);
            PopScope();
        }

        public void CheckConstructor(ConstructorDeclaration ctor)
        {
            if (ctor.Body == null)
            {
                return;
            }

            ResetState();
            PushScope();

            NamedTypeExpression dummyThisType = new NamedTypeExpression(ctor.Name, null, ctor.Line);
            DeclareLocal("this", dummyThisType, isAssigned: true);

            foreach (Parameter p in ctor.Parameters)
            {
                DeclareLocal(p.Name, p.Type, isAssigned: true);
            }

            if (ctor.BaseArguments != null)
            {
                foreach (AstNode arg in ctor.BaseArguments)
                {
                    CheckExpression(arg);
                }
            }

            CheckStatement(ctor.Body);
            PopScope();
        }

        public void CheckDestructor(DestructorDeclaration dtor)
        {
            if (dtor.Body == null)
            {
                return;
            }

            ResetState();
            PushScope();

            NamedTypeExpression dummyThisType = new NamedTypeExpression(dtor.Name, null, dtor.Line);
            DeclareLocal("this", dummyThisType, isAssigned: true);

            CheckStatement(dtor.Body);
            PopScope();
        }

        private void ResetState()
        {
            _state = new Dictionary<string, Stack<VariableAssignment>>();
            _scopeVars.Clear();
            _cleanupScopes.Clear();
            _loopCleanupDepths.Clear();
            _breakStates.Clear();
            _continueStates.Clear();
            _exceptionCleanupDepths.Clear();
            _functionCleanupDepth = 0;
        }

        private void PushScope()
        {
            _scopeVars.Push(new List<string>());
            _cleanupScopes.Add(new());
        }

        private void PopScope()
        {
            List<string> vars = _scopeVars.Pop();
            _cleanupScopes.RemoveAt(_cleanupScopes.Count - 1);
            for (int i = 0; i < vars.Count; i++)
            {
                string name = vars[i];
                if (_state.TryGetValue(name, out Stack<VariableAssignment>? stack))
                {
                    stack.Pop();
                    if (stack.Count == 0)
                    {
                        _state.Remove(name);
                    }
                }
            }
        }

        private void DeclareLocal(string name, TypeExpression type, bool isAssigned)
        {
            TypeExpression resolved = _typeChecker.ResolveAlias(type);
            TypeChecker.StructInfo? structInfo = null;

            if (resolved is NamedTypeExpression named)
            {
                structInfo = _typeChecker.GetStruct(named.Name);
                if (structInfo == null && _currentClass != null)
                {
                    structInfo = _typeChecker.GetStruct($"{_currentClass.Name}.{named.Name}");
                }
                if (structInfo == null && _currentStruct != null)
                {
                    structInfo = _typeChecker.GetStruct($"{_currentStruct.Name}.{named.Name}");
                }
                if (structInfo == null)
                {
                    structInfo = _typeChecker.Symbols.Structs.Values
                        .FirstOrDefault(s => s.Name == named.Name || s.Name.EndsWith("." + named.Name) || s.Name.EndsWith("::" + named.Name));
                }
            }

            bool fullyAssigned = isAssigned;
            HashSet<string> assignedFields = new();

            if (structInfo != null)
            {
                if (structInfo.Fields.Count == 0)
                {
                    fullyAssigned = true;
                }
                else if (isAssigned)
                {
                    foreach ((string fName, _) in structInfo.Fields)
                    {
                        assignedFields.Add(fName);
                    }
                }
            }

            VariableAssignment varAssign = new VariableAssignment
            {
                Name = name,
                Type = type,
                IsFullyAssigned = fullyAssigned,
                AssignedFields = assignedFields,
                StructInfo = structInfo
            };

            if (!_state.TryGetValue(name, out Stack<VariableAssignment>? stack))
            {
                stack = new Stack<VariableAssignment>();
                _state[name] = stack;
            }

            stack.Push(varAssign);
            if (_scopeVars.Count > 0)
            {
                _scopeVars.Peek().Add(name);
            }
        }

        private VariableAssignment? Lookup(string name)
        {
            if (_state.TryGetValue(name, out Stack<VariableAssignment>? stack) && stack.Count > 0)
            {
                return stack.Peek();
            }
            return null;
        }

        private void MarkFullyAssigned(string name)
        {
            VariableAssignment? va = Lookup(name);
            if (va != null)
            {
                va.IsFullyAssigned = true;
                if (va.StructInfo != null)
                {
                    foreach ((string fName, _) in va.StructInfo.Fields)
                    {
                        va.AssignedFields.Add(fName);
                    }
                }
            }
        }

        private Dictionary<string, Stack<VariableAssignment>> CloneState(Dictionary<string, Stack<VariableAssignment>> source)
        {
            Dictionary<string, Stack<VariableAssignment>> clone = new();
            foreach (KeyValuePair<string, Stack<VariableAssignment>> kv in source)
            {
                VariableAssignment[] items = kv.Value.ToArray();
                Array.Reverse(items);
                Stack<VariableAssignment> newStack = new Stack<VariableAssignment>();
                for (int i = 0; i < items.Length; i++)
                {
                    newStack.Push(items[i].Clone());
                }
                clone[kv.Key] = newStack;
            }
            return clone;
        }

        private Dictionary<string, Stack<VariableAssignment>> IntersectStates(
            Dictionary<string, Stack<VariableAssignment>> stateA,
            Dictionary<string, Stack<VariableAssignment>> stateB)
        {
            Dictionary<string, Stack<VariableAssignment>> result = new();

            foreach (KeyValuePair<string, Stack<VariableAssignment>> kv in stateA)
            {
                if (stateB.TryGetValue(kv.Key, out Stack<VariableAssignment>? stackB) && stackB.Count == kv.Value.Count)
                {
                    VariableAssignment[] itemsA = kv.Value.ToArray();
                    VariableAssignment[] itemsB = stackB.ToArray();
                    Array.Reverse(itemsA);
                    Array.Reverse(itemsB);

                    Stack<VariableAssignment> mergedStack = new Stack<VariableAssignment>();
                    for (int i = 0; i < itemsA.Length; i++)
                    {
                        VariableAssignment aVar = itemsA[i];
                        VariableAssignment bVar = itemsB[i];

                        VariableAssignment merged = new VariableAssignment
                        {
                            Name = aVar.Name,
                            Type = aVar.Type,
                            StructInfo = aVar.StructInfo,
                            AssignedFields = new HashSet<string>(aVar.AssignedFields),
                            IsFullyAssigned = false
                        };

                        merged.AssignedFields.IntersectWith(bVar.AssignedFields);

                        if (aVar.IsFullyAssigned && bVar.IsFullyAssigned)
                        {
                            merged.IsFullyAssigned = true;
                        }
                        else if (merged.StructInfo != null && merged.StructInfo.Fields.Count > 0)
                        {
                            if (merged.StructInfo.Fields.All(f => merged.AssignedFields.Contains(f.Name)))
                            {
                                merged.IsFullyAssigned = true;
                            }
                        }

                        mergedStack.Push(merged);
                    }

                    result[kv.Key] = mergedStack;
                }
            }

            return result;
        }

        public void CheckStatement(AstNode? stmt)
        {
            if (stmt == null)
            {
                return;
            }

            if (stmt is BlockStatement block)
            {
                PushScope();
                for (int i = 0; i < block.Statements.Count; i++)
                {
                    CheckStatement(block.Statements[i]);
                    if (block.Statements[i] is ReturnStatement or ThrowStatement or BreakStatement or ContinueStatement ||
                        _controlFlow.StatementTerminates(block.Statements[i])) break;
                }
                CheckCleanups(_cleanupScopes.Count - 1);
                PopScope();
            }
            else if (stmt is VariableDeclaration varDecl)
            {
                TypeExpression resolved = _typeChecker.ResolveAlias(varDecl.Type);
                if (varDecl.Initializer != null)
                {
                    // Declare as unassigned first to catch self-referential reads
                    DeclareLocal(varDecl.Name, varDecl.Type, isAssigned: false);
                    CheckExpression(varDecl.Initializer);
                    MarkFullyAssigned(varDecl.Name);
                }
                else if (resolved is ArrayTypeExpression)
                {
                    // Fixed-size stack array buffer is allocated
                    DeclareLocal(varDecl.Name, varDecl.Type, isAssigned: true);
                }
                else
                {
                    DeclareLocal(varDecl.Name, varDecl.Type, isAssigned: false);
                }
            }
            else if (stmt is ExpressionStatement exprStmt)
            {
                CheckExpression(exprStmt.Expression);
            }
            else if (stmt is IfStatement ifStmt)
            {
                var conditionStates = CheckCondition(ifStmt.Condition);
                _state = IntersectStates(conditionStates.WhenTrue, conditionStates.WhenFalse);

                if (ifStmt.Condition is LiteralExpression litTrue && litTrue.Token.Kind == TokenKind.True)
                {
                    CheckStatement(ifStmt.Then);
                    return;
                }
                if (ifStmt.Condition is LiteralExpression litFalse && litFalse.Token.Kind == TokenKind.False)
                {
                    if (ifStmt.Else != null)
                    {
                        CheckStatement(ifStmt.Else);
                    }
                    return;
                }

                Dictionary<string, Stack<VariableAssignment>> stateBefore = CloneState(_state);

                _state = CloneState(conditionStates.WhenTrue);
                CheckStatement(ifStmt.Then);
                Dictionary<string, Stack<VariableAssignment>> stateThen = _state;
                bool thenTerminates = _controlFlow.StatementTerminates(ifStmt.Then);

                Dictionary<string, Stack<VariableAssignment>> stateElse;
                bool elseTerminates = false;
                if (ifStmt.Else != null)
                {
                    _state = CloneState(conditionStates.WhenFalse);
                    CheckStatement(ifStmt.Else);
                    stateElse = _state;
                    elseTerminates = _controlFlow.StatementTerminates(ifStmt.Else);
                }
                else
                {
                    stateElse = conditionStates.WhenFalse;
                    elseTerminates = false;
                }

                if (thenTerminates && elseTerminates)
                {
                    _state = stateThen;
                }
                else if (thenTerminates)
                {
                    _state = stateElse;
                }
                else if (elseTerminates)
                {
                    _state = stateThen;
                }
                else
                {
                    _state = IntersectStates(stateThen, stateElse);
                }
            }
            else if (stmt is WhileStatement whileStmt)
            {
                CheckExpression(whileStmt.Condition);
                Dictionary<string, Stack<VariableAssignment>> stateBefore = CloneState(_state);
                _loopCleanupDepths.Push(_cleanupScopes.Count);
                _breakStates.Push(new());
                _continueStates.Push(new());
                CheckStatement(whileStmt.Body);
                _loopCleanupDepths.Pop();
                var exits = _breakStates.Pop();
                _continueStates.Pop();
                if (whileStmt.Condition is not LiteralExpression { Token.Kind: TokenKind.True }) exits.Add(stateBefore);
                _state = MergeLoopStates(exits, stateBefore);
            }
            else if (stmt is ForStatement forStmt)
            {
                PushScope();
                if (forStmt.Initializer != null)
                {
                    CheckStatement(forStmt.Initializer);
                }
                if (forStmt.Condition != null)
                {
                    CheckExpression(forStmt.Condition);
                }

                Dictionary<string, Stack<VariableAssignment>> stateBeforeBody = CloneState(_state);
                _loopCleanupDepths.Push(_cleanupScopes.Count);
                _breakStates.Push(new());
                _continueStates.Push(new());
                CheckStatement(forStmt.Body);
                _loopCleanupDepths.Pop();
                var continues = _continueStates.Pop();
                if (!_controlFlow.StatementTerminates(forStmt.Body)) continues.Add(CloneState(_state));
                _state = MergeLoopStates(continues, stateBeforeBody);
                if (forStmt.Increment != null)
                {
                    CheckExpression(forStmt.Increment);
                }
                var exits = _breakStates.Pop();
                if (forStmt.Condition != null && forStmt.Condition is not LiteralExpression { Token.Kind: TokenKind.True }) exits.Add(stateBeforeBody);
                _state = MergeLoopStates(exits, stateBeforeBody);
                PopScope();
            }
            else if (stmt is ForeachStatement foreachStmt)
            {
                CheckExpression(foreachStmt.Collection);
                var beforeLoop = CloneState(_state);
                PushScope();
                DeclareLocal(foreachStmt.VariableName, foreachStmt.ElementType, isAssigned: true);
                _loopCleanupDepths.Push(_cleanupScopes.Count);
                _breakStates.Push(new());
                _continueStates.Push(new());
                CheckStatement(foreachStmt.Body);
                _loopCleanupDepths.Pop();
                _breakStates.Pop();
                _continueStates.Pop();
                PopScope();
                _state = beforeLoop;
            }
            else if (stmt is ReturnStatement retStmt)
            {
                if (retStmt.Value != null)
                {
                    CheckExpression(retStmt.Value);
                }
                CheckCleanups(_functionCleanupDepth);
            }
            else if (stmt is ThrowStatement throwStmt)
            {
                CheckExpression(throwStmt.Expression);
                CheckCleanups(_exceptionCleanupDepths.Count > 0 ? _exceptionCleanupDepths.Peek() : _functionCleanupDepth);
            }
            else if (stmt is TryStatement tryStmt)
            {
                Dictionary<string, Stack<VariableAssignment>> stateBefore = CloneState(_state);

                _state = CloneState(stateBefore);
                _exceptionCleanupDepths.Push(_cleanupScopes.Count);
                CheckStatement(tryStmt.TryBlock);
                _exceptionCleanupDepths.Pop();
                Dictionary<string, Stack<VariableAssignment>> stateTry = _state;
                bool tryTerminates = _controlFlow.StatementTerminates(tryStmt.TryBlock);

                List<Dictionary<string, Stack<VariableAssignment>>> catchStates = new();
                List<bool> catchTerminates = new();

                for (int i = 0; i < tryStmt.CatchClauses.Count; i++)
                {
                    CatchClause c = tryStmt.CatchClauses[i];
                    _state = CloneState(stateBefore);
                    PushScope();
                    if (!string.IsNullOrEmpty(c.VariableName) && c.ExceptionType != null)
                    {
                        DeclareLocal(c.VariableName, c.ExceptionType, isAssigned: true);
                    }
                    CheckStatement(c.Body);
                    PopScope();
                    catchStates.Add(_state);
                    catchTerminates.Add(_controlFlow.StatementTerminates(c.Body));
                }

                List<Dictionary<string, Stack<VariableAssignment>>> surviving = new();
                if (!tryTerminates)
                {
                    surviving.Add(stateTry);
                }
                for (int i = 0; i < catchStates.Count; i++)
                {
                    if (!catchTerminates[i])
                    {
                        surviving.Add(catchStates[i]);
                    }
                }

                if (surviving.Count == 0)
                {
                    _state = stateTry;
                }
                else
                {
                    Dictionary<string, Stack<VariableAssignment>> merged = surviving[0];
                    for (int i = 1; i < surviving.Count; i++)
                    {
                        merged = IntersectStates(merged, surviving[i]);
                    }
                    _state = merged;
                }
            }
            else if (stmt is DeferStatement deferStmt)
            {
                if (_checkingCleanup)
                    throw new TypeCheckException("Nested defer is not supported", deferStmt.Line);
                _cleanupScopes[^1].Add(new(deferStmt.Statement, _state.ToDictionary(p => p.Key, p => p.Value.Count)));
            }
            else if (stmt is BreakStatement or ContinueStatement)
            {
                if (_loopCleanupDepths.Count > 0) CheckCleanups(_loopCleanupDepths.Peek());
                if (stmt is BreakStatement && _breakStates.Count > 0) _breakStates.Peek().Add(CloneState(_state));
                if (stmt is ContinueStatement && _continueStates.Count > 0) _continueStates.Peek().Add(CloneState(_state));
            }
            else if (stmt is DeleteStatement delStmt)
            {
                CheckExpression(delStmt.Target);
            }
            else if (stmt is AssignmentExpression or CallExpression or UnaryExpression or BinaryExpression)
            {
                CheckExpression(stmt);
            }
        }

        private (Dictionary<string, Stack<VariableAssignment>> WhenTrue, Dictionary<string, Stack<VariableAssignment>> WhenFalse)
            CheckCondition(AstNode expression)
        {
            if (expression is UnaryExpression { Operator: TokenKind.Bang } not)
            {
                var operand = CheckCondition(not.Operand);
                return (operand.WhenFalse, operand.WhenTrue);
            }
            if (expression is BinaryExpression binary && binary.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
            {
                var left = CheckCondition(binary.Left);
                bool and = binary.Operator == TokenKind.AmpersandAmpersand;
                _state = CloneState(and ? left.WhenTrue : left.WhenFalse);
                var right = CheckCondition(binary.Right);
                return and ? (right.WhenTrue, IntersectStates(left.WhenFalse, right.WhenFalse))
                    : (IntersectStates(left.WhenTrue, right.WhenTrue), right.WhenFalse);
            }
            CheckExpression(expression);
            return (CloneState(_state), CloneState(_state));
        }

        public void CheckExpression(AstNode? expr)
        {
            if (expr == null)
            {
                return;
            }

            if (expr is IdentifierExpression id)
            {
                VariableAssignment? va = Lookup(id.Name);
                if (va != null)
                {
                    if (!va.IsFullyAssigned)
                    {
                        throw new TypeCheckException($"Use of unassigned local variable '{id.Name}'", id.Line);
                    }
                }
            }
            else if (expr is MemberAccessExpression member)
            {
                if (member.Object is IdentifierExpression targetId)
                {
                    VariableAssignment? va = Lookup(targetId.Name);
                    if (va != null)
                    {
                        TypeExpression resolvedTarget = _typeChecker.ResolveAlias(va.Type);
                        if (resolvedTarget is not PointerTypeExpression)
                        {
                            if (va.IsFullyAssigned || va.AssignedFields.Contains(member.Member))
                            {
                                return;
                            }
                            throw new TypeCheckException($"Use of unassigned field '{member.Member}' of struct variable '{targetId.Name}'", member.Line);
                        }
                    }
                }

                CheckExpression(member.Object);
            }
            else if (expr is AssignmentExpression assign)
            {
                CheckAssignment(assign);
            }
            else if (expr is UnaryExpression unary)
            {
                CheckExpression(unary.Operand);
            }
            else if (expr is BinaryExpression bin)
            {
                CheckExpression(bin.Left);
                var beforeRight = CloneState(_state);
                CheckExpression(bin.Right);
                if (bin.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
                {
                    bool alwaysRight = bin.Left is LiteralExpression literal &&
                        ((bin.Operator == TokenKind.AmpersandAmpersand && literal.Token.Kind == TokenKind.True) ||
                         (bin.Operator == TokenKind.PipePipe && literal.Token.Kind == TokenKind.False));
                    if (!alwaysRight) _state = IntersectStates(beforeRight, _state);
                }
            }
            else if (expr is CallExpression call)
            {
                CheckExpression(call.Callee);
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    CheckExpression(call.Arguments[i]);
                }
                if (_typeChecker.GetResolvedCall(call) is MethodDeclaration { Throws: true })
                {
                    var normal = CloneState(_state);
                    CheckCleanups(_exceptionCleanupDepths.Count > 0 ? _exceptionCleanupDepths.Peek() : _functionCleanupDepth);
                    _state = normal;
                }
            }
            else if (expr is CastExpression cast)
            {
                CheckExpression(cast.Operand);
            }
            else if (expr is IndexExpression index)
            {
                CheckExpression(index.Target);
                CheckExpression(index.Index);
            }
            else if (expr is ArrayLiteralExpression array)
            {
                foreach (var element in array.Elements) CheckExpression(element);
            }
            else if (expr is NewExpression newExpr)
            {
                for (int i = 0; i < newExpr.Arguments.Count; i++)
                {
                    CheckExpression(newExpr.Arguments[i]);
                }
            }
            else if (expr is InterpolatedStringExpression interp)
            {
                for (int i = 0; i < interp.Parts.Count; i++)
                {
                    CheckExpression(interp.Parts[i]);
                }
            }
            else if (expr is LambdaExpression lambda)
            {
                var beforeLambda = CloneState(_state);
                int outerCleanupDepth = _functionCleanupDepth;
                _functionCleanupDepth = _cleanupScopes.Count;
                PushScope();
                foreach (Parameter p in lambda.Parameters)
                {
                    DeclareLocal(p.Name, p.Type, isAssigned: true);
                }
                if (lambda.IsExpressionBody)
                {
                    CheckExpression(lambda.Body);
                }
                else
                {
                    CheckStatement(lambda.Body);
                }
                PopScope();
                _state = beforeLambda;
                _functionCleanupDepth = outerCleanupDepth;
            }
        }

        private Dictionary<string, Stack<VariableAssignment>> MergeLoopStates(
            List<Dictionary<string, Stack<VariableAssignment>>> states,
            Dictionary<string, Stack<VariableAssignment>> template)
        {
            Dictionary<string, Stack<VariableAssignment>> Project(Dictionary<string, Stack<VariableAssignment>> state) =>
                template.ToDictionary(p => p.Key, p => new Stack<VariableAssignment>(
                    state.TryGetValue(p.Key, out var values) ? values.Reverse().Take(p.Value.Count) : p.Value.Reverse()));
            if (states.Count == 0) return CloneState(template);
            var merged = Project(states[0]);
            foreach (var state in states.Skip(1)) merged = IntersectStates(merged, Project(state));
            return merged;
        }

        private void CheckAssignment(AssignmentExpression assign)
        {
            if (assign.Operator == TokenKind.Equals)
            {
                // 1. Evaluate RHS Value FIRST!
                CheckExpression(assign.Value);

                // 2. Perform write to LHS Target
                if (assign.Target is IdentifierExpression id)
                {
                    MarkFullyAssigned(id.Name);
                }
                else if (assign.Target is MemberAccessExpression member)
                {
                    if (member.Object is IdentifierExpression targetId)
                    {
                        VariableAssignment? va = Lookup(targetId.Name);
                        if (va != null)
                        {
                            TypeExpression resolvedTarget = _typeChecker.ResolveAlias(va.Type);
                            if (resolvedTarget is not PointerTypeExpression)
                            {
                                // Field-by-field struct initialization write!
                                va.AssignedFields.Add(member.Member);
                                if (va.StructInfo != null && va.StructInfo.Fields.Count > 0)
                                {
                                    if (va.StructInfo.Fields.All(f => va.AssignedFields.Contains(f.Name)))
                                    {
                                        va.IsFullyAssigned = true;
                                    }
                                }
                                return;
                            }
                        }
                    }

                    // Not a local struct variable field write (e.g. pointer ptr.f = val)
                    CheckExpression(member.Object);
                }
                else
                {
                    // Array indexing or pointer dereference target (*p = val, arr[i] = val)
                    CheckExpression(assign.Target);
                }
            }
            else
            {
                // Compound assignment (+=, -=, etc.) reads target first!
                CheckExpression(assign.Target);
                CheckExpression(assign.Value);

                if (assign.Target is IdentifierExpression id)
                {
                    MarkFullyAssigned(id.Name);
                }
            }
        }
    }
}
