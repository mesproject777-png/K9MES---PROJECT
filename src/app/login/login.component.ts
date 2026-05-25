import { Component } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-login',
  standalone: false,
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginComponent {
  loginForm: FormGroup;
  errorMessage = '';
  validationMessage = '';
  isSubmitting = false;

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private router: Router
  ) {
    this.loginForm = this.fb.group({
      loginId: ['', Validators.required],
      password: ['', Validators.required],
    });
    this.authService.clearSession();
  }

  onSubmit(): void {
    this.errorMessage = '';
    this.validationMessage = '';

    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      this.validationMessage = 'Enter username and password.';
      return;
    }

    const { loginId, password } = this.loginForm.value;

    this.isSubmitting = true;

    this.authService.login(loginId, password).subscribe({
      next: () => {
        this.isSubmitting = false;
        this.router.navigateByUrl(this.authService.getFirstAllowedRoute());
      },
      error: (error) => {
        this.errorMessage = error?.error?.error || 'Unable to log in.';
        this.isSubmitting = false;
      }
    });
  }
}
