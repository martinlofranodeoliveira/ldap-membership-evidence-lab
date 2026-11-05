using System.Runtime.InteropServices;

namespace MembershipEvidence;

internal sealed class WriterLock : IDisposable
{
    private readonly FileStream file;
    private WriterLock(FileStream file) { this.file = file; }
    [DllImport("libc", SetLastError = true)]
    private static extern int flock(int fd, int operation);
    internal static async Task<IDisposable> Acquire(string directory, Budget budget)
    {
        var file = new FileStream(Path.Combine(directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        try
        {
            while (true)
            {
                budget.Check();
                if (flock(file.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4) == 0) return new WriterLock(file);
                var error = Marshal.GetLastPInvokeError();
                if (error != 11 && error != 4) throw new ObservationFailure("LOCK_ERROR");
                await Task.Delay(Math.Min(10, budget.RemainingMilliseconds));
                budget.Check();
            }
        }
        catch { file.Dispose(); throw; }
    }
    public void Dispose()
    {
        flock(file.SafeFileHandle.DangerousGetHandle().ToInt32(), 8);
        file.Dispose();
    }
}
