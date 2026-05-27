import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export type PackageStatus = 'OPEN' | 'CLOSED' | 'SHIPPED';
export type PackageType = 'BOX' | 'SHIPMENT';

export interface PackingPackageSummary {
  id: number;
  package_no: string;
  package_type: PackageType;
  status: PackageStatus;
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

  createPackage(packageType: PackageType, changedBy: string): Observable<{ data: PackingPackageSummary }> {
    return this.http.post<{ data: PackingPackageSummary }>(`${this.apiUrl}/create`, {
      package_type: packageType,
      changed_by: changedBy,
    });
  }

  getPackageDetails(packageId: number): Observable<PackingPackageDetailsResponse> {
    return this.http.get<PackingPackageDetailsResponse>(`${this.apiUrl}/${packageId}`);
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
