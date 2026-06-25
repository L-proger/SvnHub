namespace SvnHub.Domain;

public sealed record RepositoryIndexingSettings(
    bool Enabled,
    int ScanIntervalSeconds,
    int MaxRevisionsPerRepositoryPerScan);
