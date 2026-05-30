import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { environment } from '../../../../environments/environment';

interface ProductLine {
  id: number;
  code: string;
  description: string;
  status: string;
}

interface PnType {
  id: number;
  type: string;
  code: string;
  description: string;
  status: string;
}

interface SnType {
  id: number;
  sn_type_name: string;
  remark?: string | null;
}

interface ItemsResponse<T> {
  data: T[];
  total: number;
  page: number;
  limit: number;
}

interface ItemRow {
  id: number;
  pn: string;
  description: string;
  marketing_desc?: string | null;
  phantom: boolean;
  sgd_control?: boolean;
  item_type: 'Manufactured' | 'Purchased';
  product_line_id?: number | null;
  product_line_description?: string | null;
  sn_type_name?: string | null;
  pn_type_id?: number | null;
  pn_type_code?: string | null;
  created_at: string;
}

@Component({
  selector: 'app-partnumber',
  standalone: false,
  templateUrl: './partnumber.component.html',
  styleUrl: './partnumber.component.scss'
})
export class PartnumberComponent implements OnInit {
  readonly itemsApiUrl = `${environment.apiUrl}/api/items`;
  readonly productLinesApiUrl = `${environment.apiUrl}/api/users/product-lines`;
  readonly pnTypesApiUrl = `${environment.apiUrl}/api/users/pn-types`;
  readonly snTypesApiUrl = `${environment.apiUrl}/api/sn-types`;

  isLoading = false;
  isSaving = false;
  errorMessage = '';
  successMessage = '';

  items: ItemRow[] = [];
  totalItems = 0;
  page = 1;
  limit = 15;
  searchText = '';

  productLines: ProductLine[] = [];
  pnTypes: PnType[] = [];

  isCreateModalOpen = false;
  isEditMode = false;
  editingItemId: number | null = null;
  pnForm: FormGroup;
  private clearMessageTimer: number | null = null;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient
  ) {
    this.pnForm = this.fb.group({
      pn: ['', Validators.required],
      description: ['', Validators.required],
      marketing_desc: [''],
      phantom: [null, Validators.required],
      sgd_control: [false],
      item_type: [null, Validators.required],
      product_line_id: [null, Validators.required],
      sn_type_name: [''],
      pn_type_id: [null, Validators.required],
    });
  }

  ngOnInit(): void {
    this.loadLookups();
    this.loadItems();
  }

  loadLookups(): void {
    this.http.get<ProductLine[]>(this.productLinesApiUrl).subscribe({
      next: (lines) => {
        this.productLines = (lines || []).filter((line) => line.status !== 'Inactive');
      },
      error: () => {
        this.errorMessage = 'Unable to load product lines.';
      }
    });

    this.http.get<PnType[]>(this.pnTypesApiUrl).subscribe({
      next: (types) => {
        this.pnTypes = (types || []).filter((type) => type.status !== 'Inactive');
      },
      error: () => {
        this.errorMessage = 'Unable to load PN types.';
      }
    });

    // SN type is entered manually (validated by backend on save).
  }

  loadItems(): void {
    this.isLoading = true;
    this.errorMessage = '';

    let params = new HttpParams()
      .set('page', String(this.page))
      .set('limit', String(this.limit));

    if (this.searchText.trim()) {
      params = params.set('search', this.searchText.trim());
    }

    this.http.get<ItemsResponse<ItemRow>>(this.itemsApiUrl, { params }).subscribe({
      next: (response) => {
        this.items = response.data || [];
        this.totalItems = response.total || 0;
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Unable to load part numbers.';
        this.isLoading = false;
      }
    });
  }

  openCreateModal(): void {
    this.isCreateModalOpen = true;
    this.isEditMode = false;
    this.editingItemId = null;
    this.successMessage = '';
    this.errorMessage = '';
    this.pnForm.reset({
      pn: '',
      description: '',
      marketing_desc: '',
      phantom: null,
      sgd_control: false,
      item_type: null,
      product_line_id: null,
      sn_type_name: '',
      pn_type_id: null,
    });
  }

  closeCreateModal(): void {
    this.isCreateModalOpen = false;
    this.isEditMode = false;
    this.editingItemId = null;
  }

  openEditModal(row: ItemRow): void {
    this.isCreateModalOpen = true;
    this.isEditMode = true;
    this.editingItemId = row.id;
    this.successMessage = '';
    this.errorMessage = '';
    this.pnForm.reset({
      pn: row.pn || '',
      description: row.description || '',
      marketing_desc: row.marketing_desc || '',
      phantom: row.phantom,
      sgd_control: Boolean(row.sgd_control),
      item_type: row.item_type || null,
      product_line_id: row.product_line_id ?? null,
      sn_type_name: row.sn_type_name || '',
      pn_type_id: row.pn_type_id ?? null,
    });
  }

  savePn(): void {
    this.successMessage = '';
    this.errorMessage = '';

    if (this.pnForm.invalid) {
      this.pnForm.markAllAsTouched();
      this.errorMessage = this.buildMissingFieldsMessage();
      this.scheduleClearMessages();
      return;
    }

    this.isSaving = true;
    const wasEditMode = this.isEditMode;

    const request$ = (wasEditMode && this.editingItemId)
      ? this.http.put<ItemRow>(`${this.itemsApiUrl}/${this.editingItemId}`, this.pnForm.value)
      : this.http.post<ItemRow>(this.itemsApiUrl, this.pnForm.value);

    request$.subscribe({
      next: () => {
        this.isSaving = false;
        this.isCreateModalOpen = false;
        this.isEditMode = false;
        this.editingItemId = null;
        this.successMessage = wasEditMode
          ? 'Part number updated successfully.'
          : 'Part number created successfully.';
        this.scheduleClearMessages();
        this.page = 1;
        this.loadItems();
      },
      error: (error) => {
        this.isSaving = false;
        this.errorMessage = error?.error?.message || 'Unable to save part number.';
        this.scheduleClearMessages();
      }
    });
  }

  private buildMissingFieldsMessage(): string {
    const missing: string[] = [];

    if (this.pnForm.get('pn')?.invalid) missing.push('Part number');
    if (this.pnForm.get('description')?.invalid) missing.push('Description');
    if (this.pnForm.get('phantom')?.invalid) missing.push('Phantom');
    if (this.pnForm.get('item_type')?.invalid) missing.push('Item Type');
    if (this.pnForm.get('product_line_id')?.invalid) missing.push('Product Line');
    if (this.pnForm.get('pn_type_id')?.invalid) missing.push('PN Type');

    if (missing.length === 0) {
      return 'Please fill all required fields.';
    }

    return `Please fill required fields: ${missing.join(', ')}`;
  }

  onSearchChange(value: string): void {
    this.searchText = value;
    this.page = 1;
    this.loadItems();
  }

  changePage(nextPage: number): void {
    const totalPages = this.totalPages;
    const clamped = Math.min(Math.max(nextPage, 1), totalPages || 1);
    if (clamped === this.page) {
      return;
    }
    this.page = clamped;
    this.loadItems();
  }

  get totalPages(): number {
    return Math.ceil(this.totalItems / this.limit);
  }

  displayItemType(value: string): string {
    if (value === 'Manufactured') return 'Manufacture';
    if (value === 'Purchased') return 'Purchase';
    return value;
  }

  downloadFullList(): void {
    this.errorMessage = '';
    this.successMessage = '';

    const params = new HttpParams()
      .set('page', '1')
      .set('limit', '500')
      .set('search', this.searchText.trim());

    this.http.get<ItemsResponse<ItemRow>>(this.itemsApiUrl, { params }).subscribe({
      next: (response) => {
        const rows = response.data || [];
        const header = ['pn', 'Description', 'Mark_Desc', 'PH', 'PL', 'Item_Type', 'SN_Type', 'PN_Type'];
        const csvRows = [
          header.join(','),
          ...rows.map((row) => [
            this.escapeCsv(row.pn),
            this.escapeCsv(row.description),
            this.escapeCsv(row.marketing_desc || ''),
            this.escapeCsv(row.phantom ? 'Yes' : 'No'),
            this.escapeCsv(row.product_line_description || ''),
            this.escapeCsv(this.displayItemType(row.item_type)),
            this.escapeCsv(row.sn_type_name || ''),
            this.escapeCsv(row.pn_type_code || ''),
          ].join(',')),
        ];

        const blob = new Blob([csvRows.join('\n')], { type: 'text/csv;charset=utf-8;' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'part_numbers.csv';
        link.click();
        URL.revokeObjectURL(url);
      },
      error: () => {
        this.errorMessage = 'Unable to download list.';
      }
    });
  }

  private escapeCsv(value: string): string {
    const safe = String(value ?? '');
    if (safe.includes(',') || safe.includes('"') || safe.includes('\n')) {
      return `"${safe.replaceAll('"', '""')}"`;
    }
    return safe;
  }

  private scheduleClearMessages(): void {
    if (this.clearMessageTimer) {
      window.clearTimeout(this.clearMessageTimer);
    }

    this.clearMessageTimer = window.setTimeout(() => {
      this.successMessage = '';
      this.errorMessage = '';
      this.clearMessageTimer = null;
    }, 3000);
  }
}
