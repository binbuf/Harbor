using System.Globalization;

namespace Harbor.Shell.Tests;

public class ClockFormattingTests
{
    [Fact]
    public void FormatClock_ReturnsExpectedFormat()
    {
        var time = new DateTime(2026, 2, 20, 14, 30, 0);
        var result = TopMenuBar.FormatClock(time);

        // Should contain the hour, minute, and AM/PM designator
        Assert.Contains(":", result);
        Assert.Matches(@"\d{1,2}:\d{2}\s*(AM|PM|am|pm)", result);
    }

    [Fact]
    public void FormatClock_MorningTime_ShowsAM()
    {
        var time = new DateTime(2026, 2, 20, 9, 15, 0);
        var result = TopMenuBar.FormatClock(time);

        var amDesignator = CultureInfo.CurrentCulture.DateTimeFormat.AMDesignator;
        Assert.Contains(amDesignator, result);
    }

    [Fact]
    public void FormatClock_AfternoonTime_ShowsPM()
    {
        var time = new DateTime(2026, 2, 20, 14, 30, 0);
        var result = TopMenuBar.FormatClock(time);

        var pmDesignator = CultureInfo.CurrentCulture.DateTimeFormat.PMDesignator;
        Assert.Contains(pmDesignator, result);
    }

    [Fact]
    public void FormatClock_Midnight_Shows12()
    {
        var time = new DateTime(2026, 2, 20, 0, 0, 0);
        var result = TopMenuBar.FormatClock(time);

        Assert.StartsWith("12:", result);
    }

    [Fact]
    public void FormatClock_Noon_Shows12()
    {
        var time = new DateTime(2026, 2, 20, 12, 0, 0);
        var result = TopMenuBar.FormatClock(time);

        Assert.StartsWith("12:", result);
    }

    [Fact]
    public void FormatClock_DoesNotShowSeconds()
    {
        var time = new DateTime(2026, 2, 20, 14, 30, 45);
        var result = TopMenuBar.FormatClock(time);

        // Should only have one colon (h:mm), not two (h:mm:ss)
        Assert.Equal(1, result.Count(c => c == ':'));
    }
}
