using RaLaba.Application.DTOs;
using RaLaba.Application.Interfaces;
using RaLaba.Domain.Entities;
using RaLaba.Domain.Enums;
using RaLaba.Domain.Interfaces;

namespace RaLaba.Application.Services;

public class ReceiptService : IReceiptService
{
    private readonly IReceiptRepository _repository;

    public ReceiptService(IReceiptRepository repository)
    {
        _repository = repository;
    }

    public async Task<ReceiptDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var receipt = await _repository.GetByIdAsync(id, ct);
        return receipt is null ? null : ToDto(receipt);
    }

    public async Task<IReadOnlyList<ReceiptDto>> GetAllAsync(CancellationToken ct = default)
    {
        var receipts = await _repository.GetAllAsync(ct);
        return receipts.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ReceiptDto>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var receipts = await _repository.GetByDateRangeAsync(from, to, ct);
        return receipts.Select(ToDto).ToList();
    }

    public async Task<ReceiptDto> CreateAsync(CreateReceiptDto dto, CancellationToken ct = default)
    {
        var paymentMethod = Enum.Parse<PaymentMethod>(dto.PaymentMethod, ignoreCase: true);
        var type = string.IsNullOrWhiteSpace(dto.Type)
            ? TransactionType.Sale
            : Enum.Parse<TransactionType>(dto.Type, ignoreCase: true);

        var receipt = new Receipt(
            dto.Date, dto.Amount, paymentMethod,
            dto.ReceiptNo, dto.Employee, dto.Customer,
            type, dto.Paid, dto.PaidDate, dto.Notes);

        await _repository.AddAsync(receipt, ct);
        return ToDto(receipt);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => _repository.DeleteAsync(id, ct);

    private static ReceiptDto ToDto(Receipt r) => new(
        r.Id, r.ReceiptNo, r.Date, r.Employee, r.Customer,
        r.Type.ToString(), r.Amount, r.PaymentMethod.ToString(),
        r.Paid, r.PaidDate, r.Notes);
}
