using FluentAssertions;
using TenantCore.EntityFramework.Events;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Events;

public class TenantEventsTests
{
    [Fact]
    public void TenantCreatedEvent_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var @event = new TenantCreatedEvent<string>("tenant1");

        // Assert
        @event.TenantId.Should().Be("tenant1");
        @event.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TenantDeletedEvent_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var @event = new TenantDeletedEvent<string>("tenant1", hardDelete: true);

        // Assert
        @event.TenantId.Should().Be("tenant1");
        @event.HardDelete.Should().BeTrue();
        @event.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TenantArchivedEvent_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var @event = new TenantArchivedEvent<string>("tenant1");

        // Assert
        @event.TenantId.Should().Be("tenant1");
        @event.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TenantRestoredEvent_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var @event = new TenantRestoredEvent<string>("tenant1");

        // Assert
        @event.TenantId.Should().Be("tenant1");
        @event.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MigrationAppliedEvent_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var @event = new MigrationAppliedEvent<string>("tenant1", "20240101_InitialCreate");

        // Assert
        @event.TenantId.Should().Be("tenant1");
        @event.MigrationName.Should().Be("20240101_InitialCreate");
        @event.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TenantResolvedEvent_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var @event = new TenantResolvedEvent<string>("tenant1", "HeaderTenantResolver");

        // Assert
        @event.TenantId.Should().Be("tenant1");
        @event.ResolverName.Should().Be("HeaderTenantResolver");
        @event.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Events_WithGuidKey_ShouldWork()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        // Act
        var createdEvent = new TenantCreatedEvent<Guid>(tenantId);
        var deletedEvent = new TenantDeletedEvent<Guid>(tenantId, false);

        // Assert
        createdEvent.TenantId.Should().Be(tenantId);
        deletedEvent.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void Events_WithCustomTimestamp_ShouldUseProvidedTimestamp()
    {
        // Arrange
        var customTimestamp = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        var @event = new TenantCreatedEvent<string>("tenant1", customTimestamp);

        // Assert
        @event.Timestamp.Should().Be(customTimestamp);
    }
}
