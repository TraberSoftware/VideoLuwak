using System.Runtime.InteropServices;

namespace VideoLuwak.Core.Cli;

/// <summary>
/// Windows console plumbing for the dual-mode (CLI + GUI) executable.
/// </summary>
/// <remarks>
/// The application is built as a console-subsystem process so that <c>cmd.exe</c> and
/// PowerShell wait for it and print the next prompt only after the CLI has finished.
/// A GUI-subsystem (WinExe) process is not awaited by the shell: it returns to the prompt
/// immediately, so console output is written on top of the prompt and the command looks
/// stuck until the user presses Enter. <c>AttachConsole</c> cannot fix that ordering.
/// <para>
/// When the GUI is requested instead, the console Windows allocated for the process is
/// detached so double-clicking the executable does not leave a console window behind.
/// Every operation is best-effort and never throws.
/// </para>
/// </remarks>
public static class ConsoleBridge
{
    /// <summary>Detaches the process from its console. No-op on non-Windows platforms.</summary>
    public static void DetachConsoleForGuiMode()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            FreeConsole();
        }
        catch (Exception)
        {
            // Console teardown must never crash the application.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();
}
