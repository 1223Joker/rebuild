using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rebuild.Analyzers;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>The determinism analyzers report what they should, and nothing on clean code.</summary>
public class AnalyzerTests
{
    private const string Prelude = """
        using System.Collections.Generic;
        using System.Linq;
        [System.AttributeUsage(System.AttributeTargets.Class)] sealed class DeterministicRngAttribute : System.Attribute { }
        [DeterministicRng] sealed class Rng { public int NextInt(int n) => n - 1; public bool Chance(int p) => p > 0; }
        """;

    private static async Task<string[]> Ids(string body)
    {
        var tree = CSharpSyntaxTree.ParseText(Prelude + "\nclass T {\n" + body + "\n}");
        var refs = Basic.References();
        var compilation = CSharpCompilation.Create("Probe", new[] { tree }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(compileErrors.Length == 0, string.Join("\n", compileErrors.Select(e => e.ToString())));
        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DeterminismAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        return diagnostics.Select(d => d.Id).Distinct().OrderBy(i => i).ToArray();
    }

    private static class Basic
    {
        public static MetadataReference[] References()
        {
            var tpa = (string)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
            return tpa.Split(System.IO.Path.PathSeparator)
                .Where(p => p.Contains("System.") || p.EndsWith("netstandard.dll") || p.EndsWith("mscorlib.dll"))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToArray();
        }
    }

    [Theory]
    [InlineData("float f;")]
    [InlineData("double D() => 0;")]
    [InlineData("decimal m;")]
    [InlineData("System.Single s;")]
    [InlineData("int I() { var x = 1.5; return (int)x; }")]
    [InlineData("int I() => (int)System.Math.Sqrt(4);")]
    [InlineData("long L(long a) => a / (long)2.0;")]
    public async Task Floating_point_is_reported(string code) => Assert.Equal(new[] { "RB0001" }, await Ids(code));

    [Theory]
    [InlineData("int S(Dictionary<int,int> d) { int s = 0; foreach (var kv in d) s += kv.Key; return s; }")]
    [InlineData("int S(HashSet<int> h) { int s = 0; foreach (var v in h) s += v; return s; }")]
    [InlineData("int S(Dictionary<int,int> d) { int s = 0; foreach (var v in d.Values) s += v; return s; }")]
    [InlineData("int S(HashSet<int> h) => h.Sum();")]
    [InlineData("int S(Dictionary<int,int> d) => d.Count(kv => kv.Value > 0);")]
    [InlineData("int S(IReadOnlyDictionary<int,int> d) => d.First().Key;")]
    public async Task Unordered_enumeration_is_reported(string code) => Assert.Equal(new[] { "RB0002" }, await Ids(code));

    [Theory]
    [InlineData("int R(Rng r) => r.NextInt(3) + r.NextInt(4);")]
    [InlineData("void R(Rng r) { var p = (r.NextInt(3), r.Chance(5)); }")]
    [InlineData("int R(Rng r) { return r.Chance(1) ? r.NextInt(2) : 0; }")]
    public async Task Two_rng_draws_in_one_statement_are_reported(string code) => Assert.Equal(new[] { "RB0003" }, await Ids(code));

    [Theory]
    [InlineData("async void A() { }")]
    [InlineData("void A() { System.Action a = async () => { }; }")]
    public async Task Async_is_reported(string code) => Assert.Contains("RB0004", await Ids(code));

    [Fact]
    public async Task Clean_code_has_no_diagnostics()
    {
        Assert.Empty(await Ids("""
            int Clean(Rng r, Dictionary<int,int> lookup, List<int> list, int[] arr)
            {
                int a = r.NextInt(3);
                int b = r.NextInt(4);
                lookup.TryGetValue(a, out int v);
                foreach (var x in list) v += x;
                foreach (var x in arr) v += x;
                var sorted = new SortedDictionary<int,int>(lookup);
                foreach (var kv in sorted) v += kv.Value;
                return a + b + v + list.Sum() + lookup.Count;
            }
            """));
    }
}
