using HospitalApi.Data;
using HospitalApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

/// <summary>Name-only search so nurses can pick a patient to record vitals or issue a token. Returns no other details.</summary>
[ApiController, Route("api/patients/lookup"), Authorize(Roles = $"{Roles.Nurse},{Roles.Admin}")]
public class PatientLookupController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PatientLookupDto>>> Search(string? q)
    {
        q = q?.Trim();
        if (string.IsNullOrEmpty(q) || q.Length < 2 || q.Length > 50)
            return BadRequest(new { error = "Type at least 2 characters." });

        return await db.Patients.AsNoTracking()
            .Where(p => p.User.FullName.Contains(q))
            .OrderBy(p => p.User.FullName).Take(20)
            .Select(p => new PatientLookupDto(p.Id, p.User.FullName)).ToListAsync();
    }
}
