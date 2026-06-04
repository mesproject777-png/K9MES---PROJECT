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

type ReportsView = 'menu' | 'standard' | 'activityQuality';
type ReportsTab = 'tree' | 'station';
type DateSelection = 'today' | 'yesterday' | 'thisWeek' | 'thisMonth' | 'custom';

interface ActivityQualityKpiSummary {
  totalPass: number;
  totalFail: number;
  totalTested: number;
  passRate: number;
  failRate: number;
  activeStations: number;
  activeWorkOrders: number;
  activeUsers: number;
}

interface ActivityQualityChartPoint {
  label: string;
  passCount: number;
  failCount: number;
  passRate: number;
}

interface ActivityQualityDetailRow {
  date_time: string;
  site: string;
  station: string;
  product_line: string;
  part_number: string;
  work_order: string;
  pc: string;
  user_name: string;
  serial_number: string;
  result: string;
  failure_reason: string;
}

interface ActivityQualityParetoPoint {
  symptom: string;
  count: number;
  percentage: number;
  cumulativePercentage: number;
}

interface ActivityQualityStationFailRate {
  station: string;
  failCount: number;
  totalCount: number;
  failRate: number;
}

interface ActivityQualityResponse {
  kpiSummary: ActivityQualityKpiSummary;
  chartData: ActivityQualityChartPoint[];
  detailedRows: ActivityQualityDetailRow[];
  symptomsPareto: ActivityQualityParetoPoint[];
  stationFailRates?: ActivityQualityStationFailRate[];
}

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

@Component({
  selector: 'app-reports',
  standalone: false,
  templateUrl: './reports.component.html',
  styleUrl: './reports.component.scss'
})
export class ReportsComponent implements OnInit, AfterViewInit, AfterViewChecked, OnDestroy {
  readonly reportsApi = `${environment.apiUrl}/api/reports/work-order-tree`;
  readonly activityQualityApi = `${environment.apiUrl}/api/reports/activity-quality`;

  activeView: ReportsView = 'menu';
  activeTab: ReportsTab = 'tree';
  currentWorkOrder = '';
  loading = false;
  errorMessage = '';
  report: WorkOrderTreeReport | null = null;
  selectedStation: WorkOrderTreeStation | null = null;
  activityLoading = false;
  activityErrorMessage = '';
  showExportMenu = false;
  showPassSeries = true;
  showFailSeries = true;
  selectedChartPoint: ActivityQualityChartPoint | null = null;
  rerunEnabled = false;
  fullScreenEnabled = false;
  rerunTimer: number | null = null;
  readonly dateSelections: { value: DateSelection; label: string }[] = [
    { value: 'today', label: 'Today' },
    { value: 'yesterday', label: 'Yesterday' },
    { value: 'thisWeek', label: 'This Week' },
    { value: 'thisMonth', label: 'This Month' },
    { value: 'custom', label: 'Custom' },
  ];
  readonly pivotOptions = [
    { value: 'perHour', label: 'Per hour', group: 'Time' },
    { value: 'perDay', label: 'Per day', group: 'Time' },
    { value: 'perWeek', label: 'Per week', group: 'Time' },
    { value: 'perMonth', label: 'Per month', group: 'Time' },
    { value: 'perSite', label: 'Per site', group: 'Physical' },
    { value: 'perPc', label: 'Per PC', group: 'Physical' },
    { value: 'perStation', label: 'Per station', group: 'Process' },
    { value: 'perProductLine', label: 'Per PL', group: 'Product' },
    { value: 'perPartNumber', label: 'Per PN', group: 'Product' },
    { value: 'perWorkOrder', label: 'Per WO', group: 'Product' },
    { value: 'perUser', label: 'Per user', group: 'Human' },
  ];
  activityFilters = {
    fromDate: '',
    toDate: '',
    dateSelection: 'today' as DateSelection,
    startHour: 0,
    site: '',
    station: '',
    productLine: '',
    partNumber: '',
    workOrder: '',
    pc: '',
    user: '',
    pivotBy: 'perHour',
    allSites: true,
    allStations: true,
    allProductLines: true,
    allPartNumbers: true,
    allWorkOrders: true,
    allPcs: true,
    allUsers: true,
  };
  activityData: ActivityQualityResponse = this.emptyActivityQualityResponse();
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
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.applyDateSelection();
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
    this.stopRerun();
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
  }

  openActivityQualityDashboard(): void {
    this.activeView = 'activityQuality';
    this.activityErrorMessage = '';
    if (this.activityData.chartData.length === 0 && !this.activityLoading) {
      this.loadActivityQualityDashboard();
    }
  }

  backToMenu(): void {
    this.activeView = 'menu';
    this.errorMessage = '';
    this.report = null;
    this.selectedStation = null;
    this.currentWorkOrder = '';
    this.showExportMenu = false;
    this.fullScreenEnabled = false;
    this.router.navigate(['/dashboard/reports']);
  }

  loadActivityQualityDashboard(): void {
    this.applyDateSelection();
    this.activityLoading = true;
    this.activityErrorMessage = '';
    this.selectedChartPoint = null;

    this.http.get<ActivityQualityResponse>(this.activityQualityApi, { params: this.buildActivityQualityParams() }).subscribe({
      next: (response) => {
        this.activityData = {
          ...this.emptyActivityQualityResponse(),
          ...response,
          kpiSummary: { ...this.emptyActivityQualityResponse().kpiSummary, ...(response.kpiSummary || {}) },
          chartData: response.chartData || [],
          detailedRows: response.detailedRows || [],
          symptomsPareto: response.symptomsPareto || [],
          stationFailRates: response.stationFailRates || [],
        };
        this.activityLoading = false;
      },
      error: (error) => {
        this.activityData = this.emptyActivityQualityResponse();
        this.activityLoading = false;
        this.activityErrorMessage = error?.error?.message || error?.error?.error || 'Unable to load Activity & Quality Dashboard.';
      }
    });
  }

  resetActivityFilters(): void {
    this.stopRerun();
    this.rerunEnabled = false;
    this.activityFilters = {
      ...this.activityFilters,
      dateSelection: 'today',
      startHour: 0,
      site: '',
      station: '',
      productLine: '',
      partNumber: '',
      workOrder: '',
      pc: '',
      user: '',
      pivotBy: 'perHour',
      allSites: true,
      allStations: true,
      allProductLines: true,
      allPartNumbers: true,
      allWorkOrders: true,
      allPcs: true,
      allUsers: true,
    };
    this.applyDateSelection();
    this.loadActivityQualityDashboard();
  }

  onActivityDateSelectionChange(): void {
    this.applyDateSelection();
  }

  get showStartHour(): boolean {
    return this.activityFilters.dateSelection === 'today' || this.activityFilters.dateSelection === 'yesterday';
  }

  onRerunToggle(): void {
    if (this.rerunEnabled) {
      this.loadActivityQualityDashboard();
      this.stopRerun();
      this.rerunTimer = window.setInterval(() => this.loadActivityQualityDashboard(), 5 * 60 * 1000);
      return;
    }

    this.stopRerun();
  }

  stopRerun(): void {
    if (this.rerunTimer !== null) {
      window.clearInterval(this.rerunTimer);
      this.rerunTimer = null;
    }
    this.rerunEnabled = false;
  }

  toggleFullScreen(): void {
    this.fullScreenEnabled = !this.fullScreenEnabled;
  }

  toggleSeries(series: 'pass' | 'fail'): void {
    if (series === 'pass') {
      this.showPassSeries = !this.showPassSeries;
      return;
    }

    this.showFailSeries = !this.showFailSeries;
  }

  selectChartPoint(point: ActivityQualityChartPoint): void {
    this.selectedChartPoint = point;
  }

  exportActivityData(): void {
    const headers = ['Date/Time', 'Site', 'Station', 'Product Line', 'Part Number', 'Work Order', 'PC', 'User', 'Serial Number', 'Result', 'Failure Reason'];
    const rows = this.activityData.detailedRows.map((row) => [
      row.date_time,
      row.site,
      row.station,
      row.product_line,
      row.part_number,
      row.work_order,
      row.pc,
      row.user_name,
      row.serial_number,
      row.result,
      row.failure_reason,
    ]);
    this.downloadText('activity-quality-dashboard.csv', [headers, ...rows].map((row) => row.map((cell) => `"${String(cell ?? '').replace(/"/g, '""')}"`).join(',')).join('\n'));
    this.showExportMenu = false;
  }

  exportChartImage(): void {
    const svg = document.querySelector('.aq-chart-svg');
    if (!(svg instanceof SVGElement)) {
      return;
    }

    const source = new XMLSerializer().serializeToString(svg);
    const blob = new Blob([source], { type: 'image/svg+xml;charset=utf-8' });
    this.downloadBlob('activity-quality-chart.svg', blob);
    this.showExportMenu = false;
  }

  viewUnderlyingData(): void {
    const table = document.querySelector('.aq-table-card');
    table?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    this.showExportMenu = false;
  }

  get maxChartCount(): number {
    return Math.max(1, ...this.activityData.chartData.map((item) => Math.max(item.passCount, item.failCount)));
  }

  chartBarHeight(value: number): number {
    return Math.max(value > 0 ? 8 : 0, Math.round((value / this.maxChartCount) * 170));
  }

  get yieldPolyline(): string {
    const points = this.activityData.chartData;
    if (points.length === 0) {
      return '';
    }

    const width = 1000;
    const height = 180;
    const step = points.length === 1 ? 0 : width / (points.length - 1);
    return points
      .map((point, index) => {
        const x = points.length === 1 ? width / 2 : index * step;
        const y = height - ((Math.max(0, Math.min(100, point.passRate)) / 100) * height);
        return `${x},${y}`;
      })
      .join(' ');
  }

  get filteredDetailRows(): ActivityQualityDetailRow[] {
    if (!this.selectedChartPoint) {
      return this.activityData.detailedRows;
    }

    return this.activityData.detailedRows.filter((row) => this.rowMatchesSelectedPoint(row));
  }

  optionValues(field: keyof ActivityQualityDetailRow): string[] {
    return Array.from(new Set(this.activityData.detailedRows.map((row) => String(row[field] || '').trim()).filter(Boolean))).sort();
  }

  trackByChartPoint(index: number, point: ActivityQualityChartPoint): string {
    return `${index}-${point.label}`;
  }

  trackByActivityRow(index: number, row: ActivityQualityDetailRow): string {
    return `${index}-${row.serial_number}-${row.date_time}`;
  }

  trackByPareto(index: number, row: ActivityQualityParetoPoint): string {
    return `${index}-${row.symptom}`;
  }

  trackByStationFail(index: number, row: ActivityQualityStationFailRate): string {
    return `${index}-${row.station}`;
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

  private buildActivityQualityParams(): HttpParams {
    let params = new HttpParams()
      .set('fromDate', this.activityFilters.fromDate)
      .set('toDate', this.activityFilters.toDate)
      .set('pivotBy', this.activityFilters.pivotBy);

    if (this.showStartHour) {
      params = params.set('startHour', String(this.activityFilters.startHour || 0));
    }

    const filterMap = [
      { all: this.activityFilters.allSites, value: this.activityFilters.site, key: 'siteIds' },
      { all: this.activityFilters.allStations, value: this.activityFilters.station, key: 'stationIds' },
      { all: this.activityFilters.allProductLines, value: this.activityFilters.productLine, key: 'productLineIds' },
      { all: this.activityFilters.allPartNumbers, value: this.activityFilters.partNumber, key: 'partNumbers' },
      { all: this.activityFilters.allWorkOrders, value: this.activityFilters.workOrder, key: 'workOrders' },
      { all: this.activityFilters.allPcs, value: this.activityFilters.pc, key: 'pcIds' },
      { all: this.activityFilters.allUsers, value: this.activityFilters.user, key: 'userIds' },
    ];

    filterMap.forEach((filter) => {
      if (!filter.all && filter.value.trim()) {
        params = params.set(filter.key, filter.value.trim());
      }
    });

    return params;
  }

  private applyDateSelection(): void {
    const today = new Date();
    let from = new Date(today);
    let to = new Date(today);

    if (this.activityFilters.dateSelection === 'yesterday') {
      from = new Date(today);
      from.setDate(today.getDate() - 1);
      to = new Date(from);
    } else if (this.activityFilters.dateSelection === 'thisWeek') {
      const day = today.getDay() || 7;
      from = new Date(today);
      from.setDate(today.getDate() - day + 1);
    } else if (this.activityFilters.dateSelection === 'thisMonth') {
      from = new Date(today.getFullYear(), today.getMonth(), 1);
    } else if (this.activityFilters.dateSelection === 'custom') {
      return;
    }

    this.activityFilters.fromDate = this.formatInputDate(from);
    this.activityFilters.toDate = this.formatInputDate(to);
  }

  private formatInputDate(value: Date): string {
    const year = value.getFullYear();
    const month = String(value.getMonth() + 1).padStart(2, '0');
    const day = String(value.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  private rowMatchesSelectedPoint(row: ActivityQualityDetailRow): boolean {
    const label = this.selectedChartPoint?.label;
    if (!label) {
      return true;
    }

    const date = row.date_time ? new Date(row.date_time) : null;
    switch (this.activityFilters.pivotBy) {
      case 'perSite':
        return (row.site || 'Unassigned site') === label;
      case 'perPc':
        return (row.pc || 'Unassigned PC') === label;
      case 'perStation':
        return (row.station || 'Unassigned station') === label;
      case 'perProductLine':
        return (row.product_line || 'Unassigned PL') === label;
      case 'perPartNumber':
        return (row.part_number || 'Unassigned PN') === label;
      case 'perWorkOrder':
        return (row.work_order || 'Unassigned WO') === label;
      case 'perUser':
        return (row.user_name || 'Unassigned user') === label;
      case 'perDay':
        return date ? this.formatInputDate(date) === label : false;
      case 'perMonth':
        return date ? label === `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}` : false;
      default:
        return date ? `${this.formatInputDate(date)} ${String(date.getHours()).padStart(2, '0')}:00` === label : false;
    }
  }

  private emptyActivityQualityResponse(): ActivityQualityResponse {
    return {
      kpiSummary: {
        totalPass: 0,
        totalFail: 0,
        totalTested: 0,
        passRate: 0,
        failRate: 0,
        activeStations: 0,
        activeWorkOrders: 0,
        activeUsers: 0,
      },
      chartData: [],
      detailedRows: [],
      symptomsPareto: [],
      stationFailRates: [],
    };
  }

  private downloadText(fileName: string, content: string): void {
    this.downloadBlob(fileName, new Blob([content], { type: 'text/csv;charset=utf-8;' }));
  }

  private downloadBlob(fileName: string, blob: Blob): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(url);
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
