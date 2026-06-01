import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface LabelMasterDto {
  id: number;
  label_code: string;
  label_description: string;
  status: 'Active' | 'Inactive';
  created_at: string;
  updated_at: string;
}

export interface LabelPrnTemplateDto {
  id: number;
  label_master_id: number;
  prn_file_name: string;
  prn_content: string;
  preview_data?: string | null;
  version: number;
  created_at: string;
  updated_at: string;
}

export interface LabelDetailResponse {
  label: LabelMasterDto;
  prn_template?: LabelPrnTemplateDto | null;
}

@Injectable({
  providedIn: 'root',
})
export class LabelService {
  private readonly apiUrl = `${environment.apiUrl}/api/labels`;

  constructor(private http: HttpClient) {}

  getLabels(): Observable<{ data: LabelMasterDto[] }> {
    return this.http.get<{ data: LabelMasterDto[] }>(this.apiUrl);
  }

  getLabel(id: number): Observable<LabelDetailResponse> {
    return this.http.get<LabelDetailResponse>(`${this.apiUrl}/${id}`);
  }

  createLabel(payload: { label_code: string; label_description: string; status: 'Active' }): Observable<LabelMasterDto> {
    return this.http.post<LabelMasterDto>(this.apiUrl, payload);
  }

  updateLabel(id: number, payload: { label_code: string; label_description: string; status: 'Active' }): Observable<LabelMasterDto> {
    return this.http.put<LabelMasterDto>(`${this.apiUrl}/${id}`, payload);
  }

  savePrnTemplate(
    labelId: number,
    payload: { prn_file_name: string; prn_content: string; preview_data?: string | null }
  ): Observable<LabelPrnTemplateDto> {
    return this.http.post<LabelPrnTemplateDto>(`${this.apiUrl}/${labelId}/prn-template`, payload);
  }

  saveGeneration(labelId: number, payload: { rsn?: string | null; preview_data?: string | null }): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/${labelId}/generate`, payload);
  }
}
