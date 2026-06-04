import { HttpClient, HttpParams } from '@angular/common/http';
import {
  AfterViewChecked,
  AfterViewInit,
  ChangeDetectorRef,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  QueryList,
  ViewChild,
  ViewChildren,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../services/auth.service';

type ReportsView = 'menu' | 'standard' | 'scrapSn' | 'undoScrap';
type ReportsTab = 'tree' | 'station';

type ReportFlowNode = {
  id: string;
  kind: 'operator' | 'station' | 'logistics';
  title?: string;
  icon?: string;
  variant?: 'cart' | 'pallet' | 'truck';
  station?: WorkOrderTreeStation & { icon: string; colorClass: string };
};

type ReportFlowRow = {
  nodes: ReportFlowNode[];
  isReversed: boolean;
  turnSide: 'left' | 'right';
};

interface WorkOrderTreeSerial {
  id: number;
  sn: string;
  rsn: string;
  status: string;
  condition: string;
  current_station_code: string;
  current_station_order: number;
  created_at: string;
  updated_at: string;
  last_moved_at: string | null;
}

interface WorkOrderTreeStation {
  id: number;
  station_order: number;
  station_code: string;
  station_name: string;
  sample_mode: string;
  report_mode: string;
  sn_count: number;
  is_highest_count: boolean;
  serials: WorkOrderTreeSerial[];
}

interface WorkOrderTreeReport {
  workOrder: {
    id: number;
    wo: string;
    qty: number | null;
    status: string;
    plant?: string | null;
    site_name?: string | null;
    revision?: string | null;
    lot?: string | null;
  };
  partNumber: {
    id: number;
    pn: string;
    description: string;
    item_type?: string | null;
  };
  summary: {
    total_serials: number;
    completed: number;
    failed: number;
    in_stations: number;
    highest_station_count: number;
  };
  stations: WorkOrderTreeStation[];
}

interface ScrapActionResponse {
  message: string;
  action: 'SCRAP' | 'UNDO_SCRAP';
  data: {
    serial: {
      sn: string;
      rsn: string;
      status: string;
      condition: string;
      current_station_code: string | null;
      current_station_name: string | null;
      updated_at: string;
      last_moved_at: string | null;
    };
    device: {
      pn: string;
      revision: string;
      work_order: string;
      description: string;
    };
  };
}

@Component({
  selector: 'app-reports',
  standalone: false,
  templateUrl: './reports.component.html',
  styleUrl: './reports.component.scss'
})
export class ReportsComponent implements OnInit, AfterViewInit, AfterViewChecked, OnDestroy {
  readonly reportsApi = `${environment.apiUrl}/api/reports/work-order-tree`;
  readonly scrapSnApi = `${environment.apiUrl}/api/reports/scrap-sn`;
  readonly undoScrapApi = `${environment.apiUrl}/api/reports/undo-scrap`;

  activeView: ReportsView = 'menu';
  activeTab: ReportsTab = 'tree';
  currentWorkOrder = '';
  loading = false;
  errorMessage = '';
  successMessage = '';
  scrapQuery = '';
  scrapReason = '';
  scrapLoading = false;
  scrapResult: ScrapActionResponse | null = null;
  report: WorkOrderTreeReport | null = null;
  selectedStation: WorkOrderTreeStation | null = null;
  flowCardsPerRow = this.getFlowCardsPerRow();
  connectorPath = '';
  connectorWidth = 0;
  connectorHeight = 0;
  private connectorFrame: number | null = null;
  private lastConnectorSignature = '';
  private routeSubscription?: Subscription;
  @ViewChild('reportProcessFlow') private reportProcessFlowRef?: ElementRef<HTMLElement>;
  @ViewChildren('reportFlowNode') private reportFlowNodeRefs?: QueryList<ElementRef<HTMLElement>>;

  constructor(
    private http: HttpClient,
    private router: Router,
    private route: ActivatedRoute,
    private authService: AuthService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.routeSubscription = this.route.queryParamMap.subscribe((params) => {
      const wo = String(params.get('wo') || params.get('q') || '').trim();
      if (!wo) {
        return;
      }

      this.activeView = 'standard';
      this.loadWorkOrderReport(wo);
    });
  }

  ngOnDestroy(): void {
    this.routeSubscription?.unsubscribe();
    if (this.connectorFrame !== null) {
      window.cancelAnimationFrame(this.connectorFrame);
    }
  }

  ngAfterViewInit(): void {
    this.reportFlowNodeRefs?.changes.subscribe(() => this.queueConnectorRefresh());
    this.queueConnectorRefresh();
  }

  ngAfterViewChecked(): void {
    const signature = this.buildConnectorSignature();
    if (signature !== this.lastConnectorSignature) {
      this.lastConnectorSignature = signature;
      this.queueConnectorRefresh();
    }
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    this.flowCardsPerRow = this.getFlowCardsPerRow();
    this.queueConnectorRefresh();
  }

  openStandardReports(): void {
    this.activeView = 'standard';
    this.activeTab = 'tree';
    this.errorMessage = '';
    this.successMessage = '';
  }

  openScrapSnReports(): void {
    this.openReportsModule('scrapSn');
  }

  openUndoScrapReports(): void {
    this.openReportsModule('undoScrap');
  }

  backToMenu(): void {
    this.activeView = 'menu';
    this.errorMessage = '';
    this.successMessage = '';
    this.report = null;
    this.selectedStation = null;
    this.currentWorkOrder = '';
    this.resetScrapForm();
    this.router.navigate(['/dashboard/reports']);
  }

  get activeModuleTitle(): string {
    if (this.activeView === 'scrapSn') {
      return 'Scrap SN';
    }

    if (this.activeView === 'undoScrap') {
      return 'Undo Scrap';
    }

    return 'Reports';
  }

  get activeModuleIcon(): string {
    return this.activeView === 'undoScrap' ? 'undo' : 'delete_sweep';
  }

  get activeModulePrompt(): string {
    if (this.activeView === 'scrapSn') {
      return 'Enter serial number in search bar';
    }

    if (this.activeView === 'undoScrap') {
      return 'Enter scrapped serial number in search bar';
    }

    return '';
  }

  get activeModuleButtonLabel(): string {
    return this.activeView === 'undoScrap' ? 'Undo Scrap' : 'Scrap SN';
  }

  get activeModuleHelp(): string {
    if (this.activeView === 'scrapSn') {
      return 'Scrap marks a damaged or beyond-repair SN as SCRAP, blocks production actions, and saves the action in SN History.';
    }

    if (this.activeView === 'undoScrap') {
      return 'Undo Scrap removes the SCRAP mark, restores the previous New condition, and saves the action in SN History.';
    }

    return '';
  }

  loadWorkOrderReport(wo: string): void {
    const workOrder = wo.trim();
    this.errorMessage = '';

    if (!workOrder) {
      this.errorMessage = 'Enter work order number in search bar.';
      return;
    }

    this.currentWorkOrder = workOrder;
    this.loading = true;
    const params = new HttpParams().set('wo', workOrder);

    this.http.get<WorkOrderTreeReport>(this.reportsApi, { params }).subscribe({
      next: (report) => {
        this.report = report;
        this.currentWorkOrder = report.workOrder?.wo || workOrder;
        this.selectedStation = null;
        this.activeTab = 'tree';
        this.loading = false;
        this.queueConnectorRefresh();
      },
      error: (error) => {
        this.report = null;
        this.selectedStation = null;
        this.loading = false;
        this.errorMessage = error?.error?.message || 'Unable to load work order report.';
        this.setConnector('', 0, 0);
      }
    });
  }

  private openReportsModule(view: Extract<ReportsView, 'scrapSn' | 'undoScrap'>): void {
    this.activeView = view;
    this.errorMessage = '';
    this.successMessage = '';
    this.report = null;
    this.selectedStation = null;
    this.currentWorkOrder = '';
    this.resetScrapForm();
    this.setConnector('', 0, 0);
  }

  private resetScrapForm(): void {
    this.scrapQuery = '';
    this.scrapReason = '';
    this.scrapLoading = false;
    this.scrapResult = null;
  }

  selectStation(station: WorkOrderTreeStation): void {
    this.activateStation(station);
  }

  activateStation(station: WorkOrderTreeStation | null | undefined, event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();

    if (!station) {
      return;
    }

    this.selectedStation = {
      ...station,
      serials: station.serials || [],
    };
    this.activeTab = 'station';
    this.cdr.detectChanges();
  }

  openSerial(serial: WorkOrderTreeSerial): void {
    const query = String(serial.sn || serial.rsn || '').trim();
    if (!query) {
      return;
    }

    this.router.navigate(['/dashboard/sn-result'], {
      queryParams: { q: query, t: Date.now() }
    });
  }

  openScrapResultHistory(): void {
    const query = this.scrapResult?.data?.serial?.sn || this.scrapQuery.trim();
    if (!query) {
      return;
    }

    this.router.navigate(['/dashboard/sn-result'], {
      queryParams: { q: query, t: Date.now() },
    });
  }

  submitScrapModule(): void {
    const query = this.scrapQuery.trim();
    const reason = this.scrapReason.trim();
    this.errorMessage = '';
    this.successMessage = '';
    this.scrapResult = null;

    if (!query) {
      this.errorMessage = 'SN or RSN is required.';
      return;
    }

    if (!reason) {
      this.errorMessage = `${this.activeModuleButtonLabel} reason is required.`;
      return;
    }

    if (this.activeView !== 'scrapSn' && this.activeView !== 'undoScrap') {
      return;
    }

    const user = this.authService.getCurrentUser();
    const payload = {
      query,
      reason,
      changed_by: user?.user_name || user?.login_id || 'WEB-CLIENT',
      pc_name: 'WEB-CLIENT',
    };
    const endpoint = this.activeView === 'undoScrap' ? this.undoScrapApi : this.scrapSnApi;

    this.scrapLoading = true;
    this.http.post<ScrapActionResponse>(endpoint, payload).subscribe({
      next: (response) => {
        this.scrapLoading = false;
        this.scrapResult = response;
        this.successMessage = response.message;
        this.scrapReason = '';
      },
      error: (error) => {
        this.scrapLoading = false;
        this.errorMessage = error?.error?.message || error?.error?.error || `Unable to complete ${this.activeModuleButtonLabel}.`;
      }
    });
  }

  getStationIcon(index: number, station: WorkOrderTreeStation): string {
    const normalized = `${station.station_code} ${station.station_name}`.toLowerCase();

    if (station.sample_mode === 'Sample') {
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

  getStationColorClass(index: number): string {
    return `station-color-${index % 24}`;
  }

  get selectedStationTitle(): string {
    if (!this.selectedStation) {
      return "SN's in Station";
    }

    return `SN's in ${this.selectedStation.station_code}`;
  }

  get workOrderTabLabel(): string {
    return this.report?.workOrder?.wo ? `Work Order Tree - ${this.report.workOrder.wo}` : 'Work Order Tree';
  }

  get stationTabLabel(): string {
    return this.selectedStation ? `SN's in ${this.selectedStation.station_code}` : "SN's in Station";
  }

  get flowNodes(): ReportFlowNode[] {
    const stationNodes: ReportFlowNode[] = (this.report?.stations || []).map((station, index) => ({
      id: `station-${station.id}-${station.station_code}-${index}`,
      kind: 'station',
      station: {
        ...station,
        icon: this.getStationIcon(index, station),
        colorClass: this.getStationColorClass(index),
      },
    }));

    return [
      { id: 'operator', kind: 'operator', title: 'Operator / Technician', icon: 'engineering' },
      ...stationNodes,
      { id: 'cart', kind: 'logistics', variant: 'cart', title: 'Cart', icon: 'shopping_cart' },
      { id: 'pallet', kind: 'logistics', variant: 'pallet', title: 'Pallet', icon: 'inventory_2' },
      { id: 'truck', kind: 'logistics', variant: 'truck', title: 'Truck', icon: 'local_shipping' },
    ];
  }

  get flowRows(): ReportFlowRow[] {
    const rows: ReportFlowRow[] = [];
    const cardsPerRow = Math.max(2, this.flowCardsPerRow);

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

  isHighSnStation(station: WorkOrderTreeStation | null | undefined): boolean {
    const count = Number(station?.sn_count) || 0;
    const highest = Math.max(0, ...(this.report?.stations || []).map((item) => Number(item.sn_count) || 0));
    return count > 0 && count === highest;
  }

  trackByStation(index: number, station: WorkOrderTreeStation): string {
    return `${index}-${station.id}-${station.station_code}`;
  }

  trackBySerial(index: number, serial: WorkOrderTreeSerial): string {
    return `${index}-${serial.id}-${serial.sn}`;
  }

  trackByFlowNode(index: number, node: ReportFlowNode): string {
    return `${index}-${node.id}`;
  }

  private getFlowCardsPerRow(): number {
    const width = Math.max(360, window.innerWidth - 300);
    const availableWidth = Math.max(320, width - 68);
    const estimatedCardWidth = width >= 1240 ? 144 : 156;
    const estimatedLineWidth = width >= 1240 ? 30 : 36;
    const estimatedCards = Math.floor(
      (availableWidth + estimatedLineWidth) / (estimatedCardWidth + estimatedLineWidth)
    );

    return Math.max(2, Math.min(8, estimatedCards));
  }

  private buildConnectorSignature(): string {
    const ids = this.flowNodes.map((node) => node.id).join('|');
    const counts = (this.report?.stations || []).map((station) => `${station.id}:${station.sn_count}`).join('|');
    return `${this.activeView}:${this.activeTab}:${this.flowCardsPerRow}:${ids}:${counts}`;
  }

  private queueConnectorRefresh(): void {
    if (typeof window === 'undefined') {
      return;
    }

    if (this.connectorFrame !== null) {
      window.cancelAnimationFrame(this.connectorFrame);
    }

    this.connectorFrame = window.requestAnimationFrame(() => {
      this.connectorFrame = null;
      this.updateConnectorPath();
    });
  }

  private updateConnectorPath(): void {
    const container = this.reportProcessFlowRef?.nativeElement;
    const nodeRefs = this.reportFlowNodeRefs?.toArray() || [];

    if (!container || this.activeTab !== 'tree' || nodeRefs.length < 2) {
      this.setConnector('', 0, 0);
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
      this.setConnector('', 0, 0);
      return;
    }

    const pathSegments: string[] = [];
    for (let index = 0; index < orderedRects.length - 1; index += 1) {
      const currentRect = orderedRects[index];
      const nextRect = orderedRects[index + 1];
      const currentCenter = this.getRelativeCenter(currentRect, containerRect);
      const nextCenter = this.getRelativeCenter(nextRect, containerRect);
      const sameRow = Math.abs(currentCenter.y - nextCenter.y) < 28;

      pathSegments.push(sameRow
        ? this.buildSameRowConnector(currentRect, nextRect, containerRect)
        : this.buildRowTurnConnector(currentRect, nextRect, containerRect));
    }

    this.setConnector(pathSegments.join(' '), containerRect.width, containerRect.height);
  }

  private buildSameRowConnector(currentRect: DOMRect, nextRect: DOMRect, containerRect: DOMRect): string {
    const currentCenter = this.getRelativeCenter(currentRect, containerRect);
    const nextCenter = this.getRelativeCenter(nextRect, containerRect);
    const flowsRight = nextCenter.x >= currentCenter.x;
    const startX = flowsRight ? currentRect.right - containerRect.left : currentRect.left - containerRect.left;
    const endX = flowsRight ? nextRect.left - containerRect.left : nextRect.right - containerRect.left;
    const y = (currentCenter.y + nextCenter.y) / 2;

    return `M ${startX} ${y} L ${endX} ${y}`;
  }

  private buildRowTurnConnector(currentRect: DOMRect, nextRect: DOMRect, containerRect: DOMRect): string {
    const currentCenter = this.getRelativeCenter(currentRect, containerRect);
    const nextCenter = this.getRelativeCenter(nextRect, containerRect);
    const flowsDown = nextCenter.y >= currentCenter.y;
    const startX = currentCenter.x;
    const startY = flowsDown ? currentRect.bottom - containerRect.top : currentRect.top - containerRect.top;
    const endX = nextCenter.x;
    const endY = flowsDown ? nextRect.top - containerRect.top : nextRect.bottom - containerRect.top;
    const midY = startY + ((endY - startY) / 2);

    if (Math.abs(startX - endX) < 4) {
      return `M ${startX} ${startY} L ${endX} ${endY}`;
    }

    return `M ${startX} ${startY} L ${startX} ${midY} L ${endX} ${midY} L ${endX} ${endY}`;
  }

  private getRelativeCenter(rect: DOMRect, containerRect: DOMRect): { x: number; y: number } {
    return {
      x: rect.left - containerRect.left + (rect.width / 2),
      y: rect.top - containerRect.top + (rect.height / 2),
    };
  }

  private setConnector(path: string, width: number, height: number): void {
    if (this.connectorPath === path && this.connectorWidth === width && this.connectorHeight === height) {
      return;
    }

    this.connectorPath = path;
    this.connectorWidth = width;
    this.connectorHeight = height;
    this.cdr.detectChanges();
  }
}
