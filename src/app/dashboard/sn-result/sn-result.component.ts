import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnDestroy } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Location } from '@angular/common';
import { Subscription } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  TraceabilityService,
  TraceHistoryRow,
  TraceRouteStep,
  TraceSearchResponse,
} from '../../services/traceability.service';

type SnResultTab = 'preview' | 'history';
type PreviewStatus = 'Completed' | 'In Progress' | 'Pending' | 'Paused';

type WorkflowSnapshot = {
  partNumber?: {
    pn?: string;
    description?: string;
    item_type?: string;
  };
  workOrder?: {
    wo?: string;
    plant?: string | null;
    site_name?: string | null;
    qty?: number | null;
    revision?: string | null;
    lot?: string | null;
  } | null;
  routing?: Array<{
    id: number;
    station_order: number;
    station_code: string;
    station_name: string;
    sample_mode: string;
    report_mode: string;
    preview_status?: PreviewStatus | null;
  }>;
  bom?: Array<{
    id: number;
    son_pn: string;
    station_code: string;
    station_name: string;
    qty: number;
  }>;
  previewStatuses?: Record<string, PreviewStatus>;
};

type PreviewFlowNode = {
  id: string;
  kind: 'operator' | 'station' | 'empty' | 'logistics';
  title?: string;
  icon?: string;
  subtitle?: string;
  variant?: 'cart' | 'pallet' | 'truck';
  station?: {
    id: number;
    station_order: number;
    station_code: string;
    station_name: string;
    sample_mode: string;
    report_mode: string;
    icon: string;
    status: PreviewStatus;
  };
};

type PreviewFlowRow = {
  nodes: PreviewFlowNode[];
  isReversed: boolean;
  turnSide: 'left' | 'right';
};

@Component({
  selector: 'app-sn-result',
  standalone: false,
  templateUrl: './sn-result.component.html',
  styleUrl: './sn-result.component.scss',
})
export class SnResultComponent implements OnDestroy {
  readonly tabs: Array<{ id: SnResultTab; label: string; icon: string }> = [
    { id: 'preview', label: 'SN Preview', icon: 'visibility' },
    { id: 'history', label: 'SN History', icon: 'history' },
  ];

  activeTab: SnResultTab = 'preview';
  query = '';
  loading = false;
  previewLoading = false;
  errorMessage = '';
  previewMessage = '';
  historyMessage = '';
  traceResult: TraceSearchResponse | null = null;
  workflowSnapshot: WorkflowSnapshot | null = null;

  private readonly workflowApiUrl = `${environment.apiUrl}/api/workflow`;
  private routeSub: Subscription | null = null;

  constructor(
    private http: HttpClient,
    private traceabilityService: TraceabilityService,
    private route: ActivatedRoute,
    private router: Router,
    private location: Location
  ) {
    this.routeSub = this.route.queryParamMap.subscribe((params) => {
      const serial = String(params.get('q') || '').trim();
      if (!serial) {
        return;
      }

      this.query = serial;
      this.activeTab = 'preview';
      this.loadSerial(serial);
    });
  }

  ngOnDestroy(): void {
    this.routeSub?.unsubscribe();
  }

  selectTab(tab: SnResultTab): void {
    this.activeTab = tab;
  }

  onSearch(): void {
    const serial = this.query.trim();
    if (!serial) {
      this.errorMessage = 'Please enter a serial number.';
      return;
    }

    this.router.navigate(['/dashboard/sn-result'], {
      queryParams: { q: serial, t: Date.now() },
    });
  }

  goBack(): void {
    this.location.back();
  }

  trackByTab(index: number, tab: { id: SnResultTab }): string {
    return `${index}-${tab.id}`;
  }

  trackByHistory(index: number, row: TraceHistoryRow): string {
    return `${index}-${row.id}`;
  }

  trackByFlowNode(index: number, node: PreviewFlowNode): string {
    return `${index}-${node.id}`;
  }

  get serialNumber(): string {
    return this.traceResult?.serial?.sn || this.query || '-';
  }

  get workOrderNumber(): string {
    return this.traceResult?.device?.work_order || this.workflowSnapshot?.workOrder?.wo || '-';
  }

  get partNumber(): string {
    return this.traceResult?.device?.pn || this.workflowSnapshot?.partNumber?.pn || '-';
  }

  get plantName(): string {
    return this.workflowSnapshot?.workOrder?.plant || '-';
  }

  get siteName(): string {
    return this.workflowSnapshot?.workOrder?.site_name || this.traceResult?.device?.site || '-';
  }

  get childSummary(): string {
    const childCount = this.workflowSnapshot?.bom?.length || 0;
    return childCount === 1 ? '1 Child' : `${childCount} Childs`;
  }

  get snValueLabel(): string {
    return this.workflowSnapshot?.workOrder?.lot || this.serialNumber;
  }

  get assembledLabel(): string {
    const qty = this.workflowSnapshot?.workOrder?.qty;
    return qty && qty > 0 ? `Qty ${qty}` : this.traceResult?.serial?.status || '-';
  }

  get historyRows(): TraceHistoryRow[] {
    return this.traceResult?.history || [];
  }

  get flowNodes(): PreviewFlowNode[] {
    const routing = this.workflowSnapshot?.routing || [];
    const statuses = this.workflowSnapshot?.previewStatuses || {};
    const stationNodes: PreviewFlowNode[] = routing.length
      ? routing.map((step, index) => ({
          id: `station-${step.id || step.station_code}-${index}`,
          kind: 'station',
          station: {
            id: step.id || index + 1,
            station_order: step.station_order,
            station_code: step.station_code,
            station_name: step.station_name,
            sample_mode: step.sample_mode,
            report_mode: step.report_mode,
            icon: this.getStationIcon(index, step.station_name, step.station_code, step.sample_mode),
            status: statuses[step.station_code] || step.preview_status || (index === 0 ? 'In Progress' : 'Pending'),
          },
        }))
      : [
          {
            id: 'stations-pending',
            kind: 'empty',
            title: 'Stations Pending',
            icon: 'route',
          },
        ];

    return [
      { id: 'operator', kind: 'operator', title: 'Operator / Technician', icon: 'engineering' },
      ...stationNodes,
      { id: 'cart', kind: 'logistics', variant: 'cart', title: 'Cart', icon: 'shopping_cart' },
      { id: 'pallet', kind: 'logistics', variant: 'pallet', title: 'Pallet' },
      { id: 'truck', kind: 'logistics', variant: 'truck', title: 'Truck', subtitle: 'Dispatch / Shipping', icon: 'local_shipping' },
    ];
  }

  get flowRows(): PreviewFlowRow[] {
    const rows: PreviewFlowRow[] = [];
    const cardsPerRow = 6;

    for (let index = 0; index < this.flowNodes.length; index += cardsPerRow) {
      const rowIndex = rows.length;
      const nodes = this.flowNodes.slice(index, index + cardsPerRow);
      const isReversed = rowIndex % 2 === 1;
      rows.push({
        nodes: isReversed ? [...nodes].reverse() : nodes,
        isReversed,
        turnSide: isReversed ? 'left' : 'right',
      });
    }

    return rows;
  }

  formatHistoryResult(result: string): string {
    const normalized = (result || '').toUpperCase();
    if (normalized === 'PASS' || normalized === 'FAIL') {
      return normalized;
    }

    return result || '-';
  }

  getStatusClass(status: PreviewStatus): string {
    return status.toLowerCase().replace(/\s+/g, '-');
  }

  private loadSerial(serial: string): void {
    this.loading = true;
    this.previewLoading = true;
    this.errorMessage = '';
    this.previewMessage = '';
    this.historyMessage = '';
    this.traceResult = null;
    this.workflowSnapshot = null;

    this.traceabilityService.search(serial).subscribe({
      next: (result) => {
        this.traceResult = result;
        this.loading = false;
        this.historyMessage = result.history?.length ? '' : 'No SN history found for this serial number.';
        this.loadWorkflowPreview(result.device?.pn);
      },
      error: (error) => {
        this.loading = false;
        this.previewLoading = false;
        this.errorMessage = error?.error?.message || 'No preview data found for this serial number.';
        this.previewMessage = 'No preview data found for this serial number.';
        this.historyMessage = 'No SN history found for this serial number.';
      },
    });
  }

  private loadWorkflowPreview(partNumber: string | undefined): void {
    const pn = String(partNumber || '').trim();
    if (!pn) {
      this.previewLoading = false;
      this.previewMessage = 'No preview data found for this serial number.';
      return;
    }

    const params = new HttpParams().set('pn', pn);
    this.http.get<WorkflowSnapshot>(`${this.workflowApiUrl}/by-pn`, { params }).subscribe({
      next: (snapshot) => {
        this.workflowSnapshot = snapshot;
        this.previewLoading = false;
        this.previewMessage = snapshot?.routing?.length ? '' : 'No preview data found for this serial number.';
      },
      error: () => {
        this.workflowSnapshot = null;
        this.previewLoading = false;
        this.previewMessage = 'No preview data found for this serial number.';
      },
    });
  }

  private getStationIcon(index: number, name: string, code: string, sampleMode: string): string {
    const normalized = `${name} ${code}`.toLowerCase();
    if (sampleMode === 'Sample') {
      return 'saved_search';
    }

    if (normalized.includes('label')) {
      return 'qr_code_2';
    }

    if (normalized.includes('test') || normalized.includes('aoi')) {
      return 'biotech';
    }

    if (normalized.includes('pack') || normalized.includes('box')) {
      return 'inventory_2';
    }

    const icons = ['desktop_windows', 'verified_user', 'precision_manufacturing', 'memory', 'settings_applications'];
    return icons[index % icons.length];
  }
}
