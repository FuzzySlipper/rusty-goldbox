using RustyTales.Core.Modules;

namespace RustyTales.Core.Rules;

/// <summary>
/// A rule expression that failed while playing (creating a character, a
/// fight, an event), located in the definition, file and JSON path it came from.
/// </summary>
public sealed class RuleFailure(ModuleDiagnostic diagnostic) : Exception(diagnostic.Message)
{
    public ModuleDiagnostic Diagnostic { get; } = diagnostic;
}
