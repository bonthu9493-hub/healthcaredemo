using System.Net;
using System.Net.Http.Json;
using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Tests;

[Collection("api")]
public class AppointmentTests(ApiFixture api)
{
    private static DateTimeOffset InDays(int days, int hour = 10) => TestTime.InDays(days, hour);

    private static Task<HttpResponseMessage> SetStatus(TestUser who, Guid id, string status) =>
        who.Client.PatchAsJsonAsync($"/api/appointments/{id}/status", new { status });

    [Fact]
    public async Task Booking_works_and_the_doctor_is_notified()
    {
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();

        var response = await api.BookAsync(patient, doctor.ProfileId, InDays(3), "Chest pain");
        var booked = await ApiFixture.Read<AppointmentDto>(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Scheduled", booked.Status);
        var inbox = await ApiFixture.Read<NotificationList>(await doctor.Client.GetAsync("/api/notifications"));
        Assert.Contains(inbox.Items, n => n.Type == "Appointment" && n.Message.Contains("Chest pain"));
    }

    [Fact]
    public async Task Booking_the_past_is_refused()
    {
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();

        var response = await api.BookAsync(patient, doctor.ProfileId, DateTimeOffset.UtcNow.AddHours(-1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Two_patients_cannot_take_the_same_time()
    {
        var doctor = await api.NewDoctorAsync();
        var first = await api.NewPatientAsync();
        var second = await api.NewPatientAsync();
        var when = InDays(4);

        Assert.Equal(HttpStatusCode.Created, (await api.BookAsync(first, doctor.ProfileId, when)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.BookAsync(second, doctor.ProfileId, when)).StatusCode);
    }

    [Fact]
    public async Task A_status_change_is_really_saved()
    {
        // Regression: an earlier bug returned "Completed" but never wrote it to the database.
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        var booked = await ApiFixture.Read<AppointmentDto>(await api.BookAsync(patient, doctor.ProfileId, InDays(3)));

        var change = await SetStatus(doctor, booked.Id, "Completed");

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var list = await ApiFixture.Read<PagedResult<AppointmentDto>>(await patient.Client.GetAsync("/api/appointments"));
        Assert.Equal("Completed", list.Items.Single(a => a.Id == booked.Id).Status);
        Assert.Equal("Completed", await api.Db(db => db.Appointments.Where(a => a.Id == booked.Id).Select(a => a.Status).FirstAsync()));
    }

    [Fact]
    public async Task A_finished_appointment_cannot_be_changed_again()
    {
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        var booked = await ApiFixture.Read<AppointmentDto>(await api.BookAsync(patient, doctor.ProfileId, InDays(3)));
        await SetStatus(doctor, booked.Id, "Completed");

        Assert.Equal(HttpStatusCode.Conflict, (await SetStatus(doctor, booked.Id, "Cancelled")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SetStatus(patient, booked.Id, "Cancelled")).StatusCode);
    }

    [Fact]
    public async Task Patients_may_only_cancel()
    {
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        var booked = await ApiFixture.Read<AppointmentDto>(await api.BookAsync(patient, doctor.ProfileId, InDays(3)));

        Assert.Equal(HttpStatusCode.Forbidden, (await SetStatus(patient, booked.Id, "Completed")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetStatus(patient, booked.Id, "NoShow")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetStatus(patient, booked.Id, "Cancelled")).StatusCode);
    }

    [Fact]
    public async Task No_show_is_only_allowed_once_the_time_has_passed()
    {
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        var future = await ApiFixture.Read<AppointmentDto>(await api.BookAsync(patient, doctor.ProfileId, InDays(3)));
        var pastId = await api.AddPastAppointmentAsync(patient, doctor, TimeSpan.FromHours(2));

        Assert.Equal(HttpStatusCode.BadRequest, (await SetStatus(doctor, future.Id, "NoShow")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetStatus(doctor, pastId, "NoShow")).StatusCode);
    }

    [Fact]
    public async Task The_appointment_report_counts_attended_and_no_shows()
    {
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        var a = await api.AddPastAppointmentAsync(patient, doctor, TimeSpan.FromHours(3));
        var b = await api.AddPastAppointmentAsync(patient, doctor, TimeSpan.FromHours(4));
        var c = await api.AddPastAppointmentAsync(patient, doctor, TimeSpan.FromHours(5));
        await SetStatus(doctor, a, "Completed");
        await SetStatus(doctor, b, "Completed");
        await SetStatus(doctor, c, "NoShow");

        var admin = await api.AdminAsync();
        var rows = await ApiFixture.Read<List<AppointmentReportRow>>(await admin.GetAsync("/api/admin/reports/appointment-attendance"));
        var row = rows.Single(r => r.DoctorId == doctor.ProfileId);

        Assert.Equal((3, 2, 1), (row.Total, row.Attended, row.NoShow));
        Assert.Equal(66.7, row.AttendanceRate);
    }

    // ---------- schedules and slots ----------

    private async Task<List<DateTimeOffset>> SlotsAsync(TestUser asker, Guid doctorId, DateOnly date)
    {
        var r = await ApiFixture.Read<SlotsResponse>(await asker.Client.GetAsync($"/api/doctors/{doctorId}/slots?date={date:yyyy-MM-dd}"));
        return r.Slots;
    }

    private static object[] EveryDay(string from = "10:00", string to = "12:00", int minutes = 30) =>
        Enumerable.Range(0, 7).Select(d => (object)new { day = d, start = from, end = to, slotMinutes = minutes }).ToArray();

    [Fact]
    public async Task A_schedule_turns_into_bookable_slots_that_book_and_free_up()
    {
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        var other = await api.NewPatientAsync();
        var clock = new AttendanceClock(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var date = clock.Today().AddDays(2);

        Assert.False((await ApiFixture.Read<SlotsResponse>(await patient.Client.GetAsync($"/api/doctors/{doctor.ProfileId}/slots?date={date:yyyy-MM-dd}"))).HasSchedule);
        Assert.Equal(HttpStatusCode.OK, (await doctor.Client.PutAsJsonAsync("/api/schedule/me", EveryDay())).StatusCode);

        var slots = await SlotsAsync(patient, doctor.ProfileId, date);
        Assert.Equal(4, slots.Count); // 10:00-12:00 in 30-minute slots

        // A time between slots is refused; an exact slot is accepted; then it disappears for everyone.
        Assert.Equal(HttpStatusCode.BadRequest, (await api.BookAsync(patient, doctor.ProfileId, slots[0].AddMinutes(10))).StatusCode);
        var booked = await ApiFixture.Read<AppointmentDto>(await api.BookAsync(patient, doctor.ProfileId, slots[0]));
        Assert.Equal(HttpStatusCode.Conflict, (await api.BookAsync(other, doctor.ProfileId, slots[0])).StatusCode);
        Assert.Equal(3, (await SlotsAsync(other, doctor.ProfileId, date)).Count);

        // Cancelling gives the slot back.
        await SetStatus(patient, booked.Id, "Cancelled");
        Assert.Equal(4, (await SlotsAsync(other, doctor.ProfileId, date)).Count);
    }

    [Fact]
    public async Task Only_doctors_edit_schedules_and_bad_windows_are_refused()
    {
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.PutAsJsonAsync("/api/schedule/me", EveryDay())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.Client.PutAsJsonAsync("/api/schedule/me", EveryDay("12:00", "10:00"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.Client.PutAsJsonAsync("/api/schedule/me", EveryDay("9am", "12:00"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.Client.PutAsJsonAsync("/api/schedule/me",
            new[] { new { day = 1, start = "09:00", end = "11:00", slotMinutes = 30 }, new { day = 1, start = "10:00", end = "12:00", slotMinutes = 30 } })).StatusCode);
    }

    // ---------- search and paging ----------

    [Fact]
    public async Task Search_status_filter_and_paging_work_together()
    {
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        await api.BookAsync(patient, doctor.ProfileId, InDays(11, 9), "alpha migraine");
        await api.BookAsync(patient, doctor.ProfileId, InDays(11, 11), "beta migraine");
        var third = await ApiFixture.Read<AppointmentDto>(await api.BookAsync(patient, doctor.ProfileId, InDays(11, 13), "gamma cough"));
        await SetStatus(patient, third.Id, "Cancelled");

        var c = patient.Client;
        var all = await ApiFixture.Read<PagedResult<AppointmentDto>>(await c.GetAsync("/api/appointments"));
        var search = await ApiFixture.Read<PagedResult<AppointmentDto>>(await c.GetAsync("/api/appointments?q=migraine"));
        var cancelled = await ApiFixture.Read<PagedResult<AppointmentDto>>(await c.GetAsync("/api/appointments?status=Cancelled"));
        var page1 = await ApiFixture.Read<PagedResult<AppointmentDto>>(await c.GetAsync("/api/appointments?pageSize=2&page=1"));
        var page2 = await ApiFixture.Read<PagedResult<AppointmentDto>>(await c.GetAsync("/api/appointments?pageSize=2&page=2"));

        Assert.Equal(3, all.Total);
        Assert.Equal(2, search.Total);
        Assert.Equal("gamma cough", Assert.Single(cancelled.Items).Reason);
        Assert.Equal((2, 3), (page1.Items.Count, page1.Total));
        Assert.Single(page2.Items);
        Assert.Empty(page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id))); // no overlap between pages
    }

    [Fact]
    public async Task Search_only_covers_what_the_user_may_see_and_page_size_is_capped()
    {
        var doctor = await api.NewDoctorAsync();
        var alice = await api.NewPatientAsync();
        var bob = await api.NewPatientAsync();
        await api.BookAsync(alice, doctor.ProfileId, InDays(12, 9), "secret-reason-xyz");

        var bobSearch = await ApiFixture.Read<PagedResult<AppointmentDto>>(await bob.Client.GetAsync("/api/appointments?q=secret-reason-xyz"));
        var huge = await ApiFixture.Read<PagedResult<AppointmentDto>>(await alice.Client.GetAsync("/api/appointments?pageSize=100000"));

        Assert.Empty(bobSearch.Items);
        Assert.Equal(100, huge.PageSize);
    }

    [Fact]
    public async Task Wildcard_characters_in_a_search_do_not_match_everything()
    {
        var doctor = await api.NewDoctorAsync();
        var patient = await api.NewPatientAsync();
        await api.BookAsync(patient, doctor.ProfileId, InDays(13, 9), "plain visit");

        var result = await ApiFixture.Read<PagedResult<AppointmentDto>>(await patient.Client.GetAsync("/api/appointments?q=%25"));

        Assert.Empty(result.Items);
    }
}
