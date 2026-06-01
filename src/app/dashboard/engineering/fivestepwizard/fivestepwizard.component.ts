import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { HttpClient, HttpParams } from '@angular/common/http';
import { environment } from '../../../../environments/environment';

type WizardStep = {
  label: string;
};

interface Site {
  id: number;
  name: string;
}

interface ProductLine {
  id: number;
  code: string;
  description: string;
  status: string;
}

interface BomComponentRow {
  childPartNumber: string;
  childName: string;
  quantity: number;
  unit: string;
  station: string;
  remarks: string;
}

interface WorkflowWorkOrder {
  wo: string;
  siteId: number | null;
  revision: string;
  plant: string;
  status: string;
}

@Component({
  selector: 'app-fivestepwizard',
  standalone: false,
  templateUrl: './fivestepwizard.component.html',
  styleUrl: './fivestepwizard.component.scss'
})
export class FivestepwizardComponent implements OnInit {
  readonly steps: WizardStep[] = [
    { label: 'PartNumber' },
    { label: 'WorkOrder' },
    { label: 'Routing' },
    { label: 'BOM' },
    { label: 'Preview' }
  ];

  activeStep = 0;
  completedSteps: Set<number> = new Set();
  workflowParentPn = '';
  workflowParentDescription = '';
  workflowParentType = '';
  workflowWorkOrder: WorkflowWorkOrder | null = null;
  workflowRoutingSteps: Array<any> = [];
  readonly fallbackPlants = ['Electronics Plant', 'Assembly Plant', 'Main Plant', 'Product Plant'];

  // PartNumber Modal
  showPartNumberModal = false;
  partNumberForm: FormGroup;
  isPartNumberSubmitting = false;
  partNumberMessage = '';
  partNumberErrorMessage = '';
  private readonly pnTypesApi = `${environment.apiUrl}/api/users/pn-types`;

  // WorkOrder Modal
  showWorkOrderModal = false;
  workOrderForm: FormGroup;
  isWorkOrderSubmitting = false;
  workOrderMessage = '';
  workOrderErrorMessage = '';
  sites: Site[] = [];
  pnSuggestions: any[] = [];
  pnQuery = '';
  private lookupTimer: number | null = null;
  private readonly workOrdersApi = `${environment.apiUrl}/api/work-orders`;
  private readonly itemRevisionsApi = `${environment.apiUrl}/api/item-revisions`;
  private readonly sitesApi = `${environment.apiUrl}/api/sites`;
  private readonly productLinesApi = `${environment.apiUrl}/api/users/product-lines`;
  plantOptions: string[] = [];
  // Routing
  showRoutingModal = false;
  routingApiBase = `${environment.apiUrl}/api/routing`;
  stations: Array<{ id: number; station_code: string; station_desc: string; status: string }> = [];
  isStationsLoading = false;
  selectedRoutingItem: { id: number; pn: string; description?: string } | null = null;
  routeSteps: Array<any> = [];
  showAddRoutingRow = false;
  routingForm: FormGroup;
  routingError = '';
  routingSuccess = '';
  addRoutingForm: FormGroup;

  // BOM
  showBomModal = false;
  showAddBomComponent = false;
  bomComponentForm: FormGroup;
  bomComponents: BomComponentRow[] = [];
  bomMessage = '';
  bomError = '';
  showStationRulesModal = false;
  selectedRuleStationName = '';
  selectedStationRules: string[] = [];

  // Preview
  showPreviewModal = false;
  showFinalReportModal = false;
  dispatchFlowApproved = false;
  previewMessage = '';
  previewError = '';

  constructor(
    private fb: FormBuilder,
    private http: HttpClient
  ) {
    this.partNumberForm = this.fb.group({
      type: ['', Validators.required],
      code: ['', Validators.required],
      description: ['', Validators.required],
      status: ['Active', Validators.required],
    });

    const today = new Date().toISOString().slice(0, 10);
    this.workOrderForm = this.fb.group({
      wo: ['', Validators.required],
      site_id: [null, Validators.required],
      plant: ['', Validators.required],
      due_date: [today, Validators.required],
      qty: [null, [Validators.required, Validators.min(1)]],
      status: ['Released', Validators.required],
      pn: ['', Validators.required],
      revision: ['', Validators.required],
      lot: [''],
    });
    this.routingForm = this.fb.group({
      pn: [''],
    });

    this.addRoutingForm = this.fb.group({
      station_code: ['', Validators.required],
      sample_mode: ['Full', Validators.required],
      report_mode: ['Regular', Validators.required],
    });

    this.bomComponentForm = this.fb.group({
      childPartNumber: ['', Validators.required],
      childName: ['', Validators.required],
      quantity: [1, [Validators.required, Validators.min(0.001)]],
      unit: ['Nos', Validators.required],
      station: ['', Validators.required],
      remarks: [''],
    });
  }

  openRoutingHistory(): void {
    const pn = this.selectedRoutingItem?.pn || this.workOrderForm.value?.pn || this.routingForm.value?.pn || '';
    if (!pn) {
      // nothing to navigate to
      this.routingError = 'Please enter or select a PN to view history.';
      return;
    }

    this.routingError = 'Engineering Routing was removed. Please use the Workflow Routing panel.';
  }

  ngOnInit(): void {
    this.loadSites();
    this.loadPlantOptions();
    this.loadStations();
  }

  loadStations(): void {
    this.isStationsLoading = true;
    const params = new HttpParams().set('limit', 'all').set('page', '1');
    this.http.get<any>(this.sitesApi.replace('/api/sites', '/api/stations'), { params }).subscribe({
      next: (resp) => {
        const data = resp?.data || resp || [];
        this.stations = (data || []).filter((s: any) => s.status === 'Active');
        this.isStationsLoading = false;
      },
      error: () => {
        this.stations = [];
        this.isStationsLoading = false;
      }
    });
  }

  loadSites(): void {
    this.http.get<Site[]>(this.sitesApi).subscribe({
      next: (sites) => {
        this.sites = sites || [];
      }
    });
  }

  getPlantOptions(): string[] {
    return this.plantOptions.length ? this.plantOptions : this.fallbackPlants;
  }

  loadPlantOptions(): void {
    this.http.get<ProductLine[]>(this.productLinesApi).subscribe({
      next: (lines) => {
        const productLinePlants = (lines || [])
          .filter((line) => line.status !== 'Inactive')
          .map((line) => (line.description || line.code || '').trim())
          .filter((name): name is string => Boolean(name));

        this.plantOptions = Array.from(new Set([...productLinePlants, ...this.fallbackPlants]));
      },
      error: () => {
        this.plantOptions = [...this.fallbackPlants];
      }
    });
  }

  previousStep(): void {
    if (this.activeStep > 0) {
      this.activeStep -= 1;
    }
  }

  nextStep(): void {
    if (this.activeStep < this.steps.length - 1) {
      this.activeStep += 1;
    }
  }

  goToStep(index: number): void {
    // Prevent jumping to steps that are not yet available
    if (index <= this.activeStep || this.completedSteps.has(index - 1)) {
      this.activeStep = index;
    }
  }

  openPartNumberModal(): void {
    this.partNumberMessage = '';
    this.partNumberErrorMessage = '';
    this.partNumberForm.reset({
      type: '',
      code: this.workflowParentPn,
      description: '',
      status: 'Active',
    });
    this.showPartNumberModal = true;
  }

  closePartNumberModal(): void {
    this.showPartNumberModal = false;
    this.partNumberMessage = '';
    this.partNumberErrorMessage = '';
  }

  savePartNumber(): void {
    this.partNumberErrorMessage = '';
    this.partNumberMessage = '';

    if (this.partNumberForm.invalid) {
      this.partNumberForm.markAllAsTouched();
      this.partNumberErrorMessage = 'Please fill required fields: Type, PN Type Code, Description, Status.';
      return;
    }

    this.isPartNumberSubmitting = true;
    this.http.post<any>(this.pnTypesApi, this.partNumberForm.value).subscribe({
      next: () => {
        const form = this.partNumberForm.value;
        this.workflowParentPn = (form.code || '').trim();
        this.workflowParentDescription = (form.description || '').trim();
        this.workflowParentType = (form.type || '').trim();
        this.workOrderForm.patchValue({ pn: this.workflowParentPn });
        this.routingForm.patchValue({ pn: this.workflowParentPn });
        this.pnQuery = this.workflowParentPn;
        this.partNumberMessage = 'Part Number created successfully.';
        this.isPartNumberSubmitting = false;
        this.completedSteps.add(0);
        this.activeStep = 1;
        this.closePartNumberModal();
      },
      error: (error) => {
        this.partNumberErrorMessage = error?.error?.error || 'Unable to create Part Number.';
        this.isPartNumberSubmitting = false;
      }
    });
  }

  openWorkOrderModal(): void {
    this.workOrderMessage = '';
    this.workOrderErrorMessage = '';
    const today = new Date().toISOString().slice(0, 10);
    this.workOrderForm.reset({
      wo: '',
      site_id: null,
      plant: this.workflowWorkOrder?.plant || this.getPlantOptions()[0] || '',
      due_date: today,
      qty: null,
      status: 'Released',
      pn: this.workflowParentPn,
      revision: this.workflowWorkOrder?.revision || '',
      lot: '',
    });
    this.pnQuery = this.workflowParentPn;
    this.pnSuggestions = [];
    this.showWorkOrderModal = true;
  }

  closeWorkOrderModal(): void {
    this.showWorkOrderModal = false;
    this.workOrderMessage = '';
    this.workOrderErrorMessage = '';
  }

  onPnInput(value: string): void {
    this.pnQuery = value;
    this.workOrderForm.patchValue({ pn: value });

    if (this.lookupTimer) {
      window.clearTimeout(this.lookupTimer);
    }

    const trimmed = value.trim();
    if (trimmed.length < 2) {
      this.pnSuggestions = [];
      return;
    }

    this.lookupTimer = window.setTimeout(() => {
      const params = new HttpParams().set('search', trimmed).set('limit', '20');
      this.http.get<{ data: any[] }>(`${this.itemRevisionsApi}/lookup`, { params }).subscribe({
        next: (response) => {
          this.pnSuggestions = Array.isArray(response) ? response : (response.data || []);
        },
        error: () => {
          this.pnSuggestions = [];
        }
      });
    }, 250);
  }

  selectPnSuggestion(s: any): void {
    this.pnQuery = s.pn;
    this.pnSuggestions = [];
    this.workOrderForm.patchValue({ pn: s.pn });
    this.workflowParentPn = s.pn;
    this.workflowParentDescription = s.description || this.workflowParentDescription;
    this.routingForm.patchValue({ pn: s.pn });
  }

  saveWorkOrder(): void {
    this.workOrderErrorMessage = '';
    this.workOrderMessage = '';

    if (this.workOrderForm.invalid) {
      this.workOrderForm.markAllAsTouched();
      this.workOrderErrorMessage = 'Please fill all required fields.';
      return;
    }

    this.isWorkOrderSubmitting = true;
    const formValue = this.workOrderForm.value;
    this.workflowParentPn = (formValue.pn || this.workflowParentPn || '').trim();
      this.workflowWorkOrder = {
        wo: formValue.wo || '',
        siteId: formValue.site_id ?? null,
        revision: formValue.revision || '',
        plant: formValue.plant || '',
        status: formValue.status || 'Released',
    };
    this.routingForm.patchValue({ pn: this.workflowParentPn });

    this.http.post(this.workOrdersApi, this.workOrderForm.value).subscribe({
      next: () => {
        this.workOrderMessage = 'Work Order created successfully.';
        this.isWorkOrderSubmitting = false;
        this.completedSteps.add(1);
        this.activeStep = 2;
        this.closeWorkOrderModal();
      },
      error: (error) => {
        this.workOrderErrorMessage = error?.error?.message || 'Work Order saved in Work Flow state. Backend save is unavailable.';
        this.isWorkOrderSubmitting = false;
        this.completedSteps.add(1);
        this.activeStep = 2;
        this.closeWorkOrderModal();
      }
    });
  }

  isStepCompleted(index: number): boolean {
    return this.completedSteps.has(index);
  }

  isStepAvailable(index: number): boolean {
    return index <= this.activeStep || this.completedSteps.has(index - 1);
  }

  openPreviewModal(): void {
    this.previewMessage = '';
    this.previewError = '';

    if (![0, 1, 2, 3].every((step) => this.completedSteps.has(step))) {
      this.previewError = 'Complete Steps 1 to 4 before opening Preview.';
      return;
    }

    this.activeStep = 4;
    this.showPreviewModal = true;
  }

  closePreviewModal(): void {
    this.showPreviewModal = false;
    this.showFinalReportModal = false;
    this.previewMessage = '';
    this.previewError = '';
  }

  backFromPreview(): void {
    this.closePreviewModal();
    this.activeStep = 3;
  }

  finalSubmitWorkflow(): void {
    if (![0, 1, 2, 3].every((step) => this.completedSteps.has(step))) {
      this.previewError = 'Complete Steps 1 to 4 before final submit.';
      return;
    }

    this.previewError = '';
    this.previewMessage = '';
    this.showFinalReportModal = true;
  }

  closeFinalReportModal(): void {
    this.showFinalReportModal = false;
  }

  approveFinalReport(): void {
    if (![0, 1, 2, 3].every((step) => this.completedSteps.has(step))) {
      this.previewError = 'Complete Steps 1 to 4 before final submit.';
      this.showFinalReportModal = false;
      return;
    }

    this.completedSteps.add(4);
    this.activeStep = 4;
    this.dispatchFlowApproved = true;
    this.showFinalReportModal = false;
    this.previewError = '';
    this.previewMessage = 'Workflow completed successfully.';
  }

  getPreviewSite(): string {
    const siteId = this.workflowWorkOrder?.siteId ?? this.workOrderForm.value?.site_id ?? null;
    const site = this.sites.find((row) => row.id === siteId);
    return site?.name || 'Not selected';
  }

  getPreviewQuantity(): string {
    const qty = this.workOrderForm.value?.qty;
    return qty ? String(qty) : 'Not selected';
  }

  getPreviewWorkOrderStatus(): string {
    return this.workflowWorkOrder?.status || this.workOrderForm.value?.status || 'Not selected';
  }

  getPreviewPnType(): string {
    return this.workflowParentType || this.partNumberForm.value?.type || 'Not selected';
  }

  getFinalReportStatus(): string {
    if (this.completedSteps.has(4)) {
      return 'Completed';
    }

    return this.completedSteps.has(3) ? 'In Progress' : 'Pending';
  }

  getFinalReportProgress(): number {
    return Math.min(100, Math.round((this.completedSteps.size / this.steps.length) * 100));
  }

  getCurrentStationName(): string {
    const station = this.getBomStationOptions()[0];
    return station?.station_name || station?.station_code || 'Not selected';
  }

  getRoutingStepStatus(index: number): string {
    return this.dispatchFlowApproved || index === 0 ? 'Completed' : 'Pending';
  }

  getOperatorStatus(): string {
    return this.dispatchFlowApproved ? 'Completed' : 'In Progress';
  }

  getReportNotes(): string {
    const remarks = this.bomComponents
      .map((component) => component.remarks)
      .filter((remark) => !!remark);
    return remarks.length ? remarks.join(', ') : 'No remarks added.';
  }

  getPreviewChildComponents(): BomComponentRow[] {
    return this.bomComponents.filter((component) => !this.isPackagingComponent(component));
  }

  getPreviewPackagingComponents(): BomComponentRow[] {
    return this.bomComponents.filter((component) => this.isPackagingComponent(component));
  }

  /* Routing modal and operations */
  openRoutingModal(): void {
    const pn = this.getWorkflowPn();

    this.selectedRoutingItem = null;
    this.routeSteps = [...this.workflowRoutingSteps];
    this.showAddRoutingRow = false;
    this.addRoutingForm.reset({ station_code: '', sample_mode: 'Full', report_mode: 'Regular' });
    this.routingForm.reset({ pn });
    this.routingError = '';
    this.routingSuccess = '';
    this.showRoutingModal = true;

    if (pn) {
      this.selectRoutingItemByPn(pn);
    }
  }

  closeRoutingModal(): void {
    this.showRoutingModal = false;
    this.selectedRoutingItem = null;
  }

  selectRoutingItemByPn(pn: string): void {
    const params = new HttpParams().set('pn', pn);
    this.http.get<any>(`${this.routingApiBase}/by-pn`, { params }).subscribe({
      next: (item) => {
        this.selectedRoutingItem = item;
        this.workflowParentPn = item?.pn || pn;
        this.loadRoutingRows();
      },
      error: () => {
        this.selectedRoutingItem = null;
        this.routeSteps = [...this.workflowRoutingSteps];
        this.routingError = '';
        this.routingSuccess = '';
      }
    });
  }

  private normalizeRouteSteps(response: any): any[] {
    if (!response) {
      return [];
    }

    if (Array.isArray(response)) {
      return response;
    }

    if (Array.isArray(response.data)) {
      return response.data;
    }

    if (Array.isArray(response.data?.data)) {
      return response.data.data;
    }

    return [];
  }

  loadRoutingRows(): void {
    if (!this.selectedRoutingItem) return;
    this.http.get<any>(`${this.routingApiBase}/${this.selectedRoutingItem.id}/steps`).subscribe({
      next: (resp) => {
        this.routeSteps = this.normalizeRouteSteps(resp);
        this.workflowRoutingSteps = [...this.routeSteps];
      },
      error: () => {
        this.routeSteps = [...this.workflowRoutingSteps];
      }
    });
  }

  loadRoutingFromPn(): void {
    this.routingError = '';
    this.routingSuccess = '';
    const pn = this.routingForm.value?.pn?.trim() || this.getWorkflowPn();
    if (!pn) {
      this.routingError = 'Please enter PN to load routing.';
      return;
    }
    this.workflowParentPn = pn;
    this.routingForm.patchValue({ pn });
    this.selectRoutingItemByPn(pn);
    this.routingSuccess = 'Loading routing for PN...';
  }

  toggleAddRoutingRow(): void {
    this.showAddRoutingRow = !this.showAddRoutingRow;
    if (this.showAddRoutingRow) {
      this.addRoutingForm.reset({ station_code: '', sample_mode: 'Full', report_mode: 'Regular' });
    }
  }

  private doSaveRouting(itemId: number, payload: any): void {
    this.http.post(`${this.routingApiBase}/${itemId}/steps`, payload).subscribe({
      next: (resp: any) => {
        this.showAddRoutingRow = false;
        this.addRoutingForm.reset({ station_code: '', sample_mode: 'Full', report_mode: 'Regular' });
        this.routingError = '';
        this.routingSuccess = 'Routing row saved successfully.';

        const savedStep = resp?.data || resp;
        if (savedStep && !Array.isArray(savedStep)) {
          this.routeSteps = [...this.routeSteps, savedStep];
          this.workflowRoutingSteps = [...this.routeSteps];
        }

        this.loadRoutingRows();
        // mark completed and advance
        this.completedSteps.add(2);
        this.activeStep = 3;
      },
      error: () => {
        this.routingError = 'Unable to save routing row. Please try again.';
        this.routingSuccess = '';
      }
    });
  }

  saveRoutingRow(): void {
    this.routingError = '';
    this.routingSuccess = '';

    if (this.addRoutingForm.invalid) {
      this.addRoutingForm.markAllAsTouched();
      return;
    }

    const form = this.addRoutingForm.value;
    const selectedStation = this.stations.find((s) => s.station_code === form.station_code || s.id === form.station_code);
    if (!selectedStation) {
      this.routingError = 'Please select a valid station.';
      return;
    }

    const payload = {
      station_code: selectedStation.station_code,
      station_name: selectedStation.station_desc,
      sample_mode: form.sample_mode,
      report_mode: form.report_mode,
      station_order: (this.routeSteps.length ? Math.max(...this.routeSteps.map((r: any) => Number(r.station_order) || 0)) + 10 : 10)
    };

    // If we already have a selectedRoutingItem, post directly
    if (this.selectedRoutingItem && this.selectedRoutingItem.id) {
      this.doSaveRouting(this.selectedRoutingItem.id, payload);
      return;
    }

    // Otherwise, attempt to resolve selectedRoutingItem using PN from form or work order
    const pn = this.getWorkflowPn();
    if (!pn) {
      this.routingError = 'Please select or enter a PN before adding routing.';
      return;
    }

    // Resolve item by PN then save
    const params = new HttpParams().set('pn', pn);
    this.http.get<any>(`${this.routingApiBase}/by-pn`, { params }).subscribe({
      next: (item) => {
        if (item && item.id) {
          this.selectedRoutingItem = item;
          this.workflowParentPn = item.pn || pn;
          this.doSaveRouting(item.id, payload);
        } else {
          this.saveRoutingRowInWorkflow(payload);
        }
      },
      error: () => {
        this.saveRoutingRowInWorkflow(payload);
      }
    });
  }

  deleteRoutingRow(stepId: number): void {
    if (!confirm('Delete routing row?')) return;
    this.http.delete(`${this.routingApiBase}/steps/${stepId}`, { body: {} }).subscribe({
      next: () => this.loadRoutingRows(),
      error: () => {}
    });
  }

  openBomModal(): void {
    this.bomMessage = '';
    this.bomError = '';
    this.showAddBomComponent = false;
    this.resetBomComponentForm();
    this.showBomModal = true;

    const pn = this.getWorkflowPn();
    if (pn && this.routeSteps.length === 0) {
      this.selectRoutingItemByPn(pn);
    }
  }

  closeBomModal(): void {
    this.showBomModal = false;
    this.bomMessage = '';
    this.bomError = '';
    this.showAddBomComponent = false;
    this.closeStationRulesModal();
  }

  toggleAddBomComponent(): void {
    this.bomMessage = '';
    this.bomError = '';
    if (!this.getBomStationOptions().length) {
      this.showAddBomComponent = false;
      this.bomError = 'Please add routing station before adding BOM component.';
      return;
    }

    this.showAddBomComponent = !this.showAddBomComponent;
    if (this.showAddBomComponent) {
      this.resetBomComponentForm();
    }
  }

  addBomComponent(): void {
    this.bomMessage = '';
    this.bomError = '';

    if (this.bomComponentForm.invalid) {
      this.bomComponentForm.markAllAsTouched();
      this.bomError = 'Please fill all required component fields.';
      return;
    }

    const form = this.bomComponentForm.value;
    this.bomComponents = [
      ...this.bomComponents,
      {
        childPartNumber: form.childPartNumber,
        childName: form.childName,
        quantity: Number(form.quantity),
        unit: form.unit,
        station: form.station,
        remarks: form.remarks || '',
      }
    ];
    this.showAddBomComponent = false;
    this.resetBomComponentForm();
    this.bomMessage = 'Component added to BOM.';
  }

  removeBomComponent(index: number): void {
    this.bomComponents = this.bomComponents.filter((_, rowIndex) => rowIndex !== index);
  }

  onBomStationChange(stationCode: string): void {
    const selectedStation = this.getBomStationOptions().find((step) => step.station_code === stationCode);
    this.selectedRuleStationName = selectedStation?.station_name || selectedStation?.station_code || stationCode || 'Not selected';
    this.selectedStationRules = this.buildStationRules(selectedStation);
    this.showStationRulesModal = Boolean(stationCode);
  }

  closeStationRulesModal(): void {
    this.showStationRulesModal = false;
  }

  saveBom(): void {
    this.bomMessage = '';
    this.bomError = '';

    if (this.bomComponents.length === 0) {
      this.bomError = 'Add at least one child component before saving BOM.';
      return;
    }

    this.completedSteps.add(3);
    this.activeStep = 4;
    this.bomMessage = 'BOM saved successfully.';
  }

  getBomParentPn(): string {
    return this.getWorkflowPn() || 'Not selected';
  }

  getBomWorkOrder(): string {
    return this.workflowWorkOrder?.wo || this.workOrderForm.value?.wo || 'Not selected';
  }

  getBomRevision(): string {
    return this.workflowWorkOrder?.revision || this.workOrderForm.value?.revision || 'Not selected';
  }

  getBomPlant(): string {
    return this.workflowWorkOrder?.plant || this.workOrderForm.value?.plant || 'Not selected';
  }

  getBomStatus(): string {
    return this.completedSteps.has(3) ? 'Saved' : 'Draft';
  }

  getBomStationOptions(): Array<any> {
    return this.workflowRoutingSteps.length ? this.workflowRoutingSteps : this.routeSteps;
  }

  private buildStationRules(station: any): string[] {
    if (!station) {
      return [];
    }

    const rules: string[] = [];
    if (station.sample_mode) {
      rules.push(`Sample: ${station.sample_mode}`);
    }

    if (station.report_mode) {
      rules.push(`Report Method: ${station.report_mode}`);
    }

    if (station.station_order) {
      rules.push(`Station Order: ${station.station_order}`);
    }

    return rules;
  }

  private getWorkflowPn(): string {
    return (this.workflowParentPn || this.workOrderForm.value?.pn || this.routingForm.value?.pn || '').trim();
  }

  private saveRoutingRowInWorkflow(payload: any): void {
    const localStep = {
      ...payload,
      id: Date.now(),
      item_id: null,
      station_name: payload.station_name || payload.station_code,
    };

    this.routeSteps = [...this.routeSteps, localStep];
    this.workflowRoutingSteps = [...this.routeSteps];
    this.showAddRoutingRow = false;
    this.addRoutingForm.reset({ station_code: '', sample_mode: 'Full', report_mode: 'Regular' });
    this.routingError = '';
    this.routingSuccess = 'Routing row saved in Work Flow state.';
    this.completedSteps.add(2);
    this.activeStep = 3;
  }

  private isPackagingComponent(component: BomComponentRow): boolean {
    const text = `${component.childPartNumber} ${component.childName} ${component.remarks}`.toLowerCase();
    return ['box', 'pack', 'cart', 'pallet', 'label', 'bag', 'tray'].some((word) => text.includes(word));
  }

  private resetBomComponentForm(): void {
    this.bomComponentForm.reset({
      childPartNumber: '',
      childName: '',
      quantity: 1,
      unit: 'Nos',
      station: '',
      remarks: '',
    });
  }
}
