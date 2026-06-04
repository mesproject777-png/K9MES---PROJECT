import { HttpClient, HttpParams } from '@angular/common/http';
import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../../services/auth.service';
import { environment } from '../../../../environments/environment';

interface RouteBackStation {
  station_code: string;
  station_name: string;
  station_order: number;
}

interface RouteBackSummaryRow {
  station: string;
  description: string;
  qmsReport: string;
  sample: string;
  finalStatus: string;
  stationOrder: number;
}

interface RouteBackResponse {
  message?: string;
  source: string;
  serial: {
    sn: string;
    rsn: string;
    status: string;
    condition: string;
    current_station_code: string;
    current_station_order: number;
    pn: string;
    work_order: string;
  };
  routeSummary: RouteBackSummaryRow[];
  selectableStations: RouteBackStation[];
}

@Component({
  selector: 'app-sn-route-back',
  standalone: false,
  templateUrl: './sn-route-back.component.html',
  styleUrl: './sn-route-back.component.scss'
})
export class SnRouteBackComponent {
  private readonly apiUrl = `${environment.apiUrl}/api/operations/sn-route-back`;

  serialNumber = '';
  selectedStation = '';
  reason = '';
  isLoading = false;
  isSaving = false;
  errorMessage = '';
  successMessage = '';
  routeData: RouteBackResponse | null = null;

  constructor(
    private http: HttpClient,
    private router: Router,
    private authService: AuthService
  ) {}

  searchFromKeyboard(event: Event): void {
    event.preventDefault();
    this.searchSerial();
  }

  searchSerial(): void {
    const query = this.serialNumber.trim();
    this.successMessage = '';
    this.errorMessage = '';
    this.routeData = null;
    this.selectedStation = '';
    this.reason = '';

    if (!query) {
      this.errorMessage = 'Enter Serial Number.';
      return;
    }

    this.isLoading = true;
    const params = new HttpParams().set('query', query);
    this.http.get<RouteBackResponse>(this.apiUrl, { params }).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.routeData = response;
        this.selectedStation = response.serial?.current_station_code || response.selectableStations[0]?.station_code || '';
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error?.error?.message || error?.error?.error || 'Unable to load SN route summary.';
      }
    });
  }

  routeBack(): void {
    if (!this.routeData) {
      this.errorMessage = 'Search Serial Number first.';
      return;
    }

    if (!this.selectedStation) {
      this.errorMessage = 'Select Route Back Station.';
      return;
    }

    if (!this.reason.trim()) {
      this.errorMessage = 'Enter Reason / Remarks.';
      return;
    }

    this.isSaving = true;
    this.successMessage = '';
    this.errorMessage = '';
    const payload = {
      query: this.routeData.serial.sn || this.serialNumber.trim(),
      targetStationCode: this.selectedStation,
      reason: this.reason.trim(),
      changedBy: this.getChangedBy()
    };

    this.http.post<{ message: string; data: RouteBackResponse }>(this.apiUrl, payload).subscribe({
      next: (response) => {
        this.isSaving = false;
        this.successMessage = response.message || 'Route back completed successfully.';
        this.routeData = response.data;
        this.selectedStation = response.data?.serial?.current_station_code || this.selectedStation;
      },
      error: (error) => {
        this.isSaving = false;
        this.errorMessage = error?.error?.message || error?.error?.error || 'Unable to route back SN.';
      }
    });
  }

  cancel(): void {
    this.router.navigate(['/dashboard/operations']);
  }

  reset(): void {
    this.serialNumber = '';
    this.selectedStation = '';
    this.reason = '';
    this.routeData = null;
    this.errorMessage = '';
    this.successMessage = '';
  }

  trackByRouteRow(index: number, row: RouteBackSummaryRow): string {
    return `${index}-${row.station}`;
  }

  private getChangedBy(): string {
    const user = this.authService.getCurrentUser();
    return user?.user_name || user?.login_id || 'WEB-CLIENT';
  }
}
