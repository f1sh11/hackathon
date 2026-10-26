namespace SydneyBuddy.Api;

/// <summary>可向用户展示的业务错误（HTTP 422）。 / A safe domain failure exposed as HTTP 422.</summary>
public sealed class ServiceException(string message) : Exception(message);

/// <summary>行程被其他请求修改（HTTP 409）。 / An optimistic concurrency conflict (HTTP 409).</summary>
public sealed class RevisionConflictException() : Exception("The trip revision has changed. Reload it before confirming.");
