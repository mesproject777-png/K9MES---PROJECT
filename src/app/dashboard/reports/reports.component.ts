import { HttpClient, HttpParams } from '@angular/common/http';
import { AfterViewInit, Component, ElementRef, OnInit, ViewChild } from '@angular/core';
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

interface TraceSearchResponse {
  serial: TraceSerial;
  device: TraceDevice;
}

interface StationOption {
  id: number;
  station_code: string;
  station_desc: string;
  status: string;
}

interface StationsResponse {
  data: StationOption[];
  total: number;
  page: number;
  limit: number;
}

interface PassFailResponse {
  message: string;
  action: 'PASS' | 'FAIL';
  data: TraceSearchResponse;
}

@Component({
  selector: 'app-reports',
  standalone: false,
  templateUrl: './reports.component.html',
  styleUrl: './reports.component.scss'
})
export class ReportsComponent implements OnInit, AfterViewInit {
  readonly passFailApi = `${environment.apiUrl}/api/traceability/pass-fail`;
  readonly stationsApi = `${environment.apiUrl}/api/stations`;

  @ViewChild('scanInput') scanInputRef!: ElementRef<HTMLInputElement>;

  scanValue = '';
  mode: 'PASS' | 'FAIL' = 'PASS';
  submitting = false;
  errorMessage = '';
  actionMessage = '';
  actionSuccess = false;
  result: TraceSearchResponse | null = null;

  stations: StationOption[] = [];
  selectedStationCode = '';
  stationsLoading = false;

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.loadStations();
  }

  ngAfterViewInit(): void {
    this.focusScanInput();
  }

  onScanKey(event: Event): void {
    const keyboardEvent = event as KeyboardEvent;
    if (keyboardEvent.key !== 'Enter' && keyboardEvent.key !== 'Tab') {
      return;
    }

    keyboardEvent.preventDefault();
    this.submitScannedSerial();
  }

  submitScannedSerial(): void {
    this.errorMessage = '';
    this.actionMessage = '';

    if (!this.selectedStationCode) {
      this.errorMessage = 'Please select a station first';
      this.focusScanInput();
      return;
    }

    const code = this.scanValue.trim();
    if (!code) {
      this.errorMessage = 'Please scan SN or RSN';
      this.focusScanInput();
      return;
    }

    if (!/^[A-Za-z0-9_-]+$/.test(code)) {
      this.errorMessage = 'Only SN or RSN values are allowed.';
      this.focusScanInput();
      return;
    }

    if (this.submitting) {
      return;
    }

    this.submitting = true;

    const changedBy = this.getCurrentOperator();
    const stationLength = String(code.length);
    const pcName = this.getClientPcName();
    const additionalInfo = this.mode === 'PASS' ? 'Auto Pass Result' : 'Auto Fail Result';

    this.http.post<PassFailResponse>(this.passFailApi, {
      query: code,
      station_code: this.selectedStationCode,
      result: this.mode,
      changed_by: changedBy,
      station_length: stationLength,
      pc_name: pcName,
      additional_info: additionalInfo,
    }).subscribe({
      next: (response) => {
        this.submitting = false;
        this.actionSuccess = true;
        this.actionMessage = response.message || `${this.mode} submitted successfully`;
        this.result = response.data;
        this.scanValue = '';
        this.focusScanInput();
      },
      error: (error) => {
        this.submitting = false;
        this.actionSuccess = false;
        this.actionMessage = '';
        this.errorMessage = error?.error?.message || `Unable to submit ${this.mode}`;
        this.focusScanInput();
      }
    });
  }

  trackByStationOption(index: number, station: StationOption): string {
    return `${index}-${station.station_code}`;
  }

  toggleMode(): void {
    this.mode = this.mode === 'PASS' ? 'FAIL' : 'PASS';
    this.actionMessage = '';
    this.errorMessage = '';
    this.focusScanInput();
  }

  getModeTitle(): string {
    return this.mode === 'PASS' ? 'Pass' : 'Fail';
  }

  getSwitchText(): string {
    return this.mode === 'PASS' ? 'Switch to FAIL report' : 'Switch to PASS report';
  }

  getSelectedStationLabel(): string {
    if (!this.selectedStationCode) {
      return '-';
    }

    const matched = this.stations.find((s) => s.station_code === this.selectedStationCode);
    if (!matched) {
      return this.selectedStationCode;
    }

    return `${matched.station_code} - ${matched.station_desc}`;
  }

  private loadStations(): void {
    this.stationsLoading = true;

    const params = new HttpParams().set('limit', 'all').set('page', '1');
    this.http.get<StationsResponse>(this.stationsApi, { params }).subscribe({
      next: (response) => {
        this.stations = (response.data || []).filter((s) => s.status === 'Active');
        this.stationsLoading = false;
      },
      error: () => {
        this.stationsLoading = false;
        this.stations = [];
      }
    });
  }

  private focusScanInput(): void {
    if (!this.scanInputRef?.nativeElement) {
      return;
    }

    window.setTimeout(() => {
      this.scanInputRef.nativeElement.focus();
      this.scanInputRef.nativeElement.select();
    }, 0);
  }

  private getCurrentOperator(): string {
    try {
      const rawUser = localStorage.getItem('mes_auth_user');
      if (!rawUser) {
        return 'system';
      }

      const parsed = JSON.parse(rawUser) as {
        user_name?: string;
        login_id?: string;
        username?: string;
      };

      return (parsed.user_name || parsed.login_id || parsed.username || 'system').toString().trim() || 'system';
    } catch {
      return 'system';
    }
  }

  private getClientPcName(): string {
    const platform = (window.navigator.platform || 'WEB').toString().trim();
    const host = (window.location.hostname || 'CLIENT').toString().trim();
    return `${platform}@${host}`;
  }
}
