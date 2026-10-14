using System.Text.Json;

namespace VitanCut.WinUI.Services;

public static class AtomicJsonPairFile
{
    private sealed record Journal(
        string FirstPath,
        string FirstTemporaryPath,
        string FirstBackupPath,
        bool FirstExisted,
        string SecondPath,
        string SecondTemporaryPath,
        string SecondBackupPath,
        bool SecondExisted,
        string Phase,
        string? ThirdPath = null,
        string? ThirdTemporaryPath = null,
        string? ThirdBackupPath = null,
        bool ThirdExisted = false);

    public static void Recover(string journalPath, string expectedFirstPath, string expectedSecondPath)
        => RecoverCore(journalPath, expectedFirstPath, expectedSecondPath, null);

    public static void Recover(string journalPath, string expectedFirstPath, string expectedSecondPath, string expectedThirdPath)
        => RecoverCore(journalPath, expectedFirstPath, expectedSecondPath, expectedThirdPath);

    private static void RecoverCore(string journalPath, string expectedFirstPath, string expectedSecondPath, string? expectedThirdPath)
    {
        var fullJournalPath = Path.GetFullPath(journalPath);
        if (!File.Exists(fullJournalPath)) return;

        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(fullJournalPath))
            ?? throw new InvalidDataException("Atomic JSON pair journal is empty.");
        ValidateJournal(journal, fullJournalPath, expectedFirstPath, expectedSecondPath, expectedThirdPath);
        if (journal.Phase == "prepared")
        {
            Restore(journal.FirstPath, journal.FirstBackupPath, journal.FirstExisted);
            Restore(journal.SecondPath, journal.SecondBackupPath, journal.SecondExisted);
            if (journal.ThirdPath is not null)
                Restore(journal.ThirdPath, journal.ThirdBackupPath!, journal.ThirdExisted);
        }
        else if (journal.Phase != "committed")
        {
            throw new InvalidDataException("Atomic JSON pair journal has an unknown phase.");
        }

        Cleanup(journal, fullJournalPath);
    }

    public static void Write(string firstPath, string firstJson, string secondPath, string secondJson)
        => WriteCore(firstPath, firstJson, secondPath, secondJson, null, null, null);

    public static void Write(string firstPath, string firstJson, string secondPath, string secondJson,
        string thirdPath, string thirdJson)
        => WriteCore(firstPath, firstJson, secondPath, secondJson, thirdPath, thirdJson, null);

    internal static void Write(string firstPath, string firstJson, string secondPath, string secondJson, Action<string>? checkpoint)
        => WriteCore(firstPath, firstJson, secondPath, secondJson, null, null, checkpoint);

    internal static void Write(string firstPath, string firstJson, string secondPath, string secondJson,
        string thirdPath, string thirdJson, Action<string>? checkpoint)
        => WriteCore(firstPath, firstJson, secondPath, secondJson, thirdPath, thirdJson, checkpoint);

    private static void WriteCore(string firstPath, string firstJson, string secondPath, string secondJson,
        string? thirdPath, string? thirdJson, Action<string>? checkpoint)
    {
        using (JsonDocument.Parse(firstJson)) { }
        using (JsonDocument.Parse(secondJson)) { }
        if (thirdPath is not null)
        {
            if (thirdJson is null) throw new ArgumentNullException(nameof(thirdJson));
            using (JsonDocument.Parse(thirdJson)) { }
        }

        var first = Path.GetFullPath(firstPath);
        var second = Path.GetFullPath(secondPath);
        var third = thirdPath is null ? null : Path.GetFullPath(thirdPath);
        if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Atomic JSON pair targets must be different files.");
        if (third is not null && (string.Equals(first, third, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(second, third, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Atomic JSON transaction targets must be different files.");
        var directory = Path.GetDirectoryName(first);
        if (!string.Equals(directory, Path.GetDirectoryName(second), StringComparison.OrdinalIgnoreCase) ||
            (third is not null && !string.Equals(directory, Path.GetDirectoryName(third), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Atomic JSON transaction targets must share a directory.");

        Directory.CreateDirectory(directory!);
        var journalPath = first + ".pair-journal.json";
        if (third is null) Recover(journalPath, first, second);
        else Recover(journalPath, first, second, third);

        var id = Guid.NewGuid().ToString("N");
        var journal = new Journal(
            first, first + "." + id + ".tmp", first + "." + id + ".bak", File.Exists(first),
            second, second + "." + id + ".tmp", second + "." + id + ".bak", File.Exists(second),
            "prepared",
            third, third is null ? null : third + "." + id + ".tmp",
            third is null ? null : third + "." + id + ".bak", third is not null && File.Exists(third));

        try
        {
            checkpoint?.Invoke("before-first-temporary-write");
            WriteValidated(journal.FirstTemporaryPath, firstJson);
            checkpoint?.Invoke("before-second-temporary-write");
            WriteValidated(journal.SecondTemporaryPath, secondJson);
            if (third is not null)
            {
                checkpoint?.Invoke("before-third-temporary-write");
                WriteValidated(journal.ThirdTemporaryPath!, thirdJson!);
            }
            if (journal.FirstExisted) File.Copy(first, journal.FirstBackupPath);
            if (journal.SecondExisted) File.Copy(second, journal.SecondBackupPath);
            if (journal.ThirdExisted) File.Copy(third!, journal.ThirdBackupPath!);
            checkpoint?.Invoke("before-journal-write");
            AtomicJsonFile.Write(journalPath, JsonSerializer.Serialize(journal));
            checkpoint?.Invoke("prepared");

            checkpoint?.Invoke("before-first-replace");
            Replace(journal.FirstTemporaryPath, first);
            checkpoint?.Invoke("first-replaced");
            checkpoint?.Invoke("before-second-replace");
            Replace(journal.SecondTemporaryPath, second);
            checkpoint?.Invoke("second-replaced");
            if (third is not null)
            {
                checkpoint?.Invoke("before-third-replace");
                Replace(journal.ThirdTemporaryPath!, third);
                checkpoint?.Invoke("third-replaced");
            }
            AtomicJsonFile.Write(journalPath, JsonSerializer.Serialize(journal with { Phase = "committed" }));
            checkpoint?.Invoke("committed");
            Cleanup(journal, journalPath);
        }
        catch (Exception error)
        {
            try
            {
                if (third is null) Recover(journalPath, first, second);
                else Recover(journalPath, first, second, third);
            }
            catch (Exception recoveryError)
            {
                throw new IOException("Atomic JSON pair failed and recovery is still required.",
                    new AggregateException(error, recoveryError));
            }
            throw;
        }
        finally
        {
            DeleteIfExists(journal.FirstTemporaryPath);
            DeleteIfExists(journal.SecondTemporaryPath);
            if (journal.ThirdTemporaryPath is not null) DeleteIfExists(journal.ThirdTemporaryPath);
            if (!File.Exists(journalPath))
            {
                DeleteIfExists(journal.FirstBackupPath);
                DeleteIfExists(journal.SecondBackupPath);
                if (journal.ThirdBackupPath is not null) DeleteIfExists(journal.ThirdBackupPath);
            }
        }
    }

    private static void WriteValidated(string path, string json)
    {
        File.WriteAllText(path, json);
        using (JsonDocument.Parse(File.ReadAllText(path))) { }
    }

    private static void Replace(string source, string target)
    {
        if (File.Exists(target)) File.Replace(source, target, null);
        else File.Move(source, target);
    }

    private static void Restore(string target, string backup, bool existed)
    {
        if (!existed)
        {
            DeleteIfExists(target);
            return;
        }
        if (!File.Exists(backup))
            throw new IOException($"Atomic JSON pair backup is missing: {backup}");

        var restorePath = target + "." + Guid.NewGuid().ToString("N") + ".restore.tmp";
        try
        {
            File.Copy(backup, restorePath);
            Replace(restorePath, target);
        }
        finally { DeleteIfExists(restorePath); }
    }

    private static void ValidateJournal(Journal journal, string journalPath, string expectedFirstPath,
        string expectedSecondPath, string? expectedThirdPath)
    {
        if (string.IsNullOrWhiteSpace(journal.FirstPath) || string.IsNullOrWhiteSpace(journal.FirstTemporaryPath) ||
            string.IsNullOrWhiteSpace(journal.FirstBackupPath) || string.IsNullOrWhiteSpace(journal.SecondPath) ||
            string.IsNullOrWhiteSpace(journal.SecondTemporaryPath) || string.IsNullOrWhiteSpace(journal.SecondBackupPath) ||
            string.IsNullOrWhiteSpace(journal.Phase))
            throw new InvalidDataException("Atomic JSON pair journal is missing required paths or phase.");

        var first = Path.GetFullPath(journal.FirstPath);
        var second = Path.GetFullPath(journal.SecondPath);
        if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(first, Path.GetFullPath(expectedFirstPath), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(second, Path.GetFullPath(expectedSecondPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Atomic JSON pair journal does not match the expected files.");
        var expectedJournal = first + ".pair-journal.json";
        var firstDirectory = Path.GetDirectoryName(first);
        var secondDirectory = Path.GetDirectoryName(second);
        if (!string.Equals(expectedJournal, journalPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(firstDirectory, secondDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Atomic JSON pair journal targets are outside its transaction directory.");
        if (journal.ThirdPath is null)
        {
            if (journal.ThirdTemporaryPath is not null || journal.ThirdBackupPath is not null || journal.ThirdExisted)
                throw new InvalidDataException("Atomic JSON pair journal has incomplete third-file metadata.");
        }
        else
        {
            if (expectedThirdPath is null || !string.Equals(journal.ThirdPath, Path.GetFullPath(expectedThirdPath), StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(journal.ThirdTemporaryPath) || string.IsNullOrWhiteSpace(journal.ThirdBackupPath) ||
                !string.Equals(Path.GetDirectoryName(Path.GetFullPath(journal.ThirdPath)), firstDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Atomic JSON transaction journal does not match the expected third file.");
            ValidateTransactionFile(journal.ThirdTemporaryPath, journal.ThirdPath, ".tmp");
            ValidateTransactionFile(journal.ThirdBackupPath, journal.ThirdPath, ".bak");
        }

        ValidateTransactionFile(journal.FirstTemporaryPath, first, ".tmp");
        ValidateTransactionFile(journal.FirstBackupPath, first, ".bak");
        ValidateTransactionFile(journal.SecondTemporaryPath, second, ".tmp");
        ValidateTransactionFile(journal.SecondBackupPath, second, ".bak");
    }

    private static void ValidateTransactionFile(string candidate, string target, string suffix)
    {
        var fullCandidate = Path.GetFullPath(candidate);
        if (!string.Equals(Path.GetDirectoryName(fullCandidate), Path.GetDirectoryName(target), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullCandidate).StartsWith(Path.GetFileName(target) + ".", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullCandidate).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Atomic JSON pair journal contains an unsafe transaction path.");
    }

    private static void Cleanup(Journal journal, string journalPath)
    {
        DeleteIfExists(journalPath);
        DeleteIfExists(journal.FirstTemporaryPath);
        DeleteIfExists(journal.SecondTemporaryPath);
        DeleteIfExists(journal.FirstBackupPath);
        DeleteIfExists(journal.SecondBackupPath);
        if (journal.ThirdBackupPath is not null) DeleteIfExists(journal.ThirdBackupPath);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
