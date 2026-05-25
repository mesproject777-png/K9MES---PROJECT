import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';

interface ItemLookupRow {
  id: number;
  pn: string;
  description: string;
}

interface ItemRevisionRow {
  id: number;
  item_id: number;
  revision: string;
  in_date: string;
  expire_date?: string | null;
  version?: string | null;
  description?: string | null;
}

interface ItemRevisionsResponse {
  item: ItemLookupRow;
  data: ItemRevisionRow[];
  total: number;
}

@Component({
  selector: 'app-itemrevisions',
  standalone: false,
  templateUrl: './itemrevisions.component.html',
  styleUrl: './itemrevisions.component.scss'
})
export class ItemrevisionsComponent implements OnInit {
  readonly apiBase = 'http://localhost:5000/api/item-revisions';

  isLoading = false;
  isSaving = false;
  errorMessage = '';
  successMessage = '';

  pnQuery = '';
  pnSuggestions: ItemLookupRow[] = [];
  private lookupTimer: number | null = null;

  selectedItem: ItemLookupRow | null = null;
  revisions: ItemRevisionRow[] = [];
  includeHistory = false;

  isCreateModalOpen = false;
  revisionForm: FormGroup;

  constructor(
    private http: HttpClient,
    private fb: FormBuilder
  ) {
    const today = new Date().toISOString().slice(0, 10);

    this.revisionForm = this.fb.group({
      revision: ['', Validators.required],
      in_date: [today, Validators.required],
      expire_date: [''],
      version: [''],
      description: [''],
    });
  }

  ngOnInit(): void {}

  onPnInput(value: string): void {
    this.pnQuery = value;
    this.selectedItem = null;
    this.revisions = [];

    if (this.lookupTimer) {
      window.clearTimeout(this.lookupTimer);
    }

    const trimmed = value.trim();
    if (trimmed.length < 2) {
      this.pnSuggestions = [];
      return;
    }

    this.lookupTimer = window.setTimeout(() => {
      const params = new HttpParams().set('query', trimmed).set('limit', '20');
      this.http.get<{ data: ItemLookupRow[] }>(`${this.apiBase}/lookup`, { params }).subscribe({
        next: (response) => {
          this.pnSuggestions = response.data || [];
        },
        error: () => {
          this.pnSuggestions = [];
        }
      });
    }, 250);
  }

  selectPnFromSuggestion(pn: string): void {
    this.pnQuery = pn;
    this.pnSuggestions = [];

    const params = new HttpParams().set('pn', pn);
    this.http.get<ItemLookupRow>(`${this.apiBase}/by-pn`, { params }).subscribe({
      next: (item) => {
        this.selectedItem = item;
        this.loadRevisions();
      },
      error: (error) => {
        this.selectedItem = null;
        this.revisions = [];
        this.errorMessage = error?.error?.message || 'Part number not found';
        this.scheduleClearMessages();
      }
    });
  }

  loadRevisions(): void {
    if (!this.selectedItem) return;

    this.isLoading = true;
    this.errorMessage = '';

    const params = new HttpParams().set('includeHistory', String(this.includeHistory));
    this.http.get<ItemRevisionsResponse>(`${this.apiBase}/${this.selectedItem.id}/revisions`, { params }).subscribe({
      next: (response) => {
        this.revisions = response.data || [];
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Unable to load revisions.';
        this.isLoading = false;
        this.scheduleClearMessages();
      }
    });
  }

  toggleHistory(): void {
    this.includeHistory = !this.includeHistory;
    this.loadRevisions();
  }

  openCreateModal(): void {
    if (!this.selectedItem) {
      this.errorMessage = 'Please select a part number first.';
      this.scheduleClearMessages();
      return;
    }

    const today = new Date().toISOString().slice(0, 10);
    this.revisionForm.reset({
      revision: '',
      in_date: today,
      expire_date: '',
      version: '',
      description: '',
    });
    this.isCreateModalOpen = true;
  }

  closeCreateModal(): void {
    this.isCreateModalOpen = false;
  }

  saveRevision(): void {
    this.successMessage = '';
    this.errorMessage = '';

    if (!this.selectedItem) {
      this.errorMessage = 'Please select a part number first.';
      this.scheduleClearMessages();
      return;
    }

    if (this.revisionForm.invalid) {
      this.revisionForm.markAllAsTouched();
      this.errorMessage = 'Revision and In Date are required.';
      this.scheduleClearMessages();
      return;
    }

    this.isSaving = true;
    const payload = this.revisionForm.value;

    this.http.post<ItemRevisionRow>(`${this.apiBase}/${this.selectedItem.id}/revisions`, payload).subscribe({
      next: () => {
        this.isSaving = false;
        this.isCreateModalOpen = false;
        this.successMessage = 'Revision created successfully.';
        this.scheduleClearMessages();
        this.loadRevisions();
      },
      error: (error) => {
        this.isSaving = false;
        this.errorMessage = error?.error?.message || 'Unable to save revision.';
        this.scheduleClearMessages();
      }
    });
  }

  private clearMessageTimer: number | null = null;

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

