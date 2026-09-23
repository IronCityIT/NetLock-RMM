using System;
using System.Collections.Generic;

namespace NetLock_RMM_Server.Events
{
    // Iron Clad Support: bounds each notification run to a fixed set of events.
    //
    // Upstream selected unsent events per channel and afterwards marked every event with
    // `date < finished_time` as sent. Events inserted while a run was in progress (after a
    // channel's SELECT, but before the run finished) were therefore marked as sent without
    // ever being delivered. We snapshot MAX(id) at the start of a run and use it as the upper
    // bound for both the per-channel SELECT and the final "mark processed" UPDATE, so anything
    // inserted mid-run is left untouched and picked up by the next run.
    //
    // Kept free of server dependencies so it can be compiled into the test project directly.
    public static class Notification_Batch
    {
        // Channel status column -> notification recipient table. Column names cannot be SQL
        // parameters, so dynamic SQL is only ever built from these keys.
        public static readonly IReadOnlyDictionary<string, string> Channels = new Dictionary<string, string>
        {
            { "mail_status", "mail_notifications" },
            { "ms_teams_status", "microsoft_teams_notifications" },
            { "telegram_status", "telegram_notifications" },
            { "ntfy_sh_status", "ntfy_sh_notifications" },
            { "webhook_status", "webhook_notifications" },
        };

        // Channels enabled for server-generated uptime events (device connected / disconnected).
        // Keys match Sender.Notifications. Upstream omitted "webhook", so webhook recipients with
        // "Uptime Monitoring" enabled never received device offline/online alerts.
        public const string Uptime_Notification_Json = @"{""mail"":true,""microsoft_teams"":true,""telegram"":true,""ntfy_sh"":true,""webhook"":true}";

        public const string Watermark_Parameter = "@watermark";

        public const string Watermark_Query = "SELECT COALESCE(MAX(`id`), 0) FROM `events`;";

        public static string Pending_Events_Query(string status_column)
        {
            if (!Channels.ContainsKey(status_column))
                throw new ArgumentException("Unknown notification status column: " + status_column, nameof(status_column));

            return $"SELECT * FROM `events` WHERE `{status_column}` = 0 AND `read` = 0 AND `id` <= {Watermark_Parameter};";
        }

        public const string Mark_Processed_Command =
            "UPDATE `events` SET mail_status = 1, ms_teams_status = 1, telegram_status = 1, ntfy_sh_status = 1, webhook_status = 1 WHERE `id` <= " + Watermark_Parameter + ";";
    }
}
