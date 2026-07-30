using Hsm.Application.Abstractions;
using Hsm.Application.Abstractions.Behaviors;

namespace Hsm.Tests.Abstractions;

public class TransactionBehaviorTests
{
    private sealed record Write : ICommand<string>;
    private sealed record Read : IQuery<string>;

    private sealed class SpyUnitOfWork : IUnitOfWork
    {
        public int Opened { get; private set; }

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> work, CancellationToken ct)
        {
            Opened++;
            return work(ct);
        }
    }

    [Fact]
    public async Task Command_opens_a_transaction()
    {
        var uow = new SpyUnitOfWork();
        var behavior = new TransactionBehavior<Write, string>(uow);

        await behavior.HandleAsync(new Write(), () => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.Equal(1, uow.Opened);
    }

    [Fact]
    public async Task Query_does_not_open_a_transaction()
    {
        var uow = new SpyUnitOfWork();
        var behavior = new TransactionBehavior<Read, string>(uow);

        await behavior.HandleAsync(new Read(), () => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.Equal(0, uow.Opened);
    }
}
