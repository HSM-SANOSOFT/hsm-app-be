using Hsm.Application.Errors;

namespace Hsm.Application.Coms;

/// <summary>Shared 404 factories for the coms read surface.</summary>
public static class ComsErrors
{
    public static NotFoundException BatchNotFound(Guid id) => new("EmailBatch", id);

    public static NotFoundException RecipientNotFound(Guid id) => new("EmailRecipient", id);
}
