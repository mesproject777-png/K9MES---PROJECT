import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { environment } from '../../../../environments/environment';

interface StationRow {
  id: number;
  station_code: string;
  station_desc: string;
  status: string;
  created_at?: string;
}

interface StationsResponse {
  data: StationRow[];
  total: number;
  page: number;
  limit: number;
}

@Component({
  selector: 'app-stations',
  standalone: false,
  templateUrl: './stations.component.html',
  styleUrl: './stations.component.scss'
})
export class StationsComponent implements OnInit {
  readonly apiUrl = `${environment.apiUrl}/api/stations`;

  isLoading = false;
  isSaving = false;
  errorMessage = '';
  successMessage = '';

  stations: StationRow[] = [];
  totalStations = 0;
  page = 1;
  limit: number | 'all' = 25;
  searchText = '';

  isModalOpen = false;
  isEditMode = false;
  editingStationId: number | null = null;

  stationForm: FormGroup;

  private clearMessageTimer: number | null = null;

  constructor(
    private http: HttpClient,
    private fb: FormBuilder
  ) {
    this.stationForm = this.fb.group({
      station_code: ['', Validators.required],
      station_desc: ['', Validators.required],
    });
  }

  ngOnInit(): void {
    this.loadStations();
  }

  loadStations(): void {
    this.isLoading = true;
    this.errorMessage = '';

    let params = new HttpParams().set('page', String(this.page));

    if (this.limit === 'all') {
      params = params.set('limit', 'all');
    } else {
      params = params.set('limit', String(this.limit));
    }

    if (this.searchText.trim()) {
      params = params.set('search', this.searchText.trim());
    }

    this.http.get<StationsResponse>(this.apiUrl, { params }).subscribe({
      next: (response) => {
        this.stations = response.data || [];
        this.totalStations = response.total || 0;
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Unable to load stations.';
        this.isLoading = false;
      }
    });
  }

  onSearchChange(value: string): void {
    this.searchText = value;
    this.page = 1;
    this.loadStations();
  }

  onLimitChange(value: string): void {
    this.limit = value === 'all' ? 'all' : Number(value);
    this.page = 1;
    this.loadStations();
  }

  changePage(nextPage: number): void {
    const totalPages = this.totalPages;
    const clamped = Math.min(Math.max(nextPage, 1), totalPages || 1);

    if (clamped === this.page) {
      return;
    }

    this.page = clamped;
    this.loadStations();
  }

  get totalPages(): number {
    if (this.limit === 'all') {
      return 1;
    }

    return Math.max(1, Math.ceil(this.totalStations / this.limit));
  }

  openCreateModal(): void {
    this.isEditMode = false;
    this.editingStationId = null;
    this.stationForm.reset({
      station_code: '',
      station_desc: '',
    });
    this.isModalOpen = true;
  }

  openEditModal(station: StationRow): void {
    this.isEditMode = true;
    this.editingStationId = station.id;
    this.stationForm.reset({
      station_code: station.station_code,
      station_desc: station.station_desc,
    });
    this.isModalOpen = true;
  }

  closeModal(): void {
    this.isModalOpen = false;
  }

  saveStation(): void {
    this.successMessage = '';
    this.errorMessage = '';

    if (this.stationForm.invalid) {
      this.stationForm.markAllAsTouched();
      this.errorMessage = 'Station code and station description are required.';
      this.scheduleClearMessages();
      return;
    }

    this.isSaving = true;
    const payload = this.stationForm.value;

    const request$ = this.isEditMode && this.editingStationId
      ? this.http.put<StationRow>(`${this.apiUrl}/${this.editingStationId}`, payload)
      : this.http.post<StationRow>(this.apiUrl, payload);

    request$.subscribe({
      next: () => {
        this.isSaving = false;
        this.isModalOpen = false;
        this.successMessage = this.isEditMode ? 'Station updated successfully.' : 'Station created successfully.';
        this.scheduleClearMessages();
        this.loadStations();
      },
      error: (error) => {
        this.isSaving = false;
        this.errorMessage = error?.error?.message || 'Unable to save station.';
        this.scheduleClearMessages();
      }
    });
  }

  deleteStation(station: StationRow): void {
    if (!confirm(`Delete station ${station.station_code}?`)) {
      return;
    }

    this.http.delete(`${this.apiUrl}/${station.id}`).subscribe({
      next: () => {
        this.successMessage = 'Station deleted successfully.';
        this.scheduleClearMessages();
        this.loadStations();
      },
      error: (error) => {
        this.errorMessage = error?.error?.message || 'Unable to delete station.';
        this.scheduleClearMessages();
      }
    });
  }

  copyVisibleRows(): void {
    const lines = this.stations.map((row) => `${row.station_code}\t${row.station_desc}\t${row.status}`);
    navigator.clipboard.writeText(lines.join('\n')).then(() => {
      this.successMessage = 'Visible rows copied.';
      this.scheduleClearMessages();
    });
  }

  downloadCsv(): void {
    const header = ['station_code', 'station_desc', 'status'];
    const rows = this.stations.map((row) => [
      this.escapeCsv(row.station_code),
      this.escapeCsv(row.station_desc),
      this.escapeCsv(row.status),
    ].join(','));

    const csv = [header.join(','), ...rows].join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'stations.csv';
    link.click();
    URL.revokeObjectURL(url);
  }

  private escapeCsv(value: string): string {
    const safe = String(value ?? '');
    if (safe.includes(',') || safe.includes('"') || safe.includes('\n')) {
      return `"${safe.replaceAll('"', '""')}"`;
    }
    return safe;
  }

  private scheduleClearMessages(): void {
    if (this.clearMessageTimer) {
      window.clearTimeout(this.clearMessageTimer);
    }

    this.clearMessageTimer = window.setTimeout(() => {
      this.successMessage = '';
      this.errorMessage = '';
      this.clearMessageTimer = null;
    }, 3000);
  }
}
