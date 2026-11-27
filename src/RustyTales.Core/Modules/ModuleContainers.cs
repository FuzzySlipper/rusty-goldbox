namespace RustyTales.Core.Modules;

/// <summary>An opened module container (<c>.rpak</c>) and its underlying lifetime handle.</summary>
public sealed record OpenedModuleContainer(ModuleSource Source, IDisposable Handle) : IDisposable
{
    public void Dispose() => Handle.Dispose();
}

/// <summary>
/// Opens packaged module containers (<c>.rpak</c>) into <see cref="ModuleSource"/>
/// instances. Hosts with Engine content support (or a standalone reader) supply
/// an implementation; directory-only callers omit it.
/// </summary>
public interface IModuleContainerOpener
{
    /// <exception cref="IOException">The container cannot be opened or verified.</exception>
    OpenedModuleContainer Open(string path);
}
