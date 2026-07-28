namespace IntakeApi.Services;

/// <summary>
/// Generates correlation identifiers for intake requests.
/// </summary>
public interface ICorrelationIdGenerator
{
    /// <summary>
    /// Generates a new correlation identifier.
    /// </summary>
    /// <returns>A new correlation identifier.</returns>
    string Generate();
}
