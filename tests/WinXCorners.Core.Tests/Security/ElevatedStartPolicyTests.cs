using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Security;

public class ElevatedStartPolicyTests
{
    [Theory]
    [InlineData(false, SealStatus.NeverApproved, ElevatedStartAction.NotElevated)]
    [InlineData(false, SealStatus.Unchanged, ElevatedStartAction.NotElevated)]
    [InlineData(false, SealStatus.Changed, ElevatedStartAction.NotElevated)]
    [InlineData(false, SealStatus.Unreadable, ElevatedStartAction.NotElevated)]
    [InlineData(true, SealStatus.NeverApproved, ElevatedStartAction.AskFirstTimeConsent)]
    [InlineData(true, SealStatus.Unchanged, ElevatedStartAction.Proceed)]
    [InlineData(true, SealStatus.Changed, ElevatedStartAction.AskToApproveChanges)]
    [InlineData(true, SealStatus.Unreadable, ElevatedStartAction.AskToApproveChanges)]
    [InlineData(true, (SealStatus)77, ElevatedStartAction.AskToApproveChanges)]
    public void Decide_TruthTable(bool elevated, SealStatus status, ElevatedStartAction expected)
    {
        Assert.Equal(expected, ElevatedStartPolicy.Decide(elevated, new SealCheck(status, [])));
    }

    [Fact]
    public void Decide_RejectsNullSeal()
    {
        Assert.Throws<ArgumentNullException>(() => ElevatedStartPolicy.Decide(true, null!));
    }
}
