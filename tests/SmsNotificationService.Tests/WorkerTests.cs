using FluentAssertions;
using Moq;
using SmsNotificationService.Data;
using SmsNotificationService.Models;
using SmsNotificationService.Services;
using SmsNotificationService.Workers;
using Microsoft.Extensions.Logging;

namespace SmsNotificationService.Tests;

public class NotificationProcessorTests
{
    private readonly Mock<INotificationRepository> _repositoryMock = new();
    private readonly Mock<ISmsSender> _smsSenderMock = new();
    private readonly Mock<ILogger<NotificationProcessor>> _loggerMock = new();

    private NotificationProcessor CreateProcessor() => new(_loggerMock.Object, _repositoryMock.Object, _smsSenderMock.Object);

    [Fact]
    public async Task ProcessPending_NoPending_DoesNotSend()
    {
        _repositoryMock.Setup(r => r.GetPendingAsync())
            .ReturnsAsync(new List<SmsNotification>());

        var processor = CreateProcessor();
        await processor.ProcessPendingAsync(CancellationToken.None);

        _smsSenderMock.Verify(s => s.SendAsync(It.IsAny<SmsNotification>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPending_SendSuccess_UpdatesToProcessed()
    {
        var notification = new SmsNotification { Id = 1, PhoneNumber = "0712345678" };
        _repositoryMock.Setup(r => r.GetPendingAsync())
            .ReturnsAsync(new List<SmsNotification> { notification });
        _smsSenderMock.Setup(s => s.SendAsync(notification)).ReturnsAsync(SendResult.Ok());

        var processor = CreateProcessor();
        await processor.ProcessPendingAsync(CancellationToken.None);

        _repositoryMock.Verify(r => r.UpdateStatusAsync(1, NotificationStatus.PROCESSED), Times.Once);
    }

    [Fact]
    public async Task ProcessPending_SendFails_MaxRetriesExceeded_Cancels()
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
        _smsSenderMock.Setup(s => s.SendAsync(notification))
            .ReturnsAsync(SendResult.Fail("{\"error\":\"rate limit exceeded\"}"));

        var processor = CreateProcessor();
        await processor.ProcessPendingAsync(CancellationToken.None);

        _repositoryMock.Verify(r => r.UpdateStatusAsync(2, NotificationStatus.CANCELLED), Times.Once);
        _repositoryMock.Verify(r => r.UpdateDescriptionAsync(2, "{\"error\":\"rate limit exceeded\"}"), Times.Once);
    }

    [Fact]
    public async Task ProcessPending_SendFails_RetriesRemaining_SchedulesRetry()
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
        _smsSenderMock.Setup(s => s.SendAsync(notification))
            .ReturnsAsync(SendResult.Fail("{\"error\":\"temporary failure\"}", retryable: true));
        _smsSenderMock.Setup(s => s.CalculateRetryAfter(2))
            .Returns(DateTimeOffset.UtcNow.AddMinutes(5));

        var processor = CreateProcessor();
        await processor.ProcessPendingAsync(CancellationToken.None);

        _repositoryMock.Verify(r => r.UpdateRetryAsync(3, 2, It.IsAny<DateTimeOffset>()), Times.Once);
        _repositoryMock.Verify(r => r.UpdateDescriptionAsync(3, "{\"error\":\"temporary failure\"}"), Times.Once);
    }

    [Fact]
    public async Task ProcessPending_MultipleNotifications_ProcessesAll()
    {
        var notifications = new List<SmsNotification>
        {
            new() { Id = 10, PhoneNumber = "0711111111" },
            new() { Id = 11, PhoneNumber = "0722222222" },
            new() { Id = 12, PhoneNumber = "0733333333" }
        };
        _repositoryMock.Setup(r => r.GetPendingAsync())
            .ReturnsAsync(notifications);
        _smsSenderMock.Setup(s => s.SendAsync(It.IsAny<SmsNotification>())).ReturnsAsync(SendResult.Ok());

        var processor = CreateProcessor();
        await processor.ProcessPendingAsync(CancellationToken.None);

        _smsSenderMock.Verify(s => s.SendAsync(It.IsAny<SmsNotification>()), Times.Exactly(3));
        _repositoryMock.Verify(r => r.UpdateStatusAsync(It.IsAny<long>(), NotificationStatus.PROCESSED), Times.Exactly(3));
    }
}
