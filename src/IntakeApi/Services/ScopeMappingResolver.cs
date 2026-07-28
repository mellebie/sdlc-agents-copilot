using IntakeApi.Contracts;

namespace IntakeApi.Services;

public interface IScopeMappingResolver
{
    ScopeMappingResolution Resolve(SourceLdc sourceLdc, SourceApplication sourceApplication, string coolTextAccountId);
}

public sealed class ScopeMappingResolver : IScopeMappingResolver
{
    private const string MappingVersion = "2026-07-28.1";

    private static readonly HashSet<ScopeMappingKey> AllowedMappings =
    [
        new(SourceLdc.Vng, SourceApplication.BizTalk, "acct-001"),
        new(SourceLdc.Cgc, SourceApplication.Gcma, "acct-002"),
        new(SourceLdc.Nicor, SourceApplication.Kmi, "acct-003"),
        new(SourceLdc.Agl, SourceApplication.Arm, "acct-004")
    ];

    public ScopeMappingResolution Resolve(SourceLdc sourceLdc, SourceApplication sourceApplication, string coolTextAccountId)
    {
        var normalizedAccount = coolTextAccountId.Trim();
        if (AllowedMappings.Contains(new ScopeMappingKey(sourceLdc, sourceApplication, normalizedAccount)))
        {
            return new ScopeMappingResolution(true, "ROUTEABLE", MappingVersion);
        }

        return new ScopeMappingResolution(false, "REJECTED_OUT_OF_SCOPE", MappingVersion);
    }

    private readonly record struct ScopeMappingKey(SourceLdc SourceLdc, SourceApplication SourceApplication, string AccountId);
}

public readonly record struct ScopeMappingResolution(bool IsRouteable, string ReasonCode, string MappingVersion);
