using System.Text.Json;
using SzuBadminton.Models;
using SzuBadminton.Services;
using Xunit;

namespace SzuBadminton.Tests;

public sealed class CredentialAndScheduleTests
{
    [Fact]
    public void CredentialProtector_RoundTripsWithoutStoringPlainText()
    {
        const string password = "test-only-password-123";
        var protectedText = CredentialProtector.Protect(password);

        Assert.NotEqual(password, protectedText);
        Assert.DoesNotContain(password, protectedText);
        Assert.Equal(password, CredentialProtector.Unprotect(protectedText));
    }

    [Fact]
    public void SettingsSerialization_ContainsOnlyEncryptedPassword()
    {
        const string password = "never-serialize-this-password";
        var settings = new AppSettings
        {
            Accounts =
            [
                new SavedAccount
                {
                    StudentId = "2024000000",
                    EncryptedPassword = CredentialProtector.Protect(password)
                }
            ]
        };

        var json = JsonSerializer.Serialize(settings);
        Assert.DoesNotContain(password, json);
        Assert.Contains("EncryptedPassword", json);
    }

    [Fact]
    public void Schedule_UsesTodayBeforeReleaseAndTomorrowAfterRelease()
    {
        var before = new DateTime(2026, 10, 29, 11, 0, 0);
        var after = new DateTime(2026, 10, 29, 13, 0, 0);

        Assert.Equal(new DateTime(2026, 10, 29, 12, 30, 0), BookingSchedule.GetNextRun(before));
        Assert.Equal(new DateTime(2026, 10, 30, 12, 30, 0), BookingSchedule.GetNextRun(after));
        Assert.Equal("2026-10-30", BookingSchedule.GetTargetDate(before));
    }

    [Fact]
    public void Schedule_ExactlyAtReleaseUsesToday()
    {
        var release = new DateTime(2026, 10, 29, 12, 30, 0);
        Assert.Equal(release, BookingSchedule.GetNextRun(release));
    }

    [Fact]
    public void BookingStart_IsOneMinuteBeforeRelease()
    {
        Assert.Equal(new TimeSpan(12, 29, 0), BookingSchedule.BookingStartTime);
        Assert.Equal(TimeSpan.FromMinutes(1), BookingSchedule.ReleaseTime - BookingSchedule.BookingStartTime);
    }

    [Fact]
    public void ScheduledRelease_TargetsDayAfterItsRunDate()
    {
        var runAt = BookingSchedule.GetNextBookingStart(new DateTime(2026, 10, 30, 13, 0, 0));

        Assert.Equal(new DateTime(2026, 10, 31, 12, 29, 0), runAt);
        Assert.Equal("2026-11-01", BookingSchedule.GetTargetDate(runAt));
    }

    [Theory]
    [InlineData("2026-10-30T12:28:59", false)]
    [InlineData("2026-10-30T12:29:00", true)]
    [InlineData("2026-10-30T12:38:59", true)]
    [InlineData("2026-10-30T12:39:00", false)]
    public void ScheduledRelease_RunsWithinTenMinuteCatchUpWindow(string nowText, bool expected)
    {
        var runAt = new DateTime(2026, 10, 30, 12, 29, 0);
        Assert.Equal(expected, BookingSchedule.IsWithinRunWindow(DateTime.Parse(nowText), runAt, 10));
    }

    [Theory]
    [InlineData("2026-10-29T12:29:59", "2026-10-30", false)]
    [InlineData("2026-10-29T12:30:00", "2026-10-30", true)]
    [InlineData("2026-10-29T22:00:00", "2026-10-31", false)]
    [InlineData("2026-10-30T12:30:00", "2026-10-31", true)]
    public void TargetRelease_UsesPreviousDayAt1230(string nowText, string targetDate, bool expected)
    {
        Assert.Equal(expected, BookingSchedule.IsTargetReleased(DateTime.Parse(nowText), targetDate));
    }

    [Theory]
    [InlineData("2026-10-29T00:00:00", "2026-10-29,2026-10-30")]
    [InlineData("2026-10-29T12:29:59", "2026-10-29,2026-10-30")]
    [InlineData("2026-10-29T12:30:00", "2026-10-29,2026-10-30")]
    [InlineData("2026-10-29T21:59:59", "2026-10-29,2026-10-30")]
    [InlineData("2026-10-29T22:00:00", "2026-10-30,2026-10-31")]
    [InlineData("2026-10-29T23:59:59", "2026-10-30,2026-10-31")]
    public void DateRules_FollowReleaseAndClosingBoundaries(string nowText, string expected)
    {
        var now = DateTime.Parse(nowText);
        Assert.Equal(expected, string.Join(',', VenueDateRules.GetExpectedDates(now)));
    }

    [Theory]
    [InlineData("2026-10-29T11:00:00", "2026-10-29")]
    [InlineData("2026-10-29T13:00:00", "2026-10-30")]
    [InlineData("2026-10-29T23:00:00", "2026-10-30")]
    public void DateRules_SelectCorrectDefaultTarget(string nowText, string expected)
    {
        Assert.Equal(expected, VenueDateRules.GetDefaultTargetDate(DateTime.Parse(nowText)));
    }
}
