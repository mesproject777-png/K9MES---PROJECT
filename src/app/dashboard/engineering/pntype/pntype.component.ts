import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';

interface PnType {
  id: number;
  type: string;
  code: string;
  description: string;
  status: string;
  created_at: string;
}

@Component({
  selector: 'app-pntype',
  standalone: false,
  templateUrl: './pntype.component.html',
  styleUrl: './pntype.component.scss'
})
export class PntypeComponent implements OnInit {
  pnTypeForm: FormGroup;
  editForm: FormGroup;
  pnTypes: PnType[] = [];
  filteredPnTypes: PnType[] = [];
  isSubmitting = false;
  isEditSubmitting = false;
  isLoading = false;
  errorMessage = '';
  successMessage = '';
  editingId: number | null = null;
  isEditModalOpen = false;
  searchText = '';

  private readonly apiUrl = 'http://localhost:5000/api/users/pn-types';

  constructor(
    private fb: FormBuilder,
    private http: HttpClient
  ) {
    this.pnTypeForm = this.fb.group({
      type: ['', Validators.required],
      code: ['', Validators.required],
      description: ['', Validators.required],
      status: ['Active', Validators.required],
    });

    this.editForm = this.fb.group({
      type: ['', Validators.required],
      code: ['', Validators.required],
      description: ['', Validators.required],
      status: ['Active', Validators.required],
    });
  }

  ngOnInit(): void {
    this.loadPnTypes();
  }

  loadPnTypes(): void {
    this.isLoading = true;
    this.http.get<PnType[]>(this.apiUrl).subscribe({
      next: (pnTypes) => {
        this.pnTypes = pnTypes;
        this.applySearch();
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Unable to load PN types.';
        this.isLoading = false;
      }
    });
  }

  onSubmit(): void {
    this.errorMessage = '';
    this.successMessage = '';

    if (this.pnTypeForm.invalid) {
      this.pnTypeForm.markAllAsTouched();
      return;
    }

    this.isSubmitting = true;
    this.http.post<PnType>(this.apiUrl, this.pnTypeForm.value).subscribe({
      next: () => {
        this.successMessage = 'PN type created successfully.';
        this.isSubmitting = false;
        this.resetCreateForm();
        this.loadPnTypes();
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to save PN type.';
        this.isSubmitting = false;
      }
    });
  }

  startEdit(item: PnType): void {
    this.editingId = item.id;
    this.isEditModalOpen = true;
    this.editForm.patchValue({
      type: item.type,
      code: item.code,
      description: item.description,
      status: item.status,
    });
  }

  cancelEdit(): void {
    this.isEditModalOpen = false;
    this.editingId = null;
    this.editForm.reset({
      type: '',
      code: '',
      description: '',
      status: 'Active',
    });
  }

  saveEdit(): void {
    this.errorMessage = '';
    this.successMessage = '';

    if (!this.editingId) {
      return;
    }

    if (this.editForm.invalid) {
      this.editForm.markAllAsTouched();
      return;
    }

    this.isEditSubmitting = true;

    this.http.put<PnType>(`${this.apiUrl}/${this.editingId}`, this.editForm.value).subscribe({
      next: () => {
        this.successMessage = 'PN type updated successfully.';
        this.isEditSubmitting = false;
        this.cancelEdit();
        this.loadPnTypes();
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to save PN type.';
        this.isEditSubmitting = false;
      }
    });
  }

  deleteItem(id: number): void {
    this.errorMessage = '';
    this.successMessage = '';

    this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`).subscribe({
      next: () => {
        this.successMessage = 'PN type deleted successfully.';
        if (this.editingId === id) {
          this.cancelEdit();
        }
        this.loadPnTypes();
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to delete PN type.';
      }
    });
  }

  onSearchChange(value: string): void {
    this.searchText = value.toLowerCase();
    this.applySearch();
  }

  private applySearch(): void {
    if (!this.searchText) {
      this.filteredPnTypes = [...this.pnTypes];
      return;
    }

    this.filteredPnTypes = this.pnTypes.filter((item) =>
      [item.type, item.code, item.description, item.status]
        .some((value) => value.toLowerCase().includes(this.searchText))
    );
  }

  private resetCreateForm(): void {
    this.pnTypeForm.reset({
      type: '',
      code: '',
      description: '',
      status: 'Active',
    });
  }
}
