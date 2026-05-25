import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { FormsModule } from '@angular/forms';

import { MyrouteComponent } from './myroute.component';

describe('MyrouteComponent', () => {
  let component: MyrouteComponent;
  let fixture: ComponentFixture<MyrouteComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [MyrouteComponent],
      imports: [HttpClientTestingModule, FormsModule]
    })
    .compileComponents();

    fixture = TestBed.createComponent(MyrouteComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
