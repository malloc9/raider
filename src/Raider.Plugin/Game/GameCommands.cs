using BepInEx.Logging;
using static Raider.Plugin.Game.Il2CppReflection;

namespace Raider.Plugin.Game;

/// <summary>
/// Issues Raid's own server commands (the game's <c>*Cmd</c> types), the same ones its UI issues.
/// <para>
/// A command is created and started on the main thread (<c>new XCmd(args).IfOk(..).IfError(..).Execute()</c>);
/// the game sends it to its server and calls back on the main thread when the server answers.
/// </para>
/// </summary>
internal static class GameCommands
{
    /// <summary>The longest one command waits for the game server's answer.</summary>
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Runs one command and waits for the server's answer, all within <paramref name="timeLimit"/>.
    /// </summary>
    /// <param name="commandTypeName">Full name of the game's command type.</param>
    /// <param name="args">Constructor arguments.</param>
    /// <param name="timeLimit">
    /// The most this call may take, waiting for the main thread and for the answer together. A command that was
    /// started but not answered in time is reported as unconfirmed, not failed: the game may still apply it.
    /// </param>
    /// <param name="precheck">
    /// Runs on the main thread right before the command is created; returning a reason skips the command.
    /// Used to re-check conditions (such as a zero price) against the game's state at that instant.
    /// </param>
    public static async Task<CommandResult> RunAsync(
        string commandTypeName,
        object?[] args,
        TimeSpan timeLimit,
        Func<string?>? precheck = null)
    {
        var log = RaiderPlugin.Log;
        var shortName = commandTypeName[(commandTypeName.LastIndexOf('.') + 1)..];
        var description = $"{shortName}({string.Join(", ", args)})";
        var answer = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = DateTime.UtcNow;

        CommandResult? notStarted;
        try
        {
            notStarted = await MainThread.RunAsync(() =>
            {
                var skipReason = precheck?.Invoke();
                if (skipReason != null)
                {
                    return CommandResult.Skipped(skipReason);
                }

                var type = GameAccess.FindGameType(commandTypeName)
                    ?? throw new GameMemberMissingException($"command type {commandTypeName}");
                var command = New(type, args);
                Call(command, "IfOk", ToIl2CppAction(() => answer.TrySetResult(CommandResult.Ok())));
                Call(command, "IfError", ToIl2CppAction(() =>
                    answer.TrySetResult(CommandResult.Failed($"the game server rejected {shortName}"))));
                Call(command, "IfCancelled", ToIl2CppAction(() =>
                    answer.TrySetResult(CommandResult.Failed($"the game cancelled {shortName}"))));
                Call(command, "Execute");
                return null;
            }, Min(MainThread.DefaultTimeout, timeLimit));
        }
        catch (Exception ex)
        {
            // The game's own validation throws here, e.g. "prize already received". A main-thread timeout lands here
            // too; MainThread guarantees such work never runs, so "did not start" is true.
            log.LogWarning($"{description} did not start: {ex.Message}");
            return CommandResult.Failed($"{shortName} did not start: {ex.Message}");
        }

        if (notStarted != null)
        {
            return notStarted;
        }

        var answerWait = Min(AnswerTimeout, timeLimit - (DateTime.UtcNow - started));
        var finished = await Task.WhenAny(answer.Task, Task.Delay(answerWait > TimeSpan.Zero ? answerWait : TimeSpan.Zero));
        if (finished != answer.Task)
        {
            log.LogWarning($"{description} was sent but got no answer in {answerWait.TotalSeconds:0} s; it may still go through");
            // Log the answer if it comes later, so the plugin log says what really happened
            _ = answer.Task.ContinueWith(late => log.LogInfo(
                $"{description} answered late: {(late.Result.Succeeded ? "ok" : late.Result.Error)}"),
                TaskContinuationOptions.OnlyOnRanToCompletion);
            return CommandResult.Unconfirmed(
                $"{shortName} was sent, but the game server did not answer in time; it may still have gone through");
        }

        var result = await answer.Task;
        log.Log(result.Succeeded ? LogLevel.Info : LogLevel.Warning,
            $"{description}: {(result.Succeeded ? "ok" : result.Error)}");
        return result;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}

/// <summary>What happened to one game command.</summary>
internal enum CommandStatus
{
    /// <summary>The game server accepted it.</summary>
    Ok,

    /// <summary>It did not run, or the game server refused it.</summary>
    Failed,

    /// <summary>Its precheck said not to run it.</summary>
    Skipped,

    /// <summary>It was sent, but no answer came in time: the game may or may not have applied it.</summary>
    Unconfirmed
}

/// <summary>What happened to one game command, and why when it was not ok.</summary>
internal sealed record CommandResult(CommandStatus Status, string? Error)
{
    public bool Succeeded => Status == CommandStatus.Ok;

    public static CommandResult Ok() => new(CommandStatus.Ok, null);
    public static CommandResult Failed(string error) => new(CommandStatus.Failed, error);
    public static CommandResult Skipped(string reason) => new(CommandStatus.Skipped, reason);
    public static CommandResult Unconfirmed(string reason) => new(CommandStatus.Unconfirmed, reason);
}
