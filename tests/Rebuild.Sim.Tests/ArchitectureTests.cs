using System.Linq;
using Xunit;

namespace Rebuild.Sim.Tests;

/// <summary>Layering rules from docs/01-architecture.md §1–2.</summary>
public class ArchitectureTests
{
    [Fact]
    public void Sim_references_only_the_base_class_library()
    {
        var refs = typeof(Simulation).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        Assert.All(refs, name => Assert.True(
            name == "netstandard" || name == "mscorlib" || name.StartsWith("System"),
            $"Rebuild.Sim must not reference '{name}'"));
    }
}
