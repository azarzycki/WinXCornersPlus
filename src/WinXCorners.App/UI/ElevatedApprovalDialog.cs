using WinXCorners.Core.Model;
using WinXCorners.Core.Security;

namespace WinXCorners.App;

/// <summary>
/// Shown by an elevated instance before it does anything, when the settings seal reports a first run or a
/// change (brief design point 2). It lists exactly what the administrator session will run, in Core's words,
/// and continues only on an explicit click of "Approve and continue".
/// </summary>
internal static class ElevatedApprovalDialog
{
    internal const string FirstRunHeading = "Approve what the administrator session will run";
    internal const string ChangedHeading = "Settings changed since you last approved them";
    internal const string WhyAskedText =
        "These settings are stored in a file that any program running under your account can change, " +
        "which is why you are being asked.";

    // The two lists are as tall as their text: only the dialog body scrolls (inside 70% of the screen), so there is
    // never a scroll bar inside a scroll bar and nothing the user approves is hidden in a half-visible box.
    private const int ListMaxHeight = int.MaxValue;

    /// <summary>True only when the user clicked "Approve and continue".</summary>
    internal static bool Ask(IWin32Window? owner, ApplicationSettings settings, SealCheck seal, IReadOnlyList<RiskItem> risks)
    {
        using var dialog = Create(settings, seal, risks);
        return ConsentForm.ShowModal(dialog, owner) == DialogResult.OK;
    }

    /// <summary>Builds the dialog without showing it (for <see cref="Ask"/> and for tests).</summary>
    internal static Form Create(ApplicationSettings settings, SealCheck seal, IReadOnlyList<RiskItem> risks)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(seal);
        ArgumentNullException.ThrowIfNull(risks);

        // Anything other than "never approved" (changed, unreadable, or an unexpected status) is presented as a
        // change: that is the more cautious reading.
        var firstRun = seal.Status == SealStatus.NeverApproved;
        var heading = firstRun ? FirstRunHeading : ChangedHeading;
        var summary = SettingsSeal.Summarize(settings);

        // Shown before any other window exists, so it needs a taskbar button to be found.
        return new ConsentForm("WinXCornersPlus administrator approval", heading, "Approve and continue",
            "Don't run as administrator", form => CreateBody(form, firstRun ? [] : seal.Differences, summary, risks),
            showInTaskbar: true);
    }

    private static Control CreateBody(ConsentForm form, IReadOnlyList<string> differences, IReadOnlyList<string> summary,
        IReadOnlyList<RiskItem> risks)
    {
        var px = form.Px;
        var width = form.ContentPixelWidth;
        var scrollBar = DialogLayout.ScrollBarWidth(form);
        var body = DialogLayout.CreateColumn("approvalBody");

        // The lines come from Core, which has already escaped and length-limited everything taken from
        // settings.json; they are shown in full, never truncated further.
        if (differences.Count > 0)
        {
            AddSectionTitle(body, form, "What changed", px);
            var changed = DialogLayout.CreateReadOnlyList(differences, form.Font, width, ListMaxHeight, scrollBar,
                "whatChangedBox", px);
            DialogLayout.AddRow(body, changed, new Padding(0, 0, 0, px(14)));
        }

        AddSectionTitle(body, form, "What will run", px);
        var monospace = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
        form.Resources.Add(monospace);
        var willRun = DialogLayout.CreateReadOnlyList(summary, monospace, width, ListMaxHeight, scrollBar,
            "whatWillRunBox", px);
        DialogLayout.AddRow(body, willRun, new Padding(0, 0, 0, px(14)));

        if (risks.Count > 0)
        {
            var rows = RiskDialog.CreateRows(risks, px, width, form.BoldFont, form.Resources);
            DialogLayout.AddRow(body, rows, new Padding(0, 0, 0, px(2)));
        }

        var why = DialogLayout.CreateWrappingLabel(WhyAskedText, width, "whyAskedLabel");
        DialogLayout.AddRow(body, why, Padding.Empty);

        return DialogLayout.CreateScroller(body, width, DialogLayout.WorkingAreaHeight(0.7), scrollBar, "approvalScroller");
    }

    private static void AddSectionTitle(TableLayoutPanel body, ConsentForm form, string text, Func<int, int> px)
    {
        var label = DialogLayout.CreateWrappingLabel(text, form.ContentPixelWidth);
        label.Font = form.BoldFont;
        DialogLayout.AddRow(body, label, new Padding(0, 0, 0, px(4)));
    }
}
