using FluentAssertions;
using TenantCore.EntityFramework.Configuration;
using Xunit;

namespace TenantCore.EntityFramework.Tests.Configuration;

public class MigrationHistoryTableConfigTests
{
    [Fact]
    public void MigrationOptions_DefaultHistoryTable_ShouldBeEFDefault()
    {
        // Arrange & Act
        var options = new MigrationOptions();

        // Assert
        options.MigrationHistoryTable.Should().Be("__EFMigrationsHistory");
    }

    [Fact]
    public void MigrationOptions_CustomHistoryTable_ShouldPersist()
    {
        // Arrange
        var options = new MigrationOptions();

        // Act
        options.MigrationHistoryTable = "__CustomMigrations";

        // Assert
        options.MigrationHistoryTable.Should().Be("__CustomMigrations");
    }

    [Fact]
    public void MigrationOptions_SeparateMigrationHistory_DefaultShouldBeTrue()
    {
        // Arrange & Act
        var options = new MigrationOptions();

        // Assert
        options.SeparateMigrationHistory.Should().BeTrue();
    }

    [Fact]
    public void MigrationOptions_SeparateMigrationHistory_CanBeSetToFalse()
    {
        // Arrange
        var options = new MigrationOptions();

        // Act
        options.SeparateMigrationHistory = false;

        // Assert
        options.SeparateMigrationHistory.Should().BeFalse();
    }
}
