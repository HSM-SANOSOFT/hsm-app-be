namespace Hsm.Application.Docs;

/// <summary>
/// The frozen key algebra (packages/storage/src/s3/s3.service.ts +
/// docs.service.ts splitStoragePath): folder names are normalized (trimmed,
/// backslashes to slashes, edge slashes stripped, doubles collapsed,
/// lowercased) and keys are "&lt;folder&gt;/&lt;fileId&gt;"; splitting takes the last
/// segment as the file id.
/// </summary>
public static class StorageKeys
{
    /// <summary>Frozen getFileFolder.</summary>
    public static string NormalizeFolder(string? folderName)
    {
        var folder = (folderName ?? string.Empty).Trim().Replace('\\', '/');
        folder = folder.Trim('/');
        while (folder.Contains("//", StringComparison.Ordinal))
        {
            folder = folder.Replace("//", "/", StringComparison.Ordinal);
        }

        return folder.ToLowerInvariant();
    }

    /// <summary>Frozen getFileKey.</summary>
    public static string MakeKey(string? folderName, string fileId)
    {
        var folder = NormalizeFolder(folderName);
        return folder.Length > 0 ? $"{folder}/{fileId}" : fileId;
    }

    /// <summary>Frozen splitStoragePath: last segment is the file id.</summary>
    public static (string FolderName, string FileId) Split(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0
            ? (string.Empty, path)
            : (path[..index], path[(index + 1)..]);
    }
}
