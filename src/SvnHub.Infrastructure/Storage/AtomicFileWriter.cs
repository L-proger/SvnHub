using System.Text;

namespace SvnHub.Infrastructure.Storage;

internal static class AtomicFileWriter
{
    public static void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (OperatingSystem.IsWindows() && File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
            return;
        }

        File.Move(tempPath, path, overwrite: true);
    }
}

