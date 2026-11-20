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
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Runs one command and waits for the server's answer.
    /// </summary>
    /// <param name="commandTypeName">Full name of the game's command type.</param>
    /// <param name="args">Constructor arguments.</param>
    /// <param name="precheck">
    /// Runs on the main thread right before the command is created; returning a reason skips the command.
    /// Used to re-check conditions (such as a zero price) against the game's state at that instant.
    /// </param>
    public static async Task<CommandResult> RunAsync(
        string commandTypeName,
        object?[] args,
        Func<string?>? precheck = null,
        TimeSpan? timeout = null)
    {
        var log = RaiderPlugin.Log;
        var shortName = commandTypeName[(commandTypeName.LastIndexOf('.') + 1)..];
        var answer = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);

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
            });
        }
        catch (Exception ex)
        {
            // The game's own validation throws here, e.g. "prize already received"
            log.LogWarning($"{shortName}({string.Join(", ", args)}) did not start: {ex.Message}");
            return CommandResult.Failed($"{shortName} did not start: {ex.Message}");
        }

        if (notStarted != null)
        {
            return notStarted;
        }

        var finished = await Task.WhenAny(answer.Task, Task.Delay(timeout ?? DefaultTimeout));
        if (finished != answer.Task)
        {
            log.LogWarning($"{shortName}({string.Join(", ", args)}) got no answer in time");
            return CommandResult.Failed($"{shortName} got no answer from the game server in time");
        }

        var result = await answer.Task;
        log.Log(result.Succeeded ? LogLevel.Info : LogLevel.Warning,
            $"{shortName}({string.Join(", ", args)}): {(result.Succeeded ? "ok" : result.Error)}");
        return result;
    }
}

/// <summary>What happened to one game command.</summary>
internal sealed record CommandResult(bool Succeeded, bool WasSkipped, string? Error)
{
    public static CommandResult Ok() => new(true, false, null);
    public static CommandResult Failed(string error) => new(false, false, error);
    public static CommandResult Skipped(string reason) => new(false, true, reason);
}
