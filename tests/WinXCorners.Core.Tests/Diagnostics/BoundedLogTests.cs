using WinXCorners.Core.Diagnostics;

namespace WinXCorners.Core.Tests.Diagnostics;

public class BoundedLogTests
{
    /// <summary>Fixed clock in a fixed zone, so tests do not depend on the host's clock or time zone.</summary>
    private sealed class FakeTimeProvider(DateTimeOffset utcNow, TimeZoneInfo zone) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override TimeZoneInfo LocalTimeZone => zone;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private static readonly DateTimeOffset Noon = new(2026, 3, 4, 12, 5, 6, 789, TimeSpan.Zero);

    private static FakeTimeProvider Clock(TimeZoneInfo? zone = null) => new(Noon, zone ?? TimeZoneInfo.Utc);

    [Fact]
    public void Add_FormatsWithLocalTimestampFromTheTimeProvider()
    {
        var log = new BoundedLog(timeProvider: Clock());
        log.Add("hello");
        Assert.Equal(["[2026-03-04 12:05:06.789] hello"], log.Snapshot());
    }

    [Fact]
    public void Add_UsesTheProvidersLocalZone()
    {
        var plusTwo = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");
        var log = new BoundedLog(timeProvider: Clock(plusTwo));
        log.Add("x");
        Assert.Equal("[2026-03-04 14:05:06.789] x", log.Snapshot()[0]);
    }

    [Fact]
    public void Add_PadsSmallComponents()
    {
        var clock = Clock();
        clock.UtcNow = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var log = new BoundedLog(timeProvider: clock);
        log.Add("x");
        Assert.Equal("[2026-01-02 03:04:05.006] x", log.Snapshot()[0]);
    }

    [Fact]
    public void Add_DefaultsToTheSystemClock()
    {
        var log = new BoundedLog();
        log.Add("x");
        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] x$", log.Snapshot()[0]);
        Assert.Equal(500, log.Capacity);
    }

    [Fact]
    public void Capacity_EvictsOldestFirst()
    {
        var log = new BoundedLog(3, Clock());
        foreach (var message in new[] { "a", "b", "c", "d", "e" })
        {
            log.Add(message);
        }

        Assert.Equal(["c", "d", "e"], log.Snapshot().Select(static e => e[(e.IndexOf(']') + 2)..]));
    }

    [Fact]
    public void CapacityOfOneKeepsOnlyTheLatest()
    {
        var log = new BoundedLog(1, Clock());
        log.Add("a");
        log.Add("b");
        Assert.Single(log.Snapshot());
        Assert.EndsWith("] b", log.Snapshot()[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveCapacityThrows(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedLog(capacity));
    }

    [Fact]
    public void ControlCharactersAreNeutralised()
    {
        var log = new BoundedLog(timeProvider: Clock());
        var hostile = "run calc.exe\r\n[2026-03-04 12:05:07.000] Settings approved" +
                      "\ttab\0nul" + (char)0x7F + (char)0x85 + (char)0x2028 + (char)0x2029 + (char)0x1B + "[31mred\n";
        log.Add(hostile);

        var entry = Assert.Single(log.Snapshot());
        Assert.Equal(
            "[2026-03-04 12:05:06.789] run calc.exe  [2026-03-04 12:05:07.000] Settings approved tab nul     [31mred ",
            entry);
        Assert.DoesNotContain(entry, char.IsControl);
    }

    [Fact]
    public void PrintableUnicodeIsKept()
    {
        var log = new BoundedLog(timeProvider: Clock());
        var text = "C:" + (char)0x5C + "Users" + (char)0x5C + "Zo" + (char)0xEB + " " + char.ConvertFromUtf32(0x1F600);
        log.Add(text);
        Assert.EndsWith("] " + text, log.Snapshot()[0]);
    }

    [Fact]
    public void NullOrEmptyMessageIsLoggedAsEmpty()
    {
        var log = new BoundedLog(timeProvider: Clock());
        log.Add(null!);
        log.Add(string.Empty);
        Assert.All(log.Snapshot(), static e => Assert.Equal("[2026-03-04 12:05:06.789] ", e));
    }

    [Fact]
    public void EntryAdded_RaisedOncePerEntryWithTheStoredText()
    {
        var log = new BoundedLog(2, Clock());
        var raised = new List<string>();
        log.EntryAdded += raised.Add;

        log.Add("one");
        log.Add("two\nthree");
        log.Add("four");

        Assert.Equal(3, raised.Count);
        Assert.Equal("[2026-03-04 12:05:06.789] two three", raised[1]);
        Assert.Equal(log.Snapshot(), raised.Skip(1));
    }

    [Fact]
    public void EntryAdded_IsRaisedOutsideTheLock()
    {
        var log = new BoundedLog(timeProvider: Clock());
        var otherThreadCompleted = false;
        var seenByOtherThread = -1;
        log.EntryAdded += _ =>
        {
            // If the lock were still held, a different thread could not take it and this would time out.
            using var done = new ManualResetEventSlim();
            var thread = new Thread(() => { seenByOtherThread = log.Snapshot().Count; done.Set(); });
            thread.Start();
            otherThreadCompleted = done.Wait(TimeSpan.FromSeconds(10));
            thread.Join();
        };

        log.Add("first");
        Assert.True(otherThreadCompleted);
        Assert.Equal(1, seenByOtherThread);
    }

    [Fact]
    public void Snapshot_IsACopy()
    {
        var log = new BoundedLog(timeProvider: Clock());
        log.Add("a");
        var snapshot = log.Snapshot();
        log.Add("b");
        Assert.Single(snapshot);
        Assert.Equal(2, log.Snapshot().Count);
    }

    [Fact]
    public void Clear_RemovesEverythingAndLoggingContinues()
    {
        var log = new BoundedLog(timeProvider: Clock());
        log.Add("a");
        log.Clear();
        Assert.Empty(log.Snapshot());
        log.Clear();
        log.Add("b");
        Assert.Single(log.Snapshot());
    }

    [Fact]
    public async Task ConcurrentAdds_EndWithExactlyCapacityEntries()
    {
        const int capacity = 100;
        const int threads = 8;
        const int perThread = 2_000;
        var log = new BoundedLog(capacity, Clock());
        var raised = 0;
        log.EntryAdded += _ => Interlocked.Increment(ref raised);

        using var start = new Barrier(threads);
        var workers = Enumerable.Range(0, threads).Select(thread => Task.Factory.StartNew(
            () =>
            {
                start.SignalAndWait();
                for (var i = 0; i < perThread; i++)
                {
                    log.Add($"{thread}:{i}");
                    if (i % 97 == 0)
                    {
                        Assert.InRange(log.Snapshot().Count, 1, capacity);
                    }
                }
            },
            TaskCreationOptions.LongRunning)).ToArray();

        await Task.WhenAll(workers);

        Assert.Equal(capacity, log.Snapshot().Count);
        Assert.Equal(threads * perThread, raised);

        // Per thread, surviving entries keep their relative order.
        foreach (var group in log.Snapshot().Select(static e => e[(e.IndexOf(']') + 2)..].Split(':'))
                     .GroupBy(static parts => parts[0]))
        {
            var sequence = group.Select(static parts => int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)).ToList();
            Assert.Equal(sequence.Order(), sequence);
        }
    }
}
