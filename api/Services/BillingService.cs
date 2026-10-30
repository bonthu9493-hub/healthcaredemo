using HospitalApi.Data;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Services;

/// <summary>Puts a charge on the patient's open invoice, opening a new invoice if there isn't one.</summary>
public class BillingService(AppDbContext db)
{
    public async Task AddChargeAsync(Guid patientId, string source, string description, int quantity, decimal unitPrice)
    {
        var invoice = await db.Invoices
            .Where(i => i.PatientId == patientId && i.Status == InvoiceStatus.Open)
            .OrderByDescending(i => i.CreatedAt).FirstOrDefaultAsync();
        if (invoice is null)
        {
            invoice = new Invoice { PatientId = patientId };
            db.Invoices.Add(invoice);
        }
        db.InvoiceItems.Add(new InvoiceItem
        {
            Invoice = invoice, Source = source, Quantity = quantity, UnitPrice = unitPrice,
            Description = description.Length > 200 ? description[..200] : description,
        });
        // Saved straight away so that the next charge in the same request finds this invoice instead of opening a second one.
        await db.SaveChangesAsync();
    }
}
