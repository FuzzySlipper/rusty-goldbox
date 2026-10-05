namespace RustyGoldbox.Tests;

/// <summary>
/// The Blackapple Brugh campaign lives in its own repository
/// (github.com/FuzzySlipper/blackapple-brugh). Its regression tests here read
/// a checkout of it: <c>$GOLDBOX_BLACKAPPLE</c>, else a clone beside this
/// repository.
/// </summary>
internal static class BlackappleCheckout
{
    public const string Variable = "GOLDBOX_BLACKAPPLE";

    public static string Root => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } named
        ? System.IO.Path.GetFullPath(named)
        : System.IO.Path.GetFullPath(System.IO.Path.Combine(Rules.RepositoryRoot, "..", "blackapple-brugh"));

    /// <summary>A path inside the checkout.</summary>
    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);
}

/// <summary>
/// A test that needs the Blackapple checkout. Without one it is skipped with
/// the reason, unless <c>$GOLDBOX_BLACKAPPLE</c> names it (as CI does), in
/// which case a missing checkout fails the test instead of hiding it.
/// </summary>
public sealed class BlackappleFactAttribute : FactAttribute
{
    public BlackappleFactAttribute()
    {
        if (!Directory.Exists(BlackappleCheckout.Root) && Environment.GetEnvironmentVariable(BlackappleCheckout.Variable) is not { Length: > 0 })
        {
            Skip = $"Clone https://github.com/FuzzySlipper/blackapple-brugh beside this repository (or set {BlackappleCheckout.Variable}) to run the Blackapple campaign regressions.";
        }
    }
}
