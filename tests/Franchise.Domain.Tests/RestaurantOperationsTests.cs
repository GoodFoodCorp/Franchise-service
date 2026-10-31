using FluentAssertions;
using Franchise.Domain.Entities;
using Franchise.Domain.Errors;
using Franchise.Domain.ValueObjects;

namespace Franchise.Domain.Tests;

public class RestaurantOperationsTests
{
    // Paris: UTC+2 in summer (CEST), UTC+1 in winter (CET).
    private static readonly DateTimeOffset TuesdayNoonParisSummer = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TuesdayEightAmParisSummer = new(2026, 7, 14, 6, 0, 0, TimeSpan.Zero);
    private static readonly GeoPoint Republique = new(48.8674, 2.3636);
    private static readonly GeoPoint Bastille = new(48.8532, 2.3692);   // ~1.6 km
    private static readonly GeoPoint Versailles = new(48.8049, 2.1204); // ~19 km

    private static Restaurant WithLunchService(Action<Restaurant>? tweak = null)
    {
        var r = new Restaurant("Good Food République", "republique", "12 place de la République", "Paris");
        r.SetLocation(Republique);
        r.UpdateOperations("01 02 03 04 05", Restaurant.DefaultTimeZone,
            [new OpeningSlot(DayOfWeek.Tuesday, new TimeOnly(11, 30), new TimeOnly(14, 30))],
            prepTimeMinutes: 20, deliveryRadiusKm: 5, deliveryFeeCents: 299, minimumOrderCents: 1000, seatingCapacity: 40);
        tweak?.Invoke(r);
        return r;
    }

    [Fact]
    public void Opening_hours_are_evaluated_in_the_restaurant_time_zone()
    {
        var r = WithLunchService();

        r.IsOpenAt(TuesdayNoonParisSummer).Should().BeTrue("10:00 UTC is 12:00 in Paris");
        r.IsOpenAt(TuesdayEightAmParisSummer).Should().BeFalse("06:00 UTC is 08:00 in Paris");
    }

    [Fact]
    public void A_period_can_run_past_midnight()
    {
        var slot = new OpeningSlot(DayOfWeek.Saturday, new TimeOnly(19, 0), new TimeOnly(1, 0));

        slot.Contains(DayOfWeek.Saturday, new TimeOnly(23, 30)).Should().BeTrue();
        slot.Contains(DayOfWeek.Sunday, new TimeOnly(0, 30)).Should().BeTrue();
        slot.Contains(DayOfWeek.Sunday, new TimeOnly(1, 0)).Should().BeFalse();
        slot.Contains(DayOfWeek.Saturday, new TimeOnly(18, 59)).Should().BeFalse();
    }

    [Fact]
    public void A_new_restaurant_opens_every_day_from_11_to_23()
    {
        var r = new Restaurant("New", "new", "", "");

        r.OpeningHours.Should().HaveCount(7).And.OnlyContain(s => s.Opens == new TimeOnly(11, 0) && s.Closes == new TimeOnly(23, 0));
        r.IsOpenAt(TuesdayNoonParisSummer).Should().BeTrue();
        r.IsOpenAt(TuesdayEightAmParisSummer).Should().BeFalse();
    }

    [Fact]
    public void Without_opening_hours_the_restaurant_is_never_open()
    {
        var r = WithLunchService(x => x.UpdateOperations("", Restaurant.DefaultTimeZone, [], 20, 5, 299, 0, 40));

        r.IsOpenAt(TuesdayNoonParisSummer).Should().BeFalse();
        r.StatusAt(TuesdayNoonParisSummer).State.Should().Be(OpeningState.NoHours);
        r.Quote(TuesdayNoonParisSummer, Bastille).Refusal.Should().Be(QuoteRefusal.Closed);
    }

    [Fact]
    public void Status_tells_until_when_it_is_open_and_when_it_reopens()
    {
        var r = WithLunchService();

        r.StatusAt(TuesdayNoonParisSummer).Should().Be(new OpeningStatus(OpeningState.Open, ClosesAt: new TimeOnly(14, 30)));
        r.StatusAt(TuesdayEightAmParisSummer).Should().Be(
            new OpeningStatus(OpeningState.OutsideHours, NextDay: DayOfWeek.Tuesday, NextOpens: new TimeOnly(11, 30)));
        // Tuesday 15:00 in Paris: next opening is next Tuesday.
        r.StatusAt(TuesdayNoonParisSummer.AddHours(3)).Should().Be(
            new OpeningStatus(OpeningState.OutsideHours, NextDay: DayOfWeek.Tuesday, NextOpens: new TimeOnly(11, 30)));
    }

    [Fact]
    public void Closed_by_the_restaurant_wins_over_its_hours()
    {
        var r = WithLunchService(x => x.SetAcceptingOrders(false));

        r.StatusAt(TuesdayNoonParisSummer).Should().Be(new OpeningStatus(OpeningState.ClosedByRestaurant));
        r.StatusAt(TuesdayNoonParisSummer).IsOpen.Should().BeFalse();
        r.Update(r.Name, r.Address, r.City, isActive: false);
        r.StatusAt(TuesdayNoonParisSummer).State.Should().Be(OpeningState.Inactive);
    }

    [Fact]
    public void Overlapping_periods_of_a_day_are_refused()
    {
        var act = () => WithLunchService(x => x.UpdateOperations("", Restaurant.DefaultTimeZone,
            [new OpeningSlot(DayOfWeek.Monday, new TimeOnly(11, 0), new TimeOnly(15, 0)),
             new OpeningSlot(DayOfWeek.Monday, new TimeOnly(14, 0), new TimeOnly(18, 0))], 20, 5, 299, 0, 40));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Quote_accepts_an_address_in_the_zone_with_fee_and_eta()
    {
        var quote = WithLunchService().Quote(TuesdayNoonParisSummer, Bastille);

        quote.CanOrder.Should().BeTrue();
        quote.DistanceKm.Should().BeApproximately(1.6, 0.2);
        quote.DeliveryFeeCents.Should().Be(299);
        quote.MinimumOrderCents.Should().Be(1000);
        quote.EstimatedDeliveryMinutes.Should().Be(20 + 5 + 7, "prep + pickup + 1.6 km at 15 km/h");
    }

    [Theory]
    [InlineData("closed", QuoteRefusal.Closed)]
    [InlineData("paused", QuoteRefusal.Paused)]
    [InlineData("far", QuoteRefusal.OutOfZone)]
    [InlineData("unknown", QuoteRefusal.UnknownAddress)]
    [InlineData("inactive", QuoteRefusal.Inactive)]
    public void Quote_refusals(string scenario, QuoteRefusal expected)
    {
        var r = WithLunchService(x =>
        {
            if (scenario == "paused")
            {
                x.SetAcceptingOrders(false);
            }

            if (scenario == "inactive")
            {
                x.Update(x.Name, x.Address, x.City, isActive: false);
            }
        });
        var now = scenario == "closed" ? TuesdayEightAmParisSummer : TuesdayNoonParisSummer;
        GeoPoint? dropoff = scenario switch
        {
            "far" => Versailles,
            "unknown" => null,
            _ => Bastille,
        };

        var quote = r.Quote(now, dropoff);

        quote.CanOrder.Should().BeFalse();
        quote.Refusal.Should().Be(expected);
    }

    [Fact]
    public void Quote_skips_the_distance_check_when_the_address_could_not_be_looked_up()
    {
        var quote = WithLunchService().Quote(TuesdayNoonParisSummer, null, dropoffLookedUp: false);

        quote.CanOrder.Should().BeTrue("a geocoding outage must not stop the restaurant");
        quote.DistanceKm.Should().BeNull();
    }

    [Theory]
    [InlineData("Mars/Olympus", 20, 5.0, 0, 40)]
    [InlineData("Europe/Paris", 0, 5.0, 0, 40)]
    [InlineData("Europe/Paris", 20, 0.0, 0, 40)]
    [InlineData("Europe/Paris", 20, 5.0, -1, 40)]
    [InlineData("Europe/Paris", 20, 5.0, 0, -1)]
    public void Invalid_operations_are_rejected(string tz, int prep, double radius, long fee, int seats)
    {
        var r = new Restaurant("R", "r", "", "");
        var act = () => r.UpdateOperations("", tz, [], prep, radius, fee, 0, seats);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.Validation);
    }

    [Fact]
    public void Degenerate_opening_period_is_rejected()
    {
        var r = new Restaurant("R", "r", "", "");
        var act = () => r.UpdateOperations("", Restaurant.DefaultTimeZone,
            [new OpeningSlot(DayOfWeek.Monday, new TimeOnly(12, 0), new TimeOnly(12, 0))], 20, 5, 0, 0, 40);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Haversine_distance_is_symmetric_and_plausible()
    {
        Republique.DistanceKmTo(Versailles).Should().BeApproximately(Versailles.DistanceKmTo(Republique), 1e-9);
        Republique.DistanceKmTo(Versailles).Should().BeInRange(17, 21);
        Republique.DistanceKmTo(Republique).Should().Be(0);
    }

    [Fact]
    public void Out_of_range_coordinates_are_rejected()
    {
        var act = () => new GeoPoint(91, 0);
        act.Should().Throw<DomainException>();
    }
}
