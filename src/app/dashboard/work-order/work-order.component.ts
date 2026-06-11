import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { environment } from '../../../environments/environment';

type WorkflowWorkOrderSummary = {
  wo: string;
  partNumber: string;
  snType: string;
  dueDate: string;
  quantity: number | null;
  stationCount: number;
  bomCount: number;
};

type WorkflowWorkOrderApiRow = {
  wo: string | null;
  part_number: string;
  sn_type: string;
  due_date: string | null;
  quantity: number | null;
  station_count: number;
  bom_count: number;
};

@Component({
  selector: 'app-work-order',
  standalone: false,
  templateUrl: './work-order.component.html',
  styleUrl: './work-order.component.scss'
})
export class WorkOrderComponent implements OnInit, OnDestroy {
  private readonly apiUrl = `${environment.apiUrl}/api/workflow/work-orders`;
  private readonly workflowUpdatedStorageKey = 'k9_workflow_work_orders_updated_at';

  rows: WorkflowWorkOrderSummary[] = [];
  woFilter = '';
  pnFilter = '';
  page = 1;
  limit = 15;
  total = 0;
  isLoading = false;
  errorMessage = '';
  private lastObservedWorkflowUpdate = localStorage.getItem(this.workflowUpdatedStorageKey) || '';
  private refreshTimer: number | null = null;

  constructor(
    private http: HttpClient,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.loadRows();
    this.refreshTimer = window.setInterval(() => this.reloadIfWorkflowChanged(), 3000);
  }

  ngOnDestroy(): void {
    if (this.refreshTimer !== null) {
      window.clearInterval(this.refreshTimer);
    }
  }

  @HostListener('window:focus')
  onWindowFocus(): void {
    this.reloadIfWorkflowChanged();
  }

  loadRows(): void {
    this.isLoading = true;
    this.errorMessage = '';

    let params = new HttpParams()
      .set('page', String(this.page))
      .set('limit', String(this.limit));

    if (this.woFilter.trim()) params = params.set('wo', this.woFilter.trim());
    if (this.pnFilter.trim()) params = params.set('pn', this.pnFilter.trim());

    this.http.get<{ data: WorkflowWorkOrderApiRow[]; total: number; page: number; limit: number }>(this.apiUrl, { params }).subscribe({
      next: (response) => {
        this.rows = (response.data || []).map((row) => this.mapApiRow(row));
        this.total = response.total || 0;
        this.isLoading = false;
      },
      error: (error) => {
        this.rows = [];
        this.total = 0;
        this.isLoading = false;
        this.errorMessage = error?.error?.message || error?.error?.error || 'Unable to load work orders.';
      }
    });
  }

  downloadFullList(): void {
    let params = new HttpParams()
      .set('page', '1')
      .set('limit', 'all');

    if (this.woFilter.trim()) params = params.set('wo', this.woFilter.trim());
    if (this.pnFilter.trim()) params = params.set('pn', this.pnFilter.trim());

    this.http.get<{ data: WorkflowWorkOrderApiRow[] }>(this.apiUrl, { params }).subscribe({
      next: (response) => {
        this.downloadRows((response.data || []).map((row) => this.mapApiRow(row)));
      },
      error: (error) => {
        this.errorMessage = error?.error?.message || error?.error?.error || 'Unable to download list.';
      }
    });
  }

  onWoFilterChange(value: string): void {
    this.woFilter = value;
    this.page = 1;
    this.loadRows();
  }

  onPnFilterChange(value: string): void {
    this.pnFilter = value;
    this.page = 1;
    this.loadRows();
  }

  changePage(nextPage: number): void {
    const totalPages = this.totalPages || 1;
    const clampedPage = Math.min(Math.max(nextPage, 1), totalPages);
    if (clampedPage === this.page) {
      return;
    }

    this.page = clampedPage;
    this.loadRows();
  }

  editWorkOrder(row: WorkflowWorkOrderSummary): void {
    this.router.navigate(['/dashboard/workflow'], {
      queryParams: {
        pn: row.partNumber,
        wo: row.wo,
      },
    });
  }

  viewSnList(row: WorkflowWorkOrderSummary): void {
    this.router.navigate(['/dashboard/workorder/SNList'], {
      state: {
        wo: row.wo,
      },
    });
  }

  openStationLogins(row: WorkflowWorkOrderSummary): void {
    this.router.navigate(['/dashboard/workorder/station-logins'], {
      queryParams: {
        pn: row.partNumber,
        wo: row.wo,
      },
    });
  }

  get pagedRows(): WorkflowWorkOrderSummary[] {
    return this.rows;
  }

  get totalPages(): number {
    return Math.ceil(this.total / this.limit);
  }

  get showingFrom(): number {
    return this.total ? ((this.page - 1) * this.limit) + 1 : 0;
  }

  get showingTo(): number {
    return Math.min(this.page * this.limit, this.total);
  }

  private mapApiRow(row: WorkflowWorkOrderApiRow): WorkflowWorkOrderSummary {
    return {
      wo: row.wo || '',
      partNumber: row.part_number,
      snType: row.sn_type,
      dueDate: row.due_date || '',
      quantity: row.quantity,
      stationCount: row.station_count,
      bomCount: row.bom_count,
    };
  }

  private reloadIfWorkflowChanged(): void {
    const latestUpdate = localStorage.getItem(this.workflowUpdatedStorageKey) || '';
    if (!latestUpdate || latestUpdate === this.lastObservedWorkflowUpdate) {
      return;
    }

    this.lastObservedWorkflowUpdate = latestUpdate;
    this.page = 1;
    this.loadRows();
  }

  private downloadRows(rows: WorkflowWorkOrderSummary[]): void {
    const header = ['WO', 'Part Number', 'SN Type', 'Due Date', 'Quantity', 'Station', 'BOM'];
    const csvRows = [
      header.join(','),
      ...rows.map((row) => [
        this.escapeCsv(row.wo),
        this.escapeCsv(row.partNumber),
        this.escapeCsv(row.snType),
        this.escapeCsv(row.dueDate ? String(row.dueDate).slice(0, 10) : ''),
        this.escapeCsv(row.quantity === null ? '' : String(row.quantity)),
        this.escapeCsv(String(row.stationCount)),
        this.escapeCsv(String(row.bomCount)),
      ].join(',')),
    ];

    const blob = new Blob([csvRows.join('\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'workflow_work_orders.csv';
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
}
