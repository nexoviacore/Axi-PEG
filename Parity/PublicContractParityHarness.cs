using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AxPeg.Dtos.Request;

namespace AxPeg.Parity;

public sealed record PublicContractParityResult(string ContractPath, bool IsMatch, HttpStatusCode LegacyStatusCode, HttpStatusCode AxPegStatusCode);

public static class PublicContractParityHarness
{
    public const string CanInitiatePath = "/api/v1/SBPegRest/CanInitiate";

    public static async Task<PublicContractParityResult> CompareCanInitiateAsync(
        HttpClient legacyClient,
        HttpClient axPegClient,
        InitiateRequest fixture,
        CancellationToken cancellationToken = default)
    {
        using var legacyResponse = await legacyClient.PostAsJsonAsync(CanInitiatePath, fixture, cancellationToken);
        using var axPegResponse = await axPegClient.PostAsJsonAsync(CanInitiatePath, fixture, cancellationToken);

        var legacyPayload = await legacyResponse.Content.ReadAsStringAsync(cancellationToken);
        var axPegPayload = await axPegResponse.Content.ReadAsStringAsync(cancellationToken);

        return new PublicContractParityResult(
            CanInitiatePath,
            legacyResponse.StatusCode == axPegResponse.StatusCode && JsonPayloadsMatch(legacyPayload, axPegPayload),
            legacyResponse.StatusCode,
            axPegResponse.StatusCode);
    }

    private static bool JsonPayloadsMatch(string legacyPayload, string axPegPayload)
    {
        using var legacyDocument = JsonDocument.Parse(legacyPayload);
        using var axPegDocument = JsonDocument.Parse(axPegPayload);
        return JsonElement.DeepEquals(legacyDocument.RootElement, axPegDocument.RootElement);
    }
}
