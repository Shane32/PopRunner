using System.IO;

namespace PopRunner.Sample;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? capturePath = null;
        try
        {
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index] != "--capture-preview") throw new ArgumentException($"Unknown argument '{args[index]}'.");
                if (++index >= args.Length) throw new ArgumentException("--capture-preview requires a path.");
                capturePath = Path.GetFullPath(args[index]);
            }
            ApplicationConfiguration.Initialize();
            using var form = new MainForm(capturePath);
            Application.Run(form);
            return form.ExitCode;
        }
        catch (Exception ex)
        {
            if (capturePath != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(capturePath))!);
                File.WriteAllText(capturePath + ".error.txt", ex.ToString());
            }
            else MessageBox.Show($"PopRunner.Sample could not start.\n\n{ex.Message}", "PopRunner.Sample",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
