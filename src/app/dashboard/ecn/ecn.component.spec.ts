import { ComponentFixture, TestBed } from '@angular/core/testing';

import { EcnComponent } from './ecn.component';

describe('EcnComponent', () => {
  let component: EcnComponent;
  let fixture: ComponentFixture<EcnComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [EcnComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(EcnComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
