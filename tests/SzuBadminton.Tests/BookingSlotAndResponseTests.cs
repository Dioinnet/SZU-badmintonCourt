using SzuBadminton.Services;
using Xunit;

namespace SzuBadminton.Tests;

public sealed class BookingSlotAndResponseTests
{
    [Fact]
    public void SlotTracker_StopsOnlyCompletedSlot()
    {
        var tracker = new BookingSlotTracker(["19:00-20:00", "20:00-21:00"]);

        tracker.MarkBooked("19:00-20:00");

        Assert.False(tracker.AllBooked);
        Assert.Equal(["20:00-21:00"], tracker.Pending);
    }

    [Fact]
    public void SlotTracker_CompletesAfterEachSlotIsBooked()
    {
        var tracker = new BookingSlotTracker(["19:00-20:00", "20:00-21:00"]);
        tracker.MarkBooked("19:00-20:00");
        tracker.MarkBooked("20:00-21:00");

        Assert.True(tracker.AllBooked);
        Assert.Empty(tracker.Pending);
    }

    [Fact]
    public void SlotTracker_RejectsDuplicateCompletion()
    {
        var tracker = new BookingSlotTracker(["19:00-20:00"]);
        tracker.MarkBooked("19:00-20:00");
        Assert.Throws<InvalidOperationException>(() => tracker.MarkBooked("19:00-20:00"));
    }

    [Theory]
    [InlineData("{\"code\":\"0\",\"msg\":\"预约成功\"}", true)]
    [InlineData("{\"code\":0,\"msg\":\"预约成功\"}", true)]
    [InlineData("{\"code\":\"500\",\"msg\":\"场地已满\"}", false)]
    public void ResponseParser_UsesSchoolResultCode(string json, bool expected)
    {
        Assert.Equal(expected, BookingResponseParser.Parse(json).Success);
    }

    [Fact]
    public void DirectBookingResult_ParsesSuccessfulCourt()
    {
        var result = DirectBookingResult.Parse("{\"success\":true,\"court\":\"羽毛球1号场\",\"message\":\"成功\"}");

        Assert.True(result.Success);
        Assert.False(result.AlreadyBooked);
        Assert.Equal("羽毛球1号场", result.Court);
    }

    [Fact]
    public void DirectBookingResult_TreatsAlreadyBookedAsCompleted()
    {
        var result = DirectBookingResult.Parse("{\"success\":true,\"alreadyBooked\":true,\"message\":\"已预约该场地的相同时间段\"}");

        Assert.True(result.Success);
        Assert.True(result.AlreadyBooked);
    }

    [Fact]
    public void DirectBookingResult_KeepsFailureRetryable()
    {
        var result = DirectBookingResult.Parse("{\"success\":false,\"message\":\"暂时没有可用场地\"}");

        Assert.False(result.Success);
        Assert.False(result.AlreadyBooked);
        Assert.Equal("暂时没有可用场地", result.Message);
    }
}
