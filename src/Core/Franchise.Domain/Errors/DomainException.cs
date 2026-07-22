namespace Franchise.Domain.Errors;

public enum DomainErrorCode
{
    Validation,
    NotFound,
    Forbidden,
    Conflict,
}

/// <summary>Typed business error; mapped to HTTP status codes in the API layer only.</summary>
public sealed class DomainException(DomainErrorCode code, string message) : Exception(message)
{
    public DomainErrorCode Code { get; } = code;

    public static DomainException Validation(string message) => new(DomainErrorCode.Validation, message);

    public static DomainException NotFound(string message) => new(DomainErrorCode.NotFound, message);

    public static DomainException Forbidden(string message) => new(DomainErrorCode.Forbidden, message);

    public static DomainException Conflict(string message) => new(DomainErrorCode.Conflict, message);
}
