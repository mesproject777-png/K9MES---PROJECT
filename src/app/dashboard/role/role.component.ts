import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { environment } from '../../../environments/environment';

interface Role {
  id: number;
  role_name: string;
  page_access: string[];
}

interface PageOption {
  key: string;
  label: string;
}

@Component({
  selector: 'app-role',
  standalone: false,
  templateUrl: './role.component.html',
  styleUrl: './role.component.scss'
})
export class RoleComponent implements OnInit {
  roleForm: FormGroup;
  roles: Role[] = [];
  isSubmitting = false;
  editingRoleId: number | null = null;
  successMessage = '';
  errorMessage = '';

  readonly pageOptions: PageOption[] = [
    { key: 'dashboard/home', label: 'Dashboard Home' },
    { key: 'dashboard/engineering/menu', label: 'Engineering Menu' },
    { key: 'dashboard/engineering/productline', label: 'Engineering - Product Line' },
    { key: 'dashboard/engineering/pntype', label: 'Engineering - PN Type' },
    { key: 'dashboard/engineering/sntype', label: 'Master - SN Type' },
    { key: 'dashboard/engineering/partnumber', label: 'Engineering - Part Number' },
    { key: 'dashboard/engineering/itemrevisions', label: 'Engineering - Item Revisions' },
    { key: 'dashboard/manager/menu', label: 'Manager Menu' },
    { key: 'dashboard/manager/workorders', label: 'Manager - List WOs' },
    { key: 'dashboard/manager/sgdpo', label: 'Manager - List SGD PO' },
    { key: 'dashboard/manager/pnwochange', label: 'Manager - PN & WO Change' },
    { key: 'dashboard/label', label: 'Labels' },
    { key: 'dashboard/reports', label: 'Reports' },
    { key: 'dashboard/packaging', label: 'Packaging' },
    { key: 'dashboard/users', label: 'Users' },
    { key: 'dashboard/role', label: 'Role Management' },
    { key: 'dashboard/profile', label: 'Profile' },
    { key: 'dashboard/myroute', label: 'All Reports' },
    { key: 'dashboard/master/menu', label: 'Master Menu' },
    { key: 'dashboard/master/masterstation', label: 'Master Station' },
    { key: 'dashboard/master/masterusers', label: 'Master Users' },
    { key: 'dashboard/master/masterproductline', label: 'Master Product Line' },
    { key: 'dashboard/master/sites', label: 'Master Plant' },
    { key: 'dashboard/master/stations', label: 'Master Stations' },
    { key: 'dashboard/master/pntype', label: 'Master PN Type' },
  ];

  private readonly rolesApiUrl = `${environment.apiUrl}/api/users/roles`;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient
  ) {
    this.roleForm = this.fb.group({
      roleName: ['', Validators.required],
      pageAccess: [[], Validators.required]
    });
  }

  ngOnInit(): void {
    this.loadRoles();
  }

  loadRoles(): void {
    this.http.get<Role[]>(this.rolesApiUrl).subscribe({
      next: (roles) => {
        this.roles = roles;
      },
      error: () => {
        this.errorMessage = 'Unable to load roles.';
      }
    });
  }

  togglePageAccess(pageKey: string, checked: boolean): void {
    const selectedPages: string[] = [...this.roleForm.value.pageAccess];
    const updatedPages = checked
      ? [...new Set([...selectedPages, pageKey])]
      : selectedPages.filter((page) => page !== pageKey);

    this.roleForm.patchValue({ pageAccess: updatedPages });
  }

  isPageSelected(pageKey: string): boolean {
    return this.roleForm.value.pageAccess.includes(pageKey);
  }

  editRole(role: Role): void {
    this.editingRoleId = role.id;
    this.successMessage = '';
    this.errorMessage = '';
    this.roleForm.patchValue({
      roleName: role.role_name,
      pageAccess: role.page_access,
    });
  }

  cancelEdit(): void {
    this.editingRoleId = null;
    this.successMessage = '';
    this.errorMessage = '';
    this.roleForm.reset({
      roleName: '',
      pageAccess: [],
    });
  }

  onSubmit(): void {
    this.errorMessage = '';
    this.successMessage = '';

    if (this.roleForm.invalid || this.roleForm.value.pageAccess.length === 0) {
      this.roleForm.markAllAsTouched();
      this.errorMessage = 'Role name and at least one page access are required.';
      return;
    }

    this.isSubmitting = true;
    const requestBody = this.roleForm.value;

    const request$ = this.editingRoleId
      ? this.http.put<Role>(`${this.rolesApiUrl}/${this.editingRoleId}`, requestBody)
      : this.http.post<Role>(this.rolesApiUrl, requestBody);

    request$.subscribe({
      next: () => {
        this.successMessage = this.editingRoleId
          ? 'Role updated successfully.'
          : 'Role created successfully.';
        this.isSubmitting = false;
        this.cancelEdit();
        this.loadRoles();
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to save role.';
        this.isSubmitting = false;
      }
    });
  }

  deleteRole(roleId: number): void {
    this.successMessage = '';
    this.errorMessage = '';

    this.http.delete<{ message: string }>(`${this.rolesApiUrl}/${roleId}`).subscribe({
      next: () => {
        this.successMessage = 'Role deleted successfully.';
        if (this.editingRoleId === roleId) {
          this.cancelEdit();
        }
        this.loadRoles();
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to delete role.';
      }
    });
  }
}
