using WinXCorners.Core.Platform;

namespace WinXCorners.Core.Tests.Platform;

public class SessionEndPolicyTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    public void Query_end_session_only_saves_settings(long wParam)
    {
        Assert.Equal(SessionEndResponse.SaveSettings, SessionEndPolicy.Decide(SessionEndPolicy.WmQueryEndSession, wParam));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void End_session_that_really_ends_exits(long wParam)
    {
        Assert.Equal(SessionEndResponse.Exit, SessionEndPolicy.Decide(SessionEndPolicy.WmEndSession, wParam));
    }

    [Fact]
    public void Cancelled_end_session_keeps_running()
    {
        Assert.Equal(SessionEndResponse.None, SessionEndPolicy.Decide(SessionEndPolicy.WmEndSession, 0));
    }

    [Theory]
    [InlineData(0x0000)] // WM_NULL
    [InlineData(0x0010)] // WM_CLOSE: never sent by users to a hidden tray window; not a session end
    [InlineData(0x0012)] // WM_QUIT
    [InlineData(0x00FF)] // WM_INPUT
    [InlineData(-1)]
    public void Other_messages_are_ignored(int message)
    {
        Assert.Equal(SessionEndResponse.None, SessionEndPolicy.Decide(message, 1));
    }

    [Fact]
    public void Constants_match_the_Windows_headers()
    {
        Assert.Equal(0x11, SessionEndPolicy.WmQueryEndSession);
        Assert.Equal(0x16, SessionEndPolicy.WmEndSession);
        Assert.Equal(1L, SessionEndPolicy.EndSessionCloseApp);
    }
}
