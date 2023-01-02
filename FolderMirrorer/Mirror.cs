using System.Diagnostics;
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

    [GeneratedRegex("^([0-9a-zA-Z_-])+$")]
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

    public bool DoMirror()
    {
        var isValid = IsValid(out var errors);

        if (!isValid)
        {
            Printer.Print(errors);
            return false;
        }

        var arguments = new[]
        {
            Source,
            Path.Combine(Directory.GetCurrentDirectory(), RelativeDestination),
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

        var stdOut = "";
        var stdErr = "";

        using (var p = new Process() { StartInfo = startInfo })
        {
            p.Start();

            stdOut = p.StandardOutput.ReadToEnd();
            stdErr = p.StandardError.ReadToEnd();

            p.WaitForExit();

            if (p.ExitCode > 8)
            {
                Printer.Print($"Error occured in {Name}: {p.ExitCode}", ConsoleColor.Red);
                Printer.Print(stdOut);
                Printer.Print(stdErr);
                return false;
            }
        }

        PrintResult(stdOut);

        return true;
    }

    private static void PrintResult(string stdOut)
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

        Printer.Print($"Dirs: {dirsPretty}", ConsoleColor.Cyan);
        Printer.Print($"Files: {filesPretty}", ConsoleColor.Cyan);
        Printer.Print($"Bytes: {bytesPretty}", ConsoleColor.Cyan);
        Printer.Print($"Times: {timesPretty}", ConsoleColor.Cyan);
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
