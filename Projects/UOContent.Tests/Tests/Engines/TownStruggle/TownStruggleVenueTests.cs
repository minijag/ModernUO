using System;
using System.Text.Json;
using Server;
using Server.Engines.TownStruggle;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

[Collection("Sequential UOContent Tests")]
public class TownStruggleVenueTests
{
    [Fact]
    public void ThreeShardTownsShareOneLobbyAndRetainIndependentBattlefields()
    {
        var inputs = new[] { Venue("Galven", 1145, 1273), Venue("Albus", 1164, 827), Venue("Bowan", 800, 1693) };
        var venues = TownStruggleVenue.ParseCatalog(Catalog(inputs));
        Assert.Equal(3, venues.Length);
        Assert.Equal("Galven", venues[0].Name);
        Assert.Equal("Albus", venues[1].Name);
        Assert.Equal("Bowan", venues[2].Name);
        foreach (var venue in venues)
        {
            Assert.Same(Map.Felucca, venue.Map);
            Assert.Equal(new Point3D(5396, 820, 0), venue.NeutralBase);
            Assert.Equal(3, venue.CapturePoints.Count);
            Assert.Equal(3, venue.RespawnPoints.Count);
            Assert.True(venue.Area.Contains(venue.OrderBase));
            Assert.True(venue.Area.Contains(venue.ChaosBase));
        }
        Assert.NotEqual(venues[0].Area, venues[1].Area);
        Assert.NotEqual(venues[1].Area, venues[2].Area);
    }

    [Fact]
    public void LegacySingleVenueRemainsAccepted()
    {
        var venue = Assert.Single(TownStruggleVenue.ParseCatalog(JsonSerializer.Serialize(Venue("Galven"))));
        Assert.Equal("Galven", venue.Name);
    }

    [Fact]
    public void RestrictedFacetCannotSilentlyDisableSelectiveCorpseLooting()
    {
        var input = Venue("Restricted") with { MapId = Map.Trammel.MapIndex };
        Assert.Throws<ArgumentException>(() => TownStruggleVenue.Parse(JsonSerializer.Serialize(input)));
    }

    [Theory]
    [InlineData("Galven")]
    [InlineData("galven")]
    [InlineData("GALVEN")]
    public void DuplicateTownNamesAreRejectedCaseInsensitively(string secondName)
    {
        Assert.Throws<ArgumentException>(() => TownStruggleVenue.ParseCatalog(Catalog(Venue("Galven"), Venue(secondName))));
    }

    [Fact]
    public void EveryTownMustUseTheSameNeutralBaseAndMap()
    {
        var first = Venue("Galven");
        var second = Venue("Albus") with { NeutralBase = new(5397, 820, 0) };
        Assert.Throws<ArgumentException>(() => TownStruggleVenue.ParseCatalog(Catalog(first, second)));
        second = Venue("Albus") with { MapId = 1 };
        Assert.Throws<ArgumentException>(() => TownStruggleVenue.ParseCatalog(Catalog(first, second)));
    }

    [Theory]
    [InlineData("{\"venues\":[]}")]
    [InlineData("{\"venues\":null}")]
    [InlineData("{\"venues\":{}}")]
    public void CatalogMustContainAnArrayOfVenues(string json)
    {
        Assert.Throws<ArgumentException>(() => TownStruggleVenue.ParseCatalog(json));
    }

    [Fact]
    public void CatalogIsBoundedInCountAndPayloadSize()
    {
        var venues = new TownStruggleVenue.VenueFile[17];
        for (var i = 0; i < venues.Length; i++)
        {
            venues[i] = Venue($"Town {i}");
        }
        Assert.Throws<ArgumentException>(() => TownStruggleVenue.ParseCatalog(Catalog(venues)));
        Assert.Throws<ArgumentException>(() => TownStruggleVenue.ParseCatalog(new string(' ', 32769)));
    }

    [Fact]
    public void OneInvalidVenueRejectsEntireCatalog()
    {
        var invalid = Venue("Albus") with { CapturePoints = [new(105, 105, 0), new(140, 140, 0), new(170, 140, 0)] };
        Assert.Throws<ArgumentException>(() => TownStruggleVenue.ParseCatalog(Catalog(Venue("Galven"), invalid)));
        invalid = Venue("Albus") with { RespawnPoints = [new(6000, 4000, 0), new(140, 190, 0), new(170, 190, 0)] };
        Assert.Throws<ArgumentException>(() => TownStruggleVenue.ParseCatalog(Catalog(Venue("Galven"), invalid)));
        invalid = Venue("Albus") with { CapturePoints = [new(140, 140, 0)] };
        Assert.Throws<ArgumentException>(() => TownStruggleVenue.ParseCatalog(Catalog(Venue("Galven"), invalid)));
    }

    [Fact]
    public void CaptureZonesCannotOverlapTeamBasesOrOtherDestinations()
    {
        var input = Venue("Galven");
        input = input with { CapturePoints = [input.OrderBase, input.CapturePoints[1], input.CapturePoints[2]] };
        Assert.Throws<ArgumentException>(() => TownStruggleVenue.ParseCatalog(Catalog(input)));
        input = Venue("Galven") with { ChaosBase = new(118, 118, 0) };
        Assert.Throws<ArgumentException>(() => TownStruggleVenue.ParseCatalog(Catalog(input)));
    }

    private static string Catalog(params TownStruggleVenue.VenueFile[] venues) => JsonSerializer.Serialize(new { venues });

    private static TownStruggleVenue.VenueFile Venue(string name, int x = 100, int y = 100) => new(
        name, 0, x, y, 200, 200, new(5396, 820, 0), new(x + 10, y + 10, 0), new(x + 180, y + 180, 0),
        [new(x + 40, y + 40, 0), new(x + 140, y + 40, 0), new(x + 100, y + 140, 0)],
        [new(x + 40, y + 120, 0), new(x + 120, y + 80, 0), new(x + 60, y + 80, 0)]);
}
