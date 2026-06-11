import { NgModule } from '@angular/core';

import { SharedModule } from '../shared/shared.module';
import { LabelRoutingModule } from './label-routing.module';
import { LabelComponent } from './label.component';

@NgModule({
  declarations: [
    LabelComponent,
  ],
  imports: [
    SharedModule,
    LabelRoutingModule,
  ],
})
export class LabelModule {}
