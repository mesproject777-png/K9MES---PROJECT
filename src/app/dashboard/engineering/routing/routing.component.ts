import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

interface ItemLookupRow {
  id: number;
  pn: string;
  description: string;
}

interface RoutingStepRow {
  id: number;
  item_id: number;
  station_order: number;
  station_code: string;
  station_name: string;
  sample_mode: 'Full' | 'Sample';
  report_mode: 'Regular' | 'Auto Only';
  created_at?: string;
  updated_at?: string;
}

interface RoutingHistoryRow {
  id: number;
  item_id: number;
  routing_step_id?: number | null;
  action: string;
  description: string;
  change_field?: string | null;
  old_value?: string | null;
  new_value?: string | null;
  changed_by?: string;
  changed_at: string;
}

interface RoutingResponse {
  item: ItemLookupRow;
  data: RoutingStepRow[];
  history: RoutingHistoryRow[];
  total: number;
}

interface StationOption {
  id: number;
  station_code: string;
  station_desc: string;
  status: string;
}

interface StationsResponse {
  data: StationOption[];
  total: number;
  page: number;
  limit: number;
}

@Component({
  selector: 'app-routing',
  standalone: false,
  templateUrl: './routing.component.html',
  styleUrl: './routing.component.scss'
})
export class RoutingComponent implements OnInit {
  readonly apiBase = 'http://localhost:5000/api/routing';
  readonly stationsApi = 'http://localhost:5000/api/stations';
  private readonly lastRoutingPnKey = 'k9:lastRoutingPn';

  isLoading = false;
  isSaving = false;
  errorMessage = '';
  successMessage = '';

  pnQuery = '';
  pnSuggestions: ItemLookupRow[] = [];
  private lookupTimer: number | null = null;

  selectedItem: ItemLookupRow | null = null;
  routeSteps: RoutingStepRow[] = [];
  routeHistory: RoutingHistoryRow[] = [];
  includeHistory = false;

  isStepModalOpen = false;
  isEditMode = false;
  editingStepId: number | null = null;
  editingStep: RoutingStepRow | null = null;
  stepForm: FormGroup;
  stations: StationOption[] = [];
  isStationsLoading = false;

  sampleModeOptions: Array<'Full' | 'Sample'> = ['Full', 'Sample'];
  reportModeOptions: Array<'Regular' | 'Auto Only'> = ['Regular', 'Auto Only'];

  constructor(
    private http: HttpClient,
    private fb: FormBuilder,
    private route: ActivatedRoute,
    private router: Router
  ) {
    this.stepForm = this.fb.group({
      station_code: ['', Validators.required],
      sample_mode: ['Full', Validators.required],
      report_mode: ['Regular', Validators.required],
    });
  }

  ngOnInit(): void {
    this.loadStationsMaster();
    this.route.queryParamMap.subscribe((params) => {
      const pn = params.get('pn')?.trim() || this.getLastRoutingPn();
      if (pn) {
        this.selectPnFromSuggestion(pn);
      }
    });
  }

  openLabelsForCurrentItem(): void {
    if (!this.selectedItem) {
      this.errorMessage = 'Please select a part number first.';
      this.scheduleClearMessages();
      return;
    }

    this.router.navigate(['/dashboard/label'], {
      queryParams: { pn: this.selectedItem.pn }
    });
  }

  loadStationsMaster(): void {
    this.isStationsLoading = true;

    const params = new HttpParams().set('limit', 'all').set('page', '1');
    this.http.get<StationsResponse>(this.stationsApi, { params }).subscribe({
      next: (response) => {
        this.stations = (response.data || []).filter((s) => s.status === 'Active');
        this.isStationsLoading = false;
      },
      error: () => {
        this.stations = [];
        this.isStationsLoading = false;
      }
    });
  }

  onPnInput(value: string): void {
    this.pnQuery = value;
    this.selectedItem = null;
    this.routeSteps = [];
    this.routeHistory = [];

    if (this.lookupTimer) {
      window.clearTimeout(this.lookupTimer);
    }

    const trimmed = value.trim();
    if (trimmed.length < 2) {
      this.pnSuggestions = [];
      return;
    }

    this.lookupTimer = window.setTimeout(() => {
      const params = new HttpParams().set('query', trimmed).set('limit', '20');
      this.http.get<{ data: ItemLookupRow[] }>(`${this.apiBase}/lookup`, { params }).subscribe({
        next: (response) => {
          this.pnSuggestions = response.data || [];
        },
        error: () => {
          this.pnSuggestions = [];
        }
      });
    }, 250);
  }

  selectPnFromSuggestion(pn: string): void {
    this.pnQuery = pn;
    this.pnSuggestions = [];

    const params = new HttpParams().set('pn', pn);
    this.http.get<ItemLookupRow>(`${this.apiBase}/by-pn`, { params }).subscribe({
      next: (item) => {
        this.selectedItem = item;
        this.rememberRoutingPn(item.pn);
        this.loadRouting();
      },
      error: (error) => {
        this.selectedItem = null;
        this.routeSteps = [];
        this.routeHistory = [];
        this.errorMessage = error?.error?.message || 'Part number not found';
        this.scheduleClearMessages();
      }
    });
  }

  loadRouting(): void {
    if (!this.selectedItem) return;

    this.isLoading = true;
    this.errorMessage = '';

    const params = new HttpParams().set('includeHistory', String(this.includeHistory));
    this.http.get<RoutingResponse>(`${this.apiBase}/${this.selectedItem.id}/steps`, { params }).subscribe({
      next: (response) => {
        this.routeSteps = response.data || [];
        this.routeHistory = response.history || [];
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Unable to load route data.';
        this.isLoading = false;
        this.scheduleClearMessages();
      }
    });
  }

  toggleHistory(): void {
    this.includeHistory = !this.includeHistory;
    this.loadRouting();
  }

  openCreateStepModal(): void {
    if (!this.selectedItem) {
      this.errorMessage = 'Please select a part number first.';
      this.scheduleClearMessages();
      return;
    }

    if (this.isStationsLoading) {
      this.errorMessage = 'Stations are still loading. Please try again in a moment.';
      this.scheduleClearMessages();
      return;
    }

    if (!this.stations.length) {
      this.errorMessage = 'No active stations available. Please add stations first.';
      this.scheduleClearMessages();
      return;
    }

    this.isEditMode = false;
    this.editingStepId = null;
    this.editingStep = null;
    this.stepForm.reset({
      station_code: '',
      sample_mode: 'Full',
      report_mode: 'Regular',
    });
    this.isStepModalOpen = true;
  }

  openEditStepModal(step: RoutingStepRow): void {
    if (this.isStationsLoading) {
      this.errorMessage = 'Stations are still loading. Please try again in a moment.';
      this.scheduleClearMessages();
      return;
    }

    if (!this.stations.length) {
      this.errorMessage = 'No active stations available. Please add stations first.';
      this.scheduleClearMessages();
      return;
    }

    this.isEditMode = true;
    this.editingStepId = step.id;
    this.editingStep = step;
    this.stepForm.reset({
      station_code: step.station_code,
      sample_mode: step.sample_mode,
      report_mode: step.report_mode,
    });
    this.isStepModalOpen = true;
  }

  closeStepModal(): void {
    this.isStepModalOpen = false;
    this.editingStep = null;
  }

  saveStep(): void {
    this.successMessage = '';
    this.errorMessage = '';

    if (!this.selectedItem) {
      this.errorMessage = 'Please select a part number first.';
      this.scheduleClearMessages();
      return;
    }

    if (this.stepForm.invalid) {
      this.stepForm.markAllAsTouched();
      this.errorMessage = 'Please fill all required fields.';
      this.scheduleClearMessages();
      return;
    }

    this.isSaving = true;
    const formValue = this.stepForm.value;
    const selectedStation = this.stations.find((s) => s.station_code === formValue.station_code);

    if (!selectedStation) {
      this.isSaving = false;
      this.errorMessage = 'Please select a valid station from Stations list.';
      this.scheduleClearMessages();
      return;
    }

    const payload = {
      station_order: this.isEditMode && this.editingStep
        ? this.editingStep.station_order
        : this.getNextStationOrder(),
      station_code: selectedStation.station_code,
      station_name: selectedStation.station_desc,
      sample_mode: formValue.sample_mode,
      report_mode: formValue.report_mode,
    };

    const request$ = this.isEditMode && this.editingStepId
      ? this.http.put(`${this.apiBase}/steps/${this.editingStepId}`, payload)
      : this.http.post(`${this.apiBase}/${this.selectedItem.id}/steps`, payload);

    request$.subscribe({
      next: () => {
        this.isSaving = false;
        this.isStepModalOpen = false;
        this.successMessage = this.isEditMode ? 'Station updated successfully.' : 'Station added successfully.';
        this.rememberRoutingPn(this.selectedItem?.pn || '');
        this.scheduleClearMessages();
        this.loadRouting();
      },
      error: (error) => {
        this.isSaving = false;
        this.errorMessage = error?.error?.message || 'Unable to save station.';
        this.scheduleClearMessages();
      }
    });
  }

  moveStep(step: RoutingStepRow, direction: 'up' | 'down'): void {
    this.http.put(`${this.apiBase}/steps/${step.id}/move`, { direction }).subscribe({
      next: () => {
        this.successMessage = `Station moved ${direction}.`;
        this.scheduleClearMessages();
        this.loadRouting();
      },
      error: (error) => {
        this.errorMessage = error?.error?.message || `Unable to move station ${direction}.`;
        this.scheduleClearMessages();
      }
    });
  }

  deleteStep(step: RoutingStepRow): void {
    if (!confirm(`Delete station ${step.station_code}?`)) {
      return;
    }

    this.http.delete(`${this.apiBase}/steps/${step.id}`, { body: {} }).subscribe({
      next: () => {
        this.successMessage = 'Station deleted successfully.';
        this.scheduleClearMessages();
        this.loadRouting();
      },
      error: (error) => {
        this.errorMessage = error?.error?.message || 'Unable to delete station.';
        this.scheduleClearMessages();
      }
    });
  }

  private getNextStationOrder(): number {
    if (!this.routeSteps.length) {
      return 10;
    }

    const maxOrder = Math.max(...this.routeSteps.map((s) => Number(s.station_order) || 0));
    return maxOrder + 10;
  }

  private clearMessageTimer: number | null = null;

  private getLastRoutingPn(): string {
    try {
      return localStorage.getItem(this.lastRoutingPnKey)?.trim() || '';
    } catch {
      return '';
    }
  }

  private rememberRoutingPn(pn: string): void {
    const cleanPn = pn.trim();
    if (!cleanPn) return;

    try {
      localStorage.setItem(this.lastRoutingPnKey, cleanPn);
    } catch {
      // Local storage can be unavailable in restricted browser modes.
    }
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
