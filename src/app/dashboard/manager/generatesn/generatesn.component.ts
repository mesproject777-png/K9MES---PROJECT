import { Component, OnDestroy, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { environment } from '../../../../environments/environment';

interface GeneratedSerialRow {
  sn: string;
  rsn: string;
}

interface WorkflowWoDetails {
  wo: string;
  qty: number;
  balance: number;
  generated_qty: number;
  pn: string;
  sn_type_name: string;
  site_name: string;
  plant?: string;
  due_date?: string;
  revision?: string;
  serials?: GeneratedSerialRow[];
}

interface WorkflowWoSuggestion {
  wo: string;
  pn?: string;
  qty?: number;
  sn_type_name?: string;
  site_name?: string;
}

@Component({
  selector: 'app-generatesn',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './generatesn.component.html',
  styleUrls: ['./generatesn.component.scss']
})
export class GenerateSnComponent implements OnInit, OnDestroy {
  wo: string = '';
  woDetails: WorkflowWoDetails | null = null;
  sns: string[] = [];
  serialRows: GeneratedSerialRow[] = [];
  loading = false;
  message = '';
  success = false;
  validating = false;
  woSuggestions: WorkflowWoSuggestion[] = [];
  private lookupTimer: number | null = null;
  private lookupRequestId = 0;

  constructor(
    private http: HttpClient,
    private router: Router
  ) {}

  ngOnInit() {
    const selectedWo = String(history.state?.wo || '').trim();
    if (selectedWo) {
      this.wo = selectedWo;
      this.validateWo(true);
    }
  }

  ngOnDestroy() {
    if (this.lookupTimer) {
      window.clearTimeout(this.lookupTimer);
    }
  }

  onWoInput(value: string) {
    this.wo = value;
    this.lookupRequestId++;
    this.woDetails = null;
    this.sns = [];
    this.serialRows = [];

    if (this.lookupTimer) {
      window.clearTimeout(this.lookupTimer);
    }

    if (!value.trim()) {
      this.message = '';
      this.validating = false;
      this.woSuggestions = [];
      return;
    }

    this.lookupTimer = window.setTimeout(() => {
      this.lookupTimer = null;
      this.validateWo(false);
    }, 350);
  }

  validateWo(showMissingMessage = true) {
    if (this.lookupTimer) {
      window.clearTimeout(this.lookupTimer);
      this.lookupTimer = null;
    }

    const lookupWo = this.wo.trim();
    if (!lookupWo) {
      this.woDetails = null;
      this.sns = [];
      this.serialRows = [];
      this.woSuggestions = [];
      return;
    }

    const requestId = ++this.lookupRequestId;
    this.validating = true;
    this.http.get(`${environment.apiUrl}/api/generate-sn/work-orders?wo=${encodeURIComponent(lookupWo)}`).subscribe({
      next: (response: any) => {
        if (!this.isCurrentLookup(requestId, lookupWo)) {
          return;
        }

        const data = Array.isArray(response?.data)
          ? response.data
          : (Array.isArray(response) ? response : []);
        const suggestions = Array.isArray(response?.suggestions) ? response.suggestions : data;
        this.woSuggestions = suggestions
          .filter((row: WorkflowWoSuggestion) => this.startsWithTypedWo(row.wo, lookupWo))
          .slice(0, 10);

        this.woDetails = data.find((row: WorkflowWoDetails) => this.isSameWo(row.wo, lookupWo)) || null;
        if (this.woDetails) {
          this.wo = this.woDetails.wo;
          this.serialRows = this.woDetails.serials || [];
          this.sns = this.serialRows.map((row) => row.sn);
          this.success = true;
          this.message = this.getWoStatusMessage();
        } else {
          this.success = false;
          this.message = showMissingMessage ? 'WO not found in workflow work orders.' : '';
          this.sns = [];
          this.serialRows = [];
        }
      },
      error: () => {
        if (!this.isCurrentLookup(requestId, lookupWo)) {
          return;
        }

        this.success = false;
        this.message = showMissingMessage ? 'Validation failed' : '';
      },
      complete: () => {
        if (this.isCurrentLookup(requestId, lookupWo)) {
          this.validating = false;
        }
      }
    });
  }

  get isValid(): boolean {
    return !!this.woDetails && this.woDetails.balance > 0;
  }

  generate() {
    this.loading = true;
    this.message = '';

    this.http.post(`${environment.apiUrl}/api/generate-sn/generate`, {
      wo: this.wo.trim()
    }).subscribe({
      next: (response: any) => {
        this.serialRows = response.serials || [];
        this.sns = this.serialRows.length
          ? this.serialRows.map((row) => row.sn)
          : (response.sns || []);

        this.message = `Generated ${this.sns.length} SNs!`;
        this.success = true;
        this.loading = false;
        this.validateWo(false);
      },
      error: (err) => {
        this.message = err.error?.message || 'Generation failed';
        this.success = false;
        this.loading = false;
      }
    });
  }

  async copyAllSns(): Promise<void> {
    const text = this.sns.join('\n');
    if (!text) {
      this.message = 'No SNs available to copy.';
      this.success = false;
      return;
    }

    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
      } else {
        this.copyTextWithFallback(text);
      }
      this.message = 'SNs copied!';
      this.success = true;
    } catch {
      try {
        this.copyTextWithFallback(text);
        this.message = 'SNs copied!';
        this.success = true;
      } catch {
        this.message = 'Unable to copy SNs. Please select and copy manually.';
        this.success = false;
      }
    }
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
    return this.sns[0] || '';
  }

  selectWoSuggestion(suggestion: WorkflowWoSuggestion): void {
    if (!suggestion?.wo) {
      return;
    }

    this.wo = suggestion.wo;
    this.woSuggestions = [];
    this.validateWo(true);
  }

  clearForm(): void {
    if (this.lookupTimer) {
      window.clearTimeout(this.lookupTimer);
      this.lookupTimer = null;
    }

    this.lookupRequestId++;
    this.woDetails = null;
    this.wo = '';
    this.message = '';
    this.sns = [];
    this.serialRows = [];
    this.woSuggestions = [];
    this.validating = false;
  }

  private getWoStatusMessage(): string {
    if (!this.woDetails) {
      return '';
    }

    if (this.woDetails.generated_qty > 0 && this.woDetails.balance <= 0) {
      return `All ${this.woDetails.generated_qty} SNs are already generated for this WO.`;
    }

    if (this.woDetails.generated_qty > 0) {
      return `${this.woDetails.generated_qty} SNs already generated. ${this.woDetails.balance} SNs available to generate.`;
    }

    return `WO valid. ${this.woDetails.balance} SNs available to generate.`;
  }

  private isSameWo(left: string | undefined, right: string): boolean {
    return String(left || '').trim().toUpperCase() === right.trim().toUpperCase();
  }

  private startsWithTypedWo(value: string | undefined, typedWo: string): boolean {
    return String(value || '').trim().toUpperCase().startsWith(typedWo.trim().toUpperCase());
  }

  private isCurrentLookup(requestId: number, lookupWo: string): boolean {
    return requestId === this.lookupRequestId && this.isSameWo(this.wo, lookupWo);
  }

  private copyTextWithFallback(text: string): void {
    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.setAttribute('readonly', '');
    textarea.style.position = 'fixed';
    textarea.style.left = '-9999px';
    textarea.style.top = '0';
    document.body.appendChild(textarea);
    textarea.focus();
    textarea.select();

    const copied = document.execCommand('copy');
    document.body.removeChild(textarea);
    if (!copied) {
      throw new Error('copy failed');
    }
  }
}
