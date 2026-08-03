using Hsm.Application.Abstractions;
using Hsm.Application.Abstractions.Behaviors;

namespace Hsm.Tests.Abstractions;

public class ValidationBehaviorTests
{
    private sealed record Create(string Name) : ICommand<string>;

    private sealed class CreateValidator : IValidator<Create>
    {
        public IEnumerable<ValidationFailure> Validate(Create request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                yield return new ValidationFailure("name", "isNotEmpty", "name should not be empty");
            }
        }
    }

    [Fact]
    public async Task Valid_request_reaches_the_handler()
    {
        var behavior = new ValidationBehavior<Create, string>([new CreateValidator()]);

        var result = await behavior.HandleAsync(
            new Create("ok"), () => Task.FromResult("done"), CancellationToken.None);

        Assert.Equal("done", result);
    }

    [Fact]
    public async Task Invalid_request_throws_before_the_handler_with_field_detail()
    {
        var behavior = new ValidationBehavior<Create, string>([new CreateValidator()]);
        var reached = false;

        var ex = await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => behavior.HandleAsync(
            new Create("  "),
            () => { reached = true; return Task.FromResult("done"); },
            CancellationToken.None));

        Assert.False(reached);
        Assert.Contains("name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_validator_registered_is_not_an_error()
    {
        var behavior = new ValidationBehavior<Create, string>([]);

        var result = await behavior.HandleAsync(
            new Create(""), () => Task.FromResult("done"), CancellationToken.None);

        Assert.Equal("done", result);
    }
}
