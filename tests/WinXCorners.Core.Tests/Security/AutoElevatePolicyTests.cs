using WinXCorners.Core.Model;
using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Security;

public class AutoElevatePolicyTests
{
    private static readonly SealStatus[] AllStatuses = [.. Enum.GetValues<SealStatus>(), (SealStatus)42];

    /// <summary>Every combination of the three switches with every seal status, including one no version defines.</summary>
    public static TheoryData<bool, bool, bool, SealStatus> AllInputs()
    {
        var data = new TheoryData<bool, bool, bool, SealStatus>();
        foreach (var always in new[] { false, true })
        {
            foreach (var elevated in new[] { false, true })
            {
                foreach (var noElevate in new[] { false, true })
                {
                    foreach (var status in AllStatuses)
                    {
                        data.Add(always, elevated, noElevate, status);
                    }
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllInputs))]
    public void Decide_covers_the_whole_truth_table(bool always, bool elevated, bool noElevate, SealStatus approval)
    {
        var expected = !always || elevated || noElevate
            ? AutoElevateAction.DoNotElevate
            : approval == SealStatus.Unchanged ? AutoElevateAction.Elevate : AutoElevateAction.AskBeforeElevating;

        Assert.Equal(expected, AutoElevatePolicy.Decide(always, elevated, noElevate, approval));
    }

    [Theory]
    [InlineData(SealStatus.NeverApproved)]
    [InlineData(SealStatus.Changed)]
    [InlineData(SealStatus.Unreadable)]
    public void A_flag_the_approval_does_not_match_never_elevates_without_asking(SealStatus approval)
    {
        // The tampering cases: settings.json was edited to switch the flag on. With no approval, an approval of other
        // settings, or one that cannot be read, the user must be asked before any UAC prompt appears.
        var action = AutoElevatePolicy.Decide(
            alwaysRunAsAdministrator: true, isElevated: false, noElevateRequested: false, approval);

        Assert.Equal(AutoElevateAction.AskBeforeElevating, action);
    }

    [Fact]
    public void An_approval_given_with_the_flag_off_does_not_cover_a_file_that_turns_it_on()
    {
        // Review V2 D3: the user once approved a one-off "Restart as administrator" (flag off). A normal-rights
        // program then switches the flag on in settings.json. Because the seal covers the flag, the approval no
        // longer matches, and the next start explains the risk before Windows shows its prompt.
        var approvedOnce = new ApplicationSettings { AlwaysRunAsAdministrator = false };
        var stored = SettingsSeal.CreateApproval(approvedOnce);
        var tampered = new ApplicationSettings { AlwaysRunAsAdministrator = true };

        var seal = SettingsSeal.Check(tampered, stored);

        Assert.Equal(SealStatus.Changed, seal.Status);
        Assert.Contains("Always run as administrator: off -> on", seal.Differences);
        Assert.Equal(AutoElevateAction.AskBeforeElevating, AutoElevatePolicy.Decide(true, false, false, seal.Status));
    }

    [Fact]
    public void An_approval_given_with_the_flag_on_lets_the_next_start_elevate()
    {
        var settings = new ApplicationSettings { AlwaysRunAsAdministrator = true };
        var seal = SettingsSeal.Check(settings, SettingsSeal.CreateApproval(settings));

        Assert.Equal(AutoElevateAction.Elevate, AutoElevatePolicy.Decide(true, false, false, seal.Status));
    }
}
