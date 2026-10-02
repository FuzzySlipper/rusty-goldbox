namespace RustyGoldbox.Core.Modules;

internal static class DirectoryListing
{
    /// <summary>
    /// Lists files (or subdirectories) in ordinal order. A directory that
    /// can't be read becomes a <c>directory.read</c> diagnostic and null.
    /// </summary>
    public static List<string>? List(string directory, bool directories, string? module, List<ModuleDiagnostic> diagnostics, string pattern = "*")
    {
        try
        {
            IEnumerable<string> entries = directories
                ? Directory.EnumerateDirectories(directory)
                : Directory.EnumerateFiles(directory, pattern);
            return entries.Order(StringComparer.Ordinal).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "directory.read",
                $"Can't read the directory: {exception.Message} Fix its permissions or remove it.",
                module,
                directory));
            return null;
        }
    }
}
