namespace RustyTales.Core.Persistence;

/// <summary>
/// Key-value byte storage for player saves, character stables and settings.
/// Desktop products bind this to Engine <c>ProductStateStore</c>; pure-.NET
/// tools and web hosts may use <see cref="FileStateStore"/>.
/// </summary>
public interface IStateStore : IDisposable
{
    byte[]? Load(string key);

    void Save(string key, byte[] payload);
}

/// <summary>Plain filesystem implementation of <see cref="IStateStore"/> using atomic temp-file replacement.</summary>
public sealed class FileStateStore(string rootDirectory, string scope) : IStateStore
{
    private readonly string _directory = Path.Combine(Path.GetFullPath(rootDirectory), scope);

    public byte[]? Load(string key)
    {
        string path = PathFor(key);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public void Save(string key, byte[] payload)
    {
        Directory.CreateDirectory(_directory);
        string path = PathFor(key);
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temp, payload);
        File.Move(temp, path, overwrite: true);
    }

    public void Dispose()
    {
    }

    private string PathFor(string key) => Path.Combine(_directory, $"{key}.json");
}
