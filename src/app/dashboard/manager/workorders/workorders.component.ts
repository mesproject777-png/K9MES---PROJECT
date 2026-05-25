import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';

interface Site {
  id: number;
  name: string;
}

interface WorkOrderRow {
  id: number;
  wo: string;
  site_name: string;
  pl_desc?: string | null;
  due_date: string;
  qty: number;
  status: string;
  pn: string;
  revision: string;
  balance: number;
  lot?: string | null;
}

interface WorkOrdersResponse {
  data: WorkOrderRow[];
  total: number;
  page: number;
  limit: number;
}

interface ItemLookupRow {
  id: number;
  pn: string;
  description: string;
}

interface ItemRevisionRow {
  id: number;
  revision: string;
  in_date: string;
  expire_date?: string | null;
  version?: string | null;
}

@Component({
  selector: 'app-workorders',
  standalone: false,
  templateUrl: './workorders.component.html',
  styleUrl: './workorders.component.scss'
})
export class WorkordersComponent implements OnInit {
  readonly apiBase = 'http://localhost:5000/api/work-orders';
  readonly itemRevisionsApi = 'http://localhost:5000/api/item-revisions';
  readonly sitesApi = 'http://localhost:5000/api/sites';

  isLoading = false;
  isSaving = false;
  successMessage = '';
  errorMessage = '';

  woFilter = '';
  pnFilter = '';
  page = 1;
  limit = 15;
  total = 0;
  rows: WorkOrderRow[] = [];

  sites: Site[] = [];

  isCreateModalOpen = false;
  isEditMode = false;
  editingWorkOrderId: number | null = null;
  woForm: FormGroup;

  pnQuery = '';
  pnSuggestions: ItemLookupRow[] = [];
  selectedPn: ItemLookupRow | null = null;
  private lookupTimer: number | null = null;

  private clearMessageTimer: number | null = null;

  constructor(
    private http: HttpClient,
    private fb: FormBuilder
  ) {
    const today = new Date().toISOString().slice(0, 10);
    this.woForm = this.fb.group({
      wo: ['', Validators.required],
      site_id: [null, Validators.required],
      due_date: [today, Validators.required],
      qty: [null, [Validators.required, Validators.min(1)]],
      status: ['Released', Validators.required],
      pn: ['', Validators.required],
      revision: ['', Validators.required],
      lot: [''],
    });
  }

  ngOnInit(): void {
    this.loadSites();
    this.loadWorkOrders();
  }

  loadSites(): void {
    this.http.get<Site[]>(this.sitesApi).subscribe({
      next: (sites) => {
        this.sites = sites || [];
      },
      error: () => {
        this.errorMessage = 'Unable to load sites.';
        this.scheduleClearMessages();
      }
    });
  }

  loadWorkOrders(): void {
    this.isLoading = true;
    this.errorMessage = '';

    let params = new HttpParams()
      .set('page', String(this.page))
      .set('limit', String(this.limit));

    if (this.woFilter.trim()) params = params.set('wo', this.woFilter.trim());
    if (this.pnFilter.trim()) params = params.set('pn', this.pnFilter.trim());

    this.http.get<WorkOrdersResponse>(this.apiBase, { params }).subscribe({
      next: (response) => {
        this.rows = response.data || [];
        this.total = response.total || 0;
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Unable to load work orders.';
        this.isLoading = false;
        this.scheduleClearMessages();
      }
    });
  }

  onWoFilterChange(value: string): void {
    this.woFilter = value;
    this.page = 1;
    this.loadWorkOrders();
  }

  onPnFilterChange(value: string): void {
    this.pnFilter = value;
    this.page = 1;
    this.loadWorkOrders();
  }

  changePage(nextPage: number): void {
    const totalPages = this.totalPages;
    const clamped = Math.min(Math.max(nextPage, 1), totalPages || 1);
    if (clamped === this.page) return;
    this.page = clamped;
    this.loadWorkOrders();
  }

  get totalPages(): number {
    return Math.ceil(this.total / this.limit);
  }

  openCreateModal(): void {
    this.successMessage = '';
    this.errorMessage = '';
    this.isCreateModalOpen = true;
    this.isEditMode = false;
    this.editingWorkOrderId = null;
    this.pnSuggestions = [];
    this.selectedPn = null;
    this.pnQuery = '';

    const today = new Date().toISOString().slice(0, 10);
    this.woForm.reset({
      wo: '',
      site_id: null,
      due_date: today,
      qty: null,
      status: 'Released',
      pn: '',
      revision: '',
      lot: '',
    });
  }

  closeCreateModal(): void {
    this.isCreateModalOpen = false;
    this.isEditMode = false;
    this.editingWorkOrderId = null;
  }

  openEditModal(row: WorkOrderRow): void {
    this.successMessage = '';
    this.errorMessage = '';
    this.isCreateModalOpen = true;
    this.isEditMode = true;
    this.editingWorkOrderId = row.id;
    this.pnSuggestions = [];
    this.selectedPn = null;
    this.pnQuery = row.pn;

    const matchedSite = this.sites.find((s) => s.name === row.site_name);
    this.woForm.reset({
      wo: row.wo,
      site_id: matchedSite?.id || null,
      due_date: String(row.due_date).slice(0, 10),
      qty: row.qty,
      status: row.status,
      pn: row.pn,
      revision: row.revision,
      lot: row.lot || '',
    });
  }

  onPnInput(value: string): void {
    this.pnQuery = value;
    this.selectedPn = null;
    this.woForm.patchValue({ pn: value });

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
      this.http.get<{ data: ItemLookupRow[] }>(`${this.itemRevisionsApi}/lookup`, { params }).subscribe({
        next: (response) => {
          this.pnSuggestions = response.data || [];
        },
        error: () => {
          this.pnSuggestions = [];
        }
      });
    }, 250);
  }

  selectPnSuggestion(s: ItemLookupRow): void {
    this.selectedPn = s;
    this.pnQuery = s.pn;
    this.pnSuggestions = [];
    this.woForm.patchValue({ pn: s.pn });
  }

  saveWorkOrder(): void {
    this.successMessage = '';
    this.errorMessage = '';

    if (this.woForm.invalid) {
      this.woForm.markAllAsTouched();
      this.errorMessage = 'Please fill all required fields.';
      this.scheduleClearMessages();
      return;
    }

    const wasEditMode = this.isEditMode;
    this.isSaving = true;
    const request$ = wasEditMode && this.editingWorkOrderId
      ? this.http.put(`${this.apiBase}/${this.editingWorkOrderId}`, this.woForm.value)
      : this.http.post(this.apiBase, this.woForm.value);

    request$.subscribe({
      next: () => {
        this.isSaving = false;
        this.isCreateModalOpen = false;
        this.isEditMode = false;
        this.editingWorkOrderId = null;
        this.successMessage = wasEditMode ? 'WO updated successfully.' : 'WO created successfully.';
        this.scheduleClearMessages();
        if (!wasEditMode) {
          this.page = 1;
        }
        this.loadWorkOrders();
      },
      error: (error) => {
        this.isSaving = false;
        this.errorMessage = error?.error?.message || 'Unable to create WO.';
        this.scheduleClearMessages();
      }
    });
  }

  downloadFullList(): void {
    this.errorMessage = '';
    this.successMessage = '';

    let params = new HttpParams()
      .set('page', '1')
      .set('limit', '500');

    if (this.woFilter.trim()) params = params.set('wo', this.woFilter.trim());
    if (this.pnFilter.trim()) params = params.set('pn', this.pnFilter.trim());

    this.http.get<WorkOrdersResponse>(this.apiBase, { params }).subscribe({
      next: (response) => {
        const rows = response.data || [];
        const header = ['wo', 'Site name', 'pl_desc', 'due_date', 'qty', 'status', 'pn', 'revision', 'balance', 'lot'];
        const csvRows = [
          header.join(','),
          ...rows.map((row) => [
            this.escapeCsv(row.wo),
            this.escapeCsv(row.site_name),
            this.escapeCsv(row.pl_desc || ''),
            this.escapeCsv(String(row.due_date).slice(0, 10)),
            this.escapeCsv(String(row.qty)),
            this.escapeCsv(row.status),
            this.escapeCsv(row.pn),
            this.escapeCsv(row.revision),
            this.escapeCsv(String(row.balance)),
            this.escapeCsv(row.lot || ''),
          ].join(',')),
        ];

        const blob = new Blob([csvRows.join('\n')], { type: 'text/csv;charset=utf-8;' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'work_orders.csv';
        link.click();
        URL.revokeObjectURL(url);
      },
      error: () => {
        this.errorMessage = 'Unable to download list.';
        this.scheduleClearMessages();
      }
    });
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
