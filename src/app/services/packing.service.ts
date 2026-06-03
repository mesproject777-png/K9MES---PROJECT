import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export type PackageStatus = 'OPEN' | 'CLOSED' | 'SHIPPED';
export type PackageType = 'BOX' | 'SHIPMENT' | 'MULTIBOX';
export type PackageSource = 'PACKAGE' | 'MULTIBOX';

export interface PackingPackageSummary {
  id: number;
  package_no: string;
  package_type: PackageType;
  status: PackageStatus;
  source?: PackageSource;
  created_by: string;
  created_at: string;
  item_count: number;
  closed_by?: string | null;
  closed_at?: string | null;
  shipped_by?: string | null;
  shipped_at?: string | null;
}

export interface PackingPackageItem {
  id: number;
  sn: string;
  rsn: string;
  serial_status: string;
  condition: string;
  pn: string;
  revision: string;
  added_by: string;
  added_at: string;
}

export interface PackingPackageDetailsResponse {
  package: {
    id: number;
    package_no: string;
    package_type: PackageType;
    status: PackageStatus;
    source?: PackageSource;
    remark?: string | null;
    created_by: string;
    created_at: string;
    updated_at: string;
    closed_by?: string | null;
    closed_at?: string | null;
    shipped_by?: string | null;
    shipped_at?: string | null;
  };
  items: PackingPackageItem[];
}

export interface PackingHierarchyRow {
  serial_id: number;
  sn: string;
  rsn: string;
  serial_status: string;
  condition: string;
  pn: string;
  wo: string;
  multibox_no?: string | null;
  multibox_status?: string | null;
  pallet_no?: string | null;
  pallet_status?: string | null;
  shipment_no?: string | null;
  shipment_status?: string | null;
  last_packed_at?: string | null;
}

@Injectable({
  providedIn: 'root'
})
export class PackingService {
  private apiUrl = `${environment.apiUrl}/api/packing`;

  constructor(private http: HttpClient) {}

  listOpen(): Observable<{ data: PackingPackageSummary[] }> {
    return this.http.get<{ data: PackingPackageSummary[] }>(`${this.apiUrl}/open`);
  }

  listClosed(): Observable<{ data: PackingPackageSummary[] }> {
    return this.http.get<{ data: PackingPackageSummary[] }>(`${this.apiUrl}/closed`);
  }

  listShipped(): Observable<{ data: PackingPackageSummary[] }> {
    return this.http.get<{ data: PackingPackageSummary[] }>(`${this.apiUrl}/shipped`);
  }

  listHierarchy(query = ''): Observable<{ data: PackingHierarchyRow[] }> {
    const url = query.trim()
      ? `${this.apiUrl}/hierarchy?query=${encodeURIComponent(query.trim())}`
      : `${this.apiUrl}/hierarchy`;
    return this.http.get<{ data: PackingHierarchyRow[] }>(url);
  }

  createPackage(packageType: PackageType, changedBy: string): Observable<{ data: PackingPackageSummary }> {
    return this.http.post<{ data: PackingPackageSummary }>(`${this.apiUrl}/create`, {
      package_type: packageType,
      changed_by: changedBy,
    });
  }

  getPackageDetails(packageId: number): Observable<PackingPackageDetailsResponse> {
    return this.http.get<PackingPackageDetailsResponse>(`${this.apiUrl}/${packageId}`);
  }

  lookupMultibox(boxNo: string): Observable<PackingPackageDetailsResponse> {
    return this.http.get<PackingPackageDetailsResponse>(`${this.apiUrl}/multibox/${encodeURIComponent(boxNo)}`);
  }

  addToPackage(packageId: number, query: string, changedBy: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/${packageId}/add`, {
      query,
      changed_by: changedBy,
    });
  }

  closePackage(packageId: number, changedBy: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/${packageId}/close`, {
      changed_by: changedBy,
    });
  }

  shipPackage(packageId: number, changedBy: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/${packageId}/ship`, {
      changed_by: changedBy,
    });
  }
}
