import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { LoginComponent } from './login/login.component';
import { DashboardComponent } from './dashboard/dashboard.component';
import { SidebarComponent } from './sidebar/sidebar.component';
import { HomeComponent } from './dashboard/home/home.component';
import { UsersComponent } from './dashboard/users/users.component';
import { RoleComponent } from './dashboard/role/role.component';
import { ProfileComponent } from './dashboard/profile/profile.component';
import { WorkflowComponent } from './dashboard/workflow/workflow.component';
import { WorkOrderComponent } from './dashboard/work-order/work-order.component';
import { StationLoginsComponent } from './dashboard/work-order/station-logins.component';
import { SnResultComponent } from './dashboard/sn-result/sn-result.component';

import { MasterComponent } from './dashboard/master/master.component';
import { MastermenuComponent } from './dashboard/master/mastermenu/mastermenu.component';
import { MasterstationComponent } from './dashboard/master/masterstation/masterstation.component';
import { MasterusersComponent } from './dashboard/master/masterusers/masterusers.component';
import { MasterroutingComponent } from './dashboard/master/masterrouting/masterrouting.component';
import { MasterproductlineComponent } from './dashboard/master/masterproductline/masterproductline.component';
import { MastersitesComponent } from './dashboard/master/mastersites/mastersites.component';
import { EngineeringComponent } from './dashboard/engineering/engineering.component';
import { EngineeringmenuComponent } from './dashboard/engineering/engineeringmenu/engineeringmenu.component';
import { PntypeComponent } from './dashboard/engineering/pntype/pntype.component';
import { SnTypeComponent } from './dashboard/engineering/sntype/sntype.component';
import { PartnumberComponent } from './dashboard/engineering/partnumber/partnumber.component';
import { ItemrevisionsComponent } from './dashboard/engineering/itemrevisions/itemrevisions.component';
import { StationsComponent } from './dashboard/engineering/stations/stations.component';
import { EpvuploadComponent } from './dashboard/engineering/epvupload/epvupload.component';
import { AssemblydefinitionComponent } from './dashboard/engineering/assemblydefinition/assemblydefinition.component';
import { ManagerComponent } from './dashboard/manager/manager.component';
import { ManagermenuComponent } from './dashboard/manager/managermenu/managermenu.component';
import { WorkordersComponent } from './dashboard/manager/workorders/workorders.component';
import { GenerateSnComponent } from './dashboard/manager/generatesn/generatesn.component';
import { PnwochangeComponent } from './dashboard/manager/pnwochange/pnwochange.component';
import { SgdpoComponent } from './dashboard/manager/sgdpo/sgdpo.component';


import { MyrouteComponent } from './dashboard/myroute/myroute.component';
import { BomComponent } from './dashboard/bom/bom.component';
import { EcnComponent } from './dashboard/ecn/ecn.component';
import { LabelComponent } from './dashboard/label/label.component';
import { ReportsComponent } from './dashboard/reports/reports.component';
import { OperationsAssemblyComponent } from './dashboard/operations/assembly/assembly.component';
import { OperationsmenuComponent } from './dashboard/operations/operationsmenu/operationsmenu.component';
import { OpenPackagesComponent } from './dashboard/operations/packing/open-packages/open-packages.component';
import { ClosedPackagesComponent } from './dashboard/operations/packing/closed-packages/closed-packages.component';
import { ShippedPackagesComponent } from './dashboard/operations/packing/shipped-packages/shipped-packages.component';
import { authGuard } from './guards/auth.guard';
import { permissionGuard } from './guards/permission.guard';


const routes: Routes = [
  { path: 'login', component: LoginComponent },
  {
    path: 'dashboard',
    component: DashboardComponent,
    canActivate: [authGuard],
    children: [
      { path: '', redirectTo: 'home', pathMatch: 'full' },
      { path: 'home', component: HomeComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/home' } },
      { path: 'workflow', component: WorkflowComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/home' } },
      { path: 'workorder', component: WorkOrderComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/home' } },
      { path: 'workorder/station-logins', component: StationLoginsComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/home' } },
      { path: 'workorder/SNList', component: GenerateSnComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/generatesn' } },
      { path: 'work-order', redirectTo: 'workorder', pathMatch: 'full' },
      { path: 'work-order/SNList', redirectTo: 'workorder/SNList', pathMatch: 'full' },
      { path: 'sn-result', component: SnResultComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/home' } },
      { path: 'bom', component: BomComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/bom' } },
      { path: 'ecn', component: EcnComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/ecn' } },
      { path: 'label', component: LabelComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/label' } },
      { path: 'reports', component: ReportsComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/reports' } },
      { path: 'packaging', component: OpenPackagesComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/packaging' } },
      { path: 'users', component: UsersComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/users' } },
      { path: 'role', component: RoleComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/role' } },
      { path: 'profile', component: ProfileComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/profile' } },
      { path: 'myroute', component: MyrouteComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/myroute' } },
      { path: 'operations', component: OperationsmenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/operations/assembly' } },
      { path: 'operations/assembly', component: OperationsAssemblyComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/operations/assembly' } },
      { path: 'operations/packing/open', component: OpenPackagesComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/packaging' } },
      { path: 'operations/packing/closed', component: ClosedPackagesComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/packaging' } },
      { path: 'operations/packing/shipped', component: ShippedPackagesComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/packaging' } },
      {
        path: 'master', 
        component: MasterComponent,
        canActivate: [permissionGuard],
        data: { pageKey: 'dashboard/master/menu' },
        children: [
          { path: '', component: MastermenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/menu' } },
          { path: 'menu', component: MastermenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/menu' } },
          { path: 'masterstation', component: MasterstationComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/masterstation' } },
          { path: 'masterusers', component: MasterusersComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/masterusers' } },
          { path: 'masterproductline', component: MasterproductlineComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/masterproductline' } },
          { path: 'plant', component: MastersitesComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/sites' } },
          { path: 'sites', component: MastersitesComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/sites' } },
          { path: 'stations', component: StationsComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/stations' } },
          { path: 'pntype', component: PntypeComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/pntype' } },
          { path: 'sntype', component: SnTypeComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/sntype' } },
          { path: 'rolemanagement', component: RoleComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/role' } },
        ]
      },
      {
        path: 'engineering',
        component: EngineeringComponent,
        canActivate: [permissionGuard],
        data: { pageKey: 'dashboard/engineering/menu' },
        children: [
          { path: '', component: EngineeringmenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/menu' } },
          { path: 'menu', component: EngineeringmenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/menu' } },
          { path: 'productline', component: MasterproductlineComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/productline' } },
          { path: 'pntype', component: PntypeComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/pntype' } },
          { path: 'partnumber', component: PartnumberComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/partnumber' } },
          { path: 'itemrevisions', component: ItemrevisionsComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/itemrevisions' } },
          { path: 'bom', component: BomComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/bom' } },
          { path: 'stations', component: StationsComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/menu' } },
          { path: 'five-step-wizard', redirectTo: '/dashboard/workflow', pathMatch: 'full' },
          { path: 'epv-upload', component: EpvuploadComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/sntype' } },
          { path: 'assembly-definition', component: AssemblydefinitionComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/menu' } },
        ]
      },
      {
        path: 'manager',
        component: ManagerComponent,
        canActivate: [permissionGuard],
        data: { pageKey: 'dashboard/manager/menu' },
        children: [
          { path: '', component: ManagermenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/menu' } },
          { path: 'menu', component: ManagermenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/menu' } },

          { path: 'workorders', component: WorkordersComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/workorders' } },
          { path: 'sgd-po', component: SgdpoComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/sgdpo' } },
          { path: 'generatesn', component: GenerateSnComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/generatesn' } },
          { path: 'pn-wo-change', component: PnwochangeComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/pnwochange' } },
          { path: 'sntracker', component: MyrouteComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/menu' } },
        ]
      },

    ]
  },
  { path: '', redirectTo: 'login', pathMatch: 'full' },
];


@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule { }
