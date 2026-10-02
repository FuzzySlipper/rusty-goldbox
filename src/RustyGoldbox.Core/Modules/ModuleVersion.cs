using System.Globalization;

namespace RustyGoldbox.Core.Modules;

/// <summary>A module version, <c>MAJOR.MINOR.PATCH</c>.</summary>
public readonly record struct ModuleVersion(int Major, int Minor, int Patch) : IComparable<ModuleVersion>
{
    public const string FormatDescription = "MAJOR.MINOR.PATCH, for example \"0.1.0\"";

    public static bool TryParse(string text, out ModuleVersion version)
    {
        version = default;
        string[] parts = text.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        int[] numbers = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (!TryParseNumber(parts[i], out numbers[i]))
            {
                return false;
            }
        }

        version = new ModuleVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    public int CompareTo(ModuleVersion other)
    {
        int major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        int minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
    }

    public static bool operator <(ModuleVersion left, ModuleVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ModuleVersion left, ModuleVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ModuleVersion left, ModuleVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ModuleVersion left, ModuleVersion right) => left.CompareTo(right) >= 0;

    private static bool TryParseNumber(string text, out int number)
    {
        number = 0;
        if (text.Length == 0 || (text.Length > 1 && text[0] == '0'))
        {
            return false;
        }

        foreach (char c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
