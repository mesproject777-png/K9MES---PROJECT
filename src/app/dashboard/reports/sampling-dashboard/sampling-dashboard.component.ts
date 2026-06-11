import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { ReportViewBridge } from '../report-view-bridge';

type SamplingDateRange = '' | 'today' | 'yesterday' | 'thisWeek' | 'thisMonth' | 'custom';
type SamplingViewBy = 'station' | 'day';
type SamplingFilterKey = 'plant' | 'site' | 'samplingStation' | 'status' | 'samplingType' | 'partNumber' | 'workOrder';

interface SamplingOptionRow {
  id?: number;
  name?: string;
  pn?: string;
  wo?: string;
  station_code?: string;
  station_name?: string | null;
  sampling_type?: string;
  plant?: string | null;
}

interface SamplingDashboardOptions {
  sites: SamplingOptionRow[];
  samplingStations: SamplingOptionRow[];
  samplingTypes: SamplingOptionRow[];
  partNumbers: SamplingOptionRow[];
  workOrders: SamplingOptionRow[];
}

interface SamplingSnRow {
  snNumber: string;
  rsn: string;
  status: 'Pending' | 'Passed';
  partNumber: string;
  workOrder: string;
  station: string;
  samplingType: string;
  requestedTime: string;
  passedTime: string;
}

interface SamplingChartBucket {
  label: string;
  pending: number;
  passed: number;
  total: number;
  sns: SamplingSnRow[];
}

interface SamplingTypePoint {
  samplingType: string;
  count: number;
  percentage: number;
}

interface SamplingDashboardData {
  lastUpdated: string;
  summary: {
    total: number;
    pending: number;
    passed: number;
    avgSampleMinutes: number;
    pendingPercent: number;
    passedPercent: number;
  };
  chart: SamplingChartBucket[];
  stationBuckets: SamplingChartBucket[];
  dayBuckets: SamplingChartBucket[];
  samplingTypes: SamplingTypePoint[];
  sns: SamplingSnRow[];
}

@Component({
  selector: 'app-sampling-dashboard',
  standalone: false,
  templateUrl: './sampling-dashboard.component.html',
})
export class SamplingDashboardComponent extends ReportViewBridge implements OnInit {
  private readonly optionsApi = `${environment.apiUrl}/api/reports/sampling-dashboard/options`;
  private readonly dataApi = `${environment.apiUrl}/api/reports/sampling-dashboard/data`;

  samplingLoading = false;
  samplingOptionsLoaded = false;
  samplingHasRun = false;
  samplingOptions: SamplingDashboardOptions = this.emptyOptions();
  readonly plantOptions = ['Tirupati', 'Bangalore', 'Hyderabad', 'Chennai', 'Pune', 'Mumbai'];
  private readonly fallbackSitesByPlant: Record<string, string[]> = {
    Tirupati: ['Tirupati Main Site', 'Tirupati Assembly Site', 'Tirupati Quality Site'],
    Bangalore: ['Bangalore Main Site', 'Bangalore Assembly Site', 'Bangalore Quality Site'],
    Hyderabad: ['Hyderabad Main Site', 'Hyderabad Assembly Site', 'Hyderabad Quality Site'],
    Chennai: ['Chennai Main Site', 'Chennai Assembly Site', 'Chennai Quality Site'],
    Pune: ['Pune Main Site', 'Pune Assembly Site', 'Pune Quality Site'],
    Mumbai: ['Mumbai Main Site', 'Mumbai Assembly Site', 'Mumbai Quality Site'],
  };
  samplingData: SamplingDashboardData = this.emptyData();
  samplingFilters = {
    plant: '',
    site: '',
    dateRange: '' as SamplingDateRange,
    fromDate: '',
    toDate: '',
    samplingStation: '',
    status: '',
    samplingType: '',
    partNumber: '',
    workOrder: '',
    searchSn: '',
    viewBy: 'station' as SamplingViewBy,
  };
  samplingAllFilters: Record<SamplingFilterKey, boolean> = {
    plant: false,
    site: false,
    samplingStation: false,
    status: false,
    samplingType: false,
    partNumber: false,
    workOrder: false,
  };
  samplingSelectedBucket: SamplingChartBucket | null = null;
  samplingHoveredBucket: SamplingChartBucket | null = null;
  samplingDetailsOpen = false;
  samplingDetailsTitle = '';
  samplingDetailsRows: SamplingSnRow[] = [];
  samplingCurrentPage = 1;
  readonly samplingPageSize = 10;

  constructor(private http: HttpClient) {
    super();
  }

  ngOnInit(): void {
    this.applyDateRange();
    this.loadOptions();
  }

  loadOptions(): void {
    if (this.samplingOptionsLoaded) {
      return;
    }

    this.http.get<SamplingDashboardOptions>(this.optionsApi).subscribe({
      next: (response) => {
        this.samplingOptions = {
          sites: response.sites || [],
          samplingStations: response.samplingStations || [],
          samplingTypes: response.samplingTypes || [],
          partNumbers: response.partNumbers || [],
          workOrders: (response.workOrders || []).filter((row) => Boolean(row.wo)),
        };
        this.samplingOptionsLoaded = true;
      },
      error: () => {
        this.samplingOptions = this.emptyOptions();
        this.samplingOptionsLoaded = true;
      },
    });
  }

  loadSamplingDashboard(): void {
    this.applyDateRange();
    if (!this.isSamplingDashboardReady) {
      this.vm.errorMessage = 'Please select a date range before loading the Sampling Dashboard.';
      return;
    }

    this.samplingLoading = true;
    this.samplingHasRun = true;
    this.vm.errorMessage = '';

    this.http.get<SamplingDashboardData>(this.dataApi, { params: this.buildParams() }).subscribe({
      next: (response) => {
        this.samplingData = response || this.emptyData();
        this.samplingSelectedBucket = this.samplingData.chart[0] || null;
        this.samplingHoveredBucket = null;
        this.samplingDetailsOpen = false;
        this.samplingCurrentPage = 1;
        this.samplingLoading = false;
      },
      error: (error) => {
        this.samplingData = this.emptyData();
        this.samplingSelectedBucket = null;
        this.samplingHoveredBucket = null;
        this.samplingLoading = false;
        this.vm.errorMessage = error?.error?.message || 'Unable to load Sampling Dashboard.';
      },
    });
  }

  onDateRangeChange(): void {
    if (this.samplingFilters.dateRange === 'custom') {
      this.samplingFilters.fromDate = '';
      this.samplingFilters.toDate = '';
    } else {
      this.applyDateRange();
    }
    this.resetAfter('dateRange');
    if (!this.canViewByDay) {
      this.samplingFilters.viewBy = 'station';
    }
  }

  onCustomDateChange(): void {
    this.resetAfter('dateRange');
    if (!this.canViewByDay) {
      this.samplingFilters.viewBy = 'station';
    }
  }

  setViewBy(viewBy: SamplingViewBy): void {
    if (viewBy === 'day' && !this.canViewByDay) {
      return;
    }

    this.samplingFilters.viewBy = viewBy;
    this.loadSamplingDashboard();
  }

  setAllFilter(field: SamplingFilterKey, checked: boolean): void {
    this.samplingAllFilters[field] = checked;
    if (checked) {
      this.samplingFilters[field] = '';
    }
    if (field === 'plant') {
      this.samplingFilters.site = '';
      this.samplingAllFilters.site = false;
    }
    this.vm.errorMessage = '';
  }

  onFilterChange(field: SamplingFilterKey): void {
    this.samplingAllFilters[field] = false;
    if (field === 'plant') {
      this.samplingFilters.site = '';
      this.samplingAllFilters.site = false;
    }
    this.samplingFilters.searchSn = '';
    this.vm.errorMessage = '';
  }

  resetFilters(): void {
    this.samplingFilters = {
      plant: '',
      site: '',
      dateRange: '',
      fromDate: '',
      toDate: '',
      samplingStation: '',
      status: '',
      samplingType: '',
      partNumber: '',
      workOrder: '',
      searchSn: '',
      viewBy: 'station',
    };
    this.samplingAllFilters = {
      plant: false,
      site: false,
      samplingStation: false,
      status: false,
      samplingType: false,
      partNumber: false,
      workOrder: false,
    };
    this.samplingHasRun = false;
    this.samplingData = this.emptyData();
    this.samplingSelectedBucket = null;
    this.samplingHoveredBucket = null;
    this.closeDetails();
    this.vm.errorMessage = '';
  }

  isFilterDisabled(field: 'dateRange' | 'fromDate' | 'toDate' | 'plant' | 'site' | 'samplingStation' | 'status' | 'samplingType' | 'partNumber' | 'workOrder' | 'searchSn'): boolean {
    switch (field) {
      case 'dateRange':
        return false;
      case 'fromDate':
      case 'toDate':
        return this.samplingFilters.dateRange !== 'custom';
      case 'plant':
        return !this.isDateReady;
      case 'site':
      case 'samplingStation':
      case 'status':
      case 'samplingType':
      case 'partNumber':
      case 'workOrder':
      case 'searchSn':
        return !this.isDateReady;
      default:
        return false;
    }
  }

  get isSamplingDashboardReady(): boolean {
    return this.isDateReady;
  }

  get isCustomRange(): boolean {
    return this.samplingFilters.dateRange === 'custom';
  }

  get canViewByDay(): boolean {
    return Boolean(this.samplingFilters.fromDate && this.samplingFilters.toDate && this.samplingFilters.fromDate !== this.samplingFilters.toDate);
  }

  get siteOptionsForSelectedPlant(): SamplingOptionRow[] {
    if (this.samplingAllFilters.plant || !this.samplingFilters.plant) {
      return this.samplingOptions.sites;
    }

    const plant = this.samplingFilters.plant;
    if (!plant) {
      return [];
    }

    const normalizedPlant = this.normalizeLookupValue(plant);
    const matchedSites = this.samplingOptions.sites.filter((site) =>
      this.normalizeLookupValue(site.plant || site.name || '').includes(normalizedPlant)
    );

    if (matchedSites.length) {
      return matchedSites;
    }

    return (this.fallbackSitesByPlant[plant] || []).map((name, index) => ({
      id: this.getFallbackSiteId(plant, index),
      name,
      plant,
    }));
  }

  get chartTitle(): string {
    return this.samplingFilters.viewBy === 'day' ? 'Sampling by Day' : 'Sampling by Station';
  }

  get activeBucket(): SamplingChartBucket | null {
    return this.samplingHoveredBucket || this.samplingSelectedBucket;
  }

  get summarySource(): SamplingDashboardData['summary'] {
    const bucket = this.activeBucket;
    if (!bucket) {
      return this.samplingData.summary;
    }

    return {
      total: bucket.total,
      pending: bucket.pending,
      passed: bucket.passed,
      avgSampleMinutes: this.samplingData.summary.avgSampleMinutes,
      pendingPercent: bucket.total === 0 ? 0 : (bucket.pending * 100 / bucket.total),
      passedPercent: bucket.total === 0 ? 0 : (bucket.passed * 100 / bucket.total),
    };
  }

  selectBucket(bucket: SamplingChartBucket): void {
    this.samplingSelectedBucket = bucket;
    this.openDetails(bucket.sns || [], `${bucket.label} SN Details`);
  }

  hoverBucket(bucket: SamplingChartBucket | null): void {
    this.samplingHoveredBucket = bucket;
  }

  openDetails(rows: SamplingSnRow[], title: string): void {
    this.samplingDetailsRows = rows || [];
    this.samplingDetailsTitle = title;
    this.samplingCurrentPage = 1;
    this.samplingDetailsOpen = true;
  }

  closeDetails(): void {
    this.samplingDetailsOpen = false;
    this.samplingDetailsRows = [];
    this.samplingDetailsTitle = '';
    this.samplingCurrentPage = 1;
  }

  downloadCsv(): void {
    const headers = ['SN Number', 'RSN', 'Status', 'Part Number', 'Work Order', 'Station', 'Sampling Type', 'Requested Time', 'Passed Time'];
    const rows = (this.samplingDetailsOpen ? this.samplingDetailsRows : this.samplingData.sns).map((row) => ({
      'SN Number': row.snNumber,
      RSN: row.rsn,
      Status: row.status,
      'Part Number': row.partNumber,
      'Work Order': row.workOrder,
      Station: row.station,
      'Sampling Type': row.samplingType,
      'Requested Time': row.requestedTime,
      'Passed Time': row.passedTime || '-',
    }));
    const csv = [
      headers.join(','),
      ...rows.map((row) => headers.map((header) => this.csvCell(row[header as keyof typeof row] || '')).join(',')),
    ].join('\n');

    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = 'sampling-dashboard-sn-list.csv';
    anchor.click();
    URL.revokeObjectURL(url);
  }

  get pagedRows(): SamplingSnRow[] {
    const start = (this.samplingCurrentPage - 1) * this.samplingPageSize;
    return this.samplingDetailsRows.slice(start, start + this.samplingPageSize);
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.samplingDetailsRows.length / this.samplingPageSize));
  }

  get showingStart(): number {
    return this.samplingDetailsRows.length === 0 ? 0 : ((this.samplingCurrentPage - 1) * this.samplingPageSize) + 1;
  }

  get showingEnd(): number {
    return Math.min(this.samplingDetailsRows.length, this.samplingCurrentPage * this.samplingPageSize);
  }

  get pageNumbers(): Array<number | string> {
    const total = this.totalPages;
    const pages = new Set<number>([1, total, this.samplingCurrentPage]);
    if (this.samplingCurrentPage > 1) pages.add(this.samplingCurrentPage - 1);
    if (this.samplingCurrentPage < total) pages.add(this.samplingCurrentPage + 1);

    const sorted = Array.from(pages).filter((page) => page >= 1 && page <= total).sort((a, b) => a - b);
    const result: Array<number | string> = [];
    sorted.forEach((page, index) => {
      if (index > 0 && page - sorted[index - 1] > 1) {
        result.push('...');
      }
      result.push(page);
    });
    return result;
  }

  setPage(page: number | string): void {
    if (typeof page !== 'number') {
      return;
    }

    this.samplingCurrentPage = Math.max(1, Math.min(this.totalPages, page));
  }

  getBarHeight(bucket: SamplingChartBucket): number {
    const max = Math.max(1, ...this.samplingData.chart.map((item) => item.total));
    return Math.max(10, (bucket.total / max) * 100);
  }

  getSegmentHeight(bucket: SamplingChartBucket, key: 'pending' | 'passed'): number {
    if (!bucket.total) {
      return 0;
    }

    return Math.max(8, bucket[key] * 100 / bucket.total);
  }

  getTypeWidth(row: SamplingTypePoint): number {
    const max = Math.max(1, ...this.samplingData.samplingTypes.map((item) => item.count));
    return Math.max(4, row.count * 100 / max);
  }

  trackByBucket(_: number, bucket: SamplingChartBucket): string {
    return bucket.label;
  }

  trackByType(_: number, row: SamplingTypePoint): string {
    return row.samplingType;
  }

  trackBySn(_: number, row: SamplingSnRow): string {
    return `${row.snNumber}-${row.requestedTime}`;
  }

  formatNumber(value: number): string {
    return new Intl.NumberFormat('en-IN').format(value || 0);
  }

  private buildParams(): HttpParams {
    let params = new HttpParams()
      .set('fromDate', this.samplingFilters.fromDate)
      .set('toDate', this.samplingFilters.toDate)
      .set('viewBy', this.samplingFilters.viewBy);

    if (this.samplingFilters.plant) params = params.set('plant', this.samplingFilters.plant);
    if (this.samplingFilters.site) params = params.set('site', this.samplingFilters.site);
    if (this.samplingFilters.samplingStation) params = params.set('station', this.samplingFilters.samplingStation);
    if (this.samplingFilters.partNumber) params = params.set('pn', this.samplingFilters.partNumber);
    if (this.samplingFilters.workOrder) params = params.set('wo', this.samplingFilters.workOrder);
    if (this.samplingFilters.status) params = params.set('status', this.samplingFilters.status);
    if (this.samplingFilters.samplingType) params = params.set('samplingType', this.samplingFilters.samplingType);
    if (this.samplingFilters.searchSn) params = params.set('sn', this.samplingFilters.searchSn);
    return params;
  }

  private resetAfter(field: SamplingFilterKey | 'dateRange'): void {
    const order: SamplingFilterKey[] = ['plant', 'site', 'samplingStation', 'status', 'samplingType', 'partNumber', 'workOrder'];
    const start = field === 'dateRange' ? 0 : order.indexOf(field) + 1;
    order.slice(start).forEach((key) => {
      this.samplingFilters[key] = '';
      this.samplingAllFilters[key] = false;
    });
    this.samplingFilters.searchSn = '';
    this.vm.errorMessage = '';
  }

  private isFilterReady(field: SamplingFilterKey): boolean {
    return this.samplingAllFilters[field] || Boolean(this.samplingFilters[field]);
  }

  private get isDateReady(): boolean {
    if (this.samplingFilters.dateRange !== 'custom') {
      return Boolean(this.samplingFilters.dateRange);
    }

    return Boolean(this.samplingFilters.fromDate && this.samplingFilters.toDate);
  }

  private applyDateRange(): void {
    if (!this.samplingFilters.dateRange) {
      this.samplingFilters.fromDate = '';
      this.samplingFilters.toDate = '';
      return;
    }

    const today = new Date();
    let from = new Date(today);
    let to = new Date(today);

    if (this.samplingFilters.dateRange === 'yesterday') {
      from = new Date(today);
      from.setDate(today.getDate() - 1);
      to = new Date(from);
    } else if (this.samplingFilters.dateRange === 'thisWeek') {
      const day = today.getDay() || 7;
      from = new Date(today);
      from.setDate(today.getDate() - day + 1);
    } else if (this.samplingFilters.dateRange === 'thisMonth') {
      from = new Date(today.getFullYear(), today.getMonth(), 1);
    } else if (this.samplingFilters.dateRange === 'custom') {
      return;
    }

    this.samplingFilters.fromDate = this.dateInput(from);
    this.samplingFilters.toDate = this.dateInput(to);
  }

  private dateInput(value: Date): string {
    const year = value.getFullYear();
    const month = String(value.getMonth() + 1).padStart(2, '0');
    const day = String(value.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  private getFallbackSiteId(plant: string, index: number): number {
    const plantIndex = Math.max(this.plantOptions.indexOf(plant), 0);
    return -(((plantIndex + 1) * 100) + index + 1);
  }

  private normalizeLookupValue(value: string): string {
    return String(value || '').toLowerCase().replace(/[^a-z0-9]/g, '');
  }

  private csvCell(value: string): string {
    return `"${String(value).replace(/"/g, '""')}"`;
  }

  private emptyOptions(): SamplingDashboardOptions {
    return {
      sites: [],
      samplingStations: [],
      samplingTypes: [],
      partNumbers: [],
      workOrders: [],
    };
  }

  private emptyData(): SamplingDashboardData {
    return {
      lastUpdated: new Date().toISOString(),
      summary: {
        total: 0,
        pending: 0,
        passed: 0,
        avgSampleMinutes: 0,
        pendingPercent: 0,
        passedPercent: 0,
      },
      chart: [],
      stationBuckets: [],
      dayBuckets: [],
      samplingTypes: [],
      sns: [],
    };
  }
}
