import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MasterroutingComponent } from './masterrouting.component';

describe('MasterroutingComponent', () => {
  let component: MasterroutingComponent;
  let fixture: ComponentFixture<MasterroutingComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [MasterroutingComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(MasterroutingComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
