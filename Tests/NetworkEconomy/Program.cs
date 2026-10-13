try {
    if (args.Length == 3 && args[2] == "--bots") await BotNetworkIntegration.Run(args);
    else await EconomyIntegration.Run(args);
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
