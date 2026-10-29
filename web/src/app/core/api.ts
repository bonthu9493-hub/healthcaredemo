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

@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);
  private u = (p: string) => `${API}/api${p}`;

  doctors = () => this.http.get<Doctor[]>(this.u('/doctors'));
  appointments = () => this.http.get<Appointment[]>(this.u('/appointments'));
  book = (b: { doctorId: string; startsAt: string; reason: string }) =>
    this.http.post<Appointment>(this.u('/appointments'), b);
  setStatus = (id: string, status: string) =>
    this.http.patch<Appointment>(this.u(`/appointments/${id}/status`), { status });
  records = () => this.http.get<MedicalRecord[]>(this.u('/records'));
  addRecord = (b: { patientId: string; diagnosis: string; prescription: string; notes: string }) =>
    this.http.post<MedicalRecord>(this.u('/records'), b);
  myPatients = () => this.http.get<PatientRow[]>(this.u('/patients'));
  createDoctor = (b: { email: string; password: string; fullName: string; specialty: string }) =>
    this.http.post<Doctor>(this.u('/admin/doctors'), b);
  users = () => this.http.get<UserRow[]>(this.u('/admin/users'));
  audit = () => this.http.get<AuditRow[]>(this.u('/admin/audit'));
}

/** Server error body is { error } for our checks; fall back to a generic message. */
export function errorText(e: any): string {
  if (e?.status === 429) return 'Too many requests. Please wait a minute and try again.';
  const body = e?.error;
  if (body?.error) return body.error;
  if (body?.errors) return Object.values(body.errors).flat().join(' ');
  return 'Something went wrong. Please try again.';
}
