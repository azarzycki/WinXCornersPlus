using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace WinXCorners.Core.Tests.Architecture;

/// <summary>
/// Security invariants of the app's source (review S1-S5, M3, R1) checked on every OS, so a change that quietly
/// re-introduces an elevated launch path, a hook, a DLL search-path import or a swallowed exception fails CI.
/// Matching ignores comments, and string contents except where the rule is about a string ("runas").
/// </summary>
public sealed partial class SourceInvariantTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private readonly ITestOutputHelper _output;

    public SourceInvariantTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string FindRepoRoot([CallerFilePath] string thisFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WinXCorners.Rewrite.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException($"No repository root above {thisFile}.");
    }

    private static List<SourceFile> Sources(string project)
    {
        var root = Path.Combine(RepoRoot, "src", project);
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(static segment => segment is "bin" or "obj"))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(path => new SourceFile(Path.GetRelativePath(RepoRoot, path).Replace('\\', '/'), File.ReadAllText(path)))
            .ToList();
    }

    private static List<SourceFile> AppSources() => Sources("WinXCorners.App");

    private static void AssertNone(IEnumerable<string> violations)
    {
        var list = violations.ToList();
        Assert.True(list.Count == 0, "Source invariant violated:" + Environment.NewLine + string.Join(Environment.NewLine, list));
    }

    [Fact]
    public void The_app_sources_are_found()
    {
        var files = AppSources();
        Assert.Contains(files, static f => f.FileName == "ProcessLauncher.cs");
        Assert.Contains(files, static f => f.FileName == "NativeMethods.cs");
        Assert.True(files.Count > 20, $"Only {files.Count} files under src/WinXCorners.App.");
    }

    [Fact]
    public void Process_Start_appears_only_in_the_launch_and_restart_code()
    {
        AssertNone(SourceInvariants.OnlyIn(AppSources(), static f => f.Code, ProcessStart(),
            ["ProcessLauncher.cs", "ProcessElevation.cs", "AppHost.cs"], "Process.Start"));
    }

    [Fact]
    public void The_runas_verb_appears_only_in_ProcessElevation()
    {
        AssertNone(SourceInvariants.OnlyIn(AppSources(), static f => f.NoComments, RunAs(), ["ProcessElevation.cs"], "The \"runas\" verb"));
    }

    [Fact]
    public void No_hooks_keybd_event_or_Environment_Exit()
    {
        var files = AppSources();
        AssertNone(SourceInvariants.Nowhere(files, static f => f.NoComments, Hook(), "SetWindowsHookEx (review B4)")
            .Concat(SourceInvariants.Nowhere(files, static f => f.NoComments, KeybdEvent(), "keybd_event (review B5; use SendInput)"))
            .Concat(SourceInvariants.Nowhere(files, static f => f.Code, EnvironmentExit(), "Environment.Exit (review B6; skips saving)")));
    }

    [Fact]
    public void No_bare_or_empty_catch_blocks()
    {
        AssertNone(AppSources().Concat(Sources("WinXCorners.Core")).SelectMany(SourceInvariants.BadCatches));
    }

    [Fact]
    public void The_registry_is_used_only_by_the_three_registry_classes()
    {
        AssertNone(SourceInvariants.OnlyIn(AppSources(), static f => f.Code, RegistryAccess(),
            ["StartupRegistration.cs", "ApprovedSettingsStore.cs", "ThemeHelper.cs"], "Registry access"));
    }

    [Fact]
    public void Native_imports_are_declared_only_in_NativeMethods()
    {
        AssertNone(SourceInvariants.OnlyIn(AppSources(), static f => f.Code, NativeImport(), ["NativeMethods.cs"], "[DllImport]/[LibraryImport]"));
    }

    [Fact]
    public void Native_libraries_are_loaded_from_System32_only()
    {
        var assemblyInfo = Assert.Single(AppSources(), static f => f.FileName == "AssemblyInfo.cs");
        Assert.True(System32Only().IsMatch(assemblyInfo.Code),
            $"{assemblyInfo.RelativePath}: must declare [assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)] (review S3).");
    }

    [Fact]
    public void Core_stays_platform_neutral()
    {
        AssertNone(SourceInvariants.Nowhere(Sources("WinXCorners.Core"), static f => f.Code, PlatformApis(),
            "Core must not use WinForms, Microsoft.Win32 or P/Invoke"));

        var project = Path.Combine(RepoRoot, "src", "WinXCorners.Core", "WinXCorners.Core.csproj");
        var lines = File.ReadAllLines(project);
        var packageLine = Array.FindIndex(lines, static l => l.Contains("PackageReference", StringComparison.Ordinal));
        Assert.True(packageLine < 0, $"src/WinXCorners.Core/WinXCorners.Core.csproj:{packageLine + 1}: Core must have no package references.");
    }

    [Fact]
    public void No_build_output_or_binaries_are_tracked_by_git()
    {
        if (!Directory.Exists(Path.Combine(RepoRoot, ".git")) && !File.Exists(Path.Combine(RepoRoot, ".git")))
        {
            // xunit 2 has no runtime skip in this project; a source export without .git has nothing to check.
            _output.WriteLine("Not a git checkout: nothing to check.");
            return;
        }

        var startInfo = new ProcessStartInfo("git")
        {
            ArgumentList = { "-C", RepoRoot, "-c", "core.quotepath=off", "ls-files", "-z" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var git = Process.Start(startInfo)!;
        var output = git.StandardOutput.ReadToEnd();
        var errors = git.StandardError.ReadToEnd();
        Assert.True(git.WaitForExit(60_000), "git ls-files hung.");
        Assert.True(git.ExitCode == 0, $"git ls-files failed: {errors}");

        var tracked = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(tracked.Length > 50, $"git ls-files listed only {tracked.Length} files.");
        AssertNone(tracked
            .Where(static path => path.Split('/').Any(static segment => segment is "bin" or "obj") ||
                                  path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                  path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(static path => $"{path}: build output or a binary is tracked by git (review R1)"));
    }

    // ---- review V2 ---------------------------------------------------------------------------------

    /// <summary>
    /// D1: moving, replacing or copying files is what an elevated process must never be steered into. Only the settings
    /// store does it, and its policy forbids it in an elevated session.
    /// </summary>
    [Fact]
    public void Files_are_moved_replaced_or_copied_only_by_the_settings_store()
    {
        AssertNone(SourceInvariants.OnlyIn(AppSources().Concat(Sources("WinXCorners.Core")), static f => f.Code, FileMoves(),
            ["SettingsStore.cs"], "File.Move/File.Replace/File.Copy/Directory.Move"));
    }

    /// <summary>D1: Redirection Guard is turned on in Program.Main before the composition root touches the disk.</summary>
    [Fact]
    public void Redirection_Guard_is_enabled_before_settings_are_loaded()
    {
        var program = Assert.Single(AppSources(), static f => f.FileName == "Program.cs");
        var guard = program.Code.IndexOf("ProcessMitigations.EnableRedirectionGuard()", StringComparison.Ordinal);
        var host = program.Code.IndexOf("AppHost.Create(", StringComparison.Ordinal);
        Assert.True(guard >= 0, "Program.cs must call ProcessMitigations.EnableRedirectionGuard().");
        Assert.True(host > guard, "Program.cs must enable Redirection Guard before AppHost.Create.");
    }

    /// <summary>
    /// D2: Cursor.Position hides GetCursorPos failing and reports (0,0), the top-left corner. Hot corners read the
    /// pointer through InputDesktop.TryGetCursorPosition, which reports the failure.
    /// </summary>
    [Fact]
    public void Hot_corner_input_never_comes_from_Cursor_Position()
    {
        var files = AppSources();
        AssertNone(SourceInvariants.Nowhere(files.Where(static f => f.FileName == "HotCornerMonitor.cs"), static f => f.Code,
            CursorPosition(), "Cursor.Position in the hot-corner monitor (use InputDesktop.TryGetCursorPosition)"));

        var mainForm = Assert.Single(files, static f => f.FileName == "MainForm.cs");
        var start = mainForm.Code.IndexOf("new HotCornerMonitor(", StringComparison.Ordinal);
        Assert.True(start >= 0, "MainForm.cs no longer builds a HotCornerMonitor; update this guard.");
        var end = mainForm.Code.IndexOf(");", start, StringComparison.Ordinal);
        var construction = mainForm.Code[start..end];
        Assert.DoesNotMatch(CursorPosition(), construction);
        Assert.Contains("InputDesktop.TryGetCursorPosition", construction, StringComparison.Ordinal);
    }

    /// <summary>Review V2: AllowSetForegroundWindow(ASFW_ANY) handed the foreground right to every process.</summary>
    [Fact]
    public void AllowSetForegroundWindow_is_never_given_to_any_process()
    {
        AssertNone(SourceInvariants.Nowhere(AppSources(), static f => f.Code, AllowAnyForeground(), "AllowSetForegroundWindow(ASFW_ANY / -1)"));
    }

    /// <summary>
    /// Review V2: third-party actions run with the workflow's token, so a moved tag is a supply-chain risk. Every
    /// <c>uses:</c> line of every workflow names a full commit SHA, with the release it is in a trailing comment (which
    /// is what Dependabot updates).
    /// </summary>
    [Fact]
    public void Every_workflow_action_is_pinned_to_a_full_commit_sha_with_its_version()
    {
        var folder = Path.Combine(RepoRoot, ".github", "workflows");
        var workflows = Directory.GetFiles(folder, "*.yml").Concat(Directory.GetFiles(folder, "*.yaml")).Order(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(workflows);
        var uses = 0;
        AssertNone(workflows.SelectMany(path => File.ReadAllLines(path).Select((line, index) => (line, index))
            .Where(static l => UsesLine().IsMatch(l.line))
            .Select(l => (path, l.line, l.index)))
            .Where(l =>
            {
                uses++;
                return !PinnedUse().IsMatch(l.line);
            })
            .Select(l => $"{Path.GetRelativePath(RepoRoot, l.path).Replace('\\', '/')}:{l.index + 1}: not pinned to a commit SHA with a version comment: {l.line.Trim()}"));
        Assert.True(uses >= 3, $"Only {uses} uses: lines found; the guard is not looking at the workflows.");
    }

    [Fact]
    public void Dependabot_keeps_actions_and_packages_current()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, ".github", "dependabot.yml"));
        Assert.Contains("package-ecosystem: github-actions", text, StringComparison.Ordinal);
        Assert.Contains("package-ecosystem: nuget", text, StringComparison.Ordinal);
    }

    // ---- the guard's own machinery ----------------------------------------------------------------

    [Fact]
    public void The_lexer_hides_comments_and_literals_but_keeps_interpolation_holes_and_line_numbers()
    {
        const string source = """"
            // Process.Start in a comment
            /* Process.Start
               in a block */ var a = "Process.Start(x)";
            var b = @"Process.Start(""y"")";
            var c = $"{Process.Start(z)} and {{Process.Start}}";
            var d = '"'; var e = "\"Process.Start\"";
            var f = $$"""{{Process.Start(w)}} {Process.Start}""";
            """";
        var file = new SourceFile("x.cs", source);

        var hits = SourceInvariants.Matches(file, file.Code, ProcessStart(), "hit").ToList();

        Assert.Equal(["x.cs:5: hit", "x.cs:7: hit"], hits);
        Assert.Equal(source.Length, file.Code.Length);
        Assert.Equal(source.Count(static ch => ch == '\n'), file.Code.Count(static ch => ch == '\n'));
        Assert.Contains("\"Process.Start(x)\"", file.NoComments, StringComparison.Ordinal);
        Assert.DoesNotContain("in a block", file.NoComments, StringComparison.Ordinal);
    }

    [Fact]
    public void The_catch_rule_reports_bare_empty_and_comment_only_catch_all_blocks()
    {
        const string source = """
            try { } catch { Log(); }
            try { } catch (IOException) { }
            try { } catch (Exception) { /* ignored */ }
            try { } catch (System.Exception ex) when (Check(ex, (1))) { // nothing
            }
            try { } catch (IOException) { /* Expected: the file is optional. */ }
            try { } catch (Exception ex) { Log(ex); }
            try { } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
            var s = "catch { }";
            """;

        var violations = SourceInvariants.BadCatches(new SourceFile("y.cs", source)).ToList();

        Assert.Equal(4, violations.Count);
        Assert.StartsWith("y.cs:1: bare", violations[0], StringComparison.Ordinal);
        Assert.StartsWith("y.cs:2: empty", violations[1], StringComparison.Ordinal);
        Assert.StartsWith("y.cs:3: catch (Exception)", violations[2], StringComparison.Ordinal);
        Assert.StartsWith("y.cs:4: catch (Exception)", violations[3], StringComparison.Ordinal);
    }

    [Fact]
    public void The_rules_catch_what_they_are_meant_to_catch()
    {
        var bad = new SourceFile("src/Bad.cs", """
            using Microsoft.Win32;
            class Bad
            {
                [DllImport("user32.dll")] static extern void keybd_event();
                void M()
                {
                    Process.Start(new ProcessStartInfo { Verb = "RunAs" });
                    Registry.CurrentUser.OpenSubKey("x");
                    RegistryKey.OpenBaseKey(default, default);
                    Environment.Exit(0);
                    SetWindowsHookEx(13, null, 0, 0);
                }
            }
            """);
        SourceFile[] files = [bad];

        Assert.Single(SourceInvariants.OnlyIn(files, static f => f.Code, ProcessStart(), ["Other.cs"], "x"));
        Assert.Single(SourceInvariants.OnlyIn(files, static f => f.NoComments, RunAs(), ["Other.cs"], "x"));
        Assert.Equal(2, SourceInvariants.OnlyIn(files, static f => f.Code, RegistryAccess(), ["Other.cs"], "x").Count());
        Assert.Single(SourceInvariants.OnlyIn(files, static f => f.Code, NativeImport(), ["Other.cs"], "x"));
        Assert.Single(SourceInvariants.Nowhere(files, static f => f.Code, EnvironmentExit(), "x"));
        Assert.Single(SourceInvariants.Nowhere(files, static f => f.NoComments, Hook(), "x"));
        Assert.Single(SourceInvariants.Nowhere(files, static f => f.NoComments, KeybdEvent(), "x"));
        Assert.Equal(2, SourceInvariants.Nowhere(files, static f => f.Code, PlatformApis(), "x").Count());
        Assert.Empty(SourceInvariants.OnlyIn(files, static f => f.Code, ProcessStart(), ["Bad.cs"], "x"));

        var review = new SourceFile("src/Review.cs", """
            class R
            {
                void M()
                {
                    File.Move(a, b, overwrite: true);
                    File.Replace(a, b, null);
                    System.IO.File.Copy(a, b);
                    Directory.Move(a, b);
                    var p = Cursor.Position;
                    AllowSetForegroundWindow(ASFW_ANY);
                    NativeMethods.AllowSetForegroundWindow(-1);
                    AllowSetForegroundWindow(processId);
                }
            }
            """);
        SourceFile[] reviewFiles = [review];
        Assert.Equal(4, SourceInvariants.Nowhere(reviewFiles, static f => f.Code, FileMoves(), "x").Count());
        Assert.Single(SourceInvariants.Nowhere(reviewFiles, static f => f.Code, CursorPosition(), "x"));
        Assert.Equal(2, SourceInvariants.Nowhere(reviewFiles, static f => f.Code, AllowAnyForeground(), "x").Count());

        Assert.Matches(PinnedUse(), "      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1");
        Assert.Matches(PinnedUse(), "        uses: ./.github/actions/local");
        Assert.DoesNotMatch(PinnedUse(), "      - uses: actions/checkout@v4");
        Assert.DoesNotMatch(PinnedUse(), "      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1");
        Assert.DoesNotMatch(PinnedUse(), "      - uses: actions/checkout@3d3c42e # v7.0.1");
        Assert.Matches(UsesLine(), "  - uses: x/y@v1");
        Assert.DoesNotMatch(UsesLine(), "  # uses: x/y@v1");
    }

    [GeneratedRegex(@"\bProcess\s*\.\s*Start\s*\(")]
    private static partial Regex ProcessStart();

    [GeneratedRegex("\"runas\"", RegexOptions.IgnoreCase)]
    private static partial Regex RunAs();

    [GeneratedRegex(@"\bSetWindowsHookEx")]
    private static partial Regex Hook();

    [GeneratedRegex(@"\bkeybd_event\b")]
    private static partial Regex KeybdEvent();

    [GeneratedRegex(@"\bEnvironment\s*\.\s*Exit\s*\(")]
    private static partial Regex EnvironmentExit();

    [GeneratedRegex(@"\bRegistry(Key)?\s*\.")]
    private static partial Regex RegistryAccess();

    [GeneratedRegex(@"\[\s*(DllImport|LibraryImport)\b")]
    private static partial Regex NativeImport();

    [GeneratedRegex(@"\[\s*assembly\s*:\s*DefaultDllImportSearchPaths\s*\(\s*DllImportSearchPath\s*\.\s*System32\s*\)\s*\]")]
    private static partial Regex System32Only();

    [GeneratedRegex(@"\b(File\s*\.\s*(Move|Replace|Copy)|Directory\s*\.\s*Move)\s*\(")]
    private static partial Regex FileMoves();

    [GeneratedRegex(@"\bCursor\s*\.\s*Position\b")]
    private static partial Regex CursorPosition();

    [GeneratedRegex(@"\bAllowSetForegroundWindow\s*\(\s*(-\s*1|(NativeMethods\s*\.\s*)?ASFW_ANY)\s*\)")]
    private static partial Regex AllowAnyForeground();

    [GeneratedRegex(@"^\s*(-\s*)?uses\s*:")]
    private static partial Regex UsesLine();

    /// <summary>owner/repo[/path]@40-hex-sha # vX..., or a local action (./...).</summary>
    [GeneratedRegex(@"^\s*(-\s*)?uses\s*:\s*(\./\S+|[\w.-]+/[\w./-]+@[0-9a-f]{40}\s+#\s*v\d+(\.\d+)*)\s*$")]
    private static partial Regex PinnedUse();

    [GeneratedRegex(@"\bSystem\s*\.\s*Windows\s*\.\s*Forms\b|\bMicrosoft\s*\.\s*Win32\b|\bDllImport\b|\bLibraryImport\b")]
    private static partial Regex PlatformApis();
}
