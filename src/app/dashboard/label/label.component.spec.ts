import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';

import { AuthUser, AuthService } from '../../services/auth.service';
import { LabelComponent } from './label.component';

const mockUser: AuthUser = {
  id: 1,
  login_id: 'admin',
  user_name: 'Super User',
  is_active: true,
  created_at: '2026-05-26T00:00:00Z',
  role_id: 2,
  role_name: 'Supervisor',
  page_access: ['*']
};

describe('LabelComponent', () => {
  let component: LabelComponent;
  let fixture: ComponentFixture<LabelComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [LabelComponent],
      imports: [HttpClientTestingModule],
      providers: [
        {
          provide: ActivatedRoute,
          useValue: {
            queryParamMap: of(convertToParamMap({}))
          }
        },
        {
          provide: AuthService,
          useValue: {
            getCurrentUser: () => mockUser,
            currentUser$: of(mockUser)
          }
        }
      ]
    })
      .compileComponents();

    fixture = TestBed.createComponent(LabelComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should display current account login details', () => {
    const details = fixture.nativeElement.querySelector('.account-login-details');

    expect(details.textContent).toContain('Account login details');
    expect(details.textContent).toContain('admin');
    expect(details.textContent).toContain('Super User');
    expect(details.textContent).toContain('Supervisor');
  });
});
