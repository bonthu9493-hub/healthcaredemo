import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Api, errorText } from './core/api';
import { Auth } from './core/auth';
import { Notifications } from './core/notifications';
import { Realtime } from './core/realtime';

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, FormsModule, DatePipe],
  selector: 'app-root',
  templateUrl: './app.html',
})
export class App {
  auth = inject(Auth);
  notes = inject(Notifications);
  private rt = inject(Realtime);
  private api = inject(Api);

  panelOpen = signal(false);
  codeBlueOpen = signal(false);
  location = '';
  codeBlueMsg = signal('');
  codeBlueError = signal(false);

  constructor() {
    // One live connection and one notification list for the whole signed-in session.
    effect(() => {
      if (this.auth.loggedIn()) {
        this.rt.start();
        this.notes.load();
      } else {
        this.rt.stop();
        this.notes.clear();
      }
    });
  }

  sendCodeBlue() {
    this.codeBlueMsg.set('');
    this.codeBlueError.set(false);
    this.api.codeBlue(this.location.trim()).subscribe({
      next: () => {
        this.codeBlueMsg.set('Code blue sent to all doctors and nurses.');
        this.location = '';
        setTimeout(() => { this.codeBlueOpen.set(false); this.codeBlueMsg.set(''); }, 2500);
      },
      error: (e) => { this.codeBlueError.set(true); this.codeBlueMsg.set(errorText(e)); },
    });
  }

  logout() {
    this.panelOpen.set(false);
    this.auth.logout();
  }
}
