using ExpenseManagement.Api.Common;

namespace ExpenseManagement.Tests.Unit;

public class GuardTests
{
    [Fact]
    public void ToUtc_TreatsUnspecifiedDatesAsUtc()
    {
        var value = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Unspecified);

        var result = Guard.ToUtc(value);

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(10, result.Hour);
    }

    [Theory]
    [InlineData("65f1a2b3c4d5e6f708091a2b", true)]
    [InlineData("abc", false)]
    [InlineData(null, false)]
    public void ValidObjectId_AcceptsOnly24HexCharacters(string? value, bool valid)
    {
        var exception = Record.Exception(() => Guard.ValidObjectId(value, "id"));

        Assert.Equal(valid, exception is null);
    }

    [Fact]
    public void ValidDateRange_RejectsFromAfterTo()
    {
        Assert.Throws<BadRequestException>(() =>
            Guard.ValidDateRange(new DateTime(2025, 2, 1), new DateTime(2025, 1, 1)));
    }
}
