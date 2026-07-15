using FluentAssertions;
using Moq;
using Moq.Protected;
using SmsNotificationService.Configuration;
using SmsNotificationService.Models;
using SmsNotificationService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

namespace SmsNotificationService.Tests;

public class SmsApiServiceTests
{
    private readonly Mock<ILogger<SmsApiService>> _loggerMock = new();

    private SmsApiService CreateService(HttpMessageHandler handler, int retryBackoffSeconds = 30)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var options = Options.Create(new SmsServiceOptions
        {
            SmsApiUrl = "https://api.test.com/send",
            AuthorizationToken = "test-token",
            RetryBackoffSeconds = retryBackoffSeconds
        });

        return new SmsApiService(_loggerMock.Object, factoryMock.Object, options);
    }

    private static SmsNotification CreateNotification() => new()
    {
        Id = 1,
        PhoneNumber = "0712345678",
        MpesaCode = "QA12BC34",
        AdmNo = "1001",
        StudNames = "Test Student",
        Amount = 1500.00m,
        ReceiptNo = "RCP001",
        Dated = new DateTime(2026, 1, 1)
    };

    [Fact]
    public async Task SendAsync_Success_ReturnsTrue()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var service = CreateService(handler.Object);
        var result = await service.SendAsync(CreateNotification());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_ServerError_RetriesAndReturnsFalse()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("error")
            });

        var service = CreateService(handler.Object);
        var result = await service.SendAsync(CreateNotification());

        result.Should().BeFalse();
        handler.Protected().Verify("SendAsync", Times.Exactly(3),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_Exception_RetriesAndReturnsFalse()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var service = CreateService(handler.Object);
        var result = await service.SendAsync(CreateNotification());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_SecondAttemptSucceeds_ReturnsTrue()
    {
        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : new HttpResponseMessage(HttpStatusCode.OK);
            });

        var service = CreateService(handler.Object);
        var result = await service.SendAsync(CreateNotification());

        result.Should().BeTrue();
        handler.Protected().Verify("SendAsync", Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public void CalculateRetryAfter_Retry1_ReturnsBaseBackoff()
    {
        var service = CreateService(new Mock<HttpMessageHandler>().Object, retryBackoffSeconds: 30);
        var result = service.CalculateRetryAfter(1);
        var expected = TimeSpan.FromSeconds(30);

        result.Should().BeCloseTo(DateTimeOffset.UtcNow.Add(expected), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CalculateRetryAfter_Retry2_ReturnsDoubleBackoff()
    {
        var service = CreateService(new Mock<HttpMessageHandler>().Object, retryBackoffSeconds: 30);
        var result = service.CalculateRetryAfter(2);
        var expected = TimeSpan.FromSeconds(60);

        result.Should().BeCloseTo(DateTimeOffset.UtcNow.Add(expected), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CalculateRetryAfter_Retry3_ReturnsQuadrupleBackoff()
    {
        var service = CreateService(new Mock<HttpMessageHandler>().Object, retryBackoffSeconds: 30);
        var result = service.CalculateRetryAfter(3);
        var expected = TimeSpan.FromSeconds(120);

        result.Should().BeCloseTo(DateTimeOffset.UtcNow.Add(expected), TimeSpan.FromSeconds(5));
    }
}
