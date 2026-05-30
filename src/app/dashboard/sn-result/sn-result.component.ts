import { HttpClient, HttpParams } from '@angular/common/http';
import {
  AfterViewChecked,
  AfterViewInit,
  ChangeDetectorRef,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  QueryList,
  ViewChild,
  ViewChildren,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Location } from '@angular/common';
import { Subscription } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  TraceabilityService,
  TraceHistoryRow,
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
    son_description?: string;
    station_code: string;
    station_name: string;
    item_type?: string;
    pn_type?: string;
    qty: number;
  }>;
  stationRules?: Record<string, string[]>;
  previewStatuses?: Record<string, PreviewStatus>;
};

type PreviewStationNode = {
  id: number;
  station_order: number;
  station_code: string;
  station_name: string;
  sample_mode: string;
  report_mode: string;
  icon: string;
  status: PreviewStatus;
};

type PreviewFlowNode = {
  id: string;
  kind: 'operator' | 'station' | 'empty' | 'logistics';
  title?: string;
  icon?: string;
  subtitle?: string;
  variant?: 'cart' | 'pallet' | 'truck';
  station?: PreviewStationNode;
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
export class SnResultComponent implements AfterViewInit, AfterViewChecked, OnDestroy {
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
  previewConnectorPath = '';
  previewConnectorWidth = 0;
  previewConnectorHeight = 0;
  previewFlowCardsPerRow = this.getPreviewFlowCardsPerRow();
  isChildDetailsOpen = false;
  activePreviewStation: PreviewStationNode | null = null;
  previewStationStatusById: Record<number, PreviewStatus> = {};

  private readonly workflowApiUrl = `${environment.apiUrl}/api/workflow`;
  private routeSub: Subscription | null = null;
  private previewConnectorFrame: number | null = null;
  private previewConnectorSignature = '';
  @ViewChild('previewProcessFlow') private previewProcessFlowRef?: ElementRef<HTMLElement>;
  @ViewChildren('previewFlowNode') private previewFlowNodeRefs?: QueryList<ElementRef<HTMLElement>>;

  constructor(
    private http: HttpClient,
    private traceabilityService: TraceabilityService,
    private route: ActivatedRoute,
    private router: Router,
    private location: Location,
    private cdr: ChangeDetectorRef
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

  @HostListener('window:resize')
  onWindowResize(): void {
    this.previewFlowCardsPerRow = this.getPreviewFlowCardsPerRow();
    this.queuePreviewConnectorRefresh();
  }

  ngAfterViewInit(): void {
    this.previewFlowNodeRefs?.changes.subscribe(() => this.queuePreviewConnectorRefresh());
    this.queuePreviewConnectorRefresh();
  }

  ngAfterViewChecked(): void {
    const signature = this.buildPreviewConnectorSignature();

    if (signature !== this.previewConnectorSignature) {
      this.previewConnectorSignature = signature;
      this.queuePreviewConnectorRefresh();
    }
  }

  ngOnDestroy(): void {
    this.routeSub?.unsubscribe();
    if (this.previewConnectorFrame) {
      window.cancelAnimationFrame(this.previewConnectorFrame);
    }
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

  getSnResultTabLabel(tabId: SnResultTab): string {
    const rsnNumber = this.traceResult?.serial?.rsn || this.serialNumber;

    return tabId === 'preview'
      ? `SN Chart - ${rsnNumber}`
      : `SN History - ${rsnNumber}`;
  }

  get serialNumber(): string {
    return this.traceResult?.serial?.sn || this.query || '-';
  }

  get workOrderNumber(): string {
    return this.traceResult?.device?.work_order || this.workflowSnapshot?.workOrder?.wo || '-';
  }

  get partNumber(): string {
    return this.workflowSnapshot?.partNumber?.pn || this.traceResult?.device?.pn || '-';
  }

  get plantName(): string {
    return this.workflowSnapshot?.workOrder?.plant || this.traceResult?.device?.plant || 'Select Plant';
  }

  get siteName(): string {
    return this.workflowSnapshot?.workOrder?.site_name || this.traceResult?.device?.site || 'Select Site';
  }

  get childSummary(): string {
    const childCount = this.workflowSnapshot?.bom?.length || 0;
    return childCount === 1 ? '1 Child' : `${childCount} Childs`;
  }

  get parentPartNumber(): string {
    const partNumber = this.partNumber;
    const revision = String(this.workflowSnapshot?.workOrder?.revision || this.traceResult?.device?.revision || '').trim();

    if (!revision || revision === '-' || !partNumber || partNumber === '-') {
      return partNumber || '-';
    }

    const revisionSuffix = `-${revision}`;
    return partNumber.endsWith(revisionSuffix)
      ? partNumber.slice(0, -revisionSuffix.length)
      : partNumber;
  }

  get boxLeftLabel(): string {
    return this.workflowSnapshot?.workOrder?.lot || this.serialNumber;
  }

  get boxRightLabel(): string {
    const qty = this.workflowSnapshot?.workOrder?.qty;
    return qty && qty > 0 ? `Qty ${qty}` : this.traceResult?.serial?.status || '-';
  }

  get historyRows(): TraceHistoryRow[] {
    return this.traceResult?.history || [];
  }

  get bomChildren(): Array<NonNullable<WorkflowSnapshot['bom']>[number]> {
    return this.workflowSnapshot?.bom || [];
  }

  get activePreviewStationRules(): string[] {
    if (!this.activePreviewStation) {
      return [];
    }

    return this.workflowSnapshot?.stationRules?.[this.activePreviewStation.station_code] || [];
  }

  get previewStationStartTime(): string {
    return new Date().toLocaleString([], {
      year: 'numeric',
      month: 'short',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
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
            status: this.previewStationStatusById[step.id] || statuses[step.station_code] || step.preview_status || (index === 0 ? 'In Progress' : 'Pending'),
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
      { id: 'pallet', kind: 'logistics', variant: 'pallet', title: 'Pallet', icon: 'inventory_2' },
      { id: 'truck', kind: 'logistics', variant: 'truck', title: 'Truck', subtitle: 'Dispatch / Shipping', icon: 'local_shipping' },
    ];
  }

  get flowRows(): PreviewFlowRow[] {
    const rows: PreviewFlowRow[] = [];
    const cardsPerRow = Math.max(2, this.previewFlowCardsPerRow);

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

  openChildDetails(): void {
    this.isChildDetailsOpen = true;
  }

  closeChildDetails(): void {
    this.isChildDetailsOpen = false;
  }

  openPreviewStationDetails(event: Event, station?: PreviewStationNode | null): void {
    event.preventDefault();
    event.stopPropagation();

    if (!station) {
      return;
    }

    this.activePreviewStation = station;
  }

  closePreviewStationDetails(): void {
    this.activePreviewStation = null;
  }

  pausePreviewStation(): void {
    this.setPreviewStationStatus('Paused');
  }

  completePreviewStation(): void {
    this.setPreviewStationStatus('Completed');
  }

  private loadSerial(serial: string): void {
    this.loading = true;
    this.previewLoading = true;
    this.errorMessage = '';
    this.previewMessage = '';
    this.historyMessage = '';
    this.traceResult = null;
    this.workflowSnapshot = null;
    this.previewStationStatusById = {};
    this.isChildDetailsOpen = false;
    this.activePreviewStation = null;

    this.traceabilityService.search(serial).subscribe({
      next: (result) => {
        this.traceResult = result;
        this.loading = false;
        this.historyMessage = result.history?.length ? '' : 'No SN history found for this serial number.';
        this.loadWorkflowPreview(result.device?.pn, result.device?.work_order);
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

  private loadWorkflowPreview(partNumber: string | undefined, workOrder: string | undefined): void {
    const pn = String(partNumber || '').trim();
    if (!pn) {
      this.previewLoading = false;
      this.previewMessage = 'No preview data found for this serial number.';
      return;
    }

    let params = new HttpParams().set('pn', pn);
    const wo = String(workOrder || '').trim();
    if (wo) {
      params = params.set('wo', wo);
    }
    this.http.get<WorkflowSnapshot>(`${this.workflowApiUrl}/by-pn`, { params }).subscribe({
      next: (snapshot) => {
        this.workflowSnapshot = snapshot;
        this.previewLoading = false;
        this.previewMessage = snapshot?.routing?.length ? '' : 'No preview data found for this serial number.';
        this.queuePreviewConnectorRefresh();
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

  private setPreviewStationStatus(status: PreviewStatus): void {
    if (!this.activePreviewStation) {
      return;
    }

    this.previewStationStatusById = {
      ...this.previewStationStatusById,
      [this.activePreviewStation.id]: status,
    };
    this.activePreviewStation = {
      ...this.activePreviewStation,
      status,
    };
    this.queuePreviewConnectorRefresh();
    this.closePreviewStationDetails();
  }

  private buildPreviewConnectorSignature(): string {
    const flowIds = this.flowNodes.map((node) => node.id).join('|');
    const routeSignature = (this.workflowSnapshot?.routing || [])
      .map((step) => `${step.id}:${step.station_code}:${step.sample_mode}`)
      .join('|');

    return `${this.activeTab}:${this.previewFlowCardsPerRow}:${flowIds}:${routeSignature}:${this.previewLoading}`;
  }

  private queuePreviewConnectorRefresh(): void {
    if (typeof window === 'undefined') {
      return;
    }

    if (this.previewConnectorFrame) {
      window.cancelAnimationFrame(this.previewConnectorFrame);
    }

    this.previewConnectorFrame = window.requestAnimationFrame(() => {
      this.previewConnectorFrame = null;
      this.updatePreviewConnectorPath();
    });
  }

  private updatePreviewConnectorPath(): void {
    const container = this.previewProcessFlowRef?.nativeElement;
    const nodeRefs = this.previewFlowNodeRefs?.toArray() || [];

    if (!container || this.activeTab !== 'preview' || this.previewLoading || nodeRefs.length < 2) {
      this.setPreviewConnector('', 0, 0);
      return;
    }

    const containerRect = container.getBoundingClientRect();
    const nodeRectsById = new Map<string, DOMRect>();

    nodeRefs.forEach((nodeRef) => {
      const flowId = nodeRef.nativeElement.dataset['flowId'];

      if (flowId) {
        nodeRectsById.set(flowId, nodeRef.nativeElement.getBoundingClientRect());
      }
    });

    const orderedRects = this.flowNodes
      .map((node) => nodeRectsById.get(node.id))
      .filter((rect): rect is DOMRect => Boolean(rect));

    if (orderedRects.length < 2) {
      this.setPreviewConnector('', 0, 0);
      return;
    }

    const pathSegments: string[] = [];

    for (let index = 0; index < orderedRects.length - 1; index += 1) {
      const currentRect = orderedRects[index];
      const nextRect = orderedRects[index + 1];
      const currentCenter = this.getRelativeRectCenter(currentRect, containerRect);
      const nextCenter = this.getRelativeRectCenter(nextRect, containerRect);
      const sameRow = Math.abs(currentCenter.y - nextCenter.y) < 28;

      if (sameRow) {
        const flowsRight = nextCenter.x >= currentCenter.x;
        const startX = flowsRight ? currentRect.right - containerRect.left : currentRect.left - containerRect.left;
        const endX = flowsRight ? nextRect.left - containerRect.left : nextRect.right - containerRect.left;
        const y = (currentCenter.y + nextCenter.y) / 2;
        pathSegments.push(`M ${startX} ${y} L ${endX} ${y}`);
      } else {
        const flowsDown = nextCenter.y >= currentCenter.y;
        const startX = currentCenter.x;
        const startY = flowsDown ? currentRect.bottom - containerRect.top : currentRect.top - containerRect.top;
        const endX = nextCenter.x;
        const endY = flowsDown ? nextRect.top - containerRect.top : nextRect.bottom - containerRect.top;
        const midY = startY + ((endY - startY) / 2);
        pathSegments.push(`M ${startX} ${startY} L ${startX} ${midY} L ${endX} ${midY} L ${endX} ${endY}`);
      }
    }

    this.setPreviewConnector(pathSegments.join(' '), containerRect.width, containerRect.height);
  }

  private getRelativeRectCenter(rect: DOMRect, containerRect: DOMRect): { x: number; y: number } {
    return {
      x: rect.left - containerRect.left + (rect.width / 2),
      y: rect.top - containerRect.top + (rect.height / 2),
    };
  }

  private setPreviewConnector(path: string, width: number, height: number): void {
    if (
      this.previewConnectorPath === path &&
      this.previewConnectorWidth === width &&
      this.previewConnectorHeight === height
    ) {
      return;
    }

    this.previewConnectorPath = path;
    this.previewConnectorWidth = width;
    this.previewConnectorHeight = height;
    this.cdr.detectChanges();
  }

  private getPreviewFlowCardsPerRow(): number {
    const containerWidth = this.previewProcessFlowRef?.nativeElement.clientWidth || 0;
    const width = containerWidth || (typeof window === 'undefined' ? 1140 : Math.max(360, window.innerWidth - 300));
    const availablePreviewWidth = Math.max(320, width - 24);
    const estimatedCardWidth = width >= 1240 ? 144 : 156;
    const estimatedLineWidth = width >= 1240 ? 30 : 36;
    const estimatedCards = Math.floor(
      (availablePreviewWidth + estimatedLineWidth) / (estimatedCardWidth + estimatedLineWidth)
    );

    return Math.max(2, Math.min(8, estimatedCards));
  }
}
