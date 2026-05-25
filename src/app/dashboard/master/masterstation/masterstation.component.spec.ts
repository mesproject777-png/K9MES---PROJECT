import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MasterstationComponent } from './masterstation.component';

describe('MasterstationComponent', () => {
  let component: MasterstationComponent;
  let fixture: ComponentFixture<MasterstationComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [MasterstationComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(MasterstationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
