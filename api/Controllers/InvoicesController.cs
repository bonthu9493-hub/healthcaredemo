using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

/// <summary>Patients see and download only their own invoices. Admins and nurses (front desk) handle payments.</summary>
[ApiController, Route("api/invoices"), Authorize(Roles = $"{Roles.Patient},{Roles.Admin},{Roles.Nurse}")]
public class InvoicesController(AppDbContext db, NotificationService notify, AuditService audit) : ControllerBase
{
    private IQueryable<Invoice> Visible()
    {
        var uid = User.UserId();
        var q = db.Invoices.Include(i => i.Patient.User).Include(i => i.Items).AsQueryable();
        return User.RoleName() == Roles.Patient ? q.Where(i => i.Patient.UserId == uid) : q;
    }

    private static InvoiceDto ToDto(Invoice i) => new(i.Id, $"INV-{i.Number:D6}", i.PatientId, i.Patient.User.FullName, i.Status,
        i.CreatedAt, i.PaidAt, i.PaymentMethod, i.Total,
        i.Items.OrderBy(x => x.Source).ThenBy(x => x.Description)
            .Select(x => new InvoiceItemDto(x.Id, x.Source, x.Description, x.Quantity, x.UnitPrice, x.Quantity * x.UnitPrice)).ToList());

    [HttpGet]
    public async Task<PagedResult<InvoiceDto>> List(string? status, string? q, int page = 1, int pageSize = 20)
    {
        var query = Visible().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(i => i.Status == status);
        if (PagingExtensions.CleanSearch(q) is { } term)
            query = query.Where(i => i.Patient.User.FullName.Contains(term));
        return (await query.OrderByDescending(i => i.CreatedAt).ToPagedAsync(page, pageSize)).Map(ToDto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceDto>> Get(Guid id)
    {
        var invoice = await Visible().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        return invoice is null ? NotFound() : ToDto(invoice);
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id)
    {
        var invoice = await Visible().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        await audit.LogAsync("DownloadInvoice", "Invoice", id.ToString());
        var label = invoice.Status == InvoiceStatus.Paid ? "receipt" : "invoice";
        return File(ReceiptPdf.Build(invoice, invoice.Patient.User.FullName), "application/pdf", $"{label}-INV-{invoice.Number:D6}.pdf");
    }

    /// <summary>Extra charges (for example a dressing). Only on an open invoice.</summary>
    [HttpPost("{id:guid}/items"), Authorize(Roles = $"{Roles.Admin},{Roles.Nurse}")]
    public async Task<ActionResult<InvoiceDto>> AddItem(Guid id, AddInvoiceItemRequest req)
    {
        var invoice = await db.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        if (invoice.Status != InvoiceStatus.Open) return Conflict(new { error = $"A {invoice.Status.ToLowerInvariant()} invoice can't be changed." });

        db.InvoiceItems.Add(new InvoiceItem
        {
            InvoiceId = id, Source = InvoiceSource.Other, Description = req.Description.Trim(), Quantity = req.Quantity, UnitPrice = req.UnitPrice,
        });
        await db.SaveChangesAsync();
        await audit.LogAsync("AddInvoiceItem", "Invoice", id.ToString());
        return ToDto(await Visible().AsNoTracking().FirstAsync(i => i.Id == id));
    }

    /// <summary>Marks the invoice paid. One SQL statement, so it can't be paid twice or paid after being voided.</summary>
    [HttpPost("{id:guid}/pay"), Authorize(Roles = $"{Roles.Admin},{Roles.Nurse}")]
    public async Task<ActionResult<InvoiceDto>> Pay(Guid id, PayInvoiceRequest req)
    {
        var uid = User.UserId();
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return NotFound();
        if (invoice.Items.Count == 0) return BadRequest(new { error = "There is nothing to pay on an empty invoice." });

        var now = DateTimeOffset.UtcNow;
        var rows = await db.Invoices.Where(i => i.Id == id && i.Status == InvoiceStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Paid).SetProperty(i => i.PaidAt, now)
                .SetProperty(i => i.PaymentMethod, req.Method).SetProperty(i => i.PaidByUserId, uid));
        if (rows == 0) return Conflict(new { error = "This invoice is no longer open." });

        await audit.LogAsync("PayInvoice", "Invoice", id.ToString());
        var paid = await db.Invoices.AsNoTracking().Include(i => i.Patient.User).Include(i => i.Items).FirstAsync(i => i.Id == id);
        await notify.SendAsync([paid.Patient.UserId], NotificationType.Appointment,
            $"Payment received for INV-{paid.Number:D6} (Rs. {paid.Total:0.00}). Your receipt is ready to download.");
        return ToDto(paid);
    }

    [HttpPost("{id:guid}/void"), Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Void(Guid id)
    {
        var rows = await db.Invoices.Where(i => i.Id == id && i.Status == InvoiceStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Void));
        if (rows == 0) return await db.Invoices.AnyAsync(i => i.Id == id) ? Conflict(new { error = "Only an open invoice can be voided." }) : NotFound();
        await audit.LogAsync("VoidInvoice", "Invoice", id.ToString());
        return NoContent();
    }
}
