using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using BioGuard.Api.Services;
using FluentAssertions;

namespace Test1BioGuard.UnitTest;

public class EmailServiceTests
{
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<EmailService>> _mockLogger;
    private readonly EmailService _service;

    public EmailServiceTests()
    {
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<EmailService>>();
        _service = new EmailService(_mockConfig.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task SendVerificationCodeAsync_SmtpNoConfigurado_RetornaFalse()
    {
        // Arrange
        _mockConfig.Setup(c => c["Smtp:Host"]).Returns("");
        _mockConfig.Setup(c => c["Smtp:User"]).Returns("");
        _mockConfig.Setup(c => c["Smtp:Password"]).Returns("");

        // Act
        var result = await _service.SendVerificationCodeAsync("test@example.com", "Juan", "123456");

        // Assert
        result.Should().BeFalse();
    }
}
