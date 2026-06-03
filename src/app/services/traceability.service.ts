import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface TraceSerial {
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
  multibox_no?: string | null;
  pallet_no?: string | null;
  shipment_no?: string | null;
}

export interface TraceDevice {
  product_line: string;
  pn: string;
  revision: string;
  work_order: string;
  work_order_status: string;
  work_order_qty: number;
  work_order_balance: number;
  plant?: string;
  site: string;
  description: string;
}

export interface TraceProgress {
  total: number;
  completed: number;
  current: number;
  pending: number;
  percent: number;
}

export interface TraceRouteStep {
  station_order: number;
  station_code: string;
  station_name: string;
  sample_mode: string;
  report_mode: string;
  state: 'completed' | 'current' | 'pending';
  is_current: boolean;
}

export interface TraceHistoryRow {
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

export interface TraceSearchResponse {
  query: string;
  matched_by: 'SN' | 'RSN';
  serial: TraceSerial;
  device: TraceDevice;
  progress: TraceProgress;
  routing: TraceRouteStep[];
  history: TraceHistoryRow[];
  generated_at: string;
}

@Injectable({
  providedIn: 'root',
})
export class TraceabilityService {
  private readonly traceabilityApi = `${environment.apiUrl}/api/traceability/search`;

  constructor(private http: HttpClient) {}

  search(query: string): Observable<TraceSearchResponse> {
    const params = new HttpParams().set('query', query);
    return this.http.get<TraceSearchResponse>(this.traceabilityApi, { params });
  }
}
