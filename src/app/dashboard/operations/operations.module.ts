import { NgModule } from '@angular/core';

import { OperationsPagesModule } from './operations-pages.module';
import { OperationsRoutingModule } from './operations-routing.module';

@NgModule({
  imports: [
    OperationsPagesModule,
    OperationsRoutingModule,
  ],
})
export class OperationsModule {}
