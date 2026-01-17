namespace SvnHub.App.System;

public interface ISvnLookClient
{
    Task<long> GetYoungestRevisionAsync(string repoLocalPath, CancellationToken cancellationToken = default);

    Task<DateTimeOffset> GetHeadChangedAtAsync(string repoLocalPath, CancellationToken cancellationToken = default);

    Task<long> GetFileSizeAsync(
        string repoLocalPath,
        string filePath,
        long revision,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<SvnTreeEntry>> ListTreeAsync(
        string repoLocalPath,
        string path,
        long revision,
        CancellationToken cancellationToken = default
    );

    Task<string> CatAsync(
        string repoLocalPath,
        string filePath,
        long revision,
        CancellationToken cancellationToken = default
    );

    Task<byte[]> CatBytesAsync(
        string repoLocalPath,
        string filePath,
        long revision,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<SvnProperty>> GetPropertiesAsync(
        string repoLocalPath,
        string path,
        long revision,
        CancellationToken cancellationToken = default
    );
}
