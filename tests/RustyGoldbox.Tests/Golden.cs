namespace RustyGoldbox.Tests;

/// <summary>
/// Golden transcripts under tests/RustyGoldbox.Tests/Golden. Set
/// GOLDBOX_UPDATE_GOLDEN=1 to rewrite them after an intended change, then
/// review the diff.
/// </summary>
internal static class Golden
{
    public static void Verify(string name, string actual)
    {
        string path = Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Golden", name);
        if (Environment.GetEnvironmentVariable("GOLDBOX_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"No golden transcript {path}. Run the tests with GOLDBOX_UPDATE_GOLDEN=1 to create it, then review it.");
        Assert.Equal(File.ReadAllText(path), actual);
    }
}
