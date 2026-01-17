namespace SvnHub.App.System;

public interface ISvnRepositoryWriter
{
    Task DeleteAsync(
        string repoLocalPath,
        string path,
        string message,
        CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(
        string repoLocalPath,
        string path,
        IReadOnlyList<SvnPropertyEdit> propertyEdits,
        string message,
        CancellationToken cancellationToken = default);

    Task UploadAsync(
        string repoLocalPath,
        IReadOnlyList<string> createDirectories,
        IReadOnlyList<SvnPutFile> putFiles,
        string message,
        CancellationToken cancellationToken = default);

    Task EditAsync(
        string repoLocalPath,
        string oldPath,
        string newPath,
        byte[]? newContents,
        IReadOnlyList<SvnPropertyEdit> propertyEdits,
        string message,
        CancellationToken cancellationToken = default);
}
