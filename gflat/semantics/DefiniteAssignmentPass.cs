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
        }

        private void PushScope()
        {
            _scopeVars.Push(new List<string>());
        }

        private void PopScope()
        {
            List<string> vars = _scopeVars.Pop();
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
                }
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
                CheckExpression(ifStmt.Condition);

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

                _state = CloneState(stateBefore);
                CheckStatement(ifStmt.Then);
                Dictionary<string, Stack<VariableAssignment>> stateThen = _state;
                bool thenTerminates = _controlFlow.StatementTerminates(ifStmt.Then);

                Dictionary<string, Stack<VariableAssignment>> stateElse;
                bool elseTerminates = false;
                if (ifStmt.Else != null)
                {
                    _state = CloneState(stateBefore);
                    CheckStatement(ifStmt.Else);
                    stateElse = _state;
                    elseTerminates = _controlFlow.StatementTerminates(ifStmt.Else);
                }
                else
                {
                    stateElse = stateBefore;
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
                CheckStatement(whileStmt.Body);
                _state = stateBefore;
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
                CheckStatement(forStmt.Body);
                if (forStmt.Increment != null)
                {
                    CheckExpression(forStmt.Increment);
                }
                _state = stateBeforeBody;
                PopScope();
            }
            else if (stmt is ForeachStatement foreachStmt)
            {
                CheckExpression(foreachStmt.Collection);
                PushScope();
                DeclareLocal(foreachStmt.VariableName, foreachStmt.ElementType, isAssigned: true);
                CheckStatement(foreachStmt.Body);
                PopScope();
            }
            else if (stmt is ReturnStatement retStmt)
            {
                if (retStmt.Value != null)
                {
                    CheckExpression(retStmt.Value);
                }
            }
            else if (stmt is ThrowStatement throwStmt)
            {
                CheckExpression(throwStmt.Expression);
            }
            else if (stmt is TryStatement tryStmt)
            {
                Dictionary<string, Stack<VariableAssignment>> stateBefore = CloneState(_state);

                _state = CloneState(stateBefore);
                CheckStatement(tryStmt.TryBlock);
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
                CheckStatement(deferStmt.Statement);
            }
            else if (stmt is DeleteStatement delStmt)
            {
                CheckExpression(delStmt.Target);
            }
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
                if (unary.Operator == TokenKind.Ampersand)
                {
                    // Rule A: Taking the address of a variable or field initializes it!
                    if (unary.Operand is IdentifierExpression idOp)
                    {
                        MarkFullyAssigned(idOp.Name);
                        return;
                    }
                    else if (unary.Operand is MemberAccessExpression memberOp && memberOp.Object is IdentifierExpression targetId)
                    {
                        VariableAssignment? va = Lookup(targetId.Name);
                        if (va != null)
                        {
                            va.AssignedFields.Add(memberOp.Member);
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

                CheckExpression(unary.Operand);
            }
            else if (expr is BinaryExpression bin)
            {
                CheckExpression(bin.Left);
                CheckExpression(bin.Right);
            }
            else if (expr is CallExpression call)
            {
                CheckExpression(call.Callee);
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    CheckExpression(call.Arguments[i]);
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
            }
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
