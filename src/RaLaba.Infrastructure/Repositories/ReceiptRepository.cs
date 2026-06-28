using Microsoft.EntityFrameworkCore;
using RaLaba.Domain.Entities;
using RaLaba.Domain.Interfaces;
using RaLaba.Infrastructure.Persistence;

namespace RaLaba.Infrastructure.Repositories;

public class ReceiptRepository : IReceiptRepository
{
    private readonly RaLabaDbContext _context;

    public ReceiptRepository(RaLabaDbContext context)
    {
        _context = context;
    }

    public async Task<Receipt?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _context.Receipts.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<Receipt>> GetAllAsync(CancellationToken ct = default) =>
        await _context.Receipts.OrderByDescending(r => r.Date).ToListAsync(ct);

    public async Task<IReadOnlyList<Receipt>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default) =>
        await _context.Receipts
            .Where(r => r.Date >= from && r.Date <= to)
            .OrderByDescending(r => r.Date)
            .ToListAsync(ct);

    public async Task AddAsync(Receipt receipt, CancellationToken ct = default)
    {
        await _context.Receipts.AddAsync(receipt, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Receipt receipt, CancellationToken ct = default)
    {
        _context.Receipts.Update(receipt);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var receipt = await GetByIdAsync(id, ct);
        if (receipt is null) return;
        _context.Receipts.Remove(receipt);
        await _context.SaveChangesAsync(ct);
    }
}
