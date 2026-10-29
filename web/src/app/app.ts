import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from './core/auth';
import { Realtime } from './core/realtime';

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  selector: 'app-root',
  templateUrl: './app.html',
})
export class App {
  auth = inject(Auth);
  private rt = inject(Realtime);

  logout() {
    this.rt.stop();
    this.auth.logout();
  }
}
