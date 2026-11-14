using PopRunner.Checks;

try
{
    EngineChecks.Run();
    ClockChecks.Run();
    ConfigurationChecks.Run();
    ResourceChecks.Run();
    Console.WriteLine($"PASS — {Check.Passed} Core behavior tests.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
