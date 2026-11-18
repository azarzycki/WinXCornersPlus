// These tests share machine state (registry, named kernel objects, the foreground window, the WinForms
// process-wide exception mode), so they never run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Runs the "00 ..." collection first: it must call Application.SetUnhandledExceptionMode before any window
// handle exists in the process (see CrashReporterInstallTests).
[assembly: TestCollectionOrderer("WinXCorners.App.Tests.Infrastructure.ByNameCollectionOrderer", "WinXCorners.App.Tests")]
