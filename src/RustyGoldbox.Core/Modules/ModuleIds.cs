namespace RustyGoldbox.Core.Modules;

public static class ModuleIds
{
    public const string FormatDescription =
        "lowercase letters, digits and single hyphens, starting with a letter, for example \"classic\" or \"stone-crypt\"";

    public static bool IsValid(string id)
    {
        if (id.Length == 0 || id[0] is < 'a' or > 'z' || id[^1] == '-')
        {
            return false;
        }

        char previous = '\0';
        foreach (char c in id)
        {
            bool allowed = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-';
            if (!allowed || (c == '-' && previous == '-'))
            {
                return false;
            }

            previous = c;
        }

        return true;
    }
}
