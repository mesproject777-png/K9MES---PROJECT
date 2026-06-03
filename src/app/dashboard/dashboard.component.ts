import { Component } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Router } from '@angular/router';
import { AuthService, AuthUser } from '../services/auth.service';
import { PackingService } from '../services/packing.service';
import { environment } from '../../environments/environment';

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
  private readonly reportsApi = `${environment.apiUrl}/api/reports/work-order-tree`;

  constructor(
    private authService: AuthService,
    private router: Router,
    private packingService: PackingService,
    private http: HttpClient
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

    if (serialNumber.toUpperCase().startsWith('MBX-')) {
      this.packingService.lookupMultibox(serialNumber).subscribe({
        next: (details) => {
          const status = String(details?.package?.status || '').toUpperCase();
          this.router.navigate(
            [status === 'CLOSED' ? '/dashboard/operations/packing/closed' : '/dashboard/operations/packing/open'],
            { queryParams: { mbx: serialNumber, t: Date.now() } }
          );
        },
        error: () => {
          this.router.navigate(['/dashboard/operations/packing/open'], {
            queryParams: { mbx: serialNumber, t: Date.now() },
          });
        }
      });
      return;
    }

    const params = new HttpParams().set('wo', serialNumber);
    this.http.get(this.reportsApi, { params }).subscribe({
      next: () => {
        this.router.navigate(['/dashboard/reports'], {
          queryParams: { wo: serialNumber, t: Date.now() },
        });
      },
      error: () => {
        this.router.navigate(['/dashboard/sn-result'], {
          queryParams: { q: serialNumber, t: Date.now() },
        });
      }
    });
  }

  onHeaderSearchInput(): void {
    if (!this.headerSearch.trim()) {
      return;
    }

    this.searchSerialNumber();
  }
}
