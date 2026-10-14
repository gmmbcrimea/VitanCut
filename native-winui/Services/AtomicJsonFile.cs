using System.Text.Json;

namespace VitanCut.WinUI.Services;

public static class AtomicJsonFile
{
    public static void Write(string path, string json)
    {
        using (JsonDocument.Parse(json)) { }
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            using (JsonDocument.Parse(File.ReadAllText(temporaryPath))) { }
            if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
            else File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
