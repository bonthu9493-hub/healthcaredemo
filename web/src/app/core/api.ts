import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { API } from './auth';

export interface Doctor { id: string; fullName: string; specialty: string }
export interface Appointment {
  id: string; patientId: string; patientName: string; doctorId: string; doctorName: string;
  specialty: string; startsAt: string; reason: string; status: string;
}
export interface MedicalRecord {
  id: string; patientId: string; patientName: string; doctorName: string;
  diagnosis: string; prescription: string; notes: string; createdAt: string;
}
export interface PatientRow { id: string; fullName: string; dateOfBirth: string; phone: string }
export interface UserRow { id: string; email: string; fullName: string; role: string }
export interface AuditRow {
  id: number; userId: string | null; action: string; entity: string; entityId: string | null;
  ip: string | null; at: string;
}

export interface AttendanceRow {
  id: string; date: string; checkIn: string; checkOut: string | null; hours: number | null; late: boolean;
}
export interface AttendanceSummary { today: AttendanceRow | null; history: AttendanceRow[] }
export interface StaffReportRow {
  userId: string; fullName: string; role: string; workingDays: number; daysPresent: number;
  daysAbsent: number; totalHours: number; lateCount: number; missingCheckOut: number;
}
export interface AppointmentReportRow {
  doctorId: string; doctorName: string; specialty: string; total: number; attended: number; noShow: number;
  cancelled: number; upcoming: number; unmarked: number; attendanceRate: number | null;
}
export type ReportKind = 'staff-attendance' | 'appointment-attendance';

export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number }
export const PAGE_SIZE = 10;

/** Builds query params, leaving out empty values. */
export function query(values: Record<string, string | number | undefined>): Record<string, string> {
  const out: Record<string, string> = {};
  for (const [k, v] of Object.entries(values)) if (v !== undefined && v !== '') out[k] = String(v);
  return out;
}

export interface NotificationItem { id: string; type: string; message: string; createdAt: string; read: boolean }
export interface NotificationList { items: NotificationItem[]; unread: number }
export interface VitalsRow {
  id: string; patientId: string; patientName: string; recordedByName: string; recordedAt: string;
  heartRate: number; systolic: number; diastolic: number; spO2: number; temperatureC: number;
  severity: 'Normal' | 'Warning' | 'Critical'; alertReason: string | null;
}
export interface QueueEntry { id: string; number: number; patientName: string | null; mine: boolean }
export interface BoardDoctor { doctorId: string; doctorName: string; specialty: string; nowServing: number | null; waiting: QueueEntry[] }
export interface ScheduleEntry { day: number; start: string; end: string; slotMinutes: number }
export interface SlotsResponse { hasSchedule: boolean; slots: string[] }
export interface PatientLookup { id: string; fullName: string }

@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);
  private u = (p: string) => `${API}/api${p}`;

  doctors = () => this.http.get<Doctor[]>(this.u('/doctors'));
  appointments = (f: { q?: string; status?: string; page?: number } = {}) =>
    this.http.get<Paged<Appointment>>(this.u('/appointments'),
      { params: query({ q: f.q, status: f.status, page: f.page ?? 1, pageSize: PAGE_SIZE }) });
  book = (b: { doctorId: string; startsAt: string; reason: string }) =>
    this.http.post<Appointment>(this.u('/appointments'), b);
  setStatus = (id: string, status: string) =>
    this.http.patch<Appointment>(this.u(`/appointments/${id}/status`), { status });
  records = (f: { q?: string; page?: number } = {}) =>
    this.http.get<Paged<MedicalRecord>>(this.u('/records'), { params: query({ q: f.q, page: f.page ?? 1, pageSize: PAGE_SIZE }) });
  addRecord = (b: { patientId: string; diagnosis: string; prescription: string; notes: string }) =>
    this.http.post<MedicalRecord>(this.u('/records'), b);
  myPatients = () => this.http.get<PatientRow[]>(this.u('/patients'));
  createDoctor = (b: { email: string; password: string; fullName: string; specialty: string }) =>
    this.http.post<Doctor>(this.u('/admin/doctors'), b);
  createNurse = (b: { email: string; password: string; fullName: string }) =>
    this.http.post<UserRow>(this.u('/admin/nurses'), b);
  attendanceMe = () => this.http.get<AttendanceSummary>(this.u('/attendance/me'));
  checkIn = () => this.http.post<AttendanceRow>(this.u('/attendance/check-in'), {});
  checkOut = () => this.http.post<AttendanceRow>(this.u('/attendance/check-out'), {});
  staffReport = (from: string, to: string) =>
    this.http.get<StaffReportRow[]>(this.u('/admin/reports/staff-attendance'), { params: { from, to } });
  appointmentReport = (from: string, to: string) =>
    this.http.get<AppointmentReportRow[]>(this.u('/admin/reports/appointment-attendance'), { params: { from, to } });
  reportCsv = (kind: ReportKind, from: string, to: string) =>
    this.http.get(this.u(`/admin/reports/${kind}.csv`), { params: { from, to }, responseType: 'blob' });
  notifications = () => this.http.get<NotificationList>(this.u('/notifications'));
  markRead = (id: string) => this.http.post(this.u(`/notifications/${id}/read`), {});
  markAllRead = () => this.http.post(this.u('/notifications/read-all'), {});
  codeBlue = (location: string) => this.http.post(this.u('/emergency/code-blue'), { location });
  vitals = (patientId?: string) =>
    this.http.get<VitalsRow[]>(this.u('/vitals'), { params: patientId ? { patientId } : {} });
  recordVitals = (b: { patientId: string; heartRate: number; systolic: number; diastolic: number; spO2: number; temperatureC: number }) =>
    this.http.post<VitalsRow>(this.u('/vitals'), b);
  lookupPatients = (q: string) => this.http.get<PatientLookup[]>(this.u('/patients/lookup'), { params: { q } });
  board = () => this.http.get<BoardDoctor[]>(this.u('/queue/board'));
  issueToken = (patientId: string, doctorId: string) => this.http.post<QueueEntry>(this.u('/queue/tokens'), { patientId, doctorId });
  callNext = () => this.http.post<{ nowServing: number | null }>(this.u('/queue/call-next'), {});
  skipToken = (id: string) => this.http.post(this.u(`/queue/tokens/${id}/skip`), {});
  mySchedule = () => this.http.get<ScheduleEntry[]>(this.u('/schedule/me'));
  saveSchedule = (entries: ScheduleEntry[]) => this.http.put<ScheduleEntry[]>(this.u('/schedule/me'), entries);
  slots = (doctorId: string, date: string) =>
    this.http.get<SlotsResponse>(this.u(`/doctors/${doctorId}/slots`), { params: { date } });
  users = (f: { q?: string; role?: string; page?: number } = {}) =>
    this.http.get<Paged<UserRow>>(this.u('/admin/users'),
      { params: query({ q: f.q, role: f.role, page: f.page ?? 1, pageSize: PAGE_SIZE }) });
  audit = (f: { q?: string; page?: number } = {}) =>
    this.http.get<Paged<AuditRow>>(this.u('/admin/audit'), { params: query({ q: f.q, page: f.page ?? 1, pageSize: 20 }) });
}

/** Server error body is { error } for our checks; fall back to a generic message. */
export function errorText(e: any): string {
  if (e?.status === 429) return 'Too many requests. Please wait a minute and try again.';
  const body = e?.error;
  if (body?.error) return body.error;
  if (body?.errors) return Object.values(body.errors).flat().join(' ');
  return 'Something went wrong. Please try again.';
}
