using FluentAssertions;
using PdfEditor.Redaction.Operators;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.Operators;

/// <summary>
/// Tests for OperatorRegistry - the modular handler system.
/// </summary>
public class OperatorRegistryTests
{
    [Fact]
    public void CreateDefault_RegistersAllV13Operators()
    {
        // Act
        var registry = OperatorRegistry.CreateDefault();

        // Assert - v1.3.0 operators (all required for birth certificate PDF)
        registry.HasHandler("BT").Should().BeTrue("BT should be registered");
        registry.HasHandler("ET").Should().BeTrue("ET should be registered");
        registry.HasHandler("Tf").Should().BeTrue("Tf should be registered");
        registry.HasHandler("Tm").Should().BeTrue("Tm should be registered");
        registry.HasHandler("TD").Should().BeTrue("TD should be registered");
        registry.HasHandler("Td").Should().BeTrue("Td should be registered");
        registry.HasHandler("TL").Should().BeTrue("TL should be registered");
        registry.HasHandler("T*").Should().BeTrue("T* should be registered");
        registry.HasHandler("Tj").Should().BeTrue("Tj should be registered");
        registry.HasHandler("TJ").Should().BeTrue("TJ should be registered");
    }

    [Fact]
    public void CreateDefault_DoesNotRegisterFutureOperators()
    {
        // Act
        var registry = OperatorRegistry.CreateDefault();

        // Assert - these are NOT in v1.3.0 (legacy/rarely used operators)
        registry.HasHandler("'").Should().BeFalse("Quote operator is not in v1.3.0");
        registry.HasHandler("\"").Should().BeFalse("Double-quote operator is not in v1.3.0");
    }

    [Fact]
    public void CreateEmpty_HasNoHandlers()
    {
        // Act
        var registry = OperatorRegistry.CreateEmpty();

        // Assert
        registry.Count.Should().Be(0);
        registry.HasHandler("BT").Should().BeFalse();
    }

    [Fact]
    public void GetHandler_ReturnsCorrectHandler()
    {
        // Arrange
        var registry = OperatorRegistry.CreateDefault();

        // Act
        var handler = registry.GetHandler("Tj");

        // Assert
        handler.Should().NotBeNull();
        handler!.OperatorName.Should().Be("Tj");
    }

    [Fact]
    public void GetHandler_UnknownOperator_ReturnsNull()
    {
        // Arrange
        var registry = OperatorRegistry.CreateDefault();

        // Act
        var handler = registry.GetHandler("UNKNOWN");

        // Assert
        handler.Should().BeNull();
    }

    [Fact]
    public void RegisteredOperators_ReturnsAllOperatorNames()
    {
        // Arrange
        var registry = OperatorRegistry.CreateDefault();

        // Act
        var operators = registry.RegisteredOperators.ToList();

        // Assert - all v1.3.0 operators
        operators.Should().Contain("BT");
        operators.Should().Contain("ET");
        operators.Should().Contain("Tf");
        operators.Should().Contain("Tm");
        operators.Should().Contain("TD");
        operators.Should().Contain("Td");
        operators.Should().Contain("TL");
        operators.Should().Contain("T*");
        operators.Should().Contain("Tj");
        operators.Should().Contain("TJ");
    }
}
