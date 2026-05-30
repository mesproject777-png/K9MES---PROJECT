import { HttpClient, HttpParams } from '@angular/common/http';
import {
  AfterViewChecked,
  AfterViewInit,
  ChangeDetectorRef,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  QueryList,
  ViewChild,
  ViewChildren,
} from '@angular/core';
import { AbstractControl, FormBuilder, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import { environment } from '../../../environments/environment';

type WorkflowTab = {
  id: string;
  label: string;
  icon: string;
};

type PnType = {
  id: number;
  code: string;
  description: string;
  status: string;
};

type SnType = {
  id: number;
  sn_type_name: string;
  number_of_fields?: number;
  field_count?: number;
};

type Site = {
  id: number;
  name: string;
  plant?: string;
};

type StationOption = {
  id: number;
  station_code: string;
  station_desc: string;
  status: string;
};

type StationsResponse = {
  data: StationOption[];
  total: number;
  page: number;
  limit: number;
};

type RoutingStepRow = {
  id: number;
  station_order: number;
  station_code: string;
  station_name: string;
  sample_mode: 'Full' | 'Sample';
  report_mode: 'Regular' | 'Auto Only';
};

type RoutingHistoryRow = {
  id: number;
  description: string;
  change_field: string;
  old_value: string;
  new_value: string;
  changed_by: string;
  changed_at: string;
};

type BomChildRow = {
  id: number;
  son_pn: string;
  son_description: string;
  station_code: string;
  station_name: string;
  item_type: string;
  pn_type: string;
  qty: number;
};

type BomHistoryRow = {
  id: number;
  description: string;
  change_field: string;
  old_value: string;
  new_value: string;
  changed_by: string;
  changed_at: string;
};

type PreviewStatus = 'Completed' | 'In Progress' | 'Pending' | 'Paused';

type PreviewStationNode = RoutingStepRow & {
  flowIndex: number;
  icon: string;
  status: PreviewStatus;
};

type PreviewFlowNode = {
  id: string;
  kind: 'operator' | 'station' | 'empty' | 'logistics';
  title?: string;
  icon?: string;
  subtitle?: string;
  variant?: 'cart' | 'pallet' | 'truck';
  station?: PreviewStationNode;
};

type PreviewFlowRow = {
  nodes: PreviewFlowNode[];
  isReversed: boolean;
  turnSide: 'left' | 'right';
};

type WorkflowSnapshot = {
  partNumber?: {
    pn?: string;
    description?: string;
    sgd_control?: boolean;
    item_type?: string;
    sn_type_name?: string;
    pn_type_id?: number | null;
  };
  workOrder?: {
    wo?: string;
    plant?: string | null;
    site_id?: number | null;
    site_name?: string | null;
    due_date?: string | null;
    qty?: number | null;
    status?: string | null;
    pn?: string;
    revision?: string | null;
    lot?: string | null;
  } | null;
  routing?: Array<RoutingStepRow & { preview_status?: PreviewStatus | null }>;
  bom?: BomChildRow[];
  stationRules?: Record<string, string[]>;
  previewStatuses?: Record<string, PreviewStatus>;
};

@Component({
  selector: 'app-workflow',
  standalone: false,
  templateUrl: './workflow.component.html',
  styleUrl: './workflow.component.scss'
})
export class WorkflowComponent implements AfterViewInit, AfterViewChecked, OnDestroy {
  private readonly pnTypesApiUrl = `${environment.apiUrl}/api/users/pn-types`;
  private readonly snTypesApiUrl = `${environment.apiUrl}/api/sn-types`;
  private readonly sitesApiUrl = `${environment.apiUrl}/api/sites`;
  private readonly stationsApiUrl = `${environment.apiUrl}/api/stations`;
  private readonly workflowApiUrl = `${environment.apiUrl}/api/workflow`;

  readonly tabs: WorkflowTab[] = [
    { id: 'part-number', label: 'Part Number', icon: 'inventory_2' },
    { id: 'work-order', label: 'Work Order', icon: 'assignment' },
    { id: 'routing', label: 'Routing', icon: 'route' },
    { id: 'bom', label: 'BOM', icon: 'schema' },
    { id: 'preview', label: 'Preview', icon: 'visibility' }
  ];

  readonly plantOptions = ['Tirupati', 'Bangalore', 'Hyderabad', 'Chennai', 'Pune', 'Mumbai'];
  private readonly fallbackSitesByPlant: Record<string, string[]> = {
    Tirupati: ['Tirupati Main Site', 'Tirupati Assembly Site', 'Tirupati Quality Site'],
    Bangalore: ['Bangalore Main Site', 'Bangalore Assembly Site', 'Bangalore Quality Site'],
    Hyderabad: ['Hyderabad Main Site', 'Hyderabad Assembly Site', 'Hyderabad Quality Site'],
    Chennai: ['Chennai Main Site', 'Chennai Assembly Site', 'Chennai Quality Site'],
    Pune: ['Pune Main Site', 'Pune Assembly Site', 'Pune Quality Site'],
    Mumbai: ['Mumbai Main Site', 'Mumbai Assembly Site', 'Mumbai Quality Site'],
  };
  readonly workOrderStatusOptions = ['Allocated', 'Planned', 'Released', 'Cancelled', 'Closed'];
  readonly sampleModeOptions: Array<'Full' | 'Sample'> = ['Full', 'Sample'];
  readonly reportModeOptions: Array<'Regular' | 'Auto Only'> = ['Regular', 'Auto Only'];
  readonly minDueDate = this.getDateInputValue(1);

  activeTabIndex = 0;
  pnTypes: PnType[] = [];
  snTypes: SnType[] = [];
  sites: Site[] = [];
  stations: StationOption[] = [];
  partNumberForm: FormGroup;
  workOrderForm: FormGroup;
  routingStepForm: FormGroup;
  bomChildForm: FormGroup;
  partNumberErrorMessage = '';
  workOrderErrorMessage = '';
  routingErrorMessage = '';
  bomErrorMessage = '';
  isPartNumberSaved = false;
  isWorkOrderSaved = false;
  isStationsLoading = false;
  isRoutingSaving = false;
  isRoutingChildrenSaved = false;
  includeRoutingHistory = false;
  isRoutingStepEditorOpen = false;
  isRoutingEditMode = false;
  editingRoutingStepId: number | null = null;
  linkedRoutingPartNumber = '';
  linkedRoutingDescription = '';
  routeSteps: RoutingStepRow[] = [];
  routeHistory: RoutingHistoryRow[] = [];
  linkedBomPartNumber = '';
  bomChildren: BomChildRow[] = [];
  bomHistory: BomHistoryRow[] = [];
  includeBomHistory = false;
  isBomChildEditorOpen = false;
  isBomEditMode = false;
  isBomChildrenSaved = false;
  isBomChildSaving = false;
  isPreviewSaved = false;
  showSavePreviousWorkPopup = false;
  savePreviousWorkPopupLeft = 50;
  editingBomChildId: number | null = null;
  isStationRulesModalOpen = false;
  isEditingStationRules = false;
  activeRulesStationCode = '';
  activeRulesStationName = '';
  stationRulesDraft = '';
  stationRulesByStation: Record<string, string[]> = {};
  previewStationStatusById: Record<number, PreviewStatus> = {};
  isChildDetailsOpen = false;
  activePreviewStation: PreviewStationNode | null = null;
  previewActionMessage = '';
  previewActionMessageType: 'success' | 'error' = 'success';
  previewConnectorPath = '';
  previewConnectorWidth = 0;
  previewConnectorHeight = 0;
  previewFlowCardsPerRow = this.getPreviewFlowCardsPerRow();
  @ViewChild('previewProcessFlow') private previewProcessFlowRef?: ElementRef<HTMLElement>;
  @ViewChildren('previewFlowNode') private previewFlowNodeRefs?: QueryList<ElementRef<HTMLElement>>;
  private isRestoringSavedPreview = false;
  private nextRoutingStepId = 1;
  private nextRoutingHistoryId = 1;
  private nextBomChildId = 1;
  private nextBomHistoryId = 1;
  private clearMessageTimer: number | null = null;
  private advancePaneTimer: number | null = null;
  private savePreviousWorkPopupTimer: number | null = null;
  private restoreWorkflowTimer: number | null = null;
  private previewConnectorFrame: number | null = null;
  private previewConnectorSignature = '';

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private cdr: ChangeDetectorRef
  ) {
    this.partNumberForm = this.fb.group({
      pn: ['', Validators.required],
      description: ['', Validators.required],
      sgd_control: [false],
      item_type: [null, Validators.required],
      sn_type_name: [''],
      pn_type_id: [null, Validators.required],
    });

    this.workOrderForm = this.fb.group({
      wo: ['', Validators.required],
      plant: [null, Validators.required],
      site_id: [null, Validators.required],
      due_date: [this.minDueDate, [Validators.required, this.futureDateValidator.bind(this)]],
      qty: [null, [Validators.required, Validators.min(1), Validators.pattern(/^[0-9]+$/)]],
      status: ['Released', Validators.required],
      pn: ['', Validators.required],
      revision: ['', Validators.required],
      lot: [''],
    });

    this.routingStepForm = this.fb.group({
      station_code: ['', Validators.required],
      sample_mode: ['Full', Validators.required],
      report_mode: ['Regular', Validators.required],
    });

    this.bomChildForm = this.fb.group({
      son_pn: ['', Validators.required],
      qty: [1, [Validators.required, Validators.min(1), Validators.pattern(/^[0-9]+$/)]],
      station_code: [''],
    });

    this.loadPnTypes();
    this.loadSnTypes();
    this.loadSites();
    this.loadStations();

    this.partNumberForm.get('pn')?.valueChanges.subscribe((value) => {
      if (this.isRestoringSavedPreview) {
        return;
      }

      this.isPartNumberSaved = false;
      const partNumber = String(value ?? '').trim();
      if (this.restoreWorkflowTimer) {
        window.clearTimeout(this.restoreWorkflowTimer);
      }

      if (partNumber.length < 2) {
        return;
      }

      this.restoreWorkflowTimer = window.setTimeout(() => {
        this.restoreWorkflowTimer = null;
        this.restoreSavedPreviewForPartNumber(partNumber);
      }, 350);
    });

    ['description', 'sgd_control', 'item_type', 'sn_type_name', 'pn_type_id'].forEach((controlName) => {
      this.partNumberForm.get(controlName)?.valueChanges.subscribe(() => {
        if (!this.isRestoringSavedPreview) {
          this.isPartNumberSaved = false;
        }
      });
    });

    this.workOrderForm.valueChanges.subscribe(() => {
      if (!this.isRestoringSavedPreview) {
        this.isWorkOrderSaved = false;
      }
    });

    this.workOrderForm.get('plant')?.valueChanges.subscribe((plant) => {
      if (!this.isRestoringSavedPreview) {
        this.syncSelectedSiteWithPlant(String(plant ?? ''));
      }
    });
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    this.previewFlowCardsPerRow = this.getPreviewFlowCardsPerRow();
    this.queuePreviewConnectorRefresh();
  }

  ngAfterViewInit(): void {
    this.previewFlowNodeRefs?.changes.subscribe(() => this.queuePreviewConnectorRefresh());
    this.queuePreviewConnectorRefresh();
  }

  ngAfterViewChecked(): void {
    const signature = this.buildPreviewConnectorSignature();

    if (signature !== this.previewConnectorSignature) {
      this.previewConnectorSignature = signature;
      this.queuePreviewConnectorRefresh();
    }
  }

  ngOnDestroy(): void {
    if (this.clearMessageTimer) {
      window.clearTimeout(this.clearMessageTimer);
    }

    if (this.advancePaneTimer) {
      window.clearTimeout(this.advancePaneTimer);
    }

    if (this.savePreviousWorkPopupTimer) {
      window.clearTimeout(this.savePreviousWorkPopupTimer);
    }

    if (this.restoreWorkflowTimer) {
      window.clearTimeout(this.restoreWorkflowTimer);
    }

    if (this.previewConnectorFrame) {
      window.cancelAnimationFrame(this.previewConnectorFrame);
    }
  }

  selectTab(index: number): void {
    if (index !== this.activeTabIndex && !this.canLeavePane(this.activeTabIndex) && !this.isPaneSaved(index)) {
      this.showSavePreviousWorkNotice(index);
      return;
    }

    if (index === 1) {
      this.syncPartNumberToWorkOrder();
    }

    if (index === 2) {
      this.syncPartNumberToRouting();
    }

    if (index === 3) {
      this.syncPartNumberToBom();
    }

    if (index === 4) {
      this.isPreviewSaved = true;
      this.previewFlowCardsPerRow = this.getPreviewFlowCardsPerRow();
      this.queuePreviewConnectorRefresh();
    }

    this.activeTabIndex = index;
  }

  getPaneState(index: number): 'active' | 'before' | 'after' {
    if (index === this.activeTabIndex) {
      return 'active';
    }

    return index < this.activeTabIndex ? 'before' : 'after';
  }

  isPaneSaved(index: number): boolean {
    switch (index) {
      case 0:
        return this.isPartNumberSaved;
      case 1:
        return this.isWorkOrderSaved;
      case 2:
        return this.isRoutingChildrenSaved;
      case 3:
        return this.isBomChildrenSaved;
      case 4:
        return this.isPreviewSaved;
      default:
        return false;
    }
  }

  private canLeavePane(index: number): boolean {
    if (index === 4) {
      return true;
    }

    return this.isPaneSaved(index);
  }

  private showSavePreviousWorkNotice(targetIndex: number): void {
    const tabCount = this.tabs.length || 1;
    this.savePreviousWorkPopupLeft = ((targetIndex + 0.5) / tabCount) * 100;
    this.showSavePreviousWorkPopup = true;

    if (this.savePreviousWorkPopupTimer) {
      window.clearTimeout(this.savePreviousWorkPopupTimer);
    }

    this.savePreviousWorkPopupTimer = window.setTimeout(() => {
      this.showSavePreviousWorkPopup = false;
      this.savePreviousWorkPopupTimer = null;
    }, 1800);
  }

  savePartNumber(): void {
    this.partNumberErrorMessage = '';

    if (this.partNumberForm.invalid) {
      this.partNumberForm.markAllAsTouched();
      this.partNumberErrorMessage = this.buildPartNumberMissingFieldsMessage();
      this.scheduleClearMessages();
      return;
    }

    this.syncPartNumberToWorkOrder();
    this.syncPartNumberToRouting();
    this.saveWorkflowSnapshot(
      () => {
        this.isPartNumberSaved = true;
        this.advanceToNextPane(1);
      },
      (message) => {
        this.partNumberErrorMessage = message;
        this.scheduleClearMessages();
      }
    );
  }

  saveWorkOrder(): void {
    this.workOrderErrorMessage = '';

    if (this.workOrderForm.invalid) {
      this.workOrderForm.markAllAsTouched();
      this.workOrderErrorMessage = this.buildWorkOrderMissingFieldsMessage();
      this.scheduleClearMessages();
      return;
    }

    this.syncPartNumberToWorkOrder();
    this.saveWorkflowSnapshot(
      () => {
        this.isWorkOrderSaved = true;
        this.advanceToNextPane(2);
      },
      (message) => {
        this.workOrderErrorMessage = message;
        this.scheduleClearMessages();
      }
    );
  }

  allowNumberOnly(event: KeyboardEvent): void {
    const allowedKeys = ['Backspace', 'Delete', 'Tab', 'ArrowLeft', 'ArrowRight', 'Home', 'End'];

    if (allowedKeys.includes(event.key) || event.ctrlKey || event.metaKey) {
      return;
    }

    if (!/^\d$/.test(event.key)) {
      event.preventDefault();
    }
  }

  sanitizeNumberControl(form: FormGroup, controlName: string): void {
    const control = form.get(controlName);
    const currentValue = String(control?.value ?? '');
    const cleanedValue = currentValue.replace(/\D/g, '');

    if (currentValue !== cleanedValue) {
      control?.setValue(cleanedValue);
    }
  }

  get routingPartNumber(): string {
    return this.linkedRoutingPartNumber || String(this.partNumberForm.get('pn')?.value ?? '').trim();
  }

  get routingDescription(): string {
    return this.linkedRoutingDescription || String(this.partNumberForm.get('description')?.value ?? '').trim();
  }

  openRoutingStepEditor(): void {
    this.routingErrorMessage = '';

    if (!this.routingPartNumber) {
      this.routingErrorMessage = 'Please enter and save Part Number first.';
      this.scheduleClearMessages();
      return;
    }

    if (this.isStationsLoading) {
      this.routingErrorMessage = 'Stations are still loading. Please try again in a moment.';
      this.scheduleClearMessages();
      return;
    }

    if (!this.getAvailableRoutingStations().length) {
      this.routingErrorMessage = 'No active stations available.';
      this.scheduleClearMessages();
      return;
    }

    this.isRoutingEditMode = false;
    this.editingRoutingStepId = null;
    this.routingStepForm.reset({
      station_code: '',
      sample_mode: 'Full',
      report_mode: 'Regular',
    });
    this.isRoutingStepEditorOpen = true;
  }

  editRoutingStep(step: RoutingStepRow): void {
    this.routingErrorMessage = '';
    this.isRoutingChildrenSaved = false;
    this.isRoutingEditMode = true;
    this.editingRoutingStepId = step.id;
    this.routingStepForm.reset({
      station_code: step.station_code,
      sample_mode: step.sample_mode,
      report_mode: step.report_mode,
    });
    this.isRoutingStepEditorOpen = true;
  }

  saveRoutingStep(): void {
    this.routingErrorMessage = '';

    if (!this.routingPartNumber) {
      this.routingErrorMessage = 'Please enter and save Part Number first.';
      this.scheduleClearMessages();
      return;
    }

    if (this.routingStepForm.invalid) {
      this.routingStepForm.markAllAsTouched();
      this.routingErrorMessage = 'Please fill all required routing fields.';
      this.scheduleClearMessages();
      return;
    }

    const formValue = this.routingStepForm.value;
    const selectedStation = this.stations.find((station) => station.station_code === formValue.station_code);

    if (!selectedStation) {
      this.routingErrorMessage = 'Please select a valid station.';
      this.scheduleClearMessages();
      return;
    }

    const isDuplicateStation = this.routeSteps.some((step) =>
      step.station_code === selectedStation.station_code &&
      (!this.isRoutingEditMode || step.id !== this.editingRoutingStepId)
    );

    if (isDuplicateStation) {
      this.routingErrorMessage = 'This station is already added to routing.';
      this.scheduleClearMessages();
      return;
    }

    this.isRoutingSaving = true;
    this.isRoutingChildrenSaved = false;

    if (this.isRoutingEditMode && this.editingRoutingStepId !== null) {
      const stepIndex = this.routeSteps.findIndex((step) => step.id === this.editingRoutingStepId);

      if (stepIndex >= 0) {
        const previousStep = this.routeSteps[stepIndex];
        this.routeSteps[stepIndex] = {
          ...previousStep,
          station_code: selectedStation.station_code,
          station_name: selectedStation.station_desc,
          sample_mode: formValue.sample_mode,
          report_mode: formValue.report_mode,
        };
        this.addRoutingHistory(
          'Station updated',
          'Routing step',
          `${previousStep.station_code} / ${previousStep.sample_mode} / ${previousStep.report_mode}`,
          `${selectedStation.station_code} / ${formValue.sample_mode} / ${formValue.report_mode}`,
        );
      }
    } else {
      const newStep: RoutingStepRow = {
        id: this.nextRoutingStepId,
        station_order: this.getNextStationOrder(),
        station_code: selectedStation.station_code,
        station_name: selectedStation.station_desc,
        sample_mode: formValue.sample_mode,
        report_mode: formValue.report_mode,
      };

      this.nextRoutingStepId += 1;
      this.routeSteps = [...this.routeSteps, newStep];
      this.addRoutingHistory('Station added', 'Routing step', '-', newStep.station_code);
    }

    this.isRoutingSaving = false;
    this.closeRoutingStepEditor();
  }

  getAvailableRoutingStations(): StationOption[] {
    const selectedStationCodes = new Set(
      this.routeSteps
        .filter((step) => !this.isRoutingEditMode || step.id !== this.editingRoutingStepId)
        .map((step) => step.station_code)
    );

    return this.stations.filter((station) => !selectedStationCodes.has(station.station_code));
  }

  getStationRuleLabel(step: RoutingStepRow): string {
    return this.getStationRuleText(step.station_code);
  }

  openRoutingStationRules(step: RoutingStepRow): void {
    this.activePreviewStation = null;
    this.activeRulesStationCode = step.station_code;
    this.activeRulesStationName = step.station_name;
    this.stationRulesDraft = (this.stationRulesByStation[step.station_code] || []).join('\n');
    this.isEditingStationRules = false;
    this.isStationRulesModalOpen = true;
  }

  deleteRoutingStep(step: RoutingStepRow): void {
    this.isRoutingChildrenSaved = false;
    this.routeSteps = this.routeSteps.filter((routeStep) => routeStep.id !== step.id);
    this.normalizeRouteStepOrder();
    this.addRoutingHistory('Station deleted', 'Routing step', step.station_code, '-');

    if (this.editingRoutingStepId === step.id) {
      this.closeRoutingStepEditor();
    }
  }

  moveRoutingStep(step: RoutingStepRow, direction: 'up' | 'down'): void {
    this.isRoutingChildrenSaved = false;
    const currentIndex = this.routeSteps.findIndex((routeStep) => routeStep.id === step.id);
    const targetIndex = direction === 'up' ? currentIndex - 1 : currentIndex + 1;

    if (currentIndex < 0 || targetIndex < 0 || targetIndex >= this.routeSteps.length) {
      return;
    }

    const reorderedSteps = [...this.routeSteps];
    [reorderedSteps[currentIndex], reorderedSteps[targetIndex]] = [reorderedSteps[targetIndex], reorderedSteps[currentIndex]];
    this.routeSteps = reorderedSteps;
    this.normalizeRouteStepOrder();
    this.addRoutingHistory(`Station moved ${direction}`, 'Station order', String(currentIndex + 1), String(targetIndex + 1));
  }

  toggleRoutingHistory(): void {
    this.includeRoutingHistory = !this.includeRoutingHistory;
  }

  saveRoutingChildren(): void {
    this.routingErrorMessage = '';

    if (this.routeSteps.length === 0) {
      this.routingErrorMessage = 'Please add at least one station before saving routing.';
      this.scheduleClearMessages();
      return;
    }

    this.saveWorkflowSnapshot(
      () => {
        this.isRoutingChildrenSaved = true;
        this.advanceToNextPane(3);
      },
      (message) => {
        this.routingErrorMessage = message;
        this.scheduleClearMessages();
      }
    );
  }

  get bomPartNumber(): string {
    return this.linkedBomPartNumber || this.routingPartNumber;
  }

  get previewPlantName(): string {
    return String(this.workOrderForm.get('plant')?.value ?? '').trim() || 'Select Plant';
  }

  get previewSiteName(): string {
    const siteId = Number(this.workOrderForm.get('site_id')?.value);
    const selectedSite = this.getSiteOptionsForSelectedPlant().find((site) => Number(site.id) === siteId);
    return selectedSite?.name || 'Select Site';
  }

  get previewWorkOrderNumber(): string {
    return String(this.workOrderForm.get('wo')?.value ?? '').trim() || 'WO Pending';
  }

  get previewPartNumber(): string {
    return String(this.partNumberForm.get('pn')?.value ?? '').trim() || 'Part Number Pending';
  }

  get previewPartDescription(): string {
    return String(this.partNumberForm.get('description')?.value ?? '').trim() || 'Part description';
  }

  get previewParentPartNumber(): string {
    const partNumber = this.previewPartNumber;
    const revision = String(this.workOrderForm.get('revision')?.value ?? '').trim();

    if (!revision || partNumber === 'Part Number Pending') {
      return partNumber;
    }

    const revisionSuffix = `-${revision}`;
    return partNumber.endsWith(revisionSuffix)
      ? partNumber.slice(0, -revisionSuffix.length)
      : partNumber;
  }

  get previewBoxLeft(): string {
    const lot = String(this.workOrderForm.get('lot')?.value ?? '').trim();
    return lot || 'BX-50001';
  }

  get previewBoxRight(): string {
    const qty = Number(this.workOrderForm.get('qty')?.value);
    return Number.isFinite(qty) && qty > 0 ? `Qty ${qty}` : 'BX-50002';
  }

  get previewStationStartTime(): string {
    return new Date().toLocaleString([], {
      year: 'numeric',
      month: 'short',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  get previewStations(): PreviewStationNode[] {
    return this.routeSteps.map((step, index) => ({
      ...step,
      flowIndex: index + 1,
      icon: this.getPreviewStationIcon(index, step),
      status: this.getPreviewStationStatus(index, step),
    }));
  }

  get previewFlowNodes(): PreviewFlowNode[] {
    const stationNodes: PreviewFlowNode[] = this.previewStations.length
      ? this.previewStations.map((station) => ({
          id: `station-${station.id}`,
          kind: 'station',
          station,
        }))
      : [
          {
            id: 'stations-pending',
            kind: 'empty',
            title: 'Stations Pending',
            icon: 'route',
          },
        ];

    return [
      {
        id: 'operator',
        kind: 'operator',
        title: 'Operator / Technician',
        icon: 'engineering',
      },
      ...stationNodes,
      {
        id: 'cart',
        kind: 'logistics',
        variant: 'cart',
        title: 'Cart',
        icon: 'shopping_cart',
      },
      {
        id: 'pallet',
        kind: 'logistics',
        variant: 'pallet',
        title: 'Pallet',
        icon: 'inventory_2',
      },
      {
        id: 'truck',
        kind: 'logistics',
        variant: 'truck',
        title: 'Truck',
        subtitle: 'Dispatch / Shipping',
        icon: 'local_shipping',
      },
    ];
  }

  get previewFlowRows(): PreviewFlowRow[] {
    const flowNodes = this.previewFlowNodes;
    const rows: PreviewFlowRow[] = [];
    const cardsPerRow = Math.max(2, this.previewFlowCardsPerRow);

    for (let index = 0; index < flowNodes.length; index += cardsPerRow) {
      const rowIndex = rows.length;
      const nodes = flowNodes.slice(index, index + cardsPerRow);
      const isReversed = rowIndex % 2 === 1;
      rows.push({
        nodes: isReversed ? [...nodes].reverse() : nodes,
        isReversed,
        turnSide: isReversed ? 'left' : 'right',
      });
    }

    return rows;
  }

  get previewChildSummary(): string {
    const childCount = this.bomChildren.length;
    return childCount === 1 ? '1 Child' : `${childCount} Childs`;
  }

  get activePreviewStationRules(): string[] {
    if (!this.activePreviewStation) {
      return [];
    }

    return this.stationRulesByStation[this.activePreviewStation.station_code] || [];
  }

  getSiteOptionsForSelectedPlant(): Site[] {
    const selectedPlant = String(this.workOrderForm.get('plant')?.value ?? '');
    return this.getSiteOptionsForPlant(selectedPlant);
  }

  getAssemblyStationOptions(): RoutingStepRow[] {
    return this.isRoutingChildrenSaved ? this.routeSteps : [];
  }

  openChildDetails(): void {
    this.isChildDetailsOpen = true;
  }

  closeChildDetails(): void {
    this.isChildDetailsOpen = false;
  }

  openPreviewStationDetails(event: Event, station?: PreviewStationNode | null): void {
    event.preventDefault();
    event.stopPropagation();

    if (!station) {
      return;
    }

    this.activePreviewStation = station;
  }

  closePreviewStationDetails(): void {
    this.activePreviewStation = null;
  }

  pausePreviewStation(): void {
    this.setPreviewStationStatus('Paused');
  }

  completePreviewStation(): void {
    this.setPreviewStationStatus('Completed');
  }

  getPreviewStatusClass(status: PreviewStatus): string {
    return status.toLowerCase().replace(/\s+/g, '-');
  }

  savePreview(): boolean {
    this.previewActionMessage = '';
    const partNumber = String(this.partNumberForm.get('pn')?.value ?? '').trim();

    if (!partNumber) {
      this.previewActionMessageType = 'error';
      this.previewActionMessage = 'Enter a part number before saving preview.';
      this.scheduleClearMessages();
      return false;
    }

    this.syncPartNumberToWorkOrder();
    this.syncPartNumberToRouting();
    this.linkedBomPartNumber = partNumber;

    this.saveWorkflowSnapshot(
      () => {
        this.isPreviewSaved = true;
        this.previewActionMessageType = 'success';
        this.previewActionMessage = 'Preview saved for this part number.';
        this.scheduleClearMessages();
      },
      (message) => {
        this.previewActionMessageType = 'error';
        this.previewActionMessage = message;
        this.scheduleClearMessages();
      }
    );

    return true;
  }

  saveWorkflow(): void {
    this.previewActionMessage = '';
    const partNumber = String(this.partNumberForm.get('pn')?.value ?? '').trim();

    if (!partNumber) {
      this.previewActionMessageType = 'error';
      this.previewActionMessage = 'Enter a part number before saving workflow.';
      this.scheduleClearMessages();
      return;
    }

    this.saveWorkflowSnapshot(
      () => {
        this.previewActionMessageType = 'success';
        this.previewActionMessage = 'Workflow saved.';
        this.scheduleClearMessages();
        this.resetWorkflowForNewPartNumber();
      },
      (message) => {
        this.previewActionMessageType = 'error';
        this.previewActionMessage = message;
        this.scheduleClearMessages();
      }
    );
  }

  openBomChildEditor(): void {
    this.bomErrorMessage = '';

    if (!this.bomPartNumber) {
      this.bomErrorMessage = 'Please save Part Number before adding BOM childs.';
      this.scheduleClearMessages();
      return;
    }

    this.isBomEditMode = false;
    this.editingBomChildId = null;
    this.bomChildForm.reset({
      son_pn: '',
      qty: 1,
      station_code: '',
    });
    this.isBomChildEditorOpen = true;
  }

  editBomChild(child: BomChildRow): void {
    this.bomErrorMessage = '';
    this.isBomChildrenSaved = false;
    this.isBomEditMode = true;
    this.editingBomChildId = child.id;
    this.bomChildForm.reset({
      son_pn: child.son_pn,
      qty: child.qty,
      station_code: child.station_code,
    });
    this.isBomChildEditorOpen = true;
  }

  saveBomChild(): void {
    this.bomErrorMessage = '';

    if (this.bomChildForm.invalid) {
      this.bomChildForm.markAllAsTouched();
      this.bomErrorMessage = 'Please fill all required BOM fields.';
      this.scheduleClearMessages();
      return;
    }

    const formValue = this.bomChildForm.value;
    const stationCode = String(formValue.station_code ?? '').trim();
    const selectedStation = this.getAssemblyStationOptions().find((station) => station.station_code === stationCode);

    if (stationCode && !selectedStation) {
      this.bomErrorMessage = 'Please select a valid assembly station.';
      this.scheduleClearMessages();
      return;
    }

    this.isBomChildSaving = true;
    this.isBomChildrenSaved = false;

    if (this.isBomEditMode && this.editingBomChildId !== null) {
      const childIndex = this.bomChildren.findIndex((child) => child.id === this.editingBomChildId);

      if (childIndex >= 0) {
        const previousChild = this.bomChildren[childIndex];
        this.bomChildren[childIndex] = {
          ...previousChild,
          son_pn: String(formValue.son_pn).trim(),
          son_description: String(formValue.son_pn).trim(),
          station_code: selectedStation?.station_code || '',
          station_name: selectedStation?.station_name || '',
          qty: Number(formValue.qty),
        };
        this.addBomHistory('Child updated', 'BOM child', previousChild.son_pn, String(formValue.son_pn).trim());
      }
    } else {
      const child: BomChildRow = {
        id: this.nextBomChildId,
        son_pn: String(formValue.son_pn).trim(),
        son_description: String(formValue.son_pn).trim(),
        station_code: selectedStation?.station_code || '',
        station_name: selectedStation?.station_name || '',
        item_type: 'Manufactured',
        pn_type: '-',
        qty: Number(formValue.qty),
      };

      this.nextBomChildId += 1;
      this.bomChildren = [...this.bomChildren, child];
      this.addBomHistory('Child added', 'BOM child', '-', child.son_pn);
    }

    this.isBomChildSaving = false;
    this.closeBomChildEditor();
  }

  deleteBomChild(child: BomChildRow): void {
    this.isBomChildrenSaved = false;
    this.bomChildren = this.bomChildren.filter((bomChild) => bomChild.id !== child.id);
    this.addBomHistory('Child deleted', 'BOM child', child.son_pn, '-');

    if (this.editingBomChildId === child.id) {
      this.closeBomChildEditor();
    }
  }

  toggleBomHistory(): void {
    this.includeBomHistory = !this.includeBomHistory;
  }

  saveBomChildren(): void {
    this.bomErrorMessage = '';

    if (this.bomChildren.length === 0) {
      this.bomErrorMessage = 'Please add at least one BOM child before saving BOM.';
      this.scheduleClearMessages();
      return;
    }

    this.saveWorkflowSnapshot(
      () => {
        this.isBomChildrenSaved = true;
        this.advanceToNextPane(4);
      },
      (message) => {
        this.bomErrorMessage = message;
        this.scheduleClearMessages();
      }
    );
  }

  onBomStationChange(stationCode: string): void {
    const selectedStation = this.stations.find((station) => station.station_code === stationCode);

    if (selectedStation) {
      this.openStationRulesModal(selectedStation);
    }
  }

  get activeStationRules(): string[] {
    return this.stationRulesByStation[this.activeRulesStationCode] || [];
  }

  openRulesEditor(): void {
    this.stationRulesDraft = this.activeStationRules.join('\n');
    this.isEditingStationRules = true;
  }

  saveStationRules(): void {
    const rules = this.stationRulesDraft
      .split(/\r?\n/)
      .map((rule) => rule.trim())
      .filter(Boolean);

    this.stationRulesByStation = {
      ...this.stationRulesByStation,
      [this.activeRulesStationCode]: rules,
    };
    this.saveWorkflowSnapshot();
    this.isEditingStationRules = false;
  }

  closeStationRulesModal(): void {
    this.isStationRulesModalOpen = false;
    this.isEditingStationRules = false;
    this.activeRulesStationCode = '';
    this.activeRulesStationName = '';
    this.stationRulesDraft = '';
  }

  private loadPnTypes(): void {
    this.http.get<PnType[]>(this.pnTypesApiUrl).subscribe({
      next: (types) => {
        this.pnTypes = (types || []).filter((type) => type.status !== 'Inactive');
      },
      error: () => {
        this.pnTypes = [];
        this.partNumberErrorMessage = 'Unable to load PN types.';
        this.scheduleClearMessages();
      }
    });
  }

  private loadSnTypes(): void {
    this.http.get<{ data: SnType[] } | SnType[]>(this.snTypesApiUrl).subscribe({
      next: (response) => {
        const types = Array.isArray(response) ? response : (response.data || []);
        this.snTypes = types || [];
      },
      error: () => {
        this.snTypes = [];
        this.partNumberErrorMessage = 'Unable to load Serial Pattern values.';
        this.scheduleClearMessages();
      }
    });
  }

  private loadSites(): void {
    this.http.get<Site[]>(this.sitesApiUrl).subscribe({
      next: (sites) => {
        this.sites = sites || [];
        this.syncSelectedSiteWithPlant(String(this.workOrderForm.get('plant')?.value ?? ''));
      },
      error: () => {
        this.sites = [];
        this.workOrderErrorMessage = 'Unable to load sites.';
        this.scheduleClearMessages();
      }
    });
  }

  private loadStations(): void {
    this.isStationsLoading = true;

    const params = new HttpParams().set('limit', 'all').set('page', '1');
    this.http.get<StationsResponse>(this.stationsApiUrl, { params }).subscribe({
      next: (response) => {
        this.stations = (response.data || []).filter((station) => station.status === 'Active');
        this.isStationsLoading = false;
      },
      error: () => {
        this.stations = [];
        this.isStationsLoading = false;
      }
    });
  }

  private buildPartNumberMissingFieldsMessage(): string {
    const missing: string[] = [];

    if (this.partNumberForm.get('pn')?.invalid) missing.push('Part number');
    if (this.partNumberForm.get('description')?.invalid) missing.push('Description');
    if (this.partNumberForm.get('item_type')?.invalid) missing.push('Item Type');
    if (this.partNumberForm.get('pn_type_id')?.invalid) missing.push('PN Type');

    return `Please fill required fields: ${missing.join(', ')}`;
  }

  private buildWorkOrderMissingFieldsMessage(): string {
    const missing: string[] = [];

    if (this.workOrderForm.get('wo')?.invalid) missing.push('WO');
    if (this.workOrderForm.get('plant')?.invalid) missing.push('Plant');
    if (this.workOrderForm.get('site_id')?.invalid) missing.push('Site');
    if (this.workOrderForm.get('due_date')?.invalid) missing.push('Due Date');
    if (this.workOrderForm.get('qty')?.invalid) missing.push('Quantity');
    if (this.workOrderForm.get('status')?.invalid) missing.push('Status');
    if (this.workOrderForm.get('pn')?.invalid) missing.push('PN');
    if (this.workOrderForm.get('revision')?.invalid) missing.push('Revision');

    return `Please fill required fields: ${missing.join(', ')}`;
  }

  private syncPartNumberToWorkOrder(): void {
    const partNumber = String(this.partNumberForm.get('pn')?.value ?? '').trim();
    this.workOrderForm.patchValue({ pn: partNumber }, { emitEvent: false });
  }

  private syncPartNumberToRouting(): void {
    this.linkedRoutingPartNumber = String(this.partNumberForm.get('pn')?.value ?? '').trim();
    this.linkedRoutingDescription = String(this.partNumberForm.get('description')?.value ?? '').trim();
  }

  private syncPartNumberToBom(): void {
    this.linkedBomPartNumber = this.routingPartNumber;
  }

  private getPreviewStationStatus(index: number, step: RoutingStepRow): PreviewStatus {
    const savedStatus = this.previewStationStatusById[step.id];
    if (savedStatus) {
      return savedStatus;
    }

    return index === 0 ? 'In Progress' : 'Pending';
  }

  private setPreviewStationStatus(status: PreviewStatus): void {
    if (!this.activePreviewStation) {
      return;
    }

    this.previewStationStatusById = {
      ...this.previewStationStatusById,
      [this.activePreviewStation.id]: status,
    };
    this.activePreviewStation = {
      ...this.activePreviewStation,
      status,
    };
    this.queuePreviewConnectorRefresh();
    this.saveWorkflowSnapshot();
    this.closePreviewStationDetails();
  }

  private buildPreviewConnectorSignature(): string {
    const flowIds = this.previewFlowNodes.map((node) => node.id).join('|');
    const routeSignature = this.routeSteps
      .map((step) => `${step.id}:${step.station_code}:${step.sample_mode}`)
      .join('|');

    return `${this.activeTabIndex}:${this.previewFlowCardsPerRow}:${flowIds}:${routeSignature}`;
  }

  private queuePreviewConnectorRefresh(): void {
    if (typeof window === 'undefined') {
      return;
    }

    if (this.previewConnectorFrame) {
      window.cancelAnimationFrame(this.previewConnectorFrame);
    }

    this.previewConnectorFrame = window.requestAnimationFrame(() => {
      this.previewConnectorFrame = null;
      this.updatePreviewConnectorPath();
    });
  }

  private updatePreviewConnectorPath(): void {
    const container = this.previewProcessFlowRef?.nativeElement;
    const nodeRefs = this.previewFlowNodeRefs?.toArray() || [];

    if (!container || this.activeTabIndex !== 4 || nodeRefs.length < 2) {
      this.setPreviewConnector('', 0, 0);
      return;
    }

    const containerRect = container.getBoundingClientRect();
    const nodeRectsById = new Map<string, DOMRect>();

    nodeRefs.forEach((nodeRef) => {
      const flowId = nodeRef.nativeElement.dataset['flowId'];

      if (flowId) {
        nodeRectsById.set(flowId, nodeRef.nativeElement.getBoundingClientRect());
      }
    });

    const orderedRects = this.previewFlowNodes
      .map((node) => nodeRectsById.get(node.id))
      .filter((rect): rect is DOMRect => Boolean(rect));

    if (orderedRects.length < 2) {
      this.setPreviewConnector('', 0, 0);
      return;
    }

    const pathSegments: string[] = [];

    for (let index = 0; index < orderedRects.length - 1; index += 1) {
      const currentRect = orderedRects[index];
      const nextRect = orderedRects[index + 1];
      const currentCenter = this.getRelativeRectCenter(currentRect, containerRect);
      const nextCenter = this.getRelativeRectCenter(nextRect, containerRect);
      const sameRow = Math.abs(currentCenter.y - nextCenter.y) < 28;

      if (sameRow) {
        const flowsRight = nextCenter.x >= currentCenter.x;
        const startX = flowsRight ? currentRect.right - containerRect.left : currentRect.left - containerRect.left;
        const endX = flowsRight ? nextRect.left - containerRect.left : nextRect.right - containerRect.left;
        const y = (currentCenter.y + nextCenter.y) / 2;
        pathSegments.push(`M ${startX} ${y} L ${endX} ${y}`);
      } else {
        const flowsDown = nextCenter.y >= currentCenter.y;
        const startX = currentCenter.x;
        const startY = flowsDown ? currentRect.bottom - containerRect.top : currentRect.top - containerRect.top;
        const endX = nextCenter.x;
        const endY = flowsDown ? nextRect.top - containerRect.top : nextRect.bottom - containerRect.top;
        const midY = startY + ((endY - startY) / 2);
        pathSegments.push(`M ${startX} ${startY} L ${startX} ${midY} L ${endX} ${midY} L ${endX} ${endY}`);
      }
    }

    this.setPreviewConnector(pathSegments.join(' '), containerRect.width, containerRect.height);
  }

  private getRelativeRectCenter(rect: DOMRect, containerRect: DOMRect): { x: number; y: number } {
    return {
      x: rect.left - containerRect.left + (rect.width / 2),
      y: rect.top - containerRect.top + (rect.height / 2),
    };
  }

  private setPreviewConnector(path: string, width: number, height: number): void {
    if (
      this.previewConnectorPath === path &&
      this.previewConnectorWidth === width &&
      this.previewConnectorHeight === height
    ) {
      return;
    }

    this.previewConnectorPath = path;
    this.previewConnectorWidth = width;
    this.previewConnectorHeight = height;
    this.cdr.detectChanges();
  }

  private getPreviewFlowCardsPerRow(): number {
    const width = typeof window === 'undefined' ? 1400 : window.innerWidth;
    const availablePreviewWidth = Math.max(360, width - 260);
    const estimatedCardWidth = width >= 1500 ? 144 : 156;
    const estimatedLineWidth = width >= 1500 ? 34 : 40;
    const estimatedCards = Math.floor(
      (availablePreviewWidth + estimatedLineWidth) / (estimatedCardWidth + estimatedLineWidth)
    );

    return Math.max(2, Math.min(10, estimatedCards));
  }

  private getPreviewStationIcon(index: number, step: RoutingStepRow): string {
    const normalizedName = `${step.station_name} ${step.station_code}`.toLowerCase();

    if (step.sample_mode === 'Sample') {
      return 'saved_search';
    }

    if (normalizedName.includes('label')) {
      return 'qr_code_2';
    }

    if (normalizedName.includes('test') || normalizedName.includes('aoi')) {
      return 'biotech';
    }

    if (normalizedName.includes('pack') || normalizedName.includes('box')) {
      return 'inventory_2';
    }

    const icons = ['desktop_windows', 'verified_user', 'precision_manufacturing', 'memory', 'settings_applications'];
    return icons[index % icons.length];
  }

  private closeRoutingStepEditor(): void {
    this.isRoutingStepEditorOpen = false;
    this.isRoutingEditMode = false;
    this.editingRoutingStepId = null;
    this.routingStepForm.reset({
      station_code: '',
      sample_mode: 'Full',
      report_mode: 'Regular',
    });
  }

  private getNextStationOrder(): number {
    if (!this.routeSteps.length) {
      return 10;
    }

    return Math.max(...this.routeSteps.map((step) => Number(step.station_order) || 0)) + 10;
  }

  private normalizeRouteStepOrder(): void {
    this.routeSteps = this.routeSteps.map((step, index) => ({
      ...step,
      station_order: (index + 1) * 10,
    }));
  }

  private addRoutingHistory(description: string, changeField: string, oldValue: string, newValue: string): void {
    this.routeHistory = [
      {
        id: this.nextRoutingHistoryId,
        description,
        change_field: changeField,
        old_value: oldValue,
        new_value: newValue,
        changed_by: 'workflow',
        changed_at: new Date().toISOString(),
      },
      ...this.routeHistory,
    ];
    this.nextRoutingHistoryId += 1;
  }

  private closeBomChildEditor(): void {
    this.isBomChildEditorOpen = false;
    this.isBomEditMode = false;
    this.editingBomChildId = null;
    this.bomChildForm.reset({
      son_pn: '',
      qty: 1,
      station_code: '',
    });
  }

  private getSiteOptionsForPlant(plant: string): Site[] {
    if (!plant) {
      return [];
    }

    const normalizedPlant = this.normalizeLookupValue(plant);
    const matchedSites = this.sites.filter((site) =>
      this.normalizeLookupValue(site.plant || site.name).includes(normalizedPlant)
    );

    if (matchedSites.length) {
      return matchedSites;
    }

    return (this.fallbackSitesByPlant[plant] || []).map((name, index) => ({
      id: this.getFallbackSiteId(plant, index),
      name,
      plant,
    }));
  }

  private syncSelectedSiteWithPlant(plant: string): void {
    const siteControl = this.workOrderForm.get('site_id');
    const selectedSiteId = siteControl?.value;

    if (selectedSiteId === null || selectedSiteId === undefined || selectedSiteId === '') {
      return;
    }

    const hasValidSite = this.getSiteOptionsForPlant(plant).some((site) => Number(site.id) === Number(selectedSiteId));

    if (!hasValidSite) {
      siteControl?.setValue(null);
    }
  }

  private getFallbackSiteId(plant: string, index: number): number {
    const plantIndex = Math.max(this.plantOptions.indexOf(plant), 0);
    return -(((plantIndex + 1) * 100) + index + 1);
  }

  private normalizeLookupValue(value: string): string {
    return value.toLowerCase().replace(/[^a-z0-9]/g, '');
  }

  private getStationRuleText(stationCode: string): string {
    return `${stationCode} Rule`;
  }

  private openStationRulesModal(station: StationOption): void {
    this.activeRulesStationCode = station.station_code;
    this.activeRulesStationName = station.station_desc;
    this.stationRulesDraft = this.activeStationRules.join('\n');
    this.isEditingStationRules = false;
    this.isStationRulesModalOpen = true;
  }

  private restoreSavedPreviewForPartNumber(partNumber: string): void {
    if (!partNumber) {
      return;
    }

    const params = new HttpParams().set('pn', partNumber);
    this.http.get<WorkflowSnapshot>(`${this.workflowApiUrl}/by-pn`, { params }).subscribe({
      next: (snapshot) => {
        this.applyWorkflowSnapshot(snapshot);
        this.previewActionMessageType = 'success';
        this.previewActionMessage = 'Saved workflow loaded from database.';
        this.scheduleClearMessages();
      },
      error: (error) => {
        if (error?.status && error.status !== 404) {
          this.partNumberErrorMessage = this.getWorkflowErrorMessage(error);
          this.scheduleClearMessages();
        }
      }
    });
  }

  private resetWorkflowForNewPartNumber(): void {
    this.isRestoringSavedPreview = true;

    this.partNumberForm.reset({
      pn: '',
      description: '',
      sgd_control: false,
      item_type: null,
      sn_type_name: '',
      pn_type_id: null,
    }, { emitEvent: false });

    this.workOrderForm.reset({
      wo: '',
      plant: null,
      site_id: null,
      due_date: this.minDueDate,
      qty: null,
      status: 'Released',
      pn: '',
      revision: '',
      lot: '',
    }, { emitEvent: false });

    this.routingStepForm.reset({
      station_code: '',
      sample_mode: 'Full',
      report_mode: 'Regular',
    }, { emitEvent: false });

    this.bomChildForm.reset({
      son_pn: '',
      qty: 1,
      station_code: '',
    }, { emitEvent: false });

    this.isPartNumberSaved = false;
    this.isWorkOrderSaved = false;
    this.isRoutingChildrenSaved = false;
    this.isBomChildrenSaved = false;
    this.isPreviewSaved = false;
    this.isRoutingStepEditorOpen = false;
    this.isRoutingEditMode = false;
    this.isBomChildEditorOpen = false;
    this.isBomEditMode = false;
    this.includeRoutingHistory = false;
    this.includeBomHistory = false;
    this.showSavePreviousWorkPopup = false;
    this.isStationRulesModalOpen = false;
    this.isEditingStationRules = false;
    this.isChildDetailsOpen = false;
    this.activePreviewStation = null;
    this.activeRulesStationCode = '';
    this.activeRulesStationName = '';
    this.stationRulesDraft = '';
    this.previewStationStatusById = {};
    this.linkedRoutingPartNumber = '';
    this.linkedRoutingDescription = '';
    this.linkedBomPartNumber = '';
    this.routeSteps = [];
    this.routeHistory = [];
    this.bomChildren = [];
    this.bomHistory = [];
    this.nextRoutingStepId = 1;
    this.nextRoutingHistoryId = 1;
    this.nextBomChildId = 1;
    this.nextBomHistoryId = 1;
    this.editingRoutingStepId = null;
    this.editingBomChildId = null;
    this.previewActionMessage = '';
    this.previewFlowCardsPerRow = this.getPreviewFlowCardsPerRow();
    this.activeTabIndex = 0;
    this.isRestoringSavedPreview = false;
  }

  private saveWorkflowSnapshot(onSuccess?: () => void, onError?: (message: string) => void): void {
    const payload = this.buildWorkflowSnapshotPayload();

    this.http.post<WorkflowSnapshot>(`${this.workflowApiUrl}/snapshot`, payload).subscribe({
      next: (snapshot) => {
        this.applyWorkflowSnapshot(snapshot);
        onSuccess?.();
      },
      error: (error) => {
        onError?.(this.getWorkflowErrorMessage(error));
      }
    });
  }

  private buildWorkflowSnapshotPayload(): object {
    const partNumber = this.partNumberForm.getRawValue();
    const workOrder = this.workOrderForm.getRawValue();
    const siteName = this.previewSiteName === 'Select Site' ? '' : this.previewSiteName;

    return {
      partNumber,
      workOrder: {
        ...workOrder,
        site_id: this.toNullableNumber(workOrder.site_id),
        site_name: siteName,
        qty: this.toNullableNumber(workOrder.qty),
      },
      routing: this.routeSteps.map((step) => ({
        ...step,
        preview_status: this.previewStationStatusById[step.id] || null,
      })),
      bom: this.bomChildren.map((child) => ({
        ...child,
        qty: this.toNullableNumber(child.qty) || 1,
      })),
      stationRules: this.stationRulesByStation,
      previewStatuses: this.buildPreviewStatusesByStationCode(),
    };
  }

  private applyWorkflowSnapshot(snapshot: WorkflowSnapshot): void {
    if (!snapshot?.partNumber?.pn) {
      return;
    }

    const partNumber = snapshot.partNumber;
    this.isRestoringSavedPreview = true;

    this.partNumberForm.patchValue({
      pn: partNumber.pn || '',
      description: partNumber.description || '',
      sgd_control: Boolean(partNumber.sgd_control),
      item_type: partNumber.item_type || null,
      sn_type_name: partNumber.sn_type_name || '',
      pn_type_id: partNumber.pn_type_id ?? null,
    }, { emitEvent: false });

    this.workOrderForm.patchValue({
      wo: snapshot.workOrder?.wo || '',
      plant: snapshot.workOrder?.plant || null,
      site_id: snapshot.workOrder?.site_id ?? null,
      due_date: String(snapshot.workOrder?.due_date || this.minDueDate).slice(0, 10),
      qty: snapshot.workOrder?.qty ?? null,
      status: snapshot.workOrder?.status || 'Released',
      pn: partNumber.pn || '',
      revision: snapshot.workOrder?.revision || '',
      lot: snapshot.workOrder?.lot || '',
    }, { emitEvent: false });

    this.routeSteps = (snapshot.routing || []).map((step, index) => ({
      id: Number(step.id) || index + 1,
      station_order: Number(step.station_order) || ((index + 1) * 10),
      station_code: step.station_code,
      station_name: step.station_name,
      sample_mode: step.sample_mode,
      report_mode: step.report_mode,
    }));

    this.bomChildren = (snapshot.bom || []).map((child, index) => ({
      id: Number(child.id) || index + 1,
      son_pn: child.son_pn,
      son_description: child.son_description || child.son_pn,
      station_code: child.station_code || '',
      station_name: child.station_name || '',
      item_type: child.item_type || 'Manufactured',
      pn_type: child.pn_type || '-',
      qty: Number(child.qty) || 1,
    }));

    const statusesByStation = snapshot.previewStatuses || {};
    this.previewStationStatusById = this.routeSteps.reduce<Record<number, PreviewStatus>>((statuses, step) => {
      const loadedStatus = statusesByStation[step.station_code] || (snapshot.routing || []).find((row) => row.station_code === step.station_code)?.preview_status || null;
      if (loadedStatus) {
        statuses[step.id] = loadedStatus;
      }

      return statuses;
    }, {});

    this.stationRulesByStation = snapshot.stationRules || {};
    this.linkedRoutingPartNumber = partNumber.pn || '';
    this.linkedRoutingDescription = partNumber.description || '';
    this.linkedBomPartNumber = partNumber.pn || '';
    this.nextRoutingStepId = Math.max(0, ...this.routeSteps.map((step) => step.id)) + 1;
    this.nextBomChildId = Math.max(0, ...this.bomChildren.map((child) => child.id)) + 1;
    this.isPartNumberSaved = true;
    this.isWorkOrderSaved = Boolean(snapshot.workOrder?.wo);
    this.isRoutingChildrenSaved = this.routeSteps.length > 0;
    this.isBomChildrenSaved = this.bomChildren.length > 0;
    this.isRestoringSavedPreview = false;
    this.queuePreviewConnectorRefresh();
  }

  private buildPreviewStatusesByStationCode(): Record<string, PreviewStatus> {
    return this.routeSteps.reduce<Record<string, PreviewStatus>>((statuses, step) => {
      const status = this.previewStationStatusById[step.id];
      if (status) {
        statuses[step.station_code] = status;
      }

      return statuses;
    }, {});
  }

  private toNullableNumber(value: unknown): number | null {
    if (value === null || value === undefined || value === '') {
      return null;
    }

    const numberValue = Number(value);
    return Number.isFinite(numberValue) ? numberValue : null;
  }

  private getWorkflowErrorMessage(error: any): string {
    return error?.error?.message || error?.error?.error || 'Unable to save workflow data.';
  }

  private addBomHistory(description: string, changeField: string, oldValue: string, newValue: string): void {
    this.bomHistory = [
      {
        id: this.nextBomHistoryId,
        description,
        change_field: changeField,
        old_value: oldValue,
        new_value: newValue,
        changed_by: 'workflow',
        changed_at: new Date().toISOString(),
      },
      ...this.bomHistory,
    ];
    this.nextBomHistoryId += 1;
  }

  private advanceToNextPane(index: number): void {
    if (this.advancePaneTimer) {
      window.clearTimeout(this.advancePaneTimer);
    }

    this.advancePaneTimer = window.setTimeout(() => {
      this.selectTab(index);
      this.advancePaneTimer = null;
    }, 650);
  }

  private scheduleClearMessages(): void {
    if (this.clearMessageTimer) {
      window.clearTimeout(this.clearMessageTimer);
    }

    this.clearMessageTimer = window.setTimeout(() => {
      this.partNumberErrorMessage = '';
      this.workOrderErrorMessage = '';
      this.routingErrorMessage = '';
      this.bomErrorMessage = '';
      this.previewActionMessage = '';
      this.clearMessageTimer = null;
    }, 3500);
  }

  private futureDateValidator(control: AbstractControl): ValidationErrors | null {
    const value = String(control.value ?? '');

    if (!value) {
      return null;
    }

    return value >= this.minDueDate ? null : { futureDate: true };
  }

  private getDateInputValue(daysFromToday: number): string {
    const date = new Date();
    date.setDate(date.getDate() + daysFromToday);
    date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
    return date.toISOString().slice(0, 10);
  }
}
