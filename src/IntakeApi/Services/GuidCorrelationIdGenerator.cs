namespace IntakeApi.Services;

/// <summary>
/// Generates GUID-based correlation identifiers.
/// </summary>
public sealed class GuidCorrelationIdGenerator : ICorrelationIdGenerator
{
    /// <inheritdoc />
    public string Generate() => Guid.NewGuid().ToString("N");
}
