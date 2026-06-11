import { NgModule } from '@angular/core';

import { SharedModule } from '../shared/shared.module';
import { WorkflowRoutingModule } from './workflow-routing.module';
import { WorkflowComponent } from './workflow.component';

@NgModule({
  declarations: [
    WorkflowComponent,
  ],
  imports: [
    SharedModule,
    WorkflowRoutingModule,
  ],
})
export class WorkflowModule {}
