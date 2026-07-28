using ConsentService.Models;

namespace ConsentService.Security;

public interface IReOptInAuthorizationPolicy
{
    bool IsAuthorized(ReOptInTransitionRequest request);
}

public sealed class ReOptInAuthorizationPolicy : IReOptInAuthorizationPolicy
{
    public bool IsAuthorized(ReOptInTransitionRequest request)
    {
        if (request.InitiationChannel is not (ReOptInChannel.Form or ReOptInChannel.SmsResponse))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.AuthorizationProof))
        {
            return false;
        }

        return true;
    }
}
