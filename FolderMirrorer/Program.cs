using FolderMirrorer;


var mirrorsConfigFileLocation = Path.Combine(Directory.GetCurrentDirectory(), "mirrors.json");
if (!File.Exists(mirrorsConfigFileLocation))
{
    Printer.Print("Config file does not exist", ConsoleColor.Red);
    Environment.Exit(1);
}

var mirrors = await MirrorList<RobocopyMirror>.LoadFromPath(mirrorsConfigFileLocation);

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

    // Note: intentionally awaiting each individual mirroring task, as robocopy is set to use threads
    var success = await mirror.DoMirror();
    if (!success)
    {
        Environment.Exit(1);
    }

    Printer.Print("");
}


Printer.Print("DONE, sleeping a bit to let you read output", ConsoleColor.Green);

await Task.Delay(TimeSpan.FromSeconds(5));
Environment.Exit(0);
