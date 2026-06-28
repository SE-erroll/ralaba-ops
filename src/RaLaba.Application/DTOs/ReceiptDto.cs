namespace RaLaba.Application.DTOs;

public record ReceiptDto(
    Guid Id,
    string? ReceiptNo,
    DateTime Date,
    string? Employee,
    string? Customer,
    string Type,
    decimal Amount,
    string PaymentMethod,
    bool Paid,
    DateOnly? PaidDate,
    string? Notes
);

public record CreateReceiptDto(
    DateTime Date,
    decimal Amount,
    string PaymentMethod,
    string? ReceiptNo,
    string? Employee,
    string? Customer,
    string? Type,
    bool Paid = true,
    DateOnly? PaidDate = null,
    string? Notes = null
);
