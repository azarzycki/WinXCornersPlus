using System.ComponentModel;
using System.Globalization;
using WinXCorners.Core.HotCorners;
using WinXCorners.Core.Input;
using WinXCorners.Core.Layout;
using WinXCorners.Core.Model;
using WinXCorners.Core.Security;
using WinXCorners.Core.Settings;

namespace WinXCorners.App;

/// <summary>
/// The Settings window. It edits a snapshot of the live settings and applies it through
/// <see cref="IAppHost.Settings"/>; the main form reacts to <c>SettingsSession.Changed</c>.
/// Risky switches ask for confirmation at the moment the user turns them on (brief design point 5).
/// </summary>
internal sealed class AdvancedForm : Form
{
    internal const string ReleasesUrl = "https://github.com/asteski/winxcornersplus/releases";
    internal const string WindowTitle = "WinXCornersPlus Settings";

    private const int DelayPanelLeftColumnX = 16;
    private const int DelayPanelRightColumnX = 225;
    private const int DelayPanelColumnLabelWidth = 132;
    private const int DelayPanelLeftComboX = 148;
    private const int DelayPanelRightComboX = 357;
    private const int DelayPanelComboWidth = 48;
    private const int DelayPanelFirstRowY = 18;
    private const int DelayPanelSecondRowY = 52;
    private const int DelayPanelGlobalRowY = 86;
    private const int DelayPanelFooterRowY = 118;

    private static readonly (string Text, FlyoutAnimationDirection Value)[] FlyoutAnimationDirections =
    [
        ("Top", FlyoutAnimationDirection.Top),
        ("Bottom", FlyoutAnimationDirection.Bottom),
        ("Left", FlyoutAnimationDirection.Left),
        ("Right", FlyoutAnimationDirection.Right)
    ];

    private static readonly HotCornerModifierKey[] ModifierKeyValues = Enum.GetValues<HotCornerModifierKey>();

    private readonly IAppHost _host;
    private readonly ApplicationSettings _workingCopy;
    private readonly CheckBox _chkGlobalDelay;
    private readonly ComboBox _cbGlobalDelay;
    private readonly CheckBox _chkTopLeftDelay;
    private readonly ComboBox _cbTopLeftDelay;
    private readonly CheckBox _chkTopRightDelay;
    private readonly ComboBox _cbTopRightDelay;
    private readonly CheckBox _chkBottomLeftDelay;
    private readonly ComboBox _cbBottomLeftDelay;
    private readonly CheckBox _chkBottomRightDelay;
    private readonly ComboBox _cbBottomRightDelay;
    private readonly CheckBox _chkShowCountdown;
    private readonly CheckBox _chkStartWithWindows;
    private readonly CheckBox _chkFullScreen;
    private readonly CheckBox _chkAlwaysRunAsAdministrator;
    private readonly CheckBox _chkAlwaysHideTrayIcon;
    private readonly ComboBox _cbFlyoutAnimationDirection;
    private readonly ComboBox _cbHotCornerModifierKey;
    private readonly CheckBox _chkCustom;
    private readonly CheckBox _chkLaunchHidden;
    private readonly TextBox _txtCommandName;
    private readonly TextBox _txtCommand;
    private readonly TextBox _txtParameters;
    private readonly Panel _delayPanel;
    private readonly Panel _otherPanel;
    private readonly Button[] _topSectionTabs;
    private readonly Button[] _commandTabs;
    private readonly Button _btnApply;
    private readonly Font _baseFont;
    private readonly Icon? _icon;
    private readonly UiScaling _scaling;
    private bool _isDirty;
    private bool _suppressDirtyTracking;
    private bool _revertingRiskySwitch;
    private int _selectedTopSectionTab;
    private int _selectedCommandIndex = -1;
    private SecurityInfoForm? _securityInfo;

    /// <param name="host">The platform layer.</param>
    /// <param name="confirmRisk">Replaces <see cref="ConfirmRisk"/> from the start, so tests can prove that loading prompts nothing.</param>
    /// <param name="showMessage">Replaces <see cref="ShowMessage"/> from the start.</param>
    internal AdvancedForm(IAppHost host,
        Func<IWin32Window?, string, string, IReadOnlyList<RiskItem>, string, bool>? confirmRisk = null,
        Action<string, string>? showMessage = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _workingCopy = host.Settings.Snapshot();
        ConfirmRisk = confirmRisk ?? ((owner, title, heading, items, confirmText) =>
            RiskDialog.Confirm(owner, title, heading, items, confirmText));
        ShowMessage = showMessage ?? ((title, text) =>
            MessageBox.Show(this, text, title, MessageBoxButtons.OK, MessageBoxIcon.Warning));
        HandleCreated += (_, _) => ThemeHelper.ApplyNativeWindowTheme(Handle);

        Name = "advancedForm";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(438, 452);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.Manual;
        Text = WindowTitle;
        BackColor = ThemeHelper.Colors.GetSettingsBackgroundColor();
        ForeColor = ThemeHelper.Colors.GetForegroundColor();
        _baseFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Font = _baseFont;
        _icon = WindowIconLoader.TryLoadAppIcon();
        if (_icon is not null)
        {
            Icon = _icon;
        }

        // Footer links. There is no room for two links side by side left of the Apply button (x 189), so they
        // are stacked in the 52 px between the custom-command panel (ends at y 400) and the window bottom:
        // y 410-425 and 429-444, centred on the buttons (y 419-442).
        var updatesLink = CreateLink("updatesLink", "Check for updates", new Point(16, 410));
        updatesLink.LinkClicked += (_, _) => _host.OpenLink(ReleasesUrl);

        var securityLink = CreateLink("securityLink", "Security && privacy…", new Point(16, 429));
        securityLink.LinkClicked += (_, _) => ShowSecurityInfo();

        // OK has no DialogResult of its own: it closes only after a successful apply.
        var btnOk = new SettingsFooterButton
        {
            Name = "okButton",
            Text = "&OK",
            Location = new Point(270, 419),
            Size = new Size(74, 23)
        };
        btnOk.Click += (_, _) => SaveAndClose();

        _btnApply = new SettingsFooterButton
        {
            Name = "applyButton",
            Text = "&Apply",
            Location = new Point(189, 419),
            Size = new Size(74, 23),
            Enabled = false
        };
        _btnApply.Click += (_, _) => ApplyChanges();

        var btnCancel = new SettingsFooterButton
        {
            Name = "cancelButton",
            Text = "&Cancel",
            Location = new Point(351, 419),
            Size = new Size(74, 23),
            DialogResult = DialogResult.Cancel
        };
        btnCancel.Click += (_, _) => Close();

        _topSectionTabs = new Button[2];
        var topTabsHost = new Panel
        {
            Name = "sectionTabs",
            Location = new Point(16, 7),
            Size = new Size(136, 24),
            BackColor = Color.Transparent
        };

        for (var index = 0; index < 2; index++)
        {
            var tabIndex = index;
            var tabButton = CreateTabButton(index == 0 ? "delaysTab" : "advancedTab", index == 0 ? "Delays" : "Advanced",
                new Point(index * 68, 0), new Size(68, 24));
            tabButton.Click += (_, _) => SelectTopSectionTab(tabIndex);
            _topSectionTabs[index] = tabButton;
            topTabsHost.Controls.Add(tabButton);
        }

        var topSectionHost = new Panel
        {
            Location = new Point(8, 31),
            Size = new Size(422, 145),
            BackColor = Color.Transparent
        };

        _delayPanel = new Panel
        {
            Name = "delayPanel",
            Location = new Point(0, 0),
            Size = new Size(422, 145),
            BackColor = ThemeHelper.Colors.GetSettingsPanelBackgroundColor(),
            BorderStyle = BorderStyle.FixedSingle
        };

        _otherPanel = new Panel
        {
            Name = "advancedPanel",
            Location = new Point(0, 0),
            Size = new Size(422, 145),
            BackColor = ThemeHelper.Colors.GetSettingsPanelBackgroundColor(),
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false
        };

        _chkGlobalDelay = CreateCheckBox("globalDelayCheckBox", "Set a global delay in seconds:", new Point(16, DelayPanelGlobalRowY));
        _chkGlobalDelay.AutoSize = false;
        _chkGlobalDelay.Size = new Size(214, 20);
        _chkGlobalDelay.CheckedChanged += (_, _) => UpdateDelayState();
        _cbGlobalDelay = CreateDelayCombo("globalDelayComboBox", new Point(DelayPanelRightColumnX + 7, DelayPanelGlobalRowY - 4));

        _chkTopLeftDelay = CreateAlignedDelayCheckBox("topLeftDelayCheckBox", "Top Left", DelayPanelLeftColumnX, DelayPanelFirstRowY);
        _chkTopLeftDelay.CheckedChanged += (_, _) => UpdateDelayState();
        _cbTopLeftDelay = CreateDelayCombo("topLeftDelayComboBox", new Point(DelayPanelLeftComboX, DelayPanelFirstRowY - 3));
        _chkTopRightDelay = CreateAlignedDelayCheckBox("topRightDelayCheckBox", "Top Right", DelayPanelRightColumnX, DelayPanelFirstRowY);
        _chkTopRightDelay.CheckedChanged += (_, _) => UpdateDelayState();
        _cbTopRightDelay = CreateDelayCombo("topRightDelayComboBox", new Point(DelayPanelRightComboX, DelayPanelFirstRowY - 3));
        _chkBottomLeftDelay = CreateAlignedDelayCheckBox("bottomLeftDelayCheckBox", "Bottom Left", DelayPanelLeftColumnX, DelayPanelSecondRowY);
        _chkBottomLeftDelay.CheckedChanged += (_, _) => UpdateDelayState();
        _cbBottomLeftDelay = CreateDelayCombo("bottomLeftDelayComboBox", new Point(DelayPanelLeftComboX, DelayPanelSecondRowY - 3));
        _chkBottomRightDelay = CreateAlignedDelayCheckBox("bottomRightDelayCheckBox", "Bottom Right", DelayPanelRightColumnX, DelayPanelSecondRowY);
        _chkBottomRightDelay.CheckedChanged += (_, _) => UpdateDelayState();
        _cbBottomRightDelay = CreateDelayCombo("bottomRightDelayComboBox", new Point(DelayPanelRightComboX, DelayPanelSecondRowY - 3));
        _chkShowCountdown = CreateCheckBox("showCountdownCheckBox", "Show Countdown", new Point(16, DelayPanelFooterRowY));

        _delayPanel.Controls.AddRange(
        [
            _chkGlobalDelay,
            _cbGlobalDelay,
            _chkTopLeftDelay,
            _cbTopLeftDelay,
            _chkTopRightDelay,
            _cbTopRightDelay,
            _chkBottomLeftDelay,
            _cbBottomLeftDelay,
            _chkBottomRightDelay,
            _cbBottomRightDelay,
            _chkShowCountdown
        ]);

        _chkStartWithWindows = CreateCheckBox("startWithWindowsCheckBox", "Run at startup", new Point(16, 19));
        _chkFullScreen = CreateCheckBox("ignoreFullScreenCheckBox", "Do nothing on Full Screen", new Point(16, 47));
        _chkAlwaysRunAsAdministrator = CreateCheckBox("alwaysAdminCheckBox", "Always run as administrator", new Point(16, 75));
        _chkAlwaysHideTrayIcon = CreateCheckBox("hideTrayCheckBox", "Always hide tray icon", new Point(16, 103));
        var lblFlyoutAnimationDirection = CreateLabel("Flyout animation direction", new Point(228, 21));
        _cbFlyoutAnimationDirection = CreateCombo("flyoutDirectionComboBox", new Point(228, 45), new Size(150, 23));
        _cbFlyoutAnimationDirection.Items.AddRange(FlyoutAnimationDirections.Select(static direction => (object)direction.Text).ToArray());
        var lblHotCornerModifierKey = CreateLabel("Hot Corners modifier key", new Point(228, 75));
        _cbHotCornerModifierKey = CreateCombo("modifierKeyComboBox", new Point(228, 99), new Size(150, 23));
        _cbHotCornerModifierKey.Items.AddRange(ModifierKeyValues.Select(static key => (object)key.ToString()).ToArray());

        _otherPanel.Controls.AddRange(
        [
            _chkStartWithWindows,
            _chkFullScreen,
            _chkAlwaysRunAsAdministrator,
            _chkAlwaysHideTrayIcon,
            lblFlyoutAnimationDirection,
            _cbFlyoutAnimationDirection,
            lblHotCornerModifierKey,
            _cbHotCornerModifierKey
        ]);

        topSectionHost.Controls.AddRange([_delayPanel, _otherPanel]);

        _commandTabs = new Button[ApplicationSettings.CustomCommandCount];
        var tabsHost = new Panel
        {
            Name = "commandTabs",
            Location = new Point(16, 196),
            Size = new Size(352, 24),
            BackColor = Color.Transparent
        };
        for (var index = 0; index < _commandTabs.Length; index++)
        {
            var tabIndex = index;
            var tabButton = CreateTabButton("commandTab" + (index + 1).ToString(CultureInfo.InvariantCulture),
                $"Command {index + 1}", new Point(index * 88, 0), new Size(88, 24));
            tabButton.Click += (_, _) => SelectCommandTab(tabIndex);
            _commandTabs[index] = tabButton;
            tabsHost.Controls.Add(tabButton);
        }

        var customPanel = new Panel
        {
            Name = "customCommandPanel",
            Location = new Point(8, 220),
            Size = new Size(422, 180),
            BackColor = ThemeHelper.Colors.GetSettingsPanelBackgroundColor(),
            BorderStyle = BorderStyle.FixedSingle
        };

        var lblCommandName = CreateLabel("Custom command name", new Point(16, 10));
        _txtCommandName = CreateTextBox("commandNameTextBox", new Point(16, 27), SettingsSanitizer.MaxNameLength, "Command 1");
        _txtCommandName.TextChanged += (_, _) => SaveCurrentTab();

        var lblCommand = CreateLabel("Command path or hotkey sequence", new Point(16, 55));
        _txtCommand = CreateTextBox("commandTextBox", new Point(16, 72), SettingsSanitizer.MaxCommandLength,
            "Examples: notepad.exe | !_win+tab | #[Notepad,readme]:(escape)?(_win+d)");
        _txtCommand.TextChanged += (_, _) => SaveCurrentTab();

        var lblParameters = CreateLabel("Parameters (optional)", new Point(16, 98));
        _txtParameters = CreateTextBox("parametersTextBox", new Point(16, 115), SettingsSanitizer.MaxParametersLength,
            "Optional command line arguments");
        _txtParameters.TextChanged += (_, _) => SaveCurrentTab();

        _chkCustom = CreateCheckBox("enableCustomCommandsCheckBox", "Enable Custom Commands", new Point(16, 145));

        // Same row as "Enable Custom Commands", right-aligned with the text boxes (which end at x 384).
        _chkLaunchHidden = CreateCheckBox("launchHiddenCheckBox", "Hide the program's window", new Point(204, 145));
        _chkLaunchHidden.AutoSize = false;
        _chkLaunchHidden.Size = new Size(180, 20);
        _chkLaunchHidden.CheckedChanged += (_, _) => SaveCurrentTab();

        customPanel.Controls.AddRange(
        [
            lblCommandName,
            _txtCommandName,
            lblCommand,
            _txtCommand,
            lblParameters,
            _txtParameters,
            _chkCustom,
            _chkLaunchHidden
        ]);

        // Screen readers name an edit box or combo box after its accessible name; the captions beside them are
        // separate labels, so the names are set here.
        _txtCommandName.AccessibleName = "Custom command name";
        _txtCommand.AccessibleName = "Command path or hotkey sequence";
        _txtParameters.AccessibleName = "Parameters";
        _cbGlobalDelay.AccessibleName = "Global delay in seconds";
        _cbTopLeftDelay.AccessibleName = "Top Left delay in seconds";
        _cbTopRightDelay.AccessibleName = "Top Right delay in seconds";
        _cbBottomLeftDelay.AccessibleName = "Bottom Left delay in seconds";
        _cbBottomRightDelay.AccessibleName = "Bottom Right delay in seconds";
        _cbFlyoutAnimationDirection.AccessibleName = "Flyout animation direction";
        _cbHotCornerModifierKey.AccessibleName = "Hot Corners modifier key";

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        Controls.AddRange([updatesLink, securityLink, btnOk, _btnApply, btnCancel, topTabsHost, topSectionHost, tabsHost, customPanel]);

        _scaling = new UiScaling(this);
        _scaling.Apply();

        HookDirtyTracking();
        HookRiskConfirmations();

        LoadFromSettings();
    }

    /// <summary>
    /// Asks the user to confirm a risky switch: (owner, title, heading, items, confirm text) -> confirmed.
    /// Defaults to <see cref="RiskDialog.Confirm"/>; tests replace it so no modal dialog blocks them.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<IWin32Window?, string, string, IReadOnlyList<RiskItem>, string, bool> ConfirmRisk { get; set; }

    /// <summary>Shows a message: (title, text). Defaults to a warning MessageBox; tests replace it.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<string, string> ShowMessage { get; set; }

    /// <summary>The Security &amp; privacy window opened from this one, if it is open.</summary>
    internal SecurityInfoForm? SecurityInfoWindow => _securityInfo is { IsDisposed: false } form ? form : null;

    /// <summary>True when there are edits that have not been applied.</summary>
    internal bool HasPendingChanges => _isDirty;

    /// <summary>The hand-placed layout's scaler; UI tests use it to simulate higher DPI.</summary>
    internal UiScaling Scaling => _scaling;

    internal void PositionNearTaskbarTray()
    {
        const int margin = 12;

        var screen = Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position);
        var workArea = screen.WorkingArea;
        var taskbarInfo = TaskbarInfo.TryGetPrimaryTaskbar() ?? new TaskbarInfo(screen.Bounds, TaskbarEdge.Bottom);

        Location = taskbarInfo.Edge switch
        {
            TaskbarEdge.Left => new Point(workArea.Left + margin, workArea.Bottom - Height - margin),
            TaskbarEdge.Right => new Point(workArea.Right - Width - margin, workArea.Bottom - Height - margin),
            TaskbarEdge.Top => new Point(workArea.Right - Width - margin, workArea.Top + margin),
            _ => new Point(workArea.Right - Width - margin, workArea.Bottom - Height - margin)
        };
    }

    /// <summary>Opens the Security &amp; privacy window (one at a time, owned by this window) or brings it forward.</summary>
    internal void ShowSecurityInfo()
    {
        if (SecurityInfoWindow is { } existing)
        {
            if (existing.WindowState == FormWindowState.Minimized)
            {
                existing.WindowState = FormWindowState.Normal;
            }

            existing.Activate();
            return;
        }

        var form = new SecurityInfoForm(_host);
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_securityInfo, form))
            {
                _securityInfo = null;
            }

            form.Dispose();
        };
        _securityInfo = form;
        form.Show(this);
    }

    /// <summary>
    /// Validates and applies the edits. Returns false when the window should stay open: a custom command is
    /// invalid (nothing was applied) or Run at startup could not be changed (everything else was applied).
    /// </summary>
    internal bool ApplyChanges()
    {
        if (!_isDirty)
        {
            return true;
        }

        var edited = CaptureEdits();
        if (!ValidateCommands(edited))
        {
            return false;
        }

        _host.Settings.Update(live => SettingsMerge.ApplyDialogEdits(live, edited));
        var completed = true;

        if (_chkStartWithWindows.Checked != _host.IsStartupEnabled())
        {
            if (!_host.TrySetStartupEnabled(_chkStartWithWindows.Checked, out var error))
            {
                completed = false;
                ShowMessage(WindowTitle, "Run at startup could not be changed: " + (error ?? "unknown error") + ".");
                var actual = _host.IsStartupEnabled();
                WithoutTracking(() => _chkStartWithWindows.Checked = actual);
                _workingCopy.StartWithWindows = actual;
                _host.Settings.Update(live => live.StartWithWindows = actual);
            }
        }

        var save = _host.Settings.Flush();
        if (!save.Succeeded)
        {
            // The changes are live in memory and the session stays dirty, so a later save retries them.
            ShowMessage(WindowTitle, "The settings could not be saved: " + (save.Error ?? "unknown error") +
                Environment.NewLine + "They are in use now but will be lost when the app exits unless a later save succeeds.");
        }

        SetDirty(false);
        return completed;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // The window may be created on a monitor whose DPI differs from the one measured in the constructor.
        _scaling?.Apply();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == UiScaling.WmDpiChanged)
        {
            _scaling?.Apply();
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _securityInfo?.Dispose();
            _securityInfo = null;
            _baseFont.Dispose();
            _icon?.Dispose();
        }
    }

    private void LoadFromSettings()
    {
        WithoutTracking(() =>
        {
            _chkGlobalDelay.Checked = _workingCopy.GlobalDelayEnabled;
            SelectClamped(_cbGlobalDelay, _workingCopy.GlobalDelayIndex);
            _chkTopLeftDelay.Checked = _workingCopy.TopLeftDelayEnabled;
            SelectClamped(_cbTopLeftDelay, _workingCopy.TopLeftDelayIndex);
            _chkTopRightDelay.Checked = _workingCopy.TopRightDelayEnabled;
            SelectClamped(_cbTopRightDelay, _workingCopy.TopRightDelayIndex);
            _chkBottomLeftDelay.Checked = _workingCopy.BottomLeftDelayEnabled;
            SelectClamped(_cbBottomLeftDelay, _workingCopy.BottomLeftDelayIndex);
            _chkBottomRightDelay.Checked = _workingCopy.BottomRightDelayEnabled;
            SelectClamped(_cbBottomRightDelay, _workingCopy.BottomRightDelayIndex);
            _chkShowCountdown.Checked = _workingCopy.ShowCountdown;
            // The Run entry is the truth; the stored flag can be stale (e.g. removed by another tool).
            _chkStartWithWindows.Checked = _host.IsStartupEnabled();
            _chkFullScreen.Checked = _workingCopy.IgnoreFullScreen;
            _chkAlwaysRunAsAdministrator.Checked = _workingCopy.AlwaysRunAsAdministrator;
            _chkAlwaysHideTrayIcon.Checked = _workingCopy.AlwaysHideTrayIcon;
            SelectClamped(_cbFlyoutAnimationDirection, GetFlyoutAnimationDirectionIndex(_workingCopy.FlyoutAnimationDirection));
            SelectClamped(_cbHotCornerModifierKey, Math.Max(0, Array.IndexOf(ModifierKeyValues, _workingCopy.HotCornerModifierKey)));
            _chkCustom.Checked = _workingCopy.EnableCustomCommands;
            SelectTopSectionTab(0);
            SelectCommandTab(0);
            UpdateDelayState();
        });
        SetDirty(false);
    }

    /// <summary>Writes the controls into the working copy and returns a copy of it.</summary>
    private ApplicationSettings CaptureEdits()
    {
        SaveCurrentTab();

        _workingCopy.GlobalDelayEnabled = _chkGlobalDelay.Checked;
        _workingCopy.GlobalDelayIndex = _cbGlobalDelay.SelectedIndex;
        _workingCopy.TopLeftDelayEnabled = _chkTopLeftDelay.Checked;
        _workingCopy.TopLeftDelayIndex = _cbTopLeftDelay.SelectedIndex;
        _workingCopy.TopRightDelayEnabled = _chkTopRightDelay.Checked;
        _workingCopy.TopRightDelayIndex = _cbTopRightDelay.SelectedIndex;
        _workingCopy.BottomLeftDelayEnabled = _chkBottomLeftDelay.Checked;
        _workingCopy.BottomLeftDelayIndex = _cbBottomLeftDelay.SelectedIndex;
        _workingCopy.BottomRightDelayEnabled = _chkBottomRightDelay.Checked;
        _workingCopy.BottomRightDelayIndex = _cbBottomRightDelay.SelectedIndex;
        _workingCopy.ShowCountdown = _chkShowCountdown.Checked;
        _workingCopy.StartWithWindows = _chkStartWithWindows.Checked;
        _workingCopy.IgnoreFullScreen = _chkFullScreen.Checked;
        _workingCopy.AlwaysRunAsAdministrator = _chkAlwaysRunAsAdministrator.Checked;
        _workingCopy.AlwaysHideTrayIcon = _chkAlwaysHideTrayIcon.Checked;
        _workingCopy.FlyoutAnimationDirection = GetSelectedFlyoutAnimationDirection();
        _workingCopy.HotCornerModifierKey = GetSelectedModifierKey();
        _workingCopy.EnableCustomCommands = _chkCustom.Checked;
        return _workingCopy.Clone();
    }

    /// <summary>
    /// Every command must pass the same analysis that runs it. On failure: one message listing each invalid
    /// command, the first offending tab selected and its command box focused.
    /// </summary>
    private bool ValidateCommands(ApplicationSettings edited)
    {
        var errors = new List<string>();
        var firstInvalid = -1;
        var commands = edited.CustomCommands ?? [];
        for (var i = 0; i < commands.Length && i < ApplicationSettings.CustomCommandCount; i++)
        {
            var analysis = CustomCommandAnalyzer.Analyze(commands[i]?.Command);
            if (analysis.IsValid)
            {
                continue;
            }

            if (firstInvalid < 0)
            {
                firstInvalid = i;
            }

            // The error can quote the user's text; escape it so it reads as one line.
            errors.Add($"Command {i + 1}: {DisplayText.ForDisplay(analysis.Error ?? "invalid command", 300)}");
        }

        if (firstInvalid < 0)
        {
            return true;
        }

        ShowMessage(WindowTitle, "These custom commands need fixing before the settings can be applied:" +
            Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, errors));
        SelectCommandTab(firstInvalid);
        _txtCommand.Focus();
        return false;
    }

    private void SaveAndClose()
    {
        // Applied exactly once (review B6); the main form reacts to SettingsSession.Changed.
        if (!ApplyChanges())
        {
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void HookRiskConfirmations()
    {
        _chkAlwaysRunAsAdministrator.CheckedChanged += (_, _) => ConfirmRiskySwitch(_chkAlwaysRunAsAdministrator,
            "Before WinXCornersPlus runs as administrator",
            edited => SecurityPosture.DescribeElevation(edited, _host.GetSecurityContext().Install, everyStart: true));
        _chkCustom.CheckedChanged += (_, _) => ConfirmRiskySwitch(_chkCustom,
            "Before you turn on custom commands",
            edited => Single(nameof(ApplicationSettings.EnableCustomCommands), edited));
        _chkAlwaysHideTrayIcon.CheckedChanged += (_, _) => ConfirmRiskySwitch(_chkAlwaysHideTrayIcon,
            "Before you hide the tray icon",
            edited => Single(nameof(ApplicationSettings.AlwaysHideTrayIcon), edited));
        _chkLaunchHidden.CheckedChanged += (_, _) => ConfirmRiskySwitch(_chkLaunchHidden,
            "Before this command runs without a window",
            edited => Single(SecurityPosture.LaunchHiddenSetting, edited));
    }

    private IReadOnlyList<RiskItem> Single(string settingName, ApplicationSettings edited)
    {
        var item = SecurityPosture.ForSetting(settingName, edited, _host.GetSecurityContext());
        return item is null ? [] : [item];
    }

    /// <summary>
    /// Runs when the user turns a risky switch on (never while the window loads or a tab is filled in, and
    /// never for the CheckedChanged raised by reverting it). Cancelling the confirmation turns it back off.
    /// </summary>
    private void ConfirmRiskySwitch(CheckBox box, string heading, Func<ApplicationSettings, IReadOnlyList<RiskItem>> describe)
    {
        if (_suppressDirtyTracking || _revertingRiskySwitch || !box.Checked)
        {
            return;
        }

        var items = describe(CaptureEdits());
        if (items.Count == 0 || ConfirmRisk(this, WindowTitle, heading, items, "Turn on"))
        {
            return;
        }

        _revertingRiskySwitch = true;
        try
        {
            box.Checked = false;
        }
        finally
        {
            _revertingRiskySwitch = false;
        }
    }

    /// <summary>Runs <paramref name="action"/> with dirty tracking and risk prompts off, restoring the previous state.</summary>
    private void WithoutTracking(Action action)
    {
        var previous = _suppressDirtyTracking;
        _suppressDirtyTracking = true;
        try
        {
            action();
        }
        finally
        {
            _suppressDirtyTracking = previous;
        }
    }

    private void UpdateDelayState()
    {
        _cbGlobalDelay.Enabled = _chkGlobalDelay.Checked;

        var enablePerCorner = !_chkGlobalDelay.Checked;
        _chkTopLeftDelay.Enabled = enablePerCorner;
        _chkTopRightDelay.Enabled = enablePerCorner;
        _chkBottomLeftDelay.Enabled = enablePerCorner;
        _chkBottomRightDelay.Enabled = enablePerCorner;

        _cbTopLeftDelay.Enabled = enablePerCorner && _chkTopLeftDelay.Checked;
        _cbTopRightDelay.Enabled = enablePerCorner && _chkTopRightDelay.Checked;
        _cbBottomLeftDelay.Enabled = enablePerCorner && _chkBottomLeftDelay.Checked;
        _cbBottomRightDelay.Enabled = enablePerCorner && _chkBottomRightDelay.Checked;
    }

    private CustomCommandSettings? CurrentCommand()
    {
        var commands = _workingCopy.CustomCommands;
        return commands is not null && _selectedCommandIndex >= 0 && _selectedCommandIndex < commands.Length
            ? commands[_selectedCommandIndex]
            : null;
    }

    private void LoadCommandTab()
    {
        var command = CurrentCommand();
        if (command is null)
        {
            return;
        }

        // Editable boxes show the stored text unchanged: escaping it here would change the command on save.
        WithoutTracking(() =>
        {
            _txtCommandName.PlaceholderText = $"Command {_selectedCommandIndex + 1}";
            _txtCommandName.Text = command.Name ?? string.Empty;
            _txtCommand.Text = command.Command ?? string.Empty;
            _txtParameters.Text = command.Parameters ?? string.Empty;
            _chkLaunchHidden.Checked = command.LaunchHidden;
        });
    }

    private void SaveCurrentTab()
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        var command = CurrentCommand();
        if (command is null)
        {
            return;
        }

        command.Name = _txtCommandName.Text.Trim();
        command.Command = _txtCommand.Text;
        command.Parameters = _txtParameters.Text;
        command.LaunchHidden = _chkLaunchHidden.Checked;
    }

    private void SelectTopSectionTab(int index)
    {
        if (index < 0 || index >= _topSectionTabs.Length)
        {
            return;
        }

        _selectedTopSectionTab = index;
        _delayPanel.Visible = index == 0;
        _otherPanel.Visible = index == 1;
        UpdateTabStyles(_topSectionTabs, _selectedTopSectionTab);
    }

    private void SelectCommandTab(int index)
    {
        if (index < 0 || index >= _commandTabs.Length)
        {
            return;
        }

        SaveCurrentTab();
        _selectedCommandIndex = index;
        UpdateTabStyles(_commandTabs, _selectedCommandIndex);
        LoadCommandTab();
    }

    // Only colours change between selected and unselected tabs; the font is inherited from the form and is
    // never re-created here.
    private static void UpdateTabStyles(Button[] tabs, int selectedIndex)
    {
        for (var i = 0; i < tabs.Length; i++)
        {
            var selected = i == selectedIndex;
            tabs[i].BackColor = selected ? ThemeHelper.Colors.GetSettingsSelectedTabBackgroundColor() : ThemeHelper.Colors.GetSettingsBackgroundColor();
            tabs[i].ForeColor = selected ? ThemeHelper.Colors.GetSettingsSelectedTabForegroundColor() : ThemeHelper.Colors.GetSettingsUnselectedTabForegroundColor();
        }
    }

    /// <summary>Selects <paramref name="index"/> clamped to the combo's items, so a hostile settings file cannot throw here (review B3).</summary>
    private static void SelectClamped(ComboBox combo, int index)
    {
        combo.SelectedIndex = combo.Items.Count == 0 ? -1 : Math.Clamp(index, 0, combo.Items.Count - 1);
    }

    private static Button CreateTabButton(string name, string text, Point location, Size size)
    {
        var tabButton = new Button
        {
            Name = name,
            Text = text,
            Location = location,
            Size = size,
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeHelper.Colors.GetSettingsBackgroundColor(),
            ForeColor = ThemeHelper.Colors.GetSettingsUnselectedTabForegroundColor(),
            TextAlign = ContentAlignment.MiddleCenter,
            TabStop = false
        };
        tabButton.FlatAppearance.BorderSize = 0;
        tabButton.FlatAppearance.MouseOverBackColor = ThemeHelper.Colors.GetSettingsSelectedTabBackgroundColor();
        tabButton.FlatAppearance.MouseDownBackColor = ThemeHelper.Colors.GetSettingsSelectedTabBackgroundColor();
        return tabButton;
    }

    private static LinkLabel CreateLink(string name, string text, Point location)
    {
        return new LinkLabel
        {
            Name = name,
            Text = text,
            LinkColor = ThemeHelper.Colors.GetLinkColor(),
            ActiveLinkColor = Color.FromArgb(255, 160, 100),
            VisitedLinkColor = ThemeHelper.Colors.GetLinkColor(),
            Location = location,
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = ThemeHelper.Colors.GetForegroundColor()
        };
    }

    private static Label CreateLabel(string text, Point location)
    {
        return new Label
        {
            Text = text,
            Location = location,
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = ThemeHelper.Colors.GetForegroundColor()
        };
    }

    private static TextBox CreateTextBox(string name, Point location, int maxLength, string placeholder)
    {
        return new TextBox
        {
            Name = name,
            Location = location,
            Size = new Size(368, 23),
            BackColor = ThemeHelper.Colors.GetControlBackgroundColor(),
            ForeColor = ThemeHelper.Colors.GetForegroundColor(),
            BorderStyle = BorderStyle.FixedSingle,
            MaxLength = maxLength,
            PlaceholderText = placeholder
        };
    }

    private static ComboBox CreateCombo(string name, Point location, Size size)
    {
        return new ComboBox
        {
            Name = name,
            Location = location,
            Size = size,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = ThemeHelper.Colors.GetControlBackgroundColor(),
            ForeColor = ThemeHelper.Colors.GetForegroundColor(),
            FlatStyle = FlatStyle.Flat
        };
    }

    private static ComboBox CreateDelayCombo(string name, Point location)
    {
        var combo = CreateCombo(name, location, new Size(DelayPanelComboWidth, 23));
        combo.Items.AddRange(HotCornerRules.DelayLabels.Select(static label => (object)label).ToArray());
        SelectClamped(combo, ApplicationSettings.DefaultDelayIndex);
        return combo;
    }

    private static CheckBox CreateAlignedDelayCheckBox(string name, string text, int x, int y)
    {
        var checkBox = CreateCheckBox(name, text, new Point(x, y));
        checkBox.AutoSize = false;
        checkBox.Size = new Size(DelayPanelColumnLabelWidth, 20);
        return checkBox;
    }

    private static CheckBox CreateCheckBox(string name, string text, Point location)
    {
        return new CheckBox
        {
            Name = name,
            Text = text,
            AutoSize = true,
            Location = location,
            BackColor = Color.Transparent,
            ForeColor = ThemeHelper.Colors.GetForegroundColor()
        };
    }

    private void HookDirtyTracking()
    {
        foreach (var checkBox in new[] { _chkGlobalDelay, _chkTopLeftDelay, _chkTopRightDelay, _chkBottomLeftDelay, _chkBottomRightDelay, _chkShowCountdown, _chkStartWithWindows, _chkFullScreen, _chkAlwaysRunAsAdministrator, _chkAlwaysHideTrayIcon, _chkCustom, _chkLaunchHidden })
        {
            checkBox.CheckedChanged += (_, _) => MarkDirty();
        }

        foreach (var comboBox in new[] { _cbGlobalDelay, _cbTopLeftDelay, _cbTopRightDelay, _cbBottomLeftDelay, _cbBottomRightDelay, _cbFlyoutAnimationDirection, _cbHotCornerModifierKey })
        {
            comboBox.SelectedIndexChanged += (_, _) => MarkDirty();
        }

        _txtCommandName.TextChanged += (_, _) => MarkDirty();
        _txtCommand.TextChanged += (_, _) => MarkDirty();
        _txtParameters.TextChanged += (_, _) => MarkDirty();
    }

    private static int GetFlyoutAnimationDirectionIndex(FlyoutAnimationDirection direction)
    {
        for (var index = 0; index < FlyoutAnimationDirections.Length; index++)
        {
            if (FlyoutAnimationDirections[index].Value == direction)
            {
                return index;
            }
        }

        return 1;
    }

    private FlyoutAnimationDirection GetSelectedFlyoutAnimationDirection()
    {
        var selectedIndex = _cbFlyoutAnimationDirection.SelectedIndex;
        return selectedIndex >= 0 && selectedIndex < FlyoutAnimationDirections.Length
            ? FlyoutAnimationDirections[selectedIndex].Value
            : FlyoutAnimationDirection.Bottom;
    }

    private HotCornerModifierKey GetSelectedModifierKey()
    {
        var selectedIndex = _cbHotCornerModifierKey.SelectedIndex;
        return selectedIndex >= 0 && selectedIndex < ModifierKeyValues.Length
            ? ModifierKeyValues[selectedIndex]
            : HotCornerModifierKey.None;
    }

    private void MarkDirty()
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        SetDirty(true);
    }

    private void SetDirty(bool isDirty)
    {
        _isDirty = isDirty;
        _btnApply.Enabled = isDirty;
    }
}
