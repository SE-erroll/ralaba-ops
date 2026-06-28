using RaLaba.Application.DTOs;

namespace RaLaba.Application.Interfaces;

public interface IReceiptService
{
    Task<ReceiptDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ReceiptDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReceiptDto>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<ReceiptDto> CreateAsync(CreateReceiptDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
