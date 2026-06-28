using RaLaba.Domain.Enums;

namespace RaLaba.Domain.Entities;

public class Receipt
{
    public Guid Id { get; private set; }
    public string? ReceiptNo { get; private set; }
    public DateTime Date { get; private set; }
    public string? Employee { get; private set; }
    public string? Customer { get; private set; }
    public TransactionType Type { get; private set; } = TransactionType.Sale;
    public decimal Amount { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; }
    public bool Paid { get; private set; } = true;
    public DateOnly? PaidDate { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private Receipt() { } // EF Core

    public Receipt(DateTime date, decimal amount, PaymentMethod paymentMethod,
        string? receiptNo = null, string? employee = null, string? customer = null,
        TransactionType type = TransactionType.Sale, bool paid = true,
        DateOnly? paidDate = null, string? notes = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.", nameof(amount));

        Id = Guid.NewGuid();
        Date = date;
        Amount = amount;
        PaymentMethod = paymentMethod;
        ReceiptNo = receiptNo;
        Employee = employee;
        Customer = customer;
        Type = type;
        Paid = paid;
        PaidDate = paidDate;
        Notes = notes;
    }

    public void MarkAsPaid(DateOnly paidDate)
    {
        Paid = true;
        PaidDate = paidDate;
    }

    public void UpdateNotes(string? notes) => Notes = notes;
}
