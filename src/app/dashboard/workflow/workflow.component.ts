import { HttpClient, HttpParams } from '@angular/common/http';
import {
  AfterViewChecked,
  AfterViewInit,
  ChangeDetectorRef,
  Component,
  ElementRef,
  HostListener,
  OnInit,
  OnDestroy,
  QueryList,
  ViewChild,
  ViewChildren,
} from '@angular/core';
import { AbstractControl, FormBuilder, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { environment } from '../../../environments/environment';
import { LabelMasterDto, LabelService } from '../../services/label.service';

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
  used_by_pn?: string | null;
};

type FatherSnType = {
  father_pn: string;
  sn_type_name: string;
};

type PartAttributeValueKind = 'text' | 'number' | 'date' | 'boolean';

type PartAttributeOption = {
  key: string;
  label: string;
  valueKind: PartAttributeValueKind;
  placeholder: string;
  hint: string;
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
  station_login_id?: string;
  station_login_password?: string;
  station_ip?: string;
  printer_ip?: string;
  sn_count?: number | null;
  preview_sn_count?: number | null;
  sn_count_label?: string | null;
  preview_sn_count_label?: string | null;
};

type PrinterStatus = 'Online' | 'Offline';

type PrinterOption = {
  id: string;
  status: PrinterStatus;
  ipAddress: string;
  port: string;
};

type StationLabelPrintingConfig = {
  stationId: number | null;
  stationName: string;
  isLabelPrintingEnabled: boolean;
  labelCode: string;
  labelDescription: string;
  printerId: string;
  printerName: string;
  ipAddress: string;
  port: string;
  status: PrinterStatus;
};

type StationWeighingConfig = {
  stationId: number | null;
  stationName: string;
  isWeighingEnabled: boolean;
  minimumWeight: string;
  maximumWeight: string;
  tolerance: string;
};

type SamplingType = 'PERIODIC' | 'RANDOM' | 'LOT' | 'FIRST_PIECE';

type StationSamplingConfig = {
  stationId: number | null;
  stationName: string;
  isSamplingEnabled: boolean;
  samplingType: SamplingType;
  intervalQty: string;
  sampleQty: string;
  lotSize: string;
};

type StationRepairConfig = {
  stationId: number | null;
  stationName: string;
  isRepairStationEnabled: boolean;
  repairStationName: string;
};

type StationRulesTabId = 'weighing' | 'label-printing' | 'sampling' | 'repair-station';

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

type PreviewStatus = 'Passed' | 'In Progress' | 'Pending' | 'Skipped';

type PreviewStationNode = RoutingStepRow & {
  flowIndex: number;
  icon: string;
  status: PreviewStatus;
  snCount: number;
  isHighSnCount: boolean;
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
    box_qty?: number | null;
    part_attribute_key?: string | null;
    part_attribute_value?: string | null;
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
  stationLabelPrinting?: Record<string, StationLabelPrintingConfig>;
  stationWeighing?: Record<string, StationWeighingConfig>;
  stationSampling?: Record<string, StationSamplingConfig>;
  stationRepair?: Record<string, StationRepairConfig>;
  previewStatuses?: Record<string, PreviewStatus>;
};

@Component({
  selector: 'app-workflow',
  standalone: false,
  templateUrl: './workflow.component.html',
  styleUrl: './workflow.component.scss'
})
export class WorkflowComponent implements OnInit, AfterViewInit, AfterViewChecked, OnDestroy {
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
  readonly stationRuleTabs: Array<{ id: StationRulesTabId; label: string; icon: string }> = [
    { id: 'weighing', label: 'Weighing', icon: 'scale' },
    { id: 'label-printing', label: 'Label Printing', icon: 'print' },
    { id: 'sampling', label: 'Sampling', icon: 'fact_check' },
    { id: 'repair-station', label: 'Repair Station', icon: 'build' },
  ];
  readonly samplingTypeOptions: Array<{ value: SamplingType; label: string }> = [
    { value: 'PERIODIC', label: 'Periodic' },
    { value: 'RANDOM', label: 'Random' },
    { value: 'LOT', label: 'Lot / Batch' },
    { value: 'FIRST_PIECE', label: 'First Piece' },
  ];
  readonly bomItemTypeOptions: Array<'Manufactured' | 'Purchased'> = ['Manufactured', 'Purchased'];
  readonly partAttributeOptions: PartAttributeOption[] = [
    { key: 'sgd_control', label: 'SGD Control', valueKind: 'boolean', placeholder: 'Select Yes or No', hint: 'Choose whether SGD control applies to this part.' },
    { key: 'box_qty_limit', label: 'Box QTY Limit', valueKind: 'number', placeholder: 'Enter box quantity limit', hint: 'Enter the allowed box quantity limit.' },
    { key: 'ean', label: 'EAN', valueKind: 'text', placeholder: 'Enter EAN', hint: 'Enter the EAN value for this part.' },
    { key: 'mrp', label: 'MRP', valueKind: 'number', placeholder: 'Enter MRP', hint: 'Enter the MRP value.' },
    { key: 'modelno', label: 'Model No', valueKind: 'text', placeholder: 'Enter model number', hint: 'Enter the model number.' },
    { key: 'po', label: 'PO Number', valueKind: 'text', placeholder: 'Enter PO number', hint: 'Enter the purchase order number.' },
    { key: 'po_date', label: 'PO Date', valueKind: 'date', placeholder: 'Select PO date', hint: 'Select the purchase order date.' },
    { key: 'color', label: 'Color', valueKind: 'text', placeholder: 'Enter color', hint: 'Enter the part color.' },
    { key: 'field6', label: 'field6', valueKind: 'text', placeholder: 'Enter field6 value', hint: 'Enter the value for field6.' },
    { key: 'field7', label: 'field7', valueKind: 'text', placeholder: 'Enter field7 value', hint: 'Enter the value for field7.' },
    { key: 'field8', label: 'field8', valueKind: 'text', placeholder: 'Enter field8 value', hint: 'Enter the value for field8.' },
    { key: 'field9', label: 'field9', valueKind: 'text', placeholder: 'Enter field9 value', hint: 'Enter the value for field9.' },
    { key: 'field10', label: 'field10', valueKind: 'text', placeholder: 'Enter field10 value', hint: 'Enter the value for field10.' },
    { key: 'po_qty', label: 'PO Qty', valueKind: 'number', placeholder: 'Enter PO quantity', hint: 'Enter the purchase order quantity.' },
    { key: 'sw_ver', label: 'Software Version', valueKind: 'text', placeholder: 'Enter software version', hint: 'Enter the software version.' },
    { key: 'hw_ver', label: 'Hardware Version', valueKind: 'text', placeholder: 'Enter hardware version', hint: 'Enter the hardware version.' },
  ];
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
  fatherSnTypes: FatherSnType[] = [];
  workOrderErrorMessage = '';
  routingErrorMessage = '';
  bomErrorMessage = '';
  isExistingPartNumberReadonly = false;
  arePartNumberDetailsReadonly = false;
  isWorkOrderReadonly = false;
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
  isWeighingEnabled = false;
  weighingMinimum = '';
  weighingMaximum = '';
  weighingTolerance = '';
  isSamplingEnabled = false;
  samplingType: SamplingType = 'PERIODIC';
  samplingIntervalQty = '10';
  samplingSampleQty = '1';
  samplingLotSize = '1000';
  isRepairStationEnabled = false;
  repairStationName = '';
  stationRulesByStation: Record<string, string[]> = {};
  stationLabelPrintingByStation: Record<string, StationLabelPrintingConfig> = {};
  stationWeighingByStation: Record<string, StationWeighingConfig> = {};
  stationSamplingByStation: Record<string, StationSamplingConfig> = {};
  stationRepairByStation: Record<string, StationRepairConfig> = {};
  activeStationRulesTab: StationRulesTabId = 'weighing';
  isLabelPrintingEnabled = false;
  labelPrintCode = '';
  selectedLabelDescription = '';
  labelPrintValidationMessage = '';
  selectedPrinterId = '';
  labelPrintStatusMessage = '';
  labelPrintStatusType: 'success' | 'error' | 'info' = 'info';
  isTestPrintLoading = false;
  testPrintPreviewUrl = '';
  testPrintPreviewText = '';
  testPrintPageSize = { width: '4', height: '6' };
  availableLabels: LabelMasterDto[] = [];
  isStationLoginModalOpen = false;
  isEditingStationLogin = true;
  activeStationLoginStep: RoutingStepRow | null = null;
  stationLoginForm: FormGroup;
  stationLoginErrorMessage = '';
  stationLoginSuccessMessage = '';
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
  private lockedEditPartNumber = '';
  private lockedEditWorkOrder = '';
  private readonly partNumberDetailControls = [
    'description',
    'part_attribute_key',
    'part_attribute_value',
    'item_type',
    'sn_type_name',
    'pn_type_id',
  ];
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
    private route: ActivatedRoute,
    private labelService: LabelService,
    private cdr: ChangeDetectorRef
  ) {
    this.partNumberForm = this.fb.group({
      pn: ['', Validators.required],
      description: ['', Validators.required],
      part_attribute_key: [''],
      part_attribute_value: [''],
      sgd_control: [false],
      item_type: [null, Validators.required],
      sn_type_name: [''],
      pn_type_id: [null, Validators.required],
      box_qty: [null, [Validators.min(1), Validators.pattern(/^[0-9]+$/)]],
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

    this.stationLoginForm = this.fb.group({
      station_login_id: ['', Validators.required],
      station_login_password: ['', Validators.required],
      station_ip: ['', Validators.required],
      printer_ip: ['', Validators.required],
    });

    this.bomChildForm = this.fb.group({
      son_pn: ['', Validators.required],
      qty: [1, [Validators.required, Validators.min(1), Validators.pattern(/^[0-9]+$/)]],
      item_type: ['', Validators.required],
      station_code: [''],
    });

    this.loadPnTypes();
    this.loadSnTypes();
    this.loadSites();
    this.loadStations();
    this.loadLabels();

    this.partNumberForm.get('pn')?.valueChanges.subscribe((value) => {
      if (this.isRestoringSavedPreview) {
        return;
      }

      this.isPartNumberSaved = false;
      this.setPartNumberReadonlyState(false, false);
      const partNumber = String(value ?? '').trim();
      if (this.restoreWorkflowTimer) {
        window.clearTimeout(this.restoreWorkflowTimer);
      }

      if (partNumber.length < 2) {
        this.fatherSnTypes = [];
        return;
      }

      this.loadFatherSnTypes(partNumber);

      this.restoreWorkflowTimer = window.setTimeout(() => {
        this.restoreWorkflowTimer = null;
        this.restoreSavedPreviewForPartNumber(partNumber);
      }, 350);
    });

    this.partNumberDetailControls.forEach((controlName) => {
      this.partNumberForm.get(controlName)?.valueChanges.subscribe(() => {
        if (!this.isRestoringSavedPreview) {
          this.isPartNumberSaved = false;
        }

        if (controlName === 'sn_type_name') {
          this.validateFatherSnTypeSelection();
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

  ngOnInit(): void {
    this.route.queryParamMap.subscribe((params) => {
      const partNumber = String(params.get('pn') || '').trim();
      const workOrder = String(params.get('wo') || '').trim();

      if (!partNumber && !workOrder) {
        this.clearWorkflowEditLocks();
        return;
      }

      this.lockedEditPartNumber = partNumber;
      this.lockedEditWorkOrder = workOrder;
      this.applyWorkflowEditLocks();

      if (partNumber) {
        this.partNumberForm.patchValue({ pn: partNumber }, { emitEvent: false });
        this.workOrderForm.patchValue({ pn: partNumber }, { emitEvent: false });
        this.restoreSavedPreviewForPartNumber(partNumber, workOrder);
      }

      if (workOrder) {
        this.workOrderForm.patchValue({ wo: workOrder }, { emitEvent: false });
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

    this.revokeTestPrintPreviewUrl();
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

  getWorkflowActionLabel(isSaved: boolean, saveLabel = 'Save', updateLabel = 'Update'): string {
    if (isSaved) {
      return 'Saved';
    }

    return this.isWorkflowEditMode ? updateLabel : saveLabel;
  }

  canSaveBomChildren(): boolean {
    return this.bomChildren.length > 0 || this.bomHistory.length > 0 || this.isBomChildrenSaved;
  }

  private canLeavePane(index: number): boolean {
    if (index === 4) {
      return true;
    }

    if (index === 3) {
      return this.isBomPaneComplete();
    }

    return this.isPaneSaved(index);
  }

  private isBomPaneComplete(): boolean {
    if (this.isBomChildrenSaved) {
      return true;
    }

    return this.bomChildren.length === 0 && this.bomHistory.length === 0 && !this.hasBomChildDraft();
  }

  private hasBomChildDraft(): boolean {
    const value = this.bomChildForm.getRawValue();
    const qty = String(value.qty ?? '').trim();

    return Boolean(
      String(value.son_pn ?? '').trim() ||
      String(value.item_type ?? '').trim() ||
      String(value.station_code ?? '').trim() ||
      (qty !== '' && qty !== '1')
    );
  }

  private get isWorkflowEditMode(): boolean {
    return Boolean(this.lockedEditPartNumber || this.lockedEditWorkOrder);
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

  get selectedPartAttributeOption(): PartAttributeOption | null {
    const key = String(this.partNumberForm.get('part_attribute_key')?.value ?? '').trim();
    return this.partAttributeOptions.find((option) => option.key === key) || null;
  }

  get selectedPartAttributePlaceholder(): string {
    return this.selectedPartAttributeOption?.placeholder || 'Select an attribute first';
  }

  get selectedPartAttributeHint(): string {
    return this.selectedPartAttributeOption?.hint || 'Choose one extra part attribute to define for this part number.';
  }

  get selectedPartAttributeValueHint(): string {
    return this.selectedPartAttributeOption
      ? `Value for ${this.selectedPartAttributeOption.label}`
      : 'Select an attribute first, then enter its value here.';
  }

  onPartAttributeChange(): void {
    this.partNumberForm.get('part_attribute_value')?.setValue('', { emitEvent: false });
    this.partNumberForm.get('part_attribute_value')?.setErrors(null);
    this.syncPartAttributeSelection();
  }

  onPartAttributeValueChange(): void {
    this.partNumberForm.get('part_attribute_value')?.setErrors(null);
    this.syncPartAttributeSelection();
  }

  savePartNumber(): void {
    this.partNumberErrorMessage = '';

    if (!this.validatePartAttributeValue()) {
      this.partNumberForm.markAllAsTouched();
      this.scheduleClearMessages();
      return;
    }

    if (this.isExistingPartNumberReadonly) {
      this.isPartNumberSaved = true;
      this.syncPartNumberToWorkOrder();
      this.syncPartNumberToRouting();
      this.advanceToNextPane(1);
      return;
    }

    if (this.partNumberForm.invalid) {
      this.partNumberForm.markAllAsTouched();
      this.partNumberErrorMessage = this.buildPartNumberMissingFieldsMessage();
      this.scheduleClearMessages();
      return;
    }

    if (!this.validateFatherSnTypeSelection()) {
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
        station_login_id: '',
        station_login_password: '',
        station_ip: '',
        printer_ip: '',
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

  getStationLoginLabel(step: RoutingStepRow | null): string {
    const stationCode = String(step?.station_code || '').trim();
    return stationCode ? `${stationCode} Login` : 'Station Login';
  }

  openRoutingStationRules(step: RoutingStepRow): void {
    this.activePreviewStation = null;
    this.activeRulesStationCode = step.station_code;
    this.activeRulesStationName = step.station_name;
    this.stationRulesDraft = (this.stationRulesByStation[step.station_code] || []).join('\n');
    this.loadWeighingDraft(step.station_code);
    this.loadSamplingDraft(step.station_code);
    this.loadRepairDraft(step.station_code);
    this.isEditingStationRules = false;
    this.activeStationRulesTab = 'weighing';
    this.loadLabelPrintingDraft(step.station_code);
    this.isStationRulesModalOpen = true;
  }

  openStationLoginModal(step: RoutingStepRow): void {
    this.activePreviewStation = null;
    this.activeStationLoginStep = step;
    this.stationLoginForm.reset({
      station_login_id: step.station_login_id || '',
      station_login_password: step.station_login_password || '',
      station_ip: step.station_ip || '',
      printer_ip: step.printer_ip || '',
    });
    this.isEditingStationLogin = !this.hasStationLoginDetails(step);
    this.stationLoginErrorMessage = '';
    this.stationLoginSuccessMessage = '';
    this.isStationLoginModalOpen = true;
  }

  enableStationLoginEdit(): void {
    this.isEditingStationLogin = true;
    this.stationLoginErrorMessage = '';
    this.stationLoginSuccessMessage = '';
  }

  saveStationLogin(): void {
    this.stationLoginErrorMessage = '';
    this.stationLoginSuccessMessage = '';

    if (this.stationLoginForm.invalid) {
      this.stationLoginForm.markAllAsTouched();
      this.stationLoginErrorMessage = 'Please fill all station login fields.';
      return;
    }

    if (!this.activeStationLoginStep) {
      this.stationLoginErrorMessage = 'Please select a station.';
      return;
    }

    const formValue = this.stationLoginForm.value;
    const stationLoginId = String(formValue.station_login_id || '').trim();
    const loginUsedByStep = this.routeSteps.find((step) =>
      step.id !== this.activeStationLoginStep?.id &&
      String(step.station_login_id || '').trim().toLowerCase() === stationLoginId.toLowerCase()
    );
    if (loginUsedByStep) {
      this.stationLoginErrorMessage = `This login ID is already used for ${loginUsedByStep.station_code}.`;
      return;
    }

    const updatedStep: RoutingStepRow = {
      ...this.activeStationLoginStep,
      station_login_id: stationLoginId,
      station_login_password: String(formValue.station_login_password || '').trim(),
      station_ip: String(formValue.station_ip || '').trim(),
      printer_ip: String(formValue.printer_ip || '').trim(),
    };

    this.routeSteps = this.routeSteps.map((step) => step.id === updatedStep.id ? updatedStep : step);
    this.activeStationLoginStep = updatedStep;
    this.isEditingStationLogin = false;

    this.saveWorkflowSnapshot(
      () => {
        this.stationLoginSuccessMessage = 'Station login saved successfully.';
      },
      (message) => {
        this.stationLoginErrorMessage = message;
      }
    );
  }

  closeStationLoginModal(): void {
    this.isStationLoginModalOpen = false;
    this.isEditingStationLogin = true;
    this.activeStationLoginStep = null;
    this.stationLoginErrorMessage = '';
    this.stationLoginSuccessMessage = '';
    this.stationLoginForm.reset({
      station_login_id: '',
      station_login_password: '',
      station_ip: '',
      printer_ip: '',
    });
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

    if (this.hasBoxQty() && !this.hasPackRoutingStep()) {
      this.routingErrorMessage = 'Please add Pack station before saving routing.';
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

  get previewProductSerialNumber(): string {
    const lot = String(this.workOrderForm.get('lot')?.value ?? '').trim();
    if (lot) {
      return lot;
    }

    const partNumber = this.previewPartNumber;
    return partNumber !== 'Part Number Pending' ? partNumber : 'SN Pending';
  }

  get previewTotalDevicesCount(): number {
    return this.getPreviewTotalSnCount();
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
    const snCounts = this.routeSteps.map((step) => this.getPreviewStationSnCount(step));
    const highestCount = Math.max(0, ...snCounts);

    return this.routeSteps.map((step, index) => {
      const snCount = snCounts[index] || 0;

      return {
        ...step,
        flowIndex: index + 1,
        icon: this.getPreviewStationIcon(index, step),
        status: this.getPreviewStationStatus(index, step),
        snCount,
        isHighSnCount: snCount > 0 && snCount === highestCount,
      };
    });
  }

  getPreviewStationSnCountLabel(station: PreviewStationNode | null | undefined): string {
    return `${station?.snCount || 0} SN`;
  }

  isHighSnCountStation(station: PreviewStationNode | null | undefined): boolean {
    if (!station || station.snCount <= 0) {
      return false;
    }

    return Boolean(station.isHighSnCount);
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
        title: 'Carton',
        icon: 'inventory_2',
      },
      {
        id: 'pallet',
        kind: 'logistics',
        variant: 'pallet',
        title: 'Pallet',
        icon: 'warehouse',
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

    return this.buildEnabledStationTasks(this.activePreviewStation.station_code);
  }

  private buildEnabledStationTasks(stationCode: string): string[] {
    const code = String(stationCode || '').trim();
    if (!code) {
      return [];
    }

    const tasks: string[] = [];
    const weighing = this.stationWeighingByStation[code];
    const labelPrinting = this.stationLabelPrintingByStation[code];
    const sampling = this.stationSamplingByStation[code];
    const repair = this.stationRepairByStation[code];

    if (weighing?.isWeighingEnabled) {
      const values = [
        weighing.minimumWeight ? `Min ${weighing.minimumWeight}` : '',
        weighing.maximumWeight ? `Max ${weighing.maximumWeight}` : '',
        weighing.tolerance ? `Tolerance ${weighing.tolerance}` : '',
      ].filter(Boolean).join(', ');
      tasks.push(values ? `Weighing check: ${values}` : 'Weighing check enabled');
    }

    if (labelPrinting?.isLabelPrintingEnabled) {
      const label = [labelPrinting.labelCode, labelPrinting.labelDescription].filter(Boolean).join(' - ');
      const printer = labelPrinting.ipAddress || labelPrinting.printerName;
      tasks.push(`Label printing: ${label || 'Enabled'}${printer ? ` on ${printer}` : ''}`);
    }

    if (sampling?.isSamplingEnabled) {
      tasks.push(this.formatSamplingTask(sampling));
    }

    if (repair?.isRepairStationEnabled) {
      tasks.push(`Repair routing: ${repair.repairStationName || 'Repair station selected'}`);
    }

    tasks.push(...(this.stationRulesByStation[code] || []));
    return tasks;
  }

  private formatSamplingTask(config: StationSamplingConfig): string {
    const type = String(config.samplingType || 'PERIODIC').replace(/_/g, ' ').toLowerCase()
      .replace(/\b\w/g, (letter) => letter.toUpperCase());

    if (config.samplingType === 'PERIODIC') {
      return `Sampling: ${config.sampleQty || '1'} every ${config.intervalQty || '10'} units`;
    }

    if (config.samplingType === 'LOT') {
      return `Sampling: ${config.sampleQty || '1'} per lot of ${config.lotSize || '1000'}`;
    }

    return `Sampling: ${type}`;
  }

  getSiteOptionsForSelectedPlant(): Site[] {
    const selectedPlant = String(this.workOrderForm.get('plant')?.value ?? '');
    return this.getSiteOptionsForPlant(selectedPlant);
  }

  getAssemblyStationOptions(): RoutingStepRow[] {
    const seenStationKeys = new Set<string>();

    return this.routeSteps.filter((step) => {
      if (!this.isBomAssemblyStation(step)) {
        return false;
      }

      const stationKey = this.normalizeLookupValue(step.station_code || step.station_name);
      if (seenStationKeys.has(stationKey)) {
        return false;
      }

      seenStationKeys.add(stationKey);
      return true;
    });
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
    this.setPreviewStationStatus('Skipped');
  }

  completePreviewStation(): void {
    this.setPreviewStationStatus('Passed');
  }

  getPreviewStatusClass(status: PreviewStatus): string {
    return status.toLowerCase().replace(/\s+/g, '-');
  }

  isSampleStation(station: Pick<RoutingStepRow, 'sample_mode'> | null | undefined): boolean {
    return String(station?.sample_mode || '').trim().toLowerCase() === 'sample';
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
      item_type: '',
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
      item_type: child.item_type || '',
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
    const itemType = String(formValue.item_type ?? '').trim();
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
          item_type: itemType,
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
        item_type: itemType,
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

    if (this.bomChildren.length === 0 && this.bomHistory.length === 0) {
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

  get labelCodeOptions(): LabelMasterDto[] {
    const query = this.labelPrintCode.trim().toLowerCase();
    if (!query) {
      return this.availableLabels.slice(0, 20);
    }

    return this.availableLabels
      .filter((label) =>
        label.label_code.toLowerCase().includes(query) ||
        (label.label_description || '').toLowerCase().includes(query)
      )
      .slice(0, 20);
  }

  get activeRulesStationStep(): RoutingStepRow | null {
    return this.routeSteps.find((step) => step.station_code === this.activeRulesStationCode) || null;
  }

  get printerOptions(): PrinterOption[] {
    const stepPrinterIp = String(this.activeRulesStationStep?.printer_ip || '').trim();
    const savedConfig = this.stationLabelPrintingByStation[this.activeRulesStationCode];
    const options: PrinterOption[] = [];

    if (stepPrinterIp) {
      options.push({
        id: stepPrinterIp,
        status: 'Online',
        ipAddress: stepPrinterIp,
        port: savedConfig?.port || '9100',
      });
    }

    if (savedConfig?.ipAddress && !options.some((printer) => printer.ipAddress === savedConfig.ipAddress)) {
      options.push({
        id: savedConfig.ipAddress,
        status: savedConfig.status || 'Online',
        ipAddress: savedConfig.ipAddress,
        port: savedConfig.port || '9100',
      });
    }

    return options;
  }

  get selectedPrinter(): PrinterOption | null {
    const selectedValue = String(this.selectedPrinterId || '').trim();
    return this.printerOptions.find((printer) => printer.id === selectedValue) ||
      (this.isValidPrinterIp(selectedValue)
        ? {
            id: selectedValue,
            status: 'Online',
            ipAddress: selectedValue,
            port: this.stationLabelPrintingByStation[this.activeRulesStationCode]?.port || '9100',
          }
        : null);
  }

  get selectedLabel(): LabelMasterDto | null {
    const code = this.normalizeLabelCode(this.labelPrintCode);
    return this.availableLabels.find((label) => this.normalizeLabelCode(label.label_code) === code) || null;
  }

  onLabelPrintCodeChange(): void {
    const code = this.normalizeLabelCode(this.labelPrintCode);
    this.labelPrintCode = code;
    this.labelPrintStatusMessage = '';

    if (!code) {
      this.selectedLabelDescription = '';
      this.labelPrintValidationMessage = '';
      return;
    }

    const label = this.selectedLabel;
    this.selectedLabelDescription = label ? (label.label_description || label.label_code) : '';
    this.labelPrintValidationMessage = label ? '' : 'Label Code not found in Labels module.';
  }

  onPrinterSelectionChange(): void {
    this.labelPrintStatusMessage = '';
  }

  onPrinterIpChange(): void {
    this.selectedPrinterId = String(this.selectedPrinterId || '').trim();
    this.labelPrintStatusMessage = '';
  }

  onLabelPrintingEnabledChange(enabled: boolean): void {
    this.isLabelPrintingEnabled = Boolean(enabled);
    this.labelPrintValidationMessage = '';
    this.labelPrintStatusMessage = '';

    if (!this.isLabelPrintingEnabled) {
      this.isTestPrintLoading = false;
      this.testPrintPreviewText = '';
      this.revokeTestPrintPreviewUrl();
    }

    this.saveLabelPrintingEnabledState(this.isLabelPrintingEnabled);
  }

  onWeighingEnabledChange(enabled: boolean): void {
    this.isWeighingEnabled = Boolean(enabled);
    this.labelPrintStatusMessage = '';
    this.saveWeighingConfig();
  }

  onSamplingEnabledChange(enabled: boolean): void {
    this.isSamplingEnabled = Boolean(enabled);
    this.labelPrintStatusMessage = '';
    this.saveSamplingConfig();
  }

  saveActiveStationRulesTab(): void {
    if (this.activeStationRulesTab === 'weighing') {
      this.saveWeighingConfig();
      return;
    }

    if (this.activeStationRulesTab === 'sampling') {
      this.saveSamplingConfig();
      return;
    }

    if (this.activeStationRulesTab === 'repair-station') {
      this.saveRepairStationConfig();
      return;
    }

    this.saveLabelPrintingConfig();
  }

  setActiveStationRulesTab(tabId: StationRulesTabId): void {
    if (this.activeStationRulesTab !== tabId) {
      this.labelPrintStatusMessage = '';
      this.labelPrintStatusType = 'info';
    }

    this.activeStationRulesTab = tabId;
  }

  saveWeighingConfig(): void {
    const step = this.activeRulesStationStep;
    const stationCode = step?.station_code || this.activeRulesStationCode;
    if (!stationCode) {
      return;
    }

    this.stationWeighingByStation = {
      ...this.stationWeighingByStation,
      [stationCode]: {
        stationId: step?.id ?? null,
        stationName: step?.station_name || this.activeRulesStationName,
        isWeighingEnabled: this.isWeighingEnabled,
        minimumWeight: String(this.weighingMinimum || '').trim(),
        maximumWeight: String(this.weighingMaximum || '').trim(),
        tolerance: String(this.weighingTolerance || '').trim(),
      },
    };

    this.saveWorkflowSnapshot(
      () => {
        this.setLabelPrintMessage('Saved', 'success');
      },
      (message) => {
        this.setLabelPrintMessage(message, 'error');
      }
    );
  }

  saveSamplingConfig(): void {
    const step = this.activeRulesStationStep;
    const stationCode = step?.station_code || this.activeRulesStationCode;
    if (!stationCode) {
      return;
    }

    if (this.isSamplingEnabled && !this.validateSamplingConfig()) {
      return;
    }

    this.stationSamplingByStation = {
      ...this.stationSamplingByStation,
      [stationCode]: {
        stationId: step?.id ?? null,
        stationName: step?.station_name || this.activeRulesStationName,
        isSamplingEnabled: this.isSamplingEnabled,
        samplingType: this.samplingType,
        intervalQty: String(this.samplingIntervalQty || '').trim(),
        sampleQty: String(this.samplingSampleQty || '').trim(),
        lotSize: String(this.samplingLotSize || '').trim(),
      },
    };

    this.saveWorkflowSnapshot(
      () => {
        this.setLabelPrintMessage('Saved', 'success');
      },
      (message) => {
        this.setLabelPrintMessage(message, 'error');
      }
    );
  }

  onRepairStationEnabledChange(enabled: boolean): void {
    this.isRepairStationEnabled = Boolean(enabled);
    this.labelPrintStatusMessage = '';
    if (!this.isRepairStationEnabled) {
      this.saveRepairStationConfig();
    }
  }

  saveRepairStationConfig(): void {
    const step = this.activeRulesStationStep;
    const stationCode = step?.station_code || this.activeRulesStationCode;
    if (!stationCode) {
      return;
    }

    if (this.isRepairStationEnabled && !String(this.repairStationName || '').trim()) {
      this.setLabelPrintMessage('Station Name is required for repair station.', 'error');
      return;
    }

    this.stationRepairByStation = {
      ...this.stationRepairByStation,
      [stationCode]: {
        stationId: step?.id ?? null,
        stationName: step?.station_name || this.activeRulesStationName,
        isRepairStationEnabled: this.isRepairStationEnabled,
        repairStationName: String(this.repairStationName || '').trim(),
      },
    };

    this.saveWorkflowSnapshot(
      () => {
        this.setLabelPrintMessage('Saved', 'success');
      },
      (message) => {
        this.setLabelPrintMessage(message, 'error');
      }
    );
  }

  testPrinterConnection(): void {
    const printer = this.selectedPrinter;
    if (!printer) {
      this.setLabelPrintMessage('Select a printer before testing connection.', 'error');
      return;
    }

    if (printer.status === 'Offline') {
      this.setLabelPrintMessage(`Connection failed. ${printer.ipAddress} is offline.`, 'error');
      return;
    }

    this.setLabelPrintMessage('Printer connection check is not configured on the backend yet.', 'error');
  }

  testPrintLabel(): void {
    if (!this.validateLabelPrintingConfig()) {
      return;
    }

    const label = this.selectedLabel;
    if (!label) {
      this.setLabelPrintMessage('Select an existing Label Code before test print.', 'error');
      return;
    }

    this.isTestPrintLoading = true;
    this.setLabelPrintMessage('Preparing test print preview...', 'info');

    this.labelService.getLabel(label.id).subscribe({
      next: (response) => {
        const prnContent = response.prn_template?.prn_content || '';
        if (!prnContent.trim()) {
          this.isTestPrintLoading = false;
          this.setLabelPrintMessage('Selected Label Code does not have a PRN template.', 'error');
          return;
        }

        void this.prepareTestPrintPreview(prnContent);
      },
      error: () => {
        this.isTestPrintLoading = false;
        this.setLabelPrintMessage('Unable to load PRN template for selected Label Code.', 'error');
      },
    });
  }

  saveLabelPrintingConfig(): void {
    if (!this.validateLabelPrintingConfig()) {
      return;
    }

    const printer = this.selectedPrinter;
    const label = this.selectedLabel;
    const step = this.activeRulesStationStep;

    if (!printer || !label || !step) {
      this.setLabelPrintMessage('Unable to save label printing configuration.', 'error');
      return;
    }

    this.stationLabelPrintingByStation = {
      ...this.stationLabelPrintingByStation,
      [step.station_code]: {
        stationId: step.id,
        stationName: step.station_name,
        isLabelPrintingEnabled: true,
        labelCode: label.label_code,
        labelDescription: label.label_description || '',
        printerId: printer.id,
        printerName: printer.ipAddress,
        ipAddress: printer.ipAddress,
        port: printer.port,
        status: printer.status,
      },
    };

    this.saveWorkflowSnapshot(
      () => {
        this.setLabelPrintMessage('Saved', 'success');
      },
      (message) => {
        this.setLabelPrintMessage(message, 'error');
      }
    );
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
    this.saveWorkflowSnapshot(
      () => {
        this.setLabelPrintMessage('Saved', 'success');
      },
      (message) => {
        this.setLabelPrintMessage(message, 'error');
      }
    );
    this.isEditingStationRules = false;
  }

  closeStationRulesModal(): void {
    this.isStationRulesModalOpen = false;
    this.isEditingStationRules = false;
    this.activeRulesStationCode = '';
    this.activeRulesStationName = '';
    this.stationRulesDraft = '';
    this.isWeighingEnabled = false;
    this.isLabelPrintingEnabled = false;
    this.labelPrintCode = '';
    this.selectedLabelDescription = '';
    this.labelPrintValidationMessage = '';
    this.selectedPrinterId = '';
    this.labelPrintStatusMessage = '';
    this.isTestPrintLoading = false;
    this.testPrintPreviewText = '';
    this.revokeTestPrintPreviewUrl();
    this.resetWeighingFields();
    this.resetSamplingFields();
    this.resetRepairFields();
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

  private loadFatherSnTypes(partNumber: string): void {
    const pn = String(partNumber || '').trim();
    if (!pn) {
      this.fatherSnTypes = [];
      return;
    }

    const params = new HttpParams().set('pn', pn);
    this.http.get<{ data: FatherSnType[] }>(`${this.workflowApiUrl}/father-sn-types`, { params }).subscribe({
      next: (response) => {
        this.fatherSnTypes = response.data || [];
        this.validateFatherSnTypeSelection();
      },
      error: () => {
        this.fatherSnTypes = [];
      },
    });
  }

  isSnTypeBlocked(snTypeName: string): boolean {
    const selectedName = String(snTypeName || '').trim().toLowerCase();
    return !!selectedName && (this.fatherSnTypes.some((item) =>
      String(item.sn_type_name || '').trim().toLowerCase() === selectedName
    ) || this.isSnTypeGloballyUsedByAnotherPart(snTypeName));
  }

  getSnTypeBlockedReason(snTypeName: string): string {
    const selectedName = String(snTypeName || '').trim().toLowerCase();
    const globalOwner = this.getSnTypeGlobalOwner(snTypeName);
    if (globalOwner) {
      return `Already assigned to PN ${globalOwner}`;
    }

    const familyOwner = this.fatherSnTypes.find((item) =>
      String(item.sn_type_name || '').trim().toLowerCase() === selectedName
    );
    return familyOwner ? 'Already assigned in this father-child family' : '';
  }

  private getSnTypeGlobalOwner(snTypeName: string): string {
    const selectedName = String(snTypeName || '').trim().toLowerCase();
    const currentPn = this.normalizeLookupValue(this.partNumberForm.get('pn')?.value);
    const snType = this.snTypes.find((item) =>
      String(item.sn_type_name || '').trim().toLowerCase() === selectedName
    );
    const ownerPn = String(snType?.used_by_pn || '').trim();
    return ownerPn && this.normalizeLookupValue(ownerPn) !== currentPn ? ownerPn : '';
  }

  private isSnTypeGloballyUsedByAnotherPart(snTypeName: string): boolean {
    return !!this.getSnTypeGlobalOwner(snTypeName);
  }

  private validateFatherSnTypeSelection(): boolean {
    if (this.isRestoringSavedPreview) {
      return true;
    }

    const selectedSnType = String(this.partNumberForm.get('sn_type_name')?.value || '').trim();
    if (!selectedSnType || !this.isSnTypeBlocked(selectedSnType)) {
      return true;
    }

    this.partNumberErrorMessage = this.getSnTypeBlockedReason(selectedSnType) || 'This Serial Pattern is already assigned.';
    this.partNumberForm.get('sn_type_name')?.setValue('', { emitEvent: false });
    return false;
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

  private loadLabels(): void {
    this.labelService.getLabels().subscribe({
      next: (response) => {
        this.availableLabels = (response.data || []).filter((label) => label.status !== 'Inactive');
        this.onLabelPrintCodeChange();
      },
      error: () => {
        this.availableLabels = [];
      },
    });
  }

  private validatePartAttributeValue(): boolean {
    const option = this.selectedPartAttributeOption;
    const key = String(this.partNumberForm.get('part_attribute_key')?.value ?? '').trim();
    const valueControl = this.partNumberForm.get('part_attribute_value');
    const value = String(valueControl?.value ?? '').trim();

    valueControl?.setErrors(null);

    if (!key) {
      this.syncPartAttributeSelection();
      return true;
    }

    if (!option) {
      this.partNumberErrorMessage = 'Select a valid Part Attribute.';
      valueControl?.setErrors({ invalidAttribute: true });
      return false;
    }

    if (!value) {
      this.partNumberErrorMessage = `Enter a value for ${option.label}.`;
      valueControl?.setErrors({ required: true });
      return false;
    }

    if (option.valueKind === 'number') {
      const numericValue = Number(value);
      if (!Number.isFinite(numericValue) || numericValue < 0 || (option.key !== 'mrp' && numericValue === 0)) {
        this.partNumberErrorMessage = `${option.label} must be a positive number.`;
        valueControl?.setErrors({ number: true });
        return false;
      }
    }

    if (option.valueKind === 'boolean' && value !== 'yes' && value !== 'no') {
      this.partNumberErrorMessage = `${option.label} must be Yes or No.`;
      valueControl?.setErrors({ boolean: true });
      return false;
    }

    this.syncPartAttributeSelection();
    return true;
  }

  private syncPartAttributeSelection(): void {
    const key = String(this.partNumberForm.get('part_attribute_key')?.value ?? '').trim();
    const value = String(this.partNumberForm.get('part_attribute_value')?.value ?? '').trim();
    const mappedValues: { sgd_control: boolean; box_qty: number | null } = {
      sgd_control: false,
      box_qty: null,
    };

    if (key === 'sgd_control') {
      mappedValues.sgd_control = value === 'yes';
    }

    if (key === 'box_qty_limit') {
      mappedValues.box_qty = this.toNullableNumber(value);
    }

    this.partNumberForm.patchValue(mappedValues, { emitEvent: false });
  }

  private buildPartNumberMissingFieldsMessage(): string {
    const missing: string[] = [];

    if (this.partNumberForm.get('pn')?.invalid) missing.push('Part number');
    if (this.partNumberForm.get('description')?.invalid) missing.push('Description');
    if (this.partNumberForm.get('item_type')?.invalid) missing.push('Item Type');
    if (this.partNumberForm.get('pn_type_id')?.invalid) missing.push('PN Type');
    if (this.partNumberForm.get('part_attribute_value')?.invalid) missing.push('Attribute Value');

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

  private getPreviewStationSnCount(step: RoutingStepRow): number {
    const count = this.toNullableNumber(
      step.sn_count
      ?? step.preview_sn_count
      ?? this.extractCountFromLabel(step.sn_count_label || step.preview_sn_count_label)
    );

    if (count && count > 0) {
      return count;
    }

    return this.bomChildren
      .filter((child) => this.normalizeLookupValue(child.station_code) === this.normalizeLookupValue(step.station_code))
      .reduce((total, child) => total + (this.toNullableNumber(child.qty) || 0), 0);
  }

  private getPreviewTotalSnCount(): number {
    const workOrderQty = this.toNullableNumber(this.workOrderForm.get('qty')?.value);
    const boxQty = this.toNullableNumber(this.partNumberForm.get('box_qty')?.value);
    return workOrderQty || boxQty || Math.max(0, ...this.previewStations.map((station) => station.snCount));
  }

  private extractCountFromLabel(label: string | null | undefined): number | null {
    const match = String(label || '').match(/\d+/);
    return match ? Number(match[0]) : null;
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

    const renderedRows = this.previewFlowRows
      .map((row) => ({
        isReversed: row.isReversed,
        rects: row.nodes
          .map((node) => nodeRectsById.get(node.id))
          .filter((rect): rect is DOMRect => Boolean(rect)),
      }))
      .filter((row) => row.rects.length > 0);

    if (!renderedRows.length) {
      this.setPreviewConnector('', 0, 0);
      return;
    }

    const pathSegments: string[] = [];

    renderedRows.forEach((row, rowIndex) => {
      for (let index = 0; index < row.rects.length - 1; index += 1) {
        pathSegments.push(this.buildPreviewSameRowConnector(row.rects[index], row.rects[index + 1], containerRect));
      }

      const nextRow = renderedRows[rowIndex + 1];
      if (!nextRow) {
        return;
      }

      const exitRect = row.isReversed ? row.rects[0] : row.rects[row.rects.length - 1];
      const entryRect = nextRow.isReversed ? nextRow.rects[nextRow.rects.length - 1] : nextRow.rects[0];
      pathSegments.push(this.buildPreviewRowTurnConnector(exitRect, entryRect, containerRect, row.isReversed ? 'left' : 'right'));
    });

    this.setPreviewConnector(pathSegments.join(' '), containerRect.width, containerRect.height);
  }

  private buildPreviewSameRowConnector(currentRect: DOMRect, nextRect: DOMRect, containerRect: DOMRect): string {
    const currentCenter = this.getRelativeRectCenter(currentRect, containerRect);
    const nextCenter = this.getRelativeRectCenter(nextRect, containerRect);
    const flowsRight = nextCenter.x >= currentCenter.x;
    const startX = flowsRight ? currentRect.right - containerRect.left : currentRect.left - containerRect.left;
    const endX = flowsRight ? nextRect.left - containerRect.left : nextRect.right - containerRect.left;
    const y = (currentCenter.y + nextCenter.y) / 2;

    return `M ${startX} ${y} L ${endX} ${y}`;
  }

  private buildPreviewRowTurnConnector(
    currentRect: DOMRect,
    nextRect: DOMRect,
    containerRect: DOMRect,
    turnSide: 'left' | 'right'
  ): string {
    const currentCenter = this.getRelativeRectCenter(currentRect, containerRect);
    const nextCenter = this.getRelativeRectCenter(nextRect, containerRect);
    const currentLeft = currentRect.left - containerRect.left;
    const currentRight = currentRect.right - containerRect.left;
    const nextLeft = nextRect.left - containerRect.left;
    const nextRight = nextRect.right - containerRect.left;
    const startX = turnSide === 'right' ? currentRight : currentLeft;
    const endX = turnSide === 'right' ? nextRight : nextLeft;
    const startY = currentCenter.y;
    const endY = nextCenter.y;
    const midY = startY + ((endY - startY) / 2);
    const gutterOffset = 28;
    const gutterX = turnSide === 'right'
      ? Math.min(containerRect.width - 6, Math.max(startX, endX) + gutterOffset)
      : Math.max(6, Math.min(startX, endX) - gutterOffset);

    return `M ${startX} ${startY} L ${gutterX} ${startY} L ${gutterX} ${midY} L ${gutterX} ${endY} L ${endX} ${endY}`;
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

    if (this.isSampleStation(step)) {
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
      item_type: '',
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

  private normalizeLabelCode(value: string): string {
    return String(value || '').trim().toUpperCase();
  }

  private loadLabelPrintingDraft(stationCode: string): void {
    const config = this.stationLabelPrintingByStation[stationCode];
    const defaultPrinter = this.printerOptions[0];

    this.isLabelPrintingEnabled = Boolean(config?.isLabelPrintingEnabled);
    this.labelPrintCode = config?.labelCode || '';
    this.selectedLabelDescription = config?.labelDescription || '';
    this.labelPrintValidationMessage = '';
    this.selectedPrinterId = config?.ipAddress || config?.printerId || defaultPrinter?.id || '';
    this.labelPrintStatusMessage = '';
    this.labelPrintStatusType = 'info';
    this.onLabelPrintCodeChange();
  }

  private loadWeighingDraft(stationCode: string): void {
    const config = this.stationWeighingByStation[stationCode];
    this.isWeighingEnabled = Boolean(config?.isWeighingEnabled);
    this.weighingMinimum = config?.minimumWeight || '';
    this.weighingMaximum = config?.maximumWeight || '';
    this.weighingTolerance = config?.tolerance || '';
  }

  private loadSamplingDraft(stationCode: string): void {
    const config = this.stationSamplingByStation[stationCode];
    this.isSamplingEnabled = Boolean(config?.isSamplingEnabled);
    this.samplingType = config?.samplingType || 'PERIODIC';
    this.samplingIntervalQty = config?.intervalQty || '10';
    this.samplingSampleQty = config?.sampleQty || '1';
    this.samplingLotSize = config?.lotSize || '1000';
  }

  private loadRepairDraft(stationCode: string): void {
    const config = this.stationRepairByStation[stationCode];
    this.isRepairStationEnabled = Boolean(config?.isRepairStationEnabled);
    this.repairStationName = config?.repairStationName || '';
  }

  private saveLabelPrintingEnabledState(enabled: boolean): void {
    const step = this.activeRulesStationStep;
    if (!step) {
      this.setLabelPrintMessage('Select a routing station before changing label printing.', 'error');
      return;
    }

    const existingConfig = this.stationLabelPrintingByStation[step.station_code];
    const printer = this.selectedPrinter;
    const label = this.selectedLabel;

    this.stationLabelPrintingByStation = {
      ...this.stationLabelPrintingByStation,
      [step.station_code]: {
        stationId: step.id,
        stationName: step.station_name,
        isLabelPrintingEnabled: enabled,
        labelCode: label?.label_code || existingConfig?.labelCode || this.normalizeLabelCode(this.labelPrintCode),
        labelDescription: label?.label_description || existingConfig?.labelDescription || this.selectedLabelDescription || '',
        printerId: printer?.id || existingConfig?.printerId || this.selectedPrinterId || '',
        printerName: printer?.ipAddress || existingConfig?.printerName || this.selectedPrinterId || '',
        ipAddress: printer?.ipAddress || existingConfig?.ipAddress || this.selectedPrinterId || '',
        port: printer?.port || existingConfig?.port || '9100',
        status: printer?.status || existingConfig?.status || 'Online',
      },
    };

    this.saveWorkflowSnapshot(
      () => {
        this.setLabelPrintMessage('Saved', 'success');
      },
      (message) => {
        this.setLabelPrintMessage(message, 'error');
      }
    );
  }

  private validateLabelPrintingConfig(): boolean {
    const code = this.normalizeLabelCode(this.labelPrintCode);
    const label = this.selectedLabel;

    if (!this.activeRulesStationStep) {
      this.setLabelPrintMessage('Select a routing station before saving label printing.', 'error');
      return false;
    }

    if (!code) {
      this.labelPrintValidationMessage = 'Label Code is required.';
      this.setLabelPrintMessage('Label Code is required.', 'error');
      return false;
    }

    if (!label) {
      this.labelPrintValidationMessage = 'Label Code not found in Labels module.';
      this.setLabelPrintMessage('Select an existing Label Code from Labels module.', 'error');
      return false;
    }

    if (!this.selectedPrinter) {
      this.setLabelPrintMessage('Printer IP is required.', 'error');
      return false;
    }

    this.labelPrintValidationMessage = '';
    return true;
  }

  private validateSamplingConfig(): boolean {
    const intervalQty = Number(this.samplingIntervalQty);
    const sampleQty = Number(this.samplingSampleQty);
    const lotSize = Number(this.samplingLotSize);

    if (!this.activeRulesStationStep) {
      this.setLabelPrintMessage('Select a routing station before saving sampling.', 'error');
      return false;
    }

    if (this.samplingType === 'FIRST_PIECE') {
      return true;
    }

    if (!Number.isFinite(sampleQty) || sampleQty <= 0) {
      this.setLabelPrintMessage('Sample Qty must be greater than zero.', 'error');
      return false;
    }

    if (this.samplingType === 'PERIODIC' || this.samplingType === 'RANDOM') {
      if (!Number.isFinite(intervalQty) || intervalQty <= 0) {
        this.setLabelPrintMessage('Interval Qty must be greater than zero.', 'error');
        return false;
      }

      if (sampleQty > intervalQty) {
        this.setLabelPrintMessage('Sample Qty cannot be greater than Interval Qty.', 'error');
        return false;
      }
    }

    if (this.samplingType === 'LOT') {
      if (!Number.isFinite(lotSize) || lotSize <= 0) {
        this.setLabelPrintMessage('Lot Size must be greater than zero.', 'error');
        return false;
      }

      if (sampleQty > lotSize) {
        this.setLabelPrintMessage('Sample Qty cannot be greater than Lot Size.', 'error');
        return false;
      }
    }

    return true;
  }

  private setLabelPrintMessage(message: string, type: 'success' | 'error' | 'info'): void {
    this.labelPrintStatusMessage = message;
    this.labelPrintStatusType = type;
  }

  private isValidPrinterIp(value: string): boolean {
    return /^(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)$/.test(value);
  }

  private async prepareTestPrintPreview(prnContent: string): Promise<void> {
    this.revokeTestPrintPreviewUrl();
    this.testPrintPreviewText = '';

    try {
      const { width, height } = this.getTestPrintLabelarySize(prnContent);
      this.testPrintPageSize = { width, height };
      const response = await fetch(`http://api.labelary.com/v1/printers/8dpmm/labels/${width}x${height}/0/`, {
        method: 'POST',
        headers: {
          Accept: 'image/png',
          'Content-Type': 'application/x-www-form-urlencoded',
        },
        body: prnContent,
      });

      if (!response.ok) {
        throw new Error(await response.text());
      }

      const blob = await response.blob();
      this.testPrintPreviewUrl = URL.createObjectURL(blob);
      this.setLabelPrintMessage('Test print preview prepared.', 'success');
      this.printTestPrintPreview();
    } catch {
      this.testPrintPageSize = this.getTestPrintLabelarySize(prnContent);
      this.testPrintPreviewText = this.buildTestPrintTextPreview(prnContent);
      this.setLabelPrintMessage('Labelary preview unavailable. Local test print preview prepared.', 'success');
      this.printTestPrintPreview();
    } finally {
      this.isTestPrintLoading = false;
    }
  }

  private printTestPrintPreview(): void {
    const markup = this.buildTestPrintPreviewMarkup();
    if (!markup) {
      this.setLabelPrintMessage('Unable to prepare test print preview.', 'error');
      return;
    }

    const printFrame = document.createElement('iframe');
    printFrame.style.position = 'fixed';
    printFrame.style.right = '0';
    printFrame.style.bottom = '0';
    printFrame.style.width = '0';
    printFrame.style.height = '0';
    printFrame.style.border = '0';
    printFrame.setAttribute('aria-hidden', 'true');
    document.body.appendChild(printFrame);

    const frameWindow = printFrame.contentWindow;
    const frameDocument = printFrame.contentDocument || frameWindow?.document;
    if (!frameWindow || !frameDocument) {
      printFrame.remove();
      this.setLabelPrintMessage('Unable to open test print preview.', 'error');
      return;
    }

    frameDocument.open();
    frameDocument.write(markup);
    frameDocument.close();

    const printAndCleanup = () => {
      frameWindow.focus();
      frameWindow.print();
      window.setTimeout(() => printFrame.remove(), 800);
    };

    const image = frameDocument.querySelector('img');
    if (image && !(image as HTMLImageElement).complete) {
      image.addEventListener('load', printAndCleanup, { once: true });
      image.addEventListener('error', printAndCleanup, { once: true });
      return;
    }

    window.setTimeout(printAndCleanup, 100);
  }

  private buildTestPrintPreviewMarkup(): string {
    const pageWidth = this.testPrintPageSize.width;
    const pageHeight = this.testPrintPageSize.height;

    if (this.testPrintPreviewUrl) {
      return `<!doctype html>
<html>
<head>
  <title>Test Label Print</title>
  <style>
    @page { size: ${pageWidth}in ${pageHeight}in; margin: 0; }
    * { box-sizing: border-box; }
    html, body {
      margin: 0;
      padding: 0;
      background: #ffffff;
      width: ${pageWidth}in;
      height: ${pageHeight}in;
      overflow: hidden;
    }
    body {
      display: block;
      line-height: 0;
    }
    img {
      display: block;
      width: ${pageWidth}in;
      height: ${pageHeight}in;
      object-fit: contain;
      page-break-inside: avoid;
      break-inside: avoid;
      page-break-after: avoid;
      break-after: avoid;
    }
  </style>
</head>
<body><img src="${this.testPrintPreviewUrl}" alt="Test Label Preview"></body>
</html>`;
    }

    if (this.testPrintPreviewText) {
      const escapedText = this.escapeHtml(this.testPrintPreviewText);
      return `<!doctype html>
<html>
<head>
  <title>Test Label Print</title>
  <style>
    @page { size: ${pageWidth}in ${pageHeight}in; margin: 0; }
    * { box-sizing: border-box; }
    html, body {
      margin: 0;
      padding: 0;
      background: #ffffff;
      width: ${pageWidth}in;
      height: ${pageHeight}in;
      overflow: hidden;
      font-family: Arial, sans-serif;
    }
    pre {
      width: ${pageWidth}in;
      height: ${pageHeight}in;
      margin: 0;
      padding: 12px;
      overflow: hidden;
      white-space: pre-wrap;
      font-size: 14px;
      line-height: 1.35;
      page-break-inside: avoid;
      break-inside: avoid;
      page-break-after: avoid;
      break-after: avoid;
    }
  </style>
</head>
<body><pre>${escapedText}</pre></body>
</html>`;
    }

    return '';
  }

  private getTestPrintLabelarySize(zpl: string): { width: string; height: string } {
    const widthDots = Number(zpl.match(/\^PW(\d+)/i)?.[1]) || 812;
    const heightDots = Number(zpl.match(/\^LL(\d+)/i)?.[1]) || 1218;
    return {
      width: this.formatTestPrintInches(widthDots / 8 / 25.4),
      height: this.formatTestPrintInches(heightDots / 8 / 25.4),
    };
  }

  private formatTestPrintInches(value: number): string {
    return Math.max(0.5, Math.min(15, value)).toFixed(2).replace(/\.?0+$/, '');
  }

  private buildTestPrintTextPreview(zpl: string): string {
    const fields = Array.from(zpl.matchAll(/\^FD([\s\S]*?)\^FS/gi))
      .map((match) => this.cleanZplField(match[1]))
      .filter(Boolean);

    return fields.length ? fields.slice(0, 18).join('\n') : 'Test label preview';
  }

  private cleanZplField(value: string): string {
    return value
      .replace(/\^FH\\?/gi, '')
      .replace(/\\&/g, '\n')
      .replace(/_0D_0A/gi, '\n')
      .replace(/\^[A-Z0-9,]+/gi, '')
      .trim();
  }

  private escapeHtml(value: string): string {
    return value
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  private revokeTestPrintPreviewUrl(): void {
    if (this.testPrintPreviewUrl) {
      URL.revokeObjectURL(this.testPrintPreviewUrl);
      this.testPrintPreviewUrl = '';
    }
  }

  private isBomAssemblyStation(step: RoutingStepRow): boolean {
    const normalizedStationCode = this.normalizeLookupValue(step.station_code).toUpperCase();
    const normalizedStationName = this.normalizeLookupValue(step.station_name).toUpperCase();

    return /^ASM(0?[1-9]|1[0-5])$/.test(normalizedStationCode)
      || normalizedStationCode === 'BOXING01'
      || normalizedStationName === 'BOXING01';
  }

  private getStationRuleText(stationCode: string): string {
    return `${stationCode} Rule`;
  }

  private hasStationLoginDetails(step: RoutingStepRow): boolean {
    return Boolean(
      step.station_login_id &&
      step.station_login_password &&
      step.station_ip &&
      step.printer_ip
    );
  }

  private openStationRulesModal(station: StationOption): void {
    this.activeRulesStationCode = station.station_code;
    this.activeRulesStationName = station.station_desc;
    this.stationRulesDraft = this.activeStationRules.join('\n');
    this.loadWeighingDraft(station.station_code);
    this.loadSamplingDraft(station.station_code);
    this.loadRepairDraft(station.station_code);
    this.loadLabelPrintingDraft(station.station_code);
    this.isEditingStationRules = false;
    this.activeStationRulesTab = 'weighing';
    this.isStationRulesModalOpen = true;
  }

  private resetWeighingFields(): void {
    this.isWeighingEnabled = false;
    this.weighingMinimum = '';
    this.weighingMaximum = '';
    this.weighingTolerance = '';
  }

  private resetSamplingFields(): void {
    this.isSamplingEnabled = false;
    this.samplingType = 'PERIODIC';
    this.samplingIntervalQty = '10';
    this.samplingSampleQty = '1';
    this.samplingLotSize = '1000';
  }

  private resetRepairFields(): void {
    this.isRepairStationEnabled = false;
    this.repairStationName = '';
  }

  private restoreSavedPreviewForPartNumber(partNumber: string, workOrder = ''): void {
    if (!partNumber) {
      return;
    }

    let params = new HttpParams().set('pn', partNumber);
    if (workOrder.trim()) {
      params = params.set('wo', workOrder.trim());
    }

    this.http.get<WorkflowSnapshot>(`${this.workflowApiUrl}/by-pn`, { params }).subscribe({
      next: (snapshot) => {
        this.applyWorkflowSnapshot(snapshot);
        if (this.isWorkflowEditMode) {
          this.applyWorkflowEditFieldLocks(snapshot);
        } else {
          this.setPartNumberReadonlyState(true, true);
        }
        this.previewActionMessageType = 'success';
        this.previewActionMessage = 'Saved workflow loaded from database.';
        this.scheduleClearMessages();
      },
      error: (error) => {
        this.setPartNumberReadonlyState(false, false);
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
      part_attribute_key: '',
      part_attribute_value: '',
      sgd_control: false,
      item_type: null,
      sn_type_name: '',
      pn_type_id: null,
      box_qty: null,
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
      item_type: '',
      station_code: '',
    }, { emitEvent: false });

    this.isPartNumberSaved = false;
    this.isExistingPartNumberReadonly = false;
    this.arePartNumberDetailsReadonly = false;
    this.setWorkOrderReadonlyState(false);
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
    this.closeStationLoginModal();
    this.isChildDetailsOpen = false;
    this.activePreviewStation = null;
    this.activeRulesStationCode = '';
    this.activeRulesStationName = '';
    this.stationRulesDraft = '';
    this.stationLabelPrintingByStation = {};
    this.stationWeighingByStation = {};
    this.stationSamplingByStation = {};
    this.stationRepairByStation = {};
    this.isLabelPrintingEnabled = false;
    this.labelPrintCode = '';
    this.selectedLabelDescription = '';
    this.labelPrintValidationMessage = '';
    this.selectedPrinterId = '';
    this.labelPrintStatusMessage = '';
    this.isTestPrintLoading = false;
    this.testPrintPreviewText = '';
    this.revokeTestPrintPreviewUrl();
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
    this.setPartNumberReadonlyState(false, false);
  }

  private saveWorkflowSnapshot(onSuccess?: () => void, onError?: (message: string) => void): void {
    const payload = this.buildWorkflowSnapshotPayload();
    const savedPartNumber = String((payload as WorkflowSnapshot).partNumber?.pn || '').trim();
    const savedWorkOrder = String((payload as WorkflowSnapshot).workOrder?.wo || '').trim();

    if (!savedPartNumber) {
      onError?.('Part number is required before saving workflow data.');
      return;
    }

    this.http.post<WorkflowSnapshot | null>(`${this.workflowApiUrl}/snapshot`, payload).subscribe({
      next: (snapshot) => {
        this.verifyWorkflowWorkOrderConnection(
          savedPartNumber,
          savedWorkOrder,
          () => {
            if (snapshot?.partNumber?.pn && (snapshot.routing?.length || this.routeSteps.length === 0)) {
              this.applyWorkflowSnapshot(snapshot);
            }
            localStorage.setItem('k9_workflow_work_orders_updated_at', String(Date.now()));
            onSuccess?.();
          },
          onError
        );
      },
      error: (error) => {
        onError?.(this.getWorkflowErrorMessage(error));
      }
    });
  }

  private verifyWorkflowWorkOrderConnection(
    partNumber: string,
    workOrder: string,
    onVerified: () => void,
    onError?: (message: string) => void
  ): void {
    const params = new HttpParams()
      .set('pn', partNumber)
      .set('page', '1')
      .set('limit', 'all');

    this.http.get<{ data: Array<{ wo?: string; part_number?: string; partNumber?: string; pn?: string }> }>(`${this.workflowApiUrl}/work-orders`, { params }).subscribe({
      next: (response) => {
        const normalizedPartNumber = partNumber.toUpperCase();
        const normalizedWorkOrder = workOrder.toUpperCase();
        const isVisibleInWorkOrder = (response.data || []).some((row) => {
          const rowPartNumber = String(row.part_number || row.partNumber || row.pn || '').trim().toUpperCase();
          const rowWorkOrder = String(row.wo || '').trim().toUpperCase();
          return rowPartNumber === normalizedPartNumber && (!normalizedWorkOrder || rowWorkOrder === normalizedWorkOrder);
        });

        if (!isVisibleInWorkOrder) {
          const label = workOrder ? `PN ${partNumber} / WO ${workOrder}` : `PN ${partNumber}`;
          onError?.(`Workflow save did not reach the Work Order table for ${label}. Please retry after K9Api refreshes.`);
          return;
        }

        onVerified();
      },
      error: () => {
        onError?.('Workflow saved request completed, but Work Order table verification failed. Please retry after K9Api refreshes.');
      }
    });
  }

  private buildWorkflowSnapshotPayload(): object {
    const partNumber = this.partNumberForm.getRawValue();
    const workOrder = this.workOrderForm.getRawValue();
    const siteName = this.previewSiteName === 'Select Site' ? '' : this.previewSiteName;

    return {
      partNumber: {
        ...partNumber,
        pn_type_id: this.toNullableNumber(partNumber.pn_type_id),
        box_qty: this.toNullableNumber(partNumber.box_qty),
      },
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
      stationLabelPrinting: this.stationLabelPrintingByStation,
      stationWeighing: this.stationWeighingByStation,
      stationSampling: this.stationSamplingByStation,
      stationRepair: this.stationRepairByStation,
      previewStatuses: this.buildPreviewStatusesByStationCode(),
    };
  }

  private applyWorkflowSnapshot(snapshot: WorkflowSnapshot): void {
    if (!snapshot?.partNumber?.pn) {
      return;
    }

    const partNumber = snapshot.partNumber;
    const partAttributeKey = partNumber.part_attribute_key || this.inferPartAttributeKey(partNumber);
    const partAttributeValue = partNumber.part_attribute_value || this.inferPartAttributeValue(partNumber, partAttributeKey);
    this.isRestoringSavedPreview = true;

    this.partNumberForm.patchValue({
      pn: partNumber.pn || '',
      description: partNumber.description || '',
      part_attribute_key: partAttributeKey,
      part_attribute_value: partAttributeValue,
      sgd_control: Boolean(partNumber.sgd_control),
      item_type: partNumber.item_type || null,
      sn_type_name: partNumber.sn_type_name || '',
      pn_type_id: partNumber.pn_type_id ?? null,
      box_qty: partNumber.box_qty ?? null,
    }, { emitEvent: false });
    this.syncPartAttributeSelection();

    const workOrder = snapshot.workOrder || null;

    this.workOrderForm.patchValue({
      wo: workOrder?.wo || '',
      plant: workOrder?.plant || null,
      site_id: workOrder?.site_id ?? null,
      due_date: workOrder?.due_date ? String(workOrder.due_date).slice(0, 10) : '',
      qty: workOrder?.qty ?? null,
      status: workOrder?.status || '',
      pn: partNumber.pn || '',
      revision: workOrder?.revision || '',
      lot: workOrder?.lot || '',
    }, { emitEvent: false });

    this.routeSteps = (snapshot.routing || []).map((step, index) => ({
      id: Number(step.id) || index + 1,
      station_order: Number(step.station_order) || ((index + 1) * 10),
      station_code: step.station_code,
      station_name: step.station_name,
      sample_mode: step.sample_mode,
      report_mode: step.report_mode,
      station_login_id: step.station_login_id || '',
      station_login_password: step.station_login_password || '',
      station_ip: step.station_ip || '',
      printer_ip: step.printer_ip || '',
      sn_count: this.toNullableNumber(step.sn_count ?? step.preview_sn_count),
      sn_count_label: step.sn_count_label || step.preview_sn_count_label || '',
    }));

    this.bomChildren = (snapshot.bom || []).map((child, index) => ({
      id: Number(child.id) || index + 1,
      son_pn: child.son_pn,
      son_description: child.son_description || child.son_pn,
      station_code: child.station_code || '',
      station_name: child.station_name || '',
      item_type: child.item_type || '',
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
    this.stationLabelPrintingByStation = snapshot.stationLabelPrinting || {};
    this.stationWeighingByStation = snapshot.stationWeighing || {};
    this.stationSamplingByStation = snapshot.stationSampling || {};
    this.stationRepairByStation = snapshot.stationRepair || {};
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
    if (this.isWorkflowEditMode) {
      this.applyWorkflowEditFieldLocks(snapshot);
    } else {
      this.applyWorkflowEditLocks();
    }
    this.queuePreviewConnectorRefresh();
  }

  private applyWorkflowEditFieldLocks(snapshot: WorkflowSnapshot): void {
    const partNumber = snapshot.partNumber;
    if (!partNumber) {
      return;
    }

    const partAttributeKey = partNumber.part_attribute_key || this.inferPartAttributeKey(partNumber);
    const partAttributeValue = partNumber.part_attribute_value || this.inferPartAttributeValue(partNumber, partAttributeKey);
    const workOrder = snapshot.workOrder || null;

    this.isExistingPartNumberReadonly = Boolean(this.lockedEditPartNumber || partNumber.pn);
    this.arePartNumberDetailsReadonly = false;
    this.isWorkOrderReadonly = false;

    this.setControlReadonlyBySavedValue(this.partNumberForm, 'pn', this.lockedEditPartNumber || partNumber.pn, true);
    this.setControlReadonlyBySavedValue(this.partNumberForm, 'description', partNumber.description);
    this.setControlReadonlyBySavedValue(this.partNumberForm, 'part_attribute_key', partAttributeKey);
    this.setControlReadonlyBySavedValue(this.partNumberForm, 'part_attribute_value', partAttributeValue);
    this.setControlReadonlyBySavedValue(this.partNumberForm, 'item_type', partNumber.item_type);
    this.setControlReadonlyBySavedValue(this.partNumberForm, 'sn_type_name', partNumber.sn_type_name);
    this.setControlReadonlyBySavedValue(this.partNumberForm, 'pn_type_id', partNumber.pn_type_id);
    this.setControlReadonlyBySavedValue(this.partNumberForm, 'box_qty', partNumber.box_qty);

    this.setControlReadonlyBySavedValue(this.workOrderForm, 'wo', this.lockedEditWorkOrder || workOrder?.wo);
    this.setControlReadonlyBySavedValue(this.workOrderForm, 'plant', workOrder?.plant);
    this.setControlReadonlyBySavedValue(this.workOrderForm, 'site_id', workOrder?.site_id);
    this.setControlReadonlyBySavedValue(this.workOrderForm, 'due_date', workOrder?.due_date);
    this.setControlReadonlyBySavedValue(this.workOrderForm, 'qty', workOrder?.qty);
    this.setControlReadonlyBySavedValue(this.workOrderForm, 'status', workOrder?.status);
    this.setControlReadonlyBySavedValue(this.workOrderForm, 'pn', this.lockedEditPartNumber || partNumber.pn, true);
    this.setControlReadonlyBySavedValue(this.workOrderForm, 'revision', workOrder?.revision);
    this.setControlReadonlyBySavedValue(this.workOrderForm, 'lot', workOrder?.lot);
  }

  private setControlReadonlyBySavedValue(form: FormGroup, controlName: string, savedValue: unknown, forceReadonly = false): void {
    const control = form.get(controlName);
    if (!control) {
      return;
    }

    if (forceReadonly || this.hasSavedWorkflowValue(savedValue)) {
      control.disable({ emitEvent: false });
      return;
    }

    control.enable({ emitEvent: false });
  }

  private hasSavedWorkflowValue(value: unknown): boolean {
    if (value === null || value === undefined) {
      return false;
    }

    if (typeof value === 'string') {
      return value.trim() !== '';
    }

    return true;
  }

  private setPartNumberReadonlyState(isExistingPartNumber: boolean, lockDetails: boolean): void {
    this.isExistingPartNumberReadonly = isExistingPartNumber;
    this.arePartNumberDetailsReadonly = lockDetails;
    const partNumberControl = this.partNumberForm.get('pn');

    if (isExistingPartNumber) {
      partNumberControl?.disable({ emitEvent: false });
    } else if (!this.lockedEditPartNumber) {
      partNumberControl?.enable({ emitEvent: false });
    }

    this.partNumberDetailControls.forEach((controlName) => {
      const control = this.partNumberForm.get(controlName);
      if (lockDetails) {
        control?.disable({ emitEvent: false });
      } else {
        control?.enable({ emitEvent: false });
      }
    });

    this.setWorkOrderReadonlyState(isExistingPartNumber && !this.lockedEditWorkOrder);
  }

  private setWorkOrderReadonlyState(readonly: boolean): void {
    this.isWorkOrderReadonly = readonly;

    if (readonly) {
      this.workOrderForm.disable({ emitEvent: false });
      return;
    }

    this.workOrderForm.enable({ emitEvent: false });
    this.workOrderForm.get('pn')?.disable({ emitEvent: false });

    if (this.lockedEditWorkOrder) {
      this.workOrderForm.get('wo')?.disable({ emitEvent: false });
    }
  }

  private applyWorkflowEditLocks(): void {
    const partNumberControl = this.partNumberForm.get('pn');
    const workOrderControl = this.workOrderForm.get('wo');

    if (this.lockedEditPartNumber) {
      partNumberControl?.setValue(this.lockedEditPartNumber, { emitEvent: false });
      this.workOrderForm.get('pn')?.setValue(this.lockedEditPartNumber, { emitEvent: false });
      partNumberControl?.disable({ emitEvent: false });
    } else {
      partNumberControl?.enable({ emitEvent: false });
    }

    if (this.isWorkOrderReadonly) {
      this.workOrderForm.disable({ emitEvent: false });
    } else if (this.lockedEditWorkOrder) {
      workOrderControl?.setValue(this.lockedEditWorkOrder, { emitEvent: false });
      workOrderControl?.disable({ emitEvent: false });
    } else {
      workOrderControl?.enable({ emitEvent: false });
    }
  }

  private clearWorkflowEditLocks(): void {
    this.lockedEditPartNumber = '';
    this.lockedEditWorkOrder = '';
    this.setWorkOrderReadonlyState(false);
    this.applyWorkflowEditLocks();
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

  private inferPartAttributeKey(partNumber: WorkflowSnapshot['partNumber']): string {
    if (!partNumber) {
      return '';
    }

    if (partNumber.part_attribute_key) {
      return partNumber.part_attribute_key;
    }

    if (partNumber.box_qty) {
      return 'box_qty_limit';
    }

    if (partNumber.sgd_control) {
      return 'sgd_control';
    }

    return '';
  }

  private inferPartAttributeValue(partNumber: WorkflowSnapshot['partNumber'], key: string): string {
    if (!partNumber) {
      return '';
    }

    if (partNumber.part_attribute_value) {
      return partNumber.part_attribute_value;
    }

    if (key === 'box_qty_limit' && partNumber.box_qty) {
      return String(partNumber.box_qty);
    }

    if (key === 'sgd_control') {
      return partNumber.sgd_control ? 'yes' : 'no';
    }

    return '';
  }

  private toNullableNumber(value: unknown): number | null {
    if (value === null || value === undefined || value === '') {
      return null;
    }

    const numberValue = Number(value);
    return Number.isFinite(numberValue) ? numberValue : null;
  }

  private hasBoxQty(): boolean {
    const boxQty = this.toNullableNumber(this.partNumberForm.get('box_qty')?.value);
    return Boolean(boxQty && boxQty > 0);
  }

  private hasPackRoutingStep(): boolean {
    return this.routeSteps.some((step) => this.isPackStation(step.station_code, step.station_name));
  }

  private isPackStation(stationCode?: string | null, stationName?: string | null): boolean {
    return `${stationCode || ''} ${stationName || ''}`.toLowerCase().includes('pack');
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
