import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnDestroy } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { environment } from '../../../environments/environment';

interface TraceSerial {
  id: number;
  sn: string;
  rsn: string;
  status: string;
  condition: string;
  current_station_code: string | null;
  current_station_name: string | null;
  current_station_order: number | null;
  created_at: string;
  updated_at: string;
  last_moved_at: string | null;
}

interface TraceDevice {
  product_line: string;
  pn: string;
  revision: string;
  work_order: string;
  work_order_status: string;
  work_order_qty: number;
  work_order_balance: number;
  site: string;
  description: string;
}

interface TraceProgress {
  total: number;
  completed: number;
  current: number;
  pending: number;
  percent: number;
}

interface TraceRouteStep {
  station_order: number;
  station_code: string;
  station_name: string;
  sample_mode: string;
  report_mode: string;
  state: 'completed' | 'current' | 'pending';
  is_current: boolean;
}

interface TraceHistoryRow {
  id: number;
  user_name: string;
  date_time: string;
  station: string;
  length: string | null;
  pc_name: string | null;
  result: 'PASS' | 'FAIL' | string;
  additional_info: string;
  event_type?: string;
  child_sn?: string;
  child_rsn?: string;
  child_pn?: string;
  child_revision?: string;
  parent_sn?: string;
  parent_rsn?: string;
  parent_pn?: string;
  parent_revision?: string;
}

interface TraceSearchResponse {
  query: string;
  matched_by: 'SN' | 'RSN';
  serial: TraceSerial;
  device: TraceDevice;
  progress: TraceProgress;
  routing: TraceRouteStep[];
  history: TraceHistoryRow[];
  generated_at: string;
}

@Component({
  selector: 'app-myroute',
  standalone: false,
  templateUrl: './myroute.component.html',
  styleUrls: ['./myroute.component.scss']
})
export class MyrouteComponent implements OnDestroy {
  readonly traceabilityApi = `${environment.apiUrl}/api/traceability/search`;

  query = '';
  loading = false;
  errorMessage = '';
  searchResult: TraceSearchResponse | null = null;

  autoRefresh = true;
  refreshIntervalMs = 10000;
  lastRefreshAt: Date | null = null;

  private refreshTimer: number | null = null;
  private queryParamSub: Subscription | null = null;

  constructor(
    private http: HttpClient,
    private route: ActivatedRoute,
    private router: Router
  ) {
    this.queryParamSub = this.route.queryParamMap.subscribe((params) => {
      const q = String(params.get('q') || '').trim();
      if (!q) {
        return;
      }

      if (q === this.query && this.searchResult) {
        return;
      }

      this.query = q;
      this.onSearch();
    });
  }

  onSearch(): void {
    const normalized = this.query.trim();

    if (!normalized) {
      this.errorMessage = 'Please enter SN or RSN.';
      this.searchResult = null;
      this.stopAutoRefresh();
      return;
    }

    if (!/^[A-Za-z0-9_-]+$/.test(normalized)) {
      this.errorMessage = 'Search supports only SN or RSN values.';
      this.searchResult = null;
      this.stopAutoRefresh();
      return;
    }

    this.fetchTrace(normalized, true);
  }

  toggleAutoRefresh(): void {
    this.autoRefresh = !this.autoRefresh;

    if (!this.autoRefresh) {
      this.stopAutoRefresh();
      return;
    }

    if (this.searchResult) {
      this.startAutoRefresh();
    }
  }

  get currentStepLabel(): string {
    const current = this.searchResult?.routing?.find((step) => step.is_current);
    if (!current) {
      return 'Not started';
    }

    return `${current.station_code} - ${current.station_name}`;
  }

  trackByStation(index: number, step: TraceRouteStep): string {
    return `${index}-${step.station_order}-${step.station_code}`;
  }

  trackByHistory(index: number, row: TraceHistoryRow): string {
    return `${index}-${row.id}`;
  }

  formatStateLabel(state: string): string {
    if (!state) {
      return '';
    }

    return state.charAt(0).toUpperCase() + state.slice(1).toLowerCase();
  }

  formatHistoryResult(result: string): string {
    const normalized = (result || '').toUpperCase();
    if (normalized === 'PASS') {
      return 'PASS';
    }

    if (normalized === 'FAIL') {
      return 'FAIL';
    }

    return result || '-';
  }

  openRouting(snOrRsn: string): void {
    const normalized = String(snOrRsn || '').trim();
    if (!normalized) {
      return;
    }

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: normalized },
      queryParamsHandling: 'merge',
    });
  }

  openChildRouting(childSn: string): void {
    this.openRouting(childSn);
  }

  onRouteWheel(event: WheelEvent): void {
    const container = event.currentTarget as HTMLElement | null;
    if (!container) {
      return;
    }

    const maxScrollLeft = container.scrollWidth - container.clientWidth;
    if (maxScrollLeft <= 0) {
      return;
    }

    // Keep the page fixed: any wheel gesture over this area scrolls the routing strip left/right.
    // This prevents vertical page scrolling while interacting with the route tracker.
    const delta = Math.abs(event.deltaX) > Math.abs(event.deltaY) ? event.deltaX : event.deltaY;
    container.scrollLeft += delta;
    event.preventDefault();
    event.stopPropagation();
  }

  ngOnDestroy(): void {
    this.stopAutoRefresh();

    if (this.queryParamSub) {
      this.queryParamSub.unsubscribe();
      this.queryParamSub = null;
    }
  }

  private fetchTrace(query: string, withLoader: boolean): void {
    if (withLoader) {
      this.loading = true;
    }

    this.errorMessage = '';

    const params = new HttpParams().set('query', query);

    this.http.get<TraceSearchResponse>(this.traceabilityApi, { params }).subscribe({
      next: (response) => {
        this.searchResult = response;
        this.loading = false;
        this.lastRefreshAt = new Date();

        if (this.autoRefresh) {
          this.startAutoRefresh();
        }
      },
      error: (error) => {
        this.loading = false;
        this.searchResult = null;
        this.stopAutoRefresh();
        this.errorMessage = error?.error?.message || 'No route data found for this serial.';
      },
    });
  }

  private startAutoRefresh(): void {
    this.stopAutoRefresh();

    this.refreshTimer = window.setInterval(() => {
      const normalized = this.query.trim();
      if (!normalized || !this.autoRefresh) {
        this.stopAutoRefresh();
        return;
      }

      this.fetchTrace(normalized, false);
    }, this.refreshIntervalMs);
  }

  private stopAutoRefresh(): void {
    if (this.refreshTimer) {
      window.clearInterval(this.refreshTimer);
      this.refreshTimer = null;
    }
  }
}
