using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Requests;

public class Events_Notification_Service : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessEventsTask();
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                Logging.Handler.Debug("Events_Notification_Service.ExecuteAsync", "Task canceled: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                // The task was completed cleanly
                break;
            }
            catch (Exception ex)
            {
                Logging.Handler.Error("EventsNotificationService", "ExecuteAsync", ex.ToString());
            }
        }
    }

    private async Task ProcessEventsTask()
    {
        string startedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Logging.Handler.Debug("Events_Notification_Service.ProcessEventsTask", "Periodic task executed at: ", startedTime);

        try
        {
            // Snapshot the newest event id; this run only handles (and later marks) events up to it
            long watermark = await NetLock_RMM_Server.Events.Sender.Get_Watermark();

            foreach (var channel in NetLock_RMM_Server.Events.Notification_Batch.Channels)
                await NetLock_RMM_Server.Events.Sender.Smtp(channel.Key, channel.Value, watermark);

            string finishedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Logging.Handler.Debug("Events_Notification_Service.ProcessEventsTask", "Periodic task finished at: ", finishedTime);

            // Markiere alte Events als gelesen
            await NetLock_RMM_Server.Events.Sender.Mark_Old_Read(watermark);
        }
        catch (Exception ex)
        {
            Logging.Handler.Error("EventsNotificationService", "ProcessEventsTask", ex.ToString());
        }
    }
}