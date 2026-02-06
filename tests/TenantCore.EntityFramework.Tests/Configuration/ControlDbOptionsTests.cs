using FluentAssertions;
using TenantCore.EntityFramework.Configuration;
using TenantCore.EntityFramework.ControlDb;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Configuration;

public class ControlDbOptionsTests
{
    [Fact]
    public void DefaultValues_ShouldBeCorrect()
    {
        // Act
        var options = new ControlDbOptions();

        // Assert
        options.Enabled.Should().BeFalse();
        options.ConnectionString.Should().BeNull();
        options.Schema.Should().Be("tenant_control");
        options.ApplyMigrationsOnStartup.Should().BeTrue();
        options.EnableCaching.Should().BeTrue();
        options.CacheDuration.Should().Be(TimeSpan.FromMinutes(5));
        options.MigratableStatuses.Should().BeEquivalentTo(new[] { TenantStatus.Pending, TenantStatus.Active });
    }

    [Fact]
    public void Properties_ShouldBeSettable()
    {
        // Arrange
        var options = new ControlDbOptions();

        // Act
        options.Enabled = true;
        options.ConnectionString = "Host=localhost;Database=controldb";
        options.Schema = "custom_schema";
        options.ApplyMigrationsOnStartup = false;
        options.EnableCaching = false;
        options.CacheDuration = TimeSpan.FromMinutes(10);
        options.MigratableStatuses = new[] { TenantStatus.Active };

        // Assert
        options.Enabled.Should().BeTrue();
        options.ConnectionString.Should().Be("Host=localhost;Database=controldb");
        options.Schema.Should().Be("custom_schema");
        options.ApplyMigrationsOnStartup.Should().BeFalse();
        options.EnableCaching.Should().BeFalse();
        options.CacheDuration.Should().Be(TimeSpan.FromMinutes(10));
        options.MigratableStatuses.Should().BeEquivalentTo(new[] { TenantStatus.Active });
    }
}
