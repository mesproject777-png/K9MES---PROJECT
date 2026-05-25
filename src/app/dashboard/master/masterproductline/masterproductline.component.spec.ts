import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MasterproductlineComponent } from './masterproductline.component';

describe('MasterproductlineComponent', () => {
  let component: MasterproductlineComponent;
  let fixture: ComponentFixture<MasterproductlineComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [MasterproductlineComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(MasterproductlineComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
