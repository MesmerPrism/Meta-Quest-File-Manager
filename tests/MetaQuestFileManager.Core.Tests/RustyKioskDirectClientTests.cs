using MetaQuestFileManager.Core;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace MetaQuestFileManager.Core.Tests;

public sealed class RustyKioskDirectClientTests
{
    private const string EmptySha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Fact]
    public void RequestSignature_MatchesAndroidCrossClientVector()
    {
        var signature = RustyKioskDirectAuth.SignRequest(
            "0123-4567-89AB-CDEF",
            "POST",
            "/v1/kiosk/invoke",
            "http_12345678",
            1_784_650_000L,
            EmptySha);

        Assert.Equal("f35ef975435590bf944f26e5055267d3615c6f1916f4a8b3986389900b588989", signature);
    }

    [Fact]
    public void ResponseSignature_MatchesAndroidCrossClientVector()
    {
        var signature = RustyKioskDirectAuth.SignResponse(
            "0123-4567-89AB-CDEF",
            "http_12345678",
            200,
            EmptySha);

        Assert.Equal("0a4418fe4677bfac1a12047ef8ea842e3ebaca7e758b8a190a4de009eaf9babb", signature);
    }

    [Fact]
    public void Endpoint_RequiresHttpAddressAndCompletePairingCode()
    {
        var endpoint = RustyKioskDirectEndpoint.Parse(
            "http://192.168.137.42:39873",
            "0123-4567-89AB-CDEF-0123-4567-89");

        Assert.Equal("http://192.168.137.42:39873/", endpoint.BaseUri.ToString());
        Assert.Throws<ArgumentException>(() => RustyKioskDirectEndpoint.Parse("https://example.com", "short"));
    }

    [Fact]
    public async Task Status_VerifiesSignedResponseBeforeReturningReadback()
    {
        const string code = "0123-4567-89AB-CDEF-0123-4567-89";
        var handler = new SignedResponseHandler(code, tamperBodyAfterSigning: false);
        var client = new RustyKioskDirectClient(
            RustyKioskDirectEndpoint.Parse("http://192.0.2.1:39873", code),
            new HttpClient(handler));

        var status = await client.GetStatusAsync();

        Assert.Equal(RustyKioskDirectClient.ContractSchema, status.Schema);
        Assert.True(status.InstallerAllowed);
        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.Contains("X-Rusty-Signature"));
    }

    [Fact]
    public async Task Status_RejectsBodyChangedAfterSignature()
    {
        const string code = "0123-4567-89AB-CDEF-0123-4567-89";
        var client = new RustyKioskDirectClient(
            RustyKioskDirectEndpoint.Parse("http://192.0.2.1:39873", code),
            new HttpClient(new SignedResponseHandler(code, tamperBodyAfterSigning: true)));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetStatusAsync());
    }

    private sealed class SignedResponseHandler(
        string pairingCode,
        bool tamperBodyAfterSigning) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            var requestId = request.Headers.GetValues("X-Rusty-Request-Id").Single();
            var signedBytes = Encoding.UTF8.GetBytes(
                "{\"accepted\":true,\"schema\":\"rusty.kiosk.direct_operator.v1\",\"endpoint\":\"http://192.0.2.1:39873\",\"installer_allowed\":true,\"staging_directory_kind\":\"app-owned\",\"message\":\"ready\"}");
            var returnedBytes = tamperBodyAfterSigning
                ? Encoding.UTF8.GetBytes("{\"accepted\":false,\"message\":\"tampered\"}")
                : signedBytes;
            var sha = Convert.ToHexString(SHA256.HashData(signedBytes)).ToLowerInvariant();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(returnedBytes)
            };
            response.Headers.TryAddWithoutValidation("X-Rusty-Request-Id", requestId);
            response.Headers.TryAddWithoutValidation("X-Rusty-Content-Sha256", sha);
            response.Headers.TryAddWithoutValidation(
                "X-Rusty-Signature",
                RustyKioskDirectAuth.SignResponse(pairingCode, requestId, 200, sha));
            return Task.FromResult(response);
        }
    }
}
