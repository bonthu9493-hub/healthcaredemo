using System.Net;
using System.Net.Http.Json;
using HospitalApi.Dtos;
using HospitalApi.Services;

namespace HospitalApi.Tests;

/// <summary>Who may see or change what. These are the tests that matter most for patient privacy.</summary>
[Collection("api")]
public class AccessControlTests(ApiFixture api)
{
    private static DateTimeOffset InDays(int days, int hour = 10) => TestTime.InDays(days, hour);

    [Theory]
    [InlineData("/api/admin/users")]
    [InlineData("/api/admin/audit")]
    [InlineData("/api/admin/reports/staff-attendance")]
    [InlineData("/api/admin/reports/appointment-attendance")]
    [InlineData("/api/admin/reports/staff-attendance.csv")]
    public async Task Only_admins_reach_admin_endpoints(string url)
    {
        var patient = await api.NewPatientAsync();
        var doctor = await api.NewDoctorAsync();
        var nurse = await api.NewNurseAsync();

        foreach (var user in new[] { patient, doctor, nurse })
            Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.GetAsync(url)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await (await api.AdminAsync()).GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Nobody_but_an_admin_can_create_staff()
    {
        var patient = await api.NewPatientAsync();
        var doctor = await api.NewDoctorAsync();
        var body = new { email = "x@test.local", password = ApiFixture.StrongPassword, fullName = "Intruder" };

        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.PostAsJsonAsync("/api/admin/nurses", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PostAsJsonAsync("/api/admin/nurses", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await patient.Client.PostAsJsonAsync("/api/admin/doctors", new { body.email, body.password, body.fullName, specialty = "Any" })).StatusCode);
    }

    [Fact]
    public async Task A_patient_sees_only_their_own_appointments()
    {
        var doctor = await api.NewDoctorAsync();
        var alice = await api.NewPatientAsync();
        var bob = await api.NewPatientAsync();
        await api.BookAsync(alice, doctor.ProfileId, InDays(5, 9), "Alice visit");
        await api.BookAsync(bob, doctor.ProfileId, InDays(5, 14), "Bob visit");

        var aliceList = await ApiFixture.Read<PagedResult<AppointmentDto>>(await alice.Client.GetAsync("/api/appointments"));

        Assert.Single(aliceList.Items);
        Assert.Equal(alice.ProfileId, aliceList.Items[0].PatientId);
    }

    [Fact]
    public async Task A_patient_cannot_change_someone_elses_appointment()
    {
        var doctor = await api.NewDoctorAsync();
        var alice = await api.NewPatientAsync();
        var bob = await api.NewPatientAsync();
        var booked = await ApiFixture.Read<AppointmentDto>(await api.BookAsync(alice, doctor.ProfileId, InDays(5)));

        var attempt = await bob.Client.PatchAsJsonAsync($"/api/appointments/{booked.Id}/status", new { status = "Cancelled" });

        Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode); // doesn't even confirm it exists
    }

    [Fact]
    public async Task A_doctor_sees_only_their_own_appointments()
    {
        var drA = await api.NewDoctorAsync();
        var drB = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        await api.BookAsync(patient, drA.ProfileId, InDays(6, 9));
        await api.BookAsync(patient, drB.ProfileId, InDays(6, 15));

        var list = await ApiFixture.Read<PagedResult<AppointmentDto>>(await drA.Client.GetAsync("/api/appointments"));

        Assert.Single(list.Items);
        Assert.Equal(drA.ProfileId, list.Items[0].DoctorId);
    }

    [Fact]
    public async Task Nurses_see_all_appointments_but_cannot_change_them()
    {
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        var nurse = await api.NewNurseAsync();
        var booked = await ApiFixture.Read<AppointmentDto>(await api.BookAsync(patient, doctor.ProfileId, InDays(7)));

        var list = await ApiFixture.Read<PagedResult<AppointmentDto>>(await nurse.Client.GetAsync("/api/appointments?pageSize=100"));
        var change = await nurse.Client.PatchAsJsonAsync($"/api/appointments/{booked.Id}/status", new { status = "Cancelled" });

        Assert.Contains(list.Items, a => a.Id == booked.Id);
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
    }

    [Fact]
    public async Task Only_patients_can_book()
    {
        var doctor = await api.NewDoctorAsync();
        var nurse = await api.NewNurseAsync();
        var admin = await api.AdminAsync();
        var body = new { doctorId = doctor.ProfileId, startsAt = InDays(8), reason = "Not allowed" };

        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Client.PostAsJsonAsync("/api/appointments", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.PostAsJsonAsync("/api/appointments", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/appointments", body)).StatusCode);
    }

    [Fact]
    public async Task Medical_records_are_limited_to_the_patient_and_their_own_doctor()
    {
        var doctor = await api.NewDoctorAsync();
        var otherDoctor = await api.NewDoctorAsync();
        var alice = await api.NewPatientAsync();
        var bob = await api.NewPatientAsync();
        await api.BookAsync(alice, doctor.ProfileId, InDays(9));

        // The treating doctor can write a record for Alice.
        var create = await doctor.Client.PostAsJsonAsync("/api/records",
            new { patientId = alice.ProfileId, diagnosis = "Hypertension", prescription = "Amlodipine", notes = "Review in 4 weeks" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // A doctor without an appointment with Alice cannot.
        var stranger = await otherDoctor.Client.PostAsJsonAsync("/api/records",
            new { patientId = alice.ProfileId, diagnosis = "Nosy", prescription = "", notes = "" });
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);

        // Alice sees it; Bob and the other doctor do not; admins and nurses are refused outright.
        Assert.Single((await ApiFixture.Read<PagedResult<RecordDto>>(await alice.Client.GetAsync("/api/records"))).Items);
        Assert.Empty((await ApiFixture.Read<PagedResult<RecordDto>>(await bob.Client.GetAsync("/api/records"))).Items);
        Assert.Empty((await ApiFixture.Read<PagedResult<RecordDto>>(await otherDoctor.Client.GetAsync("/api/records"))).Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.AdminAsync()).GetAsync("/api/records")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.NewNurseAsync()).Client.GetAsync("/api/records")).StatusCode);
    }

    [Fact]
    public async Task Patient_lookup_is_for_nurses_and_admins_and_returns_names_only()
    {
        var patient = await api.NewPatientAsync();
        var doctor = await api.NewDoctorAsync();
        var nurse = await api.NewNurseAsync();
        var url = $"/api/patients/lookup?q={Uri.EscapeDataString(patient.FullName)}";

        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.GetAsync(url)).StatusCode);

        var json = await (await nurse.Client.GetAsync(url)).Content.ReadAsStringAsync();
        Assert.Contains(patient.FullName, json);
        Assert.DoesNotContain("dateOfBirth", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Notifications_belong_to_their_owner()
    {
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        var snoop = await api.NewPatientAsync();
        await api.BookAsync(patient, doctor.ProfileId, InDays(10)); // notifies the doctor

        var inbox = await ApiFixture.Read<NotificationList>(await doctor.Client.GetAsync("/api/notifications"));
        Assert.NotEmpty(inbox.Items);
        Assert.Empty((await ApiFixture.Read<NotificationList>(await snoop.Client.GetAsync("/api/notifications"))).Items);

        var steal = await snoop.Client.PostAsync($"/api/notifications/{inbox.Items[0].Id}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, steal.StatusCode);
    }
}
