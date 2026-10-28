using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using transactions_ingest.Core.Persistence;
using transactions_ingest.Server.Contracts;

namespace transactions_ingest.Server.Controllers;

[ApiController]
[Route("api/transactions")]
public class TransactionsController(TransactionsDbContext db) : ControllerBase
{
    /// <summary>Newest first, paged server-side (D11), optionally filtered by source and status.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<TransactionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<TransactionDto>>> List([FromQuery] TransactionListQuery query, CancellationToken cancellationToken)
    {
        var transactions = db.Transactions.AsNoTracking();
        if (query.Source is not null)
        {
            transactions = transactions.Where(t => t.SourceSystem == query.Source);
        }
        if (query.Status is not null)
        {
            transactions = transactions.Where(t => t.Status == query.Status);
        }

        var total = await transactions.CountAsync(cancellationToken);
        var items = await transactions
            // Tie-breakers keep page boundaries stable when times are equal.
            .OrderByDescending(t => t.TransactionTime)
            .ThenBy(t => t.SourceSystem)
            .ThenBy(t => t.TransactionId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(t => new TransactionDto(
                t.Id, t.SourceSystem, t.TransactionId, t.LocationCode, t.ProductName, t.Amount, t.TransactionTime, t.Status))
            .ToListAsync(cancellationToken);

        return new PagedResult<TransactionDto>(items, query.Page, query.PageSize, total);
    }

    /// <summary>The audit trail of one transaction, oldest first.</summary>
    [HttpGet("{id:int}/audit")]
    [ProducesResponseType<IReadOnlyList<AuditEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AuditEntryDto>>> Audit(int id, CancellationToken cancellationToken)
    {
        if (!await db.Transactions.AnyAsync(t => t.Id == id, cancellationToken))
        {
            return NotFound();
        }

        return await db.TransactionAudits
            .AsNoTracking()
            .Where(a => a.TransactionPk == id)
            .OrderBy(a => a.Id)
            .Select(a => new AuditEntryDto(a.ChangedAt, a.ChangeType, a.FieldName, a.OldValue, a.NewValue, a.ImportRunId))
            .ToListAsync(cancellationToken);
    }
}
