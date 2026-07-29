namespace Hsm.Application.Abstractions;

/// <summary>A dispatchable request. Use ICommand or IQuery, never this directly.</summary>
public interface IRequest<out TResult>;

/// <summary>Mutates state. Runs inside a transaction.</summary>
public interface ICommand<out TResult> : IRequest<TResult>;

/// <summary>A command with no meaningful result.</summary>
public interface ICommand : ICommand<Unit>;

/// <summary>Reads state. Never opens a transaction.</summary>
public interface IQuery<out TResult> : IRequest<TResult>;

/// <summary>The absence of a result — commands that return nothing.</summary>
public readonly record struct Unit
{
    public static Unit Value => default;
}
