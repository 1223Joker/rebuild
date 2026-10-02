using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rebuild.Analyzers
{
    /// <summary>
    /// Compile-time determinism guards for the simulation assembly. Complements BannedApiAnalyzers
    /// (src/BannedSymbols.txt) with rules that a symbol list cannot express.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DeterminismAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>Types marked with an attribute of this name count as deterministic RNGs for RB0003.</summary>
        public const string RngAttributeName = "DeterministicRngAttribute";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            Diagnostics.FloatingPoint, Diagnostics.UnorderedEnumeration,
            Diagnostics.MultipleRngCalls, Diagnostics.Async);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.EnableConcurrentExecution();

            // RB0001: float/double/decimal written as a type ...
            context.RegisterSyntaxNodeAction(AnalyzeTypeSyntax,
                SyntaxKind.PredefinedType, SyntaxKind.IdentifierName, SyntaxKind.QualifiedName);
            // ... or produced by an expression (literals, var, method results, conversions).
            context.RegisterOperationAction(AnalyzeFloatOperation,
                OperationKind.Literal, OperationKind.Conversion, OperationKind.Binary, OperationKind.Unary,
                OperationKind.Invocation, OperationKind.PropertyReference, OperationKind.FieldReference,
                OperationKind.ObjectCreation, OperationKind.CompoundAssignment);

            // RB0002
            context.RegisterOperationAction(AnalyzeForEach, OperationKind.Loop);
            context.RegisterOperationAction(AnalyzeLinq, OperationKind.Invocation);

            // RB0003
            context.RegisterOperationBlockAction(AnalyzeRngDraws);

            // RB0004
            context.RegisterSyntaxNodeAction(AnalyzeAsync,
                SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement,
                SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.SimpleLambdaExpression,
                SyntaxKind.AnonymousMethodExpression);
        }

        private static bool IsFloatingType(ITypeSymbol? type)
        {
            if (type == null) return false;
            if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                type = named.TypeArguments[0];
            switch (type.SpecialType)
            {
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                    return true;
            }
            string name = type.ToDisplayString();
            return name == "System.Half" || name == "System.Runtime.InteropServices.NFloat";
        }

        private static void AnalyzeTypeSyntax(SyntaxNodeAnalysisContext ctx)
        {
            // Avoid double reports: a QualifiedName's right IdentifierName is visited separately.
            if (ctx.Node is IdentifierNameSyntax && ctx.Node.Parent is QualifiedNameSyntax) return;
            if (ctx.Node is IdentifierNameSyntax && ctx.Node.Parent is MemberAccessExpressionSyntax) return;
            var symbol = ctx.SemanticModel.GetSymbolInfo(ctx.Node, ctx.CancellationToken).Symbol as ITypeSymbol;
            if (IsFloatingType(symbol))
                ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.FloatingPoint, ctx.Node.GetLocation(), ctx.Node.ToString()));
        }

        private static void AnalyzeFloatOperation(OperationAnalysisContext ctx)
        {
            var op = ctx.Operation;
            if (!IsFloatingType(op.Type)) return;
            // Report the outermost floating expression only.
            if (op.Parent != null && IsFloatingType(op.Parent.Type)) return;
            // Implicit conversions are reported via their operand or parent.
            if (op is IConversionOperation conv && conv.IsImplicit && IsFloatingType(conv.Operand.Type)) return;
            ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.FloatingPoint, op.Syntax.GetLocation(), op.Syntax.ToString()));
        }

        private static bool IsUnorderedType(ITypeSymbol? type)
        {
            if (type == null) return false;
            var def = type.OriginalDefinition.ToDisplayString();
            switch (def)
            {
                case "System.Collections.Generic.Dictionary<TKey, TValue>":
                case "System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection":
                case "System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection":
                case "System.Collections.Generic.HashSet<T>":
                case "System.Collections.Generic.IDictionary<TKey, TValue>":
                case "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>":
                case "System.Collections.Generic.ISet<T>":
                case "System.Collections.Generic.IReadOnlySet<T>":
                case "System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>":
                case "System.Collections.Hashtable":
                    return true;
            }
            return false;
        }

        private static ITypeSymbol? UnwrapConversion(IOperation op)
        {
            while (op is IConversionOperation c && c.IsImplicit) op = c.Operand;
            return op.Type;
        }

        private static void AnalyzeForEach(OperationAnalysisContext ctx)
        {
            if (!(ctx.Operation is IForEachLoopOperation loop)) return;
            var type = UnwrapConversion(loop.Collection);
            if (IsUnorderedType(type))
                ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnorderedEnumeration,
                    loop.Collection.Syntax.GetLocation(), type!.ToDisplayString()));
        }

        private static void AnalyzeLinq(OperationAnalysisContext ctx)
        {
            var inv = (IInvocationOperation)ctx.Operation;
            var containing = inv.TargetMethod.ContainingType?.ToDisplayString();
            if (containing != "System.Linq.Enumerable") return;
            if (inv.Arguments.Length == 0) return;
            var type = UnwrapConversion(inv.Arguments[0].Value);
            if (IsUnorderedType(type))
                ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnorderedEnumeration,
                    inv.Syntax.GetLocation(), type!.ToDisplayString()));
        }

        private static bool IsRngType(ITypeSymbol? type)
        {
            if (type == null) return false;
            foreach (var attr in type.GetAttributes())
                if (attr.AttributeClass?.Name == RngAttributeName) return true;
            return false;
        }

        private static bool IsRngDraw(IOperation op)
        {
            if (!(op is IInvocationOperation inv) || inv.Instance == null || !IsRngType(inv.Instance.Type)) return false;
            var name = inv.TargetMethod.Name;
            return name.StartsWith("Next", System.StringComparison.Ordinal) || name == "Chance";
        }

        private static void AnalyzeRngDraws(OperationBlockAnalysisContext ctx)
        {
            foreach (var block in ctx.OperationBlocks)
                foreach (var op in block.DescendantsAndSelf())
                {
                    // Count RNG draws per statement (or per expression-bodied member).
                    bool isStatementRoot = op is IExpressionStatementOperation || op is IReturnOperation
                        || op is IVariableDeclarationGroupOperation
                        || (op.Parent == null && !(op is IBlockOperation));
                    if (!isStatementRoot) continue;
                    int draws = 0;
                    foreach (var d in op.DescendantsAndSelf())
                        if (IsRngDraw(d)) draws++;
                    if (draws > 1)
                        ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.MultipleRngCalls, op.Syntax.GetLocation(), draws));
                }
        }

        private static void AnalyzeAsync(SyntaxNodeAnalysisContext ctx)
        {
            SyntaxTokenList modifiers = default;
            SyntaxToken asyncKeyword = default;
            switch (ctx.Node)
            {
                case MethodDeclarationSyntax m: modifiers = m.Modifiers; break;
                case LocalFunctionStatementSyntax l: modifiers = l.Modifiers; break;
                case AnonymousFunctionExpressionSyntax a: asyncKeyword = a.AsyncKeyword; break;
                default: return;
            }
            foreach (var mod in modifiers)
                if (mod.IsKind(SyntaxKind.AsyncKeyword)) asyncKeyword = mod;
            if (asyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
                ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.Async, asyncKeyword.GetLocation()));
        }
    }
}
