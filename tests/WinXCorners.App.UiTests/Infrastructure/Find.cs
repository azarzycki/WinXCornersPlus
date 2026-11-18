using System.Reflection;

namespace WinXCorners.App.UiTests.Infrastructure;

/// <summary>Looks up controls by name and drives the few interactions WinForms has no public method for.</summary>
internal static class Find
{
    /// <summary>The single descendant of <paramref name="root"/> with this name and type; fails otherwise.</summary>
    internal static T Control<T>(Control root, string name)
        where T : Control
    {
        var matches = root.Controls.Find(name, searchAllChildren: true).OfType<T>().ToList();
        Assert.True(matches.Count == 1, $"Expected one {typeof(T).Name} named '{name}' in {root.Name}, found {matches.Count}.");
        return matches[0];
    }

    /// <summary>The descendant with this name, or null.</summary>
    internal static T? Optional<T>(Control root, string name)
        where T : Control
        => root.Controls.Find(name, searchAllChildren: true).OfType<T>().FirstOrDefault();

    internal static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandChild in Descendants(child))
            {
                yield return grandChild;
            }
        }
    }

    /// <summary>All visible text of <paramref name="root"/> (labels, text boxes, buttons), one control per line.</summary>
    internal static string AllText(Control root) =>
        string.Join(Environment.NewLine, Descendants(root).Where(static c => c.Visible).Select(static c => c.Text));

    /// <summary>Raises LinkClicked for the first link, exactly as a click on it does.</summary>
    internal static void ClickLink(LinkLabel link)
    {
        var method = typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(LinkLabel), "OnLinkClicked");
        method.Invoke(link, [new LinkLabelLinkClickedEventArgs(link.Links[0], MouseButtons.Left)]);
    }
}
