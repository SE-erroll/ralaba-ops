using RaLaba.Domain.Entities;

namespace RaLaba.Domain.Interfaces;

public interface IReceiptRepository
{
    Task<Receipt?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Receipt>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<Receipt>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Receipt receipt, CancellationToken ct = default);
    Task UpdateAsync(Receipt receipt, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
