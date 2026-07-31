namespace Testbench.Core.Deploy;

/// <summary>
/// Replaces a target folder with an exact copy of a source folder.
///
/// The PowerShell bench did "Remove-Item -Recurse; robocopy /E" for this, which
/// is the same contract: the target is not merged into, it is replaced. Doing it
/// in-process avoids robocopy's exit-code convention (0-7 are success, which is
/// easy to get wrong) and gives a usable error when a file is locked, which is
/// the common case because the game keeps its DLLs open.
/// </summary>
public static class DirectoryMirror
{
    public static void Replace(string source, string target)
    {
        if (!Directory.Exists(source)) throw new IOException($"Quelle fehlt: {source}");

        DeleteIfExists(target);
        Directory.CreateDirectory(target);
        CopyInto(source, target);
    }

    public static void DeleteIfExists(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            // Read-only attributes come along with mods unpacked from zips and
            // would make Delete throw halfway through, leaving a partial folder.
            ClearReadOnly(path);
            Directory.Delete(path, recursive: true);
        }
        catch (IOException ex)
        {
            throw new IOException(
                $"'{path}' liess sich nicht loeschen: {ex.Message} " +
                "Laeuft 7DaysToDie.exe noch? Das Spiel haelt die Mod-DLLs offen.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException($"Kein Zugriff auf '{path}': {ex.Message}", ex);
        }
    }

    private static void CopyInto(string source, string target)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var dst = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(file, dst, overwrite: true);
            // Copied attributes would make the next Replace fail on delete.
            var attrs = File.GetAttributes(dst);
            if (attrs.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(dst, attrs & ~FileAttributes.ReadOnly);
        }
    }

    private static void ClearReadOnly(string dir)
    {
        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if (attrs.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
        }
    }
}
