using WinXCorners.Core.HotCorners;

namespace WinXCorners.Core.Tests.HotCorners;

public class SessionActivityTests
{
    [Fact]
    public void Starts_active()
    {
        Assert.True(new SessionActivity().IsActive);
    }

    [Fact]
    public void Lock_and_unlock()
    {
        var session = new SessionActivity();
        Assert.True(session.Apply(SessionChange.SessionLock));
        Assert.False(session.IsActive);
        Assert.False(session.Apply(SessionChange.SessionLock));
        Assert.True(session.Apply(SessionChange.SessionUnlock));
        Assert.True(session.IsActive);
        Assert.False(session.Apply(SessionChange.SessionUnlock));
    }

    [Theory]
    [InlineData(SessionChange.ConsoleDisconnect, SessionChange.ConsoleConnect)]
    [InlineData(SessionChange.RemoteDisconnect, SessionChange.RemoteConnect)]
    [InlineData(SessionChange.ConsoleDisconnect, SessionChange.RemoteConnect)]
    [InlineData(SessionChange.RemoteDisconnect, SessionChange.ConsoleConnect)]
    public void Disconnect_and_reconnect(SessionChange disconnect, SessionChange connect)
    {
        var session = new SessionActivity();
        Assert.True(session.Apply(disconnect));
        Assert.False(session.IsActive);
        Assert.True(session.Apply(connect));
        Assert.True(session.IsActive);
    }

    [Fact]
    public void A_locked_session_that_reconnects_stays_paused_until_it_is_unlocked()
    {
        var session = new SessionActivity();
        session.Apply(SessionChange.SessionLock);
        session.Apply(SessionChange.RemoteDisconnect);
        Assert.False(session.Apply(SessionChange.RemoteConnect));
        Assert.False(session.IsActive);
        Assert.True(session.Apply(SessionChange.SessionUnlock));
        Assert.True(session.IsActive);
    }

    [Fact]
    public void An_unlock_while_disconnected_stays_paused_until_reconnected()
    {
        var session = new SessionActivity();
        session.Apply(SessionChange.ConsoleDisconnect);
        session.Apply(SessionChange.SessionLock);
        Assert.False(session.Apply(SessionChange.SessionUnlock));
        Assert.False(session.IsActive);
        Assert.True(session.Apply(SessionChange.ConsoleConnect));
    }

    [Theory]
    [InlineData(SessionChange.SessionLogon)]
    [InlineData(SessionChange.SessionLogoff)]
    [InlineData(SessionChange.SessionRemoteControl)]
    [InlineData((SessionChange)0)]
    [InlineData((SessionChange)1234)]
    public void Other_notifications_change_nothing(SessionChange change)
    {
        var active = new SessionActivity();
        Assert.False(active.Apply(change));
        Assert.True(active.IsActive);

        var locked = new SessionActivity();
        locked.Apply(SessionChange.SessionLock);
        Assert.False(locked.Apply(change));
        Assert.False(locked.IsActive);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    public void Pointer_is_available_only_when_read_on_our_desktop_in_an_active_session(
        bool positionRead, bool ourDesktop, bool locked, bool expected)
    {
        var session = new SessionActivity();
        if (locked)
        {
            session.Apply(SessionChange.SessionLock);
        }

        Assert.Equal(expected, session.IsPointerAvailable(positionRead, ourDesktop));
    }
}
