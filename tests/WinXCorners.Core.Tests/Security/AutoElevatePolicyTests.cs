using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Security;

public class AutoElevatePolicyTests
{
    [Theory]
    // always, elevated, noElevate, approval -> expected
    [InlineData(false, false, false, false, AutoElevateAction.DoNotElevate)]
    [InlineData(false, false, false, true, AutoElevateAction.DoNotElevate)]
    [InlineData(false, false, true, false, AutoElevateAction.DoNotElevate)]
    [InlineData(false, false, true, true, AutoElevateAction.DoNotElevate)]
    [InlineData(false, true, false, false, AutoElevateAction.DoNotElevate)]
    [InlineData(false, true, false, true, AutoElevateAction.DoNotElevate)]
    [InlineData(false, true, true, false, AutoElevateAction.DoNotElevate)]
    [InlineData(false, true, true, true, AutoElevateAction.DoNotElevate)]
    [InlineData(true, false, false, false, AutoElevateAction.AskBeforeElevating)]
    [InlineData(true, false, false, true, AutoElevateAction.Elevate)]
    [InlineData(true, false, true, false, AutoElevateAction.DoNotElevate)]
    [InlineData(true, false, true, true, AutoElevateAction.DoNotElevate)]
    [InlineData(true, true, false, false, AutoElevateAction.DoNotElevate)]
    [InlineData(true, true, false, true, AutoElevateAction.DoNotElevate)]
    [InlineData(true, true, true, false, AutoElevateAction.DoNotElevate)]
    [InlineData(true, true, true, true, AutoElevateAction.DoNotElevate)]
    public void Decide_covers_the_whole_truth_table(
        bool always, bool elevated, bool noElevate, bool approval, AutoElevateAction expected)
    {
        Assert.Equal(expected, AutoElevatePolicy.Decide(always, elevated, noElevate, approval));
    }

    [Fact]
    public void A_flag_nobody_ever_confirmed_as_administrator_never_elevates_silently()
    {
        // The tampering case: settings.json was edited to switch the flag on, but no administrator
        // session has ever been approved, so the user must be asked before any UAC prompt appears.
        var action = AutoElevatePolicy.Decide(
            alwaysRunAsAdministrator: true, isElevated: false, noElevateRequested: false, approvalOnRecord: false);

        Assert.NotEqual(AutoElevateAction.Elevate, action);
    }
}
