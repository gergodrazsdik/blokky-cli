using System.Diagnostics;

namespace Blokky.Core.Tests;

/// <summary>
/// Runs work with a deadline, for tests that bound running time: a regression fails the test with a
/// <see cref="TimeoutException"/> instead of hanging it. The timed-out work keeps running in the background until it ends.
/// </summary>
internal static class Bounded
{
    /// <summary>Runs <paramref name="work"/> on the thread pool; its result, time and bytes allocated on its thread.</summary>
    public static async Task<(T Result, TimeSpan Elapsed, long Allocated)> RunAsync<T>(Func<T> work, TimeSpan timeout)
    {
        var run = Task.Run(() =>
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            var result = work();
            return (result, clock.Elapsed, GC.GetAllocatedBytesForCurrentThread() - before);
        });
        return await run.WaitAsync(timeout).ConfigureAwait(false);
    }
}
