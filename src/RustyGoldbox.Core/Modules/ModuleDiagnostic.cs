namespace RustyGoldbox.Core.Modules;

/// <summary>
/// One problem found while loading modules. <see cref="Rule"/> is a stable
/// identifier for the rule that was broken; the message says how to fix it.
/// </summary>
/// <param name="Rule">Stable rule identifier, for example <c>manifest.version</c>.</param>
/// <param name="Message">What is wrong and what a valid value looks like.</param>
/// <param name="Module">The module ID, when it is known.</param>
/// <param name="File">Full path of the file the problem is in, when there is one.</param>
/// <param name="JsonPath">Location inside <paramref name="File"/>, for example <c>$.requires[0].version</c>.</param>
public sealed record ModuleDiagnostic(
    string Rule,
    string Message,
    string? Module = null,
    string? File = null,
    string? JsonPath = null);
