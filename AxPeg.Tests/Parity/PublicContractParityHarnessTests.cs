using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AxPeg.Dtos.Request;
using AxPeg.Parity;
using Xunit;

namespace AxPeg.Tests.Parity;

public class PublicContractParityHarnessTests
{
    [Fact]
    public async Task CompareCanInitiateAsync_reports_matching_public_contracts()
    {
        var request = new InitiateRequest
        {
            AppName = "TestApp",
            ProcessName = "TestProcess",
            TaskName = "TestTask",
            IndexNo = "1",
            KeyValue = "TestKey"
        };
        using var legacy = CreateClient(HttpStatusCode.OK,
            "{\"success\":true,\"message\":\"Can initiate PEG task.\",\"data\":true}");
        using var axPeg = CreateClient(HttpStatusCode.OK,
            "{\"data\":true,\"message\":\"Can initiate PEG task.\",\"success\":true}");

        var result = await PublicContractParityHarness.CompareCanInitiateAsync(legacy, axPeg, request);

        Assert.True(result.IsMatch);
        Assert.Equal("/api/v1/SBPegRest/CanInitiate", result.ContractPath);
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            });
    }

    private static HttpClient CreateClient(HttpStatusCode statusCode, string payload) => new(new StaticResponseHandler(statusCode, payload))
    {
        BaseAddress = new Uri("https://parity.test")
    };
}
