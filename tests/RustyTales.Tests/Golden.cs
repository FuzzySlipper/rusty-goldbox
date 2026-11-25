namespace RustyTales.Tests;

/// <summary>
/// Golden transcripts under tests/RustyTales.Tests/Golden. Set
/// TALES_UPDATE_GOLDEN=1 to rewrite them after an intended change, then
/// review the diff.
/// </summary>
internal static class Golden
{
    public static void Verify(string name, string actual)
    {
        string path = Path.Combine(Rules.RepositoryRoot, "tests", "RustyTales.Tests", "Golden", name);
        if (Environment.GetEnvironmentVariable("TALES_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"No golden transcript {path}. Run the tests with TALES_UPDATE_GOLDEN=1 to create it, then review it.");
        Assert.Equal(File.ReadAllText(path), actual);
    }
}
