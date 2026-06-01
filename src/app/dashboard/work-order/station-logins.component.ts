import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { environment } from '../../../environments/environment';

type StationLoginApiRow = {
  id?: number;
  row_key: string;
  login_row_id: number | null;
  station_id: number;
  station_order: number;
  station_code: string;
  station_name: string;
  station_login_id: string;
  station_login_password: string;
  isEditing: boolean;
  showPassword: boolean;
  isNew: boolean;
};

type StationLoginEntry = {
  row_key: string;
  login_row_id: number | null;
  station_login_id: string;
  station_login_password: string;
  showPassword: boolean;
  isNew: boolean;
};

type StationLoginStation = {
  station_id: number;
  station_order: number;
  station_code: string;
  station_name: string;
  isEditing: boolean;
  logins: StationLoginEntry[];
};

@Component({
  selector: 'app-station-logins',
  standalone: false,
  templateUrl: './station-logins.component.html',
  styleUrl: './station-logins.component.scss'
})
export class StationLoginsComponent implements OnInit {
  private readonly apiUrl = `${environment.apiUrl}/api/workflow/station-logins`;

  pn = '';
  wo = '';
  description = '';
  rows: StationLoginStation[] = [];
  isLoading = false;
  isSaving = false;
  errorMessage = '';
  successMessage = '';
  private nextLocalId = -1;

  constructor(
    private http: HttpClient,
    private route: ActivatedRoute,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.route.queryParamMap.subscribe((params) => {
      this.pn = String(params.get('pn') || '').trim();
      this.wo = String(params.get('wo') || '').trim();
      this.loadRows();
    });
  }

  loadRows(): void {
    if (!this.pn) {
      this.errorMessage = 'Part number is required.';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.successMessage = '';

    let params = new HttpParams().set('pn', this.pn);
    if (this.wo) {
      params = params.set('wo', this.wo);
    }

    this.http.get<{ partNumber: { description?: string; wo?: string }; stations: Partial<StationLoginApiRow>[] }>(this.apiUrl, { params }).subscribe({
      next: (response) => {
        this.description = response.partNumber?.description || '';
        this.wo = response.partNumber?.wo || this.wo;
        this.rows = this.groupStationRows(response.stations || []);
        this.isLoading = false;
      },
      error: (error) => {
        this.rows = [];
        this.isLoading = false;
        this.errorMessage = error?.error?.message || error?.error?.error || 'Unable to load station logins.';
      },
    });
  }

  saveRows(): void {
    this.errorMessage = '';
    this.successMessage = '';

    const validationMessage = this.validateRows();
    if (validationMessage) {
      this.errorMessage = validationMessage;
      return;
    }

    this.isSaving = true;
    this.http.put<{ message: string }>(this.apiUrl, {
      pn: this.pn,
      wo: this.wo,
      stations: this.rows.flatMap((row) =>
        row.logins.filter((login) => this.hasLoginData(login)).map((login) => ({
          id: login.login_row_id,
          station_id: row.station_id,
          station_code: row.station_code,
          station_login_id: login.station_login_id.trim(),
          station_login_password: login.station_login_password.trim(),
        }))
      ),
    }).subscribe({
      next: (response) => {
        this.isSaving = false;
        this.successMessage = response.message || 'Station logins saved successfully.';
        this.loadRows();
      },
      error: (error) => {
        this.isSaving = false;
        this.errorMessage = error?.error?.message || error?.error?.error || 'Unable to save station logins.';
      },
    });
  }

  goBack(): void {
    this.router.navigate(['/dashboard/workorder']);
  }

  editRow(row: StationLoginStation): void {
    row.isEditing = true;
    this.successMessage = '';
  }

  addUser(row: StationLoginStation): void {
    row.logins.push({
      row_key: `new-${this.nextLocalId--}`,
      login_row_id: null,
      station_login_id: '',
      station_login_password: '',
      showPassword: false,
      isNew: true,
    });
    row.isEditing = true;
    this.errorMessage = '';
    this.successMessage = '';
  }

  togglePassword(login: StationLoginEntry): void {
    login.showPassword = !login.showPassword;
  }

  trackByStation(index: number, row: StationLoginStation): number {
    return row.station_id || index;
  }

  trackByLogin(index: number, login: StationLoginEntry): string {
    return login.row_key || String(index);
  }

  private validateRows(): string {
    const loginIds = new Set<string>();
    for (const row of this.rows) {
      for (const login of row.logins) {
        if (!this.hasLoginData(login)) {
          continue;
        }

        const loginId = login.station_login_id.trim();
        const password = login.station_login_password.trim();
        if (!loginId || !password) {
          return `User ID and password are required for station ${row.station_code}.`;
        }

        const key = loginId.toLowerCase();
        if (loginIds.has(key)) {
          return `User ID ${loginId} is already used by another station.`;
        }

        loginIds.add(key);
      }
    }

    return '';
  }

  private hasLoginData(login: StationLoginEntry): boolean {
    return !!login.station_login_id.trim() || !!login.station_login_password.trim();
  }

  private groupStationRows(rows: Partial<StationLoginApiRow>[]): StationLoginStation[] {
    const grouped = new Map<number, StationLoginStation>();
    for (const row of rows) {
      const stationId = Number(row.station_id || row.id || 0);
      if (!grouped.has(stationId)) {
        grouped.set(stationId, {
          station_id: stationId,
          station_order: Number(row.station_order || 0),
          station_code: row.station_code || '',
          station_name: row.station_name || '',
          isEditing: false,
          logins: [],
        });
      }

      grouped.get(stationId)!.logins.push({
        row_key: String(row.login_row_id || `station-${stationId}-${grouped.get(stationId)!.logins.length}`),
        login_row_id: row.login_row_id ?? null,
        station_login_id: row.station_login_id || '',
        station_login_password: row.station_login_password || '',
        showPassword: false,
        isNew: false,
      });
    }

    return Array.from(grouped.values()).map((station) => ({
      ...station,
      logins: station.logins.length
        ? station.logins
        : [{
            row_key: `station-${station.station_id}-empty`,
            login_row_id: null,
            station_login_id: '',
            station_login_password: '',
            showPassword: false,
            isNew: false,
          }],
    }));
  }
}
