using System.Security.Cryptography;
using System.Text;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Core.Campaigns;

/// <summary>
/// A module's content identity: SHA-256 over every file in its directory
/// (path and bytes, in path order, hidden entries skipped). Two copies with
/// the same version but different content get different identities.
/// </summary>
public static class ModuleIdentity
{
    public static string Of(ModuleManifest module)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        List<string> files = Directory.EnumerateFiles(module.Directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(module.Directory, file).Replace('\\', '/'))
            .Where(relative => !relative.Split('/').Any(part => part.StartsWith('.')))
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (string relative in files)
        {
            byte[] content = File.ReadAllBytes(Path.Combine(module.Directory, relative));
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData([0]);
            hash.AppendData(BitConverter.GetBytes((long)content.Length));
            hash.AppendData(content);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
