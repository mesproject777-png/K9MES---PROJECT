import { HttpClient, HttpParams } from '@angular/common/http';
import { Component } from '@angular/core';
import { environment } from '../../../../environments/environment';

interface ItemLookupRow {
  id: number;
  pn: string;
  description: string;
}

interface ItemRevisionRow {
  id: number;
  item_id: number;
  revision: string;
  in_date: string;
  expire_date?: string | null;
}

interface StationRow {
  id: number;
  station_code: string;
  station_desc: string;
  created_at: string;
  status: string;
}

interface AssemblyLineRow {
  id: number | null;
  son_pn: string;
  son_description: string;
  son_rev: string;
  son_item_type: string;
  son_pn_type: string;
  station_code: string;
  station_name: string;
  assemble_order: number | null;
  pattern_regex: string;
  part_to_validate: number | null;
  regex_value_to_match: string;
  transform_regex: string;
}

interface AssemblyHistoryRow {
  id: number;
  action: string;
  description: string;
  change_data: Record<string, unknown>;
  changed_by: string;
  changed_at: string;
}

interface AssemblyViewResponse {
  item: ItemLookupRow;
  revision: ItemRevisionRow;
  data: AssemblyLineRow[];
  history: AssemblyHistoryRow[];
  total: number;
}

interface RevisionsResponse {
  item: ItemLookupRow;
  data: ItemRevisionRow[];
  total: number;
}

interface StationsResponse {
  data: StationRow[];
  total: number;
  page: number;
  limit: number;
}

interface AssemblyGroup {
  station_code: string;
  station_name: string;
  lines: AssemblyLineRow[];
}

@Component({
  selector: 'app-assemblydefinition',
  standalone: false,
  templateUrl: './assemblydefinition.component.html',
  styleUrl: './assemblydefinition.component.scss'
})
export class AssemblydefinitionComponent {
  readonly apiBase = `${environment.apiUrl}/api/assembly`;
  readonly stationsApiBase = `${environment.apiUrl}/api/stations`;

  pnQuery = '';
  pnSuggestions: ItemLookupRow[] = [];
  selectedMainItem: ItemLookupRow | null = null;
  mainRevisions: ItemRevisionRow[] = [];
  selectedMainRevision = '';

  lines: AssemblyLineRow[] = [];
  lineGroups: AssemblyGroup[] = [];

  isLoading = false;
  isSaving = false;
  successMessage = '';
  errorMessage = '';

  isHistoryModalOpen = false;
  isHistoryLoading = false;
  history: AssemblyHistoryRow[] = [];

  isEditModalOpen = false;
  editingLine: AssemblyLineRow | null = null;

  stations: StationRow[] = [];
  isStationsLoading = false;
  selectedStationCode = '';
  assembleOrder: number | null = null;
  patternRegex = 'Skip';
  partToValidate: number | null = 1;
  regexValueToMatch = '';
  transformRegex = '';

  private lookupTimer: number | null = null;
  private clearMessageTimer: number | null = null;

  constructor(private http: HttpClient) {}

  onPnInput(value: string): void {
    this.pnQuery = value;
    this.selectedMainItem = null;
    this.mainRevisions = [];
    this.selectedMainRevision = '';
    this.lines = [];
    this.lineGroups = [];
    this.history = [];

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

  selectPnFromSuggestion(item: ItemLookupRow): void {
    this.pnQuery = item.pn;
    this.pnSuggestions = [];
    this.selectedMainItem = item;
    this.mainRevisions = [];
    this.selectedMainRevision = '';
    this.lines = [];
    this.lineGroups = [];
    this.history = [];

    this.loadMainRevisions(item.id);
  }

  onRevisionChange(revision: string): void {
    this.selectedMainRevision = revision;
  }

  runSearch(): void {
    if (!this.selectedMainItem) {
      this.errorMessage = 'Please select a valid PN first.';
      this.scheduleClearMessages();
      return;
    }

    if (!this.selectedMainRevision) {
      this.errorMessage = 'Please select revision.';
      this.scheduleClearMessages();
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';

    const params = new HttpParams()
      .set('pn', this.selectedMainItem.pn)
      .set('revision', this.selectedMainRevision)
      .set('includeHistory', 'false');

    this.http.get<AssemblyViewResponse>(`${this.apiBase}/view/search`, { params }).subscribe({
      next: (response) => {
        this.lines = response.data || [];
        this.lineGroups = this.buildGroups(this.lines);
        this.isLoading = false;
      },
      error: (error) => {
        this.lines = [];
        this.lineGroups = [];
        this.isLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to load Assembly Definition data.';
        this.scheduleClearMessages();
      }
    });
  }

  openHistoryModal(): void {
    if (!this.selectedMainItem || !this.selectedMainRevision) {
      this.errorMessage = 'Select PN and Revision first.';
      this.scheduleClearMessages();
      return;
    }

    this.isHistoryModalOpen = true;
    this.isHistoryLoading = true;

    const params = new HttpParams()
      .set('pn', this.selectedMainItem.pn)
      .set('revision', this.selectedMainRevision)
      .set('includeHistory', 'true');

    this.http.get<AssemblyViewResponse>(`${this.apiBase}/view/search`, { params }).subscribe({
      next: (response) => {
        this.history = response.history || [];
        this.isHistoryLoading = false;
      },
      error: (error) => {
        this.history = [];
        this.isHistoryLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to load history.';
        this.scheduleClearMessages();
      }
    });
  }

  closeHistoryModal(): void {
    this.isHistoryModalOpen = false;
  }

  openEditLineModal(line: AssemblyLineRow): void {
    if (!this.selectedMainItem || !this.selectedMainRevision) {
      this.errorMessage = 'Select PN and Revision first.';
      this.scheduleClearMessages();
      return;
    }

    this.editingLine = line;
    this.isEditModalOpen = true;
    this.selectedStationCode = line.station_code || '';
    this.assembleOrder = line.assemble_order ?? null;
    this.patternRegex = line.pattern_regex || '';
    this.partToValidate = line.part_to_validate ?? null;
    this.regexValueToMatch = line.regex_value_to_match || '';
    this.transformRegex = line.transform_regex || '';

    if (this.stations.length === 0) {
      this.loadStations();
    }
  }

  closeEditModal(): void {
    this.isEditModalOpen = false;
    this.editingLine = null;
  }

  saveAssemblyDefinition(): void {
    if (!this.selectedMainItem || !this.selectedMainRevision) {
      this.errorMessage = 'Main PN + revision are required.';
      this.scheduleClearMessages();
      return;
    }

    if (!this.editingLine) {
      this.errorMessage = 'Select a line to edit.';
      this.scheduleClearMessages();
      return;
    }

    if (!this.selectedStationCode) {
      this.errorMessage = 'Station is required.';
      this.scheduleClearMessages();
      return;
    }

    if (this.assembleOrder !== null && (!Number.isFinite(this.assembleOrder) || this.assembleOrder <= 0)) {
      this.errorMessage = 'Assembly order must be a positive number.';
      this.scheduleClearMessages();
      return;
    }

    if (this.partToValidate !== null && (!Number.isFinite(this.partToValidate) || this.partToValidate <= 0)) {
      this.errorMessage = 'Regex group to validate must be a positive number.';
      this.scheduleClearMessages();
      return;
    }

    this.isSaving = true;

    const payload = {
      station_code: this.selectedStationCode,
      assemble_order: this.assembleOrder,
      pattern_regex: this.patternRegex,
      part_to_validate: this.partToValidate,
      regex_value_to_match: this.regexValueToMatch,
      transform_regex: this.transformRegex,
    };

    const request$ = this.editingLine.id
      ? this.http.put(`${this.apiBase}/lines/${this.editingLine.id}`, payload)
      : this.http.post(`${this.apiBase}/lines`, {
          main_pn: this.selectedMainItem.pn,
          main_revision: this.selectedMainRevision,
          son_pn: this.editingLine.son_pn,
          son_rev: '',
          ...payload,
        });

    request$.subscribe({
      next: () => {
        this.isSaving = false;
        this.isEditModalOpen = false;
        this.editingLine = null;
        this.successMessage = 'Assembly definition saved.';
        this.scheduleClearMessages();
        this.runSearch();
      },
      error: (error) => {
        this.isSaving = false;
        this.errorMessage = error?.error?.message || 'Unable to save assembly definition.';
        this.scheduleClearMessages();
      }
    });
  }

  formatChangeData(changeData: Record<string, unknown>): Array<{ key: string; value: string }> {
    if (!changeData || typeof changeData !== 'object') {
      return [];
    }

    return Object.keys(changeData).map((key) => ({
      key,
      value: String(changeData[key] ?? ''),
    }));
  }

  private loadMainRevisions(itemId: number): void {
    this.http.get<RevisionsResponse>(`${this.apiBase}/${itemId}/revisions`).subscribe({
      next: (response) => {
        this.mainRevisions = response.data || [];
        if (this.mainRevisions.length > 0) {
          this.selectedMainRevision = this.mainRevisions[0].revision;
        }
      },
      error: (error) => {
        this.mainRevisions = [];
        this.selectedMainRevision = '';
        this.errorMessage = error?.error?.message || 'Unable to load revisions.';
        this.scheduleClearMessages();
      }
    });
  }

  private loadStations(): void {
    this.isStationsLoading = true;

    const params = new HttpParams().set('limit', 'all');
    this.http.get<StationsResponse>(this.stationsApiBase, { params }).subscribe({
      next: (response) => {
        this.stations = response.data || [];
        this.isStationsLoading = false;
      },
      error: () => {
        this.stations = [];
        this.isStationsLoading = false;
      }
    });
  }

  private buildGroups(lines: AssemblyLineRow[]): AssemblyGroup[] {
    const groups = new Map<string, AssemblyGroup>();

    for (const line of lines || []) {
      const key = String(line.station_code || '').toUpperCase();
      if (!groups.has(key)) {
        groups.set(key, {
          station_code: line.station_code,
          station_name: line.station_name,
          lines: [],
        });
      }

      groups.get(key)!.lines.push(line);
    }

    return Array.from(groups.values()).sort((a, b) => a.station_code.localeCompare(b.station_code));
  }

  private scheduleClearMessages(): void {
    if (this.clearMessageTimer) {
      window.clearTimeout(this.clearMessageTimer);
    }

    this.clearMessageTimer = window.setTimeout(() => {
      this.successMessage = '';
      this.errorMessage = '';
    }, 4000);
  }
}
