import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';

interface Site {
  id: number;
  name: string;
  created_at: string;
}

@Component({
  selector: 'app-mastersites',
  standalone: false,
  templateUrl: './mastersites.component.html',
  styleUrl: './mastersites.component.scss'
})
export class MastersitesComponent implements OnInit {
  siteForm: FormGroup;
  editForm: FormGroup;
  sites: Site[] = [];
  filteredSites: Site[] = [];
  isSubmitting = false;
  isEditSubmitting = false;
  isLoading = false;
  errorMessage = '';
  successMessage = '';
  editingId: number | null = null;
  isEditModalOpen = false;
  searchText = '';

  private readonly apiUrl = 'http://localhost:5000/api/sites';

  constructor(
    private fb: FormBuilder,
    private http: HttpClient
  ) {
    this.siteForm = this.fb.group({
      name: ['', Validators.required],
    });

    this.editForm = this.fb.group({
      name: ['', Validators.required],
    });
  }

  ngOnInit(): void {
    this.loadSites();
  }

  loadSites(): void {
    this.isLoading = true;
    this.http.get<Site[]>(this.apiUrl).subscribe({
      next: (sites) => {
        this.sites = sites || [];
        this.applySearch();
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Unable to load sites.';
        this.isLoading = false;
      }
    });
  }

  onSubmit(): void {
    this.errorMessage = '';
    this.successMessage = '';

    if (this.siteForm.invalid) {
      this.siteForm.markAllAsTouched();
      return;
    }

    this.isSubmitting = true;
    this.http.post<Site>(this.apiUrl, this.siteForm.value).subscribe({
      next: () => {
        this.successMessage = 'Site created successfully.';
        this.isSubmitting = false;
        this.resetCreateForm();
        this.loadSites();
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to save site.';
        this.isSubmitting = false;
      }
    });
  }

  startEdit(item: Site): void {
    this.editingId = item.id;
    this.isEditModalOpen = true;
    this.editForm.patchValue({ name: item.name });
  }

  cancelEdit(): void {
    this.isEditModalOpen = false;
    this.editingId = null;
    this.editForm.reset({ name: '' });
  }

  saveEdit(): void {
    this.errorMessage = '';
    this.successMessage = '';

    if (!this.editingId) return;
    if (this.editForm.invalid) {
      this.editForm.markAllAsTouched();
      return;
    }

    this.isEditSubmitting = true;
    this.http.put<Site>(`${this.apiUrl}/${this.editingId}`, this.editForm.value).subscribe({
      next: () => {
        this.successMessage = 'Site updated successfully.';
        this.isEditSubmitting = false;
        this.cancelEdit();
        this.loadSites();
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to save site.';
        this.isEditSubmitting = false;
      }
    });
  }

  deleteItem(id: number): void {
    this.errorMessage = '';
    this.successMessage = '';

    this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`).subscribe({
      next: () => {
        this.successMessage = 'Site deleted successfully.';
        if (this.editingId === id) {
          this.cancelEdit();
        }
        this.loadSites();
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to delete site.';
      }
    });
  }

  onSearchChange(value: string): void {
    this.searchText = value.toLowerCase();
    this.applySearch();
  }

  private applySearch(): void {
    if (!this.searchText) {
      this.filteredSites = [...this.sites];
      return;
    }

    this.filteredSites = this.sites.filter((item) =>
      item.name.toLowerCase().includes(this.searchText)
    );
  }

  private resetCreateForm(): void {
    this.siteForm.reset({ name: '' });
  }
}

