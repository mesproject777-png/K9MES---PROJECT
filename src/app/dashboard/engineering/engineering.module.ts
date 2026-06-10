import { NgModule } from '@angular/core';

import { MasterEngineeringSharedModule } from '../shared/master-engineering-shared.module';
import { SharedModule } from '../shared/shared.module';
import { AssemblydefinitionComponent } from './assemblydefinition/assemblydefinition.component';
import { EngineeringRoutingModule } from './engineering-routing.module';
import { EngineeringComponent } from './engineering.component';
import { EngineeringmenuComponent } from './engineeringmenu/engineeringmenu.component';
import { EpvuploadComponent } from './epvupload/epvupload.component';
import { FivestepwizardComponent } from './fivestepwizard/fivestepwizard.component';
import { ItemrevisionsComponent } from './itemrevisions/itemrevisions.component';
import { PartnumberComponent } from './partnumber/partnumber.component';

@NgModule({
  declarations: [
    EngineeringComponent,
    EngineeringmenuComponent,
    PartnumberComponent,
    ItemrevisionsComponent,
    EpvuploadComponent,
    FivestepwizardComponent,
    AssemblydefinitionComponent,
  ],
  imports: [
    SharedModule,
    MasterEngineeringSharedModule,
    EngineeringRoutingModule,
  ],
})
export class EngineeringModule {}
