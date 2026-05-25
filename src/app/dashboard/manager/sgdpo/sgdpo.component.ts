import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';

type SgdPoStatus = 'open' | 'cancel' | 'complete';

interface SgdPoRow {
  id: number;
  po: string;
  status: SgdPoStatus;
  sw_version?: string | null;
  hw_version?: string | null;
  po_qty: number;
  item: string;
  wo_qty: number;
  created_at?: string;
  updated_at?: string;
}

interface SgdPoListResponse {
  data: SgdPoRow[];
}

@Component({
  selector: 'app-sgdpo',
  standalone: false,
  templateUrl: './sgdpo.component.html',
  styleUrl: './sgdpo.component.scss'
})
export class SgdpoComponent implements OnInit {
  readonly apiBase = 'http://localhost:5000/api/sgd-pos';

  isLoading = false;
  isSaving = false;
  successMessage = '';
  errorMessage = '';

  searchText = '';
  rows: SgdPoRow[] = [];

  isModalOpen = false;
  isEditMode = false;
  editingId: number | null = null;
  editingPo: string | null = null;
  editingItemPn: string | null = null;
  editingWoQty: number | null = null;

  poForm: FormGroup;

  private clearMessageTimer: number | null = null;

  readonly statusOptions: { value: SgdPoStatus; label: string }[] = [
    { value: 'open', label: 'Open' },
    { value: 'cancel', label: 'Cancel' },
    { value: 'complete', label: 'Complete' }
  ];

  constructor(
    private http: HttpClient,
    private fb: FormBuilder
  ) {
    this.poForm = this.fb.group({
      po: ['', Validators.required],
      pn: ['', Validators.required],
      status: ['open', Validators.required],
      sw_version: [''],
      hw_version: [''],
      po_qty: [null, [Validators.required, Validators.min(1)]]
    });
  }

  ngOnInit(): void {
    this.loadSgdPos();
  }

  loadSgdPos(): void {
    this.isLoading = true;
    this.errorMessage = '';

    let params = new HttpParams();
    if (this.searchText.trim()) {
      params = params.set('search', this.searchText.trim());
    }

    this.http.get<SgdPoListResponse>(this.apiBase, { params }).subscribe({
      next: (response) => {
        this.rows = response.data || [];
        this.isLoading = false;
      },
      error: (error) => {
        this.errorMessage = error?.error?.message || 'Unable to load SGD POs.';
        this.isLoading = false;
        this.scheduleClearMessages();
      }
    });
  }

  onSearchChange(value: string): void {
    this.searchText = value;
    this.loadSgdPos();
  }

  openCreateModal(): void {
    this.successMessage = '';
    this.errorMessage = '';

    this.isModalOpen = true;
    this.isEditMode = false;
    this.editingId = null;
    this.editingPo = null;
    this.editingItemPn = null;
    this.editingWoQty = null;

    this.poForm.reset({
      po: '',
      pn: '',
      status: 'open',
      sw_version: '',
      hw_version: '',
      po_qty: null
    });

    this.poForm.get('po')?.enable({ emitEvent: false });
    this.poForm.get('pn')?.enable({ emitEvent: false });
  }

  openEditModal(row: SgdPoRow): void {
    this.successMessage = '';
    this.errorMessage = '';

    this.isModalOpen = true;
    this.isEditMode = true;
    this.editingId = row.id;
    this.editingPo = row.po;
    this.editingItemPn = row.item;
    this.editingWoQty = row.wo_qty;

    this.poForm.reset({
      po: row.po,
      pn: row.item,
      status: row.status,
      sw_version: row.sw_version || '',
      hw_version: row.hw_version || '',
      po_qty: row.po_qty
    });

    // Backend does not allow changing PO or Item.
    this.poForm.get('po')?.disable({ emitEvent: false });
    this.poForm.get('pn')?.disable({ emitEvent: false });
  }

  closeModal(): void {
    this.isModalOpen = false;
    this.isEditMode = false;
    this.editingId = null;
    this.editingPo = null;
    this.editingItemPn = null;
    this.editingWoQty = null;
  }

  save(): void {
    this.successMessage = '';
    this.errorMessage = '';

    if (this.poForm.invalid) {
      this.poForm.markAllAsTouched();
      this.errorMessage = 'Please fill all required fields.';
      this.scheduleClearMessages();
      return;
    }

    this.isSaving = true;

    const raw = this.poForm.getRawValue();

    const payload: any = {
      status: String(raw.status || 'open').toLowerCase(),
      sw_version: raw.sw_version || null,
      hw_version: raw.hw_version || null,
      po_qty: raw.po_qty
    };

    const request$ = (this.isEditMode && this.editingId)
      ? this.http.put(`${this.apiBase}/${this.editingId}`, payload)
      : this.http.post(this.apiBase, {
          po: raw.po,
          pn: raw.pn,
          ...payload
        });

    request$.subscribe({
      next: () => {
        this.isSaving = false;
        this.isModalOpen = false;
        this.successMessage = this.isEditMode
          ? 'SGD PO updated successfully.'
          : 'SGD PO created successfully.';
        this.scheduleClearMessages();
        this.loadSgdPos();
      },
      error: (error) => {
        this.isSaving = false;
        this.errorMessage = error?.error?.message || 'Unable to save SGD PO.';
        this.scheduleClearMessages();
      }
    });
  }

  private scheduleClearMessages(): void {
    if (this.clearMessageTimer) {
      window.clearTimeout(this.clearMessageTimer);
    }

    this.clearMessageTimer = window.setTimeout(() => {
      this.successMessage = '';
      this.errorMessage = '';
      this.clearMessageTimer = null;
    }, 5000);
  }
}
