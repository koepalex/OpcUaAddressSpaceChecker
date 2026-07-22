using OpcUaAddressSpaceChecker.OpcUa;
using OpcUaAddressSpaceChecker.Validation;

namespace OpcUaAddressSpaceChecker.Tests.Validation;

public sealed class ValidationViewPolicyTests
{
    [Fact]
    public void Auto_policy_marks_anonymous_restricted_and_authenticated_assumed_complete()
    {
        var anonymous = ValidationViewPolicy.Evaluate(
            AuthenticationMode.Anonymous,
            ViewCompletenessRequest.Auto,
            accessDeniedCount: 0);
        var authenticated = ValidationViewPolicy.Evaluate(
            AuthenticationMode.UserName,
            ViewCompletenessRequest.Auto,
            accessDeniedCount: 0);

        Assert.Equal(ValidationViewState.Restricted, anonymous.EffectiveViewState);
        Assert.Equal(FindingConfidence.Inconclusive, anonymous.AbsenceConfidence);
        Assert.Equal(ValidationViewState.AssumedComplete, authenticated.EffectiveViewState);
        Assert.Equal(FindingConfidence.Confirmed, authenticated.AbsenceConfidence);
    }

    [Fact]
    public void Auto_policy_downgrades_authenticated_session_when_access_is_denied()
    {
        var metadata = ValidationViewPolicy.Evaluate(
            AuthenticationMode.Certificate,
            ViewCompletenessRequest.Auto,
            accessDeniedCount: 2);

        Assert.Equal(ValidationViewState.Restricted, metadata.EffectiveViewState);
        Assert.Equal(2, metadata.AccessDeniedCount);
        Assert.Equal(FindingConfidence.Inconclusive, metadata.AbsenceConfidence);
    }

    [Fact]
    public void Observed_browse_denial_overrides_explicit_complete_request()
    {
        var metadata = ValidationViewPolicy.Evaluate(
            AuthenticationMode.UserName,
            ViewCompletenessRequest.Complete,
            accessDeniedCount: 1);

        Assert.Equal(ValidationViewState.Restricted, metadata.EffectiveViewState);
        Assert.False(metadata.HasCompleteView);
    }

    [Theory]
    [InlineData("auto", ViewCompletenessRequest.Auto)]
    [InlineData("Complete", ViewCompletenessRequest.Complete)]
    [InlineData("restricted", ViewCompletenessRequest.Restricted)]
    public void TryParse_accepts_supported_values(string value, ViewCompletenessRequest expected)
    {
        Assert.True(ValidationViewPolicy.TryParse(value, out var actual));
        Assert.Equal(expected, actual);
    }
}
