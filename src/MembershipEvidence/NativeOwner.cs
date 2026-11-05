using System.Diagnostics;
using System.Text;

namespace MembershipEvidence;

internal sealed class NativeOwner : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> output;
    private readonly Task<string> errors;
    private bool stopped;
    private NativeOwner(Process process)
    {
        this.process = process;
        output = Drain(process.StandardOutput, 1024 * 1024);
        errors = Drain(process.StandardError, 8192);
    }
    internal bool HasExited => process.HasExited;
    internal static NativeOwner Start(string mode, string directory, int port)
    {
        if (mode is not ("server" or "seed" or "modify" or "oracle")) throw new FixtureException("OWNER_MODE");
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "ldap-evidence-owner"))
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directory
        };
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(directory);
        start.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            var process = Process.Start(start) ?? throw new FixtureException("OWNER_START");
            return new NativeOwner(process);
        }
        catch (System.ComponentModel.Win32Exception) { throw new FixtureException("OWNER_START"); }
    }
    private static async Task<string> Drain(StreamReader reader, int maximum)
    {
        var buffer = new char[4096];
        var text = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer)) != 0)
        {
            var remaining = maximum - text.Length;
            if (remaining > 0) text.Append(buffer, 0, Math.Min(remaining, count));
        }
        return text.ToString();
    }
    internal async Task<string> Finish(CancellationToken token)
    {
        using var registration = token.Register(() =>
        {
            try { process.StandardInput.Close(); } catch (IOException) { } catch (ObjectDisposedException) { }
        });
        await process.WaitForExitAsync();
        var text = await output;
        await errors;
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new FixtureException("NATIVE_OPERATION_FAILED");
        return text;
    }
    internal static async Task<string> Run(string mode, string directory, int port, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await using var owner = Start(mode, directory, port);
        return await owner.Finish(token);
    }
    public async ValueTask DisposeAsync()
    {
        if (stopped) return;
        stopped = true;
        try
        {
            if (!process.HasExited)
            {
                try { await process.StandardInput.WriteLineAsync("STOP"); }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync();
            await output;
            await errors;
        }
        finally { process.Dispose(); }
    }
}
