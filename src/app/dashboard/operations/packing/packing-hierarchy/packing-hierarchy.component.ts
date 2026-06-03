import { Component } from '@angular/core';
import { PackingHierarchyRow, PackingService } from '../../../../services/packing.service';

@Component({
  selector: 'app-packing-hierarchy',
  standalone: false,
  templateUrl: './packing-hierarchy.component.html',
  styleUrl: './packing-hierarchy.component.scss'
})
export class PackingHierarchyComponent {
  isLoading = false;
  errorMessage = '';
  query = '';
  rows: PackingHierarchyRow[] = [];

  constructor(private packingService: PackingService) {
    this.load();
  }

  load(): void {
    this.isLoading = true;
    this.errorMessage = '';

    this.packingService.listHierarchy(this.query).subscribe({
      next: (response) => {
        this.rows = response.data || [];
        this.isLoading = false;
      },
      error: (error) => {
        this.rows = [];
        this.isLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to load packing hierarchy.';
      }
    });
  }

  clear(): void {
    this.query = '';
    this.load();
  }

  statusClass(status?: string | null): string {
    return String(status || 'none').trim().toLowerCase();
  }
}
