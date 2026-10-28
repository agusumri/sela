namespace Sela.Core.Tests;

public class NotchScaleTests
{
    [Theory]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    public void Offered_sizes_are_kept(int percent) => Assert.Equal(percent, NotchScale.Normalize(percent));

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    [InlineData(49)]
    [InlineData(60)]
    [InlineData(101)]
    [InlineData(150)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Anything_else_is_full_size(int percent) => Assert.Equal(100, NotchScale.Normalize(percent));

    [Fact]
    public void Default_setting_is_full_size() => Assert.Equal(100, new SelaSettings().NotchIdleScale);

    [Fact]
    public void Allowed_lists_the_three_sizes() => Assert.Equal(new[] { 50, 75, 100 }, NotchScale.Allowed);
}
