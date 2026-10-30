using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api/medicines"), Authorize(Roles = $"{Roles.Doctor},{Roles.Pharmacist},{Roles.Admin}")]
public class MedicinesController(AppDbContext db, AttendanceClock clock, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<MedicineDto>> List(string? q, bool low = false, int page = 1, int pageSize = 20)
    {
        var today = clock.Today();
        var soon = today.AddDays(30);
        var query = db.Medicines.AsNoTracking().AsQueryable();
        if (PagingExtensions.CleanSearch(q) is { } term) query = query.Where(m => m.Name.Contains(term));
        if (low) query = query.Where(m => m.Stock <= m.ReorderLevel);
        return await query.OrderBy(m => m.Name)
            .Select(m => new MedicineDto(m.Id, m.Name, m.Strength, m.UnitPrice, m.Stock, m.ReorderLevel, m.ExpiryDate,
                m.Stock <= m.ReorderLevel, m.ExpiryDate < today, m.ExpiryDate >= today && m.ExpiryDate <= soon))
            .ToPagedAsync(page, pageSize);
    }

    [HttpPost, Authorize(Roles = $"{Roles.Pharmacist},{Roles.Admin}")]
    public async Task<ActionResult<MedicineDto>> Create(CreateMedicineRequest req)
    {
        var today = clock.Today();
        if (req.ExpiryDate <= today) return BadRequest(new { error = "Expiry date must be in the future." });
        var name = req.Name.Trim();
        if (await db.Medicines.AnyAsync(m => m.Name == name)) return Conflict(new { error = "A medicine with that name already exists." });

        var m = new Medicine
        {
            Name = name, Strength = req.Strength.Trim(), UnitPrice = req.UnitPrice, Stock = req.Stock,
            ReorderLevel = req.ReorderLevel, ExpiryDate = req.ExpiryDate,
        };
        db.Medicines.Add(m);
        await db.SaveChangesAsync();
        await audit.LogAsync("CreateMedicine", "Medicine", m.Id.ToString());
        return StatusCode(201, ToDto(m, today));
    }

    /// <summary>Adds stock. The addition happens inside SQL, so two people restocking at once can't overwrite each other.</summary>
    [HttpPost("{id:guid}/restock"), Authorize(Roles = $"{Roles.Pharmacist},{Roles.Admin}")]
    public async Task<ActionResult<MedicineDto>> Restock(Guid id, RestockRequest req)
    {
        var today = clock.Today();
        if (req.NewExpiryDate is { } d && d <= today) return BadRequest(new { error = "Expiry date must be in the future." });
        var current = await db.Medicines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (current is null) return NotFound();

        var expiry = req.NewExpiryDate ?? current.ExpiryDate;
        await db.Medicines.Where(m => m.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Stock, m => m.Stock + req.Quantity)
            .SetProperty(m => m.ExpiryDate, expiry));
        await audit.LogAsync("RestockMedicine", "Medicine", id.ToString());
        return ToDto(await db.Medicines.AsNoTracking().FirstAsync(m => m.Id == id), today);
    }

    private static MedicineDto ToDto(Medicine m, DateOnly today) => new(m.Id, m.Name, m.Strength, m.UnitPrice, m.Stock,
        m.ReorderLevel, m.ExpiryDate, m.Stock <= m.ReorderLevel, m.ExpiryDate < today,
        m.ExpiryDate >= today && m.ExpiryDate <= today.AddDays(30));
}
