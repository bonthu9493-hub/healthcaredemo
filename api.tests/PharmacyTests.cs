using System.Net;
using System.Net.Http.Json;
using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Tests;

[Collection("api")]
public class ProfileTests(ApiFixture api)
{
    private static object Profile(string? blood = "O+", string allergies = "Penicillin", string phone = "+91 9876543210") => new
    {
        phone, bloodGroup = blood, allergies, chronicConditions = "Asthma", emergencyContactName = "Ravi Kumar", emergencyContactPhone = "+91 9000000000",
    };

    [Fact]
    public async Task A_patient_edits_and_reads_their_own_profile()
    {
        var patient = await api.NewPatientAsync();

        var save = await patient.Client.PutAsJsonAsync("/api/profile/me", Profile());
        var mine = await ApiFixture.Read<PatientProfileDto>(await patient.Client.GetAsync("/api/profile/me"));

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(("O+", "Penicillin", "Asthma"), (mine.BloodGroup, mine.Allergies, mine.ChronicConditions));
        Assert.Equal("Ravi Kumar", mine.EmergencyContactName);
    }

    [Theory]
    [InlineData("Z+")]
    [InlineData("O")]
    [InlineData("<b>")]
    public async Task A_made_up_blood_group_is_refused(string blood)
    {
        var patient = await api.NewPatientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await patient.Client.PutAsJsonAsync("/api/profile/me", Profile(blood))).StatusCode);
    }

    [Fact]
    public async Task Markup_and_bad_phone_numbers_are_refused()
    {
        var patient = await api.NewPatientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await patient.Client.PutAsJsonAsync("/api/profile/me", Profile(allergies: "<script>x</script>"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await patient.Client.PutAsJsonAsync("/api/profile/me", Profile(phone: "call me maybe"))).StatusCode);
    }

    [Fact]
    public async Task Only_patients_have_a_profile_page()
    {
        var doctor = await api.NewDoctorAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.GetAsync("/api/profile/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.AdminAsync()).GetAsync("/api/profile/me")).StatusCode);
    }

    [Fact]
    public async Task The_bedside_summary_shows_allergies_to_the_right_staff_and_hides_contact_details()
    {
        var patient = await api.NewPatientAsync();
        var treating = await api.NewDoctorAsync();
        var stranger = await api.NewDoctorAsync();
        var nurse = await api.NewNurseAsync();
        await patient.Client.PutAsJsonAsync("/api/profile/me", Profile());
        await api.TreatAsync(patient, treating);
        var url = $"/api/patients/{patient.ProfileId}/summary";

        var asDoctor = await treating.Client.GetAsync(url);
        var summary = await ApiFixture.Read<PatientSummaryDto>(asDoctor);
        var raw = await (await nurse.Client.GetAsync(url)).Content.ReadAsStringAsync();

        Assert.Equal(("O+", "Penicillin"), (summary.BloodGroup, summary.Allergies));
        Assert.InRange(summary.Age, 30, 40);
        Assert.Contains("Penicillin", raw);                       // a nurse may see any patient
        Assert.DoesNotContain("9876543210", raw);                 // never the phone number
        Assert.DoesNotContain("dateOfBirth", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.AdminAsync()).GetAsync(url)).StatusCode);
    }
}

[Collection("api")]
public class StaffCreationTests(ApiFixture api)
{
    [Theory]
    [InlineData("Pharmacist")]
    [InlineData("LabTech")]
    [InlineData("Nurse")]
    public async Task An_admin_creates_each_kind_of_staff_who_can_then_log_in(string role)
    {
        var staff = await api.NewStaffAsync(role);
        var me = await ApiFixture.Read<AuthResponse>(await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { staff.Email, staff.Password }));
        Assert.Equal(role, me.Role);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Doctor")]
    [InlineData("Patient")]
    [InlineData("Root")]
    public async Task Powerful_roles_cannot_be_created_through_the_staff_endpoint(string role)
    {
        var admin = await api.AdminAsync();
        var response = await admin.PostAsJsonAsync("/api/admin/staff",
            new { email = $"x{Guid.NewGuid():N}@test.local", password = ApiFixture.StrongPassword, fullName = "Nope Nope", role });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Nobody_but_an_admin_can_use_it()
    {
        var pharmacist = await api.NewPharmacistAsync();
        var nurse = await api.NewNurseAsync();
        var body = new { email = $"x{Guid.NewGuid():N}@test.local", password = ApiFixture.StrongPassword, fullName = "Nope Nope", role = "Nurse" };

        Assert.Equal(HttpStatusCode.Forbidden, (await pharmacist.Client.PostAsJsonAsync("/api/admin/staff", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Client.PostAsJsonAsync("/api/admin/staff", body)).StatusCode);
    }

    [Fact]
    public async Task Pharmacists_and_lab_technicians_have_shift_attendance_too()
    {
        var pharmacist = await api.NewPharmacistAsync();
        var labTech = await api.NewLabTechAsync();

        Assert.Equal(HttpStatusCode.Created, (await pharmacist.Client.PostAsync("/api/attendance/check-in", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await labTech.Client.PostAsync("/api/attendance/check-in", null)).StatusCode);
        var rows = await ApiFixture.Read<List<StaffReportRow>>(await (await api.AdminAsync()).GetAsync("/api/admin/reports/staff-attendance"));
        Assert.Contains(rows, r => r.UserId == pharmacist.UserId && r.Role == "Pharmacist");
        Assert.Contains(rows, r => r.UserId == labTech.UserId && r.Role == "LabTech");
    }
}

[Collection("api")]
public class MedicineTests(ApiFixture api)
{
    [Fact]
    public async Task A_pharmacist_adds_medicines_with_sensible_rules()
    {
        var pharmacist = await api.NewPharmacistAsync();
        var med = await api.NewMedicineAsync(pharmacist, stock: 10, reorder: 20); // below its reorder level from the start

        Assert.True(med.LowStock);
        Assert.False(med.Expired);
        Assert.Equal(HttpStatusCode.Conflict, (await pharmacist.Client.PostAsJsonAsync("/api/medicines",
            new { name = med.Name, strength = "", unitPrice = 5, stock = 1, reorderLevel = 1, expiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await pharmacist.Client.PostAsJsonAsync("/api/medicines",
            new { name = "Old stock", strength = "", unitPrice = 5, stock = 1, reorderLevel = 1, expiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await pharmacist.Client.PostAsJsonAsync("/api/medicines",
            new { name = "Free <b>lunch</b>", strength = "", unitPrice = 5, stock = 1, reorderLevel = 1, expiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await pharmacist.Client.PostAsJsonAsync("/api/medicines",
            new { name = "Negative price", strength = "", unitPrice = -1, stock = 1, reorderLevel = 1, expiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) })).StatusCode);
    }

    [Fact]
    public async Task Only_pharmacists_and_admins_change_the_catalogue_and_only_some_roles_may_read_it()
    {
        var pharmacist = await api.NewPharmacistAsync();
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        var nurse = await api.NewNurseAsync();
        var med = await api.NewMedicineAsync(pharmacist);
        var body = new { name = "Sneaky", strength = "", unitPrice = 5, stock = 1, reorderLevel = 1, expiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) };

        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PostAsJsonAsync("/api/medicines", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PostAsJsonAsync($"/api/medicines/{med.Id}/restock", new { quantity = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.GetAsync("/api/medicines")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Client.GetAsync("/api/medicines")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await doctor.Client.GetAsync($"/api/medicines?q={Uri.EscapeDataString(med.Name)}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await api.AdminAsync()).GetAsync("/api/medicines")).StatusCode);
    }

    [Fact]
    public async Task Restocking_adds_to_the_count_and_can_renew_the_expiry()
    {
        var pharmacist = await api.NewPharmacistAsync();
        var med = await api.NewMedicineAsync(pharmacist, stock: 5);
        var later = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(3));

        var restocked = await ApiFixture.Read<MedicineDto>(await pharmacist.Client.PostAsJsonAsync($"/api/medicines/{med.Id}/restock", new { quantity = 40, newExpiryDate = later }));

        Assert.Equal(45, restocked.Stock);
        Assert.Equal(later, restocked.ExpiryDate);
        Assert.Equal(HttpStatusCode.BadRequest, (await pharmacist.Client.PostAsJsonAsync($"/api/medicines/{med.Id}/restock", new { quantity = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await pharmacist.Client.PostAsJsonAsync($"/api/medicines/{med.Id}/restock",
            new { quantity = 1, newExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await pharmacist.Client.PostAsJsonAsync($"/api/medicines/{Guid.NewGuid()}/restock", new { quantity = 1 })).StatusCode);
    }

    [Fact]
    public async Task Two_simultaneous_restocks_both_count()
    {
        var pharmacist = await api.NewPharmacistAsync();
        var med = await api.NewMedicineAsync(pharmacist, stock: 0);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            pharmacist.Client.PostAsJsonAsync($"/api/medicines/{med.Id}/restock", new { quantity = 10 })));

        Assert.Equal(50, await api.Db(db => db.Medicines.Where(m => m.Id == med.Id).Select(m => m.Stock).FirstAsync()));
    }

    [Fact]
    public async Task Low_stock_and_expiring_medicines_are_flagged_and_searchable()
    {
        var pharmacist = await api.NewPharmacistAsync();
        var low = await api.NewMedicineAsync(pharmacist, stock: 2, reorder: 10);
        var soon = await api.NewMedicineAsync(pharmacist, expiry: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)));
        var fine = await api.NewMedicineAsync(pharmacist, stock: 500, reorder: 10);

        var all = (await ApiFixture.ReadPaged<MedicineDto>(await pharmacist.Client.GetAsync("/api/medicines?pageSize=100"))).Items;
        var onlyLow = (await ApiFixture.ReadPaged<MedicineDto>(await pharmacist.Client.GetAsync("/api/medicines?low=true&pageSize=100"))).Items;

        Assert.True(all.Single(m => m.Id == soon.Id).ExpiringSoon);
        Assert.False(all.Single(m => m.Id == fine.Id).ExpiringSoon);
        Assert.Contains(onlyLow, m => m.Id == low.Id);
        Assert.DoesNotContain(onlyLow, m => m.Id == fine.Id);
    }
}

[Collection("api")]
public class PrescriptionTests(ApiFixture api)
{
    private static object Rx(Guid patientId, params (Guid medicine, int qty)[] items) => new
    {
        patientId, notes = "After food",
        items = items.Select(i => new { medicineId = i.medicine, dosage = "1 tablet twice daily", durationDays = 5, quantity = i.qty }).ToArray(),
    };

    private async Task<(TestUser patient, TestUser doctor, TestUser pharmacist)> Setup()
    {
        var patient = await api.NewPatientAsync();
        var doctor = await api.NewDoctorAsync();
        var pharmacist = await api.NewPharmacistAsync();
        await api.TreatAsync(patient, doctor);
        return (patient, doctor, pharmacist);
    }

    private static async Task<PrescriptionDto> Write(TestUser doctor, Guid patientId, params (Guid, int)[] items) =>
        await ApiFixture.Read<PrescriptionDto>(await doctor.Client.PostAsJsonAsync("/api/prescriptions", Rx(patientId, items)));

    [Fact]
    public async Task A_treating_doctor_prescribes_and_everyone_is_told()
    {
        var (patient, doctor, pharmacist) = await Setup();
        var med = await api.NewMedicineAsync(pharmacist);

        var response = await doctor.Client.PostAsJsonAsync("/api/prescriptions", Rx(patient.ProfileId, (med.Id, 10)));
        var rx = await ApiFixture.Read<PrescriptionDto>(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(("Pending", patient.FullName), (rx.Status, rx.PatientName));
        Assert.Equal(med.Name, Assert.Single(rx.Items).MedicineName);
        var patientInbox = await ApiFixture.Read<NotificationList>(await patient.Client.GetAsync("/api/notifications"));
        Assert.Contains(patientInbox.Items, n => n.Message.Contains("new prescription"));
        var pharmacistInbox = await ApiFixture.Read<NotificationList>(await pharmacist.Client.GetAsync("/api/notifications"));
        Assert.Contains(pharmacistInbox.Items, n => n.Message.Contains("to dispense"));
    }

    [Fact]
    public async Task Prescribing_rules()
    {
        var (patient, doctor, pharmacist) = await Setup();
        var stranger = await api.NewDoctorAsync();
        var med = await api.NewMedicineAsync(pharmacist);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.PostAsJsonAsync("/api/prescriptions", Rx(patient.ProfileId, (med.Id, 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.PostAsJsonAsync("/api/prescriptions", Rx(patient.ProfileId, (med.Id, 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pharmacist.Client.PostAsJsonAsync("/api/prescriptions", Rx(patient.ProfileId, (med.Id, 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.Client.PostAsJsonAsync("/api/prescriptions", Rx(patient.ProfileId, (Guid.NewGuid(), 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.Client.PostAsJsonAsync("/api/prescriptions", Rx(patient.ProfileId, (med.Id, 1), (med.Id, 2)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.Client.PostAsJsonAsync("/api/prescriptions", Rx(patient.ProfileId))).StatusCode);   // no items
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.Client.PostAsJsonAsync("/api/prescriptions", Rx(patient.ProfileId, (med.Id, 0)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.Client.PostAsJsonAsync("/api/prescriptions", new
        {
            patientId = patient.ProfileId, items = new[] { new { medicineId = med.Id, dosage = "<script>", durationDays = 1, quantity = 1 } },
        })).StatusCode);
    }

    [Fact]
    public async Task An_expired_medicine_cannot_be_prescribed()
    {
        var (patient, doctor, pharmacist) = await Setup();
        var med = await api.NewMedicineAsync(pharmacist);
        await api.Db(async db => await db.Medicines.Where(m => m.Id == med.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ExpiryDate, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)))));

        var response = await doctor.Client.PostAsJsonAsync("/api/prescriptions", Rx(patient.ProfileId, (med.Id, 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("expired", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_recorded_allergy_blocks_the_prescription_unless_the_doctor_overrides_on_purpose()
    {
        var (patient, doctor, pharmacist) = await Setup();
        var penicillin = await api.NewMedicineAsync(pharmacist, name: $"Penicillin V {Guid.NewGuid():N}"[..24]);
        await patient.Client.PutAsJsonAsync("/api/profile/me", new
        {
            phone = "+91 9876543210", bloodGroup = "A+", allergies = "pollen, Penicillin", chronicConditions = "", emergencyContactName = "", emergencyContactPhone = "",
        });

        var blocked = await doctor.Client.PostAsJsonAsync("/api/prescriptions", Rx(patient.ProfileId, (penicillin.Id, 10)));
        var body = await blocked.Content.ReadAsStringAsync();
        var overridden = await doctor.Client.PostAsJsonAsync("/api/prescriptions", new
        {
            patientId = patient.ProfileId, overrideAllergyWarning = true,
            items = new[] { new { medicineId = penicillin.Id, dosage = "1 tablet daily", durationDays = 3, quantity = 3 } },
        });

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("allergyConflict", body);
        Assert.Contains("Allergy warning", body);
        Assert.Equal(HttpStatusCode.Created, overridden.StatusCode);
        // The override is on the audit trail.
        Assert.True(await api.Db(db => db.AuditLogs.AnyAsync(a => a.Action == "PrescribeAllergyOverride" && a.UserId == doctor.UserId)));
    }

    [Fact]
    public async Task Unrelated_medicines_are_not_blocked_by_an_allergy()
    {
        var (patient, doctor, pharmacist) = await Setup();
        var paracetamol = await api.NewMedicineAsync(pharmacist, name: $"Paracetamol {Guid.NewGuid():N}"[..20]);
        await patient.Client.PutAsJsonAsync("/api/profile/me", new
        {
            phone = "+91 9876543210", bloodGroup = "A+", allergies = "Penicillin", chronicConditions = "", emergencyContactName = "", emergencyContactPhone = "",
        });

        Assert.Equal(HttpStatusCode.Created, (await doctor.Client.PostAsJsonAsync("/api/prescriptions", Rx(patient.ProfileId, (paracetamol.Id, 5)))).StatusCode);
    }

    [Fact]
    public async Task Prescriptions_are_visible_only_to_the_patient_the_writing_doctor_and_pharmacists()
    {
        var (patient, doctor, pharmacist) = await Setup();
        var other = await api.NewPatientAsync();
        var otherDoctor = await api.NewDoctorAsync();
        var med = await api.NewMedicineAsync(pharmacist);
        var rx = await Write(doctor, patient.ProfileId, (med.Id, 5));

        async Task<List<PrescriptionDto>> See(TestUser u) => (await ApiFixture.ReadPaged<PrescriptionDto>(await u.Client.GetAsync("/api/prescriptions?pageSize=100"))).Items;

        Assert.Contains(await See(patient), p => p.Id == rx.Id);
        Assert.Contains(await See(doctor), p => p.Id == rx.Id);
        Assert.Contains(await See(pharmacist), p => p.Id == rx.Id);
        Assert.DoesNotContain(await See(other), p => p.Id == rx.Id);
        Assert.DoesNotContain(await See(otherDoctor), p => p.Id == rx.Id);
        foreach (var blocked in new[] { await api.NewNurseAsync(), await api.NewLabTechAsync() })
            Assert.Equal(HttpStatusCode.Forbidden, (await blocked.Client.GetAsync("/api/prescriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.AdminAsync()).GetAsync("/api/prescriptions")).StatusCode);
    }

    [Fact]
    public async Task Dispensing_takes_stock_marks_it_done_and_bills_the_patient_on_one_invoice()
    {
        var (patient, doctor, pharmacist) = await Setup();
        var a = await api.NewMedicineAsync(pharmacist, stock: 50, price: 12.50m);
        var b = await api.NewMedicineAsync(pharmacist, stock: 20, price: 40m);
        var rx = await Write(doctor, patient.ProfileId, (a.Id, 10), (b.Id, 2));

        var dispense = await pharmacist.Client.PostAsync($"/api/prescriptions/{rx.Id}/dispense", null);
        var done = await ApiFixture.Read<PrescriptionDto>(dispense);

        Assert.Equal(HttpStatusCode.OK, dispense.StatusCode);
        Assert.Equal("Dispensed", done.Status);
        Assert.Equal(40, await api.Db(db => db.Medicines.Where(m => m.Id == a.Id).Select(m => m.Stock).FirstAsync()));
        Assert.Equal(18, await api.Db(db => db.Medicines.Where(m => m.Id == b.Id).Select(m => m.Stock).FirstAsync()));

        var invoice = Assert.Single(await api.InvoicesAsync(patient.Client, "Open"));
        Assert.Equal(2, invoice.Items.Count(i => i.Source == "Medicine"));
        Assert.Equal(10 * 12.50m + 2 * 40m, invoice.Total);

        var inbox = await ApiFixture.Read<NotificationList>(await patient.Client.GetAsync("/api/notifications"));
        Assert.Contains(inbox.Items, n => n.Message.Contains("has been dispensed"));
    }

    [Fact]
    public async Task Only_pharmacists_dispense_and_only_once()
    {
        var (patient, doctor, pharmacist) = await Setup();
        var med = await api.NewMedicineAsync(pharmacist);
        var rx = await Write(doctor, patient.ProfileId, (med.Id, 5));

        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PostAsync($"/api/prescriptions/{rx.Id}/dispense", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.PostAsync($"/api/prescriptions/{rx.Id}/dispense", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await pharmacist.Client.PostAsync($"/api/prescriptions/{rx.Id}/dispense", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await pharmacist.Client.PostAsync($"/api/prescriptions/{rx.Id}/dispense", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await pharmacist.Client.PostAsync($"/api/prescriptions/{Guid.NewGuid()}/dispense", null)).StatusCode);
    }

    [Fact]
    public async Task Not_enough_stock_changes_nothing_at_all()
    {
        var (patient, doctor, pharmacist) = await Setup();
        var plenty = await api.NewMedicineAsync(pharmacist, stock: 100);
        var scarce = await api.NewMedicineAsync(pharmacist, stock: 1);
        var rx = await Write(doctor, patient.ProfileId, (plenty.Id, 10), (scarce.Id, 5)); // second line cannot be met

        var dispense = await pharmacist.Client.PostAsync($"/api/prescriptions/{rx.Id}/dispense", null);

        Assert.Equal(HttpStatusCode.Conflict, dispense.StatusCode);
        Assert.Contains("Not enough stock", await dispense.Content.ReadAsStringAsync());
        // All or nothing: the first line's stock is back, the prescription is still pending, nobody was billed.
        Assert.Equal(100, await api.Db(db => db.Medicines.Where(m => m.Id == plenty.Id).Select(m => m.Stock).FirstAsync()));
        Assert.Equal("Pending", await api.Db(db => db.Prescriptions.Where(p => p.Id == rx.Id).Select(p => p.Status).FirstAsync()));
        Assert.Empty(await api.InvoicesAsync(patient.Client));
    }

    [Fact]
    public async Task Two_pharmacists_clicking_at_once_dispense_only_once()
    {
        var (patient, doctor, pharmacist) = await Setup();
        var second = await api.NewPharmacistAsync();
        var med = await api.NewMedicineAsync(pharmacist, stock: 100);
        var rx = await Write(doctor, patient.ProfileId, (med.Id, 10));

        var results = await Task.WhenAll(
            pharmacist.Client.PostAsync($"/api/prescriptions/{rx.Id}/dispense", null),
            second.Client.PostAsync($"/api/prescriptions/{rx.Id}/dispense", null));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(90, await api.Db(db => db.Medicines.Where(m => m.Id == med.Id).Select(m => m.Stock).FirstAsync())); // taken once
        Assert.Single((await api.InvoicesAsync(patient.Client)).Single().Items);
    }

    [Fact]
    public async Task Expired_stock_is_never_handed_out()
    {
        var (patient, doctor, pharmacist) = await Setup();
        var med = await api.NewMedicineAsync(pharmacist);
        var rx = await Write(doctor, patient.ProfileId, (med.Id, 5));
        await api.Db(async db => await db.Medicines.Where(m => m.Id == med.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ExpiryDate, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)))));

        var dispense = await pharmacist.Client.PostAsync($"/api/prescriptions/{rx.Id}/dispense", null);

        Assert.Equal(HttpStatusCode.Conflict, dispense.StatusCode);
        Assert.Equal(100, await api.Db(db => db.Medicines.Where(m => m.Id == med.Id).Select(m => m.Stock).FirstAsync()));
    }

    [Fact]
    public async Task A_doctor_can_cancel_a_pending_prescription_which_then_cannot_be_dispensed()
    {
        var (patient, doctor, pharmacist) = await Setup();
        var otherDoctor = await api.NewDoctorAsync();
        var med = await api.NewMedicineAsync(pharmacist);
        var rx = await Write(doctor, patient.ProfileId, (med.Id, 5));

        Assert.Equal(HttpStatusCode.NotFound, (await otherDoctor.Client.PostAsync($"/api/prescriptions/{rx.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await doctor.Client.PostAsync($"/api/prescriptions/{rx.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await pharmacist.Client.PostAsync($"/api/prescriptions/{rx.Id}/dispense", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await doctor.Client.PostAsync($"/api/prescriptions/{rx.Id}/cancel", null)).StatusCode); // already cancelled
    }

    [Fact]
    public async Task Dispensing_the_last_of_a_medicine_warns_the_pharmacists()
    {
        var (patient, doctor, pharmacist) = await Setup();
        var med = await api.NewMedicineAsync(pharmacist, stock: 10, reorder: 5);
        var rx = await Write(doctor, patient.ProfileId, (med.Id, 8)); // leaves 2, below the reorder level of 5

        await pharmacist.Client.PostAsync($"/api/prescriptions/{rx.Id}/dispense", null);

        var inbox = await ApiFixture.Read<NotificationList>(await pharmacist.Client.GetAsync("/api/notifications"));
        Assert.Contains(inbox.Items, n => n.Message.StartsWith("Low stock") && n.Message.Contains(med.Name));
    }
}
