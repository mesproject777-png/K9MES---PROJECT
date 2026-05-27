import { Component, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { environment } from '../../../../environments/environment';

interface GeneratedSerialRow {
  sn: string;
  rsn: string;
}

@Component({
  selector: 'app-generatesn',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './generatesn.component.html',
  styleUrls: ['./generatesn.component.scss']
})
export class GenerateSnComponent implements OnInit {
  wo: string = '';
  quantity: number = 0;
  woDetails: any = null;
  sns: string[] = [];
  serialRows: GeneratedSerialRow[] = [];
  loading = false;
  message = '';
  success = false;
  validating = false;

  constructor(
    private http: HttpClient,
    private router: Router
  ) {}

  ngOnInit() {}

  validateWo() {
    if (!this.wo.trim()) {
      this.woDetails = null;
      return;
    }

    this.validating = true;
    this.http.get(`${environment.apiUrl}/api/generate-sn/work-orders?wo=${encodeURIComponent(this.wo)}`).subscribe({
      next: (response: any) => {
        const data = response.data || response;
        this.woDetails = data[0] || null;
        if (this.woDetails) {
          this.quantity = this.woDetails.balance || this.woDetails.qty;
          this.success = true;
          this.message = 'WO valid. Qty set.';
        } else {
          this.success = false;
          this.message = 'WO not found or balance = 0';
        }
      },
      error: () => {
        this.success = false;
        this.message = 'Validation failed';
      },
      complete: () => this.validating = false
    });
  }

  get isValid(): boolean {
    return !!this.woDetails && this.quantity === this.woDetails.balance && this.quantity > 0;
  }

  generate() {
    this.loading = true;
    this.message = '';

    this.http.post(`${environment.apiUrl}/api/generate-sn/generate`, {
      wo: this.wo.trim(),
      qty: this.quantity
    }).subscribe({
      next: (response: any) => {
        this.serialRows = response.serials || [];
        this.sns = this.serialRows.length
          ? this.serialRows.map((row) => row.sn)
          : (response.sns || []);

        this.message = `Generated ${this.sns.length} SNs!`;
        this.success = true;
        this.loading = false;
      },
      error: (err) => {
        this.message = err.error?.message || 'Generation failed';
        this.success = false;
        this.loading = false;
      }
    });
  }

  copyAllSns() {
    const text = this.serialRows.length
      ? this.serialRows.map((row) => `${row.sn} | ${row.rsn}`).join('\n')
      : this.sns.join('\n');

    navigator.clipboard.writeText(text).then(() => {
      this.message = this.serialRows.length ? 'SN + RSN copied!' : 'SNs copied!';
      this.success = true;
    });
  }

  getRsnAt(index: number): string {
    if (index < 0 || index >= this.serialRows.length) {
      return '';
    }

    return this.serialRows[index].rsn || '';
  }

  getTrackSearchValue(index: number, sn: string): string {
    return this.getRsnAt(index) || sn;
  }

  openTracker(searchValue?: string): void {
    const value = String(searchValue || '').trim() || this.getFirstTraceValue();
    if (!value) {
      return;
    }

    this.router.navigate(['/dashboard/manager/sntracker'], {
      queryParams: { q: value }
    });
  }

  private getFirstTraceValue(): string {
    if (this.serialRows.length > 0) {
      return this.serialRows[0].rsn || this.serialRows[0].sn || '';
    }

    return this.sns[0] || '';
  }
}

