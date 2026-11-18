using System.Drawing;
using WinXCorners.Core.Layout;

namespace WinXCorners.Core.Tests.Layout;

public class ColorContrastTests
{
    private static readonly Color Dark = Color.FromArgb(20, 20, 20);
    private static readonly Color Light = Color.White;

    [Fact]
    public void Luminance_of_black_and_white_and_known_values()
    {
        Assert.Equal(0.0, ColorContrast.RelativeLuminance(Color.Black), 6);
        Assert.Equal(1.0, ColorContrast.RelativeLuminance(Color.White), 6);
        // sRGB (128,128,128) is about 0.2159 (WCAG reference value).
        Assert.Equal(0.2159, ColorContrast.RelativeLuminance(Color.FromArgb(128, 128, 128)), 3);
        // Channel weights: pure green dominates, pure blue barely counts.
        Assert.Equal(0.7152, ColorContrast.RelativeLuminance(Color.FromArgb(0, 255, 0)), 4);
        Assert.Equal(0.0722, ColorContrast.RelativeLuminance(Color.FromArgb(0, 0, 255)), 4);
        // The linear segment of the sRGB curve (channel value 10 -> 10/255/12.92).
        Assert.Equal(10 / 255d / 12.92, ColorContrast.RelativeLuminance(Color.FromArgb(10, 10, 10)), 6);
    }

    [Fact]
    public void Ratio_is_21_for_black_on_white_1_for_equal_colours_and_symmetric()
    {
        Assert.Equal(21.0, ColorContrast.Ratio(Color.Black, Color.White), 6);
        Assert.Equal(1.0, ColorContrast.Ratio(Color.Teal, Color.Teal), 6);
        Assert.Equal(ColorContrast.Ratio(Color.Orange, Dark), ColorContrast.Ratio(Dark, Color.Orange), 9);
    }

    [Fact]
    public void Alpha_is_ignored()
    {
        Assert.Equal(ColorContrast.RelativeLuminance(Color.FromArgb(255, 200, 100, 50)),
            ColorContrast.RelativeLuminance(Color.FromArgb(0, 200, 100, 50)), 9);
    }

    /// <summary>
    /// Every accent-like colour: the chosen text colour is the better of the two, and never worse than the other.
    /// The old rule (white below luminance 0.45) picked white on mid-tones such as Windows' orange accent.
    /// </summary>
    [Theory]
    [InlineData(255, 140, 0)]   // Windows orange accent: dark text wins clearly.
    [InlineData(0, 120, 215)]   // Default blue accent: white wins.
    [InlineData(232, 17, 35)]   // Red.
    [InlineData(16, 137, 62)]   // Green.
    [InlineData(255, 185, 0)]   // Gold.
    [InlineData(118, 118, 118)] // Grey.
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    public void ReadableText_picks_whichever_contrasts_more(int r, int g, int b)
    {
        var background = Color.FromArgb(r, g, b);
        var chosen = ColorContrast.ReadableText(background, Dark, Light);
        var other = chosen == Dark ? Light : Dark;

        Assert.True(ColorContrast.Ratio(background, chosen) >= ColorContrast.Ratio(background, other));
    }

    [Fact]
    public void Orange_accent_gets_dark_text_where_the_old_threshold_gave_white()
    {
        var orange = Color.FromArgb(255, 140, 0);
        Assert.True(ColorContrast.RelativeLuminance(orange) < 0.45, "The old rule would have chosen white.");
        Assert.Equal(Dark, ColorContrast.ReadableText(orange, Dark, Light));
        Assert.True(ColorContrast.Ratio(orange, Light) < 3.0);
        Assert.True(ColorContrast.Ratio(orange, Dark) > 7.0);
    }

    [Fact]
    public void A_tie_goes_to_the_light_colour()
    {
        Assert.Equal(Color.White, ColorContrast.ReadableText(Color.Gray, Color.White, Color.White));
    }
}
