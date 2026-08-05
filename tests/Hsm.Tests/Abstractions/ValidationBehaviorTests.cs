using FluentValidation;
using Hsm.Application.Abstractions;
using Hsm.Application.Abstractions.Behaviors;

namespace Hsm.Tests.Abstractions;

public class ValidationBehaviorTests
{
    private sealed record Create(string Name, int Page) : ICommand<string>;

    private sealed class CreateValidator : AbstractValidator<Create>
    {
        public CreateValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(x => x.Page).ValidPage();
        }
    }

    [Fact]
    public async Task Valid_request_reaches_the_handler()
    {
        var behavior = new ValidationBehavior<Create, string>([new CreateValidator()]);

        var result = await behavior.HandleAsync(
            new Create("ok", 1), () => Task.FromResult("done"), CancellationToken.None);

        Assert.Equal("done", result);
    }

    [Fact]
    public async Task Invalid_request_throws_before_the_handler_naming_every_bad_field()
    {
        var behavior = new ValidationBehavior<Create, string>([new CreateValidator()]);
        var reached = false;

        var exception = await Assert.ThrowsAsync<ValidationException>(() => behavior.HandleAsync(
            new Create("  ", 0),
            () => { reached = true; return Task.FromResult("done"); },
            CancellationToken.None));

        Assert.False(reached);
        Assert.Contains(exception.Errors, e => e.PropertyName == "Name");
        Assert.Contains(exception.Errors, e => e.PropertyName == "Page");
    }

    [Fact]
    public async Task Every_registered_validator_runs_and_failures_are_merged()
    {
        var behavior = new ValidationBehavior<Create, string>(
            [new CreateValidator(), new CreateValidator()]);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => behavior.HandleAsync(
            new Create(string.Empty, 1), () => Task.FromResult("done"), CancellationToken.None));

        Assert.Equal(2, exception.Errors.Count(e => e.PropertyName == "Name"));
    }

    [Fact]
    public async Task No_validator_registered_is_not_an_error()
    {
        var behavior = new ValidationBehavior<Create, string>([]);

        var result = await behavior.HandleAsync(
            new Create(string.Empty, 0), () => Task.FromResult("done"), CancellationToken.None);

        Assert.Equal("done", result);
    }
}
