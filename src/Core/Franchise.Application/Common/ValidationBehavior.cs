using FluentValidation;
using Franchise.Domain.Errors;
using MediatR;

namespace Franchise.Application.Common;

/// <summary>Runs FluentValidation validators before each handler.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, ct);
            if (!result.IsValid)
            {
                throw DomainException.Validation(string.Join(" ", result.Errors.Select(e => e.ErrorMessage)));
            }
        }

        return await next();
    }
}
