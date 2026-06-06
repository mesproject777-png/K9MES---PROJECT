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

type ReportsView = 'menu' | 'standard' | 'scrapSn' | 'undoScrap' | 'activityQuality' | 'todays';
type ReportsTab = 'tree' | 'station';
type DateSelection = 'today' | 'yesterday' | 'thisWeek' | 'thisMonth' | 'custom';
type TodaysDashboardTab = 'filters' | 'overview';
type TodaysDropdownKey = 'dateSelection' | 'site' | 'station' | 'partNumber' | 'workOrder' | 'pc' | 'user';
const TODAYS_ALL_VALUE = '__all__';

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

interface ReportSiteOption {
  id: number;
  name: string;
}

interface ReportStationOption {
  station_code: string;
  station_name?: string | null;
}

interface ReportPartNumberOption {
  id: number;
  pn: string;
  description?: string | null;
}

interface ReportWorkOrderOption {
  id?: number;
  wo: string;
  part_number?: string | null;
  site?: string | null;
}

interface TodaysDashboardOptions {
  sites: ReportSiteOption[];
  stations: ReportStationOption[];
  partNumbers: ReportPartNumberOption[];
  workOrders: ReportWorkOrderOption[];
}

interface TodaysDashboardSummary {
  totalSn: number;
  passCount: number;
  failCount: number;
  reworkCount: number;
  nffCount: number;
  pendingCount: number;
  fpy: number;
  wipCount: number;
  avgCycleTime: number;
  topFailingStation: string;
  topFailingStationFails: number;
  highestLoadStation: string;
  highestLoadStationSn: number;
}

interface TodaysSnDetail {
  sn: string;
  pn: string;
  wo: string;
  station: string;
  operator: string;
  status: 'Pass' | 'Fail' | 'Rework' | 'NFF' | 'Pending';
  startTime: string;
  endTime: string;
  cycleSeconds?: number;
  result: string;
}

interface TodaysHourBucket {
  label: string;
  pass: number;
  fail: number;
  rework: number;
  nff: number;
  pending: number;
  sns: TodaysSnDetail[];
}

interface TodaysFpyPoint {
  label: string;
  fpy: number;
}

interface TodaysStationFailure {
  station: string;
  count: number;
}

interface TodaysMetricCard {
  label: string;
  value: string;
  subtext: string;
  trend: string;
  trendDirection: 'up' | 'down';
  icon: string;
  tone: 'blue' | 'green' | 'red' | 'purple' | 'amber' | 'cyan';
}

interface TodaysDropdownOption {
  value: string;
  label: string;
}

interface TodaysDashboardData {
  lastUpdated: string;
  summary: TodaysDashboardSummary;
  hourlyBuckets: TodaysHourBucket[];
  dailyBuckets: TodaysHourBucket[];
  dailyBucket: TodaysHourBucket;
  fpyTrend: TodaysFpyPoint[];
  failureByStation: TodaysStationFailure[];
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
  readonly activityQualityApi = `${environment.apiUrl}/api/reports/activity-quality`;
  readonly sitesApi = `${environment.apiUrl}/api/sites`;
  readonly stationsApi = `${environment.apiUrl}/api/stations`;
  readonly partNumbersApi = `${environment.apiUrl}/api/items`;
  readonly workOrdersApi = `${environment.apiUrl}/api/workflow/work-orders`;
  readonly todaysOptionsApi = `${environment.apiUrl}/api/reports/todays-dashboard/options`;
  readonly todaysDataApi = `${environment.apiUrl}/api/reports/todays-dashboard/data`;

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
  activityLoading = false;
  activityErrorMessage = '';
  showExportMenu = false;
  showPassSeries = true;
  showFailSeries = true;
  selectedChartPoint: ActivityQualityChartPoint | null = null;
  activityDetailsCurrentPage = 1;
  readonly activityDetailsPageSize = 10;
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
  todaysActiveTab: TodaysDashboardTab = 'filters';
  todaysLookupsLoaded = false;
  todaysLookupLoading = false;
  sites: ReportSiteOption[] = [];
  stations: ReportStationOption[] = [];
  partNumbers: ReportPartNumberOption[] = [];
  workOrders: ReportWorkOrderOption[] = [];
  readonly plantOptions = ['Tirupati', 'Bangalore', 'Hyderabad', 'Chennai', 'Pune', 'Mumbai'];
  readonly fallbackSitesByPlant: Record<string, string[]> = {
    Tirupati: ['Tirupati Main Site', 'Tirupati Assembly Site', 'Tirupati Quality Site'],
    Bangalore: ['Bangalore Main Site', 'Bangalore Assembly Site', 'Bangalore Quality Site'],
    Hyderabad: ['Hyderabad Main Site', 'Hyderabad Assembly Site', 'Hyderabad Quality Site'],
    Chennai: ['Chennai Main Site', 'Chennai Assembly Site', 'Chennai Quality Site'],
    Pune: ['Pune Main Site', 'Pune Assembly Site', 'Pune Quality Site'],
    Mumbai: ['Mumbai Main Site', 'Mumbai Assembly Site', 'Mumbai Quality Site'],
  };
  pcOptions = ['PC-01', 'PC-02', 'PC-03', 'Line PC', 'Packing PC'];
  userOptions = ['Operator A', 'Operator B', 'Supervisor', 'Quality User', 'Maintenance'];
  dateSelectionOptions = ['All Dates', 'Exact Dates', 'Today', 'Yesterday', 'Yesterday to Now', 'This Week', 'Last 24 hours'];
  todaysFilters = {
    dateSelection: '',
    fromDate: '',
    toDate: '',
    site: '',
    station: '',
    partNumber: '',
    workOrder: '',
    pc: '',
    user: '',
    xAxis: 'hour',
  };
  todaysHourlyBuckets: TodaysHourBucket[] = [];
  todaysDailyBuckets: TodaysHourBucket[] = [];
  todaysDailyBucket: TodaysHourBucket | null = null;
  todaysFpyTrend: TodaysFpyPoint[] = [];
  todaysFailureByStation: TodaysStationFailure[] = [];
  todaysSummary: TodaysDashboardSummary | null = null;
  selectedTodaysBucketIndex = 0;
  selectedTodaysDailyBucketIndex = 0;
  todaysDetailsModalOpen = false;
  todaysDetailsTitle = '';
  todaysDetailsRows: TodaysSnDetail[] = [];
  todaysMetricRows: TodaysSnDetail[] | null = null;
  todaysMetricLabel = '';
  openTodaysDropdown: TodaysDropdownKey | null = null;
  todaysCurrentPage = 1;
  readonly todaysPageSize = 10;
  todaysAutoRefreshEnabled = true;
  todaysLastUpdatedAt = new Date();
  private connectorFrame: number | null = null;
  private lastConnectorSignature = '';
  private routeSubscription?: Subscription;
  private todaysAutoRefreshTimer: number | null = null;
  private todaysLookupPending = 0;
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
    this.stopTodaysAutoRefresh();
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

  @HostListener('document:click')
  onDocumentClick(): void {
    this.openTodaysDropdown = null;
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

  openActivityQualityDashboard(): void {
    this.activeView = 'activityQuality';
    this.activityErrorMessage = '';
    if (this.activityData.chartData.length === 0 && !this.activityLoading) {
      this.loadActivityQualityDashboard();
    }
  }

  openTodaysDashboard(): void {
    this.activeView = 'todays';
    this.todaysActiveTab = 'filters';
    this.errorMessage = '';
    this.loadTodaysLookups();
  }

  backToMenu(): void {
    this.activeView = 'menu';
    this.todaysActiveTab = 'filters';
    this.errorMessage = '';
    this.successMessage = '';
    this.report = null;
    this.selectedStation = null;
    this.currentWorkOrder = '';
    this.resetScrapForm();
    this.showExportMenu = false;
    this.fullScreenEnabled = false;
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

  loadActivityQualityDashboard(): void {
    this.applyDateSelection();
    this.activityLoading = true;
    this.activityErrorMessage = '';
    this.selectedChartPoint = null;
    this.activityDetailsCurrentPage = 1;

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
    this.activityDetailsCurrentPage = 1;
  }

  closeActivityDetailsPopup(): void {
    this.selectedChartPoint = null;
    this.activityDetailsCurrentPage = 1;
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

  exportChartPointData(point: ActivityQualityChartPoint): void {
    const headers = ['Date/Time', 'Site', 'Station', 'Product Line', 'Part Number', 'Work Order', 'PC', 'User', 'Serial Number', 'Result', 'Failure Reason'];
    const rows = this.activityRowsForPoint(point).map((row) => [
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
    const safeLabel = point.label.replace(/[^a-z0-9_-]+/gi, '-').replace(/^-+|-+$/g, '').toLowerCase() || 'bar';
    const csv = [headers, ...rows].map((row) => row.map((cell) => `"${String(cell ?? '').replace(/"/g, '""')}"`).join(',')).join('\n');
    this.downloadText(`activity-quality-${safeLabel}.csv`, csv);
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

  chartSnCount(point: ActivityQualityChartPoint): number {
    return point.passCount + point.failCount;
  }

  chartFailRate(point: ActivityQualityChartPoint): number {
    const total = this.chartSnCount(point);
    return total === 0 ? 0 : Math.round((point.failCount / total) * 100);
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

  get pagedActivityDetailRows(): ActivityQualityDetailRow[] {
    const start = (this.activityDetailsCurrentPage - 1) * this.activityDetailsPageSize;
    return this.filteredDetailRows.slice(start, start + this.activityDetailsPageSize);
  }

  get activityDetailsTotalPages(): number {
    return Math.max(1, Math.ceil(this.filteredDetailRows.length / this.activityDetailsPageSize));
  }

  get activityDetailsShowingStart(): number {
    return this.filteredDetailRows.length === 0 ? 0 : ((this.activityDetailsCurrentPage - 1) * this.activityDetailsPageSize) + 1;
  }

  get activityDetailsShowingEnd(): number {
    return Math.min(this.filteredDetailRows.length, this.activityDetailsCurrentPage * this.activityDetailsPageSize);
  }

  get activityDetailsPageNumbers(): Array<number | string> {
    const total = this.activityDetailsTotalPages;
    if (total <= 6) {
      return Array.from({ length: total }, (_, index) => index + 1);
    }

    const pages = new Set<number>([1, total, this.activityDetailsCurrentPage]);
    if (this.activityDetailsCurrentPage > 1) pages.add(this.activityDetailsCurrentPage - 1);
    if (this.activityDetailsCurrentPage < total) pages.add(this.activityDetailsCurrentPage + 1);

    const sorted = Array.from(pages).sort((a, b) => a - b);
    const result: Array<number | string> = [];
    sorted.forEach((page, index) => {
      const previous = sorted[index - 1];
      if (previous && page - previous > 1) {
        result.push('...');
      }
      result.push(page);
    });

    return result;
  }

  setActivityDetailsPage(page: number | string): void {
    if (typeof page !== 'number') {
      return;
    }

    this.activityDetailsCurrentPage = Math.max(1, Math.min(this.activityDetailsTotalPages, page));
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

  resetTodaysFilters(): void {
    this.stopTodaysAutoRefresh();
    this.todaysAutoRefreshEnabled = true;
    this.todaysFilters = {
      dateSelection: '',
      fromDate: '',
      toDate: '',
      site: '',
      station: '',
      partNumber: '',
      workOrder: '',
      pc: '',
      user: '',
      xAxis: 'hour',
    };
    this.todaysSummary = null;
    this.todaysHourlyBuckets = [];
    this.todaysDailyBuckets = [];
    this.todaysDailyBucket = null;
    this.todaysFpyTrend = [];
    this.todaysFailureByStation = [];
    this.todaysMetricRows = null;
    this.todaysMetricLabel = '';
    this.selectedTodaysDailyBucketIndex = 0;
    this.todaysCurrentPage = 1;
    this.todaysActiveTab = 'filters';
    this.loadTodaysLookups(true);
  }

  runTodaysDashboard(): void {
    if (!this.isTodaysDashboardReady) {
      this.errorMessage = 'Please fill all required dashboard filters in order.';
      return;
    }

    this.loadTodaysDashboardData();
  }

  loadTodaysLookups(force = false): void {
    if (!force && (this.todaysLookupsLoaded || this.todaysLookupLoading)) {
      return;
    }

    this.todaysLookupLoading = true;
    this.todaysLookupsLoaded = false;

    let params = new HttpParams();
    if (this.todaysFilters.site && !this.isTodaysAllValue(this.todaysFilters.site)) {
      params = params.set('site', this.todaysFilters.site);
    }
    if (this.todaysFilters.station && !this.isTodaysAllValue(this.todaysFilters.station)) {
      params = params.set('station', this.todaysFilters.station);
    }
    if (this.todaysFilters.partNumber && !this.isTodaysAllValue(this.todaysFilters.partNumber)) {
      params = params.set('pn', this.todaysFilters.partNumber);
    }

    this.http.get<TodaysDashboardOptions>(this.todaysOptionsApi, { params }).subscribe({
      next: (response) => {
        this.sites = this.mergeTodaysSites(response.sites || []);
        this.stations = response.stations || [];
        this.partNumbers = response.partNumbers || [];
        this.workOrders = (response.workOrders || []).filter((row) => Boolean(row.wo));
        this.todaysLookupsLoaded = true;
        this.todaysLookupLoading = false;
      },
      error: () => {
        this.sites = this.mergeTodaysSites([]);
        this.stations = [];
        this.partNumbers = [];
        this.workOrders = [];
        this.todaysLookupsLoaded = true;
        this.todaysLookupLoading = false;
      }
    });
  }

  loadTodaysDashboardData(preserveSelection = false): void {
    const previousPage = this.todaysCurrentPage;
    const previousBucketIndex = this.selectedTodaysBucketIndex;
    let params = new HttpParams()
      .set('fromDate', this.todaysFilters.fromDate)
      .set('toDate', this.todaysFilters.toDate)
      .set('xAxis', this.todaysFilters.xAxis);

    if (this.todaysFilters.site && !this.isTodaysAllValue(this.todaysFilters.site)) {
      params = params.set('site', this.todaysFilters.site);
    }
    if (this.todaysFilters.station && !this.isTodaysAllValue(this.todaysFilters.station)) {
      params = params.set('station', this.todaysFilters.station);
    }
    if (this.todaysFilters.partNumber && !this.isTodaysAllValue(this.todaysFilters.partNumber)) {
      params = params.set('pn', this.todaysFilters.partNumber);
    }
    if (this.todaysFilters.workOrder && !this.isTodaysAllValue(this.todaysFilters.workOrder)) {
      params = params.set('wo', this.todaysFilters.workOrder);
    }

    this.loading = true;
    this.errorMessage = '';

    this.http.get<TodaysDashboardData>(this.todaysDataApi, { params }).subscribe({
      next: (response) => {
        this.todaysSummary = response.summary || null;
        this.todaysHourlyBuckets = response.hourlyBuckets || [];
        this.todaysDailyBuckets = response.dailyBuckets || [];
        this.todaysDailyBucket = response.dailyBucket || null;
        this.todaysFpyTrend = response.fpyTrend || [];
        this.todaysFailureByStation = response.failureByStation || [];
        this.todaysMetricRows = null;
        this.todaysMetricLabel = '';
        this.todaysLastUpdatedAt = response.lastUpdated ? new Date(response.lastUpdated) : new Date();
        this.selectedTodaysBucketIndex = preserveSelection
          ? Math.min(previousBucketIndex, Math.max(0, this.todaysHourlyBuckets.length - 1))
          : 0;
        this.selectedTodaysDailyBucketIndex = preserveSelection
          ? Math.min(this.selectedTodaysDailyBucketIndex, Math.max(0, this.todaysDailyBuckets.length - 1))
          : this.getDefaultTodaysDailyBucketIndex();
        this.todaysCurrentPage = preserveSelection ? Math.min(previousPage, this.todaysTotalPages) : 1;
        this.loading = false;
        this.todaysActiveTab = 'overview';
        this.startTodaysAutoRefresh();
      },
      error: (error) => {
        this.todaysSummary = null;
        this.todaysHourlyBuckets = [];
        this.todaysDailyBuckets = [];
        this.todaysDailyBucket = null;
        this.todaysFpyTrend = [];
        this.todaysFailureByStation = [];
        this.todaysMetricRows = null;
        this.todaysMetricLabel = '';
        this.todaysCurrentPage = 1;
        this.loading = false;
        this.errorMessage = error?.error?.message || 'Unable to load Today dashboard data.';
      }
    });
  }

  onDateSelectionChange(): void {
    const today = new Date();
    const startOfToday = this.startOfDay(today);
    const yesterday = new Date(startOfToday);
    yesterday.setDate(yesterday.getDate() - 1);

    switch (this.todaysFilters.dateSelection) {
      case 'All Dates': {
        const allStart = new Date(2000, 0, 1);
        this.setTodaysDateRange(allStart, today);
        break;
      }
      case 'Today':
        this.setTodaysDateRange(startOfToday, startOfToday);
        break;
      case 'Yesterday':
        this.setTodaysDateRange(yesterday, yesterday);
        break;
      case 'Yesterday to Now':
        this.setTodaysDateRange(yesterday, today);
        break;
      case 'This Week': {
        const weekStart = new Date(startOfToday);
        weekStart.setDate(weekStart.getDate() - weekStart.getDay());
        this.setTodaysDateRange(weekStart, today);
        break;
      }
      case 'Last 24 hours': {
        const lastDay = new Date(today);
        lastDay.setHours(lastDay.getHours() - 24);
        this.setTodaysDateRange(lastDay, today);
        break;
      }
      case 'Exact Dates':
      default:
        if (!this.todaysFilters.fromDate) this.todaysFilters.fromDate = this.getTodayInputValue();
        if (!this.todaysFilters.toDate) this.todaysFilters.toDate = this.getTodayInputValue();
        this.enforceXAxisDateRule();
        break;
    }

    this.resetTodaysAfter('date');
  }

  onTodaysDateChange(): void {
    this.enforceXAxisDateRule();
    this.resetTodaysAfter('date');
  }

  onTodaysSiteChange(): void {
    this.resetTodaysAfter('site');
    this.loadTodaysLookups(true);
  }

  onTodaysStationChange(): void {
    this.resetTodaysAfter('station');
    this.loadTodaysLookups(true);
  }

  onTodaysPartNumberChange(): void {
    this.resetTodaysAfter('partNumber');
    this.loadTodaysLookups(true);
  }

  onTodaysWorkOrderChange(): void {
    this.resetTodaysAfter('workOrder');
  }

  toggleTodaysDropdown(field: TodaysDropdownKey, event: Event): void {
    event.preventDefault();
    event.stopPropagation();

    if (this.isTodaysDropdownDisabled(field)) {
      this.openTodaysDropdown = null;
      return;
    }

    this.openTodaysDropdown = this.openTodaysDropdown === field ? null : field;
  }

  selectTodaysDropdownOption(field: TodaysDropdownKey, value: string, event: Event): void {
    event.preventDefault();
    event.stopPropagation();

    this.todaysFilters[field] = value;
    this.openTodaysDropdown = null;

    if (field === 'dateSelection') {
      this.onDateSelectionChange();
    } else if (field === 'site') {
      this.onTodaysSiteChange();
    } else if (field === 'station') {
      this.onTodaysStationChange();
    } else if (field === 'partNumber') {
      this.onTodaysPartNumberChange();
    } else if (field === 'workOrder') {
      this.onTodaysWorkOrderChange();
    } else if (field === 'pc') {
      this.todaysFilters.user = '';
    }
  }

  isTodaysDropdownDisabled(field: TodaysDropdownKey): boolean {
    switch (field) {
      case 'site':
        return !this.isTodaysDateReady;
      case 'station':
        return !this.todaysFilters.site;
      case 'partNumber':
        return !this.todaysFilters.station;
      case 'workOrder':
        return !this.todaysFilters.partNumber;
      case 'pc':
        return !this.todaysFilters.workOrder;
      case 'user':
        return !this.todaysFilters.pc;
      default:
        return false;
    }
  }

  getTodaysDropdownLabel(field: TodaysDropdownKey): string {
    const value = this.getTodaysDropdownValue(field);
    if (!value) {
      return this.getTodaysDropdownPlaceholder(field);
    }

    const option = this.getTodaysDropdownOptions(field).find((item) => item.value === value);
    return option?.label || value;
  }

  getTodaysDropdownValue(field: TodaysDropdownKey): string {
    return String(this.todaysFilters[field] || '');
  }

  isTodaysDropdownSelected(field: TodaysDropdownKey, value: string): boolean {
    return this.getTodaysDropdownValue(field) === value;
  }

  getTodaysDropdownPlaceholder(field: TodaysDropdownKey): string {
    switch (field) {
      case 'dateSelection':
        return 'Select Date';
      case 'site':
        return 'Select Site';
      case 'station':
        return 'Select Station';
      case 'partNumber':
        return 'Select Part Number';
      case 'workOrder':
        return 'Select Work Order';
      case 'pc':
        return 'Select PC';
      case 'user':
        return 'Select User';
      default:
        return 'Select';
    }
  }

  getTodaysDropdownOptions(field: TodaysDropdownKey): TodaysDropdownOption[] {
    switch (field) {
      case 'dateSelection':
        return this.dateSelectionOptions.map((option) => ({ value: option, label: option }));
      case 'site':
        return [
          { value: TODAYS_ALL_VALUE, label: 'All Sites' },
          ...this.sites.map((site) => ({ value: site.name, label: site.name })),
        ];
      case 'station':
        return [
          { value: TODAYS_ALL_VALUE, label: 'All Stations' },
          ...this.stations.map((station) => ({
            value: station.station_code,
            label: station.station_name
              ? `${station.station_code} - ${station.station_name}`
              : station.station_code,
          })),
        ];
      case 'partNumber':
        return [
          { value: TODAYS_ALL_VALUE, label: 'All Part Numbers' },
          ...this.partNumbers.map((part) => ({ value: part.pn, label: part.pn })),
        ];
      case 'workOrder':
        return [
          { value: TODAYS_ALL_VALUE, label: 'All Work Orders' },
          ...this.workOrders.map((workOrder) => ({ value: workOrder.wo, label: workOrder.wo })),
        ];
      case 'pc':
        return [
          { value: TODAYS_ALL_VALUE, label: 'All PCs' },
          ...this.pcOptions.map((pc) => ({ value: pc, label: pc })),
        ];
      case 'user':
        return [
          { value: TODAYS_ALL_VALUE, label: 'All Users' },
          ...this.userOptions.map((user) => ({ value: user, label: user })),
        ];
      default:
        return [];
    }
  }

  get isDateRangeOver48Hours(): boolean {
    if (!this.todaysFilters.fromDate || !this.todaysFilters.toDate) {
      return false;
    }

    const from = new Date(this.todaysFilters.fromDate);
    const to = new Date(this.todaysFilters.toDate);
    return (to.getTime() - from.getTime()) > (48 * 60 * 60 * 1000);
  }

  get isTodaysDateReady(): boolean {
    return Boolean(this.todaysFilters.dateSelection && this.todaysFilters.fromDate && this.todaysFilters.toDate);
  }

  get isTodaysDashboardReady(): boolean {
    return Boolean(
      this.isTodaysDateReady &&
      this.todaysFilters.site &&
      this.todaysFilters.station &&
      this.todaysFilters.partNumber &&
      this.todaysFilters.workOrder &&
      this.todaysFilters.pc &&
      this.todaysFilters.user &&
      this.todaysFilters.xAxis
    );
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

  trackByTodaysBucket(index: number, bucket: TodaysHourBucket): string {
    return `${index}-${bucket.label}`;
  }

  trackByTodaysSn(index: number, sn: TodaysSnDetail): string {
    return `${index}-${sn.sn}`;
  }

  selectTodaysBucket(index: number): void {
    this.selectedTodaysBucketIndex = index;
    this.todaysCurrentPage = 1;
  }

  selectTodaysDailyBucket(index: number): void {
    this.selectedTodaysDailyBucketIndex = index;
    this.todaysCurrentPage = 1;
  }

  selectTodaysGraphBucket(index: number): void {
    if (this.todaysFilters.xAxis === 'day') {
      this.selectTodaysDailyBucket(index);
    } else {
      this.selectTodaysBucket(index);
    }

    const bucket = this.todaysGraphBuckets[index];
    this.openTodaysDetails(bucket?.sns || [], `${bucket?.label || 'Selected'} SN Details`);
  }

  openTodaysFpyPoint(index: number): void {
    if (this.todaysFilters.xAxis === 'day') {
      this.selectTodaysDailyBucket(index);
    } else {
      this.selectTodaysBucket(index);
    }

    const bucket = this.todaysGraphBuckets[index];
    this.openTodaysDetails(bucket?.sns || [], `${bucket?.label || 'Selected'} FPY Details`);
  }

  openTodaysStationFailure(row: TodaysStationFailure): void {
    const station = String(row.station || '').trim();
    const rows = this.todaysAllRows.filter((item) =>
      item.status === 'Fail' && String(item.station || '').trim() === station
    );
    this.openTodaysDetails(rows, `${station || 'Station'} Failure Details`);
  }

  openTodaysDetails(rows: TodaysSnDetail[], title: string): void {
    this.todaysDetailsRows = rows || [];
    this.todaysDetailsTitle = title;
    this.todaysMetricRows = rows || [];
    this.todaysMetricLabel = title.replace(/\s+Details$/i, '');
    this.todaysCurrentPage = 1;
    this.todaysDetailsModalOpen = true;
  }

  closeTodaysDetails(): void {
    this.todaysDetailsModalOpen = false;
    this.todaysDetailsRows = [];
    this.todaysDetailsTitle = '';
    this.todaysCurrentPage = 1;
  }

  setTodaysAllFilter(field: Exclude<TodaysDropdownKey, 'dateSelection'>, checked: boolean): void {
    this.todaysFilters[field] = checked ? TODAYS_ALL_VALUE : '';

    if (field === 'site') {
      this.onTodaysSiteChange();
    } else if (field === 'station') {
      this.onTodaysStationChange();
    } else if (field === 'partNumber') {
      this.onTodaysPartNumberChange();
    } else if (field === 'workOrder') {
      this.onTodaysWorkOrderChange();
    } else if (field === 'pc') {
      this.todaysFilters.user = '';
    }
  }

  getDefaultTodaysDailyBucketIndex(): number {
    for (let index = this.todaysDailyBuckets.length - 1; index >= 0; index--) {
      if (this.getTodaysBucketTotal(this.todaysDailyBuckets[index]) > 0) {
        return index;
      }
    }

    return 0;
  }

  get selectedTodaysBucket(): TodaysHourBucket | null {
    if (this.todaysFilters.xAxis === 'day') {
      return this.todaysDailyBuckets[this.selectedTodaysDailyBucketIndex] || this.todaysDailyBucket;
    }

    return this.todaysHourlyBuckets[this.selectedTodaysBucketIndex] || null;
  }

  get todaysGraphBuckets(): TodaysHourBucket[] {
    return this.todaysFilters.xAxis === 'day' ? this.todaysDailyBuckets : this.todaysHourlyBuckets;
  }

  get selectedTodaysRows(): TodaysSnDetail[] {
    return this.todaysDetailsModalOpen ? this.todaysDetailsRows : (this.selectedTodaysBucket?.sns || []);
  }

  get todaysAllRows(): TodaysSnDetail[] {
    const seen = new Set<string>();
    const rows: TodaysSnDetail[] = [];
    this.todaysGraphBuckets.forEach((bucket) => {
      (bucket.sns || []).forEach((row) => {
        const key = `${row.sn}-${row.station}-${row.startTime}-${row.endTime}`;
        if (!seen.has(key)) {
          seen.add(key);
          rows.push(row);
        }
      });
    });
    return rows;
  }

  get pagedTodaysRows(): TodaysSnDetail[] {
    const start = (this.todaysCurrentPage - 1) * this.todaysPageSize;
    return this.selectedTodaysRows.slice(start, start + this.todaysPageSize);
  }

  get todaysTotalPages(): number {
    return Math.max(1, Math.ceil(this.selectedTodaysRows.length / this.todaysPageSize));
  }

  get todaysShowingStart(): number {
    return this.selectedTodaysRows.length === 0 ? 0 : ((this.todaysCurrentPage - 1) * this.todaysPageSize) + 1;
  }

  get todaysShowingEnd(): number {
    return Math.min(this.selectedTodaysRows.length, this.todaysCurrentPage * this.todaysPageSize);
  }

  get todaysPageNumbers(): Array<number | string> {
    const total = this.todaysTotalPages;
    if (total <= 6) {
      return Array.from({ length: total }, (_, index) => index + 1);
    }

    const pages = new Set<number>([1, total, this.todaysCurrentPage]);
    if (this.todaysCurrentPage > 1) pages.add(this.todaysCurrentPage - 1);
    if (this.todaysCurrentPage < total) pages.add(this.todaysCurrentPage + 1);

    const sorted = Array.from(pages).sort((a, b) => a - b);
    const result: Array<number | string> = [];
    sorted.forEach((page, index) => {
      const previous = sorted[index - 1];
      if (previous && page - previous > 1) {
        result.push('...');
      }
      result.push(page);
    });

    return result;
  }

  setTodaysPage(page: number | string): void {
    if (typeof page !== 'number') {
      return;
    }

    this.todaysCurrentPage = Math.max(1, Math.min(this.todaysTotalPages, page));
  }

  goToTodaysFilters(): void {
    this.todaysActiveTab = 'filters';
  }

  toggleTodaysAutoRefresh(): void {
    this.todaysAutoRefreshEnabled = !this.todaysAutoRefreshEnabled;
    if (this.todaysAutoRefreshEnabled) {
      this.refreshTodaysDashboard();
      this.startTodaysAutoRefresh();
    } else {
      this.stopTodaysAutoRefresh();
    }
  }

  setTodaysAutoRefresh(enabled: boolean): void {
    this.todaysAutoRefreshEnabled = enabled;
    if (enabled) {
      this.refreshTodaysDashboard();
      this.startTodaysAutoRefresh();
    } else {
      this.stopTodaysAutoRefresh();
    }
  }

  refreshTodaysDashboard(): void {
    if (!this.isTodaysDashboardReady) {
      return;
    }

    this.loadTodaysDashboardData(true);
  }

  downloadTodaysCsv(): void {
    const rows = this.getTodaysExportRows();
    const headers = this.getTodaysExportHeaders();
    const csv = [
      headers.join(','),
      ...rows.map((row) => headers.map((header) => this.toCsvCell(row[header] || '')).join(',')),
    ].join('\n');

    this.downloadTextFile(csv, `todays-dashboard-${this.todaysFilters.xAxis}.csv`, 'text/csv;charset=utf-8;');
  }

  exportTodaysExcel(): void {
    const rows = this.getTodaysExportRows();
    const headers = this.getTodaysExportHeaders();
    const table = `
      <table>
        <thead><tr>${headers.map((header) => `<th>${this.escapeHtml(header)}</th>`).join('')}</tr></thead>
        <tbody>
          ${rows.map((row) => `<tr>${headers.map((header) => `<td>${this.escapeHtml(row[header] || '')}</td>`).join('')}</tr>`).join('')}
        </tbody>
      </table>
    `;

    this.downloadTextFile(table, `todays-dashboard-${this.todaysFilters.xAxis}.xls`, 'application/vnd.ms-excel;charset=utf-8;');
  }

  exportTodaysPdf(): void {
    const rows = this.getTodaysExportRows();
    const headers = this.getTodaysExportHeaders();
    const metrics = this.todaysOverviewMetrics;
    const popup = window.open('', '_blank', 'width=1100,height=800');
    if (!popup) {
      this.errorMessage = 'Please allow popups to export PDF.';
      return;
    }

    popup.document.write(`
      <!doctype html>
      <html>
        <head>
          <title>Today's Dashboard</title>
          <style>
            body { font-family: Arial, sans-serif; color: #07133a; padding: 24px; }
            h1 { margin: 0 0 6px; }
            .meta { color: #44516f; margin-bottom: 18px; }
            .metrics { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; margin-bottom: 18px; }
            .metric { border: 1px solid #d9e2ef; border-radius: 8px; padding: 12px; }
            .metric small { color: #51627f; text-transform: uppercase; font-weight: 700; }
            .metric strong { display: block; margin-top: 8px; font-size: 22px; }
            table { width: 100%; border-collapse: collapse; font-size: 12px; }
            th, td { border: 1px solid #d9e2ef; padding: 8px; text-align: left; }
            th { background: #f3f6fb; }
            @media print { button { display: none; } }
          </style>
        </head>
        <body>
          <h1>Today's Dashboard</h1>
          <div class="meta">Generated: ${this.escapeHtml(this.todaysLastUpdatedLabel)} | X-Axis: ${this.escapeHtml(this.todaysFilters.xAxis)}</div>
          <div class="metrics">
            ${metrics.map((metric) => `<div class="metric"><small>${this.escapeHtml(metric.label)}</small><strong>${this.escapeHtml(metric.value)}</strong><span>${this.escapeHtml(metric.subtext)}</span></div>`).join('')}
          </div>
          <table>
            <thead><tr>${headers.map((header) => `<th>${this.escapeHtml(header)}</th>`).join('')}</tr></thead>
            <tbody>${rows.map((row) => `<tr>${headers.map((header) => `<td>${this.escapeHtml(row[header] || '')}</td>`).join('')}</tr>`).join('')}</tbody>
          </table>
          <script>window.onload = function () { window.print(); };</script>
        </body>
      </html>
    `);
    popup.document.close();
  }

  get todaysOverviewMetrics(): TodaysMetricCard[] {
    const bucket = this.selectedTodaysBucket;
    if (!bucket) {
      return [];
    }
    const rows = this.todaysMetricRows || bucket.sns;
    const total = rows.length;
    const pass = rows.filter((row) => row.status === 'Pass').length;
    const fail = rows.filter((row) => row.status === 'Fail').length;
    const rework = rows.filter((row) => row.status === 'Rework').length;
    const nff = rows.filter((row) => row.status === 'NFF').length;
    const pending = rows.filter((row) => row.status === 'Pending').length;
    const fpy = total > 0 ? (pass * 100 / total) : 0;
    const wip = pending + rework + nff;
    const avgCycle = this.getAverageCycleSeconds(rows);
    const topFailing = this.getTopStationFromRows(rows.filter((row) => row.status === 'Fail'));
    const highestLoad = this.getTopStationFromRows(rows);
    const metricLabel = this.todaysMetricLabel || `${bucket.label} selected`;

    return [
      {
        label: 'Total SN',
        value: this.formatTodaysNumber(total),
        subtext: metricLabel,
        trend: '+ 12.5%',
        trendDirection: 'up',
        icon: 'dashboard',
        tone: 'blue',
      },
      {
        label: 'Pass Count',
        value: this.formatTodaysNumber(pass),
        subtext: 'vs Yesterday',
        trend: '+ 11.8%',
        trendDirection: 'up',
        icon: 'check_circle',
        tone: 'green',
      },
      {
        label: 'Fail Count',
        value: this.formatTodaysNumber(fail),
        subtext: 'vs Yesterday',
        trend: '+ 8.3%',
        trendDirection: 'up',
        icon: 'cancel',
        tone: 'red',
      },
      {
        label: 'FPY (%)',
        value: `${fpy.toFixed(2)}%`,
        subtext: 'vs Yesterday',
        trend: '+ 2.35%',
        trendDirection: 'up',
        icon: 'monitoring',
        tone: 'purple',
      },
      {
        label: 'WIP Count',
        value: this.formatTodaysNumber(wip),
        subtext: 'vs Yesterday',
        trend: '- 5.6%',
        trendDirection: 'down',
        icon: 'hourglass_empty',
        tone: 'amber',
      },
      {
        label: 'Avg Cycle Time',
        value: `${avgCycle.toFixed(1)} sec`,
        subtext: 'vs Yesterday',
        trend: '- 3.2 sec',
        trendDirection: 'down',
        icon: 'schedule',
        tone: 'blue',
      },
      {
        label: 'Top Failing Station',
        value: topFailing.station,
        subtext: `${this.formatTodaysNumber(topFailing.count)} Fails`,
        trend: '',
        trendDirection: 'up',
        icon: 'warning',
        tone: 'red',
      },
      {
        label: 'Highest Load Station',
        value: highestLoad.station,
        subtext: `${this.formatTodaysNumber(highestLoad.count)} SN`,
        trend: '',
        trendDirection: 'up',
        icon: 'desktop_windows',
        tone: 'cyan',
      },
    ];
  }

  get todaysMaxBucketTotal(): number {
    return Math.max(1, ...this.todaysGraphBuckets.map((bucket) => this.getTodaysBucketTotal(bucket)));
  }

  get todaysSnChartTitle(): string {
    return this.todaysFilters.xAxis === 'day' ? 'SN Count By Day' : 'SN Count By Hour';
  }

  get todaysFpyChartTitle(): string {
    return this.todaysFilters.xAxis === 'day' ? 'FPY (%) By Day' : 'FPY (%) By Hour';
  }

  get todaysFpyLinePoints(): string {
    if (this.todaysFpyTrend.length === 0) {
      return '';
    }

    return this.todaysFpyTrend
      .map((point, index) => `${this.getTodaysFpySvgX(index)},${this.getTodaysFpySvgY(point)}`)
      .join(' ');
  }

  get todaysFpyAreaPoints(): string {
    if (this.todaysFpyTrend.length === 0) {
      return '';
    }

    return `0,230 ${this.todaysFpyLinePoints} 640,230`;
  }

  get todaysMaxFailureStationCount(): number {
    return Math.max(1, ...this.todaysFailureByStation.map((row) => Number(row.count) || 0));
  }

  getTodaysFpyLeft(index: number): number {
    if (this.todaysFpyTrend.length <= 1) {
      return 50;
    }

    return 7 + ((index / (this.todaysFpyTrend.length - 1)) * 88);
  }

  getTodaysFpyBottom(point: TodaysFpyPoint): number {
    const value = Math.max(70, Math.min(100, Number(point.fpy) || 0));
    return 13 + (((value - 70) / 30) * 81);
  }

  getTodaysFpyLabelVisible(index: number): boolean {
    if (this.todaysFpyTrend.length <= 12) {
      return true;
    }

    return index % 2 === 0;
  }

  getTodaysFpyZoneWidth(): number {
    return this.todaysFpyTrend.length <= 1 ? 88 : Math.min(12, 88 / Math.max(1, this.todaysFpyTrend.length - 1));
  }

  getTodaysStationFailureWidth(row: TodaysStationFailure): number {
    return Math.max(4, ((Number(row.count) || 0) / this.todaysMaxFailureStationCount) * 100);
  }

  trackByTodaysFailureStation(index: number, row: TodaysStationFailure): string {
    return `${index}-${row.station}`;
  }

  private getTodaysFpySvgX(index: number): number {
    if (this.todaysFpyTrend.length <= 1) {
      return 320;
    }

    return Math.round((index / (this.todaysFpyTrend.length - 1)) * 640);
  }

  private getTodaysFpySvgY(point: TodaysFpyPoint): number {
    const value = Math.max(70, Math.min(100, Number(point.fpy) || 0));
    return Math.round(230 - ((value - 70) / 30) * 200);
  }

  getSegmentHeight(bucket: TodaysHourBucket, key: 'pass' | 'fail' | 'rework' | 'nff' | 'pending'): number {
    const total = this.getTodaysBucketTotal(bucket);
    if (total <= 0) {
      return 0;
    }

    const maxScaledPercent = (total / this.todaysMaxBucketTotal) * 100;
    return Math.max(3, (bucket[key] / total) * maxScaledPercent);
  }

  getTodaysBarHeight(bucket: TodaysHourBucket): number {
    const total = this.getTodaysBucketTotal(bucket);
    return Math.max(6, (total / this.todaysMaxBucketTotal) * 100);
  }

  getStatusShareHeight(bucket: TodaysHourBucket, key: 'pass' | 'fail' | 'rework' | 'nff' | 'pending'): number {
    const total = this.getTodaysBucketTotal(bucket);
    if (total <= 0) {
      return 0;
    }

    return Math.max(2, (bucket[key] / total) * 100);
  }

  getTodaysBucketTotal(bucket: TodaysHourBucket): number {
    return bucket.pass + bucket.fail + bucket.rework + bucket.nff + bucket.pending;
  }

  formatTodaysNumber(value: number): string {
    return new Intl.NumberFormat('en-US').format(Math.max(0, Math.round(value)));
  }

  getTodaysStatusClass(status: TodaysSnDetail['status']): string {
    return `status-${status.toLowerCase()}`;
  }

  getAverageCycleSeconds(rows: TodaysSnDetail[]): number {
    const values = rows
      .map((row) => Number(row.cycleSeconds) || 0)
      .filter((value) => value > 0);
    if (values.length === 0) {
      return 0;
    }

    return values.reduce((sum, value) => sum + value, 0) / values.length;
  }

  getTopStationFromRows(rows: TodaysSnDetail[]): { station: string; count: number } {
    const counts = new Map<string, number>();
    rows.forEach((row) => {
      const station = String(row.station || '-').trim() || '-';
      counts.set(station, (counts.get(station) || 0) + 1);
    });

    const top = Array.from(counts.entries()).sort((a, b) => b[1] - a[1])[0];
    return top ? { station: top[0], count: top[1] } : { station: '-', count: 0 };
  }

  getTodaysFilterDisplayValue(field: TodaysDropdownKey, fallback: string): string {
    const value = this.getTodaysDropdownValue(field);
    if (!value) {
      return fallback;
    }

    if (this.isTodaysAllValue(value)) {
      const options = this.getConcreteTodaysOptions(field);
      return options.length > 0
        ? this.getTodaysDropdownOptions(field).find((option) => option.value === value)?.label || fallback
        : fallback;
    }

    return value;
  }

  get todaysLastUpdatedLabel(): string {
    return new Intl.DateTimeFormat('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: true,
    }).format(this.todaysLastUpdatedAt);
  }

  private getTodayInputValue(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private setTodaysDateRange(from: Date, to: Date): void {
    this.todaysFilters.fromDate = this.formatDateInput(from);
    this.todaysFilters.toDate = this.formatDateInput(to);
    this.enforceXAxisDateRule();
  }

  private enforceXAxisDateRule(): void {
    if (this.isDateRangeOver48Hours) {
      this.todaysFilters.xAxis = 'day';
    }
  }

  private resetTodaysAfter(field: 'date' | 'site' | 'station' | 'partNumber' | 'workOrder'): void {
    if (field === 'date') {
      this.todaysFilters.site = '';
    }
    if (field === 'date' || field === 'site') {
      this.todaysFilters.station = '';
    }
    if (field === 'date' || field === 'site' || field === 'station') {
      this.todaysFilters.partNumber = '';
    }
    if (field === 'date' || field === 'site' || field === 'station' || field === 'partNumber') {
      this.todaysFilters.workOrder = '';
    }
    if (field !== 'workOrder') {
      this.todaysFilters.pc = '';
      this.todaysFilters.user = '';
    }
    this.errorMessage = '';
  }

  private startOfDay(date: Date): Date {
    const value = new Date(date);
    value.setHours(0, 0, 0, 0);
    return value;
  }

  private formatDateInput(date: Date): string {
    return date.toISOString().slice(0, 10);
  }

  private mergeTodaysSites(sites: ReportSiteOption[]): ReportSiteOption[] {
    const merged = new Map<string, ReportSiteOption>();

    sites.forEach((site) => {
      const name = String(site.name || '').trim();
      if (name) {
        merged.set(name.toLowerCase(), { id: Number(site.id) || 0, name });
      }
    });

    this.plantOptions
      .flatMap((plant) => this.fallbackSitesByPlant[plant] || [])
      .forEach((name, index) => {
        const key = name.toLowerCase();
        if (!merged.has(key)) {
          merged.set(key, { id: -1000 - index, name });
        }
      });

    return Array.from(merged.values()).sort((a, b) => a.name.localeCompare(b.name));
  }

  isTodaysAllValue(value: string): boolean {
    return value === TODAYS_ALL_VALUE;
  }

  private generateTodaysOverview(): void {
    this.todaysLastUpdatedAt = new Date();

    if (this.todaysFilters.xAxis !== 'hour') {
      this.todaysHourlyBuckets = [];
      this.selectedTodaysBucketIndex = 0;
      this.generateTodaysDailyOverview();
      this.todaysCurrentPage = 1;
      return;
    }

    this.todaysDailyBucket = null;
    const seed = this.getTodaysSeed();
    const labels = [
      '12 AM', '01 AM', '02 AM', '03 AM', '04 AM', '05 AM',
      '06 AM', '07 AM', '08 AM', '09 AM', '10 AM', '11 AM',
      '12 PM', '01 PM', '02 PM', '03 PM', '04 PM', '05 PM',
      '06 PM', '07 PM', '08 PM', '09 PM', '10 PM', '11 PM',
    ];
    const baseTotals = [
      320, 280, 260, 240, 310, 450, 680, 980, 1250, 1620, 1950, 1780,
      1650, 1420, 1150, 970, 650, 500, 420, 350, 310, 280, 265, 245,
    ];

    this.todaysHourlyBuckets = labels.map((label, index) => {
      const variation = ((seed + (index + 1) * 37) % 90) - 35;
      const total = Math.max(80, baseTotals[index] + variation);
      const fail = Math.max(8, Math.round(total * (0.12 + (((seed + index) % 6) / 100))));
      const rework = Math.max(4, Math.round(total * (0.035 + (((seed + index * 3) % 3) / 100))));
      const nff = Math.max(3, Math.round(total * (0.018 + (((seed + index * 5) % 3) / 100))));
      const pending = Math.max(3, Math.round(total * (0.012 + (((seed + index * 7) % 2) / 100))));
      const pass = Math.max(0, total - fail - rework - nff - pending);

      return {
        label,
        pass,
        fail,
        rework,
        nff,
        pending,
        sns: this.generateTodaysSnRows(label, index, pass, fail, rework, nff, pending, false),
      };
    });

    this.selectedTodaysBucketIndex = 0;
    this.todaysCurrentPage = 1;
  }

  private generateTodaysSnRows(
    label: string,
    bucketIndex: number,
    pass: number,
    fail: number,
    rework: number,
    nff: number,
    pending: number,
    includeFullDetails = false
  ): TodaysSnDetail[] {
    const rows: TodaysSnDetail[] = [];
    const statuses: Array<{ status: TodaysSnDetail['status']; count: number; results: string[] }> = [
      { status: 'Pass', count: pass, results: ['All Test Passed', 'Visual Check Passed', 'Functional Test Passed'] },
      { status: 'Fail', count: fail, results: ['Voltage High', 'Function Fail', 'Connection Fail'] },
      { status: 'Rework', count: rework, results: ['Rework Required', 'Retest Required'] },
      { status: 'NFF', count: nff, results: ['No Fault Found'] },
      { status: 'Pending', count: pending, results: ['In Process'] },
    ];
    const seed = this.getTodaysSeed();
    const dateCode = (this.todaysFilters.fromDate || this.getTodayInputValue()).replace(/-/g, '').slice(2);
    const displayRows = Math.min(80, Math.max(24, Math.round((pass + fail + rework + nff + pending) / 45)));
    let sequence = 1;

    statuses.forEach((group, groupIndex) => {
      const groupRows = group.status === 'Pass'
        ? Math.max(2, displayRows - 4)
        : Math.min(2, Math.max(1, Math.round(group.count / 220)));

      for (let i = 0; i < groupRows && rows.length < displayRows; i += 1) {
        const serialIndex = (bucketIndex + 1) * 1000 + (groupIndex + 1) * 100 + sequence;
        const rowPn = this.getGeneratedPartNumber(serialIndex);
        const rowWo = this.getGeneratedWorkOrder(serialIndex, dateCode);
        rows.push({
          sn: `SN${dateCode}${String(serialIndex).padStart(5, '0')}`,
          pn: rowPn,
          wo: rowWo,
          station: this.getGeneratedStation(serialIndex),
          operator: this.getGeneratedOperator(serialIndex),
          status: group.status,
          startTime: this.getGeneratedStartTime(bucketIndex, rows.length),
          endTime: group.status === 'Pending' ? '-' : this.getGeneratedEndTime(bucketIndex, rows.length),
          result: group.results[(seed + bucketIndex + i) % group.results.length],
        });
        sequence += 1;
      }
    });

    const sortedRows = rows.sort((a, b) => {
      const order = ['Pass', 'Fail', 'Rework', 'Pending', 'NFF'];
      return order.indexOf(a.status) - order.indexOf(b.status);
    });

    return includeFullDetails ? sortedRows : sortedRows.map((row) => ({ ...row }));
  }

  private generateTodaysDailyOverview(): void {
    const seed = this.getTodaysSeed();
    const dayCount = Math.max(1, this.getTodaysRangeDays());
    const total = Math.max(850, Math.round((18645 + (seed % 420)) * Math.min(dayCount, 8) / 4));
    const fail = Math.max(48, Math.round(total * (0.064 + ((seed % 5) / 1000))));
    const rework = Math.max(20, Math.round(total * 0.027));
    const nff = Math.max(12, Math.round(total * 0.011));
    const pending = Math.max(8, Math.round(total * 0.008));
    const pass = Math.max(0, total - fail - rework - nff - pending);

    this.todaysDailyBucket = {
      label: this.getTodaysDailyLabel(),
      pass,
      fail,
      rework,
      nff,
      pending,
      sns: this.generateTodaysSnRows('Daily', 10, pass, fail, rework, nff, pending, true),
    };
  }

  private startTodaysAutoRefresh(): void {
    this.stopTodaysAutoRefresh();
    if (!this.todaysAutoRefreshEnabled || typeof window === 'undefined') {
      return;
    }

    this.todaysAutoRefreshTimer = window.setInterval(() => {
      this.refreshTodaysDashboard();
      this.cdr.detectChanges();
    }, 5 * 60 * 1000);
  }

  private stopTodaysAutoRefresh(): void {
    if (this.todaysAutoRefreshTimer !== null && typeof window !== 'undefined') {
      window.clearInterval(this.todaysAutoRefreshTimer);
    }
    this.todaysAutoRefreshTimer = null;
  }

  private getTodaysExportHeaders(): string[] {
    return ['SN', 'PN', 'WO', 'Station', 'Operator', 'Status', 'Start Time', 'End Time', 'Result'];
  }

  private getTodaysExportRows(): Array<Record<string, string>> {
    return this.selectedTodaysRows.map((serial) => {
      const base: Record<string, string> = {
        SN: serial.sn,
        PN: serial.pn,
        WO: serial.wo,
        Status: serial.status,
        Result: serial.result,
      };

      base['Station'] = serial.station;
      base['Operator'] = serial.operator;
      base['Start Time'] = serial.startTime;
      base['End Time'] = serial.endTime;

      return base;
    });
  }

  private toCsvCell(value: string): string {
    return `"${String(value).replace(/"/g, '""')}"`;
  }

  private downloadTextFile(content: string, filename: string, mimeType: string): void {
    const blob = new Blob([content], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }

  private escapeHtml(value: string): string {
    return String(value)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  private getTodaysDailyLabel(): string {
    const from = this.todaysFilters.fromDate;
    const to = this.todaysFilters.toDate;
    if (from && to && from !== to) {
      return `${this.formatDisplayDate(from)} - ${this.formatDisplayDate(to)}`;
    }

    return this.formatDisplayDate(to || from || this.getTodayInputValue());
  }

  private getTodaysRangeDays(): number {
    if (!this.todaysFilters.fromDate || !this.todaysFilters.toDate) {
      return 1;
    }

    const from = new Date(this.todaysFilters.fromDate);
    const to = new Date(this.todaysFilters.toDate);
    const diff = Math.abs(to.getTime() - from.getTime());
    return Math.max(1, Math.ceil(diff / (24 * 60 * 60 * 1000)) + 1);
  }

  private formatDisplayDate(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }

    return new Intl.DateTimeFormat('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    }).format(date);
  }

  private getGeneratedStation(seed: number): string {
    if (this.todaysFilters.station && !this.isTodaysAllValue(this.todaysFilters.station)) {
      return this.todaysFilters.station;
    }

    const stations = this.getConcreteTodaysOptions('station').map((option) => option.value);
    const fallbackStations = ['ASM02', 'ASM08', 'APT7', 'AO105', 'ASM05', 'APT1', 'ASIN9'];
    const values = stations.length > 0 ? stations : fallbackStations;
    return values[seed % values.length];
  }

  private getRepresentativeStation(seed: number): string {
    if (this.todaysFilters.station && !this.isTodaysAllValue(this.todaysFilters.station)) {
      return this.todaysFilters.station;
    }

    const stations = this.getConcreteTodaysOptions('station').map((option) => option.value);
    return stations.length > 0 ? stations[seed % stations.length] : 'ASM02';
  }

  private getGeneratedOperator(seed: number): string {
    if (this.todaysFilters.user && !this.isTodaysAllValue(this.todaysFilters.user)) {
      return this.todaysFilters.user;
    }

    const users = this.getConcreteTodaysOptions('user').map((option) => option.value);
    const fallbackUsers = ['Michael', 'David', 'James', 'Robert', 'William', 'Daniel'];
    const values = users.length > 0 ? users : fallbackUsers;
    return values[seed % values.length];
  }

  private getGeneratedPartNumber(seed: number): string {
    if (this.todaysFilters.partNumber && !this.isTodaysAllValue(this.todaysFilters.partNumber)) {
      return this.todaysFilters.partNumber;
    }

    const partNumbers = this.getConcreteTodaysOptions('partNumber').map((option) => option.value);
    const fallbackPartNumbers = Array.from({ length: 8 }, (_, index) => `PN-${10020 + index}`);
    const values = partNumbers.length > 0 ? partNumbers : fallbackPartNumbers;
    return values[seed % values.length];
  }

  private getGeneratedWorkOrder(seed: number, dateCode: string): string {
    if (this.todaysFilters.workOrder && !this.isTodaysAllValue(this.todaysFilters.workOrder)) {
      return this.todaysFilters.workOrder;
    }

    const workOrders = this.getConcreteTodaysOptions('workOrder').map((option) => option.value);
    const fallbackWorkOrders = Array.from({ length: 6 }, (_, index) => `WO-${dateCode}-${String(index + 1).padStart(2, '0')}`);
    const values = workOrders.length > 0 ? workOrders : fallbackWorkOrders;
    return values[seed % values.length];
  }

  private getConcreteTodaysOptions(field: TodaysDropdownKey): TodaysDropdownOption[] {
    return this.getTodaysDropdownOptions(field).filter((option) => !this.isTodaysAllValue(option.value));
  }

  private getGeneratedStartTime(bucketIndex: number, rowIndex: number): string {
    const hour = 8 + ((bucketIndex + rowIndex) % 4);
    const minute = Math.max(0, 42 - (rowIndex * 4) - (bucketIndex % 3));
    const second = 10 + ((bucketIndex + rowIndex * 7) % 50);
    return this.formatClockTime(hour, minute, second);
  }

  private getGeneratedEndTime(bucketIndex: number, rowIndex: number): string {
    const hour = 8 + ((bucketIndex + rowIndex) % 4);
    const minute = Math.min(59, Math.max(0, 43 - (rowIndex * 4) - (bucketIndex % 3)));
    const second = 5 + ((bucketIndex + rowIndex * 9) % 50);
    return this.formatClockTime(hour, minute, second);
  }

  private formatClockTime(hour: number, minute: number, second: number): string {
    const date = new Date();
    date.setHours(hour, minute, second, 0);
    return new Intl.DateTimeFormat('en-US', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: true,
    }).format(date);
  }

  private getTodaysSeed(): number {
    const source = [
      this.todaysFilters.site,
      this.todaysFilters.station,
      this.todaysFilters.partNumber,
      this.todaysFilters.workOrder,
      this.todaysFilters.fromDate,
      this.todaysFilters.toDate,
    ].join('|');

    return Array.from(source).reduce((sum, char) => sum + char.charCodeAt(0), 0);
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

    return this.rowMatchesActivityPointLabel(row, label);
  }

  private activityRowsForPoint(point: ActivityQualityChartPoint): ActivityQualityDetailRow[] {
    return this.activityData.detailedRows.filter((row) => this.rowMatchesActivityPointLabel(row, point.label));
  }

  private rowMatchesActivityPointLabel(row: ActivityQualityDetailRow, label: string): boolean {
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
