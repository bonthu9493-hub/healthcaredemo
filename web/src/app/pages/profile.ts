import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, errorText } from '../core/api';

@Component({
  imports: [FormsModule],
  template: `
    <form class="card" (ngSubmit)="save()" #f="ngForm">
      <h2>My health profile</h2>
      <p class="muted">Your doctors and nurses see your blood group, allergies and conditions so they can treat you safely. Please keep them up to date.</p>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      @if (ok()) { <p class="ok" role="status">{{ ok() }}</p> }
      @if (name()) { <p><strong>{{ name() }}</strong> <small class="muted">· born {{ dob() }}</small></p> }
      <div class="row">
        <label>Blood group
          <select name="bg" [(ngModel)]="m.bloodGroup">
            <option [ngValue]="null">Not sure</option>
            @for (g of groups; track g) { <option [ngValue]="g">{{ g }}</option> }
          </select>
        </label>
        <label>Phone <input name="phone" [(ngModel)]="m.phone" required pattern="\\+?[0-9 \\-]{7,20}" /></label>
      </div>
      <label>Allergies <small class="muted">(medicines or foods, separated by commas)</small>
        <input name="al" [(ngModel)]="m.allergies" maxlength="500" pattern="[^<>]*" placeholder="e.g. Penicillin, peanuts" />
      </label>
      <label>Long-term conditions <input name="cc" [(ngModel)]="m.chronicConditions" maxlength="500" pattern="[^<>]*" placeholder="e.g. Asthma, diabetes" /></label>
      <div class="row">
        <label>Emergency contact name <input name="en" [(ngModel)]="m.emergencyContactName" maxlength="100" pattern="[^<>]*" /></label>
        <label>Emergency contact phone <input name="ep" [(ngModel)]="m.emergencyContactPhone" pattern="(\\+?[0-9 \\-]{7,20})?" /></label>
      </div>
      <button [disabled]="f.invalid">Save profile</button>
    </form>
  `,
})
export class Profile implements OnInit {
  private api = inject(Api);
  groups = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];
  name = signal('');
  dob = signal('');
  error = signal('');
  ok = signal('');
  m = { phone: '', bloodGroup: null as string | null, allergies: '', chronicConditions: '', emergencyContactName: '', emergencyContactPhone: '' };

  ngOnInit() {
    this.api.profile().subscribe((p) => {
      this.name.set(p.fullName);
      this.dob.set(p.dateOfBirth);
      this.m = {
        phone: p.phone, bloodGroup: p.bloodGroup, allergies: p.allergies, chronicConditions: p.chronicConditions,
        emergencyContactName: p.emergencyContactName, emergencyContactPhone: p.emergencyContactPhone,
      };
    });
  }

  save() {
    this.error.set('');
    this.ok.set('');
    this.api.saveProfile(this.m).subscribe({
      next: () => this.ok.set('Profile saved.'),
      error: (e) => this.error.set(errorText(e)),
    });
  }
}
