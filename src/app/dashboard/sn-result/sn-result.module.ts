import { NgModule } from '@angular/core';

import { SharedModule } from '../shared/shared.module';
import { SnResultRoutingModule } from './sn-result-routing.module';
import { SnResultComponent } from './sn-result.component';

@NgModule({
  declarations: [
    SnResultComponent,
  ],
  imports: [
    SharedModule,
    SnResultRoutingModule,
  ],
})
export class SnResultModule {}