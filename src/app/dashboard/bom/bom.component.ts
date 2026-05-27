import { HttpClient, HttpParams } from '@angular/common/http';
import { Component } from '@angular/core';
import { environment } from '../../../environments/environment';

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

interface BomLineRow {
  id: number;
  son_pn: string;
  son_description: string;
  son_rev: string;
  son_item_type: string;
  son_pn_type: string;
  son_qty: number;
  reference_designators: string;
}

interface BomHistoryRow {
  id: number;
  action: string;
  description: string;
  change_data: Record<string, unknown>;
  changed_by: string;
  changed_at: string;
}

interface BomViewResponse {
  item: ItemLookupRow;
  revision: ItemRevisionRow;
  data: BomLineRow[];
  history: BomHistoryRow[];
  total: number;
}

interface RevisionsResponse {
  item: ItemLookupRow;
  data: ItemRevisionRow[];
  total: number;
}

@Component({
  selector: 'app-bom',
  standalone: false,
  templateUrl: './bom.component.html',
  styleUrl: './bom.component.scss'
})
export class BomComponent {
  readonly apiBase = `${environment.apiUrl}/api/bom`;

  pnQuery = '';
  pnSuggestions: ItemLookupRow[] = [];
  selectedMainItem: ItemLookupRow | null = null;
  mainRevisions: ItemRevisionRow[] = [];
  selectedMainRevision = '';

  lines: BomLineRow[] = [];
  history: BomHistoryRow[] = [];
  showHistory = false;

  isLoading = false;
  isSaving = false;
  successMessage = '';
  errorMessage = '';

  isAddModalOpen = false;
  sonPnQuery = '';
  sonPnSuggestions: ItemLookupRow[] = [];
  selectedSonItem: ItemLookupRow | null = null;
  sonRevisions: ItemRevisionRow[] = [];
  selectedSonRevision = '';
  sonQty = 1;
  sonReferenceDesignators = '';

  private lookupTimer: number | null = null;
  private sonLookupTimer: number | null = null;
  private clearMessageTimer: number | null = null;

  constructor(private http: HttpClient) {}

  onPnInput(value: string): void {
    this.pnQuery = value;
    this.selectedMainItem = null;
    this.mainRevisions = [];
    this.selectedMainRevision = '';
    this.lines = [];
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
      .set('includeHistory', String(this.showHistory));

    this.http.get<BomViewResponse>(`${this.apiBase}/view/search`, { params }).subscribe({
      next: (response) => {
        this.lines = response.data || [];
        this.history = response.history || [];
        this.isLoading = false;
      },
      error: (error) => {
        this.lines = [];
        this.history = [];
        this.isLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to load BOM data.';
        this.scheduleClearMessages();
      }
    });
  }

  toggleHistory(): void {
    this.showHistory = !this.showHistory;
    if (this.selectedMainItem && this.selectedMainRevision) {
      this.runSearch();
    }
  }

  openAddSonModal(): void {
    if (!this.selectedMainItem || !this.selectedMainRevision) {
      this.errorMessage = 'Select PN and Revision before adding BOM line.';
      this.scheduleClearMessages();
      return;
    }

    this.isAddModalOpen = true;
    this.sonPnQuery = '';
    this.sonPnSuggestions = [];
    this.selectedSonItem = null;
    this.sonRevisions = [];
    this.selectedSonRevision = '';
    this.sonQty = 1;
    this.sonReferenceDesignators = '';
  }

  closeAddSonModal(): void {
    this.isAddModalOpen = false;
  }

  onSonPnInput(value: string): void {
    this.sonPnQuery = value;
    this.selectedSonItem = null;
    this.sonRevisions = [];
    this.selectedSonRevision = '';

    if (this.sonLookupTimer) {
      window.clearTimeout(this.sonLookupTimer);
    }

    const trimmed = value.trim();
    if (trimmed.length < 2) {
      this.sonPnSuggestions = [];
      return;
    }

    this.sonLookupTimer = window.setTimeout(() => {
      const params = new HttpParams().set('query', trimmed).set('limit', '20');
      this.http.get<{ data: ItemLookupRow[] }>(`${this.apiBase}/lookup`, { params }).subscribe({
        next: (response) => {
          this.sonPnSuggestions = response.data || [];
        },
        error: () => {
          this.sonPnSuggestions = [];
        }
      });
    }, 250);
  }

  selectSonPnFromSuggestion(item: ItemLookupRow): void {
    this.sonPnQuery = item.pn;
    this.sonPnSuggestions = [];
    this.selectedSonItem = item;
    this.sonRevisions = [];
    this.selectedSonRevision = '';
    this.loadSonRevisions(item.id);
  }

  saveSonLine(): void {
    if (!this.selectedMainItem || !this.selectedMainRevision) {
      this.errorMessage = 'Main PN + revision are required.';
      this.scheduleClearMessages();
      return;
    }

    if (!this.selectedSonItem) {
      this.errorMessage = 'Please select a valid Son PN.';
      this.scheduleClearMessages();
      return;
    }

    if (!Number.isFinite(this.sonQty) || this.sonQty <= 0) {
      this.errorMessage = 'Quantity must be greater than 0.';
      this.scheduleClearMessages();
      return;
    }

    this.isSaving = true;

    this.http.post(`${this.apiBase}/lines`, {
      main_pn: this.selectedMainItem.pn,
      main_revision: this.selectedMainRevision,
      son_pn: this.selectedSonItem.pn,
      son_rev: this.selectedSonRevision,
      son_qty: this.sonQty,
      reference_designators: this.sonReferenceDesignators,
    }).subscribe({
      next: () => {
        this.isSaving = false;
        this.isAddModalOpen = false;
        this.successMessage = 'BOM line added successfully.';
        this.scheduleClearMessages();
        this.runSearch();
      },
      error: (error) => {
        this.isSaving = false;
        this.errorMessage = error?.error?.message || 'Unable to add BOM line.';
        this.scheduleClearMessages();
      }
    });
  }

  deleteLine(line: BomLineRow): void {
    if (!confirm(`Delete BOM line for ${line.son_pn}?`)) {
      return;
    }

    this.http.delete(`${this.apiBase}/lines/${line.id}`, { body: {} }).subscribe({
      next: () => {
        this.successMessage = 'BOM line deleted successfully.';
        this.scheduleClearMessages();
        this.runSearch();
      },
      error: (error) => {
        this.errorMessage = error?.error?.message || 'Unable to delete BOM line.';
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

  private loadSonRevisions(itemId: number): void {
    this.http.get<RevisionsResponse>(`${this.apiBase}/${itemId}/revisions`).subscribe({
      next: (response) => {
        this.sonRevisions = response.data || [];
        if (this.sonRevisions.length > 0) {
          this.selectedSonRevision = this.sonRevisions[0].revision;
        }
      },
      error: () => {
        this.sonRevisions = [];
        this.selectedSonRevision = '';
      }
    });
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
