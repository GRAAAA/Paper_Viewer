using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PaperView;

internal static class Program
{
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0 || (args.Length == 1 && File.Exists(args[0])))
        {
            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }

        var stdout = GetStdHandle(-11);
        if (stdout == IntPtr.Zero || stdout == new IntPtr(-1)) AttachConsole(uint.MaxValue);
        // Open after attaching; redirected handles also work for scripts and tests.
        using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        using var error = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
        Console.SetOut(output);
        Console.SetError(error);
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Pass one PDF path or one command. Use --help for usage.");
            return 2;
        }
        var command = args[0].ToLowerInvariant();
        if (command is "--version" or "version")
        {
            Console.WriteLine("PaperView " + Assembly.GetExecutingAssembly().GetName().Version!.ToString(3));
            return 0;
        }
        if (command is "--help" or "help" or "-h")
        {
            Console.WriteLine("PaperView [PDF path | --version | install | update | uninstall]\nInstall for the current user; reopen your terminal, then use paperview.\nUpdates come from GRAAAA/Paper_Viewer GitHub Releases. Uninstall preserves reading history.");
            return 0;
        }
        if (command is not ("install" or "update" or "uninstall"))
        {
            Console.Error.WriteLine("Unknown command or missing PDF: " + args[0] + ". Use --help for usage.");
            return 2;
        }
        try
        {
            var source = Environment.ProcessPath!;
            if (command == "install" && File.Exists(Path.ChangeExtension(source, ".runtimeconfig.json")))
                throw new InvalidOperationException("Install requires the standalone release executable. Run scripts/publish.ps1 first.");
            var staging = Path.Combine(Path.GetTempPath(), "PaperView-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var script = Path.Combine(staging, "manage.ps1");
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("PaperView.Packaging.manage.ps1")!)
            using (var destination = File.Create(script)) resource.CopyTo(destination);
            var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PaperView", "PaperView.exe");
            var self = string.Equals(source, installed, StringComparison.OrdinalIgnoreCase);
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = !self, RedirectStandardError = !self
            };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Command", command, "-Source", source, "-CleanupScript" })
                start.ArgumentList.Add(argument);
            if (self)
            {
                start.ArgumentList.Add("-WaitForProcess");
                start.ArgumentList.Add(Environment.ProcessId.ToString());
                Console.WriteLine("Finishing after this process exits. Result: %LOCALAPPDATA%\\PaperView\\maintenance.log\nUse the paperview terminal command for synchronous maintenance.");
            }
            using var process = Process.Start(start)!;
            if (self) return 0;
            // GUI executables do not reliably pass console handles to a hidden child.
            // Explicitly relay both streams as they arrive, including progress/errors.
            var stdoutRelay = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
            var stderrRelay = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
            process.WaitForExit();
            Task.WhenAll(stdoutRelay, stderrRelay).GetAwaiter().GetResult();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PaperView: " + ex.Message);
            return 1;
        }
    }
}
