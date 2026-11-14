namespace PopRunner.Checks;

internal static class Check
{
    internal static int Passed { get; private set; }

    internal static void Run(string name, Action test)
    {
        try { test(); }
        catch (Exception error) { throw new InvalidOperationException($"FAIL: {name}", error); }
        Passed++;
        Console.WriteLine($"PASS: {name}");
    }

    internal static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Near(double actual, double expected, string message) =>
        That(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-7,
            $"{message} Expected {expected}, got {actual}.");

    internal static void Throws<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        catch (Exception error)
        {
            throw new InvalidOperationException($"{message} Expected {typeof(TException).Name}, got {error.GetType().Name}.", error);
        }
        throw new InvalidOperationException($"{message} Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
