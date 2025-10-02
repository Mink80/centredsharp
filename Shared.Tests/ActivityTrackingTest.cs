using CentrED;
using CentrED.Server.Config;
using Xunit;

namespace Shared.Tests;

public class ActivityTrackingTest
{
    [Fact]
    public void TestAddActivityMinutes()
    {
        var account = new Account("testuser", "password", AccessLevel.Normal);

        // Add activity for January 2025
        account.AddActivityMinutes(2025, 1, 5);
        Assert.Equal(5, account.GetActivityMinutes(2025, 1));

        // Add more activity to the same month
        account.AddActivityMinutes(2025, 1, 10);
        Assert.Equal(15, account.GetActivityMinutes(2025, 1));

        // Add activity for a different month
        account.AddActivityMinutes(2025, 2, 20);
        Assert.Equal(20, account.GetActivityMinutes(2025, 2));

        // Verify January is unchanged
        Assert.Equal(15, account.GetActivityMinutes(2025, 1));
    }

    [Fact]
    public void TestActivityHistoryRotation()
    {
        var account = new Account("testuser", "password", AccessLevel.Normal);

        // Add 13 months of activity starting from January 2024
        for (int month = 1; month <= 13; month++)
        {
            int year = 2024;
            int actualMonth = month;
            if (month > 12)
            {
                year = 2025;
                actualMonth = month - 12;
            }
            account.AddActivityMinutes(year, actualMonth, 10);
        }

        // Should only keep 12 months
        Assert.Equal(12, account.ActivityHistory.Count);

        // The oldest month (January 2024) should be removed
        Assert.Equal(0, account.GetActivityMinutes(2024, 1));

        // The newest month (January 2025) should be present
        Assert.Equal(10, account.GetActivityMinutes(2025, 1));
    }

    [Fact]
    public void TestGetNonExistentMonth()
    {
        var account = new Account("testuser", "password", AccessLevel.Normal);

        // Should return 0 for months with no activity
        Assert.Equal(0, account.GetActivityMinutes(2025, 5));
    }

    [Fact]
    public void TestMultipleYears()
    {
        var account = new Account("testuser", "password", AccessLevel.Normal);

        account.AddActivityMinutes(2024, 12, 100);
        account.AddActivityMinutes(2025, 1, 50);

        Assert.Equal(100, account.GetActivityMinutes(2024, 12));
        Assert.Equal(50, account.GetActivityMinutes(2025, 1));
    }
}
