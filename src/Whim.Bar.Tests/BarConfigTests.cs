using Xunit;

namespace Whim.Bar.Tests;

public class BarConfigTests
{
	[Fact]
	public void Height_PropertyChanged()
	{
		// Given
		BarConfig config = new(leftComponents: [], centerComponents: [], rightComponents: []);

		// When
		// Then
		Assert.PropertyChanged(config, nameof(config.Height), () => config.Height = 1);
		Assert.Equal(1, config.Height);
	}

	[Fact]
	public void FontSize_DefaultsToBaseTextSize()
	{
		// Given a config with no explicit font size
		BarConfig config = new(leftComponents: [], centerComponents: [], rightComponents: []);

		// When / Then the default matches the shared base text style (14)
		Assert.Equal(14, config.FontSize);
	}

	[Fact]
	public void FontSize_PropertyChanged()
	{
		// Given
		BarConfig config = new(leftComponents: [], centerComponents: [], rightComponents: []);

		// When
		// Then
		Assert.PropertyChanged(config, nameof(config.FontSize), () => config.FontSize = 20);
		Assert.Equal(20, config.FontSize);
	}
}
