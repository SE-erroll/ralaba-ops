using RaLaba.Domain.Entities;
using RaLaba.Domain.Enums;
using Xunit;

namespace RaLaba.UnitTests;

public class ReceiptTests
{
    [Fact]
    public void Constructor_WithValidData_CreatesReceipt()
    {
        var receipt = new Receipt(DateTime.UtcNow, 500m, PaymentMethod.Cash, customer: "Juan");

        Assert.NotEqual(Guid.Empty, receipt.Id);
        Assert.Equal(500m, receipt.Amount);
        Assert.Equal(PaymentMethod.Cash, receipt.PaymentMethod);
    }

    [Fact]
    public void Constructor_WithZeroAmount_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new Receipt(DateTime.UtcNow, 0m, PaymentMethod.Cash));
    }

    [Fact]
    public void MarkAsPaid_SetsPaidAndPaidDate()
    {
        var receipt = new Receipt(DateTime.UtcNow, 250m, PaymentMethod.GCash, paid: false);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        receipt.MarkAsPaid(date);

        Assert.True(receipt.Paid);
        Assert.Equal(date, receipt.PaidDate);
    }
}
