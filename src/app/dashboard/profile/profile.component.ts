import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { AbstractControl, FormBuilder, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { AuthService, AuthUser } from '../../services/auth.service';

@Component({
  selector: 'app-profile',
  standalone: false,
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss'
})
export class ProfileComponent implements OnInit {
  currentUser: AuthUser | null = null;
  passwordForm: FormGroup;
  isChangingPassword = false;
  showPasswordForm = false;
  successMessage = '';
  errorMessage = '';

  private readonly apiBaseUrl = 'http://localhost:5000/api/users';

  constructor(
    private authService: AuthService,
    private fb: FormBuilder,
    private http: HttpClient,
    private route: ActivatedRoute
  ) {
    this.currentUser = this.authService.getCurrentUser();
    this.authService.currentUser$.subscribe((user) => {
      this.currentUser = user;
    });

    this.passwordForm = this.fb.group(
      {
        currentPassword: ['', Validators.required],
        newPassword: ['', [Validators.required, Validators.minLength(6)]],
        confirmPassword: ['', Validators.required]
      },
      { validators: this.passwordsMatchValidator }
    );
  }

  ngOnInit(): void {
    this.route.queryParamMap.subscribe((params) => {
      if (params.get('section') === 'settings') {
        this.showPasswordForm = true;
        window.setTimeout(() => {
          document.getElementById('account-settings')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        });
      }
    });
  }

  get displayName(): string {
    return this.currentUser?.user_name || this.currentUser?.login_id || 'User';
  }

  get initial(): string {
    return this.displayName.trim().charAt(0).toUpperCase() || 'U';
  }

  get createdDate(): string {
    return this.currentUser?.created_at ? this.formatDate(this.currentUser.created_at) : 'Not available';
  }

  get permissions(): string[] {
    return this.currentUser?.page_access || [];
  }

  get visiblePermissions(): string[] {
    return this.permissions.slice(0, 14);
  }

  get hiddenPermissionCount(): number {
    return Math.max(this.permissions.length - this.visiblePermissions.length, 0);
  }

  togglePasswordForm(): void {
    this.showPasswordForm = !this.showPasswordForm;
    this.successMessage = '';
    this.errorMessage = '';
    if (!this.showPasswordForm) {
      this.passwordForm.reset();
    }
  }

  changePassword(): void {
    this.successMessage = '';
    this.errorMessage = '';

    if (this.passwordForm.invalid) {
      this.passwordForm.markAllAsTouched();
      return;
    }

    if (!this.currentUser) {
      this.errorMessage = 'User session was not found. Please log in again.';
      return;
    }

    const { currentPassword, newPassword } = this.passwordForm.value;
    this.isChangingPassword = true;

    this.authService.login(this.currentUser.login_id, currentPassword).subscribe({
      next: () => {
        this.http.put(`${this.apiBaseUrl}/${this.currentUser!.id}`, {
          loginId: this.currentUser!.login_id,
          userName: this.currentUser!.user_name,
          password: newPassword,
          roleId: this.currentUser!.role_id,
          isActive: this.currentUser!.is_active,
          updatedBy: this.currentUser!.login_id
        }).subscribe({
          next: () => {
            this.successMessage = 'Password updated successfully.';
            this.passwordForm.reset();
            this.showPasswordForm = false;
            this.isChangingPassword = false;
          },
          error: (error) => {
            this.errorMessage = error?.error?.error || 'Unable to update password.';
            this.isChangingPassword = false;
          }
        });
      },
      error: () => {
        this.errorMessage = 'Current password is incorrect.';
        this.isChangingPassword = false;
      }
    });
  }

  isFieldInvalid(fieldName: string): boolean {
    const field = this.passwordForm.get(fieldName);
    return !!field && field.invalid && (field.dirty || field.touched);
  }

  private passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
    const newPassword = control.get('newPassword')?.value;
    const confirmPassword = control.get('confirmPassword')?.value;
    return newPassword && confirmPassword && newPassword !== confirmPassword ? { passwordMismatch: true } : null;
  }

  private formatDate(value: string): string {
    return new Intl.DateTimeFormat('en-IN', {
      day: 'numeric',
      month: 'short',
      year: 'numeric'
    }).format(new Date(value));
  }
}
