namespace SvnHub.App.System;

public interface ISvnRepositoryWriter
{
    Task DeleteAsync(
        string repoLocalPath,
        string path,
        string message,
        CancellationToken cancellationToken = default);

    Task MoveAsync(
        string repoLocalPath,
        string oldPath,
        string newPath,
        string message,
        CancellationToken cancellationToken = default);

    Task PutFileAsync(
        string repoLocalPath,
        string oldPath,
        string newPath,
        byte[] contents,
        string message,
        CancellationToken cancellationToken = default);
}
