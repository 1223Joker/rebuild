using Microsoft.CodeAnalysis;

namespace Rebuild.Analyzers
{
    /// <summary>Diagnostic ids of the determinism guards (docs/01-architecture.md §2, ADR 0006).</summary>
    public static class Diagnostics
    {
        private const string Category = "Determinism";

        public static readonly DiagnosticDescriptor FloatingPoint = new DiagnosticDescriptor(
            "RB0001", "Floating point is banned in the simulation",
            "'{0}' uses floating point; use domain integers or Fix (ADR 0002)",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnorderedEnumeration = new DiagnosticDescriptor(
            "RB0002", "Enumeration of an unordered container",
            "Enumerating '{0}' has unspecified order; use arrays, List<T> or SortedDictionary (ADR 0006)",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MultipleRngCalls = new DiagnosticDescriptor(
            "RB0003", "More than one RNG draw in one statement",
            "This statement draws from a deterministic RNG {0} times; draw once per statement (ADR 0006)",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor Async = new DiagnosticDescriptor(
            "RB0004", "async is banned in the simulation",
            "async/await is not allowed in the simulation (ADR 0006)",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
    }
}
