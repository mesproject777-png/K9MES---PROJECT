import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, Observable, tap } from 'rxjs';

export interface AuthUser {
  id: number;
  login_id: string;
  user_name: string;
  is_active: boolean;
  created_at: string;
  role_id: number;
  role_name: string;
  page_access: string[];
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly storageKey = 'mes_auth_user';
  private readonly apiBaseUrl = 'http://localhost:5000/api/users';
  private readonly currentUserSubject = new BehaviorSubject<AuthUser | null>(this.readStoredUser());

  currentUser$ = this.currentUserSubject.asObservable();

  constructor(
    private http: HttpClient,
    private router: Router
  ) {}

  login(loginId: string, password: string): Observable<AuthUser> {
    return this.http.post<AuthUser>(`${this.apiBaseUrl}/login`, { loginId, password }).pipe(
      tap((user) => this.setCurrentUser(user))
    );
  }

  logout(): void {
    this.clearSession();
    this.router.navigate(['/login']);
  }

  clearSession(): void {
    localStorage.removeItem(this.storageKey);
    this.currentUserSubject.next(null);
  }

  getCurrentUser(): AuthUser | null {
    return this.currentUserSubject.value;
  }

  isAuthenticated(): boolean {
    return !!this.getCurrentUser();
  }

  hasAccess(pageKey: string): boolean {
    const user = this.getCurrentUser();
    if (!user) {
      return false;
    }

    return user.page_access.includes('*') || user.page_access.includes(pageKey);
  }

  getFirstAllowedRoute(): string {
    const user = this.getCurrentUser();
    if (!user || user.page_access.length === 0) {
      return '/login';
    }

    if (user.page_access.includes('*')) {
      return '/dashboard/home';
    }

    return `/${user.page_access[0]}`;
  }

  private setCurrentUser(user: AuthUser): void {
    localStorage.setItem(this.storageKey, JSON.stringify(user));
    this.currentUserSubject.next(user);
  }

  private readStoredUser(): AuthUser | null {
    const storedUser = localStorage.getItem(this.storageKey);
    return storedUser ? JSON.parse(storedUser) as AuthUser : null;
  }
}
