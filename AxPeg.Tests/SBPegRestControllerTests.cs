using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using AxPeg.Services.Interfaces;
using AxPeg.Dtos.Request;
using AxPeg.Dtos.Response;

namespace AxPeg.Tests
{
    public class SBPegRestControllerTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly Mock<IAxPegService> _mockPegService = new();
        private readonly Mock<IAxPegActionsService> _mockActionsService = new();

        public SBPegRestControllerTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Register the mocks into the DI container for testing
                    services.AddScoped(_ => _mockPegService.Object);
                    services.AddScoped(_ => _mockActionsService.Object);
                });
            });
        }

        [Fact]
        public async Task CanInitiate_ReturnsOk_WhenServiceReturnsTrue()
        {
            // Arrange
            _mockPegService.Setup(x => x.CanInitiatePEGAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var client = _factory.CreateClient();
            var request = new InitiateRequest
            {
                AppName = "TestApp",
                ProcessName = "TestProcess",
                TaskName = "TestTask",
                IndexNo = "1",
                KeyValue = "TestKey"
            };

            // Act
            var response = await client.PostAsJsonAsync("/api/v1/SBPegRest/CanInitiate", request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<ServiceResponse<bool>>();
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.True(result.Data);
        }

        [Fact]
        public async Task CanInitiate_ReturnsBadRequest_WhenServiceReturnsFalse()
        {
            // Arrange
            _mockPegService.Setup(x => x.CanInitiatePEGAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            var client = _factory.CreateClient();
            var request = new InitiateRequest { AppName = "TestApp" };

            // Act
            var response = await client.PostAsJsonAsync("/api/v1/SBPegRest/CanInitiate", request);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Approve_ReturnsOk_WhenApproveSucceeds()
        {
            // Arrange
            _mockActionsService.Setup(x => x.ApproveTaskAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var client = _factory.CreateClient();
            var request = new ActionRequest { AppName = "TestApp", TaskId = "123", UserName = "John" };

            // Act
            var response = await client.PostAsJsonAsync("/api/v1/SBPegRest/Approve", request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<ServiceResponse<bool>>();
            Assert.NotNull(result);
            Assert.True(result.Success);
        }

        [Fact]
        public async Task Reject_ReturnsBadRequest_WhenRejectFails()
        {
            // Arrange
            _mockActionsService.Setup(x => x.RejectTaskAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            var client = _factory.CreateClient();
            var request = new ActionRequest { AppName = "TestApp", TaskId = "123", UserName = "John" };

            // Act
            var response = await client.PostAsJsonAsync("/api/v1/SBPegRest/Reject", request);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
