namespace Roam.Api.Infrastructure.Errors;

public sealed class ConflictException(string message) : Exception(message);
public sealed class MissingException(string message) : Exception(message);
