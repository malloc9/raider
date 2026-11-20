namespace Raider.Tests.Fakes;

using Raider.Core.Models;
using Raider.Core.Services;

/// <summary>
/// Fake implementation of IAccountStore for testing.
/// </summary>
public sealed class FakeAccountStore : IAccountStore
{
    private readonly List<Account> _accounts = new();

    public IReadOnlyList<Account> Accounts => _accounts.AsReadOnly();

    public Task<IReadOnlyList<Account>> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_accounts.AsReadOnly());

    public Task SaveAsync(IReadOnlyList<Account> accounts, CancellationToken cancellationToken = default)
    {
        _accounts.Clear();
        _accounts.AddRange(accounts);
        return Task.CompletedTask;
    }

    public void AddAccount(Account account) => _accounts.Add(account);
    public void RemoveAccount(string accountId) => _accounts.RemoveAll(a => a.Id == accountId);
    public void Clear() => _accounts.Clear();
}