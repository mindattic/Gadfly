using Gadfly.Tour;

Console.OutputEncoding = System.Text.Encoding.UTF8;

TourOptions options;
try
{
    options = TourOptions.Parse(args);
}
catch (HelpRequested)
{
    Console.WriteLine(TourOptions.Help);
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(TourOptions.Help);
    return 2;
}

Console.WriteLine($"Gadfly demo tour: {(options.Live ? "LIVE providers" : "MOCK panel")}, {options.BaseUrl}");
await using var server = new ServerProcess();
try
{
    if (options.Url is null)
    {
        if (options.Fresh)
        {
            Console.WriteLine($"Dropping database {TourOptions.TourDatabase} (--fresh)...");
            await TourDb.DropDatabaseAsync(options.ConnectionString);
        }
        await server.StartAsync(options, CancellationToken.None);
        Console.WriteLine($"Started the server (log: {server.LogPath}).");
    }
    var tour = new DemoTour(options, server);
    var checklist = await tour.RunAsync();
    checklist.Summary();
    if (options.Keep && options.Url is null)
    {
        server.Detach();
        Console.WriteLine($"Server left running at {options.BaseUrl} (--keep). Demo password: {options.Password}");
    }
    return checklist.AnyFailed ? 1 : 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"✗ The tour could not run: {ex.Message}");
    if (options.Url is null) Console.Error.WriteLine($"  Server log: {server.LogPath}");
    return 1;
}
