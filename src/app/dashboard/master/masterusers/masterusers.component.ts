import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { AuthService } from '../../../services/auth.service';

interface Role {
  id: number;
  role_name: string;
  page_access: string[];
}

interface User {
  id: number;
  login_id: string;
  user_name: string;
  is_active: boolean;
  role_id: number;
  role_name: string;
  page_access: string[];
}

interface UserHistoryEntry {
  id: number;
  user_id: number;
  field_name: string;
  old_value: string | null;
  new_value: string | null;
  changed_by: string;
  changed_at: string;
}

@Component({
  selector: 'app-master-users',
  standalone: false,
  templateUrl: './masterusers.component.html',
  styleUrls: ['./masterusers.component.scss']
})
export class MasterusersComponent implements OnInit {
  userForm: FormGroup;
  editForm: FormGroup;
  roles: Role[] = [];
  users: User[] = [];
  filteredUsers: User[] = [];
  historyEntries: UserHistoryEntry[] = [];
  isSubmitting = false;
  isEditSubmitting = false;
  isLoading = false;
  isHistoryLoading = false;
  errorMessage = '';
  successMessage = '';
  searchText = '';
  editingUserId: number | null = null;
  selectedHistoryUserId: number | null = null;
  isEditModalOpen = false;

  private readonly apiBaseUrl = 'http://localhost:5000/api/users';

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private authService: AuthService
  ) {
    this.userForm = this.fb.group({
      loginId: ['', Validators.required],
      userName: ['', Validators.required],
      password: [''],
      isActive: [true, Validators.required],
      roleId: ['', Validators.required]
    });

    this.editForm = this.fb.group({
      loginId: ['', Validators.required],
      userName: ['', Validators.required],
      password: [''],
      isActive: [true, Validators.required],
      roleId: ['', Validators.required]
    });
  }

  ngOnInit(): void {
    this.loadRoles();
    this.loadUsers();
  }

  loadRoles(): void {
    this.http.get<Role[]>(`${this.apiBaseUrl}/roles`).subscribe({
      next: (roles) => {
        this.roles = roles;
      },
      error: () => {
        this.errorMessage = 'Unable to load roles from backend.';
      }
    });
  }

  loadUsers(): void {
    this.isLoading = true;
    this.http.get<User[]>(this.apiBaseUrl).subscribe({
      next: (users) => {
        this.users = users;
        this.applySearch();
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Unable to load users from backend.';
        this.isLoading = false;
      }
    });
  }

  onSubmit(): void {
    this.errorMessage = '';
    this.successMessage = '';

    if (this.userForm.invalid) {
      this.userForm.markAllAsTouched();
      return;
    }

    if (!this.userForm.value.password) {
      this.errorMessage = 'Password is required when creating a new user.';
      return;
    }

    this.isSubmitting = true;
    const currentUser = this.authService.getCurrentUser();
    const payload = {
      ...this.userForm.value,
      updatedBy: currentUser?.login_id || currentUser?.user_name || 'system',
    };

    this.http.post<User>(this.apiBaseUrl, payload).subscribe({
      next: () => {
        this.successMessage = 'User created successfully.';
        this.resetCreateForm();
        this.isSubmitting = false;
        this.loadUsers();
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to create user.';
        this.isSubmitting = false;
      }
    });
  }

  onSearchChange(value: string): void {
    this.searchText = value.toLowerCase();
    this.applySearch();
  }

  startEdit(user: User): void {
    this.editingUserId = user.id;
    this.isEditModalOpen = true;
    this.errorMessage = '';
    this.successMessage = '';
    this.editForm.patchValue({
      loginId: user.login_id,
      userName: user.user_name,
      password: '',
      isActive: user.is_active,
      roleId: String(user.role_id),
    });
  }

  cancelEdit(): void {
    this.resetEditForm();
  }

  saveEdit(): void {
    this.errorMessage = '';
    this.successMessage = '';

    if (!this.editingUserId) {
      return;
    }

    if (this.editForm.invalid) {
      this.editForm.markAllAsTouched();
      return;
    }

    this.isEditSubmitting = true;
    const currentUser = this.authService.getCurrentUser();
    const payload = {
      ...this.editForm.value,
      updatedBy: currentUser?.login_id || currentUser?.user_name || 'system',
    };

    this.http.put<User>(`${this.apiBaseUrl}/${this.editingUserId}`, payload).subscribe({
      next: () => {
        this.successMessage = 'User updated successfully.';
        this.isEditSubmitting = false;
        this.resetEditForm();
        this.loadUsers();
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to update user.';
        this.isEditSubmitting = false;
      }
    });
  }

  showHistory(user: User): void {
    if (this.selectedHistoryUserId === user.id) {
      this.closeHistory();
      return;
    }

    this.selectedHistoryUserId = user.id;
    this.isHistoryLoading = true;
    this.historyEntries = [];

    this.http.get<UserHistoryEntry[]>(`${this.apiBaseUrl}/${user.id}/history`).subscribe({
      next: (entries) => {
        this.historyEntries = entries;
        this.isHistoryLoading = false;
      },
      error: () => {
        this.errorMessage = 'Unable to load user history.';
        this.isHistoryLoading = false;
      }
    });
  }

  closeHistory(): void {
    this.selectedHistoryUserId = null;
    this.historyEntries = [];
  }

  getPageAccessPreview(): string {
    const selectedRoleId = Number(this.userForm.value.roleId);
    const selectedRole = this.roles.find((role) => role.id === selectedRoleId);
    return selectedRole ? selectedRole.page_access.join(', ') : '';
  }

  getEditPageAccessPreview(): string {
    const selectedRoleId = Number(this.editForm.value.roleId);
    const selectedRole = this.roles.find((role) => role.id === selectedRoleId);
    return selectedRole ? selectedRole.page_access.join(', ') : '';
  }

  private resetCreateForm(): void {
    this.userForm.reset({
      loginId: '',
      userName: '',
      password: '',
      isActive: true,
      roleId: ''
    });
  }

  private resetEditForm(): void {
    this.editingUserId = null;
    this.isEditModalOpen = false;
    this.editForm.reset({
      loginId: '',
      userName: '',
      password: '',
      isActive: true,
      roleId: ''
    });
  }

  private applySearch(): void {
    if (!this.searchText) {
      this.filteredUsers = [...this.users];
      return;
    }

    this.filteredUsers = this.users.filter((user) => {
      const status = user.is_active ? 'active' : 'inactive';
      return [
        user.login_id,
        user.user_name,
        user.role_name,
        status,
      ]
        .filter(Boolean)
        .some((value) => value.toLowerCase().includes(this.searchText));
    });
  }
}
