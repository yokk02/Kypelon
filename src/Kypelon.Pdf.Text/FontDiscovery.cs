namespace Kypelon.Pdf.Text;

/// <summary>Opt-in font-file discovery. Explicit font registration is recommended for reproducibility.</summary>
public static class FontDiscovery
{
    /// <summary>Enumerates TrueType files from conventional system directories, ignoring inaccessible subdirectories.</summary>
    public static IEnumerable<string> SystemTrueTypeFiles()
    {
        string[] roots = OperatingSystem.IsWindows() ? [Environment.GetFolderPath(Environment.SpecialFolder.Fonts)] : OperatingSystem.IsMacOS() ? ["/System/Library/Fonts", "/Library/Fonts"] : ["/usr/share/fonts", "/usr/local/share/fonts"];
        foreach (var root in roots)
            if (Directory.Exists(root))
                foreach (var file in Directory.EnumerateFiles(root, "*.ttf", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                    yield return file;
    }
}
