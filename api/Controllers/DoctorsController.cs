using HospitalApi.Data;
using HospitalApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api/doctors"), Authorize]
public class DoctorsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<DoctorDto>> List() =>
        await db.Doctors.AsNoTracking().OrderBy(d => d.User.FullName)
            .Select(d => new DoctorDto(d.Id, d.User.FullName, d.Specialty)).ToListAsync();
}
