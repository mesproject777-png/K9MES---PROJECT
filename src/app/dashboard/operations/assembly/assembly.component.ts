import { HttpClient, HttpParams } from '@angular/common/http';
import { Component } from '@angular/core';
import { AuthService } from '../../../services/auth.service';
import { environment } from '../../../../environments/environment';

interface TraceSerial {
  sn: string;
  rsn: string;
  status?: string;
  condition?: string;
  current_station_code: string | null;
  current_station_name: string | null;
}

interface TraceRoutingStep {
  station_order: number;
  station_code: string;
  station_name: string;
  is_current: boolean;
}

interface TraceDevice {
  pn: string;
  revision: string;
}

interface TraceSearchResponse {
  query: string;
  matched_by: 'SN' | 'RSN';
  serial: TraceSerial;
  device: TraceDevice;
  routing: TraceRoutingStep[];
}

interface AssemblyOperationLine {
  id: number;
  son_item_id: number;
  son_item_revision_id: number | null;
  son_pn: string;
  son_description: string;
  son_rev: string;
  station_code: string;
  station_name: string;
  assemble_order: number;
}

interface AssemblyOperationBinding {
  id: number;
  child_sn: string;
  child_rsn: string;
  child_pn: string;
  child_revision: string;
  created_at: string;
}

interface AssemblyOperationStatusResponse {
  parent: {
    id: number;
    sn: string;
    rsn: string;
    pn: string;
    revision: string;
    station_code: string;
  };
  required: AssemblyOperationLine[];
  bindings: AssemblyOperationBinding[];
  requiredTotal: number;
  boundTotal: number;
  remaining: number;
}

interface AssemblyBindResponse {
  message: string;
  requiredTotal: number;
  boundTotal: number;
  remaining: number;
}

@Component({
  selector: 'app-operations-assembly',
  standalone: false,
  templateUrl: './assembly.component.html',
  styleUrl: './assembly.component.scss'
})
export class OperationsAssemblyComponent {
  readonly traceabilityApi = `${environment.apiUrl}/api/traceability/search`;
  readonly passFailApi = `${environment.apiUrl}/api/traceability/pass-fail`;
  readonly assemblyOpsStatusApi = `${environment.apiUrl}/api/assembly/operations/status`;
  readonly assemblyOpsBindApi = `${environment.apiUrl}/api/assembly/operations/bind`;

  query = '';
  selectedStage = '';
  readonly stageOptions = [
    'ASM01',
    'ASM02',
    'ASM03',
    'ASM04',
    'ASM05',
    'ASM06',
    'ASM07',
    'ASM08',
    'ASM09',
    'ASM10',
    'ASM11',
    'ASM12',
    'ASM13',
    'ASM14',
    'ASM15',
    'BOXING01',
  ];
  isLoading = false;
  errorMessage = '';
  successMessage = '';

  parentContext: AssemblyOperationStatusResponse['parent'] | null = null;
  requiredTotal = 0;
  boundTotal = 0;
  remaining = 0;

  constructor(
    private http: HttpClient,
    private authService: AuthService
  ) {}

  private getChangedBy(): string {
    return this.authService.getCurrentUser()?.user_name
      || this.authService.getCurrentUser()?.login_id
      || 'WEB-CLIENT';
  }

  onSubmit(): void {
    const scanned = this.query.trim();
    this.query = '';

    this.successMessage = '';
    this.errorMessage = '';

    if (!scanned) {
      this.errorMessage = 'Please scan SN.';
      return;
    }

    if (!/^[A-Za-z0-9_-]+$/.test(scanned)) {
      this.errorMessage = 'SN supports only letters/numbers, dash and underscore.';
      return;
    }

    if (!this.parentContext) {
      this.setParentFromScan(scanned);
      return;
    }

    const parentSn = String(this.parentContext.sn || '').trim();
    const parentRsn = String(this.parentContext.rsn || '').trim();

    if (scanned.toUpperCase() === parentSn.toUpperCase() || (parentRsn && scanned.toUpperCase() === parentRsn.toUpperCase())) {
      this.completeAssembly();
      return;
    }

    this.bindChild(scanned);
  }

  resetSession(clearMessages = true): void {
    this.parentContext = null;
    this.requiredTotal = 0;
    this.boundTotal = 0;
    this.remaining = 0;

    if (clearMessages) {
      this.successMessage = '';
      this.errorMessage = '';
    }
  }

  private setParentFromScan(parentQuery: string): void {
    const stage = String(this.selectedStage || '').trim().toUpperCase();
    if (!stage) {
      this.errorMessage = 'Please select Assembly Stage first.';
      return;
    }

    this.isLoading = true;

    const traceParams = new HttpParams().set('query', parentQuery);
    this.http.get<TraceSearchResponse>(this.traceabilityApi, { params: traceParams }).subscribe({
      next: (trace) => {
        const serialStatus = String(trace?.serial?.status || '').trim().toUpperCase();
        if (serialStatus === 'COMPLETED') {
          this.isLoading = false;
          this.errorMessage = 'SN is already Completed. Assembly cannot be done.';
          return;
        }

        if (serialStatus === 'FAILED') {
          this.isLoading = false;
          this.errorMessage = 'SN status is Failed. Assembly cannot be done.';
          return;
        }

        const routing = Array.isArray(trace?.routing) ? trace.routing : [];
        const currentStep = routing.find((step) => step.is_current) || null;
        const currentCode = String(currentStep?.station_code || trace?.serial?.current_station_code || '').trim().toUpperCase();

        if (!currentCode) {
          this.isLoading = false;
          this.errorMessage = 'Unable to determine current routing station for this SN.';
          return;
        }

        const stageInRoute = routing.some(
          (step) => String(step.station_code || '').trim().toUpperCase() === stage
        );

        if (!stageInRoute) {
          this.isLoading = false;
          this.errorMessage = `Selected stage ${stage} is not in this SN routing.`;
          return;
        }

        if (currentCode !== stage) {
          this.isLoading = false;
          this.errorMessage = `Current station is ${currentCode}. Please select current station first.`;
          return;
        }

        const statusParams = new HttpParams().set('parent_query', parentQuery);
        this.http.get<AssemblyOperationStatusResponse>(this.assemblyOpsStatusApi, { params: statusParams }).subscribe({
          next: (status) => {
            this.isLoading = false;
            this.parentContext = status.parent;
            this.requiredTotal = status.requiredTotal;
            this.boundTotal = status.boundTotal;
            this.remaining = status.remaining;

            this.successMessage = `Parent set: ${status.parent.sn} @ ${status.parent.station_code}. Scan child SN(s).`;
          },
          error: (error) => {
            this.isLoading = false;
            this.parentContext = null;
            this.errorMessage = error?.error?.message || 'Unable to start Assembly session.';
          }
        });
      },
      error: (error) => {
        this.isLoading = false;
        this.parentContext = null;
        this.errorMessage = error?.error?.message || 'SN not found.';
      }
    });
  }

  private bindChild(childQuery: string): void {
    if (!this.parentContext) {
      return;
    }

    this.isLoading = true;
    const changedBy = this.getChangedBy();

    const payload = {
      parent_query: this.parentContext.sn,
      child_query: childQuery,
      station_code: String(this.selectedStage || this.parentContext.station_code || '').trim().toUpperCase(),
      changed_by: changedBy,
    };

    this.http.post<AssemblyBindResponse>(this.assemblyOpsBindApi, payload).subscribe({
      next: (response) => {
        this.requiredTotal = response.requiredTotal;
        this.boundTotal = response.boundTotal;
        this.remaining = response.remaining;

        this.successMessage = response.message;
        if (this.remaining === 0) {
          this.successMessage = `${response.message}. All required parts scanned. Saving PASS for this stage...`;
          this.completeAssembly(true);
          return;
        }

        this.isLoading = false;
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to bind child SN.';
      }
    });
  }

  private completeAssembly(isAuto = false): void {
    if (!this.parentContext) {
      return;
    }

    if (this.remaining > 0) {
      this.errorMessage = `Scan remaining child SN(s) first. Remaining: ${this.remaining}.`;
      return;
    }

    this.isLoading = true;
    const changedBy = this.getChangedBy();

    const stage = String(this.selectedStage || this.parentContext.station_code || '')
      .trim()
      .toUpperCase();

    if (!stage) {
      this.isLoading = false;
      this.errorMessage = 'Please select Assembly Stage first.';
      return;
    }

    const payload = {
      query: this.parentContext.sn,
      station_code: stage,
      result: 'PASS',
      remark: 'Assembly completed',
      changed_by: changedBy,
    };

    this.http.post(this.passFailApi, payload).subscribe({
      next: () => {
        this.isLoading = false;
        this.successMessage = isAuto
          ? `Assembly completed. PASS saved for ${stage} and SN moved to next station.`
          : `PASS saved for ${stage} and SN moved to next station.`;
        this.resetSession(false);
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to PASS this station.';
      }
    });
  }
}
