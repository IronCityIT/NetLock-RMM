using System;
using System.Globalization;

namespace Global.Sensors
{
    // Iron Clad Support: culture-safe handling of scheduler timestamps.
    //
    // Upstream wrote `last_run` with InvariantCulture after every execution but several schedule
    // types parsed it with the machine culture. On non-US locales (e.g. de-DE) that throws a
    // FormatException for days > 12 or silently swaps day and month otherwise. The exception is
    // caught by the per-sensor handler, which deletes the sensor file, so monitoring stopped.
    // The "date & time" schedule also parsed `time_scheduler_date` as dd.MM.yyyy while the web
    // console stores yyyy-MM-dd, so those sensors failed (and were deleted) on every check.
    //
    // Kept free of agent dependencies so it can be compiled into the test project directly.
    public static class Schedule_Time
    {
        // InvariantCulture "G" pattern; the format upstream already uses for most writes.
        public const string Last_Run_Format = "MM/dd/yyyy HH:mm:ss";

        // yyyy-MM-dd: sensors/jobs dialogs. Scan-job dialogs store DateTime.ToString() in the web
        // console request culture, which is en-US (M/d/yyyy) or de-DE (dd.MM.yyyy). The console
        // never writes day-first dates with slashes, so M/d/yyyy is unambiguous here.
        private static readonly string[] Schedule_Date_Formats = { "yyyy-MM-dd", "dd.MM.yyyy", "M/d/yyyy" };

        public static string Format(DateTime value) => value.ToString(Last_Run_Format, CultureInfo.InvariantCulture);

        public static string Now() => Format(DateTime.Now);

        // Accepts the invariant format first, then values written by older agents with the
        // machine culture.
        public static bool TryParse_Last_Run(string? value, out DateTime result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            return DateTime.TryParseExact(value, Last_Run_Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result)
                || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out result)
                || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        // Rewrites a stored last_run into the invariant format. Unreadable values become empty,
        // which every schedule type treats as "never run" instead of throwing.
        public static string? Normalize_Last_Run(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return TryParse_Last_Run(value, out DateTime parsed) ? Format(parsed) : string.Empty;
        }

        public static DateTime Parse_Last_Run(string value) =>
            TryParse_Last_Run(value, out DateTime result) ? result : throw new FormatException("Unsupported last_run: " + value);

        // time_scheduler_date may carry a time suffix ("2026-09-23 00:00:00"); only the date is used.
        public static DateTime Parse_Schedule_Date(string value)
        {
            string date = (value ?? string.Empty).Trim().Split(' ')[0];

            if (DateTime.TryParseExact(date, Schedule_Date_Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
                return result;

            throw new FormatException("Unsupported time_scheduler_date: " + value);
        }

        // Half-open window [from, to) of events a run must evaluate: everything since the previous
        // run. `to` is truncated to the stored precision so it equals the next run's `from`, which
        // tiles consecutive runs without gaps or overlap. Without a readable previous run the
        // window is empty, so a sensor never replays the whole log history at once.
        public static (DateTime from, DateTime to) Since_Previous_Run(string? previous_last_run, DateTime run_start)
        {
            DateTime to = Parse_Last_Run(Format(run_start));

            if (TryParse_Last_Run(previous_last_run, out DateTime from) && from < to)
                return (from, to);

            return (to, to);
        }

        public static DateTime Parse_Schedule_Date_Time(string date, string time) =>
            Parse_Schedule_Date(date).Add(TimeSpan.ParseExact(time, @"hh\:mm\:ss", CultureInfo.InvariantCulture));
    }
}
