namespace Raider.Tests.Unit;

public sealed class JsonAccountStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "raider-tests", Guid.NewGuid().ToString());
    private readonly string _filePath;
    private readonly JsonAccountStore _store;

    public JsonAccountStoreTests()
    {
        _filePath = Path.Combine(_directory, "raider-settings.json");
        _store = new JsonAccountStore(_filePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Load_WithNoFile_ReturnsNoAccounts()
    {
        var accounts = await _store.LoadAsync();

        accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAccountsWithTheirOwnRoutines()
    {
        var alice = TestDataBuilder.CreateAccount("alice", "alice@example.com", "Alice", Routine.Default());
        var bob = TestDataBuilder.CreateAccount("bob", "bob@example.com", "Bob",
            TestDataBuilder.CreateRoutine(TestDataBuilder.CreateChore("gem", "Collect Gem Mine", ChoreType.CollectGemMine)));

        await _store.SaveAsync(new[] { alice, bob });
        var loaded = await new JsonAccountStore(_filePath).LoadAsync();

        loaded.Should().HaveCount(2);
        loaded[0].Should().BeEquivalentTo(alice);
        loaded[1].Should().BeEquivalentTo(bob);
    }

    [Fact]
    public async Task Save_AfterRemovingAnAccount_PersistsTheRemoval()
    {
        var alice = TestDataBuilder.CreateAccount("alice", "alice@example.com", "Alice");
        var bob = TestDataBuilder.CreateAccount("bob", "bob@example.com", "Bob");
        await _store.SaveAsync(new[] { alice, bob });

        await _store.SaveAsync(new[] { bob });

        (await _store.LoadAsync()).Select(a => a.Id).Should().Equal("bob");
    }

    [Fact]
    public async Task Save_NeverWritesAPassword()
    {
        await _store.SaveAsync(new[] { TestDataBuilder.CreateAccount(routine: Routine.Default()) });

        var json = await File.ReadAllTextAsync(_filePath);
        json.Should().NotContainEquivalentOf("password");
    }

    [Fact]
    public void Account_HasNoPasswordField()
    {
        typeof(Account).GetProperties().Select(p => p.Name)
            .Should().NotContain(name => name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }
}
