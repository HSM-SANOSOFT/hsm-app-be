using Hsm.Application.Abstractions;
using Hsm.Application.Identity;

namespace Hsm.Application.Templates.Commands.DeleteTemplate;

public sealed class DeleteTemplateHandler(ITemplateStore store, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteTemplateCommand, Unit>
{
    public async Task<Unit> HandleAsync(DeleteTemplateCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var target = await store.FindByIdAsync(request.Id, withChildren: true, withBase: false, ct)
            ?? throw TemplateErrors.NotFound(request.Id.ToString());

        if (await store.CountReferencingBaseAsync(request.Id, ct) > 0)
        {
            throw TemplateErrors.InUse(request.Id);
        }

        await store.RemoveAsync(target, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
