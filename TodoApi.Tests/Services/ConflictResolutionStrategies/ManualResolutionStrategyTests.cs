using TodoApi.Common;
using TodoApi.Services.ConflictResolutionStrategies;
using TodoApi.Tests.Builders;

namespace TodoApi.Tests.Services.ConflictResolutionStrategies;

public class ManualResolutionStrategyTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly ManualResolutionStrategy<TestLocalEntity, TestExternalEntity> _strategy;

    public ManualResolutionStrategyTests()
    {
        _mockLogger = new Mock<ILogger>();
        _strategy = new ManualResolutionStrategy<TestLocalEntity, TestExternalEntity>(_mockLogger.Object);
    }

    [Fact]
    public void StrategyType_ShouldReturnManualResolution()
    {
        // Act
        var result = _strategy.StrategyType;

        // Assert
        Assert.Equal(ConflictResolutionStrategy.ManualResolution, result);
    }

    [Fact]
    public void ShouldApplyExternalChanges_ShouldAlwaysReturnFalse()
    {
        // Arrange
        var localEntity = new TestLocalEntity { Id = "local-1", Name = "Local Entity" };
        var externalEntity = new TestExternalEntity { Id = "external-1", Name = "External Entity" };
        var conflictInfo = ConflictInfoBuilder.Create()
            .WithEntityType("TestEntity")
            .WithEntityId("local-1")
            .WithExternalEntityId("external-1")
            .WithLocalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithExternalLastModified(DateTime.UtcNow)
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-2))
            .WithModifiedFields(new List<string> { "Name" })
            .Build();

        // Act
        var result = _strategy.ShouldApplyExternalChanges(localEntity, externalEntity, conflictInfo);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldApplyExternalChanges_WithNullEntities_ShouldReturnFalse()
    {
        // Arrange
        TestLocalEntity? localEntity = null;
        TestExternalEntity? externalEntity = null;
        var conflictInfo = ConflictInfoBuilder.Create()
            .WithEntityType("TestEntity")
            .WithEntityId("local-1")
            .WithExternalEntityId("external-1")
            .WithLocalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithExternalLastModified(DateTime.UtcNow)
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-2))
            .WithModifiedFields(new List<string> { "Name" })
            .Build();

        // Act
        var result = _strategy.ShouldApplyExternalChanges(localEntity!, externalEntity!, conflictInfo);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetResolutionReason_ShouldReturnCorrectReason()
    {
        // Arrange
        var localEntity = new TestLocalEntity { Id = "local-1", Name = "Local Entity" };
        var externalEntity = new TestExternalEntity { Id = "external-1", Name = "External Entity" };
        var localLastModified = new DateTime(2024, 1, 15, 10, 30, 0);
        var externalLastModified = new DateTime(2024, 1, 15, 11, 45, 0);

        var conflictInfo = ConflictInfoBuilder.Create()
            .WithEntityType("TestEntity")
            .WithEntityId("local-1")
            .WithExternalEntityId("external-1")
            .WithLocalLastModified(localLastModified)
            .WithExternalLastModified(externalLastModified)
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-2))
            .WithModifiedFields(new List<string> { "Name", "Description" })
            .Build();

        // Act
        var result = _strategy.GetResolutionReason(localEntity, externalEntity, conflictInfo);

        // Assert
        var expectedReason = $"Manual resolution required. Local changes made at {localLastModified:yyyy-MM-dd HH:mm:ss} " +
                           $"conflict with external changes made at {externalLastModified:yyyy-MM-dd HH:mm:ss}. " +
                           "Human intervention needed to determine which changes to keep.";
        Assert.Equal(expectedReason, result);
    }

    [Fact]
    public void GetResolutionReason_ShouldLogErrorWithCorrectParameters()
    {
        // Arrange
        var localEntity = new TestLocalEntity { Id = "local-1", Name = "Local Entity" };
        var externalEntity = new TestExternalEntity { Id = "external-1", Name = "External Entity" };
        var conflictInfo = ConflictInfoBuilder.Create()
            .WithEntityType("TestEntity")
            .WithEntityId("local-1")
            .WithExternalEntityId("external-1")
            .WithLocalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithExternalLastModified(DateTime.UtcNow)
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-2))
            .WithModifiedFields(new List<string> { "Name", "Description" })
            .Build();

        // Act
        _strategy.GetResolutionReason(localEntity, externalEntity, conflictInfo);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("CONFLICT DETECTED")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void GetResolutionReason_ShouldLogErrorWithCorrectEntityInformation()
    {
        // Arrange
        var localEntity = new TestLocalEntity { Id = "local-1", Name = "Local Entity" };
        var externalEntity = new TestExternalEntity { Id = "external-1", Name = "External Entity" };
        var conflictInfo = ConflictInfoBuilder.Create()
            .WithEntityType("TestEntity")
            .WithEntityId("local-1")
            .WithExternalEntityId("external-1")
            .WithLocalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithExternalLastModified(DateTime.UtcNow)
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-2))
            .WithModifiedFields(new List<string> { "Name", "Description" })
            .Build();

        // Act
        _strategy.GetResolutionReason(localEntity, externalEntity, conflictInfo);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains("TestEntity") &&
                    v.ToString()!.Contains("local-1") &&
                    v.ToString()!.Contains("external-1") &&
                    v.ToString()!.Contains("Name, Description")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void GetResolutionReason_ShouldLogErrorLevelInsteadOfWarning()
    {
        // Arrange
        var localEntity = new TestLocalEntity { Id = "local-1", Name = "Local Entity" };
        var externalEntity = new TestExternalEntity { Id = "external-1", Name = "External Entity" };
        var conflictInfo = ConflictInfoBuilder.Create()
            .WithEntityType("TestEntity")
            .WithEntityId("local-1")
            .WithExternalEntityId("external-1")
            .WithLocalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithExternalLastModified(DateTime.UtcNow)
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-2))
            .WithModifiedFields(new List<string> { "Name", "Description" })
            .Build();

        // Act
        _strategy.GetResolutionReason(localEntity, externalEntity, conflictInfo);

        // Assert - Verify it logs at Error level, not Warning level
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify it does NOT log at Warning level
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void GetResolutionReason_WithEmptyModifiedFields_ShouldHandleGracefully()
    {
        // Arrange
        var localEntity = new TestLocalEntity { Id = "local-1", Name = "Local Entity" };
        var externalEntity = new TestExternalEntity { Id = "external-1", Name = "External Entity" };
        var conflictInfo = ConflictInfoBuilder.Create()
            .WithEntityType("TestEntity")
            .WithEntityId("local-1")
            .WithExternalEntityId("external-1")
            .WithLocalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithExternalLastModified(DateTime.UtcNow)
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-2))
            .WithModifiedFields(new List<string>())
            .Build();

        // Act
        var result = _strategy.GetResolutionReason(localEntity, externalEntity, conflictInfo);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Manual resolution required", result);
    }

    [Fact]
    public void GetResolutionReason_WithNullModifiedFields_ShouldThrowArgumentNullException()
    {
        // Arrange
        var localEntity = new TestLocalEntity { Id = "local-1", Name = "Local Entity" };
        var externalEntity = new TestExternalEntity { Id = "external-1", Name = "External Entity" };
        var conflictInfo = ConflictInfoBuilder.Create()
            .WithEntityType("TestEntity")
            .WithEntityId("local-1")
            .WithExternalEntityId("external-1")
            .WithLocalLastModified(DateTime.UtcNow.AddHours(-1))
            .WithExternalLastModified(DateTime.UtcNow)
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-2))
            .WithModifiedFields(null!)
            .Build();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            _strategy.GetResolutionReason(localEntity, externalEntity, conflictInfo));
        Assert.Equal("values", exception.ParamName);
    }

    [Fact]
    public void GetResolutionReason_WithDifferentDateTimeFormats_ShouldFormatCorrectly()
    {
        // Arrange
        var localEntity = new TestLocalEntity { Id = "local-1", Name = "Local Entity" };
        var externalEntity = new TestExternalEntity { Id = "external-1", Name = "External Entity" };
        var localLastModified = new DateTime(2024, 12, 25, 23, 59, 59);
        var externalLastModified = new DateTime(2024, 12, 26, 0, 0, 1);

        var conflictInfo = ConflictInfoBuilder.Create()
            .WithEntityType("TestEntity")
            .WithEntityId("local-1")
            .WithExternalEntityId("external-1")
            .WithLocalLastModified(localLastModified)
            .WithExternalLastModified(externalLastModified)
            .WithLastSyncedAt(DateTime.UtcNow.AddHours(-2))
            .WithModifiedFields(new List<string> { "Name" })
            .Build();

        // Act
        var result = _strategy.GetResolutionReason(localEntity, externalEntity, conflictInfo);

        // Assert
        var expectedLocalFormat = localLastModified.ToString("yyyy-MM-dd HH:mm:ss");
        var expectedExternalFormat = externalLastModified.ToString("yyyy-MM-dd HH:mm:ss");
        Assert.Contains(expectedLocalFormat, result);
        Assert.Contains(expectedExternalFormat, result);
    }

    [Fact]
    public void Constructor_WithLogger_ShouldSetLogger()
    {
        // Arrange & Act
        var strategy = new ManualResolutionStrategy<TestLocalEntity, TestExternalEntity>(_mockLogger.Object);

        // Assert
        Assert.NotNull(strategy);
        Assert.Equal(ConflictResolutionStrategy.ManualResolution, strategy.StrategyType);
    }

    // Test entity classes for generic type testing
    public class TestLocalEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class TestExternalEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
