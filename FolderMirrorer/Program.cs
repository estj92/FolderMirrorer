using FolderMirrorer;

var logger = SingletonMultiLogger.Instance;

var mirrorsConfigFileLocation = Path.Combine(AppContext.BaseDirectory, "mirrors.json");
if (!File.Exists(mirrorsConfigFileLocation))
{
    await logger.Error("Config file does not exist");
    Environment.Exit(1);
}

var mirrors = await MirrorList<RobocopyMirror>.LoadFromPath(mirrorsConfigFileLocation);
if (!mirrors.IsValid(out var errors))
{
    await logger.Error(errors);
    Environment.Exit(1);
}
else
{
    await logger.Info("All mirrors look good, starting");
}

foreach (var mirror in mirrors.Where(m => m.Enabled))
{
    await logger.Info(mirror.ToString());

    // Note: intentionally awaiting each individual mirroring task, as robocopy is set to use threads
    var success = await mirror.DoMirror();
    if (!success)
    {
        Environment.Exit(1);
    }
    SingletonMultiLogger.NewLine();
}

await Task.Delay(TimeSpan.FromSeconds(5));
Environment.Exit(0);
