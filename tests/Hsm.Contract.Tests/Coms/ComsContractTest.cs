using System.Text.Json;

namespace Hsm.Contract.Tests.Coms;

/// <summary>
/// Base for the U14 coms contract tests: the shared plumbing plus a poll
/// helper for the background dispatcher's observable outcomes.
/// </summary>
public abstract class ComsContractTest(ComsApiFactory factory) : ContractTest<ComsApiFactory>(factory)
{
    /// <summary>Polls until the condition holds (the in-process dispatcher is asynchronous).</summary>
    protected static async Task WaitForAsync(Func<Task<bool>> condition, string what, int timeoutMs = 10000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Timed out waiting for: {what}");
    }

    protected async Task<JsonElement> GetBatchAsync(string bearer, string batchId)
    {
        var response = await Api.GetAsync(Client, $"/v1/coms/emails/batches/{batchId}", bearer: bearer);
        Assert.True(response.Status == 200, $"get batch failed: {response.RawBody}");
        return response.Data;
    }
}
