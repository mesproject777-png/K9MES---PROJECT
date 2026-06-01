import { Component, OnDestroy, OnInit } from '@angular/core';
import { LabelMasterDto, LabelService } from '../../services/label.service';
import { TraceabilityService, TraceRouteStep, TraceSearchResponse } from '../../services/traceability.service';

type LabelStatus = 'Active' | 'Draft';

interface LabelMaster {
  id: number;
  code: string;
  description: string;
  type: string;
  status: LabelStatus;
  prnText: string;
  fileName: string;
  createdDate: string;
  fields: ExtractedLabelFields;
}

interface ExtractedLabelFields {
  model: string;
  serialNumber: string;
  ean: string;
  macId: string;
  chipId: string;
  ratedVoltage: string;
  bisNumber: string;
}

interface StickerLabelData {
  productName: string;
  model: string;
  partNumber: string;
  serialNumber: string;
  workOrder: string;
  revision: string;
  macId: string;
  chipId: string;
  ean: string;
  manufacturingDate: string;
  bisNumber: string;
}

interface MesLabelData {
  rsn: string;
  serialNumber: string;
  workOrder: string;
  quantity: string;
  status: string;
  site: string;
  plant: string;
  lot: string;
  partNumber: string;
  parentPartNumber: string;
  revision: string;
  description: string;
  pnType: string;
  stationCode: string;
  stationName: string;
}

interface PreviewElement {
  id: string;
  kind: 'text' | 'barcode' | 'qr' | 'box';
  text: string;
  x: number;
  y: number;
  width: number;
  height: number;
  fontSize: number;
}

interface PlaceholderResolution {
  name: string;
  token: string;
  value: string;
  resolved: boolean;
}

type PrintDensity = 6 | 8 | 12 | 24;
type PrintQuality = 'Grayscale' | 'Bitonal';
type LabelSizeUnit = 'inches' | 'cm' | 'mm';

@Component({
  selector: 'app-label',
  standalone: false,
  templateUrl: './label.component.html',
  styleUrl: './label.component.scss'
})
export class LabelComponent implements OnInit, OnDestroy {
  private readonly previewWidth = 390;
  private readonly previewHeight = 540;

  readonly labelTypes = ['Product', 'Box', 'Carton', 'Shipping'];
  readonly fieldLabels: Array<{ key: keyof ExtractedLabelFields; label: string }> = [
    { key: 'model', label: 'Model' },
    { key: 'serialNumber', label: 'Serial Number' },
    { key: 'ean', label: 'EAN' },
    { key: 'macId', label: 'MAC ID' },
    { key: 'chipId', label: 'CHIP ID' },
    { key: 'bisNumber', label: 'BIS/R Number' },
  ];
  readonly editableStickerFields: Array<{ key: keyof StickerLabelData; label: string }> = [
    { key: 'productName', label: 'Product Name' },
    { key: 'model', label: 'Model' },
    { key: 'partNumber', label: 'PN' },
    { key: 'serialNumber', label: 'SN / RSN' },
    { key: 'workOrder', label: 'Work Order' },
    { key: 'revision', label: 'Revision' },
    { key: 'macId', label: 'MAC ID' },
    { key: 'chipId', label: 'CHIP ID' },
    { key: 'ean', label: 'EAN' },
    { key: 'manufacturingDate', label: 'MFG Date' },
    { key: 'bisNumber', label: 'BIS / Cert No' },
  ];
  readonly printDensityOptions: Array<{ value: PrintDensity; label: string }> = [
    { value: 6, label: '6 dpmm (152 dpi)' },
    { value: 8, label: '8 dpmm (203 dpi)' },
    { value: 12, label: '12 dpmm (300 dpi)' },
    { value: 24, label: '24 dpmm (600 dpi)' },
  ];
  readonly printQualityOptions: PrintQuality[] = ['Grayscale', 'Bitonal'];
  readonly labelSizeUnits: LabelSizeUnit[] = ['inches', 'cm', 'mm'];

  labels: LabelMaster[] = [];
  activeTab: 'create' | 'history' = 'create';
  selectedLabelId: number | null = null;
  labelCode = '';
  labelDescription = '';
  labelType = this.labelTypes[0];
  searchCode = '';
  isLabelListOpen = false;
  uploadedFileName = '';
  rawPrnText = '';
  generatedPrnText = '';
  isRawEditorOpen = false;
  isGeneratedEditorOpen = false;
  rsnQuery = '';
  isSearchingRsn = false;
  traceResult: TraceSearchResponse | null = null;
  routingStations: TraceRouteStep[] = [];
  selectedStationCode = '';
  mesData: MesLabelData = this.createEmptyMesData();
  stickerData: StickerLabelData = this.createEmptyStickerData();
  extractedFields = this.createEmptyFields();
  previewElements: PreviewElement[] = [];
  codeImageById: Record<string, string> = {};
  placeholderResolutions: PlaceholderResolution[] = [];
  unresolvedPlaceholderNames: string[] = [];
  previewMode: 'html' | 'image' | 'empty' = 'empty';
  labelaryPreviewUrl = '';
  isPreviewLoading = false;
  printDensity: PrintDensity = 12;
  printQuality: PrintQuality = 'Grayscale';
  labelWidth = 4;
  labelHeight = 6;
  labelSizeUnit: LabelSizeUnit = 'inches';
  private hasCustomLabelSize = false;
  message = '';
  messageType: 'success' | 'error' | 'info' = 'info';

  constructor(
    private traceabilityService: TraceabilityService,
    private labelService: LabelService
  ) {}

  ngOnInit(): void {
    this.startNewLabel();
    this.loadLabelsFromDatabase();
  }

  ngOnDestroy(): void {
    this.revokeLabelaryPreviewUrl();
  }

  get filteredLabels(): LabelMaster[] {
    const query = this.searchCode.trim().toLowerCase();
    if (!query) {
      return this.labels;
    }

    return this.labels.filter((label) =>
      label.code.toLowerCase().includes(query) ||
      (label.description || '').toLowerCase().includes(query)
    );
  }

  get isEditMode(): boolean {
    return this.selectedLabelId !== null;
  }

  get canSavePrn(): boolean {
    return Boolean(this.rawPrnText.trim() && this.labelCode.trim());
  }

  startNewLabel(): void {
    this.selectedLabelId = null;
    this.activeTab = 'create';
    this.labelCode = '';
    this.labelDescription = '';
    this.labelType = this.labelTypes[0];
    this.searchCode = '';
    this.uploadedFileName = '';
    this.rawPrnText = '';
    this.generatedPrnText = '';
    this.printDensity = 12;
    this.printQuality = 'Grayscale';
    this.labelWidth = 4;
    this.labelHeight = 6;
    this.labelSizeUnit = 'inches';
    this.hasCustomLabelSize = false;
    this.placeholderResolutions = [];
    this.unresolvedPlaceholderNames = [];
    this.rsnQuery = '';
    this.traceResult = null;
    this.routingStations = [];
    this.selectedStationCode = '';
    this.mesData = this.createEmptyMesData();
    this.stickerData = this.createEmptyStickerData();
    this.extractedFields = this.createEmptyFields();
    this.previewElements = [];
    this.codeImageById = {};
    this.revokeLabelaryPreviewUrl();
    this.previewMode = 'empty';
    this.setMessage('Ready to create a new label.', 'info');
  }

  saveLabel(): void {
    const code = this.normalizeLabelCode(this.labelCode);

    if (!code) {
      this.setMessage('Label Code is required.', 'error');
      return;
    }

    if (!/^[A-Z0-9-]+$/.test(code)) {
      this.setMessage('Use alphanumeric characters and hyphen only for Label Code.', 'error');
      return;
    }

    const duplicate = this.labels.find((label) => label.code === code && label.id !== this.selectedLabelId);
    if (duplicate) {
      this.setMessage('Label Code already exists.', 'error');
      return;
    }

    if (!this.rawPrnText.trim()) {
      this.setMessage('Enter completed PRN/ZPL template content before saving.', 'error');
      return;
    }

    const masterPayload = {
      label_code: code,
      label_description: this.labelDescription.trim(),
      status: 'Active' as const,
    };
    const saveMaster$ = this.selectedLabelId === null
      ? this.labelService.createLabel(masterPayload)
      : this.labelService.updateLabel(this.selectedLabelId, masterPayload);

    this.setMessage('Saving label to database...', 'info');
    saveMaster$.subscribe({
      next: (savedLabel) => {
        this.selectedLabelId = savedLabel.id;
        this.labelService.savePrnTemplate(savedLabel.id, {
          prn_file_name: this.uploadedFileName,
          prn_content: this.rawPrnText,
          preview_data: null,
        }).subscribe({
          next: () => {
            this.labelCode = code;
            this.loadLabelsFromDatabase();
            this.setMessage('Label and PRN template saved to database.', 'success');
          },
          error: () => {
            this.setMessage('Label saved, but PRN template could not be saved.', 'error');
          },
        });
      },
      error: (error) => {
        const message = error?.error?.error || 'Unable to save label.';
        this.setMessage(message, 'error');
      },
    });
    this.labelCode = code;
  }

  selectLabel(label: LabelMaster): void {
    this.activeTab = 'create';
    this.setMessage(`Loading ${label.code} from database...`, 'info');

    this.labelService.getLabel(label.id).subscribe({
      next: (response) => {
        this.selectedLabelId = response.label.id;
        this.labelCode = response.label.label_code;
        this.labelDescription = response.label.label_description || '';
        this.labelType = this.labelTypes[0];
        this.uploadedFileName = response.prn_template?.prn_file_name || '';
        this.rawPrnText = response.prn_template?.prn_content || '';
        this.hasCustomLabelSize = false;
        this.syncLabelSizeFromZpl(this.rawPrnText);
        this.extractedFields = this.createEmptyFields();
        this.refreshExtractedFields();
        this.generatePreview();
        this.isLabelListOpen = false;
        this.setMessage(`${response.label.label_code} loaded for editing.`, 'info');
      },
      error: () => {
        this.setMessage('Unable to load selected label.', 'error');
      },
    });
  }

  selectTab(tab: 'create' | 'history'): void {
    this.activeTab = tab;
    if (tab === 'history') {
      this.loadLabelsFromDatabase();
    }
  }

  formatCreatedDate(value: string | undefined): string {
    if (!value) {
      return '-';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }

    return date.toLocaleDateString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    });
  }

  loadFirstSearchResult(): void {
    const firstMatch = this.filteredLabels[0];
    if (firstMatch) {
      this.selectLabel(firstMatch);
    }
  }

  openLabelList(): void {
    this.isLabelListOpen = true;
  }

  closeLabelList(): void {
    this.isLabelListOpen = false;
  }

  onPrnFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    this.uploadedFileName = file.name;
    const reader = new FileReader();
    reader.onload = () => {
      this.rawPrnText = String(reader.result ?? '');
      this.refreshExtractedFields();
      this.generatePreview();
      input.value = '';
    };
    reader.onerror = () => {
      this.setMessage('Unable to read PRN file.', 'error');
      input.value = '';
    };
    reader.readAsText(file);
  }

  onRawPrnChanged(): void {
    this.refreshExtractedFields();
    if (this.rawPrnText.trim()) {
      this.generatePreview();
    } else {
      this.previewMode = 'empty';
      this.previewElements = [];
      this.codeImageById = {};
      this.revokeLabelaryPreviewUrl();
    }
  }

  searchRsn(): void {
    const query = this.rsnQuery.trim();
    if (!query) {
      this.setMessage('Enter RSN / Serial Number.', 'error');
      return;
    }

    this.isSearchingRsn = true;
    this.setMessage('Searching RSN / Serial Number...', 'info');

    this.traceabilityService.search(query).subscribe({
      next: (response) => {
        this.traceResult = response;
        this.routingStations = response.routing || [];
        this.selectedStationCode = this.routingStations.find((step) => step.is_current)?.station_code || this.routingStations[0]?.station_code || '';
        this.applyTraceResult(response);
        this.isSearchingRsn = false;

        if (!this.routingStations.length) {
          this.setMessage('No Routing Stations found for this Part Number.', 'error');
        } else {
          this.setMessage('MES data loaded for RSN.', 'success');
        }

        this.generatePreview();
      },
      error: () => {
        this.traceResult = null;
        this.routingStations = [];
        this.selectedStationCode = '';
        this.mesData = this.createEmptyMesData();
        this.isSearchingRsn = false;
        this.setMessage('No Work Order found for this RSN.', 'error');
        this.generatePreview();
      }
    });
  }

  onStationChange(): void {
    this.updateSelectedStationData();
    this.generatePreview();
  }

  generatePreview(): void {
    this.refreshExtractedFields();
    this.generatedPrnText = this.rawPrnText;
    this.placeholderResolutions = [];
    this.unresolvedPlaceholderNames = [];

    if (!this.rawPrnText.trim()) {
      this.previewMode = 'empty';
      this.previewElements = [];
      this.codeImageById = {};
      this.revokeLabelaryPreviewUrl();
      this.setMessage('Upload or enter PRN/ZPL data before preview.', 'error');
      return;
    }

    void this.generateLabelaryPreview(this.generatedPrnText);
  }

  onLabelarySettingsChanged(): void {
    this.hasCustomLabelSize = true;
    if (this.rawPrnText.trim()) {
      this.generatePreview();
    }
  }

  printLabel(): void {
    if (this.unresolvedPlaceholderNames.length) {
      this.setMessage('Resolve highlighted placeholders before printing.', 'error');
      return;
    }

    const previewMarkup = this.buildPrintPreviewMarkup();
    if (!previewMarkup) {
      this.setMessage('Generate preview before printing.', 'error');
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
      this.setMessage('Unable to open print preview.', 'error');
      return;
    }

    frameDocument.open();
    frameDocument.write(previewMarkup);
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

  onStickerDataChanged(): void {
    if (this.rawPrnText.trim()) {
      this.generatePreview();
    }
  }

  savePrnData(): void {
    if (!this.canSavePrn) {
      this.setMessage('Save a Label Code and PRN data first.', 'error');
      return;
    }

    this.refreshExtractedFields();
    this.saveLabel();
    this.setMessage('PRN data saved with label master.', 'success');
  }

  clearPrnData(): void {
    this.uploadedFileName = '';
    this.rawPrnText = '';
    this.generatedPrnText = '';
    this.placeholderResolutions = [];
    this.unresolvedPlaceholderNames = [];
    this.extractedFields = this.createEmptyFields();
    this.stickerData = this.createEmptyStickerData();
    this.previewElements = [];
    this.codeImageById = {};
    this.revokeLabelaryPreviewUrl();
    this.previewMode = 'empty';
    this.setMessage('PRN data cleared.', 'info');
  }

  getBarcodeBars(text: string): number[] {
    const source = text || 'K9MES';
    return Array.from({ length: 34 }, (_, index) => {
      const code = source.charCodeAt(index % source.length) + index;
      return (code % 4) + 1;
    });
  }

  getQrCells(text: string): boolean[] {
    const source = text || 'K9MES';
    return Array.from({ length: 49 }, (_, index) => {
      const code = source.charCodeAt(index % source.length);
      return ((code + index * 7) % 5) < 2 || index % 8 === 0;
    });
  }

  private rebuildHtmlPreview(zpl: string = this.generatedPrnText || this.rawPrnText): void {
    this.previewElements = this.parseZplToPreviewElements(zpl);
    this.codeImageById = {};
    this.previewMode = this.previewElements.length ? 'html' : 'empty';
  }

  private async generateLabelaryPreview(zpl: string): Promise<void> {
    this.isPreviewLoading = true;
    this.setMessage('Generating PRN/ZPL preview...', 'info');

    try {
      const { width, height } = this.getLabelarySize(zpl);
      const response = await fetch(`http://api.labelary.com/v1/printers/${this.printDensity}dpmm/labels/${width}x${height}/0/`, {
        method: 'POST',
        headers: {
          Accept: 'image/png',
          'Content-Type': 'application/x-www-form-urlencoded',
          'X-Quality': this.printQuality,
        },
        body: zpl,
      });

      if (!response.ok) {
        throw new Error(await response.text());
      }

      const blob = await response.blob();
      this.revokeLabelaryPreviewUrl();
      this.labelaryPreviewUrl = URL.createObjectURL(blob);
      this.previewElements = [];
      this.codeImageById = {};
      this.previewMode = 'image';
      this.setMessage('PRN/ZPL preview generated from template content.', 'success');
    } catch {
      this.rebuildHtmlPreview(zpl);
      void this.generateCodeImages();
      this.setMessage('Local preview generated. Labelary preview was unavailable.', 'info');
    } finally {
      this.isPreviewLoading = false;
    }
  }

  private getLabelarySize(zpl: string): { width: string; height: string } {
    if (!this.hasCustomLabelSize) {
      this.syncLabelSizeFromZpl(zpl);
    }

    const width = this.formatLabelaryInches(this.convertLabelSizeToInches(this.labelWidth));
    const height = this.formatLabelaryInches(this.convertLabelSizeToInches(this.labelHeight));

    return { width, height };
  }

  private syncLabelSizeFromZpl(zpl: string): void {
    const widthDots = Number(zpl.match(/\^PW(\d+)/i)?.[1]) || 0;
    const heightDots = Number(zpl.match(/\^LL(\d+)/i)?.[1]) || 0;

    if (!widthDots && !heightDots) {
      return;
    }

    this.labelSizeUnit = 'inches';
    if (widthDots) {
      this.labelWidth = Number(this.formatLabelaryInches(widthDots / this.printDensity / 25.4));
    }
    if (heightDots) {
      this.labelHeight = Number(this.formatLabelaryInches(heightDots / this.printDensity / 25.4));
    }
  }

  private convertLabelSizeToInches(value: number): number {
    const numericValue = Number(value) || 0;
    if (this.labelSizeUnit === 'cm') {
      return numericValue / 2.54;
    }
    if (this.labelSizeUnit === 'mm') {
      return numericValue / 25.4;
    }
    return numericValue;
  }

  private formatLabelaryInches(value: number): string {
    return Math.max(0.5, Math.min(15, value)).toFixed(2).replace(/\.?0+$/, '');
  }

  private revokeLabelaryPreviewUrl(): void {
    if (this.labelaryPreviewUrl) {
      URL.revokeObjectURL(this.labelaryPreviewUrl);
      this.labelaryPreviewUrl = '';
    }
  }

  private buildPrintPreviewMarkup(): string {
    if (this.previewMode === 'image' && this.labelaryPreviewUrl) {
      return `<!doctype html>
<html>
<head>
  <title>Label Preview</title>
  <style>
    @page { margin: 0; }
    html, body {
      margin: 0;
      padding: 0;
      background: #ffffff;
      width: max-content;
      height: max-content;
      overflow: hidden;
    }
    body {
      display: block;
      line-height: 0;
    }
    img {
      display: block;
      max-width: 100vw;
      height: auto;
      page-break-inside: avoid;
      break-inside: avoid;
      page-break-after: avoid;
    }
  </style>
</head>
<body>
  <img src="${this.labelaryPreviewUrl}" alt="Label Preview">
</body>
</html>`;
    }

    if (this.previewMode === 'html' && this.previewElements.length) {
      const labelMarkup = this.buildPrintLabelMarkup();
      return `<!doctype html>
<html>
<head>
  <title>Label Preview</title>
  <style>
    @page { margin: 0; }
    html, body {
      margin: 0;
      padding: 0;
      background: #ffffff;
      width: max-content;
      height: max-content;
      overflow: hidden;
    }
    body {
      display: block;
    }
    .print-label {
      position: relative;
      width: ${this.previewWidth}px;
      height: ${this.previewHeight}px;
      background: #ffffff;
      overflow: hidden;
      page-break-inside: avoid;
      break-inside: avoid;
      page-break-after: avoid;
    }
    .preview-item {
      position: absolute;
      box-sizing: border-box;
      color: #101828;
      overflow: hidden;
    }
    .preview-text {
      font-weight: 750;
      line-height: 1.2;
      white-space: pre-wrap;
      word-break: break-word;
      font-family: Arial, sans-serif;
    }
    .preview-box {
      border: 2px solid #101828;
    }
    .barcode-wrap img,
    .qr-wrap img {
      width: 100%;
      height: 100%;
      object-fit: contain;
      display: block;
    }
  </style>
</head>
<body>${labelMarkup}</body>
</html>`;
    }

    return '';
  }

  private buildPrintLabelMarkup(): string {
    const items = this.previewElements.map((item) => {
      const style = `left:${item.x}px;top:${item.y}px;width:${item.width}px;height:${item.height}px;font-size:${item.fontSize}px;`;
      if (item.kind === 'box') {
        return `<span class="preview-item preview-box" style="${style}"></span>`;
      }

      if ((item.kind === 'barcode' || item.kind === 'qr') && this.codeImageById[item.id]) {
        const className = item.kind === 'barcode' ? 'barcode-wrap' : 'qr-wrap';
        const alt = item.kind === 'barcode' ? 'Barcode' : 'QR Code';
        return `<span class="preview-item" style="${style}"><span class="${className}"><img src="${this.codeImageById[item.id]}" alt="${alt}"></span></span>`;
      }

      return `<span class="preview-item preview-text" style="${style}">${this.escapeHtml(item.text)}</span>`;
    }).join('');

    return `<div class="print-label">${items}</div>`;
  }

  private escapeHtml(value: string): string {
    return value
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  private async generateCodeImages(): Promise<void> {
    const nextImages: Record<string, string> = {};
    const [barcodeModule, qrModule] = await Promise.all([
      import('jsbarcode'),
      import('qrcode'),
    ]);
    const jsBarcode = (barcodeModule as any).default || barcodeModule;
    const qrCode = (qrModule as any).default || qrModule;

    for (const element of this.previewElements) {
      if (!element.text || (element.kind !== 'barcode' && element.kind !== 'qr')) {
        continue;
      }

      if (element.kind === 'barcode') {
        const barcodeImage = this.createBarcodeSvgDataUrl(element.text, jsBarcode);
        if (barcodeImage) {
          nextImages[element.id] = barcodeImage;
        }
      }

      if (element.kind === 'qr') {
        const qrImage = await this.createQrSvgDataUrl(element.text, qrCode);
        if (qrImage) {
          nextImages[element.id] = qrImage;
        }
      }
    }

    this.codeImageById = nextImages;
  }

  private createBarcodeSvgDataUrl(value: string, jsBarcode: any): string {
    try {
      const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      jsBarcode(svg, value, {
        format: 'CODE128',
        displayValue: false,
        height: 58,
        margin: 0,
        width: 1.8,
      });
      const serialized = new XMLSerializer().serializeToString(svg);
      return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(serialized)}`;
    } catch {
      return '';
    }
  }

  private async createQrSvgDataUrl(value: string, qrCode: any): Promise<string> {
    try {
      const svg = await qrCode.toString(value, {
        type: 'svg',
        errorCorrectionLevel: 'M',
        margin: 1,
        width: 160,
      });
      return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
    } catch {
      return '';
    }
  }

  private parseZplToPreviewElements(zpl: string): PreviewElement[] {
    const rawElements: PreviewElement[] = [];
    const records = zpl.split(/\^FS/gi).filter((record) => record.trim());

    records.forEach((record) => {
      const positionMatch = record.match(/\^(?:FO|FT)(\d+),(\d+)/i);
      const fieldMatch = record.match(/\^FD([\s\S]*)/i);
      const boxMatch = record.match(/\^GB(\d+),(\d+),/i);

      if (!positionMatch) {
        return;
      }

      const x = Number(positionMatch[1]) || 0;
      const y = Number(positionMatch[2]) || 0;

      if (boxMatch) {
        rawElements.push({
          id: `box-${rawElements.length}`,
          kind: 'box',
          text: '',
          x,
          y,
          width: Number(boxMatch[1]) || 40,
          height: Number(boxMatch[2]) || 24,
          fontSize: 12,
        });
        return;
      }

      if (!fieldMatch) {
        return;
      }

      const text = this.cleanZplField(fieldMatch[1]);
      if (!text) {
        return;
      }

      const upperRecord = record.toUpperCase();
      const kind = upperRecord.includes('^BQ') || text.trim().startsWith('<')
        ? 'qr'
        : upperRecord.includes('^BC') || upperRecord.includes('^BY') || this.looksLikeBarcode(text)
          ? 'barcode'
          : 'text';
      const fontSize = this.getPreviewFontSize(record);

      rawElements.push({
        id: `field-${rawElements.length}`,
        kind,
        text,
        x,
        y,
        width: kind === 'qr' ? 160 : kind === 'barcode' ? 360 : Math.max(140, Math.min(520, text.length * fontSize * 0.7)),
        height: kind === 'qr' ? 160 : kind === 'barcode' ? 110 : Math.max(30, fontSize * 1.6),
        fontSize,
      });
    });

    if (!rawElements.length && zpl.trim()) {
      this.extractFdValues(zpl).slice(0, 10).forEach((value, index) => {
        rawElements.push({
          id: `fallback-${index}`,
          kind: this.looksLikeBarcode(value) ? 'barcode' : value.trim().startsWith('<') ? 'qr' : 'text',
          text: value,
          x: 50,
          y: 70 + index * 80,
          width: 420,
          height: this.looksLikeBarcode(value) ? 110 : 40,
          fontSize: 28,
        });
      });
    }

    const normalizedElements = this.normalizePreviewElements(rawElements);

    if (!normalizedElements.length) {
      return this.buildSummaryPreviewElements();
    }

    if (this.hasCrowdedPreview(normalizedElements)) {
      const summaryElements = this.buildSummaryPreviewElements();
      return summaryElements.length ? summaryElements : normalizedElements;
    }

    return normalizedElements;
  }

  private refreshExtractedFields(): void {
    const xmlValues = this.extractXmlValues(this.rawPrnText);

    this.extractedFields = {
      model: xmlValues['MODELNO'] || xmlValues['MODEL'] || this.mesData.description,
      serialNumber: xmlValues['SRNO'] || xmlValues['SERIALNO'] || xmlValues['RSN'] || this.mesData.rsn || this.mesData.serialNumber,
      ean: xmlValues['EAN'] || '',
      macId: xmlValues['MACID'] || '',
      chipId: xmlValues['CHIPID'] || '',
      bisNumber: xmlValues['BIS'] || xmlValues['BISNO'] || xmlValues['RNUMBER'] || '',
      ratedVoltage: '',
    };
  }

  private normalizePreviewElements(elements: PreviewElement[]): PreviewElement[] {
    if (!elements.length) {
      return [];
    }

    const minX = Math.min(...elements.map((element) => element.x));
    const minY = Math.min(...elements.map((element) => element.y));
    const maxX = Math.max(...elements.map((element) => element.x + element.width));
    const maxY = Math.max(...elements.map((element) => element.y + element.height));
    const availableWidth = this.previewWidth - 28;
    const availableHeight = this.previewHeight - 28;
    const scale = Math.min(availableWidth / Math.max(maxX - minX, 1), availableHeight / Math.max(maxY - minY, 1), 0.52);

    return elements.map((element) => ({
      ...element,
      x: Math.max(8, Math.round((element.x - minX) * scale + 14)),
      y: Math.max(8, Math.round((element.y - minY) * scale + 14)),
      width: Math.max(element.kind === 'text' ? 48 : 34, Math.round(element.width * scale)),
      height: Math.max(element.kind === 'text' ? 20 : 34, Math.round(element.height * scale)),
      fontSize: Math.max(9, Math.min(20, Math.round(element.fontSize * scale))),
    }));
  }

  private buildSummaryPreviewElements(): PreviewElement[] {
    const rows = [
      ['MODEL', this.stickerData.model || this.stickerData.productName],
      ['PN', this.stickerData.partNumber],
      ['SN', this.stickerData.serialNumber],
      ['WO', this.stickerData.workOrder],
      ['REV', this.stickerData.revision],
      ['MAC', this.stickerData.macId],
      ['CHIP', this.stickerData.chipId],
      ['EAN', this.stickerData.ean],
      ['MFG DATE', this.stickerData.manufacturingDate],
      ['BIS', this.stickerData.bisNumber],
    ].filter(([, value]) => Boolean(value));
    const elements: PreviewElement[] = [];

    rows.forEach(([label, value], index) => {
      elements.push({
        id: `summary-${index}`,
        kind: 'text',
        text: `${label} : ${value}`,
        x: 24,
        y: 28 + index * 26,
        width: 340,
        height: 20,
        fontSize: 12,
      });
    });

    const barcodeValues = [
      ['BARCODE', this.stickerData.serialNumber || this.stickerData.ean || this.stickerData.chipId],
    ].filter(([, value]) => Boolean(value));

    barcodeValues.forEach(([label, value], index) => {
      const y = 324 + index * 76;
      elements.push({
        id: `summary-barcode-label-${index}`,
        kind: 'text',
        text: label,
        x: 22,
        y,
        width: 70,
        height: 18,
        fontSize: 11,
      });
      elements.push({
        id: `summary-barcode-${index}`,
        kind: 'barcode',
        text: value,
        x: 82,
        y: y - 10,
        width: 276,
        height: 78,
        fontSize: 12,
      });
      elements.push({
        id: `summary-barcode-value-${index}`,
        kind: 'text',
        text: value,
        x: 92,
        y: y + 60,
        width: 252,
        height: 18,
        fontSize: 11,
      });
    });

    const qrValue = this.buildQrPayload();
    if (qrValue) {
      elements.push({
        id: 'summary-qr',
        kind: 'qr',
        text: qrValue,
        x: 254,
        y: 24,
        width: 112,
        height: 112,
        fontSize: 12,
      });
    }

    return elements;
  }

  private hasCrowdedPreview(elements: PreviewElement[]): boolean {
    const meaningfulElements = elements.filter((element) => element.kind !== 'box');
    let collisions = 0;

    for (let firstIndex = 0; firstIndex < meaningfulElements.length; firstIndex += 1) {
      for (let secondIndex = firstIndex + 1; secondIndex < meaningfulElements.length; secondIndex += 1) {
        if (this.getOverlapArea(meaningfulElements[firstIndex], meaningfulElements[secondIndex]) > 60) {
          collisions += 1;
        }
      }
    }

    const barcodeCount = meaningfulElements.filter((element) => element.kind === 'barcode').length;
    return collisions > 2 || barcodeCount > 2 || meaningfulElements.length > 18;
  }

  private getOverlapArea(first: PreviewElement, second: PreviewElement): number {
    const xOverlap = Math.max(0, Math.min(first.x + first.width, second.x + second.width) - Math.max(first.x, second.x));
    const yOverlap = Math.max(0, Math.min(first.y + first.height, second.y + second.height) - Math.max(first.y, second.y));
    return xOverlap * yOverlap;
  }

  private extractFdValues(zpl: string): string[] {
    return Array.from(zpl.matchAll(/\^FD([\s\S]*?)\^FS/gi))
      .map((match) => this.cleanZplField(match[1]))
      .filter(Boolean);
  }

  private findField(source: string, labels: string[]): string {
    for (const label of labels) {
      const pattern = new RegExp(`${label}\\s*[:=-]\\s*([^\\n\\r|^]+)`, 'i');
      const match = source.match(pattern);
      if (match?.[1]) {
        return match[1].trim();
      }
    }

    return '';
  }

  private findAdjacentValue(values: string[], labels: string[]): string {
    const normalizedLabels = labels.map((label) => this.normalizeFieldLabel(label));

    for (let index = 0; index < values.length - 1; index += 1) {
      const normalizedValue = this.normalizeFieldLabel(values[index]);
      const isLabel = normalizedLabels.some((label) => normalizedValue === label || normalizedValue.includes(label));

      if (isLabel) {
        return values[index + 1]?.replace(/^[A-Z /-]+[:=-]\s*/i, '').trim() || '';
      }
    }

    return '';
  }

  private extractXmlText(zpl: string): string {
    return Array.from(zpl.matchAll(/<([A-Za-z0-9_:-]+)>([^<]+)<\/\1>/g))
      .map((match) => `${match[1]}: ${match[2]}`)
      .join('\n');
  }

  private extractXmlValues(zpl: string): Record<string, string> {
    return Array.from(zpl.matchAll(/<([A-Za-z0-9_:-]+)>([^<]+)<\/\1>/g))
      .reduce<Record<string, string>>((values, match) => {
        values[this.normalizeFieldLabel(match[1]).toUpperCase()] = match[2].trim();
        return values;
      }, {});
  }

  private applyTraceResult(response: TraceSearchResponse): void {
    const device = response.device;
    const serial = response.serial;
    this.mesData = {
      ...this.mesData,
      rsn: serial.rsn || this.rsnQuery.trim(),
      serialNumber: serial.sn || '',
      workOrder: device.work_order || '',
      quantity: String(device.work_order_qty ?? ''),
      status: device.work_order_status || serial.status || '',
      site: device.site || '',
      plant: device.plant || '',
      lot: '',
      partNumber: device.pn || '',
      parentPartNumber: device.pn || '',
      revision: device.revision || '',
      description: device.description || '',
      pnType: device.product_line || '',
    };
    this.updateSelectedStationData();
    this.refreshExtractedFields();
    this.updateStickerDataFromSources();
  }

  private updateSelectedStationData(): void {
    const station = this.routingStations.find((step) => step.station_code === this.selectedStationCode);
    this.mesData = {
      ...this.mesData,
      stationCode: station?.station_code || '',
      stationName: station?.station_name || '',
    };
  }

  private applyTemplateValues(template: string): string {
    const availableValues = this.buildPlaceholderValueMap();
    const placeholderNames = this.detectPlaceholders(template);
    this.placeholderResolutions = placeholderNames.map((name) => {
      const value = this.resolvePlaceholderValue(name, availableValues);
      return {
        name,
        token: `{${name}}`,
        value,
        resolved: Boolean(value),
      };
    });
    this.unresolvedPlaceholderNames = this.placeholderResolutions
      .filter((placeholder) => !placeholder.resolved)
      .map((placeholder) => placeholder.name);

    return template.replace(/\{([^{}]+)\}/g, (match, key: string) => {
      const normalizedKey = this.normalizeFieldLabel(key).toUpperCase();
      return this.resolvePlaceholderValue(normalizedKey, availableValues) || match;
    });
  }

  private detectPlaceholders(template: string): string[] {
    return Array.from(template.matchAll(/\{([^{}]+)\}/g))
      .map((match) => this.normalizeFieldLabel(match[1]).toUpperCase())
      .filter((name, index, names) => Boolean(name) && names.indexOf(name) === index);
  }

  private buildPlaceholderValueMap(): Record<string, string> {
    return {
      RSN: this.stickerData.serialNumber,
      SN: this.stickerData.serialNumber,
      SERIAL: this.stickerData.serialNumber,
      WO: this.stickerData.workOrder,
      WORKORDER: this.stickerData.workOrder,
      PN: this.stickerData.partNumber,
      PARTNUMBER: this.stickerData.partNumber,
      REVISION: this.stickerData.revision,
      REV: this.stickerData.revision,
      PRODUCT: this.stickerData.productName,
      PRODUCTNAME: this.stickerData.productName,
      MODEL: this.stickerData.model,
      MODELNO: this.stickerData.model,
      EAN: this.stickerData.ean,
      MACID: this.stickerData.macId,
      MAC: this.stickerData.macId,
      CHIPID: this.stickerData.chipId,
      CHIP: this.stickerData.chipId,
      MFGDATE: this.stickerData.manufacturingDate,
      MANUFACTURINGDATE: this.stickerData.manufacturingDate,
      BIS: this.stickerData.bisNumber,
      BISNO: this.stickerData.bisNumber,
      RNUMBER: this.stickerData.bisNumber,
      LOT: this.mesData.lot,
      STATION: this.mesData.stationCode || this.mesData.stationName,
      STATIONNAME: this.mesData.stationName,
      DESCRIPTION: this.stickerData.productName,
      PRODUCTDESCRIPTION: this.stickerData.productName,
    };
  }

  private resolvePlaceholderValue(name: string, availableValues: Record<string, string>): string {
    const normalizedName = this.normalizeFieldLabel(name).toUpperCase();
    if (availableValues[normalizedName]) {
      return availableValues[normalizedName];
    }

    const xmlValues = this.extractXmlValues(this.rawPrnText);
    if (xmlValues[normalizedName] && !xmlValues[normalizedName].includes('{')) {
      return xmlValues[normalizedName];
    }

    return '';
  }

  private updateStickerDataFromSources(overwriteExisting = true): void {
    const nextData: StickerLabelData = {
      productName: this.mesData.description || this.stickerData.productName,
      model: this.extractedFields.model || this.mesData.description || this.stickerData.model,
      partNumber: this.mesData.partNumber || this.stickerData.partNumber,
      serialNumber: this.extractedFields.serialNumber || this.mesData.rsn || this.mesData.serialNumber || this.stickerData.serialNumber,
      workOrder: this.mesData.workOrder || this.stickerData.workOrder,
      revision: this.mesData.revision || this.stickerData.revision,
      macId: this.extractedFields.macId || this.stickerData.macId,
      chipId: this.extractedFields.chipId || this.stickerData.chipId,
      ean: this.extractedFields.ean || this.stickerData.ean,
      manufacturingDate: this.stickerData.manufacturingDate || this.getTodayDateValue(),
      bisNumber: this.extractedFields.bisNumber || this.stickerData.bisNumber,
    };

    if (overwriteExisting) {
      this.stickerData = nextData;
      return;
    }

    this.stickerData = {
      ...this.stickerData,
      model: this.stickerData.model || nextData.model,
      serialNumber: this.stickerData.serialNumber || nextData.serialNumber,
      ean: this.stickerData.ean || nextData.ean,
      macId: this.stickerData.macId || nextData.macId,
      chipId: this.stickerData.chipId || nextData.chipId,
      bisNumber: this.stickerData.bisNumber || nextData.bisNumber,
    };
  }

  private buildQrPayload(): string {
    return [
      ['MODEL', this.stickerData.model],
      ['PN', this.stickerData.partNumber],
      ['SN', this.stickerData.serialNumber],
      ['WO', this.stickerData.workOrder],
      ['REV', this.stickerData.revision],
      ['MAC', this.stickerData.macId],
      ['CHIP', this.stickerData.chipId],
      ['EAN', this.stickerData.ean],
    ]
      .filter(([, value]) => Boolean(value))
      .map(([key, value]) => `${key}:${value}`)
      .join('|');
  }

  private pickValue(values: string[], index: number): string {
    return values[index]?.replace(/^[A-Z /-]+[:=-]\s*/i, '').trim() || '';
  }

  private pickLikelySerial(values: string[]): string {
    return values.find((value) => /\b[A-Z0-9]{10,24}\b/i.test(value) && !/\b\d{12,14}\b/.test(value)) || '';
  }

  private pickByPattern(values: string[], pattern: RegExp, exclusions: string[] = []): string {
    const value = values.find((candidate) =>
      pattern.test(candidate) && !exclusions.some((term) => candidate.toUpperCase().includes(term))
    );
    return value?.match(pattern)?.[0]?.trim() || value?.trim() || '';
  }

  private cleanZplField(value: string): string {
    return value
      .replace(/\^FH\\?/gi, '')
      .replace(/\\&/g, '\n')
      .replace(/_0D_0A/gi, '\n')
      .replace(/\^[A-Z0-9,]+/gi, '')
      .trim();
  }

  private looksLikeBarcode(value: string): boolean {
    return /^\d{8,}$/.test(value.trim()) || /^[A-Z0-9-]{12,}$/.test(value.trim());
  }

  private getPreviewFontSize(commandBlock: string): number {
    const fontMatch = commandBlock.match(/\^A[A-Z0-9],?(\d{1,3})?/i);
    const rawSize = Number(fontMatch?.[1]) || 34;
    return Math.max(12, Math.min(24, rawSize * 0.42));
  }

  private createEmptyFields(): ExtractedLabelFields {
    return {
      model: '',
      serialNumber: '',
      ean: '',
      macId: '',
      chipId: '',
      ratedVoltage: '',
      bisNumber: '',
    };
  }

  private createEmptyStickerData(): StickerLabelData {
    return {
      productName: '',
      model: '',
      partNumber: '',
      serialNumber: '',
      workOrder: '',
      revision: '',
      macId: '',
      chipId: '',
      ean: '',
      manufacturingDate: '',
      bisNumber: '',
    };
  }

  private getTodayDateValue(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private normalizeLabelCode(value: string): string {
    return value.trim().toUpperCase();
  }

  private normalizeFieldLabel(value: string): string {
    return value.toLowerCase().replace(/[^a-z0-9]/g, '');
  }

  private inferLabelType(code: string): string {
    if (code.startsWith('BOX')) return 'Box';
    if (code.startsWith('CTN')) return 'Carton';
    if (code.startsWith('SHP')) return 'Shipping';
    return 'Product';
  }

  private loadLabelsFromDatabase(): void {
    this.labelService.getLabels().subscribe({
      next: (response) => {
        this.labels = (response.data || []).map((label) => this.mapLabelDto(label));
      },
      error: () => {
        this.labels = [];
        this.setMessage('Unable to load labels history from database.', 'error');
      },
    });
  }

  private mapLabelDto(label: LabelMasterDto): LabelMaster {
    return {
      id: label.id,
      code: label.label_code,
      description: label.label_description || '',
      type: this.labelTypes[0],
      status: 'Active',
      prnText: '',
      fileName: '',
      createdDate: label.created_at,
      fields: this.createEmptyFields(),
    };
  }

  private setMessage(message: string, type: 'success' | 'error' | 'info'): void {
    this.message = message;
    this.messageType = type;
  }

  private createEmptyMesData(): MesLabelData {
    return {
      rsn: '',
      serialNumber: '',
      workOrder: '',
      quantity: '',
      status: '',
      site: '',
      plant: '',
      lot: '',
      partNumber: '',
      parentPartNumber: '',
      revision: '',
      description: '',
      pnType: '',
      stationCode: '',
      stationName: '',
    };
  }
}
