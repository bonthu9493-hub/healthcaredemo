using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRealtimeFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DoctorSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Day = table.Column<int>(type: "int", nullable: false),
                    Start = table.Column<TimeOnly>(type: "time", nullable: false),
                    End = table.Column<TimeOnly>(type: "time", nullable: false),
                    SlotMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorSchedules_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QueueTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueTokens_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueTokens_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VitalSigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    HeartRate = table.Column<int>(type: "int", nullable: false),
                    Systolic = table.Column<int>(type: "int", nullable: false),
                    Diastolic = table.Column<int>(type: "int", nullable: false),
                    SpO2 = table.Column<int>(type: "int", nullable: false),
                    TemperatureC = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AlertReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VitalSigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VitalSigns_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VitalSigns_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorSchedules_DoctorId_Day",
                table: "DoctorSchedules",
                columns: new[] { "DoctorId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueTokens_Date_DoctorId_Number",
                table: "QueueTokens",
                columns: new[] { "Date", "DoctorId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueTokens_DoctorId",
                table: "QueueTokens",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueTokens_PatientId",
                table: "QueueTokens",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_VitalSigns_PatientId_RecordedAt",
                table: "VitalSigns",
                columns: new[] { "PatientId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VitalSigns_RecordedByUserId",
                table: "VitalSigns",
                column: "RecordedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorSchedules");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "QueueTokens");

            migrationBuilder.DropTable(
                name: "VitalSigns");
        }
    }
}
