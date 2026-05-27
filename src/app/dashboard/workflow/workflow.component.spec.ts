import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FivestepwizardComponent } from '../engineering/fivestepwizard/fivestepwizard.component';

import { WorkflowComponent } from './workflow.component';

describe('WorkflowComponent', () => {
  let component: WorkflowComponent;
  let fixture: ComponentFixture<WorkflowComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [WorkflowComponent, FivestepwizardComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(WorkflowComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
