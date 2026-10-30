using System.Net;
using System.Net.Http.Json;
using System.Text;
using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Tests;

[Collection("api")]
public class WardTests(ApiFixture api)
{
    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private async Task<(WardDto ward, BedDto bed)> NewWardWithBed(decimal rate = 1000m, string type = "General")
    {
        var admin = await api.AdminAsync();
        var created = await admin.PostAsJsonAsync("/api/wards", new { name = $"Ward {Unique()}", type, dailyRate = rate });
        created.EnsureSuccessStatusCode();
        var ward = await ApiFixture.Read<WardDto>(created);
        var bedResponse = await admin.PostAsJsonAsync($"/api/wards/{ward.Id}/beds", new { label = "Bed 1" });
        bedResponse.EnsureSuccessStatusCode();
        return (ward, await ApiFixture.Read<BedDto>(bedResponse));
    }

    private async Task<BedDto> AnotherBed(WardDto ward, string label)
    {
        var r = await (await api.AdminAsync()).PostAsJsonAsync($"/api/wards/{ward.Id}/beds", new { label });
        r.EnsureSuccessStatusCode();
        return await ApiFixture.Read<BedDto>(r);
    }

    private static Task<HttpResponseMessage> Admit(TestUser by, TestUser patient, BedDto bed, string reason = "Pneumonia, needs observation") =>
        by.Client.PostAsJsonAsync("/api/admissions", new { patientId = patient.ProfileId, bedId = bed.Id, reason });

    private async Task<WardDto> Board(TestUser viewer, Guid wardId) =>
        (await ApiFixture.Read<List<WardDto>>(await viewer.Client.GetAsync("/api/wards"))).Single(w => w.Id == wardId);

    [Fact]
    public async Task Only_admins_build_wards_and_beds_with_validation()
    {
        var admin = await api.AdminAsync();
        var nurse = await api.NewNurseAsync();
        var (ward, _) = await NewWardWithBed();

        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Client.PostAsJsonAsync("/api/wards", new { name = "Sneaky", type = "ICU", dailyRate = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Client.PostAsJsonAsync($"/api/wards/{ward.Id}/beds", new { label = "Bed 9" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/wards", new { name = ward.Name, type = "ICU", dailyRate = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/wards/{ward.Id}/beds", new { label = "Bed 1" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/wards", new { name = $"W {Unique()}", type = "Spa", dailyRate = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/wards", new { name = "<b>x</b>", type = "ICU", dailyRate = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/wards", new { name = $"W {Unique()}", type = "ICU", dailyRate = -5 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/wards/{Guid.NewGuid()}/beds", new { label = "Bed 1" })).StatusCode);
    }

    [Fact]
    public async Task The_board_is_for_clinical_staff_and_admins_only()
    {
        var patient = await api.NewPatientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.GetAsync("/api/wards")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.NewPharmacistAsync()).Client.GetAsync("/api/wards")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await api.NewNurseAsync()).Client.GetAsync("/api/wards")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await api.NewDoctorAsync()).Client.GetAsync("/api/wards")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonymous().GetAsync("/api/wards")).StatusCode);
    }

    [Fact]
    public async Task A_nurse_admits_a_patient_the_bed_shows_occupied_and_the_patient_is_told()
    {
        var (ward, bed) = await NewWardWithBed();
        var nurse = await api.NewNurseAsync();
        var patient = await api.NewPatientAsync();

        var response = await Admit(nurse, patient, bed);
        var board = await Board(nurse, ward.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal((1, 1), (board.TotalBeds, board.Occupied));
        Assert.Equal(patient.FullName, board.Beds.Single().PatientName);
        Assert.Contains((await ApiFixture.Read<NotificationList>(await patient.Client.GetAsync("/api/notifications"))).Items, n => n.Message.Contains("admitted to"));
        var mine = Assert.Single(await ApiFixture.Read<List<AdmissionDto>>(await patient.Client.GetAsync("/api/admissions/me")));
        Assert.Equal((ward.Name, "Bed 1", null), (mine.WardName, mine.BedLabel, mine.DischargedAt));
    }

    [Fact]
    public async Task A_bed_holds_one_patient_and_a_patient_holds_one_bed()
    {
        var (ward, bed) = await NewWardWithBed();
        var bed2 = await AnotherBed(ward, "Bed 2");
        var nurse = await api.NewNurseAsync();
        var first = await api.NewPatientAsync();
        var second = await api.NewPatientAsync();

        Assert.Equal(HttpStatusCode.Created, (await Admit(nurse, first, bed)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Admit(nurse, second, bed)).StatusCode);   // bed taken
        Assert.Equal(HttpStatusCode.Conflict, (await Admit(nurse, first, bed2)).StatusCode);   // already admitted elsewhere
        Assert.Equal(HttpStatusCode.Created, (await Admit(nurse, second, bed2)).StatusCode);
    }

    [Fact]
    public async Task Two_simultaneous_admissions_to_one_bed_cannot_both_win()
    {
        var (_, bed) = await NewWardWithBed();
        var nurse = await api.NewNurseAsync();
        var a = await api.NewPatientAsync();
        var b = await api.NewPatientAsync();

        var results = await Task.WhenAll(Admit(nurse, a, bed), Admit(nurse, b, bed));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, await api.Db(db => db.Admissions.CountAsync(x => x.BedId == bed.Id && x.DischargedAt == null)));
    }

    [Fact]
    public async Task Admission_rules()
    {
        var (_, bed) = await NewWardWithBed();
        var patient = await api.NewPatientAsync();
        var nurse = await api.NewNurseAsync();
        var treating = await api.NewDoctorAsync();
        var stranger = await api.NewDoctorAsync();
        await api.TreatAsync(patient, treating);

        Assert.Equal(HttpStatusCode.Forbidden, (await Admit(patient, patient, bed)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Admit(await api.NewPharmacistAsync(), patient, bed)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Admit(stranger, patient, bed)).StatusCode);   // doctors admit only their own patients
        Assert.Equal(HttpStatusCode.BadRequest, (await nurse.Client.PostAsJsonAsync("/api/admissions", new { patientId = patient.ProfileId, bedId = Guid.NewGuid(), reason = "Unknown bed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await nurse.Client.PostAsJsonAsync("/api/admissions", new { patientId = Guid.NewGuid(), bedId = bed.Id, reason = "Unknown patient" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Admit(nurse, patient, bed, "<script>")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Admit(treating, patient, bed)).StatusCode);
    }

    [Fact]
    public async Task Discharge_frees_the_bed_records_the_summary_and_bills_the_stay()
    {
        var (ward, bed) = await NewWardWithBed(rate: 1500m);
        var nurse = await api.NewNurseAsync();
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        var admitted = await ApiFixture.Read<AdmissionDto>(await Admit(nurse, patient, bed));

        var discharge = await doctor.Client.PostAsJsonAsync($"/api/admissions/{admitted.Id}/discharge", new { summary = "Recovered well. Follow up in two weeks." });
        var done = await ApiFixture.Read<AdmissionDto>(discharge);

        Assert.Equal(HttpStatusCode.OK, discharge.StatusCode);
        Assert.NotNull(done.DischargedAt);
        Assert.Contains("Follow up", done.DischargeSummary);
        Assert.Equal(0, (await Board(nurse, ward.Id)).Occupied);
        var charge = Assert.Single(Assert.Single(await api.InvoicesAsync(patient.Client, "Open")).Items, i => i.Source == "Bed");
        Assert.Equal((1, 1500m), (charge.Quantity, charge.UnitPrice)); // a same-day stay is one day
        Assert.Contains(ward.Name, charge.Description);
        Assert.Equal(HttpStatusCode.Created, (await Admit(nurse, await api.NewPatientAsync(), bed)).StatusCode); // the bed can be used again
    }

    [Fact]
    public async Task A_longer_stay_is_billed_per_started_day()
    {
        var (_, bed) = await NewWardWithBed(rate: 800m);
        var nurse = await api.NewNurseAsync();
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        var admitted = await ApiFixture.Read<AdmissionDto>(await Admit(nurse, patient, bed));
        await api.Db(async db => await db.Admissions.Where(a => a.Id == admitted.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AdmittedAt, DateTimeOffset.UtcNow.AddDays(-2).AddHours(-3)))); // 2 days 3 hours ago

        await doctor.Client.PostAsJsonAsync($"/api/admissions/{admitted.Id}/discharge", new { summary = "Long recovery, now stable." });

        var charge = Assert.Single(Assert.Single(await api.InvoicesAsync(patient.Client, "Open")).Items, i => i.Source == "Bed");
        Assert.Equal(3, charge.Quantity); // 2 days and 3 hours = 3 started days
        Assert.Equal(2400m, charge.Total);
    }

    [Fact]
    public async Task Only_a_doctor_discharges_with_a_summary_and_only_once()
    {
        var (_, bed) = await NewWardWithBed();
        var nurse = await api.NewNurseAsync();
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        var admitted = await ApiFixture.Read<AdmissionDto>(await Admit(nurse, patient, bed));
        var url = $"/api/admissions/{admitted.Id}/discharge";

        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Client.PostAsJsonAsync(url, new { summary = "Nurses can't do this" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.PostAsJsonAsync(url, new { summary = "Let me out please" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.Client.PostAsJsonAsync(url, new { summary = "ok" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.Client.PostAsJsonAsync(url, new { summary = "<script>alert(1)</script>" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await doctor.Client.PostAsJsonAsync($"/api/admissions/{Guid.NewGuid()}/discharge", new { summary = "Nobody here" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await doctor.Client.PostAsJsonAsync(url, new { summary = "Fit to go home." })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await doctor.Client.PostAsJsonAsync(url, new { summary = "Fit to go home." })).StatusCode);
        Assert.Single(Assert.Single(await api.InvoicesAsync(patient.Client)).Items, i => i.Source == "Bed"); // billed once
    }

    [Fact]
    public async Task Patients_see_only_their_own_admissions()
    {
        var (_, bed) = await NewWardWithBed();
        var nurse = await api.NewNurseAsync();
        var patient = await api.NewPatientAsync();
        var other = await api.NewPatientAsync();
        await Admit(nurse, patient, bed);

        Assert.Single(await ApiFixture.Read<List<AdmissionDto>>(await patient.Client.GetAsync("/api/admissions/me")));
        Assert.Empty(await ApiFixture.Read<List<AdmissionDto>>(await other.Client.GetAsync("/api/admissions/me")));
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Client.GetAsync("/api/admissions/me")).StatusCode);
    }
}

public class PdfTests
{
    private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public void A_document_has_the_structure_every_PDF_reader_expects()
    {
        var text = Text(SimplePdf.Create([new PdfLine("Hello receipt")]));

        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/Type /Catalog", text);
        Assert.Contains("/Count 1", text);
        Assert.Contains("(Hello receipt) Tj", text);
        Assert.Contains("xref", text);
        Assert.Contains("startxref", text);
    }

    [Fact]
    public void The_cross_reference_table_points_at_the_real_object_offsets()
    {
        var bytes = SimplePdf.Create([new PdfLine("One"), new PdfLine("Two")]);
        var text = Text(bytes);
        var xrefAt = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        var entries = text[xrefAt..].Split('\n').Skip(3).Where(l => l.EndsWith(" n ")).Select(l => int.Parse(l[..10])).ToList();

        Assert.NotEmpty(entries);
        for (var i = 0; i < entries.Count; i++)
            Assert.StartsWith($"{i + 1} 0 obj", text[entries[i]..]);
    }

    [Fact]
    public void Special_characters_cannot_break_out_of_a_PDF_string()
    {
        Assert.Equal(@"a\(b\)c\\d", SimplePdf.Escape(@"a(b)c\d"));
        Assert.Equal("tab newline", SimplePdf.Escape("tab\tnewline"));
        var text = Text(SimplePdf.Create([new PdfLine("evil) Tj ET (injected")]));
        Assert.DoesNotContain("(evil) Tj", text);
        Assert.Contains(@"(evil\) Tj ET \(injected) Tj", text);
    }

    [Fact]
    public void Text_outside_Latin1_is_replaced_instead_of_corrupting_the_file()
    {
        var text = Text(SimplePdf.Create([new PdfLine("Rupee ₹ and 日本")]));

        Assert.Contains("(Rupee ? and ??) Tj", text);
    }

    [Fact]
    public void Long_documents_continue_on_more_pages()
    {
        var lines = Enumerable.Range(1, 120).Select(i => new PdfLine($"Line {i}")).ToList();

        var text = Text(SimplePdf.Create(lines));

        Assert.Contains("/Count 3", text);
        Assert.Contains("(Line 120) Tj", text);
    }

    [Fact]
    public void An_empty_document_is_still_a_valid_one_page_PDF()
    {
        var text = Text(SimplePdf.Create([]));

        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("/Count 1", text);
    }

    [Fact]
    public void A_receipt_shows_the_patient_items_total_and_status()
    {
        var invoice = new Invoice { Number = 42, Status = InvoiceStatus.Paid, PaymentMethod = "UPI", PaidAt = DateTimeOffset.UtcNow };
        invoice.Items.Add(new InvoiceItem { Source = "Consultation", Description = "Consultation (Dr Rao)", Quantity = 1, UnitPrice = 500m });
        invoice.Items.Add(new InvoiceItem { Source = "Medicine", Description = "Amoxicillin 500 mg x10", Quantity = 10, UnitPrice = 12.5m });

        var text = Text(ReceiptPdf.Build(invoice, "Pat (One)"));

        Assert.Contains("RECEIPT", text);
        Assert.Contains("INV-000042", text);
        Assert.Contains(@"Pat \(One\)", text);            // brackets in the name are escaped
        Assert.Contains("125.00", text);                 // 10 x 12.50
        Assert.Contains("625.00", text);                 // total: 500 + 125
        Assert.Contains("UPI", text);
    }

    [Fact]
    public void An_unpaid_invoice_is_labelled_so_it_cannot_pass_for_a_receipt()
    {
        var open = new Invoice { Number = 7, Status = InvoiceStatus.Open };
        open.Items.Add(new InvoiceItem { Description = "Test", Quantity = 1, UnitPrice = 1m });
        var voided = new Invoice { Number = 8, Status = InvoiceStatus.Void };

        Assert.Contains("INVOICE - UNPAID", Text(ReceiptPdf.Build(open, "X")));
        Assert.DoesNotContain("RECEIPT", Text(ReceiptPdf.Build(open, "X")));
        Assert.Contains("INVOICE - VOID", Text(ReceiptPdf.Build(voided, "X")));
    }

    [Fact]
    public void Very_long_descriptions_are_shortened_to_keep_the_columns_aligned()
    {
        var invoice = new Invoice { Number = 1, Status = InvoiceStatus.Open };
        invoice.Items.Add(new InvoiceItem { Description = new string('x', 300), Quantity = 1, UnitPrice = 1m });

        var text = Text(ReceiptPdf.Build(invoice, "X"));

        Assert.DoesNotContain(new string('x', 60), text);
        Assert.Contains("...", text);
    }
}
