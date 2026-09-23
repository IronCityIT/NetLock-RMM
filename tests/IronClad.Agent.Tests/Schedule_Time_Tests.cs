using System.Globalization;
using Global.Sensors;
using Xunit;

namespace IronClad.Agent.Tests;

// Runs each test body under a specific machine culture, as the agent service would.
public sealed class Culture_Scope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public Culture_Scope(string name) => CultureInfo.CurrentCulture = new CultureInfo(name);

    public void Dispose() => CultureInfo.CurrentCulture = _culture;
}

public class Legacy_Behavior_Reproduction
{
    private static readonly DateTime Sep_23 = new(2026, 9, 23, 17, 5, 0);
    private static readonly DateTime Mar_9 = new(2026, 3, 9, 17, 5, 0);

    [Fact]
    public void Invariant_last_run_parsed_with_german_culture_throws()
    {
        // Upstream: last_run = startTime.ToString(InvariantCulture); later DateTime.Parse(last_run)
        string stored = Sep_23.ToString(CultureInfo.InvariantCulture);

        using var _ = new Culture_Scope("de-DE");
        Assert.Throws<FormatException>(() => DateTime.Parse(stored));
    }

    [Fact]
    public void Invariant_last_run_parsed_with_german_culture_swaps_day_and_month()
    {
        string stored = Mar_9.ToString(CultureInfo.InvariantCulture); // "03/09/2026 17:05:00"

        using var _ = new Culture_Scope("de-DE");
        Assert.Equal(new DateTime(2026, 9, 3, 17, 5, 0), DateTime.Parse(stored));
    }

    [Fact]
    public void Date_and_time_schedule_rejects_web_console_date_format()
    {
        // Upstream type 1: ParseExact($"{date.Split(' ')[0]} {time}", "dd.MM.yyyy HH:mm:ss")
        // Web console stores time_scheduler_date as yyyy-MM-dd.
        Assert.Throws<FormatException>(() =>
            DateTime.ParseExact("2026-09-23 17:00:00", "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
    }
}

public class Schedule_Time_Tests
{
    public static TheoryData<string> Cultures => new() { "de-DE", "en-GB", "en-US", "fr-FR", "ja-JP" };

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Last_run_round_trips_in_every_culture(string culture)
    {
        using var _ = new Culture_Scope(culture);

        foreach (var value in new[] { new DateTime(2026, 9, 23, 17, 5, 1), new DateTime(2026, 3, 9, 1, 2, 3) })
        {
            string stored = Schedule_Time.Format(value);
            Assert.Equal(value, Schedule_Time.Parse_Last_Run(stored));
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Existing_invariant_last_run_is_read_correctly(string culture)
    {
        // Value written by upstream's execute path: startTime.ToString(InvariantCulture)
        string stored = new DateTime(2026, 3, 9, 17, 5, 0).ToString(CultureInfo.InvariantCulture);

        using var _ = new Culture_Scope(culture);
        Assert.Equal(new DateTime(2026, 3, 9, 17, 5, 0), Schedule_Time.Parse_Last_Run(stored));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void Legacy_culture_written_last_run_is_read_with_machine_culture(string culture)
    {
        using var _ = new Culture_Scope(culture);

        // Upstream first-run init: DateTime.Now.ToString() (machine culture); day > 12 is unambiguous
        var value = new DateTime(2026, 9, 23, 17, 5, 0);
        string stored = value.ToString();

        Assert.Equal(value, Schedule_Time.Parse_Last_Run(stored));
        Assert.Equal(Schedule_Time.Format(value), Schedule_Time.Normalize_Last_Run(stored));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("not a date", "")]
    public void Normalize_turns_unreadable_values_into_never_run(string? stored, string? expected)
    {
        using var _ = new Culture_Scope("de-DE");
        Assert.Equal(expected, Schedule_Time.Normalize_Last_Run(stored));
    }

    [Theory]
    [InlineData("2026-09-23")]
    [InlineData("2026-09-23 00:00:00")]
    [InlineData("23.09.2026")]
    [InlineData("23.09.2026 00:00:00")]
    [InlineData("9/23/2026 12:00:00 AM")] // scan-job dialog, en-US console
    [InlineData("09/23/2026 00:00:00")]
    public void Schedule_date_accepts_web_console_and_legacy_formats(string stored)
    {
        foreach (var culture in new[] { "de-DE", "en-US" })
        {
            using var _ = new Culture_Scope(culture);
            Assert.Equal(new DateTime(2026, 9, 23), Schedule_Time.Parse_Schedule_Date(stored));
        }
    }

    [Fact]
    public void Schedule_date_time_combines_date_and_time()
    {
        Assert.Equal(new DateTime(2026, 9, 23, 17, 30, 0), Schedule_Time.Parse_Schedule_Date_Time("2026-09-23", "17:30:00"));
    }

    [Fact]
    public void Slash_schedule_dates_are_month_first()
    {
        Assert.Equal(new DateTime(2026, 3, 9), Schedule_Time.Parse_Schedule_Date("3/9/2026 2:30:00 PM"));
    }

    [Fact]
    public void Scan_job_date_time_from_en_us_console_is_accepted()
    {
        // Upstream: ParseExact("9/23/2026 14:30:00", "dd.MM.yyyy HH:mm:ss") threw for every en-US scan job
        Assert.Throws<FormatException>(() =>
            DateTime.ParseExact("9/23/2026 14:30:00", "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));

        Assert.Equal(new DateTime(2026, 9, 23, 14, 30, 0), Schedule_Time.Parse_Schedule_Date_Time("9/23/2026 2:30:00 PM", "14:30:00"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026/09/23")]
    [InlineData("23/09/2026")]
    public void Schedule_date_rejects_unsupported_or_empty_values(string stored)
    {
        Assert.Throws<FormatException>(() => Schedule_Time.Parse_Schedule_Date(stored));
    }

    [Fact]
    public void Event_window_covers_everything_since_previous_run()
    {
        string previous = Schedule_Time.Format(new DateTime(2026, 9, 23, 17, 0, 0));
        var run_start = new DateTime(2026, 9, 23, 17, 5, 0, 750);

        var (from, to) = Schedule_Time.Since_Previous_Run(previous, run_start);

        Assert.Equal(new DateTime(2026, 9, 23, 17, 0, 0), from);
        Assert.Equal(new DateTime(2026, 9, 23, 17, 5, 0), to);
    }

    [Fact]
    public void Consecutive_event_windows_tile_without_gap_or_overlap()
    {
        var first_start = new DateTime(2026, 9, 23, 17, 5, 0, 750);
        string stored_after_first = Schedule_Time.Format(first_start); // what the agent persists as last_run

        var (_, first_to) = Schedule_Time.Since_Previous_Run(Schedule_Time.Format(new DateTime(2026, 9, 23, 17, 0, 0)), first_start);
        var (second_from, _) = Schedule_Time.Since_Previous_Run(stored_after_first, new DateTime(2026, 9, 23, 17, 10, 0));

        Assert.Equal(first_to, second_from);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("09/23/2026 18:00:00")] // previous run in the future (clock change)
    public void Event_window_is_empty_without_a_usable_previous_run(string? previous)
    {
        var (from, to) = Schedule_Time.Since_Previous_Run(previous, new DateTime(2026, 9, 23, 17, 5, 0));
        Assert.Equal(from, to);
    }

    [Fact]
    public void Legacy_event_window_was_empty()
    {
        // Upstream: startTime = DateTime.Now; endTime = DateTime.Now; query [startTime, endTime]
        var startTime = DateTime.Now;
        var endTime = DateTime.Now;
        Assert.True(endTime - startTime < TimeSpan.FromSeconds(1));
    }
}
