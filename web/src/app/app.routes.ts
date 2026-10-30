import { Routes } from '@angular/router';
import { authGuard, roleGuard } from './core/auth';
import { Admin } from './pages/admin';
import { Appointments } from './pages/appointments';
import { Attendance } from './pages/attendance';
import { Reports } from './pages/reports';
import { Login } from './pages/login';
import { Records } from './pages/records';
import { Register } from './pages/register';

export const routes: Routes = [
  { path: 'login', component: Login },
  { path: 'register', component: Register },
  { path: 'appointments', component: Appointments, canActivate: [authGuard] },
  { path: 'records', component: Records, canActivate: [roleGuard('Patient', 'Doctor')] },
  { path: 'admin', component: Admin, canActivate: [roleGuard('Admin')] },
  { path: 'attendance', component: Attendance, canActivate: [roleGuard('Doctor', 'Nurse')] },
  { path: 'reports', component: Reports, canActivate: [roleGuard('Admin')] },
  { path: '', pathMatch: 'full', redirectTo: 'appointments' },
  { path: '**', redirectTo: 'appointments' },
];
