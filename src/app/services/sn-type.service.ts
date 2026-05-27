import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject } from 'rxjs';
import { tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface SNTypeField {
  id?: number;
  sn_type_id?: number;
  sort_order: number;
  field_type: string;
  field_string?: string | null;
  field_size?: number | null;
  epv_type_id?: number | null;
  epv_sub_type_id?: number | null;
  epv_type_name?: string | null;
  epv_sub_type_name?: string | null;
  created_at?: string;
  updated_at?: string;
}

export interface SNType {
  id?: number;
  sn_type_name: string;
  remark?: string;
  fields?: SNTypeField[];
  number_of_fields?: number;
  created_at?: string;
  updated_at?: string;
}

export interface APIResponse<T> {
  data?: T[];
  total?: number;
}

export interface EPVUploadPayload {
  file_name: string;
  mime_type?: string;
  file_content_base64: string;
  epv_type_id?: number;
  epv_sub_type_id?: number;
}

export interface EPVUpload {
  id: number;
  sn_type_id: number;
  file_name: string;
  mime_type?: string;
  source_kind: string;
  record_count: number;
  epv_type_id?: number;
  epv_sub_type_id?: number;
  epv_type_name?: string;
  epv_sub_type_name?: string;
  created_at: string;
  first_value?: string;
}

export interface EPVUploadResponse {
  message: string;
  upload: EPVUpload;
  values_preview: string[];
  values_total: number;
}

export interface EPVType {
  id: number;
  type_name: string;
  regex_rule: string;
  created_at: string;
  updated_at: string;
}

export interface EPVSubType {
  id: number;
  epv_type_id: number;
  sub_type_name: string;
  regex_rule: string;
  created_at: string;
  updated_at: string;
}

export interface EPVRegexMasterRow {
  epv_type_id: number;
  type_name: string;
  type_regex_rule: string;
  epv_sub_type_id?: number;
  sub_type_name?: string;
  sub_type_regex_rule?: string;
  total_quantity?: number;
  used_quantity?: number;
  unused_quantity?: number;
  type_updated_at?: string;
  sub_type_updated_at?: string;
}

@Injectable({
  providedIn: 'root'
})
export class SnTypeService {
  private apiUrl = `${environment.apiUrl}/api/sn-types`;
  private epvTypeApiUrl = `${environment.apiUrl}/api/epv-types`;
  
  private snTypesSubject = new BehaviorSubject<SNType[]>([]);
  public snTypes$ = this.snTypesSubject.asObservable();
  
  private selectedSnTypeSubject = new BehaviorSubject<SNType | null>(null);
  public selectedSnType$ = this.selectedSnTypeSubject.asObservable();

  constructor(private http: HttpClient) {
    this.loadSNTypes();
  }

  // Load all SN Types
  loadSNTypes(): Observable<APIResponse<SNType>> {
    return this.http.get<APIResponse<SNType>>(this.apiUrl).pipe(
      tap(response => {
        this.snTypesSubject.next(response.data || []);
      })
    );
  }

  // Get all SN Types
  getSNTypes(): Observable<APIResponse<SNType>> {
    return this.http.get<APIResponse<SNType>>(this.apiUrl);
  }

  // Get SN Type by ID with fields
  getSNTypeById(id: number): Observable<SNType> {
    return this.http.get<SNType>(`${this.apiUrl}/${id}`).pipe(
      tap(snType => {
        this.selectedSnTypeSubject.next(snType);
      })
    );
  }

  // Create new SN Type
  createSNType(snType: SNType): Observable<SNType> {
    return this.http.post<SNType>(this.apiUrl, snType).pipe(
      tap(newSnType => {
        const currentSnTypes = this.snTypesSubject.value;
        this.snTypesSubject.next([...currentSnTypes, newSnType]);
      })
    );
  }

  // Update SN Type
  updateSNType(id: number, snType: SNType): Observable<SNType> {
    return this.http.put<SNType>(`${this.apiUrl}/${id}`, snType).pipe(
      tap(updatedSnType => {
        const currentSnTypes = this.snTypesSubject.value;
        const index = currentSnTypes.findIndex(s => s.id === id);
        if (index !== -1) {
          currentSnTypes[index] = updatedSnType;
          this.snTypesSubject.next([...currentSnTypes]);
        }
        if (this.selectedSnTypeSubject.value?.id === id) {
          this.selectedSnTypeSubject.next(updatedSnType);
        }
      })
    );
  }

  // Delete SN Type
  deleteSNType(id: number): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`).pipe(
      tap(() => {
        const currentSnTypes = this.snTypesSubject.value;
        this.snTypesSubject.next(currentSnTypes.filter(s => s.id !== id));
        if (this.selectedSnTypeSubject.value?.id === id) {
          this.selectedSnTypeSubject.next(null);
        }
      })
    );
  }

  // Add field to SN Type
  addField(snTypeId: number, field: SNTypeField): Observable<SNTypeField> {
    return this.http.post<SNTypeField>(`${this.apiUrl}/${snTypeId}/fields`, field);
  }

  // Update SN Type field
  updateField(fieldId: number, field: SNTypeField): Observable<SNTypeField> {
    return this.http.put<SNTypeField>(`${this.apiUrl}/fields/${fieldId}`, field);
  }

  // Delete SN Type field
  deleteField(fieldId: number): Observable<any> {
    return this.http.delete(`${this.apiUrl}/fields/${fieldId}`);
  }

  // Get allowed field types
  getFieldTypes(): Observable<any> {
    return this.http.get(`${this.apiUrl}/reference/field-types`);
  }

  // Get current selected SN Type
  getSelectedSnType(): SNType | null {
    return this.selectedSnTypeSubject.value;
  }

  uploadEPVFile(snTypeId: number, payload: EPVUploadPayload): Observable<EPVUploadResponse> {
    return this.http.post<EPVUploadResponse>(`${this.apiUrl}/${snTypeId}/epv-upload`, payload);
  }

  getEPVUploads(snTypeId: number): Observable<{ data: EPVUpload[] }> {
    return this.http.get<{ data: EPVUpload[] }>(`${this.apiUrl}/${snTypeId}/epv-uploads`);
  }

  getEPVTypes(): Observable<{ data: EPVType[] }> {
    return this.http.get<{ data: EPVType[] }>(this.epvTypeApiUrl);
  }

  getEPVRegexMaster(): Observable<{ data: EPVRegexMasterRow[] }> {
    return this.http.get<{ data: EPVRegexMasterRow[] }>(`${this.epvTypeApiUrl}/regex-master`);
  }

  createEPVType(payload: { type_name: string; regex_rule: string }): Observable<EPVType> {
    return this.http.post<EPVType>(this.epvTypeApiUrl, payload);
  }

  deleteEPVType(typeId: number): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.epvTypeApiUrl}/${typeId}`);
  }

  getEPVSubTypes(typeId: number): Observable<{ type: EPVType; data: EPVSubType[] }> {
    return this.http.get<{ type: EPVType; data: EPVSubType[] }>(`${this.epvTypeApiUrl}/${typeId}/sub-types`);
  }

  createEPVSubType(typeId: number, payload: { sub_type_name: string; regex_rule: string }): Observable<EPVSubType> {
    return this.http.post<EPVSubType>(`${this.epvTypeApiUrl}/${typeId}/sub-types`, payload);
  }

  deleteEPVSubType(subTypeId: number): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.epvTypeApiUrl}/sub-types/${subTypeId}`);
  }

  // Set selected SN Type
  setSelectedSnType(snType: SNType | null): void {
    this.selectedSnTypeSubject.next(snType);
  }
}
