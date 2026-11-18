using WinXCorners.Core.Platform;

namespace WinXCorners.Core.Tests.Platform;

public class CommandLineOptionsTests
{
    [Fact]
    public void Parse_EmptyAndNullGiveDefaults()
    {
        foreach (var args in new[] { null, Array.Empty<string?>(), new string?[] { null, "  " } })
        {
            var options = CommandLineOptions.Parse(args);
            Assert.False(options.NoElevate);
            Assert.False(options.ShowSettings);
            Assert.Null(options.RestartedFromProcessId);
            Assert.Empty(options.Unrecognized);
        }
    }

    [Fact]
    public void Parse_RecognisesAllSwitchesCaseInsensitively()
    {
        var options = CommandLineOptions.Parse(["--NO-ELEVATE", "--Show-Settings", "--restarted-FROM", "4242"]);
        Assert.True(options.NoElevate);
        Assert.True(options.ShowSettings);
        Assert.Equal(4242, options.RestartedFromProcessId);
        Assert.Empty(options.Unrecognized);
    }

    [Theory]
    [InlineData("--restarted-from=17", 17)]
    [InlineData("--RESTARTED-FROM= 17", 17)]
    public void Parse_AcceptsEqualsForm(string arg, int expected)
    {
        Assert.Equal(expected, CommandLineOptions.Parse([arg]).RestartedFromProcessId);
    }

    [Theory]
    [InlineData(new[] { "--restarted-from" }, "--restarted-from")]
    [InlineData(new[] { "--restarted-from", "abc" }, "--restarted-from abc")]
    [InlineData(new[] { "--restarted-from", "-5" }, "--restarted-from -5")]
    [InlineData(new[] { "--restarted-from", "0" }, "--restarted-from 0")]
    [InlineData(new[] { "--restarted-from", "99999999999" }, "--restarted-from 99999999999")]
    [InlineData(new[] { "--restarted-from", "1e3" }, "--restarted-from 1e3")]
    [InlineData(new[] { "--restarted-from=" }, "--restarted-from=")]
    [InlineData(new[] { "--restarted-from=x" }, "--restarted-from=x")]
    public void Parse_InvalidProcessIdIsNullAndRecorded(string[] args, string unrecognized)
    {
        var options = CommandLineOptions.Parse(args);
        Assert.Null(options.RestartedFromProcessId);
        Assert.Equal([unrecognized], options.Unrecognized);
    }

    [Fact]
    public void Parse_DoesNotConsumeFollowingSwitchAsProcessId()
    {
        var options = CommandLineOptions.Parse(["--restarted-from", "--show-settings"]);
        Assert.Null(options.RestartedFromProcessId);
        Assert.True(options.ShowSettings);
        Assert.Equal(["--restarted-from"], options.Unrecognized);
    }

    [Fact]
    public void Parse_RecordsUnknownArgumentsAndNeverThrows()
    {
        var options = CommandLineOptions.Parse(["/elevate", "--no-elevate", "file.txt", "\0"]);
        Assert.True(options.NoElevate);
        Assert.Equal(["/elevate", "file.txt", "\0"], options.Unrecognized);
    }

    [Theory]
    [InlineData(1234, false)]
    [InlineData(1, true)]
    public void BuildRestartArguments_RoundTripsThroughParse(int pid, bool noElevate)
    {
        var args = CommandLineOptions.BuildRestartArguments(pid, noElevate);
        var options = CommandLineOptions.Parse(args.ToArray());
        Assert.Equal(pid, options.RestartedFromProcessId);
        Assert.Equal(noElevate, options.NoElevate);
        Assert.False(options.ShowSettings);
        Assert.Empty(options.Unrecognized);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BuildRestartArguments_RejectsNonPositivePid(int pid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommandLineOptions.BuildRestartArguments(pid, false));
    }
}
