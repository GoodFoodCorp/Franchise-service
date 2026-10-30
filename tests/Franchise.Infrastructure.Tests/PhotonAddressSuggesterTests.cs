using System.Net;
using System.Text;
using FluentAssertions;
using Franchise.Application.Common;
using Franchise.Domain.ValueObjects;
using Franchise.Infrastructure.Geocoding;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Franchise.Infrastructure.Tests;

public class PhotonAddressSuggesterTests
{
    private const string Answer = """
    {"features":[
      {"geometry":{"coordinates":[2.3290,48.8466]},"properties":{"type":"house","housenumber":"10","street":"Rue de Rennes","postcode":"75006","city":"Paris","country":"France","countrycode":"FR"}},
      {"geometry":{"coordinates":[13.3777,52.5163]},"properties":{"type":"house","housenumber":"1","street":"Pariser Platz","postcode":"10117","city":"Berlin","country":"Deutschland","countrycode":"DE"}},
      {"geometry":{"coordinates":[4.3517,50.8466]},"properties":{"type":"street","name":"Rue Neuve","postcode":"1000","city":"Bruxelles","country":"Belgique","countrycode":"BE"}},
      {"geometry":{"coordinates":[-0.1276,51.5072]},"properties":{"type":"house","housenumber":"10","street":"Downing Street","postcode":"SW1A","city":"London","country":"United Kingdom","countrycode":"GB"}},
      {"geometry":{"coordinates":[2.35,48.85]},"properties":{"type":"city","name":"Paris","country":"France","countrycode":"FR"}},
      {"geometry":{"coordinates":[2.3290,48.8466]},"properties":{"type":"house","housenumber":"10","street":"Rue de Rennes","postcode":"75006","city":"Paris","country":"France","countrycode":"FR"}}
    ]}
    """;

    private static (PhotonAddressSuggester Suggester, StubHandler Handler) Build(HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler(status, Answer);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://photon.test/") };
        var suggester = new PhotonAddressSuggester(http, new MemoryCache(new MemoryCacheOptions()),
            ServedCountries.FromSetting(null), NullLogger<PhotonAddressSuggester>.Instance);
        return (suggester, handler);
    }

    [Fact]
    public async Task Keeps_street_addresses_of_the_EU_once_written_the_local_way()
    {
        var (suggester, _) = Build();

        var result = await suggester.SuggestAsync("rue", null, 5);

        result.Select(s => s.Label).Should().Equal(
            "10 Rue de Rennes, 75006 Paris, France",
            "Pariser Platz 1, 10117 Berlin, Deutschland",
            "Rue Neuve, 1000 Bruxelles, Belgique");
        result[1].CountryCode.Should().Be("DE");
        result[0].Latitude.Should().Be(48.8466);
        result[0].Longitude.Should().Be(2.3290);
    }

    [Fact]
    public async Task Biases_on_the_given_point_and_caches_the_answer()
    {
        var (suggester, handler) = Build();

        await suggester.SuggestAsync("rue de rennes", new GeoPoint(48.8429, 2.3241), 5);
        await suggester.SuggestAsync("rue de rennes", new GeoPoint(48.8429, 2.3241), 5);

        handler.Calls.Should().Be(1);
        handler.LastUri!.Query.Should().Contain("lat=48.843").And.Contain("lon=2.324").And.Contain("q=rue%20de%20rennes")
            .And.Contain("bbox=1.924,48.573,2.724,49.113");
    }

    [Fact]
    public async Task A_provider_outage_is_reported_as_unavailable()
    {
        var (suggester, _) = Build(HttpStatusCode.ServiceUnavailable);

        var act = () => suggester.SuggestAsync("rue de rennes", null, 5);

        await act.Should().ThrowAsync<GeocodingUnavailableException>();
    }

    [Fact]
    public void Served_countries_default_to_the_27_EU_members()
    {
        ServedCountries.FromSetting("").Codes.Should().HaveCount(27).And.Contain(["fr", "de", "es", "it", "pl"]);
        ServedCountries.FromSetting("FR, be,xx1").Codes.Should().Equal("fr", "be");
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
