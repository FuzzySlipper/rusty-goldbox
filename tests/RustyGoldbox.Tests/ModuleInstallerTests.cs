using RustyGoldbox.Game;

namespace RustyGoldbox.Tests;

/// <summary>What the Game's module installer accepts before any network work starts.</summary>
public sealed class ModuleInstallerTests
{
    [Fact]
    public void APastedGitHubPageOrSourceStartsWorkAndAnythingElseIsExplained()
    {
        ModuleInstaller installer = new(new ModuleLibrary(_ => []));

        installer.PreviewFrom("https://github.com/FuzzySlipper/blackapple-brugh");
        Assert.True(installer.Busy);
        Assert.Empty(installer.Messages);

        ModuleInstaller other = new(new ModuleLibrary(_ => []));
        other.PreviewFrom("not a source");
        Assert.False(other.Busy);
        Assert.Contains("isn't a module source", Assert.Single(other.Messages), StringComparison.Ordinal);

        other.Install("github:FuzzySlipper/blackapple-brugh", "blackapple-brugh", "not a version");
        Assert.False(other.Busy);
        Assert.Contains("is not a module version", Assert.Single(other.Messages), StringComparison.Ordinal);
    }
}
