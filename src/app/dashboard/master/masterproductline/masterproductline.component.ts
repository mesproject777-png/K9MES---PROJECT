import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { environment } from '../../../../environments/environment';

interface ProductLine {
  id: number;
  code: string;
  description: string;
  status: string;
  created_at: string;
}

@Component({
  selector: 'app-masterproductline',
  standalone: false,
  templateUrl: './masterproductline.component.html',
  styleUrl: './masterproductline.component.scss'
})
export class MasterproductlineComponent implements OnInit {
  productLineForm: FormGroup;
  editForm: FormGroup;
  productLines: ProductLine[] = [];
  filteredProductLines: ProductLine[] = [];
  isSubmitting = false;
  isEditSubmitting = false;
  isLoading = false;
  errorMessage = '';
  successMessage = '';
  editingId: number | null = null;
  isEditModalOpen = false;
  searchText = '';

  private readonly apiUrl = `${environment.apiUrl}/api/users/product-lines`;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient
  ) {
    this.productLineForm = this.fb.group({
      code: ['', Validators.required],
      description: ['', Validators.required],
      status: ['Active', Validators.required],
    });

    this.editForm = this.fb.group({
      code: ['', Validators.required],
      description: ['', Validators.required],
      status: ['Active', Validators.required],
    });
  }

  ngOnInit(): void {
    this.loadProductLines();
  }

  loadProductLines(): void {
    this.isLoading = true;
    this.http.get<ProductLine[]>(this.apiUrl).subscribe({
      next: (productLines) => {
        this.productLines = productLines;
        this.applySearch();
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Unable to load product lines.';
        this.isLoading = false;
      }
    });
  }

  onSubmit(): void {
    this.errorMessage = '';
    this.successMessage = '';

    if (this.productLineForm.invalid) {
      this.productLineForm.markAllAsTouched();
      return;
    }

    this.isSubmitting = true;
    this.http.post<ProductLine>(this.apiUrl, this.productLineForm.value).subscribe({
      next: () => {
        this.successMessage = 'Product line created successfully.';
        this.isSubmitting = false;
        this.resetCreateForm();
        this.loadProductLines();
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to save product line.';
        this.isSubmitting = false;
      }
    });
  }

  startEdit(item: ProductLine): void {
    this.editingId = item.id;
    this.isEditModalOpen = true;
    this.editForm.patchValue({
      code: item.code,
      description: item.description,
      status: item.status,
    });
  }

  cancelEdit(): void {
    this.isEditModalOpen = false;
    this.editingId = null;
    this.editForm.reset({
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

    this.http.put<ProductLine>(`${this.apiUrl}/${this.editingId}`, this.editForm.value).subscribe({
      next: () => {
        this.successMessage = 'Product line updated successfully.';
        this.isEditSubmitting = false;
        this.cancelEdit();
        this.loadProductLines();
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to save product line.';
        this.isEditSubmitting = false;
      }
    });
  }

  private resetCreateForm(): void {
    this.productLineForm.reset({
      code: '',
      description: '',
      status: 'Active',
    });
  }

  deleteItem(id: number): void {
    this.errorMessage = '';
    this.successMessage = '';

    this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`).subscribe({
      next: () => {
        this.successMessage = 'Product line deleted successfully.';
        if (this.editingId === id) {
          this.cancelEdit();
        }
        this.loadProductLines();
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to delete product line.';
      }
    });
  }

  onSearchChange(value: string): void {
    this.searchText = value.toLowerCase();
    this.applySearch();
  }

  private applySearch(): void {
    if (!this.searchText) {
      this.filteredProductLines = [...this.productLines];
      return;
    }

    this.filteredProductLines = this.productLines.filter((item) =>
      [item.code, item.description, item.status]
        .some((value) => value.toLowerCase().includes(this.searchText))
    );
  }
}
