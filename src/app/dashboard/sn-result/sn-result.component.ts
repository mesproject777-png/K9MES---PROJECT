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
import { ActivatedRoute, Router } from '@angular/router';
import { Location } from '@angular/common';
import { Subscription } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  TraceabilityService,
  TraceAssembledPart,
  TraceHistoryRow,
  TraceSearchResponse,
  TraceSnValue,
} from '../../services/traceability.service';
import { PackingHierarchyRow, PackingService } from '../../services/packing.service';

type SnResultTab = 'preview' | 'history';
type PreviewStatus = 'Completed' | 'In Progress' | 'Pending' | 'Paused' | 'Failed';

type WorkflowSnapshot = {
  partNumber?: {
    pn?: string;
    description?: string;
    item_type?: string;
  };
  workOrder?: {
    wo?: string;
    plant?: string | null;
    site_name?: string | null;
    qty?: number | null;
    revision?: string | null;
    lot?: string | null;
  } | null;
  routing?: Array<{
    id: number;
    station_order: number;
    station_code: string;
    station_name: string;
    sample_mode: string;
    report_mode: string;
    preview_status?: PreviewStatus | null;
  }>;
  bom?: Array<{
    id: number;
    son_pn: string;
    son_description?: string;
    station_code: string;
    station_name: string;
    item_type?: string;
    pn_type?: string;
    qty: number;
  }>;
  stationRules?: Record<string, string[]>;
  previewStatuses?: Record<string, PreviewStatus>;
};

type PreviewStationNode = {
  id: number;
  station_order: number;
  station_code: string;
  station_name: string;
  sample_mode: string;
  report_mode: string;
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

type LogisticsVariant = NonNullable<PreviewFlowNode['variant']>;

type PreviewFlowRow = {
  nodes: PreviewFlowNode[];
  isReversed: boolean;
  turnSide: 'left' | 'right';
};

type SnHistoryDisplayRow = {
  id: string;
  stationCode: string;
  stationLoginId: string;
  date: string;
  time: string;
  actionDescription: string;
  linkSerial?: string;
  linkLabel?: string;
  linkRole?: 'father' | 'child';
  relatedPart?: string;
  relatedRevision?: string;
  isBinding?: boolean;
};

type LogisticsMultiboxGroup = {
  multiboxNo: string;
  multiboxStatus: string;
  palletNo: string;
  shipmentNo: string;
  rows: PackingHierarchyRow[];
};

type LogisticsPalletGroup = {
  palletNo: string;
  palletStatus: string;
  shipmentNo: string;
  multiboxes: LogisticsMultiboxGroup[];
  rows: PackingHierarchyRow[];
};

@Component({
  selector: 'app-sn-result',
  standalone: false,
  templateUrl: './sn-result.component.html',
  styleUrl: './sn-result.component.scss',
})
export class SnResultComponent implements AfterViewInit, AfterViewChecked, OnDestroy {
  readonly tabs: Array<{ id: SnResultTab; label: string; icon: string }> = [
    { id: 'preview', label: 'SN Preview', icon: 'visibility' },
    { id: 'history', label: 'SN History', icon: 'history' },
  ];

  activeTab: SnResultTab = 'preview';
  query = '';
  loading = false;
  previewLoading = false;
  errorMessage = '';
  previewMessage = '';
  historyMessage = '';
  traceResult: TraceSearchResponse | null = null;
  workflowSnapshot: WorkflowSnapshot | null = null;
  previewConnectorPath = '';
  previewConnectorWidth = 0;
  previewConnectorHeight = 0;
  previewFlowCardsPerRow = this.getPreviewFlowCardsPerRow();
  isChildDetailsOpen = false;
  isAssembledPartsOpen = false;
  isSnValuesOpen = false;
  activePreviewStation: PreviewStationNode | null = null;
  activePreviewLogistics: PreviewFlowNode | null = null;
  logisticsLoading = false;
  logisticsMessage = '';
  logisticsRows: PackingHierarchyRow[] = [];
  selectedShipmentPalletNo = '';
  activeSnListMultibox: LogisticsMultiboxGroup | null = null;
  previewStationStatusById: Record<number, PreviewStatus> = {};

  private readonly workflowApiUrl = `${environment.apiUrl}/api/workflow`;
  private routeSub: Subscription | null = null;
  private previewConnectorFrame: number | null = null;
  private previewConnectorSignature = '';
  @ViewChild('previewProcessFlow') private previewProcessFlowRef?: ElementRef<HTMLElement>;
  @ViewChildren('previewFlowNode') private previewFlowNodeRefs?: QueryList<ElementRef<HTMLElement>>;

  constructor(
    private http: HttpClient,
    private traceabilityService: TraceabilityService,
    private packingService: PackingService,
    private route: ActivatedRoute,
    private router: Router,
    private location: Location,
    private cdr: ChangeDetectorRef
  ) {
    this.routeSub = this.route.queryParamMap.subscribe((params) => {
      const serial = String(params.get('q') || '').trim();
      if (!serial) {
        return;
      }

      this.query = serial;
      this.activeTab = 'preview';
      this.loadSerial(serial);
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
    this.routeSub?.unsubscribe();
    if (this.previewConnectorFrame) {
      window.cancelAnimationFrame(this.previewConnectorFrame);
    }
  }

  selectTab(tab: SnResultTab): void {
    this.activeTab = tab;
  }

  onSearch(): void {
    const serial = this.query.trim();
    if (!serial) {
      this.errorMessage = 'Please enter a serial number.';
      return;
    }

    this.router.navigate(['/dashboard/sn-result'], {
      queryParams: { q: serial, t: Date.now() },
    });
  }

  goBack(): void {
    this.location.back();
  }

  trackByTab(index: number, tab: { id: SnResultTab }): string {
    return `${index}-${tab.id}`;
  }

  trackByHistory(index: number, row: TraceHistoryRow | SnHistoryDisplayRow): string {
    return `${index}-${row.id}`;
  }

  trackByFlowNode(index: number, node: PreviewFlowNode): string {
    return `${index}-${node.id}`;
  }

  getSnResultTabLabel(tabId: SnResultTab): string {
    const snNumber = this.serialNumber;

    return tabId === 'preview'
      ? `SN Chart - ${snNumber}`
      : `SN History - ${snNumber}`;
  }

  get serialNumber(): string {
    return this.traceResult?.serial?.sn || this.query || '-';
  }

  get workOrderNumber(): string {
    return this.traceResult?.device?.work_order || this.workflowSnapshot?.workOrder?.wo || '-';
  }

  get partNumber(): string {
    return this.workflowSnapshot?.partNumber?.pn || this.traceResult?.device?.pn || '-';
  }

  get plantName(): string {
    return this.workflowSnapshot?.workOrder?.plant || this.traceResult?.device?.plant || 'Select Plant';
  }

  get siteName(): string {
    return this.workflowSnapshot?.workOrder?.site_name || this.traceResult?.device?.site || 'Select Site';
  }

  get childSummary(): string {
    const childCount = this.workflowSnapshot?.bom?.length || 0;
    return childCount === 1 ? '1 Child' : `${childCount} Childs`;
  }

  get parentPartNumber(): string {
    const partNumber = this.partNumber;
    const revision = String(this.workflowSnapshot?.workOrder?.revision || this.traceResult?.device?.revision || '').trim();

    if (!revision || revision === '-' || !partNumber || partNumber === '-') {
      return partNumber || '-';
    }

    const revisionSuffix = `-${revision}`;
    return partNumber.endsWith(revisionSuffix)
      ? partNumber.slice(0, -revisionSuffix.length)
      : partNumber;
  }

  get boxLeftLabel(): string {
    const valueCount = this.snValueRows.length;
    const historyCount = this.snPreviewHistoryRows.length;

    if (valueCount > 0) {
      return `${valueCount} value${valueCount === 1 ? '' : 's'}`;
    }

    if (historyCount > 0) {
      return `${historyCount} history`;
    }

    return this.workflowSnapshot?.workOrder?.lot || 'NA';
  }

  get boxRightLabel(): string {
    return 'Assembled parts';
  }

  get assembledParts(): TraceAssembledPart[] {
    return this.traceResult?.assembled_parts || [];
  }

  get snValueRows(): TraceSnValue[] {
    return this.traceResult?.sn_values || [];
  }

  get snPreviewHistoryRows(): SnHistoryDisplayRow[] {
    return this.snHistoryDisplayRows.slice(0, 3);
  }

  get snValuesSummary(): string {
    const count = this.snValueRows.length;
    return count === 1 ? '1 Connected Value' : `${count} Connected Values`;
  }

  get assembledPartsSummary(): string {
    const count = this.assembledParts.length;
    return count === 1 ? '1 Part' : `${count} Parts`;
  }

  get historyRows(): TraceHistoryRow[] {
    return (this.traceResult?.history || []).filter((history) => this.shouldShowSnHistoryRow(history));
  }

  get snHistoryDisplayRows(): SnHistoryDisplayRow[] {
    const displayRows: SnHistoryDisplayRow[] = this.historyRows.map((history, index) => {
        const dateTime = this.parseHistoryDate(history.date_time);
        const bindingDetails = this.getBindingDisplayDetails(history);

        return {
          id: `${history.event_type || 'history'}-${history.id}-${history.date_time || index}`,
          stationCode: history.station || this.traceResult?.serial?.current_station_code || '-',
          stationLoginId: history.user_name || '-',
          date: dateTime.date,
          time: dateTime.time,
          actionDescription: bindingDetails?.actionDescription || this.buildHistoryActionDescription(history),
          linkSerial: bindingDetails?.linkSerial,
          linkLabel: bindingDetails?.linkLabel,
          linkRole: bindingDetails?.linkRole,
          relatedPart: bindingDetails?.relatedPart,
          relatedRevision: bindingDetails?.relatedRevision,
          isBinding: !!bindingDetails,
        };
      });

    if (this.traceResult && !this.hasGeneratedHistoryRow(this.historyRows)) {
      const generatedDateTime = this.parseHistoryDate(this.traceResult.serial.created_at);
      displayRows.push({
        id: 'generated-fallback',
        stationCode: this.traceResult.serial.current_station_code || 'Not started',
        stationLoginId: 'system',
        date: generatedDateTime.date,
        time: generatedDateTime.time,
        actionDescription: 'SN_GENERATED - SN generated',
      });
    }

    const allowedRows = displayRows.filter((row) => this.shouldShowSnHistoryDisplayRow(row));

    if (allowedRows.length) {
      return allowedRows;
    }

    if (this.traceResult) {
      const fallbackDateTime = this.parseHistoryDate(
        this.traceResult.serial.last_moved_at || this.traceResult.serial.updated_at || this.traceResult.serial.created_at
      );

      return [
        {
          id: 'status-fallback',
          stationCode: this.traceResult.serial.current_station_code || 'Not started',
          stationLoginId: 'system',
          date: fallbackDateTime.date,
          time: fallbackDateTime.time,
          actionDescription: this.traceResult.serial.status
            ? `Serial status: ${this.traceResult.serial.status}`
            : 'Serial generated',
        },
      ];
    }

    return [];
  }

  get bomChildren(): Array<NonNullable<WorkflowSnapshot['bom']>[number]> {
    return this.workflowSnapshot?.bom || [];
  }

  get activePreviewStationRules(): string[] {
    if (!this.activePreviewStation) {
      return [];
    }

    return this.workflowSnapshot?.stationRules?.[this.activePreviewStation.station_code] || [];
  }

  get activePreviewStationMultiboxNo(): string {
    if (!this.activePreviewStation || !this.isPackStation(this.activePreviewStation)) {
      return '';
    }

    return String(this.traceResult?.serial?.multibox_no || '').trim();
  }

  get activePreviewStationPalletNo(): string {
    if (!this.activePreviewStation || !this.isPackStation(this.activePreviewStation)) {
      return '';
    }

    return String(this.traceResult?.serial?.pallet_no || '').trim();
  }

  get activePreviewStationShipmentNo(): string {
    if (!this.activePreviewStation || !this.isPackStation(this.activePreviewStation)) {
      return '';
    }

    return String(this.traceResult?.serial?.shipment_no || '').trim();
  }

  get activeLogisticsTitle(): string {
    return this.activePreviewLogistics?.title || 'Packing';
  }

  get activeLogisticsIcon(): string {
    return this.activePreviewLogistics?.icon || 'inventory_2';
  }

  get activeLogisticsStatus(): PreviewStatus {
    return this.getLogisticsStatus(this.activePreviewLogistics?.variant);
  }

  get activeLogisticsReference(): string {
    switch (this.activePreviewLogistics?.variant) {
      case 'cart':
        return this.traceMultiboxNo;
      case 'pallet':
        return this.tracePalletNo;
      case 'truck':
        return this.traceShipmentNo;
      default:
        return '';
    }
  }

  get activeLogisticsKindLabel(): string {
    switch (this.activePreviewLogistics?.variant) {
      case 'cart':
        return 'MultiBox';
      case 'pallet':
        return 'Pallet';
      case 'truck':
        return 'Shipment';
      default:
        return 'Packing';
    }
  }

  get logisticsMultiboxes(): LogisticsMultiboxGroup[] {
    return this.groupRowsByMultibox(this.logisticsRows);
  }

  get logisticsPallets(): LogisticsPalletGroup[] {
    return this.groupRowsByPallet(this.logisticsRows);
  }

  get selectedShipmentPallet(): LogisticsPalletGroup | null {
    return this.logisticsPallets.find((pallet) => pallet.palletNo === this.selectedShipmentPalletNo) || null;
  }

  get activeSnListRows(): PackingHierarchyRow[] {
    return this.activeSnListMultibox?.rows || [];
  }

  get traceMultiboxNo(): string {
    return String(this.traceResult?.serial?.multibox_no || '').trim();
  }

  get tracePalletNo(): string {
    return String(this.traceResult?.serial?.pallet_no || '').trim();
  }

  get traceShipmentNo(): string {
    return String(this.traceResult?.serial?.shipment_no || '').trim();
  }

  get isSerialShipped(): boolean {
    return Boolean(this.traceShipmentNo);
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

  get flowNodes(): PreviewFlowNode[] {
    const routing = this.workflowSnapshot?.routing || [];
    const statuses = this.workflowSnapshot?.previewStatuses || {};
    const stationNodes: PreviewFlowNode[] = routing.length
      ? routing.map((step, index) => ({
          id: `station-${step.id || step.station_code}-${index}`,
          kind: 'station',
          station: {
            id: step.id || index + 1,
            station_order: step.station_order,
            station_code: step.station_code,
            station_name: step.station_name,
            sample_mode: step.sample_mode,
            report_mode: step.report_mode,
            icon: this.getStationIcon(index, step.station_name, step.station_code, step.sample_mode),
            status: this.previewStationStatusById[step.id] || this.getSerialRouteStatus(step.station_code) || statuses[step.station_code] || step.preview_status || (index === 0 ? 'In Progress' : 'Pending'),
          },
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
      { id: 'operator', kind: 'operator', title: 'Operator / Technician', icon: 'engineering' },
      ...stationNodes,
      { id: 'cart', kind: 'logistics', variant: 'cart', title: 'Cart', icon: 'shopping_cart' },
      { id: 'pallet', kind: 'logistics', variant: 'pallet', title: 'Pallet', icon: 'inventory_2' },
      { id: 'truck', kind: 'logistics', variant: 'truck', title: 'Truck', subtitle: 'Dispatch / Shipping', icon: 'local_shipping' },
    ];
  }

  get flowRows(): PreviewFlowRow[] {
    const rows: PreviewFlowRow[] = [];
    const cardsPerRow = Math.max(2, this.previewFlowCardsPerRow);

    for (let index = 0; index < this.flowNodes.length; index += cardsPerRow) {
      const rowIndex = rows.length;
      const nodes = this.flowNodes.slice(index, index + cardsPerRow);
      const isReversed = rowIndex % 2 === 1;
      rows.push({
        nodes: isReversed ? [...nodes].reverse() : nodes,
        isReversed,
        turnSide: isReversed ? 'left' : 'right',
      });
    }

    return rows;
  }

  formatHistoryResult(result: string): string {
    const normalized = (result || '').toUpperCase();
    if (normalized === 'PASS' || normalized === 'FAIL' || normalized === 'SCRAP') {
      return normalized;
    }

    if (normalized === 'UNDO_SCRAP') {
      return 'UNDO SCRAP';
    }

    return result || '-';
  }

  openLinkedSerial(serial: string | undefined): void {
    const normalized = String(serial || '').trim();
    if (!normalized) {
      return;
    }

    this.router.navigate(['/dashboard/sn-result'], {
      queryParams: { q: normalized, t: Date.now() },
    });
  }

  private getSerialRouteStatus(stationCode: string): PreviewStatus | null {
    const routeStep = (this.traceResult?.routing || []).find(
      (step) => String(step.station_code || '').trim().toUpperCase() === String(stationCode || '').trim().toUpperCase()
    );

    if (!routeStep) {
      return null;
    }

    if (this.isStationFailed(stationCode)) {
      return 'Failed';
    }

    switch (routeStep.state) {
      case 'completed':
        return 'Completed';
      case 'current':
        return 'In Progress';
      case 'pending':
        return 'Pending';
      default:
        return null;
    }
  }

  isPackStation(station: PreviewStationNode): boolean {
    return `${station.station_code || ''} ${station.station_name || ''}`.toLowerCase().includes('pack');
  }

  private parseHistoryDate(value: string | null | undefined): { date: string; time: string } {
    const parsedDate = value ? new Date(value) : null;

    if (!parsedDate || Number.isNaN(parsedDate.getTime())) {
      return { date: '-', time: '-' };
    }

    return {
      date: parsedDate.toLocaleDateString('en-CA'),
      time: parsedDate.toLocaleTimeString('en-GB', {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
      }),
    };
  }

  private buildHistoryActionDescription(history: TraceHistoryRow): string {
    const eventType = String(history.event_type || '').trim();
    const parts = [
      eventType.toUpperCase() === 'BOM_BIND' ? '' : eventType,
      this.formatHistoryResult(history.result) !== '-' ? this.formatHistoryResult(history.result) : '',
      history.additional_info,
    ].filter((part) => String(part || '').trim());

    return parts.length ? parts.join(' - ') : 'Station activity recorded';
  }

  private getBindingDisplayDetails(history: TraceHistoryRow): Partial<SnHistoryDisplayRow> | null {
    const eventType = String(history.event_type || '').trim().toUpperCase();
    if (eventType !== 'BOM_BIND') {
      return null;
    }

    const currentSn = this.normalizeSerialValue(this.traceResult?.serial?.sn);
    const currentRsn = this.normalizeSerialValue(this.traceResult?.serial?.rsn);
    const parentSn = String(history.parent_sn || '').trim();
    const parentRsn = String(history.parent_rsn || '').trim();
    const childSn = String(history.child_sn || '').trim();
    const childRsn = String(history.child_rsn || '').trim();
    const parentValues = [parentSn, parentRsn]
      .map((value) => this.normalizeSerialValue(value))
      .filter((value) => !!value);
    const isFatherContext = parentValues.includes(currentSn) || parentValues.includes(currentRsn);

    if (isFatherContext) {
      const fatherLabel = this.preferredSerialLabel(parentRsn, parentSn, this.serialNumber);
      const childLabel = this.preferredSerialLabel(childSn, childRsn);
      if (childLabel === '-') {
        return null;
      }

      return {
        actionDescription: `This father SN ${fatherLabel} is binded with child SN`,
        linkSerial: childLabel,
        linkLabel: childLabel,
        linkRole: 'child',
        relatedPart: history.child_pn,
        relatedRevision: history.child_revision,
      };
    }

    const childLabel = this.preferredSerialLabel(childSn, childRsn, this.serialNumber);
    const fatherLabel = this.preferredSerialLabel(parentRsn, parentSn);
    if (fatherLabel === '-') {
      return null;
    }

    return {
      actionDescription: `This child SN ${childLabel} is binded with father SN`,
      linkSerial: fatherLabel,
      linkLabel: fatherLabel,
      linkRole: 'father',
      relatedPart: history.parent_pn,
      relatedRevision: history.parent_revision,
    };
  }

  private preferredSerialLabel(...values: Array<string | null | undefined>): string {
    return values.map((value) => String(value || '').trim()).find((value) => !!value) || '-';
  }

  formatSnValueRow(row: TraceSnValue): string {
    const parts = [
      row.chip_id ? `Chip ID: ${row.chip_id}` : '',
      row.imes ? `IMES: ${row.imes}` : '',
    ].filter((part) => !!part);

    return parts.length ? parts.join(' | ') : '-';
  }

  formatSnValueDate(value: string | null | undefined): string {
    const parsed = this.parseHistoryDate(value || '');
    return parsed.date === '-' ? '-' : `${parsed.date} ${parsed.time}`;
  }

  private normalizeSerialValue(value: string | null | undefined): string {
    return String(value || '').trim().toUpperCase();
  }

  private shouldShowSnHistoryRow(history: TraceHistoryRow): boolean {
    const result = String(history.result || '').trim().toUpperCase();
    const eventType = String(history.event_type || '').trim().toUpperCase();
    const info = String(history.additional_info || '').trim().toUpperCase();

    if (eventType === 'SN_GENERATED' || info === 'SN GENERATED') {
      return true;
    }

    if (eventType === 'BOM_BIND') {
      return true;
    }

    if (result === 'SCRAP' || result === 'UNDO_SCRAP') {
      return true;
    }

    if (eventType && eventType !== 'PASS' && eventType !== 'FAIL') {
      return false;
    }

    return result === 'PASS' || result === 'FAIL';
  }

  private shouldShowSnHistoryDisplayRow(row: SnHistoryDisplayRow): boolean {
    const action = String(row.actionDescription || '').trim().toUpperCase();

    if (!action || action.startsWith('NOT_PASS') || action.includes('ALREADY PASSED') || action.includes('PREVIOUS STATION')) {
      return false;
    }

    return action.startsWith('PASS')
      || action.startsWith('FAIL')
      || action.startsWith('SCRAP')
      || action.startsWith('UNDO SCRAP')
      || action.startsWith('SN_GENERATED')
      || action.includes('BOM_BIND')
      || action.includes('BINDED')
      || action.includes('BOUND');
  }

  private hasGeneratedHistoryRow(rows: TraceHistoryRow[]): boolean {
    return rows.some((history) => {
      const eventType = String(history.event_type || '').trim().toUpperCase();
      const info = String(history.additional_info || '').trim().toUpperCase();
      return eventType === 'SN_GENERATED' || info === 'SN GENERATED';
    });
  }

  getStatusClass(status: PreviewStatus): string {
    return status.toLowerCase().replace(/\s+/g, '-');
  }

  private isStationFailed(stationCode: string): boolean {
    const normalizedStation = String(stationCode || '').trim().toUpperCase();
    if (!normalizedStation) {
      return false;
    }

    const stationRows = (this.traceResult?.history || [])
      .filter((history) => String(history.station || '').trim().toUpperCase() === normalizedStation)
      .map((history) => ({
        result: String(history.result || '').trim().toUpperCase(),
        date: this.parseHistoryTimestamp(history.date_time),
      }))
      .filter((history) => history.result === 'PASS' || history.result === 'FAIL')
      .sort((a, b) => b.date - a.date);

    return stationRows[0]?.result === 'FAIL';
  }

  private parseHistoryTimestamp(value: string | null | undefined): number {
    const timestamp = value ? new Date(value).getTime() : 0;
    return Number.isFinite(timestamp) ? timestamp : 0;
  }

  openChildDetails(): void {
    this.isChildDetailsOpen = true;
  }

  closeChildDetails(): void {
    this.isChildDetailsOpen = false;
  }

  openSnValuesDetails(): void {
    this.isSnValuesOpen = true;
  }

  closeSnValuesDetails(): void {
    this.isSnValuesOpen = false;
  }

  openAssembledParts(): void {
    this.isAssembledPartsOpen = true;
  }

  closeAssembledParts(): void {
    this.isAssembledPartsOpen = false;
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

  openPreviewLogisticsDetails(event: Event, node: PreviewFlowNode): void {
    event.preventDefault();
    event.stopPropagation();

    this.activePreviewLogistics = node;
    this.logisticsRows = [];
    this.logisticsMessage = '';
    this.selectedShipmentPalletNo = '';
    this.activeSnListMultibox = null;

    const reference = this.getLogisticsReferenceForNode(node);
    if (!reference) {
      this.logisticsLoading = false;
      this.logisticsMessage = `${node.title || 'Packing'} is not completed for this SN.`;
      return;
    }

    this.logisticsLoading = true;
    this.packingService.listHierarchy(reference).subscribe({
      next: (response) => {
        this.logisticsRows = response.data || [];
        this.logisticsLoading = false;
        this.logisticsMessage = this.logisticsRows.length ? '' : `No records found for ${reference}.`;
      },
      error: (error) => {
        this.logisticsRows = [];
        this.logisticsLoading = false;
        this.logisticsMessage = error?.error?.message || `No records found for ${reference}.`;
      },
    });
  }

  closePreviewLogisticsDetails(): void {
    this.activePreviewLogistics = null;
    this.logisticsLoading = false;
    this.logisticsMessage = '';
    this.logisticsRows = [];
    this.selectedShipmentPalletNo = '';
    this.activeSnListMultibox = null;
    this.queuePreviewConnectorRefresh();
  }

  selectShipmentPallet(pallet: LogisticsPalletGroup): void {
    this.selectedShipmentPalletNo = pallet.palletNo;
    this.activeSnListMultibox = null;
  }

  openMultiboxSerials(multibox: LogisticsMultiboxGroup): void {
    this.activeSnListMultibox = multibox;
  }

  closeMultiboxSerials(): void {
    this.activeSnListMultibox = null;
  }

  openPackagingHistory(): void {
    this.router.navigate(['/dashboard/operations/packing/history']);
  }

  getLogisticsStatus(variant?: LogisticsVariant): PreviewStatus {
    switch (variant) {
      case 'cart':
        return this.traceMultiboxNo ? 'Completed' : 'Pending';
      case 'pallet':
        return this.tracePalletNo ? 'Completed' : 'Pending';
      case 'truck':
        return this.traceShipmentNo ? 'Completed' : 'Pending';
      default:
        return 'Pending';
    }
  }

  pausePreviewStation(): void {
    this.setPreviewStationStatus('Paused');
  }

  completePreviewStation(): void {
    this.setPreviewStationStatus('Completed');
  }

  private loadSerial(serial: string): void {
    this.loading = true;
    this.previewLoading = true;
    this.errorMessage = '';
    this.previewMessage = '';
    this.historyMessage = '';
    this.traceResult = null;
    this.workflowSnapshot = null;
    this.previewStationStatusById = {};
    this.isChildDetailsOpen = false;
    this.isAssembledPartsOpen = false;
    this.isSnValuesOpen = false;
    this.activePreviewStation = null;
    this.activePreviewLogistics = null;
    this.logisticsLoading = false;
    this.logisticsMessage = '';
    this.logisticsRows = [];
    this.selectedShipmentPalletNo = '';
    this.activeSnListMultibox = null;

    this.traceabilityService.search(serial).subscribe({
      next: (result) => {
        this.traceResult = result;
        this.loading = false;
        this.historyMessage = result.history?.length ? '' : 'No SN history found for this serial number.';
        this.loadWorkflowPreview(result.device?.pn, result.device?.work_order);
      },
      error: (error) => {
        this.loading = false;
        this.previewLoading = false;
        this.errorMessage = error?.error?.message || 'No preview data found for this serial number.';
        this.previewMessage = 'No preview data found for this serial number.';
        this.historyMessage = 'No SN history found for this serial number.';
      },
    });
  }

  private loadWorkflowPreview(partNumber: string | undefined, workOrder: string | undefined): void {
    const pn = String(partNumber || '').trim();
    if (!pn) {
      this.previewLoading = false;
      this.previewMessage = 'No preview data found for this serial number.';
      return;
    }

    let params = new HttpParams().set('pn', pn);
    const wo = String(workOrder || '').trim();
    if (wo) {
      params = params.set('wo', wo);
    }
    this.http.get<WorkflowSnapshot>(`${this.workflowApiUrl}/by-pn`, { params }).subscribe({
      next: (snapshot) => {
        this.workflowSnapshot = snapshot;
        this.previewLoading = false;
        this.previewMessage = snapshot?.routing?.length ? '' : 'No preview data found for this serial number.';
        this.queuePreviewConnectorRefresh();
      },
      error: () => {
        this.workflowSnapshot = null;
        this.previewLoading = false;
        this.previewMessage = 'No preview data found for this serial number.';
      },
    });
  }

  private getLogisticsReferenceForNode(node: PreviewFlowNode): string {
    switch (node.variant) {
      case 'cart':
        return this.traceMultiboxNo;
      case 'pallet':
        return this.tracePalletNo;
      case 'truck':
        return this.traceShipmentNo;
      default:
        return '';
    }
  }

  private groupRowsByMultibox(rows: PackingHierarchyRow[]): LogisticsMultiboxGroup[] {
    const groups = new Map<string, PackingHierarchyRow[]>();

    rows.forEach((row) => {
      const multiboxNo = String(row.multibox_no || 'Unassigned MultiBox').trim() || 'Unassigned MultiBox';
      groups.set(multiboxNo, [...(groups.get(multiboxNo) || []), row]);
    });

    return Array.from(groups.entries()).map(([multiboxNo, groupRows]) => ({
      multiboxNo,
      multiboxStatus: String(groupRows[0]?.multibox_status || '-'),
      palletNo: String(groupRows[0]?.pallet_no || '-'),
      shipmentNo: String(groupRows[0]?.shipment_no || '-'),
      rows: groupRows,
    }));
  }

  private groupRowsByPallet(rows: PackingHierarchyRow[]): LogisticsPalletGroup[] {
    const groups = new Map<string, PackingHierarchyRow[]>();

    rows.forEach((row) => {
      const palletNo = String(row.pallet_no || 'Unassigned Pallet').trim() || 'Unassigned Pallet';
      groups.set(palletNo, [...(groups.get(palletNo) || []), row]);
    });

    return Array.from(groups.entries()).map(([palletNo, groupRows]) => ({
      palletNo,
      palletStatus: String(groupRows[0]?.pallet_status || '-'),
      shipmentNo: String(groupRows[0]?.shipment_no || '-'),
      multiboxes: this.groupRowsByMultibox(groupRows),
      rows: groupRows,
    }));
  }

  private getStationIcon(index: number, name: string, code: string, sampleMode: string): string {
    const normalized = `${name} ${code}`.toLowerCase();
    if (sampleMode === 'Sample') {
      return 'saved_search';
    }

    if (normalized.includes('label')) {
      return 'qr_code_2';
    }

    if (normalized.includes('test') || normalized.includes('aoi')) {
      return 'biotech';
    }

    if (normalized.includes('pack') || normalized.includes('box')) {
      return 'inventory_2';
    }

    const icons = ['desktop_windows', 'verified_user', 'precision_manufacturing', 'memory', 'settings_applications'];
    return icons[index % icons.length];
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
    this.closePreviewStationDetails();
  }

  private buildPreviewConnectorSignature(): string {
    const flowIds = this.flowNodes.map((node) => node.id).join('|');
    const routeSignature = (this.workflowSnapshot?.routing || [])
      .map((step) => `${step.id}:${step.station_code}:${step.sample_mode}`)
      .join('|');

    return [
      this.activeTab,
      this.previewFlowCardsPerRow,
      flowIds,
      routeSignature,
      this.traceMultiboxNo,
      this.tracePalletNo,
      this.traceShipmentNo,
      this.previewLoading,
    ].join(':');
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

    if (!container || this.activeTab !== 'preview' || this.previewLoading || nodeRefs.length < 2) {
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

    const orderedRects = this.flowNodes
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
    const containerWidth = this.previewProcessFlowRef?.nativeElement.clientWidth || 0;
    const width = containerWidth || (typeof window === 'undefined' ? 1140 : Math.max(360, window.innerWidth - 300));
    const availablePreviewWidth = Math.max(320, width - 24);
    const estimatedCardWidth = width >= 1240 ? 144 : 156;
    const estimatedLineWidth = width >= 1240 ? 30 : 36;
    const estimatedCards = Math.floor(
      (availablePreviewWidth + estimatedLineWidth) / (estimatedCardWidth + estimatedLineWidth)
    );

    return Math.max(2, Math.min(8, estimatedCards));
  }
}
