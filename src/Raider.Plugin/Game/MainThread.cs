#if RAID_INTEROP
extern alias UnityInterop;
#endif
using System.Collections.Concurrent;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace Raider.Plugin.Game;

/// <summary>
/// Runs work on Unity's main thread, where everything that changes game state must happen.
/// <para>
/// The plugin's HTTP server answers on thread-pool threads; a Chore queues its work here and awaits it. The queue
/// is drained every frame by <see cref="MainThreadPump"/>, a MonoBehaviour the plugin adds at load (see ADR 0004).
/// </para>
/// </summary>
internal static class MainThread
{
    private static readonly ConcurrentQueue<Action> Queue = new();
    private static ManualLogSource? _log;

    /// <summary>Why work cannot run on the main thread, or null when it can.</summary>
    public static string? UnavailableReason { get; private set; } = "the main-thread pump has not started";

    public static void Start(BasePlugin plugin, ManualLogSource log)
    {
        _log = log;
#if RAID_INTEROP
        try
        {
            plugin.AddComponent<MainThreadPump>();
            UnavailableReason = null;
            log.LogInfo("Main-thread pump started");
        }
        catch (Exception ex)
        {
            UnavailableReason = $"the main-thread pump could not start: {ex.Message}";
            log.LogError($"Could not start the main-thread pump: {ex}");
        }
#else
        UnavailableReason = "this plugin was built without Raid's interop assemblies, so it cannot act in the game";
        log.LogError($"Chores are disabled: {UnavailableReason}");
#endif
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the main thread and returns its result. Fails if the game does not get to
    /// it within <paramref name="timeout"/> (the game's frame loop is not running).
    /// <para>
    /// Work that timed out never runs: the main thread skips it if it gets to it later, so a caller told "did not
    /// start" can trust that nothing happened in the game. Work the main thread already started is waited for.
    /// </para>
    /// </summary>
    public static async Task<T> RunAsync<T>(Func<T> work, TimeSpan? timeout = null)
    {
        if (UnavailableReason != null)
        {
            throw new InvalidOperationException($"Cannot act in the game: {UnavailableReason}");
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new WorkState();
        Queue.Enqueue(() =>
        {
            if (!state.TryStart())
            {
                return;
            }

            try
            {
                completion.TrySetResult(work());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        var finished = await Task.WhenAny(completion.Task, Task.Delay(timeout ?? DefaultTimeout));
        if (finished != completion.Task && state.TryAbandon())
        {
            throw new TimeoutException("The game's main thread did not pick up the work (is the game frozen?)");
        }

        // Either it finished, or the main thread started it just as the wait ran out; it finishes within the frame
        return await completion.Task;
    }

    /// <summary>How long callers wait by default for the main thread to pick up work.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Whether queued work was started by the main thread or abandoned by its caller; whichever comes first wins.</summary>
    private sealed class WorkState
    {
        private const int Queued = 0, Started = 1, Abandoned = 2;
        private int _value = Queued;

        public bool TryStart() => Interlocked.CompareExchange(ref _value, Started, Queued) == Queued;

        public bool TryAbandon() => Interlocked.CompareExchange(ref _value, Abandoned, Queued) == Queued;
    }

    public static Task RunAsync(Action work, TimeSpan? timeout = null) =>
        RunAsync(() =>
        {
            work();
            return true;
        }, timeout);

    /// <summary>Called once per frame on the main thread.</summary>
    internal static void Drain()
    {
        // Bounded so a flood of work cannot stall a frame forever
        for (var i = 0; i < 64 && Queue.TryDequeue(out var work); i++)
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                _log?.LogError($"Main-thread work failed: {ex}");
            }
        }
    }
}

#if RAID_INTEROP
/// <summary>
/// Drains <see cref="MainThread"/>'s queue every frame. Injected into IL2CPP by BasePlugin.AddComponent.
/// </summary>
internal sealed class MainThreadPump : UnityInterop::UnityEngine.MonoBehaviour
{
    public MainThreadPump(IntPtr pointer) : base(pointer)
    {
    }

    // Called by Unity by name
    private void Update() => MainThread.Drain();
}
#endif
