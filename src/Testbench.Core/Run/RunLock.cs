using System.Diagnostics;
using System.Text.Json;

namespace Testbench.Core.Run;

public sealed record LockInfo(int Pid, string Owner, string What, DateTimeOffset Since);

/// <summary>
/// Only one run at a time, machine-wide.
///
/// Two runs cannot coexist: they share Steam, the server ports and above all the
/// GamePrefs registry key, where the second run's backup would capture the first
/// run's defaults and then restore those as "the tuned values". The GUI and an
/// agent both drive runs, so the guard cannot live in either of them.
///
/// The named mutex does the actual excluding; the lock file exists so the other
/// side can say WHO is running WHAT instead of just refusing.
/// </summary>
public sealed class RunLock : IDisposable
{
    private const string MutexName = @"Global\SevenDaysTestbench.Run";

    private readonly Mutex _mutex;
    private readonly string _lockFile;
    private bool _held;

    private RunLock(Mutex mutex, string lockFile, bool held)
    {
        _mutex = mutex;
        _lockFile = lockFile;
        _held = held;
    }

    /// <summary>
    /// Tries to take the lock. Returns null and fills <paramref name="holder"/>
    /// when someone else has it.
    /// </summary>
    public static RunLock? TryAcquire(string stateRoot, string owner, string what, out LockInfo? holder)
    {
        holder = null;
        Directory.CreateDirectory(stateRoot);
        var lockFile = Path.Combine(stateRoot, "run.lock");

        var mutex = new Mutex(initiallyOwned: false, MutexName);
        var got = false;
        try
        {
            got = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // Previous holder died without releasing. The lock is ours, and the
            // stale lock file is overwritten below.
            got = true;
        }

        if (!got)
        {
            holder = ReadLockFile(lockFile);
            mutex.Dispose();
            return null;
        }

        var info = new LockInfo(Environment.ProcessId, owner, what, DateTimeOffset.Now);
        try
        {
            Config.ConfigStore.WriteAtomic(lockFile, JsonSerializer.Serialize(info, Config.ConfigStore.Json));
        }
        catch (IOException)
        {
            // A lock file we cannot write is a nuisance, not a reason to refuse:
            // the mutex is what actually excludes.
        }

        return new RunLock(mutex, lockFile, true);
    }

    /// <summary>Who holds the lock right now, or null if nobody does.</summary>
    public static LockInfo? CurrentHolder(string stateRoot)
    {
        var lockFile = Path.Combine(stateRoot, "run.lock");
        var mutex = new Mutex(initiallyOwned: false, MutexName);
        try
        {
            var free = mutex.WaitOne(TimeSpan.Zero);
            if (free) { mutex.ReleaseMutex(); return null; }
            return ReadLockFile(lockFile);
        }
        catch (AbandonedMutexException)
        {
            mutex.ReleaseMutex();
            return null;
        }
        finally
        {
            mutex.Dispose();
        }
    }

    private static LockInfo? ReadLockFile(string lockFile)
    {
        if (!File.Exists(lockFile)) return null;
        try
        {
            var info = JsonSerializer.Deserialize<LockInfo>(File.ReadAllText(lockFile), Config.ConfigStore.Json);
            if (info is null) return null;

            // A lock file whose process is gone describes nothing. Say so rather
            // than blaming a PID that has since been reused.
            try { using var _ = Process.GetProcessById(info.Pid); }
            catch (ArgumentException) { return null; }

            return info;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (!_held) return;
        _held = false;
        try { File.Delete(_lockFile); } catch (IOException) { }
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }
}
