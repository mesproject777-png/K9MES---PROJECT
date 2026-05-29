import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService, AuthUser } from '../services/auth.service';

@Component({
  selector: 'app-dashboard',
  standalone: false,
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent {
  currentUser: AuthUser | null = null;
  isProfileMenuOpen = false;
  headerSearch = '';

  constructor(
    private authService: AuthService,
    private router: Router
  ) {
    this.currentUser = this.authService.getCurrentUser();

    this.authService.currentUser$.subscribe((user) => {
      this.currentUser = user;
    });
  }

  toggleProfileMenu(): void {
    this.isProfileMenuOpen = !this.isProfileMenuOpen;
  }

  openProfile(): void {
    this.isProfileMenuOpen = false;
    this.router.navigate(['/dashboard/profile']);
  }

  openSettings(): void {
    this.isProfileMenuOpen = false;
    this.router.navigate(['/dashboard/profile'], { queryParams: { section: 'settings' } });
  }

  logout(): void {
    this.isProfileMenuOpen = false;
    this.authService.logout();
  }

  searchSerialNumber(event?: Event): void {
    event?.preventDefault();

    const serialNumber = this.headerSearch.trim();
    if (!serialNumber) {
      return;
    }

    this.router.navigate(['/dashboard/sn-result'], {
      queryParams: { q: serialNumber, t: Date.now() },
    });
  }

  onHeaderSearchInput(): void {
    if (!this.headerSearch.trim()) {
      return;
    }

    this.searchSerialNumber();
  }
}
