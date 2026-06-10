import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { MyrouteComponent } from './myroute.component';

const routes: Routes = [
  { path: '', component: MyrouteComponent },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class MyrouteRoutingModule {}