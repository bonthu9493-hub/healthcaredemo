import { inject } from '@angular/core';
import { Routes } from '@angular/router';
import { Auth, homeFor, roleGuard } from './core/auth';
import { Admin } from './pages/admin';
import { Appointments } from './pages/appointments';
import { Attendance } from './pages/attendance';
import { Billing } from './pages/billing';
import { Lab } from './pages/lab';
import { Login } from './pages/login';
import { Pharmacy } from './pages/pharmacy';
import { Prescriptions } from './pages/prescriptions';
import { Profile } from './pages/profile';
import { Queue } from './pages/queue';
import { Records } from './pages/records';
import { Register } from './pages/register';
import { Reports } from './pages/reports';
import { Schedule } from './pages/schedule';
import { Vitals } from './pages/vitals';
import { Wards } from './pages/wards';

/** Signed-out visitors are sent to /login by the guard on the page they land on; everyone else goes to their own home page. */
const home = () => homeFor(inject(Auth).role());

export const routes: Routes = [
  { path: 'login', component: Login },
  { path: 'register', component: Register },
  { path: 'appointments', component: Appointments, canActivate: [roleGuard('Patient', 'Doctor', 'Nurse', 'Admin')] },
  { path: 'queue', component: Queue, canActivate: [roleGuard('Patient', 'Doctor', 'Nurse', 'Admin')] },
  { path: 'records', component: Records, canActivate: [roleGuard('Patient', 'Doctor')] },
  { path: 'vitals', component: Vitals, canActivate: [roleGuard('Patient', 'Doctor', 'Nurse')] },
  { path: 'profile', component: Profile, canActivate: [roleGuard('Patient')] },
  { path: 'prescriptions', component: Prescriptions, canActivate: [roleGuard('Patient', 'Doctor')] },
  { path: 'pharmacy', component: Pharmacy, canActivate: [roleGuard('Pharmacist', 'Admin')] },
  { path: 'lab', component: Lab, canActivate: [roleGuard('Patient', 'Doctor', 'LabTech')] },
  { path: 'billing', component: Billing, canActivate: [roleGuard('Patient', 'Nurse', 'Admin')] },
  { path: 'wards', component: Wards, canActivate: [roleGuard('Patient', 'Doctor', 'Nurse', 'Admin')] },
  { path: 'schedule', component: Schedule, canActivate: [roleGuard('Doctor')] },
  { path: 'attendance', component: Attendance, canActivate: [roleGuard('Doctor', 'Nurse', 'Pharmacist', 'LabTech')] },
  { path: 'admin', component: Admin, canActivate: [roleGuard('Admin')] },
  { path: 'reports', component: Reports, canActivate: [roleGuard('Admin')] },
  { path: '', pathMatch: 'full', redirectTo: home },
  { path: '**', redirectTo: home },
];
