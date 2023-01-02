using FolderMirrorer;
using System.Text.Json;


var mirrorsConfigFileLocation = Path.Combine(Directory.GetCurrentDirectory(), "mirrors.json");
if (!File.Exists(mirrorsConfigFileLocation))
{
    Printer.Print("Config file does not exist", ConsoleColor.Red);
    Environment.Exit(1);
}

var jsonString = File.ReadAllText(mirrorsConfigFileLocation);
var mirrors = JsonSerializer.Deserialize<MirrorList<RobocopyMirror>>(jsonString);

if (mirrors == null)
{
    Printer.Print("Could not parse jsonfile");
    Environment.Exit(1);
}

if (!mirrors.IsValid(out var errors))
{
    Printer.Print("Problems found in mirrors", ConsoleColor.Red);
    foreach (var error in errors)
    {
        Printer.Print(error, ConsoleColor.Red);
    }
    Environment.Exit(1);
}
else
{
    Printer.Print("All Mirrors look good, starting", ConsoleColor.Green);
    Printer.Print("");
}

foreach (var mirror in mirrors.Where(m => m.Enabled))
{
    Printer.Print(mirror.ToString());

    var success = mirror.DoMirror();
    if (!success)
    {
        Environment.Exit(1);
    }

    Printer.Print("");
}


Printer.Print("DONE, sleeping a bit to let you read output", ConsoleColor.Green);

Thread.Sleep(TimeSpan.FromSeconds(5));
Environment.Exit(0);
