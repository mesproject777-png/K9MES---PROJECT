import { NgModule } from '@angular/core';

import { MasterEngineeringSharedModule } from '../shared/master-engineering-shared.module';
import { SharedModule } from '../shared/shared.module';
import { MasterRoutingModule } from './master-routing.module';
import { MasterComponent } from './master.component';
import { MastermenuComponent } from './mastermenu/mastermenu.component';
import { MasterroutingComponent } from './masterrouting/masterrouting.component';
import { MastersitesComponent } from './mastersites/mastersites.component';
import { MasterstationComponent } from './masterstation/masterstation.component';
import { MasterusersComponent } from './masterusers/masterusers.component';

@NgModule({
  declarations: [
    MasterComponent,
    MastermenuComponent,
    MasterroutingComponent,
    MastersitesComponent,
    MasterstationComponent,
    MasterusersComponent,
  ],
  imports: [
    SharedModule,
    MasterEngineeringSharedModule,
    MasterRoutingModule,
  ],
})
export class MasterModule {}
