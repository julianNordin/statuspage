using Microsoft.EntityFrameworkCore;
using StatusPage.Domain;
using StatusPage.Domain.Model;

namespace StatusPage.Infrastructure.Tests;

[Collection(SqlServerDatabase.Name)]
public class ComponentRoundTripTests(SqlServerFixture fixture)
{
    private static Component NewComponent(string slug) => new()
    {
        Id = Guid.NewGuid(),
        Name = "The API",
        Slug = slug,
        TargetUrl = "https://example.com/health",
        CreatedAt = new DateTimeOffset(2026, 8, 4, 19, 30, 0, TimeSpan.FromHours(2)),
    };

    [Fact]
    public async Task A_component_survives_a_round_trip_with_its_settings_intact()
    {
        var written = NewComponent($"api-{Guid.NewGuid():N}");
        written.DegradedAboveMs = 750;
        written.FailuresToOpen = 4;
        written.SuccessesToClose = 3;

        await using (var db = fixture.NewContext())
        {
            db.Components.Add(written);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.NewContext())
        {
            var read = await db.Components.SingleAsync(
                c => c.Id == written.Id, TestContext.Current.CancellationToken);

            Assert.Equal("The API", read.Name);
            Assert.Equal("https://example.com/health", read.TargetUrl);
            Assert.Equal(750, read.DegradedAboveMs);
            Assert.Equal(4, read.FailuresToOpen);
            Assert.Equal(3, read.SuccessesToClose);
            Assert.True(read.Enabled);
        }
    }

    [Fact]
    public async Task A_components_settings_come_back_as_the_domain_policies_they_describe()
    {
        var written = NewComponent($"api-{Guid.NewGuid():N}");
        written.ExpectedStatusCode = 204;
        written.DegradedAboveMs = 250;

        await using (var db = fixture.NewContext())
        {
            db.Components.Add(written);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.NewContext())
        {
            var read = await db.Components.SingleAsync(
                c => c.Id == written.Id, TestContext.Current.CancellationToken);

            Assert.Equal(
                new CheckPolicy(204, TimeSpan.FromMilliseconds(250)), read.CheckPolicy());
            Assert.Equal(new Hysteresis(3, 2), read.Hysteresis());
        }
    }

    [Fact]
    public async Task Intervals_come_back_attached_to_their_component_and_convert_to_the_domain()
    {
        var component = NewComponent($"api-{Guid.NewGuid():N}");
        var start = new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.Zero);

        component.Intervals.Add(new ComponentInterval
        {
            State = ComponentState.Up,
            StartedAt = start,
            EndedAt = start.AddHours(6),
        });
        component.Intervals.Add(new ComponentInterval
        {
            State = ComponentState.Down,
            StartedAt = start.AddHours(6),
            EndedAt = null,
        });

        await using (var db = fixture.NewContext())
        {
            db.Components.Add(component);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.NewContext())
        {
            var read = await db.Components
                .Include(c => c.Intervals)
                .SingleAsync(c => c.Id == component.Id, TestContext.Current.CancellationToken);

            Assert.Equal(2, read.Intervals.Count);

            var open = read.Intervals.Single(i => i.EndedAt is null);
            Assert.Equal(ComponentState.Down, open.State);

            var domain = open.ToDomain();
            Assert.True(domain.IsOpen);
            Assert.Equal(ComponentState.Down, domain.State);
        }
    }

    [Fact]
    public async Task A_datetimeoffset_keeps_its_instant_across_a_round_trip()
    {
        // SQL Server stores datetimeoffset with the offset preserved. This matters because
        // every commit in this project is +02:00 and every interval boundary is compared
        // against another one; a silent conversion to UTC-naive would still compare equal
        // here, so the assertion is on the offset itself, not only on the instant.
        var component = NewComponent($"api-{Guid.NewGuid():N}");
        var stamped = new DateTimeOffset(2026, 8, 4, 19, 30, 0, TimeSpan.FromHours(2));

        component.Intervals.Add(new ComponentInterval
        {
            State = ComponentState.Up,
            StartedAt = stamped,
            EndedAt = null,
        });

        await using (var db = fixture.NewContext())
        {
            db.Components.Add(component);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = fixture.NewContext())
        {
            var read = await db.Intervals.SingleAsync(
                i => i.ComponentId == component.Id, TestContext.Current.CancellationToken);

            Assert.Equal(stamped, read.StartedAt);
            Assert.Equal(TimeSpan.FromHours(2), read.StartedAt.Offset);
        }
    }
}
