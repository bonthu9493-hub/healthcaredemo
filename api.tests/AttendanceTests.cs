using System.Net;
using System.Net.Http.Json;
using HospitalApi.Dtos;

namespace HospitalApi.Tests;

[Collection("api")]
public class AttendanceTests(ApiFixture api)
{
    [Fact]
    public async Task Staff_check_in_and_out_once_per_shift()
    {
        var nurse = await api.NewNurseAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await nurse.Client.PostAsync("/api/attendance/check-out", null)).StatusCode); // not in yet
        Assert.Equal(HttpStatusCode.Created, (await nurse.Client.PostAsync("/api/attendance/check-in", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await nurse.Client.PostAsync("/api/attendance/check-in", null)).StatusCode); // already in
        Assert.Equal(HttpStatusCode.OK, (await nurse.Client.PostAsync("/api/attendance/check-out", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await nurse.Client.PostAsync("/api/attendance/check-in", null)).StatusCode); // shift already done
        Assert.Equal(HttpStatusCode.Conflict, (await nurse.Client.PostAsync("/api/attendance/check-out", null)).StatusCode);

        var me = await ApiFixture.Read<AttendanceSummary>(await nurse.Client.GetAsync("/api/attendance/me"));
        Assert.NotNull(me.Today?.CheckOut);
        Assert.Single(me.History);
    }

    [Fact]
    public async Task Only_doctors_and_nurses_have_attendance()
    {
        var patient = await api.NewPatientAsync();
        var admin = await api.AdminAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await patient.Client.PostAsync("/api/attendance/check-in", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync("/api/attendance/check-in", null)).StatusCode);
    }

    [Fact]
    public async Task Attendance_is_always_recorded_for_the_caller_only()
    {
        var doctor = await api.NewDoctorAsync();
        var nurse = await api.NewNurseAsync();

        // Even if a caller supplies someone else's id, the record belongs to the token's owner.
        await nurse.Client.PostAsJsonAsync("/api/attendance/check-in", new { userId = doctor.UserId });

        var doctorView = await ApiFixture.Read<AttendanceSummary>(await doctor.Client.GetAsync("/api/attendance/me"));
        Assert.Null(doctorView.Today);
        Assert.Empty(doctorView.History);
    }

    [Fact]
    public async Task The_staff_report_counts_presence_and_ignores_bad_ranges()
    {
        var present = await api.NewNurseAsync();
        var absent = await api.NewDoctorAsync();
        await present.Client.PostAsync("/api/attendance/check-in", null);
        await present.Client.PostAsync("/api/attendance/check-out", null);
        var admin = await api.AdminAsync();

        var rows = await ApiFixture.Read<List<StaffReportRow>>(await admin.GetAsync("/api/admin/reports/staff-attendance"));
        var p = rows.Single(r => r.UserId == present.UserId);
        var a = rows.Single(r => r.UserId == absent.UserId);

        Assert.Equal(1, p.DaysPresent);
        Assert.Equal(0, a.DaysPresent);
        Assert.Equal(a.WorkingDays, a.DaysAbsent); // never turned up on any working day
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/reports/staff-attendance?from=2026-12-31&to=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/reports/staff-attendance?from=2020-01-01&to=2026-01-01")).StatusCode);
    }

    [Fact]
    public async Task CSV_exports_neutralise_spreadsheet_formulas()
    {
        // A staff name that starts with "=" would run as a formula when the CSV is opened in Excel.
        await api.NewNurseAsync("=HYPERLINK(\"http://evil.example\",\"click\")");
        var admin = await api.AdminAsync();

        var response = await admin.GetAsync("/api/admin/reports/staff-attendance.csv");
        var csv = await response.Content.ReadAsStringAsync();

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("'=HYPERLINK", csv);
        Assert.DoesNotContain("\"=HYPERLINK", csv);
    }
}
