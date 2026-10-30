using System.Net;
using System.Net.Http.Json;
using HospitalApi.Dtos;
using HospitalApi.Services;

namespace HospitalApi.Tests;

[Collection("api")]
public class VitalsTests(ApiFixture api)
{
    private static object Reading(Guid patientId, int hr = 72, int sys = 118, int dia = 76, int spo2 = 98, double temp = 36.8) =>
        new { patientId, heartRate = hr, systolic = sys, diastolic = dia, spO2 = spo2, temperatureC = temp };

    [Fact]
    public async Task A_nurse_records_vitals_and_the_patient_reads_them()
    {
        var nurse = await api.NewNurseAsync();
        var patient = await api.NewPatientAsync();

        var save = await nurse.Client.PostAsJsonAsync("/api/vitals", Reading(patient.ProfileId));
        var mine = await ApiFixture.Read<List<VitalsDto>>(await patient.Client.GetAsync("/api/vitals"));

        Assert.Equal(HttpStatusCode.Created, save.StatusCode);
        var row = Assert.Single(mine);
        Assert.Equal((72, 118, 76, 98), (row.HeartRate, row.Systolic, row.Diastolic, row.SpO2));
        Assert.Equal("Normal", row.Severity);
    }

    [Fact]
    public async Task The_server_decides_the_severity_not_the_client()
    {
        var nurse = await api.NewNurseAsync();
        var patient = await api.NewPatientAsync();

        var response = await nurse.Client.PostAsJsonAsync("/api/vitals",
            new { patientId = patient.ProfileId, heartRate = 150, systolic = 200, diastolic = 110, spO2 = 85, temperatureC = 40.0, severity = "Normal" });

        var saved = await ApiFixture.Read<VitalsDto>(response);
        Assert.Equal("Critical", saved.Severity);
        Assert.Contains("oxygen 85%", saved.AlertReason);
    }

    [Theory]
    [InlineData(10, 118, 76, 98, 36.8)]   // heart rate impossible
    [InlineData(72, 118, 76, 40, 36.8)]   // oxygen impossible
    [InlineData(72, 118, 76, 98, 55.0)]   // temperature impossible
    [InlineData(72, 100, 100, 98, 36.8)]  // diastolic not below systolic
    [InlineData(72, 90, 120, 98, 36.8)]   // diastolic above systolic
    public async Task Impossible_readings_are_refused(int hr, int sys, int dia, int spo2, double temp)
    {
        var nurse = await api.NewNurseAsync();
        var patient = await api.NewPatientAsync();

        var response = await nurse.Client.PostAsJsonAsync("/api/vitals", Reading(patient.ProfileId, hr, sys, dia, spo2, temp));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Who_may_record_and_read_vitals()
    {
        var nurse = await api.NewNurseAsync();
        var treating = await api.NewDoctorAsync();
        var stranger = await api.NewDoctorAsync();
        var alice = await api.NewPatientAsync();
        var bob = await api.NewPatientAsync();
        await api.BookAsync(alice, treating.ProfileId, TestTime.InDays(14));
        var reading = Reading(alice.ProfileId);

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.Client.PostAsJsonAsync("/api/vitals", reading)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.AdminAsync()).PostAsJsonAsync("/api/vitals", reading)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await treating.Client.PostAsJsonAsync("/api/vitals", reading)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.PostAsJsonAsync("/api/vitals", reading)).StatusCode);

        // Bob asking for Alice's readings only ever gets his own (none).
        var bobSees = await ApiFixture.Read<List<VitalsDto>>(await bob.Client.GetAsync($"/api/vitals?patientId={alice.ProfileId}"));
        Assert.Empty(bobSees);
        Assert.Equal(HttpStatusCode.OK, (await treating.Client.GetAsync($"/api/vitals?patientId={alice.ProfileId}")).StatusCode);
        Assert.Empty(await ApiFixture.Read<List<VitalsDto>>(await stranger.Client.GetAsync($"/api/vitals?patientId={alice.ProfileId}")));
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.AdminAsync()).GetAsync($"/api/vitals?patientId={alice.ProfileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await nurse.Client.GetAsync("/api/vitals")).StatusCode); // patientId required
    }

    [Fact]
    public async Task A_critical_reading_alerts_the_treating_doctor_and_other_nurses_but_not_strangers()
    {
        var recorder = await api.NewNurseAsync();
        var otherNurse = await api.NewNurseAsync();
        var treating = await api.NewDoctorAsync();
        var stranger = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        await api.BookAsync(patient, treating.ProfileId, TestTime.InDays(14));

        await recorder.Client.PostAsJsonAsync("/api/vitals", Reading(patient.ProfileId, hr: 140, sys: 190, dia: 100, spo2: 85, temp: 40));

        async Task<bool> HasAlert(TestUser u) =>
            (await ApiFixture.Read<NotificationList>(await u.Client.GetAsync("/api/notifications")))
                .Items.Any(n => n.Type == "Vitals" && n.Message.StartsWith("CRITICAL") && n.Message.Contains(patient.FullName));

        Assert.True(await HasAlert(treating));
        Assert.True(await HasAlert(otherNurse));
        Assert.False(await HasAlert(recorder));  // you know what you just entered
        Assert.False(await HasAlert(stranger));  // not their patient
        Assert.False(await HasAlert(patient));
    }
}

[Collection("api")]
public class EmergencyAndNotificationTests(ApiFixture api)
{
    [Fact]
    public async Task Code_blue_reaches_every_other_doctor_and_nurse_only()
    {
        var raiser = await api.NewNurseAsync();
        var nurse = await api.NewNurseAsync();
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();

        var raise = await raiser.Client.PostAsJsonAsync("/api/emergency/code-blue", new { location = "Ward 3, Bed 12" });

        Assert.Equal(HttpStatusCode.Accepted, raise.StatusCode);
        async Task<NotificationDto?> Alert(TestUser u) =>
            (await ApiFixture.Read<NotificationList>(await u.Client.GetAsync("/api/notifications"))).Items.FirstOrDefault(n => n.Type == "CodeBlue");
        Assert.Contains("Ward 3, Bed 12", (await Alert(nurse))!.Message);
        Assert.NotNull(await Alert(doctor));
        Assert.Null(await Alert(raiser));
        Assert.Null(await Alert(patient));
    }

    [Fact]
    public async Task Only_staff_can_raise_code_blue_and_markup_is_refused()
    {
        var patient = await api.NewPatientAsync();
        var nurse = await api.NewNurseAsync();
        var body = new { location = "ICU" };

        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.PostAsJsonAsync("/api/emergency/code-blue", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.AdminAsync()).PostAsJsonAsync("/api/emergency/code-blue", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await nurse.Client.PostAsJsonAsync("/api/emergency/code-blue", new { location = "<script>alert(1)</script>" })).StatusCode);
    }

    [Fact]
    public async Task Notifications_can_be_marked_read_one_by_one_or_all_at_once()
    {
        var raiser = await api.NewNurseAsync();
        var doctor = await api.NewDoctorAsync();
        await raiser.Client.PostAsJsonAsync("/api/emergency/code-blue", new { location = "ER 1" });
        await raiser.Client.PostAsJsonAsync("/api/emergency/code-blue", new { location = "ER 2" });

        var before = await ApiFixture.Read<NotificationList>(await doctor.Client.GetAsync("/api/notifications"));
        Assert.Equal(2, before.Unread);

        Assert.Equal(HttpStatusCode.NoContent, (await doctor.Client.PostAsync($"/api/notifications/{before.Items[0].Id}/read", null)).StatusCode);
        Assert.Equal(1, (await ApiFixture.Read<NotificationList>(await doctor.Client.GetAsync("/api/notifications"))).Unread);

        await doctor.Client.PostAsync("/api/notifications/read-all", null);
        Assert.Equal(0, (await ApiFixture.Read<NotificationList>(await doctor.Client.GetAsync("/api/notifications"))).Unread);
    }
}

[Collection("api")]
public class QueueTests(ApiFixture api)
{
    private static Task<HttpResponseMessage> Issue(TestUser by, TestUser patient, TestUser doctor) =>
        by.Client.PostAsJsonAsync("/api/queue/tokens", new { patientId = patient.ProfileId, doctorId = doctor.ProfileId });

    private static async Task<BoardDoctorDto> BoardFor(TestUser viewer, TestUser doctor) =>
        (await ApiFixture.Read<List<BoardDoctorDto>>(await viewer.Client.GetAsync("/api/queue/board"))).Single(d => d.DoctorId == doctor.ProfileId);

    [Fact]
    public async Task Tokens_are_numbered_per_doctor_and_called_in_order()
    {
        var nurse = await api.NewNurseAsync();
        var doctor = await api.NewDoctorAsync();
        var otherDoctor = await api.NewDoctorAsync();
        var first = await api.NewPatientAsync();
        var second = await api.NewPatientAsync();

        var t1 = await ApiFixture.Read<QueueEntryDto>(await Issue(nurse, first, doctor));
        var t2 = await ApiFixture.Read<QueueEntryDto>(await Issue(nurse, second, doctor));
        var elsewhere = await ApiFixture.Read<QueueEntryDto>(await Issue(nurse, first, otherDoctor));
        Assert.Equal((1, 2, 1), (t1.Number, t2.Number, elsewhere.Number));

        var next = await doctor.Client.PostAsync("/api/queue/call-next", null);
        Assert.Contains("\"nowServing\":1", await next.Content.ReadAsStringAsync());
        Assert.Equal(1, (await BoardFor(nurse, doctor)).NowServing);

        await doctor.Client.PostAsync("/api/queue/call-next", null);
        var board = await BoardFor(nurse, doctor);
        Assert.Equal(2, board.NowServing);
        Assert.Empty(board.Waiting);

        // The patient was told when their turn came.
        var inbox = await ApiFixture.Read<NotificationList>(await first.Client.GetAsync("/api/notifications"));
        Assert.Contains(inbox.Items, n => n.Type == "Queue" && n.Message.Contains("ready to see you"));
    }

    [Fact]
    public async Task One_active_token_per_patient_per_doctor()
    {
        var nurse = await api.NewNurseAsync();
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();

        Assert.Equal(HttpStatusCode.Created, (await Issue(nurse, patient, doctor)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Issue(nurse, patient, doctor)).StatusCode);
    }

    [Fact]
    public async Task Patients_only_see_their_own_name_on_the_board()
    {
        var nurse = await api.NewNurseAsync();
        var doctor = await api.NewDoctorAsync();
        var alice = await api.NewPatientAsync();
        var bob = await api.NewPatientAsync();
        await Issue(nurse, alice, doctor);
        await Issue(nurse, bob, doctor);

        var asAlice = await BoardFor(alice, doctor);
        var asNurse = await BoardFor(nurse, doctor);

        Assert.Equal(2, asAlice.Waiting.Count);
        Assert.Equal(alice.FullName, asAlice.Waiting.Single(w => w.Mine).PatientName);
        Assert.Null(asAlice.Waiting.Single(w => !w.Mine).PatientName);
        Assert.All(asNurse.Waiting, w => Assert.NotNull(w.PatientName));
    }

    [Fact]
    public async Task Only_the_right_roles_can_issue_call_and_skip()
    {
        var nurse = await api.NewNurseAsync();
        var doctor = await api.NewDoctorAsync();
        var otherDoctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        var token = await ApiFixture.Read<QueueEntryDto>(await Issue(nurse, patient, doctor));

        Assert.Equal(HttpStatusCode.Forbidden, (await Issue(patient, patient, doctor)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Issue(doctor, patient, doctor)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Client.PostAsync("/api/queue/call-next", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.PostAsync($"/api/queue/tokens/{token.Id}/skip", null)).StatusCode);
        // A doctor cannot touch another doctor's queue.
        Assert.Equal(HttpStatusCode.NotFound, (await otherDoctor.Client.PostAsync($"/api/queue/tokens/{token.Id}/skip", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await nurse.Client.PostAsync($"/api/queue/tokens/{token.Id}/skip", null)).StatusCode);
        Assert.Empty((await BoardFor(nurse, doctor)).Waiting);
    }
}
