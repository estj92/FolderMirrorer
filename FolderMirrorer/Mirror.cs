using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FolderMirrorer;

public class MirrorList<T> : List<T>
    where T : RobocopyMirror
{
    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();

        var destinationCounts = this
            .GroupBy(i => i.RelativeDestination)
            .Select(s => new { Destination = s.Key, Count = s.Count() })
            .Where(s => s.Count > 1)
            .Select(s => $"Repeated destination: '{s.Destination}': {s.Count}");

        errors.AddRange(destinationCounts);

        foreach (var mirro in this)
        {
            _ = mirro.IsValid(out var mirrorErrors);
            errors.AddRange(mirrorErrors);
        }

        return errors.Count == 0;
    }

    public static async Task<MirrorList<T>> LoadFromPath(string path)
    {
        using var stream = File.OpenRead(path);
        var mirrors = await JsonSerializer.DeserializeAsync<MirrorList<T>>(stream);
        return mirrors ?? throw new Exception("Could not load mirrors");
    }
}

public partial class RobocopyMirror
{
    public string Name { get; set; }
    public string Source { get; set; }
    public string RelativeDestination { get; set; }
    public bool Enabled { get; set; }

    public RobocopyMirror(string name, string source, string relativeDestination, bool enabled)
    {
        Name = name;
        Source = source;
        RelativeDestination = relativeDestination;
        Enabled = enabled;
    }

    static readonly SingletonMultiLogger Logger = SingletonMultiLogger.Instance;

    [GeneratedRegex("^([ 0-9a-zA-Z_-])+$")]
    private static partial Regex GeneratedAllowedDestinationRegex();
    public static readonly Regex AllowedDestinationRegex = GeneratedAllowedDestinationRegex();

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();

        if (!Directory.Exists(Source))
        {
            errors.Add($"Source '{Source}' does not exist");
        }

        if (!AllowedDestinationRegex.IsMatch(RelativeDestination))
        {
            errors.Add($"Relative destination '{RelativeDestination}' contains disallowed characters");
        }

        return errors.Count == 0;
    }

    public async Task<bool> DoMirror()
    {
        var isValid = IsValid(out var errors);

        if (!isValid)
        {
            await Logger.Error(errors);
            return false;
        }

        (var exitCode, var stdOut, var stdErr) = await RunMirrorAndReturnOutput();

        if (exitCode > 8)
        {
            await Logger.Error($"Error occured in {Name}: {exitCode}");
            await Logger.Error(stdOut);
            await Logger.Error(stdErr);
            return false;
        }

        await LogResult(stdOut);

        return true;
    }

    private async Task<(int exitCode, string stdOut, string stdErr)> RunMirrorAndReturnOutput()
    {
        var arguments = new[] {
            $"\"{Source}\"",
            $"\"{Path.Combine(AppContext.BaseDirectory, RelativeDestination)}\"",
            "/e",  // Copy subdirectories
            "/j",  // Unbuffered IO
            "/mir",  // Mirror the directory tree
            "/r:5",  // Retry 5 times
            "/w:1",  // Wait 1 second between retries
            "/NFL",  // : No File List - don't log file names.
            "/NDL",  // : No Directory List - don't log directory names.
            "/NJH",  // : No Job Header.
            "/NP",  // : No Progress -don't display percentage copied.
            "/NS",  // : No Size -don't log file sizes.
            "/NC",  // : No Class -don't log file classes.
            "/bytes",  // Print size as bytes
            "/mt",  // Use multiple threads
            //"/l",  // Just list - remove for production
        };

        var startInfo = new ProcessStartInfo()
        {
            FileName = "ROBOCOPY",
            Arguments = string.Join(' ', arguments),
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var p = new Process() { StartInfo = startInfo };
        p.Start();

        var stdOutTask = p.StandardOutput.ReadToEndAsync();
        var stdErrTask = p.StandardError.ReadToEndAsync();

        Task.WaitAll(stdOutTask, stdErrTask);

        await p.WaitForExitAsync();

        return (p.ExitCode, stdOutTask.Result, stdErrTask.Result);

    }

    private static async Task LogResult(string stdOut)
    {
        static Match GetMatch(Regex regex, string[] lines)
        {
            return lines
                .Select(l => regex.Match(l))
                .Where(l => l.Success)
                .FirstOrDefault(Match.Empty);
        }

        static string GetLinePattern(string name, string total, string copied, string skipped, string mismatch, string failed, string extras)
        {
            return $@"^\s*{name}\s*:\s*(?<Total>{total})\s*(?<Copied>{copied})\s*(?<Skipped>{skipped})\s*(?<Mismatch>{mismatch})\s*(?<Failed>{failed})\s*(?<Extras>{extras}).*$";
        }

        var dirsRegex = new Regex(GetLinePattern("Dirs", @"\d+", @"\d+", @"\d+", @"\d+", @"\d+", @"\d+"));
        var filesRegex = new Regex(GetLinePattern("Files", @"\d+", @"\d+", @"\d+", @"\d+", @"\d+", @"\d+"));
        var bytesRegex = new Regex(GetLinePattern("Bytes", @"\d+", @"\d+", @"\d+", @"\d+", @"\d+", @"\d+"));
        var timePattern = @"\d+:\d+:\d+";
        var timesRegex = new Regex(GetLinePattern("Times", timePattern, timePattern, @"\s*", @"\s*", timePattern, timePattern));

        var stdOutLines = stdOut.Split(Environment.NewLine);

        var dirsPretty = IntegersToString(GetMatch(dirsRegex, stdOutLines), new[] { "", "K", "M", "B" }, 1000);
        var filesPretty = IntegersToString(GetMatch(filesRegex, stdOutLines), new[] { "", "K", "M", "B" }, 1000);
        var bytesPretty = IntegersToString(GetMatch(bytesRegex, stdOutLines), new[] { "B", "KiB", "MiB", "GiB" }, 1024);
        var timesPretty = TimesToString(GetMatch(timesRegex, stdOutLines));

        await Logger.Info($"Dirs: {dirsPretty}");
        await Logger.Info($"Files: {filesPretty}");
        await Logger.Info($"Bytes: {bytesPretty}");
        await Logger.Info($"Times: {timesPretty}");
    }

    private static string TimesToString(Match match)
    {
        static TimeSpan FromString(string input)
        {
            var parts = input.Split(':').Select(x => int.Parse(x)).ToArray();

            if (parts.Length > 3)
            {
                throw new ArgumentException("Invalid time value");
            }

            return TimeSpan.FromSeconds(parts[0] * 60 * 60 + parts[1] * 60 + parts[2]);
        }

        var keys = new[] { "Total", "Copied", "Failed", "Extras" };
        var parts = keys.Select(k => $"{k}: {FromString(match.Groups[k].Value)}");
        return string.Join(", ", parts);
    }

    private static string IntegersToString(Match match, string[] sizeNames, int decreaseSize)
    {
        var keys = new[] { "Total", "Copied", "Skipped", "Mismatch", "Failed", "Extras" };
        var values = keys.Select(k => $"{k}: {HumanReadableNumberFromString(match.Groups[k].Value, sizeNames, decreaseSize)}");
        return string.Join(", ", values);
    }

    private static string HumanReadableNumberFromString(string valueString, string[] names, int decreaseSize)
    {
        var value = long.Parse(valueString);

        var firstNames = names.Take(names.Length - 1);
        foreach (var name in firstNames)
        {
            if (value < decreaseSize * 5)
            {
                return $"{value} {name}";
            }

            value /= decreaseSize;
        }

        return $"{value} {names.Last()}";
    }

    public override string ToString() => $"{Name}: \"{Source}\" -> \"{RelativeDestination}\" [Enabled: {Enabled}]";
}
