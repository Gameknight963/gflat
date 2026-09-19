namespace gflat.diagnostics
{
    public static class DiagnosticRules
    {
        // 0001 - 0999: Syntax, operators, pointers
        public static readonly DiagnosticDescriptor GF0001_CannotDereferenceVoid = new DiagnosticDescriptor(
            "GF0001", "Invalid Dereference", "Cannot dereference 'void*'",
            DiagnosticSeverity.Error, "Types");

        public static readonly DiagnosticDescriptor GF0002_BreakOutsideLoop = new DiagnosticDescriptor(
            "GF0002", "Invalid Break", "Cannot break outside of a loop",
            DiagnosticSeverity.Error, "ControlFlow");

        public static readonly DiagnosticDescriptor GF0003_ContinueOutsideLoop = new DiagnosticDescriptor(
            "GF0003", "Invalid Continue", "Cannot continue outside of a loop",
            DiagnosticSeverity.Error, "ControlFlow");

        public static readonly DiagnosticDescriptor GF0004_SyntaxError = new DiagnosticDescriptor(
            "GF0004", "Syntax Error", "{0}",
            DiagnosticSeverity.Error, "Syntax");

        public static readonly DiagnosticDescriptor GF0005_ExpectedToken = new DiagnosticDescriptor(
            "GF0005", "Expected Token", "Expected {0} but got {1} '{2}'",
            DiagnosticSeverity.Error, "Syntax");

        // 1000 - 1999: Type checking & assignment
        public static readonly DiagnosticDescriptor GF1000_GeneralTypeError = new DiagnosticDescriptor(
            "GF1000", "Type Error", "{0}",
            DiagnosticSeverity.Error, "Types");

        public static readonly DiagnosticDescriptor GF1001_TypeMismatch = new DiagnosticDescriptor(
            "GF1001", "Type Mismatch", "Cannot assign '{0}' to '{1}'",
            DiagnosticSeverity.Error, "Types");

        public static readonly DiagnosticDescriptor GF1002_BinaryOperatorMismatch = new DiagnosticDescriptor(
            "GF1002", "Operator Mismatch", "Operator '{0}' cannot be applied to operands of type '{1}' and '{2}'",
            DiagnosticSeverity.Error, "Types");

        public static readonly DiagnosticDescriptor GF1003_UndeclaredIdentifier = new DiagnosticDescriptor(
            "GF1003", "Undeclared Identifier", "The name '{0}' does not exist in the current context",
            DiagnosticSeverity.Error, "Symbols");

        public static readonly DiagnosticDescriptor GF1004_TypeNotFound = new DiagnosticDescriptor(
            "GF1004", "Type Not Found", "The type or namespace '{0}' could not be found",
            DiagnosticSeverity.Error, "Types");

        // 2000 - 2999: Control flow & reachability
        public static readonly DiagnosticDescriptor GF2001_UnreachableCode = new DiagnosticDescriptor(
            "GF2001", "Unreachable Code", "Unreachable code detected",
            DiagnosticSeverity.Warning, "ControlFlow");

        public static readonly DiagnosticDescriptor GF2002_NotAllCodePathsReturn = new DiagnosticDescriptor(
            "GF2002", "Missing Return", "Not all code paths return a value in function '{0}'",
            DiagnosticSeverity.Error, "ControlFlow");

        // 3000 - 3999: Definite assignment & variables
        public static readonly DiagnosticDescriptor GF3001_UseOfUnassignedVariable = new DiagnosticDescriptor(
            "GF3001", "Unassigned Variable", "Use of unassigned local variable '{0}'",
            DiagnosticSeverity.Error, "DefiniteAssignment");

        public static readonly DiagnosticDescriptor GF3002_VariableAlreadyDeclared = new DiagnosticDescriptor(
            "GF3002", "Duplicate Variable", "Variable '{0}' already declared in this scope",
            DiagnosticSeverity.Error, "Variables");

        // 4000 - 4999: Memory, destructors, lifecycles
        public static readonly DiagnosticDescriptor GF4001_NonCopyableDestructorAssign = new DiagnosticDescriptor(
            "GF4001", "Non-Copyable Type Assignment", "Cannot assign to '{0}': types with destructors cannot be copied or reassigned by value",
            DiagnosticSeverity.Error, "Memory");

        public static readonly DiagnosticDescriptor GF4002_NonCopyableDestructorCopyInit = new DiagnosticDescriptor(
            "GF4002", "Non-Copyable Type Initialization", "Cannot copy value of type '{0}' because it defines a destructor. Pass by pointer, or use an explicit method if available.",
            DiagnosticSeverity.Error, "Memory");

        public static readonly DiagnosticDescriptor GF4003_NonCopyableDestructorPassByValue = new DiagnosticDescriptor(
            "GF4003", "Non-Copyable Parameter", "Parameter '{0}' cannot have type '{1}' because types with destructors cannot be passed by value. Use a pointer instead ('{1}*').",
            DiagnosticSeverity.Error, "Memory");

        public static readonly DiagnosticDescriptor GF4004_NonCopyableDestructorForeach = new DiagnosticDescriptor(
            "GF4004", "Non-Copyable Foreach", "Foreach iteration variable '{0}' cannot have type '{1}' because types with destructors cannot be copied by value. Use a pointer instead ('{1}*').",
            DiagnosticSeverity.Error, "Memory");

        // 1010: Allocator hooks
        public static readonly DiagnosticDescriptor GF1010_MissingCustomAllocator = new DiagnosticDescriptor(
            "GF1010", "Missing Custom Allocator",
            "'#no_default_allocator' is set but '{0}' was not defined. Define 'void* __gflat_alloc(ulong size)' and 'void __gflat_free(void* ptr)'.",
            DiagnosticSeverity.Error, "Memory");

        public static readonly DiagnosticDescriptor GF1011_InvalidAllocatorSignature = new DiagnosticDescriptor(
            "GF1011", "Invalid Allocator Signature",
            "Allocator hook '{0}' has an invalid signature: {1}",
            DiagnosticSeverity.Error, "Memory");
    }
}
