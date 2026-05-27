import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { environment } from '../../../../environments/environment';

interface TransferResponse {
  success: boolean;
  transferred_count: number;
  source_wo: string;
  target_wo: string;
  serials: string[];
}

@Component({
  selector: 'app-pnwochange',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './pnwochange.component.html',
  styleUrls: ['./pnwochange.component.scss']
})
export class PnwochangeComponent {
  sourceWo = '';
  targetWo = '';
  serialNumber = '';

  loadingAll = false;
  loadingSingle = false;
  success = false;
  message = '';
  lastTransfer: TransferResponse | null = null;

  constructor(private http: HttpClient) {}

  transferAllNew(): void {
    if (!this.isWoPairValid) {
      this.success = false;
      this.message = 'Source WO and Target WO are required and cannot be the same.';
      return;
    }

    this.loadingAll = true;
    this.resetMessage();

    this.http.post<TransferResponse>(`${environment.apiUrl}/api/work-orders/transfer`, {
      source_wo: this.sourceWo.trim(),
      target_wo: this.targetWo.trim(),
      mode: 'all-new'
    }).subscribe({
      next: (response) => {
        this.lastTransfer = response;
        this.success = true;
        this.message = `Transferred ${response.transferred_count} New SN(s) from ${response.source_wo} to ${response.target_wo}.`;
        this.loadingAll = false;
      },
      error: (error) => {
        this.success = false;
        this.message = error?.error?.message || 'Unable to transfer New SNs.';
        this.loadingAll = false;
      }
    });
  }

  transferSingleSn(): void {
    if (!this.isWoPairValid) {
      this.success = false;
      this.message = 'Source WO and Target WO are required and cannot be the same.';
      return;
    }

    if (!this.serialNumber.trim()) {
      this.success = false;
      this.message = 'SN is required for single transfer.';
      return;
    }

    this.loadingSingle = true;
    this.resetMessage();

    this.http.post<TransferResponse>(`${environment.apiUrl}/api/work-orders/transfer`, {
      source_wo: this.sourceWo.trim(),
      target_wo: this.targetWo.trim(),
      mode: 'single',
      sn: this.serialNumber.trim(),
    }).subscribe({
      next: (response) => {
        this.lastTransfer = response;
        this.success = true;
        this.message = `Transferred SN ${response.serials[0] || this.serialNumber.trim()} from ${response.source_wo} to ${response.target_wo}.`;
        this.loadingSingle = false;
      },
      error: (error) => {
        this.success = false;
        this.message = error?.error?.message || 'Unable to transfer SN.';
        this.loadingSingle = false;
      }
    });
  }

  clearForm(): void {
    this.sourceWo = '';
    this.targetWo = '';
    this.serialNumber = '';
    this.message = '';
    this.lastTransfer = null;
  }

  get isWoPairValid(): boolean {
    const source = this.sourceWo.trim();
    const target = this.targetWo.trim();
    return !!source && !!target && source !== target;
  }

  private resetMessage(): void {
    this.message = '';
    this.lastTransfer = null;
  }
}
