using System.Net;
using System.Net.Http.Json;
using System.Text;
using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Tests;

[Collection("api")]
public class LabTests(ApiFixture api)
{
    private async Task<LabTestDto> AnyTest(TestUser asker) =>
        (await ApiFixture.Read<List<LabTestDto>>(await asker.Client.GetAsync("/api/labs/tests"))).First();

    private async Task<(TestUser patient, TestUser doctor, TestUser tech, LabOrderDto order)> Ordered()
    {
        var patient = await api.NewPatientAsync();
        var doctor = await api.NewDoctorAsync();
        var tech = await api.NewLabTechAsync();
        await api.TreatAsync(patient, doctor);
        var test = await AnyTest(doctor);
        var response = await doctor.Client.PostAsJsonAsync("/api/labs/orders", new { patientId = patient.ProfileId, labTestId = test.Id, notes = "Fasting sample" });
        response.EnsureSuccessStatusCode();
        return (patient, doctor, tech, await ApiFixture.Read<LabOrderDto>(response));
    }

    [Fact]
    public async Task The_test_catalogue_starts_with_common_tests_and_is_extended_only_by_lab_staff()
    {
        var doctor = await api.NewDoctorAsync();
        var tech = await api.NewLabTechAsync();
        var patient = await api.NewPatientAsync();

        var tests = await ApiFixture.Read<List<LabTestDto>>(await doctor.Client.GetAsync("/api/labs/tests"));
        Assert.True(tests.Count >= 8);
        Assert.Contains(tests, t => t.Name.Contains("CBC"));

        var name = $"Vitamin D {Guid.NewGuid():N}"[..16];
        Assert.Equal(HttpStatusCode.Created, (await tech.Client.PostAsJsonAsync("/api/labs/tests", new { name, price = 900 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await tech.Client.PostAsJsonAsync("/api/labs/tests", new { name, price = 900 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PostAsJsonAsync("/api/labs/tests", new { name = "Sneaky", price = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.GetAsync("/api/labs/tests")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await tech.Client.PostAsJsonAsync("/api/labs/tests", new { name = "Free test", price = 0 })).StatusCode);
    }

    [Fact]
    public async Task A_treating_doctor_orders_a_test_and_the_lab_is_told()
    {
        var (patient, doctor, tech, order) = await Ordered();

        Assert.Equal(("Ordered", patient.FullName), (order.Status, order.PatientName));
        Assert.True(order.Price > 0);
        var inbox = await ApiFixture.Read<NotificationList>(await tech.Client.GetAsync("/api/notifications"));
        Assert.Contains(inbox.Items, n => n.Message.StartsWith("New lab order"));
    }

    [Fact]
    public async Task Ordering_rules()
    {
        var patient = await api.NewPatientAsync();
        var doctor = await api.NewDoctorAsync();
        var stranger = await api.NewDoctorAsync();
        await api.TreatAsync(patient, doctor);
        var test = await AnyTest(doctor);
        var body = new { patientId = patient.ProfileId, labTestId = test.Id, notes = "" };

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.PostAsJsonAsync("/api/labs/orders", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.PostAsJsonAsync("/api/labs/orders", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.NewNurseAsync()).Client.PostAsJsonAsync("/api/labs/orders", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.Client.PostAsJsonAsync("/api/labs/orders", new { patientId = patient.ProfileId, labTestId = Guid.NewGuid(), notes = "" })).StatusCode);
    }

    [Fact]
    public async Task The_result_notifies_both_people_bills_the_test_and_can_only_be_entered_once()
    {
        var (patient, doctor, tech, order) = await Ordered();

        var done = await tech.Client.PostAsJsonAsync($"/api/labs/orders/{order.Id}/result", new { resultText = "Haemoglobin 9.1 g/dL (low)", abnormal = true });
        var result = await ApiFixture.Read<LabOrderDto>(done);

        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.Equal(("Completed", true), (result.Status, result.Abnormal));
        Assert.Contains("Haemoglobin", result.ResultText);
        Assert.Contains((await ApiFixture.Read<NotificationList>(await patient.Client.GetAsync("/api/notifications"))).Items, n => n.Message.Contains("result is ready"));
        Assert.Contains((await ApiFixture.Read<NotificationList>(await doctor.Client.GetAsync("/api/notifications"))).Items, n => n.Message.Contains("ABNORMAL"));

        var invoice = Assert.Single(await api.InvoicesAsync(patient.Client, "Open"));
        var line = Assert.Single(invoice.Items, i => i.Source == "Lab");
        Assert.Equal(order.Price, line.UnitPrice);

        Assert.Equal(HttpStatusCode.Conflict, (await tech.Client.PostAsJsonAsync($"/api/labs/orders/{order.Id}/result", new { resultText = "Second try", abnormal = false })).StatusCode);
        Assert.Single(Assert.Single(await api.InvoicesAsync(patient.Client, "Open")).Items, i => i.Source == "Lab"); // billed once
    }

    [Fact]
    public async Task Two_technicians_entering_a_result_at_once_bill_only_once()
    {
        var (patient, _, tech, order) = await Ordered();
        var other = await api.NewLabTechAsync();
        object body = new { resultText = "Normal", abnormal = false };

        var results = await Task.WhenAll(
            tech.Client.PostAsJsonAsync($"/api/labs/orders/{order.Id}/result", body),
            other.Client.PostAsJsonAsync($"/api/labs/orders/{order.Id}/result", body));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Single((await api.InvoicesAsync(patient.Client)).Single().Items, i => i.Source == "Lab");
    }

    [Fact]
    public async Task Only_lab_technicians_enter_results()
    {
        var (patient, doctor, _, order) = await Ordered();
        var body = new { resultText = "Looks fine", abnormal = false };

        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PostAsJsonAsync($"/api/labs/orders/{order.Id}/result", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.PostAsJsonAsync($"/api/labs/orders/{order.Id}/result", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.AdminAsync()).PostAsJsonAsync($"/api/labs/orders/{order.Id}/result", body)).StatusCode);
    }

    [Fact]
    public async Task Result_text_is_validated()
    {
        var (_, _, tech, order) = await Ordered();

        Assert.Equal(HttpStatusCode.BadRequest, (await tech.Client.PostAsJsonAsync($"/api/labs/orders/{order.Id}/result", new { resultText = "<script>alert(1)</script>", abnormal = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await tech.Client.PostAsJsonAsync($"/api/labs/orders/{order.Id}/result", new { resultText = "x", abnormal = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tech.Client.PostAsJsonAsync($"/api/labs/orders/{Guid.NewGuid()}/result", new { resultText = "Fine", abnormal = false })).StatusCode);
    }

    [Fact]
    public async Task Lab_orders_are_visible_only_to_the_patient_the_ordering_doctor_and_the_lab()
    {
        var (patient, doctor, tech, order) = await Ordered();
        var otherPatient = await api.NewPatientAsync();
        var otherDoctor = await api.NewDoctorAsync();

        async Task<bool> Sees(TestUser u) =>
            (await ApiFixture.ReadPaged<LabOrderDto>(await u.Client.GetAsync("/api/labs/orders?pageSize=100"))).Items.Any(o => o.Id == order.Id);

        Assert.True(await Sees(patient));
        Assert.True(await Sees(doctor));
        Assert.True(await Sees(tech));
        Assert.False(await Sees(otherPatient));
        Assert.False(await Sees(otherDoctor));
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.NewNurseAsync()).Client.GetAsync("/api/labs/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.AdminAsync()).GetAsync("/api/labs/orders")).StatusCode);
    }

    [Fact]
    public async Task A_cancelled_order_cannot_receive_a_result_and_costs_nothing()
    {
        var (patient, doctor, tech, order) = await Ordered();
        var otherDoctor = await api.NewDoctorAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await otherDoctor.Client.PostAsync($"/api/labs/orders/{order.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await doctor.Client.PostAsync($"/api/labs/orders/{order.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await tech.Client.PostAsJsonAsync($"/api/labs/orders/{order.Id}/result", new { resultText = "Too late", abnormal = false })).StatusCode);
        Assert.Empty(await api.InvoicesAsync(patient.Client));
    }
}

[Collection("api")]
public class BillingTests(ApiFixture api)
{
    /// <summary>A patient with one open invoice (a completed appointment, billed at the doctor's fee).</summary>
    private async Task<(TestUser patient, TestUser doctor, InvoiceDto invoice)> WithInvoice()
    {
        var patient = await api.NewPatientAsync();
        var doctor = await api.NewDoctorAsync();
        var booked = await ApiFixture.Read<AppointmentDto>(await api.BookAsync(patient, doctor.ProfileId, TestTime.InDays(30)));
        var done = await doctor.Client.PatchAsJsonAsync($"/api/appointments/{booked.Id}/status", new { status = "Completed" });
        done.EnsureSuccessStatusCode();
        return (patient, doctor, Assert.Single(await api.InvoicesAsync(patient.Client, "Open")));
    }

    [Fact]
    public async Task An_attended_appointment_is_billed_at_the_doctors_fee()
    {
        var (_, doctor, invoice) = await WithInvoice();

        var line = Assert.Single(invoice.Items);
        Assert.Equal("Consultation", line.Source);
        Assert.Equal(500m, line.UnitPrice); // the default fee
        Assert.Contains(doctor.FullName, line.Description);
        Assert.StartsWith("INV-", invoice.Number);
    }

    [Fact]
    public async Task A_doctors_own_fee_is_used()
    {
        var admin = await api.AdminAsync();
        var email = $"fee.{Guid.NewGuid():N}@test.local";
        var created = await admin.PostAsJsonAsync("/api/admin/doctors",
            new { email, password = ApiFixture.StrongPassword, fullName = "Dr Pricey", specialty = "Surgery", consultationFee = 1200 });
        var newDoctor = await ApiFixture.Read<DoctorDto>(created);
        Assert.Equal(1200m, newDoctor.ConsultationFee);
        var doctorClient = await api.LoginAsync(email, ApiFixture.StrongPassword);
        var doctorId = newDoctor.Id;
        var patient = await api.NewPatientAsync();
        var booked = await ApiFixture.Read<AppointmentDto>(await api.BookAsync(patient, doctorId, TestTime.InDays(31)));

        await doctorClient.PatchAsJsonAsync($"/api/appointments/{booked.Id}/status", new { status = "Completed" });

        Assert.Equal(1200m, Assert.Single((await api.InvoicesAsync(patient.Client)).Single().Items).UnitPrice);
    }

    [Fact]
    public async Task Cancelled_and_no_show_appointments_are_not_billed()
    {
        var patient = await api.NewPatientAsync();
        var doctor = await api.NewDoctorAsync();
        var booked = await ApiFixture.Read<AppointmentDto>(await api.BookAsync(patient, doctor.ProfileId, TestTime.InDays(30)));
        await patient.Client.PatchAsJsonAsync($"/api/appointments/{booked.Id}/status", new { status = "Cancelled" });
        var past = await api.AddPastAppointmentAsync(patient, doctor, TimeSpan.FromHours(3));
        await doctor.Client.PatchAsJsonAsync($"/api/appointments/{past}/status", new { status = "NoShow" });

        Assert.Empty(await api.InvoicesAsync(patient.Client));
    }

    [Fact]
    public async Task Several_charges_in_a_row_land_on_one_open_invoice()
    {
        var (patient, doctor, first) = await WithInvoice();
        var again = await ApiFixture.Read<AppointmentDto>(await api.BookAsync(patient, doctor.ProfileId, TestTime.InDays(35)));
        await doctor.Client.PatchAsJsonAsync($"/api/appointments/{again.Id}/status", new { status = "Completed" });

        var open = Assert.Single(await api.InvoicesAsync(patient.Client, "Open"));

        Assert.Equal(first.Id, open.Id);
        Assert.Equal(2, open.Items.Count);
        Assert.Equal(1000m, open.Total);
    }

    [Fact]
    public async Task Invoices_are_private_to_the_patient_and_the_front_desk()
    {
        var (patient, doctor, invoice) = await WithInvoice();
        var other = await api.NewPatientAsync();
        var nurse = await api.NewNurseAsync();

        Assert.Contains(await api.InvoicesAsync(patient.Client), i => i.Id == invoice.Id);
        Assert.DoesNotContain(await api.InvoicesAsync(other.Client), i => i.Id == invoice.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/invoices/{invoice.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await nurse.Client.GetAsync($"/api/invoices/{invoice.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await api.AdminAsync()).GetAsync($"/api/invoices/{invoice.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.GetAsync("/api/invoices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.NewPharmacistAsync()).Client.GetAsync("/api/invoices")).StatusCode);
    }

    [Fact]
    public async Task Paying_marks_it_paid_notifies_the_patient_and_freezes_the_invoice()
    {
        var (patient, doctor, invoice) = await WithInvoice();
        var nurse = await api.NewNurseAsync();

        var pay = await nurse.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { method = "UPI" });
        var paid = await ApiFixture.Read<InvoiceDto>(pay);

        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        Assert.Equal(("Paid", "UPI"), (paid.Status, paid.PaymentMethod));
        Assert.NotNull(paid.PaidAt);
        Assert.Contains((await ApiFixture.Read<NotificationList>(await patient.Client.GetAsync("/api/notifications"))).Items, n => n.Message.Contains("Payment received"));
        // Frozen: can't pay again, add to, or void a paid invoice.
        Assert.Equal(HttpStatusCode.Conflict, (await nurse.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { method = "Cash" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await nurse.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/items", new { description = "Late add", quantity = 1, unitPrice = 50 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await (await api.AdminAsync()).PostAsync($"/api/invoices/{invoice.Id}/void", null)).StatusCode);
        // New charges go onto a fresh invoice.
        var again = await ApiFixture.Read<AppointmentDto>(await api.BookAsync(patient, doctor.ProfileId, TestTime.InDays(36)));
        await doctor.Client.PatchAsJsonAsync($"/api/appointments/{again.Id}/status", new { status = "Completed" });
        Assert.NotEqual(invoice.Id, Assert.Single(await api.InvoicesAsync(patient.Client, "Open")).Id);
    }

    [Fact]
    public async Task Payment_rules()
    {
        var (patient, doctor, invoice) = await WithInvoice();
        var nurse = await api.NewNurseAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { method = "Cash" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { method = "Cash" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await nurse.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { method = "Bitcoin" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await nurse.Client.PostAsJsonAsync($"/api/invoices/{Guid.NewGuid()}/pay", new { method = "Cash" })).StatusCode);
        Assert.Equal("Open", (await ApiFixture.Read<InvoiceDto>(await patient.Client.GetAsync($"/api/invoices/{invoice.Id}"))).Status);
    }

    [Fact]
    public async Task Paying_the_same_invoice_twice_at_once_succeeds_only_once()
    {
        var (_, _, invoice) = await WithInvoice();
        var a = await api.NewNurseAsync();
        var b = await api.NewNurseAsync();

        var results = await Task.WhenAll(
            a.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { method = "Cash" }),
            b.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { method = "Card" }));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_front_desk_adds_extra_charges_to_an_open_invoice_with_validation()
    {
        var (patient, _, invoice) = await WithInvoice();
        var nurse = await api.NewNurseAsync();

        var added = await nurse.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/items", new { description = "Wound dressing", quantity = 2, unitPrice = 75.50 });
        var updated = await ApiFixture.Read<InvoiceDto>(added);

        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal(500m + 151m, updated.Total);
        Assert.Contains(updated.Items, i => i.Source == "Other" && i.Description == "Wound dressing");
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/items", new { description = "Free money", quantity = 1, unitPrice = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await nurse.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/items", new { description = "<b>x</b>", quantity = 1, unitPrice = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await nurse.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/items", new { description = "Negative", quantity = 1, unitPrice = -5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await nurse.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/items", new { description = "Zero qty", quantity = 0, unitPrice = 5 })).StatusCode);
    }

    [Fact]
    public async Task Only_an_admin_can_void_and_only_while_open()
    {
        var (_, _, invoice) = await WithInvoice();
        var nurse = await api.NewNurseAsync();
        var admin = await api.AdminAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Client.PostAsync($"/api/invoices/{invoice.Id}/void", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/invoices/{invoice.Id}/void", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await nurse.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { method = "Cash" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/invoices/{invoice.Id}/void", null)).StatusCode);
    }

    [Fact]
    public async Task Receipts_download_as_real_PDFs_only_for_people_allowed_to_see_the_invoice()
    {
        var (patient, _, invoice) = await WithInvoice();
        var other = await api.NewPatientAsync();
        var nurse = await api.NewNurseAsync();

        var open = await patient.Client.GetAsync($"/api/invoices/{invoice.Id}/pdf");
        var openBytes = await open.Content.ReadAsByteArrayAsync();
        await nurse.Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/pay", new { method = "Card" });
        var paid = await patient.Client.GetAsync($"/api/invoices/{invoice.Id}/pdf");
        var paidText = Encoding.Latin1.GetString(await paid.Content.ReadAsByteArrayAsync());

        Assert.Equal("application/pdf", open.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF-1.4", Encoding.Latin1.GetString(openBytes));
        Assert.Contains("INVOICE - UNPAID", Encoding.Latin1.GetString(openBytes));
        Assert.Contains("RECEIPT", paidText);
        Assert.Contains(patient.FullName, paidText);
        Assert.Contains("Rs.", paidText);
        Assert.Contains("receipt-INV-", paid.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/invoices/{invoice.Id}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonymous().GetAsync($"/api/invoices/{invoice.Id}/pdf")).StatusCode);
    }

    [Fact]
    public async Task The_billing_report_adds_up_what_was_paid_and_what_is_owed()
    {
        var admin = await api.AdminAsync();
        var before = await ApiFixture.Read<BillingReport>(await admin.GetAsync("/api/admin/reports/billing"));
        var (_, _, paidInvoice) = await WithInvoice();
        var (_, _, openInvoice) = await WithInvoice();
        await (await api.NewNurseAsync()).Client.PostAsJsonAsync($"/api/invoices/{paidInvoice.Id}/pay", new { method = "Cash" });

        var after = await ApiFixture.Read<BillingReport>(await admin.GetAsync("/api/admin/reports/billing"));

        Assert.Equal(before.PaidInvoices + 1, after.PaidInvoices);
        Assert.Equal(before.OpenInvoices + 1, after.OpenInvoices);
        Assert.Equal(before.Revenue + 500m, after.Revenue);
        Assert.Equal(before.Outstanding + 500m, after.Outstanding);
        Assert.Contains(after.BySource, r => r.Source == "Consultation");
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.NewNurseAsync()).Client.GetAsync("/api/admin/reports/billing")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/reports/billing?from=2026-12-31&to=2026-01-01")).StatusCode);
    }

    [Fact]
    public async Task Invoice_numbers_are_unique_and_increase()
    {
        var (_, _, a) = await WithInvoice();
        var (_, _, b) = await WithInvoice();

        Assert.NotEqual(a.Number, b.Number);
        Assert.True(string.CompareOrdinal(b.Number, a.Number) > 0);
    }
}
