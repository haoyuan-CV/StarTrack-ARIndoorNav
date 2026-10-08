using System;
using System.IO;
using UnityEngine;

public static class NavTrace
{
    private const string FileName = "startrack_nav_trace.txt";

    public static string LogPath => Path.Combine(Application.persistentDataPath, FileName);

    public static void Log(string message)
    {
        string line = $"[NAVTRACE] {DateTime.Now:HH:mm:ss.fff} {message}";
        Debug.LogWarning(line);
        WriteLine(line);
    }

    public static void Clear()
    {
        try
        {
            File.WriteAllText(LogPath, "");
            Debug.LogWarning($"[NAVTRACE] cleared file={LogPath}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NAVTRACE] failed to clear trace file: {e.Message}");
        }
    }

    private static void WriteLine(string line)
    {
        try
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NAVTRACE] failed to write trace file: {e.Message}");
        }
    }
}
