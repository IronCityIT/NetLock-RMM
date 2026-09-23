using MySqlConnector;
using NetLock_RMM_Server.Events;
using Xunit;

namespace IronClad.Server.Tests;

public class Notification_Batch_Query_Tests
{
    [Fact]
    public void Pending_query_is_bounded_by_watermark()
    {
        string query = Notification_Batch.Pending_Events_Query("mail_status");

        Assert.Contains("`mail_status` = 0", query);
        Assert.Contains("`id` <= @watermark", query);
    }

    [Theory]
    [InlineData("mail_status`; DROP TABLE `events")]
    [InlineData("read")]
    [InlineData("")]
    public void Pending_query_rejects_unknown_columns(string column)
    {
        Assert.Throws<ArgumentException>(() => Notification_Batch.Pending_Events_Query(column));
    }

    [Fact]
    public void Mark_command_is_bounded_by_watermark_not_date()
    {
        Assert.Contains("WHERE `id` <= @watermark", Notification_Batch.Mark_Processed_Command);
        Assert.DoesNotContain("date", Notification_Batch.Mark_Processed_Command);
    }
}

// Runs the notification-run SQL against a real MySQL/MariaDB server.
// Connection string: ICS_TEST_MYSQL (defaults to the local unix socket as root).
public sealed class Notification_Batch_Db_Tests : IAsyncLifetime
{
    private readonly string _database = "iclad_test_" + Guid.NewGuid().ToString("N")[..12];
    private string _cs = string.Empty;

    public async Task InitializeAsync()
    {
        string server = Environment.GetEnvironmentVariable("ICS_TEST_MYSQL")
                        ?? "Server=/run/mysqld/mysqld.sock;Protocol=Unix;User ID=root;";

        await Exec(server, $"CREATE DATABASE `{_database}`;");
        _cs = new MySqlConnectionStringBuilder(server) { Database = _database }.ConnectionString;

        // Subset of the upstream `events` table used by the notification service
        await Exec(_cs, """
            CREATE TABLE `events` (
              `id` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              `date` DATETIME NOT NULL,
              `read` INT NOT NULL DEFAULT 0,
              `mail_status` INT NOT NULL DEFAULT 0,
              `ms_teams_status` INT NOT NULL DEFAULT 0,
              `telegram_status` INT NOT NULL DEFAULT 0,
              `ntfy_sh_status` INT NOT NULL DEFAULT 0,
              `webhook_status` INT NOT NULL DEFAULT 0
            );
            """);
    }

    public async Task DisposeAsync()
    {
        if (_cs.Length > 0)
            await Exec(_cs, $"DROP DATABASE IF EXISTS `{_database}`;");
    }

    [Fact]
    public async Task Legacy_date_based_marking_drops_event_inserted_mid_run()
    {
        // Reproduces the upstream defect so the fix below is known to address a real failure.
        await Insert_Event("2026-09-23 10:00:00");

        var delivered = new List<(string channel, long id)>();
        foreach (var channel in Notification_Batch.Channels.Keys)
        {
            foreach (long id in await Query_Ids($"SELECT id FROM `events` WHERE `{channel}` = 0 AND `read` = 0;"))
                delivered.Add((channel, id));

            if (channel == "mail_status")
                await Insert_Event("2026-09-23 10:00:01"); // agent reports an event while the run is in progress
        }

        await Exec(_cs, "UPDATE events SET mail_status = '1', ms_teams_status = '1', telegram_status = '1', ntfy_sh_status = '1', webhook_status = '1' WHERE date < '2026-09-23 10:00:02';");

        // Event 2 was never sent by mail, yet it is now marked as sent: the notification is lost.
        Assert.DoesNotContain(("mail_status", 2L), delivered);
        Assert.Empty(await Query_Ids("SELECT id FROM `events` WHERE `mail_status` = 0;"));
    }

    [Fact]
    public async Task Watermark_run_leaves_event_inserted_mid_run_for_next_run()
    {
        await Insert_Event("2026-09-23 10:00:00");

        var first = await Simulate_Run(on_channel_done: async channel =>
        {
            if (channel == "mail_status")
                await Insert_Event("2026-09-23 10:00:01");
        });

        // First run handles only event 1, on every channel
        Assert.All(first, d => Assert.Equal(1L, d.id));
        Assert.Equal(Notification_Batch.Channels.Count, first.Count);

        // Event 2 is untouched by the first run's mark step
        Assert.Equal(new[] { 2L }, await Query_Ids("SELECT id FROM `events` WHERE `mail_status` = 0;"));

        // Second run delivers event 2 on every channel, including mail
        var second = await Simulate_Run();
        Assert.Contains(("mail_status", 2L), second);
        Assert.Equal(Notification_Batch.Channels.Count, second.Count);
        Assert.Empty(await Query_Ids("SELECT id FROM `events` WHERE `mail_status` = 0 OR `webhook_status` = 0;"));
    }

    [Fact]
    public async Task Watermark_run_is_noop_on_empty_table()
    {
        var delivered = await Simulate_Run();
        Assert.Empty(delivered);
    }

    [Fact]
    public async Task Read_events_are_not_delivered_but_are_marked()
    {
        await Insert_Event("2026-09-23 10:00:00", read: 1);

        Assert.Empty(await Simulate_Run());
        Assert.Empty(await Query_Ids("SELECT id FROM `events` WHERE `mail_status` = 0;"));
    }

    // Mirrors Events_Notification_Service.ProcessEventsTask using the production SQL.
    private async Task<List<(string channel, long id)>> Simulate_Run(Func<string, Task>? on_channel_done = null)
    {
        var delivered = new List<(string channel, long id)>();

        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync();

        long watermark = Convert.ToInt64(await new MySqlCommand(Notification_Batch.Watermark_Query, conn).ExecuteScalarAsync());

        foreach (var channel in Notification_Batch.Channels.Keys)
        {
            var select = new MySqlCommand(Notification_Batch.Pending_Events_Query(channel), conn);
            select.Parameters.AddWithValue(Notification_Batch.Watermark_Parameter, watermark);

            var ids = new List<long>();
            await using (var reader = await select.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    ids.Add(Convert.ToInt64(reader["id"]));

            foreach (long id in ids)
            {
                delivered.Add((channel, id));
                await Exec(_cs, $"UPDATE `events` SET `{channel}` = 1 WHERE id = {id};");
            }

            if (on_channel_done != null)
                await on_channel_done(channel);
        }

        var mark = new MySqlCommand(Notification_Batch.Mark_Processed_Command, conn);
        mark.Parameters.AddWithValue(Notification_Batch.Watermark_Parameter, watermark);
        await mark.ExecuteNonQueryAsync();

        return delivered;
    }

    private Task Insert_Event(string date, int read = 0) =>
        Exec(_cs, $"INSERT INTO `events` (`date`, `read`) VALUES ('{date}', {read});");

    private async Task<List<long>> Query_Ids(string sql)
    {
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync();
        var ids = new List<long>();
        await using var reader = await new MySqlCommand(sql, conn).ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(Convert.ToInt64(reader[0]));
        return ids;
    }

    private static async Task Exec(string cs, string sql)
    {
        await using var conn = new MySqlConnection(cs);
        await conn.OpenAsync();
        await new MySqlCommand(sql, conn).ExecuteNonQueryAsync();
    }
}
