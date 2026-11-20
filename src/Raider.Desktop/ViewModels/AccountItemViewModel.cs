namespace Raider.Desktop.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Raider.Core.Models;

/// <summary>
/// One Account in the Accounts list, with its own Routine shown as Chore toggles.
/// </summary>
public sealed partial class AccountItemViewModel : ObservableObject
{
    private readonly Action _routineChanged;

    public AccountItemViewModel(Account account, Action routineChanged)
    {
        _routineChanged = routineChanged;
        Id = account.Id;
        PlariumLogin = account.PlariumLogin;
        DisplayName = account.DisplayName;

        // Show every Chore; the ones in the Account's Routine are ticked, in Routine order first
        var enabled = account.Routine.Chores;
        var others = Routine.Default().Chores.Where(c => enabled.All(e => e.Type != c.Type));
        Chores = new ObservableCollection<ChoreToggleViewModel>(
            enabled.Select(c => new ChoreToggleViewModel(c, true, OnChoreToggled))
                .Concat(others.Select(c => new ChoreToggleViewModel(c, false, OnChoreToggled))));

        _status = "Idle";
    }

    public string Id { get; }

    public string PlariumLogin { get; }

    public string DisplayName { get; }

    public ObservableCollection<ChoreToggleViewModel> Chores { get; }

    [ObservableProperty]
    private string _status;

    public Account ToAccount() => new(
        Id,
        PlariumLogin,
        DisplayName,
        new Routine(Chores.Where(c => c.IsEnabled).Select(c => c.Chore).ToList()));

    private void OnChoreToggled() => _routineChanged();
}

/// <summary>
/// A Chore that can be switched on or off in an Account's Routine.
/// </summary>
public sealed partial class ChoreToggleViewModel : ObservableObject
{
    private readonly Action _changed;

    public ChoreToggleViewModel(Chore chore, bool isEnabled, Action changed)
    {
        Chore = chore;
        _isEnabled = isEnabled;
        _changed = changed;
    }

    public Chore Chore { get; }

    public string Name => Chore.Name;

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value) => _changed();
}
