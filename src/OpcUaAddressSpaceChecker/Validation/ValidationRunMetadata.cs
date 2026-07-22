using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.Validation;

public enum ViewCompletenessRequest
{
    Auto,
    Complete,
    Restricted
}

public enum ValidationViewState
{
    Complete,
    AssumedComplete,
    Restricted
}

public sealed record ValidationRunMetadata(
    AuthenticationMode AuthenticationMode,
    ViewCompletenessRequest RequestedViewCompleteness,
    ValidationViewState EffectiveViewState,
    string ViewStateBasis,
    int AccessDeniedCount)
{
    public static ValidationRunMetadata Default { get; } = new(
        AuthenticationMode.Anonymous,
        ViewCompletenessRequest.Complete,
        ValidationViewState.Complete,
        "No restricted-view evidence was supplied.",
        0);

    public bool HasCompleteView =>
        EffectiveViewState is ValidationViewState.Complete or ValidationViewState.AssumedComplete;

    public FindingConfidence AbsenceConfidence =>
        HasCompleteView ? FindingConfidence.Confirmed : FindingConfidence.Inconclusive;
}

public static class ValidationViewPolicy
{
    public static bool TryParse(string? value, out ViewCompletenessRequest request)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "auto":
                request = ViewCompletenessRequest.Auto;
                return true;
            case "complete":
                request = ViewCompletenessRequest.Complete;
                return true;
            case "restricted":
                request = ViewCompletenessRequest.Restricted;
                return true;
            default:
                request = default;
                return false;
        }
    }

    public static ValidationRunMetadata Evaluate(
        AuthenticationMode authenticationMode,
        ViewCompletenessRequest request,
        int accessDeniedCount)
    {
        if (accessDeniedCount > 0)
        {
            return new ValidationRunMetadata(
                authenticationMode,
                request,
                ValidationViewState.Restricted,
                $"The server returned BadUserAccessDenied for {accessDeniedCount} browsed node(s).",
                accessDeniedCount);
        }

        return request switch
        {
            ViewCompletenessRequest.Complete => new(
                authenticationMode,
                request,
                ValidationViewState.Complete,
                "The operator explicitly asserted a complete validation view.",
                accessDeniedCount),
            ViewCompletenessRequest.Restricted => new(
                authenticationMode,
                request,
                ValidationViewState.Restricted,
                "The operator explicitly marked the validation view as restricted.",
                accessDeniedCount),
            _ when authenticationMode == AuthenticationMode.Anonymous => new(
                authenticationMode,
                request,
                ValidationViewState.Restricted,
                "Anonymous Browse may omit nodes or references that are visible to authenticated users.",
                accessDeniedCount),
            _ => new(
                authenticationMode,
                request,
                ValidationViewState.AssumedComplete,
                "Authenticated Browse is treated as complete by policy; OPC UA does not prove completeness.",
                accessDeniedCount)
        };
    }
}
