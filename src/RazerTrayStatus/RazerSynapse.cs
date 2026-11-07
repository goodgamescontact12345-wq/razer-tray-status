using System.Diagnostics;

namespace RazerTrayStatus;

internal static class SynapseLauncher
{
    private static readonly string[] Synapse4Arguments = { "--url-params=apps=synapse" };

    public static string SearchSummary
    {
        get
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            return
                $"{Path.Combine(programFiles, "Razer", "RazerAppEngine", "RazerAppEngine.exe")} (Synapse 4)\n" +
                $"{Path.Combine(programFilesX86, "Razer", "Razer Services", "Razer Central", "Razer Central.exe")} (Synapse 3)";
        }
    }

    /// <summary>Starts Synapse 4 (or 3) with the window open. Returns false when nothing is installed.</summary>
    public static bool TryOpen(out string error)
    {
        error = string.Empty;

        foreach ((string executable, string[] arguments) in Candidates())
        {
            if (!File.Exists(executable)) continue;

            try
            {
                var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
                foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                error = $"{Path.GetFileName(executable)}: {ex.Message}";
            }
        }

        if (string.IsNullOrEmpty(error)) error = "Razer Synapse was not found in any known location.";
        return false;
    }

    private static IEnumerable<(string Executable, string[] Arguments)> Candidates()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string engineRoot = Path.Combine(programFiles, "Razer", "RazerAppEngine");

        // Synapse 4 launcher lives at the engine root; the versioned folders are a fallback.
        yield return (Path.Combine(engineRoot, "RazerAppEngine.exe"), Synapse4Arguments);

        foreach (string versionedEngine in VersionedEngines(engineRoot))
            yield return (versionedEngine, Synapse4Arguments);

        // Synapse 3.
        yield return (Path.Combine(programFilesX86, "Razer", "Razer Services", "Razer Central", "Razer Central.exe"), Array.Empty<string>());
        yield return (Path.Combine(programFilesX86, "Razer", "Razer Central", "Razer Central.exe"), Array.Empty<string>());
        yield return (
            Path.Combine(programFilesX86, "Razer", "Synapse3", "WPFUI", "Framework", "Razer Synapse 3 Host", "Razer Synapse 3.exe"),
            Array.Empty<string>());
        yield return (Path.Combine(programFilesX86, "Razer", "Synapse3", "WPFUI", "Razer.Synapse3.WPFUI.exe"), Array.Empty<string>());
    }

    private static IEnumerable<string> VersionedEngines(string engineRoot)
    {
        try
        {
            if (!Directory.Exists(engineRoot)) return Array.Empty<string>();

            return Directory.GetDirectories(engineRoot, "app-*")
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .Select(directory => Path.Combine(directory, "RazerAppEngine.exe"))
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
