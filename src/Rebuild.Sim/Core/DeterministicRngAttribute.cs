namespace Rebuild.Sim.Core;

/// <summary>
/// Marks a deterministic RNG type. The analyzer (RB0003) forbids more than one draw from such a type
/// in a single statement, so call order never depends on expression evaluation (ADR 0006).
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
public sealed class DeterministicRngAttribute : System.Attribute
{
}
