namespace Hsm.Application.Abstractions;

public interface IDispatcher
{
    Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken ct = default);
}
