import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OperationsmenuComponent } from './operationsmenu.component';

describe('OperationsmenuComponent', () => {
  let component: OperationsmenuComponent;
  let fixture: ComponentFixture<OperationsmenuComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [OperationsmenuComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(OperationsmenuComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
