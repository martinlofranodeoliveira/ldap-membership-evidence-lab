using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Novell.Directory.Ldap;
using Xunit;

namespace MembershipEvidence.Tests;

public sealed class NativeTests
{
    [Fact]
    public async Task RealClientTimesOutAgainstOwnedSilentPeer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var accept = listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            using var connection = new LdapConnection { ConnectionTimeout = 500 };
            connection.Constraints = new LdapConstraints { ReferralFollowing = false, TimeLimit = 200 };
            await connection.ConnectAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, TestContext.Current.CancellationToken);
            using var peer = await accept;
            var started = Stopwatch.GetTimestamp();
            var exception = await Assert.ThrowsAnyAsync<LdapException>(() => connection.BindAsync("", "", TestContext.Current.CancellationToken));
            Assert.Equal(LdapException.LdapTimeout, exception.ResultCode);
            Assert.True(Stopwatch.GetElapsedTime(started).TotalSeconds < 1.5);
        }
        finally { listener.Stop(); }
    }
    [Fact]
    public async Task PrivateCopyOfOwnerRejectsUnsupportedModeWithoutToolDiagnostics()
    {
        var directory = Path.Combine(Path.GetTempPath(), "owner-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var executable = Path.Combine(directory, "owner");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "ldap-evidence-owner"), executable);
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var info = new ProcessStartInfo(executable) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var value in new[] { "arbitrary-command", directory, "12345" }) info.ArgumentList.Add(value);
            using var process = Process.Start(info)!;
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, process.ExitCode);
            Assert.Equal("", await stdout);
            Assert.Equal("", await stderr);
        }
        finally
        {
            File.Delete(Path.Combine(directory, "owner"));
            Directory.Delete(directory);
        }
    }
    [Fact]
    public async Task FailedOwnedSeedIsSanitizedAndReaped()
    {
        var directory = Path.Combine(Path.GetTempPath(), "owner-negative-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            File.WriteAllText(Path.Combine(directory, "slapd.conf"), "not-a-valid-synthetic-configuration\n");
            var exception = await Assert.ThrowsAsync<FixtureException>(() => NativeOwner.Run("seed", directory, 12345, TestContext.Current.CancellationToken));
            Assert.Equal("NATIVE_OPERATION_FAILED", exception.Code);
            Assert.DoesNotContain(directory, exception.ToString());
        }
        finally
        {
            File.Delete(Path.Combine(directory, "slapd.conf"));
            Directory.Delete(directory);
        }
    }
}
