using Hsm.Api.Errors;
using Microsoft.AspNetCore.Http;

namespace Hsm.Api.Tests.Errors;

/// <summary>
/// Unit-level proof that an exception type outside <see cref="HsmExceptionHandler"/>'s
/// closed set (<c>ValidationException</c>, <c>UnauthorizedException</c>,
/// <c>ForbiddenException</c>, <c>NotFoundException</c>, <c>ConflictException</c>,
/// <c>TooManyRequestsException</c>, <c>BadHttpRequestException</c>) renders as a
/// bare 500 with no caller-facing detail.
///
/// <para>Calls <see cref="HsmExceptionHandler.Map"/> directly instead of round-
/// tripping through a borrowed HTTP route: <c>ProblemDetailsTests</c> used to
/// exercise this via <c>DELETE /v1/templates/not-a-guid</c>, the last route
/// that still bare-<c>Guid.Parse</c>'d a route parameter — Task 8's reshape of
/// Templates (<c>{id:guid}</c> on PUT/DELETE) closed it, leaving no HTTP
/// vehicle for an unmapped exception anywhere in the currently-mapped
/// <c>/api</c>/<c>/v1</c> routes. A direct unit test is more robust than
/// hunting for another borrowed route each time a module gets reshaped, and
/// <c>Map</c> is exactly the unit that owns this guarantee — <c>Hsm.Api.csproj</c>
/// grants <c>Hsm.Api.Tests</c> <c>InternalsVisibleTo</c> so it can be called
/// directly.</para>
/// </summary>
public class HsmExceptionHandlerMapTests
{
    private sealed class UnmappedException()
        : Exception("some internal detail — a connection string, a row's contents — that must never leak");

    [Fact]
    public void An_exception_outside_the_closed_set_maps_to_a_bare_500_with_no_detail()
    {
        var problem = HsmExceptionHandler.Map(new UnmappedException());

        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("Internal Server Error", problem.Title);
        Assert.Null(problem.Detail);
    }
}
