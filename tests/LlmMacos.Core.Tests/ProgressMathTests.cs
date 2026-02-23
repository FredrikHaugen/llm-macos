using FluentAssertions;
using LlmMacos.Core.Services;

namespace LlmMacos.Core.Tests;

public sealed class ProgressMathTests
{
    [Fact]
    public void CalculatePercent_ReturnsExpectedValue()
    {
        var result = ProgressMath.CalculatePercent(50, 200);
        result.Should().Be(25d);
    }

    [Fact]
    public void CalculatePercent_ReturnsNull_WhenTotalMissingOrInvalid()
    {
        ProgressMath.CalculatePercent(10, null).Should().BeNull();
        ProgressMath.CalculatePercent(10, 0).Should().BeNull();
    }

    [Fact]
    public void CalculateBytesPerSecond_ReturnsExpectedValue()
    {
        var result = ProgressMath.CalculateBytesPerSecond(2048, TimeSpan.FromSeconds(2));
        result.Should().Be(1024d);
    }

    [Fact]
    public void CalculateEta_ReturnsZero_WhenComplete()
    {
        var eta = ProgressMath.CalculateEta(100, 100, 1000);
        eta.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void CalculateEta_ReturnsNull_WhenInsufficientData()
    {
        ProgressMath.CalculateEta(50, null, 200).Should().BeNull();
        ProgressMath.CalculateEta(50, 100, null).Should().BeNull();
        ProgressMath.CalculateEta(50, 100, 0).Should().BeNull();
    }
}
