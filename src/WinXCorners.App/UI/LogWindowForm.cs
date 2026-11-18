using System.Runtime.InteropServices;
using WinXCorners.Core.Diagnostics;

namespace WinXCorners.App;

/// <summary>
/// Shows the runtime log. Closing it by the user only hides it so it can be reopened with its content;
/// every other close (app exit, shutdown, owner closing) is allowed through.
/// </summary>
internal sealed class LogWindowForm : Form
{
    /// <summary>When the text box holds more lines than this it is trimmed to <see cref="TrimToLines"/>.</summary>
    internal const int MaxLines = 2000;

    internal const int TrimToLines = 1000;

    private readonly BoundedLog _log;
    private readonly TextBox _logText;
    private readonly Font _baseFont;
    private readonly Font _monospaceFont;
    private int _lineCount;

    internal LogWindowForm(BoundedLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;

        Name = "logWindowForm";
        Text = "WinXCornersPlus Log Window";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 420);
        MinimumSize = new Size(640, 320);
        BackColor = ThemeHelper.Colors.GetBackgroundColor();
        _baseFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Font = _baseFont;
        _monospaceFont = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
        HandleCreated += (_, _) => ThemeHelper.ApplyNativeWindowTheme(Handle);

        _logText = new TextBox
        {
            Name = "logTextBox",
            AccessibleName = "Runtime events",
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = _monospaceFont,
            BackColor = ThemeHelper.Colors.GetControlBackgroundColor(),
            ForeColor = ThemeHelper.Colors.GetForegroundColor(),
            Dock = DockStyle.Fill,
            // The default 32K limit would silently stop appends long before the line cap is reached.
            MaxLength = 0
        };

        var copyButton = CreateButton("Copy all", "copyAllButton");
        copyButton.Click += (_, _) => CopyAll();

        var clearButton = CreateButton("Clear", "clearButton");
        clearButton.Click += (_, _) => ClearLog();

        var closeButton = CreateButton("Close", "closeButton");
        closeButton.Click += (_, _) => Hide();

        var topPanel = new Panel
        {
            Name = "topPanel",
            Height = 38,
            Dock = DockStyle.Top,
            Padding = new Padding(8, 6, 8, 6),
            BackColor = ThemeHelper.Colors.GetPanelBackgroundColor()
        };

        var title = new Label
        {
            Text = "Runtime events",
            AutoSize = true,
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ThemeHelper.Colors.GetForegroundColor(),
            Padding = new Padding(0, 6, 0, 0)
        };

        // Docking starts from the control added last, which takes the outermost place; so with this order the
        // title is on the left and the buttons read Copy all, Clear, Close with Close at the far right.
        topPanel.Controls.Add(copyButton);
        topPanel.Controls.Add(clearButton);
        topPanel.Controls.Add(closeButton);
        topPanel.Controls.Add(title);

        Controls.Add(_logText);
        Controls.Add(topPanel);

        LoadSnapshot();
        _log.EntryAdded += OnEntryAdded;
    }

    /// <summary>Appends one entry; must be called on the UI thread.</summary>
    internal void AppendLog(string entry)
    {
        if (_logText.TextLength > 0)
        {
            _logText.AppendText(Environment.NewLine);
            _lineCount++;
        }
        else
        {
            _lineCount = 1;
        }

        // BoundedLog already replaced line breaks in entries, so one entry is one line.
        _logText.AppendText(entry);

        if (_lineCount > MaxLines)
        {
            SetText(LogText.TrimToLastLines(_logText.Text, TrimToLines));
        }

        _logText.SelectionStart = _logText.TextLength;
        _logText.ScrollToCaret();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Entries added before the handle existed could not be marshalled here; the log has them all.
        LoadSnapshot();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Only a close by the user is turned into a hide. Cancelling every close (as this window once did)
        // vetoes Application.Exit() after the tray icon is already gone, leaving an invisible process, and
        // blocks Windows from shutting down (review B1).
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _log.EntryAdded -= OnEntryAdded;
        }

        base.Dispose(disposing);
        if (disposing)
        {
            _monospaceFont.Dispose();
            _baseFont.Dispose();
        }
    }

    // Raised on whichever thread logged the entry.
    private void OnEntryAdded(string entry)
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed)
                {
                    AppendLog(entry);
                }
            });
        }
        catch (InvalidOperationException)
        {
            // The handle was destroyed between the check and the call (window closing): nothing to show.
        }
    }

    private void LoadSnapshot()
    {
        var entries = _log.Snapshot();
        var text = LogText.TrimToLastLines(string.Join(Environment.NewLine, entries), TrimToLines);
        SetText(text);
        _logText.SelectionStart = _logText.TextLength;
        if (IsHandleCreated)
        {
            _logText.ScrollToCaret();
        }
    }

    private void SetText(string text)
    {
        _logText.Text = text;
        _lineCount = text.Length == 0 ? 0 : text.Count(static c => c == '\n') + 1;
    }

    private void ClearLog()
    {
        SetText(string.Empty);
        _log.Clear();
    }

    private void CopyAll()
    {
        if (_logText.TextLength == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(_logText.Text);
        }
        catch (ExternalException)
        {
            // Another program holds the clipboard open; the user can simply try again.
        }
    }

    private static Button CreateButton(string text, string name)
    {
        return new Button
        {
            Name = name,
            Text = text,
            Dock = DockStyle.Right,
            Width = 90,
            BackColor = ThemeHelper.Colors.GetControlBackgroundColor(),
            ForeColor = ThemeHelper.Colors.GetForegroundColor()
        };
    }
}
