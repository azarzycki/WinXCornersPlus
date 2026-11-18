using WinXCorners.App.UiTests.Infrastructure;

namespace WinXCorners.App.UiTests;

/// <summary>
/// Review V2 D7: a corner button shows a custom command's name from settings.json. TextRenderer treats "&amp;" as a
/// mnemonic marker unless told not to, so "Backup &amp; Sync" showed as "Backup _Sync" and "&amp;&amp;" as one "&amp;".
/// </summary>
public sealed class FlyoutControlsTests
{
    private const string Ampersands = "&&&&&&&&&&&&";

    [Fact]
    public void Button_text_flags_include_NoPrefix()
    {
        Assert.True((FlyoutActionControl.ButtonTextFlags & TextFormatFlags.NoPrefix) != 0);
    }

    [Theory]
    [InlineData(Theme.Light)]
    [InlineData(Theme.Dark)]
    public void Every_ampersand_in_a_command_name_is_drawn(Theme theme)
    {
        using var themeScope = ThemeScope.Use(theme);
        Ui.Run(() =>
        {
            using var control = new FlyoutActionControl { Size = new Size(420, 28), Text = Ampersands };
            using var bitmap = new Bitmap(control.Width, control.Height);
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));

            var withoutPrefix = TextRenderer.MeasureText(Ampersands, control.Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
            var withPrefix = TextRenderer.MeasureText(Ampersands, control.Font, Size.Empty, TextFormatFlags.Default).Width;
            var inkRight = RightmostInk(bitmap, fromX: 4, toX: control.Width - 40);

            // Twelve ampersands drawn literally are about twice as wide as the six that mnemonic processing leaves.
            Assert.True(withoutPrefix > withPrefix * 1.5, $"measure: {withoutPrefix} vs {withPrefix}");
            Assert.True(inkRight > withPrefix + 10 + ((withoutPrefix - withPrefix) / 2),
                $"Ink ends at x={inkRight}; literal text is {withoutPrefix} px wide, mnemonic-processed text {withPrefix} px.");
        });
    }

    /// <summary>The rightmost column in [fromX, toX) with a pixel clearly different from the button's fill.</summary>
    private static int RightmostInk(Bitmap bitmap, int fromX, int toX)
    {
        var fill = bitmap.GetPixel(fromX, bitmap.Height / 2);
        var rightmost = -1;
        for (var x = fromX; x < toX; x++)
        {
            for (var y = 4; y < bitmap.Height - 4; y++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (Math.Abs(pixel.R - fill.R) + Math.Abs(pixel.G - fill.G) + Math.Abs(pixel.B - fill.B) > 90)
                {
                    rightmost = x;
                    break;
                }
            }
        }

        return rightmost;
    }
}
