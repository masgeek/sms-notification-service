using FluentAssertions;
using Moq;
using SmsNotificationService.Data;
using SmsNotificationService.Models;
using SmsNotificationService.Services;
using SmsNotificationService.Workers;
using Microsoft.Extensions.Logging;

namespace SmsNotificationService.Tests;

public class WorkerTests
{
    private readonly Mock<INotificationRepository> _repositoryMock = new();
    private readonly Mock<ISmsSender> _smsSenderMock = new();
    private readonly Mock<ILogger<Worker>> _loggerMock = new();
    private readonly Mock<SqlDependencyListener> _listenerMock;

    public WorkerTests()
    {
        _listenerMock = new Mock<SqlDependencyListener>(
            "Server=test;Database=test;",
            Mock.Of<ILogger<SqlDependencyListener>>());
    }

    private Worker CreateWorker() => new(_loggerMock.Object, _repositoryMock.Object, _smsSenderMock.Object, _listenerMock.Object);

    [Fact]
    public async Task ProcessPendingNotifications_NoPending_DoesNotSend()
    {
        _repositoryMock.Setup(r => r.GetPendingAsync())
            .ReturnsAsync(new List<SmsNotification>());

        var worker = CreateWorker();
        var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        _smsSenderMock.Verify(s => s.SendAsync(It.IsAny<SmsNotification>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingNotifications_SendSuccess_UpdatesToProcessed()
    {
        var notification = new SmsNotification { Id = 1, PhoneNumber = "0712345678" };
        _repositoryMock.Setup(r => r.GetPendingAsync())
            .ReturnsAsync(new List<SmsNotification> { notification });
        _smsSenderMock.Setup(s => s.SendAsync(notification)).ReturnsAsync(true);

        var worker = CreateWorker();
        var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await worker.StopAsync(CancellationToken.None);

        _repositoryMock.Verify(r => r.UpdateStatusAsync(1, NotificationStatus.PROCESSED), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingNotifications_SendFails_MaxRetriesExceeded_Cancels()
    {
        var notification = new SmsNotification
        {
            Id = 2,
            PhoneNumber = "0712345678",
            RetryCount = 4,
            MaxRetries = 5
        };
        _repositoryMock.Setup(r => r.GetPendingAsync())
            .ReturnsAsync(new List<SmsNotification> { notification });
        _smsSenderMock.Setup(s => s.SendAsync(notification)).ReturnsAsync(false);

        var worker = CreateWorker();
        var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await worker.StopAsync(CancellationToken.None);

        _repositoryMock.Verify(r => r.UpdateStatusAsync(2, NotificationStatus.CANCELLED), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingNotifications_SendFails_RetriesRemaining_SchedulesRetry()
    {
        var notification = new SmsNotification
        {
            Id = 3,
            PhoneNumber = "0712345678",
            RetryCount = 1,
            MaxRetries = 5
        };
        _repositoryMock.Setup(r => r.GetPendingAsync())
            .ReturnsAsync(new List<SmsNotification> { notification });
        _smsSenderMock.Setup(s => s.SendAsync(notification)).ReturnsAsync(false);
        _smsSenderMock.Setup(s => s.CalculateRetryAfter(2))
            .Returns(DateTimeOffset.UtcNow.AddMinutes(5));

        var worker = CreateWorker();
        var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await worker.StopAsync(CancellationToken.None);

        _repositoryMock.Verify(r => r.UpdateRetryAsync(3, 2, It.IsAny<DateTimeOffset>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingNotifications_MultipleNotifications_ProcessesAll()
    {
        var notifications = new List<SmsNotification>
        {
            new() { Id = 10, PhoneNumber = "0711111111" },
            new() { Id = 11, PhoneNumber = "0722222222" },
            new() { Id = 12, PhoneNumber = "0733333333" }
        };
        _repositoryMock.Setup(r => r.GetPendingAsync())
            .ReturnsAsync(notifications);
        _smsSenderMock.Setup(s => s.SendAsync(It.IsAny<SmsNotification>())).ReturnsAsync(true);

        var worker = CreateWorker();
        var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        await Task.Delay(1000);
        await worker.StopAsync(CancellationToken.None);

        _smsSenderMock.Verify(s => s.SendAsync(It.IsAny<SmsNotification>()), Times.Exactly(3));
        _repositoryMock.Verify(r => r.UpdateStatusAsync(It.IsAny<long>(), NotificationStatus.PROCESSED), Times.Exactly(3));
    }
}
