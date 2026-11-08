using Microsoft.AspNetCore.Mvc;
using RaLaba.Application.DTOs;
using RaLaba.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace RaLaba.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ReceiptsController : ControllerBase
{
    private readonly IReceiptService _receiptService;
    private readonly ILogger<ReceiptsController> _logger;

    public ReceiptsController(IReceiptService receiptService, ILogger<ReceiptsController> logger)
    {
        _receiptService = receiptService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReceiptDto>>> GetAll(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var receipts = from.HasValue && to.HasValue
            ? await _receiptService.GetByDateRangeAsync(from.Value, to.Value, ct)
            : await _receiptService.GetAllAsync(ct);

        return Ok(receipts);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReceiptDto>> GetById(Guid id, CancellationToken ct)
    {
        var receipt = await _receiptService.GetByIdAsync(id, ct);
        return receipt is null ? NotFound() : Ok(receipt);
    }

    [HttpPost]
    public async Task<ActionResult<ReceiptDto>> Create(CreateReceiptDto dto, CancellationToken ct)
    {
        var created = await _receiptService.CreateAsync(dto, ct);
        _logger.LogInformation("Receipt created: {ReceiptId}", created.Id);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _receiptService.DeleteAsync(id, ct);
        return NoContent();
    }
}
